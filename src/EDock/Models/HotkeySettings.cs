namespace EDock.Models;

public sealed class HotkeySettings
{
    public string Previous { get; set; } = "W";
    public string Next { get; set; } = "S";
    public string PreviousColumn { get; set; } = "A";
    public string NextColumn { get; set; } = "D";
    public string PreviousList { get; set; } = "Q";
    public string NextList { get; set; } = "E";
    public string ToggleExpanded { get; set; } = "F";
    public string TogglePinned { get; set; } = "G";
    public string SwitchDock { get; set; } = "Tab";
    public string Launch { get; set; } = "Space";

    public HotkeySettings Copy() => new()
    {
        Previous = Previous, Next = Next, PreviousColumn = PreviousColumn, NextColumn = NextColumn,
        PreviousList = PreviousList, NextList = NextList, ToggleExpanded = ToggleExpanded,
        TogglePinned = TogglePinned, SwitchDock = SwitchDock, Launch = Launch
    };

    internal void Normalize()
    {
        Previous = NormalizeKey(Previous, "W"); Next = NormalizeKey(Next, "S");
        PreviousColumn = NormalizeKey(PreviousColumn, "A"); NextColumn = NormalizeKey(NextColumn, "D");
        PreviousList = NormalizeKey(PreviousList, "Q"); NextList = NormalizeKey(NextList, "E");
        ToggleExpanded = NormalizeKey(ToggleExpanded, "F"); TogglePinned = NormalizeKey(TogglePinned, "G");
        SwitchDock = NormalizeKey(SwitchDock, "Tab"); Launch = NormalizeKey(Launch, "Space");
    }

    public IEnumerable<string> Conflicts()
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Pairs())
        {
            if (string.IsNullOrEmpty(pair.Value)) continue;
            if (!values.TryGetValue(pair.Value, out var names)) values[pair.Value] = names = new();
            names.Add(pair.Key);
        }
        return values.Values.Where(names => names.Count > 1).Select(names => string.Join("、", names));
    }

    public IEnumerable<KeyValuePair<string, string>> Pairs()
    {
        yield return new("上下向前", Previous); yield return new("上下向后", Next);
        yield return new("向左列", PreviousColumn); yield return new("向右列", NextColumn);
        yield return new("上一个列表", PreviousList); yield return new("下一个列表", NextList);
        yield return new("展开边栏", ToggleExpanded); yield return new("固定边栏", TogglePinned);
        yield return new("切换边栏", SwitchDock); yield return new("打开项目", Launch);
    }

    private static string NormalizeKey(string? value, string fallback)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Equals("关闭", StringComparison.OrdinalIgnoreCase) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return value.Length is >= 1 and <= 24 ? value : fallback;
    }
}
