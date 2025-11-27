using CryptoBlade.Models;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public interface IMode
    {
        ModeKind Kind { get; }

        ModeSignal GenerateSignal(SigmaData data, bool enableTestSignal);
        decimal? ComputeEntryPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side);
        decimal? ComputeStopLossPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side, decimal entryPrice);
        (decimal? Tp1, decimal? Tp2) ComputeTakeProfits(SigmaData data, SymbolInfo symbolInfo, OrderSide side, decimal entryPrice, decimal risk);   
    }

    public enum ModeKind { 
        None = 0, 
        MM = 1, 
        MR = 2, 
        BO = 3
    }

    public enum ModeTier {
        None = 0,
        Soft = 1,
        Medium = 2,
        Hard = 3
    }

    public readonly record struct ModeScores(double Momentum, double MeanReversion, double Breakout);

    public readonly struct ModeSignal(bool buy, bool sell, ModeTier tier)
    {
        public readonly bool HasBuy = buy;
        public readonly bool HasSell = sell;
        public readonly ModeTier Tier = tier;

        public static ModeSignal None => new(false, false, ModeTier.None);
    }

    public sealed class ModeEngine(SigmaStrategyOptions options)
    {
        private readonly SigmaStrategyOptions _options = options ?? throw new ArgumentNullException(nameof(options));
        private DateTime _lastModeChangeUtc = DateTime.MinValue;
        private (ModeKind Kind, double Score) _currentScore = (ModeKind.None, 0);
        private double _spreadEmaBps;
        private bool _spreadEmaInitialized;

        public (IMode? mode, ModeScores scores) SelectModeAndScores(DateTime nowUtc, SigmaData data)
        {
            var modes = new Dictionary<ModeKind, double>
            {
                { ModeKind.MM, MomentumMode.Score(data, _options) },
                { ModeKind.MR, MeanReversionMode.Score(data, _options) },
                { ModeKind.BO, BreakoutMode.Score(data, _options) }
            };

            var sortedMScores = modes.OrderByDescending(kv => kv.Value);
            var bestScore = sortedMScores.First();
            double margin = bestScore.Value - sortedMScores.Skip(1).First().Value;

            if (bestScore.Value <= 0.0)
                return (null, new(0, 0, 0));

            bool passMinScore = bestScore.Value >= _options.MinScore;
            bool passMinMargin = margin >=  _options.MinMargin;

            if (passMinScore && passMinMargin)
            {
                if (_currentScore.Kind != bestScore.Key)
                {
                    var dwell = TimeSpan.FromMinutes(_options.HysteresisLockMinutes);
                    bool dwellOver = (nowUtc - _lastModeChangeUtc) >= dwell;
                    if (dwellOver)
                    {
                        _currentScore = (bestScore.Key, bestScore.Value);
                        _lastModeChangeUtc = nowUtc;
                    }
                }
            }

            IMode? mode = (_currentScore.Kind switch
            {
                ModeKind.MM => new MomentumMode(_options),
                ModeKind.MR => new MeanReversionMode(_options),
                ModeKind.BO => new BreakoutMode(_options),
                _ => null
            });

            return (mode, new ModeScores(
                modes[ModeKind.MM],
                modes[ModeKind.MR],
                modes[ModeKind.BO]));
        }

        public (bool gate, string gateReason) CheckGlobalGates(SigmaData data, DateTime nowUtc)
        {
            if (!double.IsFinite(data.SpreadBps) || data.SpreadBps <= 0)
                return (false, $"Global gate: invalid spread {data.SpreadBps:F2} bps");

            double current = data.SpreadBps;
            double minBps = (double)_options.SpreadGateMinBps;
            double absMax = (double)_options.MaxSpreadBps;
            double alpha = _options.SpreadGateEmaAlpha;
            double multiple = _options.SpreadGateMultiplier;

            if (!_spreadEmaInitialized)
            {
                double seed = Math.Max(minBps, Math.Min(current, absMax));
                _spreadEmaBps = seed;
                _spreadEmaInitialized = true;
            }
            else
            {
                double x = Math.Max(minBps, Math.Min(current, absMax));
                double oneMinusAlpha = 1.0 - alpha;
                _spreadEmaBps = alpha * x + oneMinusAlpha * _spreadEmaBps;
            }

            double baseline = Math.Max(minBps, _spreadEmaBps);
            double dynamicGate = baseline * multiple;
            double threshold = Math.Min(dynamicGate, absMax);

            data.SpreadGateThresholdBps = threshold;

            if (current > threshold + 1e-6)
                return (false,
                    $"Global gate: Spread {current:F2} bps > dynamic gate {threshold:F2} bps (ema={_spreadEmaBps:F2})");

            if (IsMacroFreeze(nowUtc, _options))
                return (false, "Global gate: Macro freeze window");

            if (IsFundingFreeze(data, nowUtc, _options))
                return (false, "Global gate: Funding window");

            if (IsCorrOppositeBlocked(data, _options))
                return (false, $"Global gate: Corr {data.CorrToBtc15m:F3} with opposite BTC bias");

            return (true, "OK");
        }

        private static bool IsMacroFreeze(DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (o?.MacroEventsUtc == null || o.MacroEventsUtc.Count == 0)
                return false;

            var before = TimeSpan.FromMinutes(o.MacroFreezeMinutesBefore);
            var after = TimeSpan.FromMinutes(o.MacroFreezeMinutesAfter);

            foreach (var dt in o.MacroEventsUtc)
            {
                var t = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                if (nowUtc >= t - before && nowUtc <= t + after)
                    return true;
            }

            return false;
        }

        private static bool IsFundingFreeze(SigmaData d, DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (!d.NextFundingUtc.HasValue)
                return false;

            var dt = DateTime.SpecifyKind(d.NextFundingUtc.Value, DateTimeKind.Utc);

            var from = dt.AddMinutes(-o.FundingFreezeMinutesBefore);
            var to = dt.AddMinutes(o.FundingFreezeMinutesAfter);

            return nowUtc >= from && nowUtc <= to;
        }

        private static bool IsCorrOppositeBlocked(SigmaData d, SigmaStrategyOptions o)
        {
            if (!double.IsFinite(d.CorrToBtc15m))
                return false;

            return Math.Abs(d.CorrToBtc15m) >= (double)o.CorrOppositeBlock
                && d.BtcBiasOpposite;
        }
    }
}
