namespace EDock.Services;

public static class DockScrollLogic
{
    /// <summary>Returns the equivalent point in a middle copy of a continuous list.</summary>
    public static double Recenter(double offset, double cycleHeight)
    {
        if (!double.IsFinite(cycleHeight) || cycleHeight <= 0 || !double.IsFinite(offset)) return 0;
        double phase = offset % cycleHeight;
        if (phase < 0) phase += cycleHeight;
        return cycleHeight + phase;
    }

    /// <summary>Time-based easing keeps rapid wheel input continuous at any frame rate.</summary>
    public static double AnimateOffset(double current, double target, double elapsedMs)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return 0;
        if (!double.IsFinite(elapsedMs) || elapsedMs <= 0) return current;
        double next = current + (target - current) * (1 - Math.Exp(-Math.Min(elapsedMs, 64) / 55));
        return Math.Abs(target - next) < .1 ? target : next;
    }

    // Reach the boundary first; the next wheel movement continues at the other end.
    public static double NextOffset(double offset, double maximum, double delta, bool loop)
    {
        if (!double.IsFinite(maximum) || maximum <= 0 || !double.IsFinite(delta)) return 0;
        offset = double.IsFinite(offset) ? Math.Clamp(offset, 0, maximum) : 0;
        if (loop && delta > 0 && offset >= maximum - .5) return 0;
        if (loop && delta < 0 && offset <= .5) return maximum;
        return Math.Clamp(offset + delta, 0, maximum);
    }
}

/// <summary>A small, damped movement for lists that cannot fill their viewport.</summary>
public sealed class DockScrollBounce
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public bool IsRunning => Math.Abs(Value) >= .05 || Math.Abs(Velocity) >= .5;

    public void Push(double scrollDelta)
    {
        if (!double.IsFinite(scrollDelta)) return;
        Velocity = Math.Clamp(Velocity - scrollDelta * 2.8, -420, 420);
    }

    public void Update(double elapsedMs)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs <= 0) return;
        // Integrate in small steps so a delayed UI frame cannot destabilize the spring.
        double remaining = Math.Min(elapsedMs, 64) / 1000;
        while (remaining > 0)
        {
            double step = Math.Min(remaining, .008);
            Velocity += (-170 * Value - 26 * Velocity) * step;
            Value = Math.Clamp(Value + Velocity * step, -18, 18);
            remaining -= step;
        }
        if (!IsRunning) Reset();
    }

    public void Reset() { Value = 0; Velocity = 0; }
}
