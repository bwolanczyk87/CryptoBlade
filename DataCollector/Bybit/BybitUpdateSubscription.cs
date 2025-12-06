using CryptoExchange.Net.Objects.Sockets;

namespace DataCollector.Bybit
{
    public interface IBybitUpdateSubscription
    {
        void AutoReconnect();
        Task CloseAsync();
    }

    public class BybitUpdateSubscription(UpdateSubscription updateSubscription) : IBybitUpdateSubscription
    {
        public void AutoReconnect()
        {
            updateSubscription.ReconnectAsync();
        }

        public async Task CloseAsync()
        {
            await updateSubscription.CloseAsync();
        }
    }
}