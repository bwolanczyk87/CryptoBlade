using System.Collections.Concurrent;
using System.Text;

namespace CryptoBlade.Strategies.Sigma.Audit
{
    public interface ISigmaAuditSink
    {
        void Add(SigmaAuditRecord r);
        IReadOnlyList<SigmaAuditRecord> Snapshot();
        void Clear();
    }

    public sealed class SigmaAuditSink : ISigmaAuditSink
    {
        private readonly string _path;
        private readonly ConcurrentQueue<SigmaAuditRecord> _q = new();
        private static readonly object _fileLock = new();

        public SigmaAuditSink(string path)
        {
            _path = path;
            EnsureHeader();
        }

        public void Add(SigmaAuditRecord r)
        {
            _q.Enqueue(r);
            AppendRow(r);
        }

        public IReadOnlyList<SigmaAuditRecord> Snapshot()
        {
            var list = new List<SigmaAuditRecord>(_q.Count);
            foreach (var r in _q) list.Add(r);
            return list;
        }

        public void Clear()
        {
            while (_q.TryDequeue(out _)) { }
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
                    File.AppendAllText(
                        _path,
                        SigmaAuditCsv.Header() + Environment.NewLine,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
        }

        private void AppendRow(SigmaAuditRecord r)
        {
            lock (_fileLock)
            {
                File.AppendAllText(
                    _path,
                    SigmaAuditCsv.Row(r) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }
}
