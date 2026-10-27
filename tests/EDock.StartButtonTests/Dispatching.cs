namespace Microsoft.UI.Dispatching;
public class DispatcherQueue
{
    public DispatcherQueueTimer CreateTimer() => new();
    public bool TryEnqueue(Action action) { action(); return true; }
}
public class DispatcherQueueTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; }
    public bool IsRunning { get; private set; }
    public event Action<DispatcherQueueTimer, object>? Tick;
    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;
    public void Fire() { if (IsRunning) Tick?.Invoke(this, EventArgs.Empty); }
}
