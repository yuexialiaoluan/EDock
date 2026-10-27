param(
    [Parameter(Mandatory = $true)][string]$ZigPath,
    [string]$Output = (Join-Path $PSScriptRoot 'EDock.StartButton.0.16.18.1.dll'),
    [switch]$Probe,
    [int]$ProbeNumber = 1
)
$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $ZigPath).Path
$destination = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$extra = @()
if ($Probe) { $extra += "-DEDOCK_STARTBUTTON_PROBE=$ProbeNumber" }
& $compiler c++ -target x86_64-windows-gnu -shared -O2 -std=c++20 `
    -fms-extensions -fno-rtti -Wno-dll-attribute-on-redeclaration @extra `
    (Join-Path $PSScriptRoot 'StartButtonAppearance.cpp') -o $destination `
    -lole32 -loleaut32 -luuid -luser32 -lkernel32 -ladvapi32 -lshcore `
    -lapi-ms-win-core-winrt-l1-1-0 -lapi-ms-win-core-winrt-string-l1-1-0
if ($LASTEXITCODE -ne 0) { throw "EDock Start button helper compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $destination | Select-Object FullName, Length
