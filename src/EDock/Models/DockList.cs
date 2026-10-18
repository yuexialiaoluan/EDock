using System;
using System.Collections.Generic;

namespace EDock.Models;

public enum ShortcutSortKey
{
    Manual,
    Name,
    Added,
    Modified,
    Size,
    Type
}

/// <summary>A named, independently ordered collection on one side of the screen.</summary>
public sealed class DockList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "默认列表";
    public List<ShortcutItem> Shortcuts { get; set; } = new();
    public ShortcutSortKey SortKey { get; set; } = ShortcutSortKey.Manual;
    public bool SortDescending { get; set; }
}
