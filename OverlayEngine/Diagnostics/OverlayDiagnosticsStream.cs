using System.Collections.Concurrent;

namespace NoIDSoftwork.OverlayEngine.Diagnostics;

internal sealed class OverlayDiagnosticsStream
{
    private const int MaxEntries = 256;
    private readonly ConcurrentQueue<OverlayDiagnosticEvent> _events = new();

    public event EventHandler<OverlayDiagnosticEvent>? EventLogged;

    public void Log(OverlayDiagnosticLevel level, string category, string message, Exception? exception = null)
    {
        var entry = new OverlayDiagnosticEvent
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Exception = exception
        };

        _events.Enqueue(entry);
        while (_events.Count > MaxEntries && _events.TryDequeue(out _))
        {
        }

        EventLogged?.Invoke(this, entry);
    }

    public IReadOnlyList<OverlayDiagnosticEvent> GetRecent(int maxCount)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<OverlayDiagnosticEvent>();
        }

        var snapshot = _events.ToArray();
        if (snapshot.Length <= maxCount)
        {
            return snapshot;
        }

        int skip = snapshot.Length - maxCount;
        return snapshot.Skip(skip).ToArray();
    }
}
