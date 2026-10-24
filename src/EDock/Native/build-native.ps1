param(
    [Parameter(Mandatory = $true)][string]$ZigPath,
    [string]$Output = (Join-Path $PSScriptRoot 'EDock.Taskbar.0.14.5.dll')
)
$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $ZigPath).Path
$source = Join-Path $PSScriptRoot 'TaskbarAppearance.cpp'
$destination = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($destination)) | Out-Null
# Zig 0.14.1 supplies the Windows ABI headers and links its C++ runtime statically.
# No compiler/toolchain files or extra runtime process are required at runtime.
& $compiler c++ -target x86_64-windows-gnu -shared -O2 -std=c++20 `
    -fms-extensions -fno-rtti -Wno-dll-attribute-on-redeclaration `
    $source -o $destination `
    -lole32 -loleaut32 -luuid -luser32 -lkernel32 -ladvapi32 `
    -lapi-ms-win-core-winrt-l1-1-0 -lapi-ms-win-core-winrt-string-l1-1-0
if ($LASTEXITCODE -ne 0) { throw "EDock taskbar helper compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $destination | Select-Object FullName, Length
