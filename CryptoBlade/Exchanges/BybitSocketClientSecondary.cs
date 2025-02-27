using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

namespace CryptoBlade.Exchanges
{
    public class BybitSocketClientSecondary : BybitSocketClient, IBybitSocketClientSecondary
    {
        public BybitSocketClientSecondary(Action<BybitSocketOptions> optionsDelegate) : base(optionsDelegate) { }
    }
}
