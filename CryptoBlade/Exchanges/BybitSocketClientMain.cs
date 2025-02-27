using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

namespace CryptoBlade.Exchanges
{
    public class BybitSocketClientMain : BybitSocketClient, IBybitSocketClientMain
    {
        public BybitSocketClientMain(Action<BybitSocketOptions> optionsDelegate) : base(optionsDelegate) { }
    }
}
