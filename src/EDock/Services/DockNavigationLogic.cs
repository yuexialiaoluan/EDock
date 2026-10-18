namespace EDock.Services;

public readonly record struct DockNavigationCell(double X, double Y);

/// <summary>Moves along the rendered row or column, including repeated scroll copies.</summary>
public static class DockNavigationLogic
{
    public static int Neighbor(IReadOnlyList<DockNavigationCell> cells, int current, int direction, bool horizontal)
    {
        if (cells.Count == 0) return -1;
        if (current < 0 || current >= cells.Count) return 0;
        var origin = cells[current];
        double forwardDistance = double.PositiveInfinity, wrapDistance = 0;
        int forward = current, wrap = current;
        int sign = direction < 0 ? -1 : 1;
        for (int i = 0; i < cells.Count; i++)
        {
            double cross = horizontal ? cells[i].Y - origin.Y : cells[i].X - origin.X;
            if (Math.Abs(cross) > .5) continue;
            double along = sign * (horizontal ? cells[i].X - origin.X : cells[i].Y - origin.Y);
            if (along > .5 && along < forwardDistance) { forward = i; forwardDistance = along; }
            if (along < wrapDistance) { wrap = i; wrapDistance = along; }
        }
        return double.IsPositiveInfinity(forwardDistance) ? wrap : forward;
    }
}
