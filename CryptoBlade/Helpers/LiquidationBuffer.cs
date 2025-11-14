using CryptoBlade.Models;

namespace CryptoBlade.Helpers
{
    public sealed class LiquidationBuffer
    {
        private readonly LinkedList<LiquidationEvent> _q = new();
        private readonly TimeSpan _window;

        public LiquidationBuffer(TimeSpan window) => _window = window;

        public void Add(LiquidationEvent liq)
        {
            _q.AddLast(liq);
            Trim(liq.Timestamp - _window);
        }

        private void Trim(DateTime threshold)
        {
            while (_q.First != null && _q.First.Value.Timestamp < threshold)
                _q.RemoveFirst();
        }

        public IReadOnlyList<LiquidationEvent> Snapshot(DateTime nowUtc)
        {
            Trim(nowUtc - _window);
            return _q.ToArray();
        }
    }
}
