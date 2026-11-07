using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Documents;

namespace YHG_Assets_Handler
{
    internal readonly record struct LineState(
        string Text, StatusType Type, DateTime Time, double? Progress, string? LinkPath, bool IsRaw);
    public sealed class ConsoleLine : IStatusLine
    {
        private readonly StatusConsole _owner;
        private readonly object _gate = new();
        private string _text;
        private StatusType _type;
        private double? _progress;
        internal int Queued;
        internal Paragraph? Paragraph;
        internal volatile bool Removed;
        internal DateTime Time { get; }
        internal string? LinkPath { get; }
        internal bool IsRaw { get; }
        internal ConsoleLine(StatusConsole owner, string text, StatusType type,
                             string? linkPath = null, bool isRaw = false)
        {
            _owner = owner; _text = text; _type = type;
            LinkPath = linkPath; IsRaw = isRaw; Time = DateTime.Now;
        }
        public bool IsAlive => !Removed;
        internal StatusType Type { get { lock (_gate) return _type; } }
        internal LineState Snapshot()
        {
            lock (_gate) return new LineState(_text, _type, Time, _progress, LinkPath, IsRaw);
        }
        public void Update(string message, StatusType? type = null)
        {
            lock (_gate) { _text = message ?? ""; _progress = null; if (type.HasValue) _type = type.Value; }
            _owner.Touch(this);
        }
        public void SetType(StatusType type)
        {
            lock (_gate) _type = type;
            _owner.Touch(this);
        }
        public void SetProgress(double percent, string? message = null)
        {
            lock (_gate)
            {
                _progress = Math.Clamp(percent, 0, 100);
                if (message != null) _text = message;
            }
            _owner.Touch(this);
        }
        public void Complete(string? message = null, StatusType type = StatusType.Success)
        {
            lock (_gate) { _progress = null; if (message != null) _text = message; _type = type; }
            _owner.Touch(this);
        }
        public IProgress<double> AsProgress(string? message = null) => new LineProgress(this, message);
        internal string ToPlainText(bool withTime)
        {
            var s = Snapshot();
            var sb = new StringBuilder();
            if (withTime) sb.Append('[').Append(s.Time.ToString("HH:mm:ss")).Append("] ");
            if (!s.IsRaw)
            {
                string pre = StatusConsole.PrefixFor(s.Type);
                if (pre.Length > 0) sb.Append(pre).Append(' ');
            }
            sb.Append(s.Text);
            if (s.LinkPath != null) sb.Append(s.LinkPath);
            if (s.Progress is double p) sb.Append($"  {p:0}%");
            return sb.ToString();
        }
        private sealed class LineProgress : IProgress<double>
        {
            private readonly ConsoleLine _line; private readonly string? _msg;
            public LineProgress(ConsoleLine line, string? msg) { _line = line; _msg = msg; }
            public void Report(double value) => _line.SetProgress(value, _msg);
        }
    }
}
