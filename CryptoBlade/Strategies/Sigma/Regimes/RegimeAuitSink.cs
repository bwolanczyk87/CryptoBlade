// FileRegimeAuditSink.cs
using System.Collections.Concurrent;
using System.Text;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public interface IRegimeAuditSink
    {
        void Add(RegimeAuditRecord r);
        IReadOnlyList<RegimeAuditRecord> Snapshot();
        void Clear();
    }

    public sealed class RegimeAuditSink : IRegimeAuditSink
    {
        private readonly string _path;
        private readonly ConcurrentQueue<RegimeAuditRecord> _q = new();
        private static readonly object _fileLock = new();

        public RegimeAuditSink(string path)
        {
            _path = path;
            EnsureHeader();
        }

        public void Add(RegimeAuditRecord r)
        {
            _q.Enqueue(r);
            AppendRow(r);
        }

        public IReadOnlyList<RegimeAuditRecord> Snapshot()
        {
            var list = new List<RegimeAuditRecord>(_q.Count);
            foreach (var r in _q) list.Add(r);
            return list;
        }

        public void Clear()
        {
            while (_q.TryDequeue(out _)) { }
            // Pliku nie czyścimy (to log historyczny). Jeśli chcesz rotację — daj znać.
        }

        private void EnsureHeader()
        {
            lock (_fileLock)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
                {
                    File.AppendAllText(_path, RegimeAuditCsv.Header() + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
        }

        private void AppendRow(RegimeAuditRecord r)
        {
            lock (_fileLock)
            {
                File.AppendAllText(_path, RegimeAuditCsv.Row(r) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }
}
