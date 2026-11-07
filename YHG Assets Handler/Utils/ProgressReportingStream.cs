using System;
using System.IO;

namespace YHG_Assets_Handler.Utils
{
    public class ProgressReportingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly Action<long> _onProgress;
        private long _bytesProcessed;
        private long _lastReported;

        public ProgressReportingStream(Stream baseStream, Action<long> onProgress)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _onProgress = onProgress ?? throw new ArgumentNullException(nameof(onProgress));
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _baseStream.Length;

        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public override void Flush() => _baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _baseStream.Read(buffer, offset, count);
            if (read > 0)
            {
                _bytesProcessed += read;
                ReportIfNeeded();
            }
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
            _bytesProcessed += count;
            ReportIfNeeded();
        }

        private void ReportIfNeeded()
        {
            // Throttles progress reporting every 256 KB to mitigate UI thread saturation
            if (_bytesProcessed - _lastReported >= 262144 || _bytesProcessed == Length)
            {
                _lastReported = _bytesProcessed;
                _onProgress(_bytesProcessed);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => _baseStream.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _baseStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}