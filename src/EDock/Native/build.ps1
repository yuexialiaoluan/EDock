param([string]$ZigPath, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (-not $ZigPath) {
    $localZig = Join-Path $PSScriptRoot '../../../../../work/v12-native-tools/zig-x86_64-windows-0.14.1/zig.exe'
    $installedZig = Get-Command zig -ErrorAction SilentlyContinue
    $ZigPath = if (Test-Path -LiteralPath $localZig) { $localZig } elseif ($installedZig) { $installedZig.Source } else { throw '请安装 Zig 0.14.1，或通过 -ZigPath 指定 zig.exe。' }
}
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'EDock.Taskbar.0.14.5.dll' }
& (Join-Path $PSScriptRoot 'build-native.ps1') -ZigPath $ZigPath -Output $OutputPath
