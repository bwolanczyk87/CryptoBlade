namespace CryptoBlade.Exchanges.Interfaces
{
    public interface IUpdateSubscription
    {
        void AutoReconnect(ILogger logger);
        Task CloseAsync();
    }
}