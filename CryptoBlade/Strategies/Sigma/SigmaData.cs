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
    public sealed class SigmaData
    {
        // ====== Identyfikacja ======
        public string Symbol { get; }

        public SigmaData(string symbol)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        // ====== Cechy surowe (NaN oznacza "brak/niepoliczalne") ======

        // Trend / zmienność / value (głównie 1h / 5m / 15m)
        public double Adx1h { get; private set; }             // [0..100] lub NaN
        public double AtrPct1h { get; private set; }          // ATR_1h / Price * 100 (%)
        public double Atr1hAbs { get; private set; }          // ATR_1h w punktach
        public double ZDvwap { get; private set; }            // z-score od D-VWAP (kotwica = start sesji)
        public VwapSource VwapKindUsed { get; private set; } = VwapSource.None;
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
        public double DeltaCvd5m { get; private set; }        // ΔCVD 5m
        public double DistToLiqPct { get; private set; }      // dystans do najbliższego dużego klastra likwidacji w %

        // Struktura/patterny (liczone przez PatternDetectors)
        public bool HasInsideOrNr7 { get; private set; }
        public bool DonchianBreakUp { get; private set; }
        public bool DonchianBreakDown { get; private set; }
        public bool DonchianBreak => DonchianBreakUp || DonchianBreakDown;

        // Sygnały pomocnicze
        public bool Bbw15mExpanding { get; private set; }     // ekspansja BBW vs kilka barów wstecz
        public decimal? OpeningRangeHigh { get; private set; }
        public decimal? OpeningRangeLow { get; private set; }

        // Korelacja z BTC (NaN = brak)
        public double CorrToBtc15m { get; private set; }      // Pearson r z BTC na 15m
        public bool BtcBiasOpposite { get; private set; }     // heurystyka pod Supervisor/gate

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
        /// </param>
        /// <param name="btcQuotes15m">
        /// Świece BTCUSDT / BTCUSD na TF=15m (do korelacji i bias opposite).
        /// </param>
        /// <param name="ticker">
        /// Aktualny ticker symbolu (BestBid/BestAsk, LastPrice, MarkPrice, IndexPrice,
        /// FundingRate, NextFundingTime, itp.).
        /// </param>
        /// <param name="spreadBpsLive">
        /// Live spread z orderbooka (bps), jeśli dostępny; w przeciwnym razie null
        /// i zostanie użyty fallback z tickera.
        /// </param>
        /// <param name="publicTrades">
        /// Public trades dla symbolu, wykorzystywane do wyliczenia ΔCVD 5m.
        /// Jeśli null lub puste, ΔCVD będzie NaN.
        /// </param>
        /// <param name="liqs20m">
        /// Likwidacje z ~20 minut dla symbolu, do oszacowania dystansu do klastra liq.
        /// </param>
        /// <param name="openInterestPoints1h">
        /// Punkty open interest (np. 1h), z których liczymy ΔOI$ 1h w %.
        /// Wystarczą 2 ostatnie próbki.
        /// </param>
        /// <param name="recentFundingRatesUtc">
        /// Ostatnie wartości funding rate (w formacie FundingRate), używane do snapshotu:
        /// predicted + last settled.
        /// </param>
        /// <param name="sessionStartHourUtc">
        /// Godzina UTC startu sesji dla DVWAP (np. 0 dla północy, 8 dla sesji EU).
        /// </param>
        /// <param name="vwapSlopeWindow">
        /// Minimalna liczba punktów okna do liczenia t-stat nachylenia DVWAP.
        /// </param>
        /// <param name="corrWindow">
        /// Długość okna (liczba log-zwrotów) do liczenia korelacji log-zwrotów
        /// symbolu z BTC na TF=15m.
        /// </param>
        public void Build(
            IReadOnlyDictionary<TimeFrame, Quote[]> quotesByTimeFrame,
            Quote[] btcQuotes15m,
            Ticker ticker,
            double? spreadBpsLive,
            IReadOnlyCollection<PublicTrade>? publicTrades,
            IReadOnlyList<LiquidationEvent>? liqs20m,
            IReadOnlyList<OpenInterestPoint>? openInterestPoints1h,
            IEnumerable<FundingRate>? recentFundingRatesUtc,
            int sessionStartHourUtc = 0,
            int vwapSlopeWindow = 60,
            int corrWindow = 60)
        {
            if (quotesByTimeFrame == null) throw new ArgumentNullException(nameof(quotesByTimeFrame));
            if (btcQuotes15m == null) throw new ArgumentNullException(nameof(btcQuotes15m));
            if (ticker == null) throw new ArgumentNullException(nameof(ticker));

            // Wyciągamy świece per TF (jeśli brak – pusta tablica).
            Quote[] q1m = quotesByTimeFrame.TryGetValue(TimeFrame.OneMinute, out var q1)
                ? q1 ?? Array.Empty<Quote>()
                : Array.Empty<Quote>();

            Quote[] q5m = quotesByTimeFrame.TryGetValue(TimeFrame.FiveMinutes, out var q5)
                ? q5 ?? Array.Empty<Quote>()
                : Array.Empty<Quote>();

            Quote[] q15m = quotesByTimeFrame.TryGetValue(TimeFrame.FifteenMinutes, out var q15)
                ? q15 ?? Array.Empty<Quote>()
                : Array.Empty<Quote>();

            Quote[] q1h = quotesByTimeFrame.TryGetValue(TimeFrame.OneHour, out var qh)
                ? qh ?? Array.Empty<Quote>()
                : Array.Empty<Quote>();

            // 0) Porządkowanie danych (chronologicznie rosnąco)
            SessionHelpers.EnsureSortedByDate(q1m);
            SessionHelpers.EnsureSortedByDate(q5m);
            SessionHelpers.EnsureSortedByDate(q15m);
            SessionHelpers.EnsureSortedByDate(q1h);
            SessionHelpers.EnsureSortedByDate(btcQuotes15m);

            // =====================================================================
            // 1. Trend / value / zmienność (TF: 1H / 5m / 15m)
            // =====================================================================

            // ADX 1H (trend strength)
            Adx1h = MarketMetrics.ComputeAdxLast(q1h);

            // ATR% 1H + ATR abs (zmienność relatywna/absolutna)
            double refPrice = SessionHelpers.SelectReferencePrice(ticker, q1h);
            (AtrPct1h, Atr1hAbs) = MarketMetrics.ComputeAtrPercentAndAbs(q1h, refPrice, lookback: 14);

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

            // Z-score do VWAP (clamp |z|<=10)
            ZDvwap = VwapHelpers.ComputeZDvwap(ticker?.LastPrice, lastVwap, tpStdDay);

            // t-stat nachylenia DVWAP (HAC/Newey–West)
            ZSlopeDvwap = VwapHelpers.ComputeSlopeTstatHAC(
                vwapSeries,
                Math.Max(10, vwapSlopeWindow));

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

            // Inside / NR7 na 5m – lokalna kompresja
            HasInsideOrNr7 = PatternDetectors.HasInsideBarOrNr7Pattern(q5m);

            // Donchian breakout na 15m – pod reżim BO
            (DonchianBreakUp, DonchianBreakDown) =
                PatternDetectors.DetectDonchianBreakout(q15m, period: 20);

            // Opening Range (np. ORB) z pierwszych 30 minut sesji (na 1m)
            var intraday1m = q1m
                .Where(b => b.Date >= anchor)
                .ToArray();

            (var orHigh, var orLow) = PatternDetectors.ComputeOpeningRange(
                intraday1m,
                minutes: 30);

            OpeningRangeHigh = (orHigh == 0m && orLow == 0m) ? null : orHigh;
            OpeningRangeLow = (orHigh == 0m && orLow == 0m) ? null : orLow;

            // =====================================================================
            // 4. Mikrostruktura (spread, ΔCVD, likwidacje)
            // =====================================================================

            // Spread w bps – preferujemy live z orderbooka, fallback z tickera
            SpreadBps = MarketMetrics.ComputeSpreadBps(spreadBpsLive, ticker);

            // ΔCVD 5m – z publicTrades, używając generycznego helpera
            DeltaCvd5m = MarketMetrics.ComputeSignedVolumeDelta(
                publicTrades,
                DateTime.UtcNow,
                TimeSpan.FromMinutes(5));

            // Dystans do najbliższego "silnego" klastra likwidacji (w %)
            DistToLiqPct = MarketMetrics.ComputeDistanceToLiqClusterPct(
                liqs20m,
                ticker?.LastPrice ?? 0m,
                DateTime.UtcNow,
                lookbackMinutes: 20.0,
                binStepPct: 0.10,
                pctl: 75.0,
                minSharePct: 5.0);

            // =====================================================================
            // 5. Derywaty / flow (ΔOI, basis, funding)
            // =====================================================================

            // ΔOI$ 1h w % (NaN, jeśli brak danych)
            OiDelta1hPct = openInterestPoints1h != null
                ? MarketMetrics.ComputeOpenInterestDeltaPctFromPoints(
                    openInterestPoints1h,
                    p => p.OpenInterest)
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
        }
    }
}
