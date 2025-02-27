namespace CryptoBlade.Configuration
{
    public class ExchangeAccount
    {
        public string Name { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public Exchange Exchange { get; set; } = Exchange.Bybit;
        public bool IsDemo { get; set; }

        public bool HasApiCredentials()
        {
            return !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
        }
    }
}