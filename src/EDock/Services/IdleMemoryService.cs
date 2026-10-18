using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

/// <summary>Gives cold resident pages back to Windows once per fully idle period.</summary>
internal sealed class IdleMemoryService : IDisposable
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<bool> _canReclaim;
    private bool _reclaimed;
    private bool _disposed;

    public IdleMemoryService(DispatcherQueue dispatcher, Func<bool> canReclaim)
    {
        _canReclaim = canReclaim;
        _timer = dispatcher.CreateTimer();
        // Dock resources are already unloaded before this grace period starts.
        // Let pending XAML/composition work settle before returning resident pages.
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.IsRepeating = false;
        _timer.Tick += OnIdle;
    }

    public void NotifyActivity()
    {
        if (_disposed) return;
        _timer.Stop();
        _reclaimed = false;
    }

    public void Update()
    {
        if (_disposed) return;
        if (!_canReclaim()) { NotifyActivity(); return; }
        if (!_reclaimed && !_timer.IsRunning) _timer.Start();
    }

    private void OnIdle(DispatcherQueueTimer sender, object args)
    {
        if (_disposed) return;
        if (!_canReclaim()) { NotifyActivity(); return; }
        // Never poll or retry a trim: active timers/framework code will fault
        // their small hot set back in naturally. Private commit is unaffected.
        _reclaimed = true;
        EmptyWorkingSet(GetCurrentProcess());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnIdle;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("psapi.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint process);
}
