using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
using OrderSide = CryptoBlade.Models.OrderSide;

namespace CryptoBlade.Strategies.Sigma
{
    public sealed class SigmaPositionManager(SigmaStrategyOptions options, ICbFuturesRestClient restClient)
    {
        private readonly SigmaStrategyOptions _options = options;
        private readonly ICbFuturesRestClient _restClient = restClient;

        public async Task OnSignalAsync(
            DateTime nowUtc,
            SymbolInfo symbolInfo,
            SigmaData data,
            IMode? mode,
            ModeSignal signal,
            IWalletManager walletManager,
            ILogger logger,
            CancellationToken cancel)
        {
            logger.LogInformation(
                "sym={Symbol} | mode={Mode} | hasBuy={HasBuy} | hasSell={HasSell}",
                symbolInfo.Name,
                mode?.Kind.ToString() ?? "None",
                signal.HasBuy,
                signal.HasSell);

            if (mode is null)
                return;

            bool hasBuy = signal.HasBuy;
            bool hasSell = signal.HasSell;
            if ((!hasBuy && !hasSell) || (hasBuy && hasSell))
                return;

            var side = hasBuy ? OrderSide.Buy : OrderSide.Sell;

            var entryPriceOpt = mode.ComputeEntryPrice(data, symbolInfo, side);
            if (!entryPriceOpt.HasValue || entryPriceOpt.Value <= 0m)
                return;

            var entryPrice = entryPriceOpt.Value;

            var slPriceOpt = mode.ComputeStopLossPrice(data, symbolInfo, side, entryPrice);
            if (!slPriceOpt.HasValue || slPriceOpt.Value <= 0m)
                return;

            var slPrice = slPriceOpt.Value;
            var minMove = ComputeMinSlMove(symbolInfo, entryPrice);
            if (side == OrderSide.Buy)
            {
                if (entryPrice - slPrice < minMove)
                    slPrice = entryPrice - minMove;
            }
            else
            {
                if (slPrice - entryPrice < minMove)
                    slPrice = entryPrice + minMove;
            }

            if (slPrice <= 0m)
                return;

            var riskPerUnit = Math.Abs(entryPrice - slPrice);
            if (riskPerUnit <= 0m)
                return;

            var (tp1Price, _) = mode.ComputeTakeProfits(data, symbolInfo, side, entryPrice, riskPerUnit);
            if (!tp1Price.HasValue || tp1Price.Value <= 0m)
                return;

            decimal equity = walletManager.Contract.Equity ?? 0m;
            decimal riskPerTradeUsd = ComputeRiskPerTradeUsd(data, signal, equity);
            if (riskPerTradeUsd <= 0m)
                return;

            decimal distance = side == OrderSide.Buy
                ? entryPrice - slPrice
                : slPrice - entryPrice;

            if (distance <= 0m)
                return;

            decimal notionalUsd = riskPerTradeUsd * entryPrice / distance;

            decimal quantity = CalculateQuantityInContracts(symbolInfo, entryPrice, notionalUsd);
            if (quantity <= 0m)
                return;

            string clientOrderId = SigmaClientOrderId.Build(
                symbolInfo.Name,
                mode.Kind,
                side,
                SigmaOrderKind.Entry,
                nowUtc);

            var request = new BybitCbFuturesRestClient.OrderRequest(
                Symbol: symbolInfo.Name,
                Category: Category.Linear,
                Side: side.ToOrderSide(),
                Type: NewOrderType.Limit,
                Quantity: quantity,
                Price: entryPrice,
                StopLossPrice: slPrice,
                SlTriggerBy: TriggerType.LastPrice,
                StopLossTakeProfitMode: StopLossTakeProfitMode.Partial,
                StopLossOrderType: OrderType.Market,
                TakeProfitPrice: tp1Price,
                TpTriggerBy: TriggerType.LastPrice,
                TakeProfitOrderType: OrderType.Limit,
                TakeProfitLimitPrice: tp1Price,
                ReduceOnly: false,
                CloseOnTrigger: false,
                TimeInForce: TimeInForce.PostOnly,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: clientOrderId);

            logger.LogInformation(
                "sym={Symbol} | side={Side} | mode={Mode} | qty={Qty} | entry={Entry} | sl={Sl} | tp1={Tp1} | riskUsd={RiskUsd}",
                symbolInfo.Name,
                side,
                mode.Kind,
                quantity,
                entryPrice,
                slPrice,
                tp1Price,
                riskPerTradeUsd);

            _ = await _restClient.PlaceOrderAsync(request, cancel);
        }

        public decimal ComputeRiskPerTradeUsd(SigmaData sigmaData, ModeSignal modeSignal, decimal? equity)
        {
            if (!equity.HasValue || equity.Value <= 0m)
                return _options.MinRiskPerTradeUsd;

            decimal risk = equity.Value * _options.RiskPerTradePct;

            if (risk < _options.MinRiskPerTradeUsd) risk = _options.MinRiskPerTradeUsd;
            if (risk > _options.MaxRiskPerTradeUsd) risk = _options.MaxRiskPerTradeUsd;

            return risk;
        }

        private static decimal CalculateQuantityInContracts(SymbolInfo symbolInfo, decimal entryPrice, decimal usdNotional)
        {
            if (entryPrice <= 0m || usdNotional <= 0m)
                return 0m;

            var rawQty = usdNotional / entryPrice;

            if (symbolInfo.QtyStep.HasValue && symbolInfo.QtyStep.Value > 0m)
            {
                var step = symbolInfo.QtyStep.Value;
                rawQty -= rawQty % step;
            }

            return MathHelpers.RoundQuantity(symbolInfo.QtyStep ?? 0m, rawQty);
        }

        private static decimal ComputeMinSlMove(SymbolInfo symbolInfo, decimal refPrice)
        {
            var scale = (int)symbolInfo.PriceScale;
            if (scale is < 0 or > 18)
                scale = 4;

            decimal tick = (decimal)Math.Pow(10, -scale);
            return tick * 2m;
        }
    }
}
