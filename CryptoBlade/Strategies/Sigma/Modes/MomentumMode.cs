using System;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// MomentumMode v2 – trigger-only:
    /// - wejścia tylko przy zgrywie:
    ///   * reżim Momentum już wybrany przez ModeEngine (trend / ATR / spread / gates),
    ///   * lokalny sweep -> reclaim na 5m (polowanie na płynność),
    ///   * flip CVD 5m w stronę zgodną z trendem,
    ///   * pullback do DVWAP (price < DVWAP w uptrendzie, > DVWAP w downtrendzie),
    ///   * ΔOI oraz autokorelacja wspierające kontynuację.
    /// </summary>
    public sealed class MomentumMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MomentumMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Mode Kind => Mode.MM;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // Bezpieczeństwo: jeśli spread z jakiegoś powodu jest już > progu, nie handlujemy,
            // nawet jeśli ModeEngine dopuścił reżim.
            if (double.IsFinite(data.SpreadBps) &&
                data.SpreadBps > (double)_options.MaxSpreadBps)
            {
                return ModeSignal.None;
            }

            // ATR gate – dodatkowa kontrola dla Momentum
            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h < (double)_options.MmAtrMinPct ||
                data.AtrPct1h > (double)_options.MmAtrMaxPct)
            {
                return ModeSignal.None;
            }

            // --- Trend i value ---

            double adx = data.Adx1h;
            double zSlope = data.ZSlopeDvwap;
            double zDev = data.ZDvwap;
            double ac = data.AutoCorr5m;
            double oi = data.OiDelta1hPct;

            // Kierunek trendu wg nachylenia DVWAP (z lekkim deadbandem)
            bool upTrend = zSlope > 0.5;
            bool downTrend = zSlope < -0.5;

            // Pullback do value – „kupujemy poniżej DVWAP” w uptrendzie
            // i „sprzedajemy powyżej DVWAP” w downtrendzie (ale bez ekstremów).
            bool pullbackLong = zDev < -0.5 && zDev > -3.5;
            bool pullbackShort = zDev > 0.5 && zDev < 3.5;

            // Autokorelacja dodatnia – preferujemy kontynuację, nie mean reversion.
            bool acSupportsTrend = ac > 0.05;

            // ΔOI w stronę trendu – nie wymagamy bardzo dużej zmiany, ale niech będzie > 0.
            bool oiSupportsUp = oi > 0.2;
            bool oiSupportsDown = oi < -0.2;

            // ADX – docelowo ModeEngine już wymusił sensowny poziom, ale dajmy miękki próg.
            bool strongAdx = adx >= (double)_options.AdxEnableMomentum;

            // --- Pattern: sweep -> reclaim ---

            bool hasSweepLong =
                data.SweepReclaimDown5m &&
                data.SweepDownOvershootBps5m >= 1.0;    // realne wybicie low

            bool hasSweepShort =
                data.SweepReclaimUp5m &&
                data.SweepUpOvershootBps5m >= 1.0;      // realne wybicie high

            // --- Orderflow: CVD flip ---

            bool cvdFlipLong = data.CvdFlipUp5m;
            bool cvdFlipShort = data.CvdFlipDown5m;

            // --- Składanie triggerów ---

            bool longTrigger =
                upTrend &&
                strongAdx &&
                pullbackLong &&
                acSupportsTrend &&
                oiSupportsUp &&
                hasSweepLong &&
                cvdFlipLong;

            bool shortTrigger =
                downTrend &&
                strongAdx &&
                pullbackShort &&
                acSupportsTrend &&
                oiSupportsDown &&
                hasSweepShort &&
                cvdFlipShort;

            // Dodatkowe „extra” – silniejsze setupy (duży overshoot + większe |z|-score)
            bool strongLong =
                longTrigger &&
                data.SweepDownOvershootBps5m >= 3.0 &&
                Math.Abs(zDev) >= 2.0;

            bool strongShort =
                shortTrigger &&
                data.SweepUpOvershootBps5m >= 3.0 &&
                Math.Abs(zDev) >= 2.0;

            bool buy = longTrigger;
            bool sell = shortTrigger;
            bool buyExtra = strongLong;
            bool sellExtra = strongShort;

            // Na wszelki wypadek, jeśli z jakiegoś powodu wyszłyby oba kierunki – ignorujemy.
            if (buy && sell)
            {
                buy = false;
                sell = false;
                buyExtra = false;
                sellExtra = false;
            }

            return new ModeSignal(buy, sell, buyExtra, sellExtra);
        }
    }
}
