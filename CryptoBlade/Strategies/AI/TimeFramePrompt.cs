using Azure;
using CryptoBlade.Models;
using OpenAI.Chat;
using Skender.Stock.Indicators;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CryptoBlade.Strategies.AI
{
    public class TimeFramePrompt
    {
        private readonly TimeFrame _timeFrame;
        private readonly StringBuilder _sb = new();
        private readonly int _numeration;
        private readonly int _priceScale;
        private readonly string _priceFormat;
        private readonly CultureInfo _invariant = CultureInfo.InvariantCulture;
        public TimeFramePrompt(SymbolInfo symbolInfo, TimeFrame timeFrame, int numeration)
        {
            _timeFrame = timeFrame;
            _numeration = numeration;
            _priceScale = (int)symbolInfo.PriceScale;
            _priceFormat = $"F{_priceScale}";
        }

        public UserChatMessage CreateUserMessage(IEnumerable<Quote> candles, int takeCandlesCount, List<string> indicators, List<string> pivots)
        {
            string tf = TimeFrameHelper.GetAbbreviation(_timeFrame);
            _sb.AppendLine($"{_numeration}. Timeframe: {tf}");

            AddCandles(candles.Take(takeCandlesCount));
            AddIndicators(candles, indicators);
            AddPivots(candles, pivots);

            var content = _sb.ToString();
            return new UserChatMessage(content);
        }

        private void AddCandles(IEnumerable<Quote> candles)
        {
            _sb.AppendLine($"{_numeration}.1. Candles");
            foreach (var candle in candles)
            {
                _sb.Append($"{candle.Date:MMddHHmm},");
                _sb.Append($"{Math.Round(candle.Open, _priceScale).ToString(_priceFormat, _invariant)},");
                _sb.Append($"{Math.Round(candle.High, _priceScale).ToString(_priceFormat, _invariant)},");
                _sb.Append($"{Math.Round(candle.Low, _priceScale).ToString(_priceFormat, _invariant)},");
                _sb.Append($"{Math.Round(candle.Close, _priceScale).ToString(_priceFormat, _invariant)},");
                _sb.Append($"{candle.Volume.ToString("F0", _invariant)};");
            }
            _sb.AppendLine();
        }

        private void AddIndicators(IEnumerable<Quote> candles, List<string> indicators)
        {
            _sb.AppendLine($"{_numeration}.2. Indicators");

            foreach (var indicator in indicators)
            {
                IndicatorRequest indicatorRequest = IndicatorRequest.Parse(indicator);
                var value = IndicatorEngine.Compute(indicatorRequest, candles);
                string fv = IndicatorEngine.Format(value, _priceScale);

                if (string.IsNullOrWhiteSpace(fv) || fv.Trim('0', '.', '-') == string.Empty)
                    fv = "N/A";

                _sb.AppendLine($"{indicator}={fv}");
            }
        }

        private void AddPivots(IEnumerable<Quote> candles, List<string> pivots)
        {
            _sb.AppendLine($"{_numeration}.2. Pivots");

            foreach (var pivot in pivots)
            {
                PivotRequest pivotRequest = PivotRequest.Parse(pivot);
                var values = PivotEngine.Compute(pivotRequest, candles)
                    .Where(z => !string.IsNullOrWhiteSpace(z.PointType))
                    .TakeLast(10)
                    .Select(z =>
                        $"{z.Date:MMddHHmm}," +
                        $"{Convert.ToDecimal(z.ZigZag ?? 0).ToString(_priceFormat, _invariant)}," +
                        $"{z.PointType}");

                _sb.AppendLine($"{pivot}={string.Join(';', values)}");
            }
        }
    }
}
