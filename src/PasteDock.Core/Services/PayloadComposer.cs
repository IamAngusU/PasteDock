using System.Text;
using PasteDock.Core.Models;

namespace PasteDock.Core.Services;

public static class PayloadComposer
{
    public static ClipPayload Combine(IReadOnlyList<ClipPayload> payloads, string separator, bool plainText,
        long maxBytes = Limits.MaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        var builder = new Composition(payloads.Count, separator, plainText, maxBytes);
        foreach (var payload in payloads) builder.Add(payload);
        return builder.Build();
    }

    /// <summary>Load one source at a time and reject oversized output before allocating a joined string.</summary>
    public static async Task<ClipPayload> CombineAsync(IReadOnlyList<Guid> ids,
        Func<Guid, CancellationToken, Task<ClipPayload>> load, string separator, bool plainText,
        CancellationToken ct = default, long maxBytes = Limits.MaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(load);
        ct.ThrowIfCancellationRequested();
        var builder = new Composition(ids.Count, separator, plainText, maxBytes);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var payload = await load(id, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            builder.Add(payload);
        }
        ct.ThrowIfCancellationRequested();
        return builder.Build();
    }

    private sealed class Composition
    {
        private readonly int _count;
        private readonly string _separator;
        private readonly bool _plain;
        private readonly long _limit;
        private readonly int _separatorBytes;
        private readonly List<string> _parts = [];
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _orderedFiles = [];
        private ClipPayload? _single;
        private bool? _fileMode;
        private long _bytes;

        public Composition(int count, string separator, bool plain, long maxBytes)
        {
            if (count == 0) throw new InvalidOperationException("Select an item first.");
            ArgumentNullException.ThrowIfNull(separator);
            if (maxBytes <= 0 || maxBytes > Limits.MaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            _count = count; _separator = separator; _plain = plain; _limit = maxBytes;
            _separatorBytes = Encoding.UTF8.GetByteCount(separator);
        }

        public void Add(ClipPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            payload.Validate();
            if (_count == 1)
            {
                _single = _plain ? new ClipPayload { Text = payload.GetPlainText() } : payload;
                _single.Validate(_limit);
                return;
            }
            var isFiles = !_plain && payload.Files.Length > 0;
            _fileMode ??= isFiles;
            if (isFiles != _fileMode.Value || (!_plain && !isFiles && payload.Png is not null))
                throw new InvalidOperationException("Mixed formats cannot be pasted together. Use the paste queue instead.");
            if (isFiles)
            {
                foreach (var file in payload.Files)
                {
                    if (!_files.Add(file)) continue;
                    if (_files.Count > 4096) throw new InvalidDataException("The combined selection exceeds 4,096 file references.");
                    CountBytes(Encoding.UTF8.GetByteCount(file));
                    _orderedFiles.Add(file);
                }
            }
            else
            {
                var text = payload.GetPlainText();
                CountBytes(checked((long)Encoding.UTF8.GetByteCount(text) + (_parts.Count > 0 ? _separatorBytes : 0)));
                // Retain text only, not image/rich-format payloads that will not be part of the result.
                _parts.Add(text);
            }
        }

        private void CountBytes(long bytes)
        {
            _bytes = checked(_bytes + bytes);
            if (_bytes > _limit)
                throw new InvalidDataException("The combined selection exceeds the clipboard size limit. Use a smaller selection or the paste queue.");
        }

        public ClipPayload Build()
        {
            var result = _single ?? (_fileMode == true
                ? new ClipPayload { Files = _orderedFiles.ToArray() }
                : new ClipPayload { Text = string.Join(_separator, _parts) });
            result.Validate(_limit);
            return result;
        }
    }
}
