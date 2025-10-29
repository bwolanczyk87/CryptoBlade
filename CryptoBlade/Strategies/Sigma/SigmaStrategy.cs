using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;
        public override string Name => "Sigma";
        protected override bool UseMarketOrdersForEntries => false;

        public SigmaStrategy(
            IOptions<SigmaStrategyOptions> options,
            IOptions<TradingBotOptions> botOptions,
            string symbol, 
            IWalletManager walletManager, 
            ICbFuturesRestClient restClient) : base(
                options, 
                botOptions, 
                symbol, 
                GetRequiredTimeFrames(), 
                walletManager, 
                restClient)
        {
            _options = options;
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames()
        {
            return [
                new TimeFrameWindow(TimeFrame.OneMinute, 60, true)
            ];
        }

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            List<StrategyIndicator> indicators = [];

            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;

            var signal = new SignalEvaluation(hasBuySignal, hasSellSignal, hasBuyExtraSignal, hasSellExtraSignal, [.. indicators]);
            return Task.FromResult(signal);
        }
    }
}
