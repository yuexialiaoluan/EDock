param(
    [Parameter(Mandatory = $true)][string]$ZigPath,
    [string]$Output = (Join-Path $PSScriptRoot 'EDock.StartMenu.0.16.15.dll')
)
$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $ZigPath).Path
$source = Join-Path $PSScriptRoot 'StartMenuAppearance.cpp'
$destination = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
& $compiler c++ -target x86_64-windows-gnu -shared -O2 -std=c++20 `
    -fms-extensions -fno-rtti -Wno-dll-attribute-on-redeclaration `
    $source -o $destination `
    -lole32 -loleaut32 -luuid -luser32 -lkernel32 -ladvapi32 `
    -lapi-ms-win-core-winrt-l1-1-0 -lapi-ms-win-core-winrt-string-l1-1-0
if ($LASTEXITCODE -ne 0) { throw "EDock Start menu helper compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $destination | Select-Object FullName, Length
