using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ticker = CryptoBlade.Models.Ticker;

namespace CryptoBlade.Api.Controllers
{
    [ApiController]
    [Route("api/bybit")]
    public class BybitController : ControllerBase
    {
        private readonly IBybitCBRestClient _client;
        private readonly ILogger<BybitController> _logger;

        public BybitController(IBybitCBRestClient client, ILogger<BybitController> logger)
        {
            _client = client;
            _logger = logger;
        }

        // =========================================================
        // ORDERS
        // =========================================================

        /// <summary>PLACE ORDER (market/limit/conditional) + optional TP/SL → returns orderId.</summary>
        [HttpPost("orders/place")]
        [Authorize]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (req.Type == NewOrderType.Limit && req.Price is null)
                return BadRequest("Limit order requires Price.");
            if (req.ReduceOnly && (req.TakeProfit.HasValue || req.StopLoss.HasValue))
                return BadRequest("reduceOnly=true cannot be combined with TP/SL in create-order.");
            if (req.TpslMode == StopLossTakeProfitMode.Full &&
               (req.TpOrderType == OrderType.Limit || req.SlOrderType == OrderType.Limit))
                return BadRequest("tpslMode=Full supports only Market TP/SL.");
            if (req.TpslMode == StopLossTakeProfitMode.Partial)
            {
                if (req.TpOrderType == OrderType.Limit && req.TpLimitPrice is null)
                    return BadRequest("Partial+TP Limit requires TpLimitPrice.");
                if (req.SlOrderType == OrderType.Limit && req.SlLimitPrice is null)
                    return BadRequest("Partial+SL Limit requires SlLimitPrice.");
            }

            var res = await _client.PlaceOrderAsync(
                symbol: req.Symbol,
                side: req.Side,
                orderType: req.Type,
                qty: req.Quantity,
                price: req.Price,
                positionIdx: req.PositionIdx,
                timeInForce: req.TimeInForce,
                reduceOnly: req.ReduceOnly,
                closeOnTrigger: req.CloseOnTrigger,
                triggerPrice: req.TriggerPrice,
                triggerBy: req.TriggerBy,
                triggerDirection: req.TriggerDirection,
                takeProfit: req.TakeProfit,
                stopLoss: req.StopLoss,
                tpTriggerBy: req.TpTriggerBy,
                slTriggerBy: req.SlTriggerBy,
                tpslMode: req.TpslMode,
                tpOrderType: req.TpOrderType,
                slOrderType: req.SlOrderType,
                tpLimitPrice: req.TpLimitPrice,
                slLimitPrice: req.SlLimitPrice,
                clientOrderId: req.ClientOrderId,
                cancel: ct);

            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "PlaceOrderFailed");

            return Ok(new { orderId = res.OrderId, clientOrderId = req.ClientOrderId });
        }

        /// <summary>EDIT ORDER (price/qty/TP/SL on order) → returns orderId.</summary>
        [HttpPatch("orders/edit")]
        [Authorize]
        public async Task<IActionResult> EditOrder([FromBody] AmendOrderRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            if (string.IsNullOrEmpty(req.OrderId) && string.IsNullOrEmpty(req.ClientOrderId))
                return BadRequest("Provide OrderId or ClientOrderId.");

            if (req.TpslMode == StopLossTakeProfitMode.Full && (req.TpLimitPrice.HasValue || req.SlLimitPrice.HasValue))
                return BadRequest("EditOrder: tpslMode=Full ignores limit prices.");

            var res = await _client.AmendOrderAsync(
                symbol: req.Symbol,
                orderId: req.OrderId,
                clientOrderId: req.ClientOrderId,
                price: req.Price,
                qty: req.Quantity,
                takeProfit: req.TakeProfit,
                stopLoss: req.StopLoss,
                tpTriggerBy: req.TpTriggerBy,
                slTriggerBy: req.SlTriggerBy,
                tpslMode: req.TpslMode,
                tpLimitPrice: req.TpLimitPrice,
                slLimitPrice: req.SlLimitPrice,
                cancel: ct);

            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "EditOrderFailed");

            return Ok(new { orderId = res.OrderId, clientOrderId = req.ClientOrderId });
        }

        /// <summary>CANCEL ORDER (by id) → returns orderId.</summary>
        [HttpDelete("orders/{symbol}/{orderId}")]
        [Authorize]
        public async Task<IActionResult> CancelOrder([FromRoute] string symbol, [FromRoute] string orderId, CancellationToken ct)
        {
            var res = await _client.CancelOrderAsync(symbol, orderId, ct);
            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "CancelOrderFailed");

            return Ok(new { orderId = res.OrderId });
        }

        /// <summary>CANCEL ALL OPEN ORDERS (optional symbol filter) → returns count canceled & success.</summary>
        [HttpDelete("orders/cancel-all")]
        [Authorize]
        public async Task<IActionResult> CancelAllOrders([FromQuery] string? symbol, CancellationToken ct)
        {
            var res = await _client.CancelAllOrdersAndCountAsync(symbol, ct);
            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "CancelAllFailed");

            return Ok(new { canceled = res.Canceled, success = res.Success });
        }

        /// <summary>PLACE REDUCE-ONLY TP FOR LONG → returns orderId.</summary>
        [HttpPost("orders/tp/long")]
        [Authorize]
        public async Task<IActionResult> PlaceLongTp([FromBody] PlaceReduceTpRequest req, CancellationToken ct)
        {
            var res = await _client.PlaceLongTakeProfitOrderAsync(req.Symbol, req.Quantity, req.Price, req.ForceMarket, ct);
            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "PlaceLongTPFailed");

            return Ok(new { orderId = res.OrderId });
        }

        /// <summary>PLACE REDUCE-ONLY TP FOR SHORT → returns orderId.</summary>
        [HttpPost("orders/tp/short")]
        [Authorize]
        public async Task<IActionResult> PlaceShortTp([FromBody] PlaceReduceTpRequest req, CancellationToken ct)
        {
            var res = await _client.PlaceShortTakeProfitOrderAsync(req.Symbol, req.Quantity, req.Price, req.ForceMarket, ct);
            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "PlaceShortTPFailed");

            return Ok(new { orderId = res.OrderId });
        }

        // =========================================================
        // POSITION: TRADING STOP (TP/SL/Trailing)
        // =========================================================

        /// <summary>SET TRADING STOP on POSITION (TP/SL/Trailing) → returns success.</summary>
        [HttpPost("positions/trading-stop")]
        [Authorize]
        public async Task<IActionResult> SetTradingStop([FromBody] SetTradingStopRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (req.TpslMode == StopLossTakeProfitMode.Full &&
               (req.TpOrderType == OrderType.Limit || req.SlOrderType == OrderType.Limit))
                return BadRequest("TradingStop Full supports only Market TP/SL.");
            if (req.TpslMode == StopLossTakeProfitMode.Partial)
            {
                if (req.TpOrderType == OrderType.Limit && req.TpLimitPrice is null)
                    return BadRequest("Partial+TP Limit requires TpLimitPrice.");
                if (req.SlOrderType == OrderType.Limit && req.SlLimitPrice is null)
                    return BadRequest("Partial+SL Limit requires SlLimitPrice.");
            }

            var res = await _client.SetTradingStopAsync(
                symbol: req.Symbol,
                priceScale: req.PriceScale,
                stopLoss: req.StopLoss,
                takeProfit: req.TakeProfit,
                trailingStop: req.TrailingStop,
                positionIdx: req.PositionIdx,
                activePrice: req.ActivePrice,
                takeProfitQuantity: req.TakeProfitQuantity,
                stopLossQuantity: req.StopLossQuantity,
                stopLossTakeProfitMode: req.TpslMode,
                tpOrderType: req.TpOrderType,
                slOrderType: req.SlOrderType,
                tpLimitPrice: req.TpLimitPrice,
                slLimitPrice: req.SlLimitPrice,
                cancel: ct);

            if (!res.Success)
                return Problem(detail: res.ErrorMessage, statusCode: 400, title: res.ErrorCode ?? "SetTradingStopFailed");

            return Ok(new { success = true });
        }

        // =========================================================
        // ACCOUNT / SYMBOLS / WALLET
        // =========================================================

        /// <summary>SET LEVERAGE for symbol.</summary>
        [HttpPost("account/leverage")]
        [Authorize]
        public async Task<IActionResult> SetLeverage([FromBody] SetLeverageRequest req, CancellationToken ct)
        {
            var symbolInfo = new SymbolInfo { Name = req.Symbol, MaxLeverage = req.MaxLeverage };
            var ok = await _client.SetLeverageAsync(symbolInfo, ct);
            return Ok(new { success = ok });
        }

        /// <summary>SET POSITION MODE (one-way / hedge) for symbol.</summary>
        [HttpPost("account/position-mode")]
        [Authorize]
        public async Task<IActionResult> SwitchPositionMode([FromBody] SwitchPositionModeRequest req, CancellationToken ct)
        {
            var ok = await _client.SwitchPositionModeAsync(req.Mode, req.Symbol, ct);
            return Ok(new { success = ok });
        }

        /// <summary>GET WALLET BALANCE (UTA) for configured quote asset.</summary>
        [HttpGet("wallet/balance")]
        [Authorize]
        public async Task<ActionResult<Strategies.Wallet.Balance>> GetBalance(CancellationToken ct)
        {
            var bal = await _client.GetBalancesAsync(ct);
            return Ok(bal);
        }

        // =========================================================
        // MARKET DATA / ORDERS / POSITIONS / FUNDING
        // =========================================================

        /// <summary>GET SYMBOL INFO with volume & volatility.</summary>
        [HttpGet("markets/symbol-info/{symbol}")]
        [Authorize]
        public async Task<ActionResult<SymbolInfo>> GetSymbolInfo([FromRoute] string symbol, CancellationToken ct)
        {
            var data = await _client.GetSymbolInfoAsync(symbol, ct);
            return Ok(data);
        }

        /// <summary>GET TICKER for a symbol.</summary>
        [HttpGet("markets/ticker/{symbol}")]
        [Authorize]
        public async Task<ActionResult<Ticker>> GetTicker([FromRoute] string symbol, CancellationToken ct)
        {
            var t = await _client.GetTickerAsync(symbol, ct);
            return Ok(t);
        }

        /// <summary>GET CLOSED KLINES (end = now - tf).</summary>
        [HttpGet("markets/klines/closed")]
        [Authorize]
        public async Task<ActionResult<Candle[]>> GetKlinesClosed(
            [FromQuery] string symbol,
            [FromQuery] TimeFrame interval,
            [FromQuery, Range(1, 2000)] int limit = 200,
            CancellationToken ct = default)
        {
            var data = await _client.GetKlinesClosedAsync(symbol, interval, limit, ct);
            return Ok(data);
        }

        /// <summary>GET KLINES BY TIME RANGE (UTC).</summary>
        [HttpGet("markets/klines/range")]
        [Authorize]
        public async Task<ActionResult<Candle[]>> GetKlinesRange(
            [FromQuery] string symbol,
            [FromQuery] TimeFrame interval,
            [FromQuery] DateTime start,
            [FromQuery] DateTime end,
            CancellationToken ct = default)
        {
            var data = await _client.GetKlinesAsync(symbol, interval, start, end, ct);
            return Ok(data);
        }

        /// <summary>GET OPEN ORDERS (with internal cursor paging).</summary>
        [HttpGet("orders")]
        [Authorize]
        public async Task<ActionResult<Order[]>> GetOrders(CancellationToken ct)
        {
            var data = await _client.GetOrdersAsync(ct);
            return Ok(data);
        }

        /// <summary>GET POSITIONS.</summary>
        [HttpGet("positions")]
        [Authorize]
        public async Task<ActionResult<Position[]>> GetPositions(CancellationToken ct)
        {
            var data = await _client.GetPositionsAsync(ct);
            return Ok(data);
        }

        /// <summary>GET FUNDING HISTORY for a symbol.</summary>
        [HttpGet("markets/funding/{symbol}")]
        [Authorize]
        public async Task<ActionResult<FundingRate[]>> GetFunding(
            [FromRoute] string symbol,
            [FromQuery] DateTime start,
            [FromQuery] DateTime end,
            CancellationToken ct)
        {
            var data = await _client.GetFundingRatesAsync(symbol, start, end, ct);
            return Ok(data);
        }

        // =========================================================
        // RAW (audyt/PnL)
        // =========================================================

        /// <summary>GET FEE RATE (raw from Bybit).</summary>
        [HttpGet("account/fee-rate")]
        [Authorize]
        public async Task<IActionResult> GetFeeRate([FromQuery] string? symbol, CancellationToken ct)
        {
            var data = await _client.GetFeeRatesRawAsync(symbol, ct);
            return Ok(data);
        }

        /// <summary>GET TRANSACTION LOG (raw).</summary>
        [HttpGet("account/transactions")]
        [Authorize]
        public async Task<IActionResult> GetTransactionLog(
            [FromQuery] AccountType accountType = AccountType.Unified,
            [FromQuery] DateTime? start = null,
            [FromQuery] DateTime? end = null,
            [FromQuery] string? cursor = null,
            CancellationToken ct = default)
        {
            var data = await _client.GetTransactionLogRawAsync(accountType, start, end, cursor, ct);
            return Ok(data);
        }

        /// <summary>GET ORDER HISTORY (raw).</summary>
        [HttpGet("orders/history")]
        [Authorize]
        public async Task<IActionResult> GetOrderHistory([FromQuery] string? symbol, [FromQuery] string? cursor, CancellationToken ct)
        {
            var data = await _client.GetOrderHistoryRawAsync(symbol, cursor, ct);
            return Ok(data);
        }

        /// <summary>GET CLOSED PNL (raw).</summary>
        [HttpGet("positions/closed-pnl")]
        [Authorize]
        public async Task<IActionResult> GetClosedPnl(
            [FromQuery] string? symbol,
            [FromQuery] DateTime? start,
            [FromQuery] DateTime? end,
            [FromQuery] string? cursor,
            CancellationToken ct)
        {
            var data = await _client.GetClosedPnlRawAsync(symbol, start, end, cursor, ct);
            return Ok(data);
        }
    }

    // ===============================================================
    // DTOs (enumy serializowane jako stringi)
    // ===============================================================

    public sealed class PlaceOrderRequest
    {
        [Required, MinLength(1)] public string Symbol { get; set; } = default!;
        [JsonConverter(typeof(JsonStringEnumConverter))] public Bybit.Net.Enums.OrderSide Side { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public NewOrderType Type { get; set; }
        [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")] public decimal Quantity { get; set; }
        [Range(typeof(decimal), "0.00000000", "79228162514264337593543950335")] public decimal? Price { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))] public PositionIdx? PositionIdx { get; set; } = null;
        [JsonConverter(typeof(JsonStringEnumConverter))] public TimeInForce? TimeInForce { get; set; } = null;
        public bool ReduceOnly { get; set; } = false;
        public bool CloseOnTrigger { get; set; } = false;

        // Conditional
        public decimal? TriggerPrice { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public TriggerType? TriggerBy { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public TriggerDirection? TriggerDirection { get; set; }

        // TP/SL w create
        public decimal? TakeProfit { get; set; }
        public decimal? StopLoss { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public TriggerType? TpTriggerBy { get; set; } = TriggerType.MarkPrice;
        [JsonConverter(typeof(JsonStringEnumConverter))] public TriggerType? SlTriggerBy { get; set; } = TriggerType.MarkPrice;
        [JsonConverter(typeof(JsonStringEnumConverter))] public StopLossTakeProfitMode? TpslMode { get; set; }

        // tylko create-order
        [JsonConverter(typeof(JsonStringEnumConverter))] public OrderType? TpOrderType { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public OrderType? SlOrderType { get; set; }
        public decimal? TpLimitPrice { get; set; }
        public decimal? SlLimitPrice { get; set; }

        /// <summary>Twój clientOrderId (orderLinkId) – do idempotencji i amend/cancel.</summary>
        public string? ClientOrderId { get; set; }
    }

    public sealed class AmendOrderRequest
    {
        [Required, MinLength(1)] public string Symbol { get; set; } = default!;
        public string? OrderId { get; set; }
        public string? ClientOrderId { get; set; }
        public decimal? Price { get; set; }
        public decimal? Quantity { get; set; }

        public decimal? TakeProfit { get; set; }
        public decimal? StopLoss { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public TriggerType? TpTriggerBy { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public TriggerType? SlTriggerBy { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public StopLossTakeProfitMode? TpslMode { get; set; }
        public decimal? TpLimitPrice { get; set; }
        public decimal? SlLimitPrice { get; set; }
    }

    public sealed class PlaceReduceTpRequest
    {
        [Required, MinLength(1)] public string Symbol { get; set; } = default!;
        [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")] public decimal Quantity { get; set; }
        [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")] public decimal Price { get; set; }
        /// <summary>Gdy true – MARKET (agresywnie). Gdy false – LIMIT + PostOnly.</summary>
        public bool ForceMarket { get; set; } = false;
    }

    public sealed class SetTradingStopRequest
    {
        [Required, MinLength(1)] public string Symbol { get; set; } = default!;
        /// <summary>Tylko dla awaryjnego fallbacku trailing; docelowo użyj tickSize.</summary>
        public decimal PriceScale { get; set; } = 2m;

        public decimal StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public decimal? TrailingStop { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))] public PositionIdx PositionIdx { get; set; }
        public decimal? ActivePrice { get; set; }
        public decimal? TakeProfitQuantity { get; set; }
        public decimal? StopLossQuantity { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public StopLossTakeProfitMode? TpslMode { get; set; } = StopLossTakeProfitMode.Full;

        // Partial – opcjonalnie limitowe TP/SL
        [JsonConverter(typeof(JsonStringEnumConverter))] public OrderType? TpOrderType { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))] public OrderType? SlOrderType { get; set; }
        public decimal? TpLimitPrice { get; set; }
        public decimal? SlLimitPrice { get; set; }
    }

    public sealed class SetLeverageRequest
    {
        [Required, MinLength(1)] public string Symbol { get; set; } = default!;
        [Range(typeof(decimal), "1", "125")] public decimal MaxLeverage { get; set; }
    }

    public sealed class SwitchPositionModeRequest
    {
        [Required, MinLength(1)] public string Symbol { get; set; } = default!;
        [JsonConverter(typeof(JsonStringEnumConverter))] public Models.PositionMode Mode { get; set; }
    }
}
