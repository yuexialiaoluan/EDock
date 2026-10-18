using System;

namespace EDock.Models;

public sealed class ShortcutItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    // Original import path is provenance only; launching always uses the private copy.
    public string? ImportedShortcutPath { get; set; }
    // Set only when a desktop shortcut is transferred into managed storage.
    public string? ImportedDesktopShortcutPath { get; set; }
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public string CustomIconPath { get; set; } = string.Empty;
    public string ImageCrop { get; set; } = "Center";
    // Leave missing dates unknown when reading configurations from older versions.
    public DateTimeOffset? AddedAtUtc { get; set; }
}
