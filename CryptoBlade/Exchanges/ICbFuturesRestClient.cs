using Bybit.Net.Enums;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;

namespace CryptoBlade.Exchanges
{
    public interface ICbFuturesRestClient
    {
        Task<bool> SetLeverageAsync(
            SymbolInfo symbol,
            CancellationToken cancel = default);

        Task<bool> SwitchPositionModeAsync(
            Models.PositionMode mode,
            string symbol,
            CancellationToken cancel = default);

        Task<bool> CancelOrderAsync(
            string symbol,
            string orderId,
            CancellationToken cancel = default);

        Task<bool> PlaceLimitBuyOrderAsync(
            string symbol,
            decimal quantity,
            decimal price,
            CancellationToken cancel = default);

        Task<bool> PlaceLimitSellOrderAsync(
            string symbol,
            decimal quantity,
            decimal price,
            CancellationToken cancel = default);

        Task<bool> PlaceMarketBuyOrderAsync(
            string symbol,
            decimal quantity,
            decimal price,
            CancellationToken cancel = default);

        Task<bool> PlaceMarketSellOrderAsync(
            string symbol,
            decimal quantity,
            decimal price,
            CancellationToken cancel = default);

        Task<bool> PlaceLongTakeProfitOrderAsync(
            string symbol,
            decimal qty,
            decimal price,
            bool force,
            CancellationToken cancel = default);

        Task<bool> PlaceShortTakeProfitOrderAsync(
            string symbol,
            decimal qty,
            decimal price,
            bool force,
            CancellationToken cancel = default);

        Task<bool> SetTradingStopAsync(
            string symbol,
            decimal priceScale,
            decimal stopLoss,
            decimal? takeProfit,
            decimal? trailingStop,
            PositionIdx positionIdx,
            decimal? activePrice = null,
            decimal? takeProfitQuantity = null,
            decimal? stopLossQuantity = null,
            StopLossTakeProfitMode? stopLossTakeProfitMode = null,
            CancellationToken cancel = default);

        Task<Balance> GetBalancesAsync(CancellationToken cancel = default);

        Task<SymbolInfo[]> GetSymbolInfoAsync(CancellationToken cancel = default);

        Task<Candle[]> GetKlinesAsync(
            string symbol,
            TimeFrame interval,
            int limit,
            CancellationToken cancel = default);

        Task<Candle[]> GetKlinesAsync(
            string symbol,
            TimeFrame interval,
            DateTime start,
            DateTime end,
            CancellationToken cancel = default);

        Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancel = default);

        Task<Order[]> GetOrdersAsync(CancellationToken cancel = default);

        Task<Position[]> GetPositionsAsync(CancellationToken cancel = default);

        Task<FundingRate[]> GetFundingRatesAsync(string symbol, DateTime start, DateTime end,
            CancellationToken cancel = default);

        // Open Interest (USD) – surowe punkty (np. 1h, limit: 2 dla Δ)
        Task<OpenInterestPoint[]> GetOpenInterestAsync(
            string symbol,
            TimeFrame interval,      // "1h","4h","1d" – zgodnie z Bybit v5
            int limit = 2,
            CancellationToken cancel = default);

        // Ostatni mark oraz index (np. z kline mark/index, limit=1 zamknięta świeca)
        Task<MarkIndexPair> GetLatestMarkAndIndexAsync(
            string symbol,
            TimeFrame interval = TimeFrame.OneMinute,
            CancellationToken cancel = default);

        // Public trades w oknie czasu (do CVD)
        Task<PublicTrade[]> GetRecentTradesAsync(
            string symbol,
            int limit,
            CancellationToken cancel = default);

        // Spread w bps z top-of-book/tickera
        Task<double> GetSpreadBpsAsync(
            string symbol,
            CancellationToken cancel = default);

        /// <summary>
        /// Bybit v5 market/open-interest (USD notionals). Zwraca serię (ts, value).
        /// interval: "5m" | "15m" | "30m" | "1h" | "4h" | "1d"
        /// limit: 1..200
        /// </summary>
        Task<(DateTime Ts, decimal Value)[]> GetOpenInterestUsdHistoryAsync(
            string symbol,
            TimeFrame interval = TimeFrame.OneHour,
            int limit = 2,
            CancellationToken cancel = default);
    }
}
