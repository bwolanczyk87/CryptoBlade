using Bybit.Net.Enums;
using CryptoBlade.Exchanges;

namespace CryptoBlade.Strategies.Wallet {
public readonly record struct Balance(
        decimal? Equity,
        decimal? WalletBalance,
        decimal? UnrealizedPnl,
        decimal? RealizedPnl);

public interface IWalletManager
    {
        Balance Contract { get; }

        Task StartAsync(CancellationToken cancel);

        Task StopAsync(CancellationToken cancel);
    }

public class NullWalletManager : IWalletManager
    {
        public Balance Contract { get; } = new Balance();

        public Task StartAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }
    }

public class WalletManager : IWalletManager
    {
        private readonly ICbFuturesRestClient m_restClient;
        private readonly ICbFuturesSocketClient m_socketClient;
        private IUpdateSubscription? m_walletSubscription;
        private CancellationTokenSource? m_cancellationTokenSource;
        private readonly ILogger<WalletManager> m_logger;
        private Task? m_initTask;

        public WalletManager(ILogger<WalletManager> logger,
            ICbFuturesRestClient restClient,
            ICbFuturesSocketClient socketClient)
        {
            m_restClient = restClient;
            m_socketClient = socketClient;
            m_logger = logger;
            m_cancellationTokenSource = new CancellationTokenSource();
        }

        public Balance Contract { get; private set; }

        public Task StartAsync(CancellationToken cancel)
        {
            m_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            m_initTask = Task.Run(async () =>
            {
                var subscription = await m_socketClient.SubscribeToWalletUpdatesAsync(OnWalletUpdate, m_cancellationTokenSource.Token);
                subscription.AutoReconnect(m_logger);
                m_walletSubscription = subscription;

                Contract = await m_restClient.GetBalancesAsync(cancel);

            }, cancel);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancel)
        {
            var walletSubscription = m_walletSubscription;
            if (walletSubscription != null)
                await walletSubscription.CloseAsync();
            m_walletSubscription = null;
            m_cancellationTokenSource?.Cancel();
            m_cancellationTokenSource?.Dispose();
        }

        private void OnWalletUpdate(Balance obj)
        {
            Contract = obj;
        }
    }

public enum WalletType
    {
        Contract,
    }
}
