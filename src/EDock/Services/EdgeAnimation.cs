namespace EDock.Services;

internal enum EdgeAnimationStyle
{
    Slide,
    Reveal,
    None
}

/// <summary>A reversible animation using elapsed time, independent of the dispatcher frame rate.</summary>
internal sealed class EdgeAnimation
{
    private double _from;
    private double _to;
    private long _started;
    private int _durationMs;

    public double Value { get; private set; }
    public bool IsRunning { get; private set; }

    public static int ClampDuration(int durationMs) => Math.Clamp(durationMs, 80, 1500);

    public static EdgeAnimationStyle ParseStyle(string? style) => style?.ToUpperInvariant() switch
    {
        "REVEAL" => EdgeAnimationStyle.Reveal,
        "NONE" => EdgeAnimationStyle.None,
        _ => EdgeAnimationStyle.Slide
    };

    public void Start(bool revealed, int durationMs, bool animate, long now)
    {
        Update(now);
        _from = Value;
        _to = revealed ? 1 : 0;
        _durationMs = ClampDuration(durationMs);
        _started = now;
        IsRunning = animate && _from != _to;
        if (!IsRunning) Value = _to;
    }

    public void Finish()
    {
        Value = _to;
        IsRunning = false;
    }

    public bool Update(long now)
    {
        if (!IsRunning) return false;
        double progress = Math.Clamp((now - _started) / (double)_durationMs, 0, 1);
        // Smooth acceleration and deceleration give both directions a visible glide.
        // This is an app animation; Windows does not publish a taskbar duration.
        double eased = progress * progress * (3 - 2 * progress);
        Value = _from + (_to - _from) * eased;
        if (progress >= 1) Finish();
        return true;
    }
}
