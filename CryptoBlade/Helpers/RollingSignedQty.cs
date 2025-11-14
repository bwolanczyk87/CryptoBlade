namespace CryptoBlade.Helpers
{
    public sealed class RollingSignedQty
    {
        private readonly LinkedList<(DateTime ts, decimal qty)> _q = new();
        private decimal _sum;
        private readonly TimeSpan _window;

        public RollingSignedQty(TimeSpan window) => _window = window;

        public void Add(DateTime ts, decimal signedQty)
        {
            _q.AddLast((ts, signedQty));
            _sum += signedQty;
            Trim(ts - _window);
        }

        private void Trim(DateTime threshold)
        {
            while (_q.First != null && _q.First.Value.ts < threshold)
            {
                var old = _q.First.Value;
                _q.RemoveFirst();
                _sum -= old.qty;
            }
        }

        public decimal Delta(DateTime nowUtc)
        {
            Trim(nowUtc - _window);
            return _sum;
        }
    }
}
