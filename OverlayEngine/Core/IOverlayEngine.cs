using NoIDSoftwork.OverlayEngine.Configuration;
using NoIDSoftwork.OverlayEngine.Diagnostics;
using NoIDSoftwork.OverlayEngine.Models;

namespace NoIDSoftwork.OverlayEngine.Core;

public interface IOverlayEngine : IDisposable
{
    OverlayEngineState State { get; }
    OverlayOptions Options { get; }
    TargetWindowInfo TargetWindow { get; }

    event EventHandler<TargetWindowChangedEventArgs>? TargetWindowChanged;
    event EventHandler<OverlayDiagnosticEvent>? DiagnosticEvent;

    void Initialize(OverlayOptions? options = null);
    void UpdateOptions(OverlayOptions options);
    bool AttachToProcess(uint processId);
    void Detach();
    void Show();
    void Hide();
    void UpdateFrame(OverlayFrame frame);
    IReadOnlyList<OverlayDiagnosticEvent> GetRecentDiagnostics(int maxCount = 100);
}
