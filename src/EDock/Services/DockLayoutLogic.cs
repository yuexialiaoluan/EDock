using EDock.Models;

namespace EDock.Services;

public readonly record struct FooterLayout(double BottomMargin, double BottomSpace, double ViewportHeight);

/// <summary>Measures the normal grid and its footer in logical pixels.</summary>
public static class DockLayoutLogic
{
    public static double PreferredWidth(DockOptions options) => Math.Max(options.DockWidth,
        12 + options.NormalColumns * (options.ShortcutDisplayMode == "Text" ? 60 : options.IconSize + 12)
            + (options.NormalColumns - 1) * options.IconSpacing);

    public static double NormalWidth(DockOptions options, double availableWidth) => Math.Min(PreferredWidth(options), availableWidth);

    public static double ColumnWidth(DockOptions options, double availableWidth) => Math.Max(1,
        (NormalWidth(options, availableWidth) - 12 - (options.NormalColumns - 1) * EffectiveSpacing(options, availableWidth)) / options.NormalColumns);

    public static double EffectiveSpacing(DockOptions options, double availableWidth) =>
        Math.Min(options.IconSpacing, Math.Max(0, (NormalWidth(options, availableWidth) - 12 - options.NormalColumns) / Math.Max(1, options.NormalColumns - 1)));

    public static double IconHeight(DockOptions options, double columnWidth) => options.ShortcutDisplayMode switch
    {
        "Text" => 0,
        "Wide" => Math.Clamp((columnWidth - 12) * .64, 42, 190) * options.IconSize / 40,
        _ => Math.Min(options.IconSize, Math.Max(1, columnWidth - 8))
    };

    // A text row has no icon slot and remains readable when icon labels are disabled.
    public static double TileHeight(DockOptions options, double columnWidth) => options.ShortcutDisplayMode == "Text"
        ? 40 : IconHeight(options, columnWidth) + 14 + (options.ShowShortcutNames ? (options.ShortcutDisplayMode == "Wide" ? 22 : 19) : 0);

    public static int FooterColumns(double width) => width - 12 >= 188 ? 4 : width - 12 >= 92 ? 2 : 1;

    public static FooterLayout Footer(DockOptions options, double width, double height, double tileHeight)
    {
        int rows = 4 / FooterColumns(width);
        double bottom = 12 + Math.Min(0, options.FooterOffset);
        double available = Math.Max(1, height - 62 - (rows * 36 + (rows - 1) * 4 + 10 + bottom));
        double space = Math.Clamp(options.FooterOffset, 0, Math.Max(0, available - tileHeight));
        return new FooterLayout(bottom, space, Math.Max(1, available - space));
    }

    // Fill the final row of a repeating grid with its following logical items.
    // A complete period ends on both a row boundary and the original first item.
    public static int CycleItemCount(int itemCount, int columns, bool repeating)
    {
        if (itemCount <= 0) return 0;
        if (!repeating) return itemCount;
        int a = itemCount, b = columns;
        while (b != 0) (a, b) = (b, a % b);
        return checked(itemCount / a * columns);
    }
}
