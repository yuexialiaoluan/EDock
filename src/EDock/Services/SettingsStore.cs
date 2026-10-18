using System;
using System.IO;
using System.Security.AccessControl;
using System.Text.Json;
using EDock.Models;

namespace EDock.Services;

public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Action<string, string> _replaceFile;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public SettingsStore(string? directory = null)
        : this(directory, static (source, destination) => File.Replace(source, destination, null))
    {
    }

    internal SettingsStore(string? directory, Action<string, string> replaceFile)
    {
        ArgumentNullException.ThrowIfNull(replaceFile);
        _replaceFile = replaceFile;
        _directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EDock"));
        SettingsFilePath = Path.Combine(_directory, "settings.json");
    }

    public string SettingsFilePath { get; }
    public string? LastError { get; private set; }

    public DockSettings Load()
    {
        lock (_gate)
        {
            LastError = null;
            try
            {
                using var stream = new FileStream(SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var settings = JsonSerializer.Deserialize<DockSettings>(stream, JsonOptions)
                    ?? throw new JsonException("The settings document is empty.");
                settings.Normalize();
                return settings;
            }
            catch (FileNotFoundException)
            {
                return new DockSettings();
            }
            catch (DirectoryNotFoundException)
            {
                return new DockSettings();
            }
            catch (JsonException ex)
            {
                BackupCorruptSettings(ex);
                return new DockSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = $"无法读取设置文件：{ex.Message}\n{SettingsFilePath}";
                return new DockSettings();
            }
        }
    }

    public void Save(DockSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            LastError = null;
            var temporaryPath = Path.Combine(_directory, $"settings.{Guid.NewGuid():N}.tmp");
            var stage = "检查设置内容";
            try
            {
                settings.Normalize();
                stage = "创建设置目录";
                Directory.CreateDirectory(_directory);
                // Clone encrypted files before writing so EFS recipients and alternate
                // data streams survive. Only our new temporary copy is truncated.
                bool clonedEncrypted = OperatingSystem.IsWindows()
                    && File.Exists(SettingsFilePath) && IsEncryptedSettingsTarget();
                if (clonedEncrypted)
                {
                    stage = "复制加密设置到临时文件";
                    File.Copy(SettingsFilePath, temporaryPath, overwrite: false);
                }
                stage = "创建并写入临时设置文件";
                using (var stream = new FileStream(temporaryPath,
                    clonedEncrypted ? FileMode.Truncate : FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, settings, JsonOptions);
                    stage = "刷新临时设置文件";
                    stream.Flush(flushToDisk: true);
                }
                // Both names reside in the same directory, so replacement is atomic.
                if (File.Exists(SettingsFilePath))
                {
                    stage = "原子替换现有设置文件";
                    ReplaceExistingSettings(temporaryPath);
                }
                else
                {
                    stage = "提交首次设置文件";
                    File.Move(temporaryPath, SettingsFilePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or NotSupportedException or ArgumentException)
            {
                LastError = $"无法保存设置（{stage}，0x{ex.HResult:X8}）：{ex.Message}\n{SettingsFilePath}";
                throw new IOException(LastError, ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LastError ??= $"设置已保存，但临时文件无法清理：{ex.Message}\n{temporaryPath}";
                }
            }
        }
    }

    private void ReplaceExistingSettings(string temporaryPath)
    {
        try
        {
            _replaceFile(temporaryPath, SettingsFilePath);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && (ex is UnauthorizedAccessException
                || ex is IOException && ex.HResult == unchecked((int)0x80071777))
            && IsEncryptedReplaceCandidate(temporaryPath))
        {
            // ReplaceFile can fail while merging EFS metadata even when both files are
            // encrypted and readable/writable (including ERROR_FILE_NOT_ENCRYPTED).
            // Keep EFS and the original access rules, then commit
            // with an atomic same-directory rename instead of modifying the live file.
            var previous = new FileInfo(SettingsFilePath);
            var replacement = new FileInfo(temporaryPath);
            replacement.SetAccessControl(previous.GetAccessControl(AccessControlSections.Access));
            replacement.CreationTimeUtc = previous.CreationTimeUtc;
            replacement.Attributes = previous.Attributes;
            if ((replacement.Attributes & FileAttributes.Encrypted) == 0)
                throw new IOException("加密设置文件的替代副本未保持加密；未提交设置。");
            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
        }
    }

    private bool IsEncryptedReplaceCandidate(string temporaryPath)
    {
        var replacement = File.GetAttributes(temporaryPath);
        return IsEncryptedSettingsTarget() && (replacement & FileAttributes.Encrypted) != 0;
    }

    private bool IsEncryptedSettingsTarget()
    {
        var previous = File.GetAttributes(SettingsFilePath);
        const FileAttributes unsafeAttributes = FileAttributes.ReadOnly | FileAttributes.ReparsePoint;
        return (previous & FileAttributes.Encrypted) != 0
            && (previous & unsafeAttributes) == 0;
    }

    private void BackupCorruptSettings(JsonException error)
    {
        var backup = Path.Combine(_directory, $"settings.corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.{Guid.NewGuid():N}.json");
        try
        {
            File.Move(SettingsFilePath, backup);
            LastError = $"设置文件损坏，已使用默认设置。原文件已备份到：{backup}\n{error.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"设置文件损坏，已使用默认设置；原文件备份失败：{ex.Message}\n{SettingsFilePath}";
        }
    }
}
