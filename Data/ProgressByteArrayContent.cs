using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MusikArchivApp.Data
{
    internal sealed class ProgressByteArrayContent : HttpContent
    {
        private const int ChunkSize = 81_920;

        private readonly byte[] content;
        private readonly string mediaType;
        private readonly Action<long, long, double?> progress;

        public ProgressByteArrayContent(byte[] content, string mediaType, Action<long, long, double?> progress)
        {
            this.content = content;
            this.mediaType = mediaType;
            this.progress = progress;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var total = content.Length;
            long sent = 0;
            var stopwatch = Stopwatch.StartNew();

            for (var offset = 0; offset < total; offset += ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = Math.Min(ChunkSize, total - offset);
                await stream.WriteAsync(content.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                sent += count;

                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                var rate = elapsedSeconds > 0.05 ? sent / elapsedSeconds : (double?)null;
                progress(sent, total, rate);
            }
        }
    }
}
