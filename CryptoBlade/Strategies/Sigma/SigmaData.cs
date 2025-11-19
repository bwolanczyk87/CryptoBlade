using System;
using System.Collections.Generic;
using System.Linq;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using CryptoExchange.Net.CommonObjects;
using Skender.Stock.Indicators;
using Ticker = CryptoBlade.Models.Ticker;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// Centralny snapshot danych wejściowych dla Sigmy.
    /// Zawiera wszystkie cechy wyliczone z helpersów (MarketMetrics, VwapHelpers,
    /// PatternDetectors, SessionHelpers, StatisticsHelpers), wspólne dla wszystkich reżimów.
    ///
    /// Zasada: brak danych ⇒ double.NaN (zamiast flag typu HasX).
    ///
    /// Klasa nie robi żadnego I/O (nie zna API, nie ma providerów).
    /// Wszystkie dane wejściowe pochodzą z SigmaStrategy (która pobiera je z giełdy
    /// i innych źródeł), a SigmaData zajmuje się wyłącznie obliczeniami i agregacją.
    /// </summary>
    public sealed class SigmaData(string symbol)
    {
        // ====== Identyfikacja ======
        public string Symbol { get; } = symbol ?? throw new ArgumentNullException(nameof(symbol));

        // ====== Cechy surowe (NaN oznacza "brak/niepoliczalne") ======

        // Trend / zmienność / value (głównie 1h / 5m / 15m)
        public double Adx1h { get; private set; }             // [0..100] lub NaN
        public double AtrPct1h { get; private set; }          // ATR_1h / Price * 100 (%)
        public double Atr1hAbs { get; private set; }          // ATR_1h w punktach
        public double AtrPct5m { get; private set; }          // ATR_5m / Price * 100 (%)
        public double Atr5mAbs { get; private set; }          // ATR_5m w punktach
        public double ZDvwap { get; private set; }            // z-score od D-VWAP (kotwica = start sesji)
        public VwapSource VwapKindUsed { get; private set; } = VwapSource.None;
        public double ZDvwapPrev { get; private set; }        // z-score z poprzedniego 1m bara intraday
        public double ZSlopeDvwap { get; private set; }       // t-stat nachylenia D-VWAP (HAC/Newey-West)

        public double AutoCorr5m { get; private set; }        // autokorelacja lag-1 (shrunk) na 5m
        public double Bbw15mPct { get; private set; }         // percentyl BBWidth (mid-rank, 0..100) na 15m
        public double Bbw15mRaw { get; private set; }         // surowa szerokość BB (Upper-Lower)/SMA * 100 na 15m

        // Mikrostruktura / egzekucja
        // public set zostawiony – jeśli zechcesz nadpisać SpreadBps świeższym live spreade’em.
        public double SpreadBps { get; set; }                 // spread bid-ask w bps (NaN jeśli brak)

        // Derywaty / flow
        public double OiDelta1hPct { get; private set; }      // ΔOI$ 1h w %
        public double FundingPredictedPct { get; private set; }   // predicted funding (na najbliższy cykl), w %
        public double FundingLastSettledPct { get; private set; } // ostatnio rozliczony funding, w %
        public DateTime? NextFundingUtc { get; private set; }     // czas kolejnego cyklu

        public double BasisPct { get; private set; }          // (Mark-Index)/Index * 100
        public double DeltaCvd5m { get; private set; }        // ΔCVD 5m (ostatnie 5 minut)
        public double DeltaCvdPrev5m { get; private set; }    // ΔCVD z poprzedniego 5-minutowego okna
        public bool CvdFlipUp5m { get; private set; }         // flip CVD: poprzednie 5m < 0, bieżące > 0
        public bool CvdFlipDown5m { get; private set; }       // flip CVD: poprzednie 5m > 0, bieżące < 0
        public double DistToLiqPct { get; private set; }      // dystans do najbliższego dużego klastra likwidacji w %

        // Struktura/patterny (liczone przez PatternDetectors)
        public bool HasInsideOrNr7 { get; private set; }

        // Sweep -> reclaim na 5m (lokalne polowanie na płynność)
        public bool SweepReclaimUp5m { get; private set; }          // sweep wcześniejszego high + reclaim (knot powyżej, close poniżej)
        public bool SweepReclaimDown5m { get; private set; }        // sweep wcześniejszego low + reclaim (knot poniżej, close powyżej)
        public double SweepUpOvershootBps5m { get; private set; }   // ile bps powyżej "swing high" poszedł knot
        public double SweepDownOvershootBps5m { get; private set; } // ile bps poniżej "swing low" poszedł knot

        public DonchianResult? DonchianResult { get; private set; }
        public bool DonchianBreakUp { get; private set; }
        public bool DonchianBreakDown { get; private set; }
        public bool DonchianBreak => DonchianBreakUp || DonchianBreakDown;

        // Breakout OR + retest na 5m (dla BreakoutMode)
        public bool OrBreakoutRetestUp5m { get; private set; }
        public bool OrBreakoutRetestDown5m { get; private set; }
        public double OrRetestDepthBpsUp5m { get; private set; }
        public double OrRetestDepthBpsDown5m { get; private set; }

        // Sygnały pomocnicze
        public bool Bbw15mExpanding { get; private set; }     // ekspansja BBW vs kilka barów wstecz
        public decimal? OpeningRangeHigh { get; private set; }
        public decimal? OpeningRangeLow { get; private set; }

        // Korelacja z BTC (NaN = brak)
        public double CorrToBtc15m { get; private set; }      // Pearson r z BTC na 15m
        public bool BtcBiasOpposite { get; private set; }     // heurystyka pod Supervisor/gate

        // ====== Surowy snapshot rynku (przydatny do audytu / triggerów) ======

        // Ticker (ostatnia znana wartość w momencie budowy SigmaData)
        public decimal? LastPrice { get; private set; }
        public decimal? BestBidPrice { get; private set; }
        public decimal? BestAskPrice { get; private set; }
        public decimal? MarkPrice { get; private set; }
        public decimal? IndexPrice { get; private set; }

        // Ostatni bar 1m (po sortowaniu po Date)
        public DateTime? Last1mTimeUtc { get; private set; }
        public decimal? Last1mOpen { get; private set; }
        public decimal? Last1mHigh { get; private set; }
        public decimal? Last1mLow { get; private set; }
        public decimal? Last1mClose { get; private set; }
        public decimal? Last1mVolume { get; private set; }

        // Ostatnia świeca 5m (po sortowaniu po Date)
        public DateTime? Last5mTimeUtc { get; private set; }
        public decimal? Last5mOpen { get; private set; }
        public decimal? Last5mHigh { get; private set; }
        public decimal? Last5mLow { get; private set; }
        public decimal? Last5mClose { get; private set; }

        // DVWAP – ostatnia wartość
        public decimal? LastDvwap { get; private set; }

        // =====================================================================
        // BUILD – wypełnia bieżącą instancję SigmaData
        // =====================================================================

        /// <summary>
        /// Wypełnia właściwości SigmaData na podstawie:
        /// - lokalnych świec symbolu po TF (1m/5m/15m/1h),
        /// - świec referencyjnych BTC na 15m (do korelacji),
        /// - tickera symbolu (Mark/Index/LastBid/Ask),
        /// - danych mikrostruktury (live spread, trades, likwidacje),
        /// - danych derywatów (historia OI, funding).
        ///
        /// Wszystkie obliczenia wykorzystują helpery (MarketMetrics, VwapHelpers,
        /// PatternDetectors, SessionHelpers, StatisticsHelpers). Żadnego I/O.
        /// </summary>
        /// <param name="quotesByTimeFrame">
        /// Świece symbolu po TF, np.:
        /// - TimeFrame.OneMinute,
        /// - TimeFrame.FiveMinutes,
        /// - TimeFrame.FifteenMinutes,
        /// - TimeFrame.OneHour.
        /// Dla brakującego TF można przekazać pustą tablicę.
        public void Build(
            DateTime nowUtc,
            Dictionary<TimeFrame, QuoteQueue> quotesByTimeFrame,
            Quote[] btcQuotes15m,
            Ticker? ticker,
            IReadOnlyCollection<PublicTrade>? publicTrades,
            IReadOnlyCollection<LiquidationEvent>? liguidations,
            IReadOnlyList<OpenInterestPoint>? openInterestPoints,
            IEnumerable<FundingRate>? recentFundingRatesUtc,
            int sessionStartHourUtc = 0,
            int vwapSlopeWindow = 60,
            int corrWindow = 60)
        {
            Quote[] q1m = quotesByTimeFrame.TryGetValue(TimeFrame.OneMinute, out var q1)
                ? q1.GetQuotes() ?? []
                : [];

            Quote[] q5m = quotesByTimeFrame.TryGetValue(TimeFrame.FiveMinutes, out var q5)
                ? q5.GetQuotes() ?? []
                : [];

            Quote[] q15m = quotesByTimeFrame.TryGetValue(TimeFrame.FifteenMinutes, out var q15)
                ? q15.GetQuotes() ?? []
                : [];

            Quote[] q1h = quotesByTimeFrame.TryGetValue(TimeFrame.OneHour, out var qh)
                ? qh.GetQuotes() ?? []
                : [];

            // 0) Porządkowanie danych (chronologicznie rosnąco)
            SessionHelpers.EnsureSortedByDate(q1m);
            SessionHelpers.EnsureSortedByDate(q5m);
            SessionHelpers.EnsureSortedByDate(q15m);
            SessionHelpers.EnsureSortedByDate(q1h);
            SessionHelpers.EnsureSortedByDate(btcQuotes15m);

            // 0.1) Snapshot tickera
            LastPrice = ticker.LastPrice;
            BestBidPrice = ticker.BestBidPrice;
            BestAskPrice = ticker.BestAskPrice;
            MarkPrice = ticker.MarkPrice;
            IndexPrice = ticker.IndexPrice;

            // 0.2) Snapshot ostatniej świecy 1m (po sortowaniu)
            if (q1m.Length > 0)
            {
                var lastBar = q1m[^1];
                Last1mTimeUtc = lastBar.Date;
                Last1mOpen = lastBar.Open;
                Last1mHigh = lastBar.High;
                Last1mLow = lastBar.Low;
                Last1mClose = lastBar.Close;
                Last1mVolume = lastBar.Volume;
            }
            else
            {
                Last1mTimeUtc = null;
                Last1mOpen = Last1mHigh = Last1mLow = Last1mClose = Last1mVolume = null;
            }

            if (q5m.Length > 0)
            {
                var last5 = q5m[^1];
                Last5mTimeUtc = last5.Date;
                Last5mOpen = last5.Open;
                Last5mHigh = last5.High;
                Last5mLow = last5.Low;
                Last5mClose = last5.Close;
            }
            else
            {
                Last5mTimeUtc = null;
                Last5mOpen = Last5mHigh = Last5mLow = Last5mClose = null;
            }

            // =====================================================================
            // 1. Trend / value / zmienność (TF: 1H / 5m / 15m)
            // =====================================================================

            // ADX 1H (trend strength)
            Adx1h = MarketMetrics.ComputeAdxLast(q1h);

            // ATR% 1H + ATR abs (zmienność relatywna/absolutna)
            double refPrice = SessionHelpers.SelectReferencePrice(ticker, q1h);
            (AtrPct1h, Atr1hAbs) = MarketMetrics.ComputeAtrPercentAndAbs(q1h, refPrice, lookback: 14);

            // ATR% 5m + ATR abs – do trailingu i krótkoterminowego ryzyka
            (AtrPct5m, Atr5mAbs) = MarketMetrics.ComputeAtrPercentAndAbs(q5m, refPrice, lookback: 14);

            // =====================================================================
            // 2. DVWAP / value (TF: 1m, anchored + fallback rolling)
            // =====================================================================

            // Kotwica sesji (np. 0:00 UTC albo 8:00 UTC – zależnie od parametru)
            DateTime anchor = SessionHelpers.GetCurrentSessionAnchorUtc(q1m, sessionStartHourUtc);

            // D-VWAP od kotwicy z fallbackiem do rolling VWAP
            var (vwapSeries, lastVwap, tpStdDay, vwapSrc) =
                VwapHelpers.ComputeAnchoredDailyVwapSeries(
                    q1m,
                    anchor,
                    minIntradayBars: 20,
                    rollingWindow: 60);

            VwapKindUsed = vwapSrc;

            // Z-score do VWAP (clamp |z|<=10) dla bieżącej ceny
            ZDvwap = VwapHelpers.ComputeZDvwap(ticker?.LastPrice, lastVwap, tpStdDay);

            // Poprzedni z-score względem DVWAP – z poprzedniego 1m bara intraday
            ZDvwapPrev = double.NaN;

            if (vwapSeries.Length >= 2
                && q1m.Length > 0
                && double.IsFinite(tpStdDay)
                && tpStdDay > 0.0)
            {
                var intraday = q1m.Where(q => q.Date >= anchor).ToArray();
                if (intraday.Length >= 2)
                {
                    var prevClose = intraday[^2].Close;
                    var prevVwap = vwapSeries[^2];

                    ZDvwapPrev = VwapHelpers.ComputeZDvwap(prevClose, prevVwap, tpStdDay);
                }
            }

            // t-stat nachylenia DVWAP (HAC/Newey–West)
            ZSlopeDvwap = VwapHelpers.ComputeSlopeTstatHAC(
                vwapSeries,
                Math.Max(10, vwapSlopeWindow));

            if (double.IsFinite(lastVwap) && lastVwap > 0.0)
                LastDvwap = (decimal)lastVwap;
            else
                LastDvwap = null;

            // =====================================================================
            // 3. Struktura zmienności / patterny (5m / 15m / 1m)
            // =====================================================================

            // Autokorelacja lag-1 na 5m (shrunk, proxy do mean-reversion / momentum)
            AutoCorr5m = MarketMetrics.ComputeLag1AutoCorrelationShrunkFromQuotes(
                q5m,
                shrinkKappa: 8);

            // BBWidth 15m – percentyl + raw w %
            (Bbw15mPct, Bbw15mRaw) = MarketMetrics.ComputeBollingerBandwidthPercentile(
                q15m,
                period: 20,
                stdDevMultiplier: 2.0);

            // Ekspansja BBW 15m (now > sprzed 2 barów)
            Bbw15mExpanding = MarketMetrics.IsBollingerBandwidthExpanding(
                q15m,
                period: 20,
                stdDevMultiplier: 2.0,
                back: 2);

            // Opening Range (np. ORB) z pierwszych 30 minut sesji (na 1m)
            SessionState.SessionAnchorUtc = anchor;
            PatternDetectors.EnsureOpeningRangeComputed(q1m, nowUtc, anchor, 30);
            OpeningRangeHigh = SessionState.OpeningRangeHigh;
            OpeningRangeLow = SessionState.OpeningRangeLow;

            // Inside / NR7 na 5m – lokalna kompresja
            HasInsideOrNr7 = PatternDetectors.HasInsideBarOrNr7Pattern(q5m);

            // Sweep -> reclaim na 5m: lokalne polowanie na płynność (Momentum)
            (SweepReclaimUp5m,
             SweepReclaimDown5m,
             SweepUpOvershootBps5m,
             SweepDownOvershootBps5m) =
                PatternDetectors.DetectSweepReclaimOnLastBar(
                    q5m,
                    lookbackBars: 12,
                    minOvershootBps: 1.0);

            // Donchian breakout na 15m – pod reżim BO
            (DonchianResult, DonchianBreakUp, DonchianBreakDown) =
                PatternDetectors.DetectDonchianBreakout(q15m, period: 20);

            // OR breakout + retest na 5m – kluczowy pattern dla BreakoutMode
            (OrBreakoutRetestUp5m,
             OrBreakoutRetestDown5m,
             OrRetestDepthBpsUp5m,
             OrRetestDepthBpsDown5m) =
                PatternDetectors.DetectOpeningRangeBreakoutWithRetestOnLastBars(
                    q5m,
                    OpeningRangeHigh,
                    OpeningRangeLow,
                    breakoutEpsPct: 0.05,
                    retestDepthPct: 0.15);

            // =====================================================================
            // 4. Mikrostruktura (spread, ΔCVD, likwidacje)
            // =====================================================================

            // Spread w bps z tickera
            SpreadBps = MarketMetrics.ComputeSpreadBps(ticker);

            // ΔCVD 5m – z publicTrades, używając generycznego helpera
            DeltaCvd5m = MarketMetrics.ComputeSignedVolumeDelta(
                publicTrades,
                fromUtc: nowUtc - TimeSpan.FromMinutes(5),
                toUtc: nowUtc);

            // Poprzednie 5m – do detekcji flipu CVD (zmiana znaku)
            DeltaCvdPrev5m = MarketMetrics.ComputeSignedVolumeDelta(
                publicTrades,
                fromUtc: nowUtc - TimeSpan.FromMinutes(10),
                toUtc: nowUtc - TimeSpan.FromMinutes(5));

            // Flipy CVD – tylko jeśli obie wartości są sensowne
            if (double.IsNaN(DeltaCvd5m) || double.IsNaN(DeltaCvdPrev5m))
            {
                CvdFlipUp5m = false;
                CvdFlipDown5m = false;
            }
            else
            {
                CvdFlipUp5m = DeltaCvdPrev5m < 0 && DeltaCvd5m > 0;
                CvdFlipDown5m = DeltaCvdPrev5m > 0 && DeltaCvd5m < 0;
            }

            // Dystans do najbliższego "silnego" klastra likwidacji (w %)
            DistToLiqPct = MarketMetrics.ComputeDistanceToLiqClusterPct(
                liguidations,
                ticker?.LastPrice ?? 0m,
                nowUtc,
                lookbackMinutes: 20.0,
                binStepPct: 0.10,
                pctl: 75.0,
                minSharePct: 5.0);

            // =====================================================================
            // 5. Derywaty / flow (ΔOI, basis, funding)
            // =====================================================================

            // ΔOI$ 1h w % (NaN, jeśli brak danych)
            OiDelta1hPct = openInterestPoints != null && openInterestPoints.Count >= 2
            ? MarketMetrics.ComputeOpenInterestDeltaPctFromSeries(
                openInterestPoints,
                horizon: TimeSpan.FromHours(1),
                getTime: p => p.Timestamp,
                getOi: p => p.OpenInterest)
            : double.NaN;

            // Basis% (Mark-Index)/Index * 100 – z tickera
            BasisPct = MarketMetrics.ComputeBasisPctFromTicker(ticker);

            // Funding snapshot (predykcja + ostatnio rozliczony)
            var (predicted, lastSettled) =
                MarketMetrics.ComputeFundingSnapshot(
                    ticker,
                    recentFundingRatesUtc);

            FundingPredictedPct = predicted?.Rate is decimal pr
                ? (double)pr
                : double.NaN;

            FundingLastSettledPct = lastSettled?.Rate is decimal lr
                ? (double)lr
                : double.NaN;

            NextFundingUtc = predicted?.Time;

            // =====================================================================
            // 6. Korelacja z BTC + "bias opposite" (heurystyka pod Supervisor)
            // =====================================================================

            var (corr, lastBtcLogRet) =
                MarketMetrics.ComputeRollingLogReturnCorrelationAndLastYFromQuotes(
                    q15m,
                    btcQuotes15m,
                    window: corrWindow);

            CorrToBtc15m = corr;

            // ostatni log-zwrot naszego symbolu X (np. ETH) na 15m
            double lastSymLogRet = double.NaN;
            if (q15m.Length >= 2)
            {
                double a = (double)q15m[^2].Close;
                double b = (double)q15m[^1].Close;
                if (a > 0.0 && double.IsFinite(a) && double.IsFinite(b))
                    lastSymLogRet = Math.Log(b / a);
            }

            BtcBiasOpposite = MarketMetrics.ComputeBiasOpposite(
                CorrToBtc15m,
                lastSymLogRet,
                lastBtcLogRet,
                ZSlopeDvwap);

            // =====================================================================
            // 7. Sanitization / clampy (NaN jako "brak")
            // =====================================================================

            Sanitize();
        }

        // =====================================================================
        //  SANITIZE
        // =====================================================================

        /// <summary>
        /// Clamp/filtr wartości liczbowych: wyrzuca Infinity, zamienia "nonsens"
        /// (ujemny spread, ujemny dystans do liq) na NaN i przycina ekstremalne wartości
        /// do rozsądnych zakresów bezpieczeństwa. NaN zostaje NaN (sygnał: brak danych).
        /// </summary>
        private void Sanitize()
        {
            // --- Trend / value / vol ---
            Adx1h = StatisticsHelpers.ClampFinite(Adx1h, 0, 100, allowNaN: true);
            AtrPct1h = StatisticsHelpers.ClampFinite(AtrPct1h, 0, 100, allowNaN: true);
            Atr1hAbs = StatisticsHelpers.ClampFinite(Atr1hAbs, 0, 1e12, allowNaN: true);
            ZDvwap = StatisticsHelpers.ClampFinite(ZDvwap, -10, 10, allowNaN: true);
            ZDvwapPrev = StatisticsHelpers.ClampFinite(ZDvwapPrev, -10, 10, allowNaN: true);
            ZSlopeDvwap = StatisticsHelpers.ClampFinite(ZSlopeDvwap, -25, 25, allowNaN: true);
            AutoCorr5m = StatisticsHelpers.ClampFinite(AutoCorr5m, -1, 1, allowNaN: true);
            Bbw15mPct = StatisticsHelpers.ClampFinite(Bbw15mPct, 0, 100, allowNaN: true);
            Bbw15mRaw = StatisticsHelpers.ClampFinite(Bbw15mRaw, 0, 1e6, allowNaN: true);

            // --- Mikrostruktura ---
            SpreadBps = double.IsFinite(SpreadBps) && SpreadBps >= 0
                ? StatisticsHelpers.ClampFinite(SpreadBps, 0, 1e4, allowNaN: true)   // 10 000 bps = 100%
                : double.NaN;

            // --- Derywaty / flow ---
            OiDelta1hPct = StatisticsHelpers.ClampFinite(OiDelta1hPct, -500, 500, allowNaN: true);
            BasisPct = StatisticsHelpers.ClampFinite(BasisPct, -100, 100, allowNaN: true);
            DeltaCvd5m = StatisticsHelpers.ClampFinite(DeltaCvd5m, -1e12, 1e12, allowNaN: true);
            DeltaCvdPrev5m = StatisticsHelpers.ClampFinite(DeltaCvdPrev5m, -1e12, 1e12, allowNaN: true);

            FundingPredictedPct = StatisticsHelpers.ClampFinite(FundingPredictedPct, -5, 5, allowNaN: true);
            FundingLastSettledPct = StatisticsHelpers.ClampFinite(FundingLastSettledPct, -5, 5, allowNaN: true);
            // NextFundingUtc – DateTime? nie clampujemy

            // Dystans do likwidacji: musi być ≥0; jeśli ujemny/bez sensu → NaN
            if (!double.IsFinite(DistToLiqPct) || DistToLiqPct < 0)
                DistToLiqPct = double.NaN;
            else
                DistToLiqPct = StatisticsHelpers.ClampFinite(DistToLiqPct, 0, 1e6, allowNaN: true);

            // --- Korelacja BTC ---
            CorrToBtc15m = StatisticsHelpers.ClampFinite(CorrToBtc15m, -1, 1, allowNaN: true);

            OrRetestDepthBpsUp5m = StatisticsHelpers.ClampFinite(OrRetestDepthBpsUp5m, 0, 1e4, allowNaN: true);
            OrRetestDepthBpsDown5m = StatisticsHelpers.ClampFinite(OrRetestDepthBpsDown5m, 0, 1e4, allowNaN: true);
        }
    }
}
