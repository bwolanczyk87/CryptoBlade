using CryptoExchange.Net.Objects.Sockets;

namespace CryptoBlade.Exchanges
{
    public class BybitUpdateSubscription : IUpdateSubscription
    {
        private readonly UpdateSubscription m_subscription;

        public BybitUpdateSubscription(UpdateSubscription subscription)
        {
            m_subscription = subscription;
        }

        public void AutoReconnect(ILogger logger)
        {
            m_subscription.ReconnectAsync();
        }

        public async Task CloseAsync()
        {
            await m_subscription.CloseAsync();
        }
    }
}