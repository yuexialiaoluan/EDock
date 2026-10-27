[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][Alias('CompilerPath')][string]$InnoSetupCompiler,
    [string]$LicenseDirectory
)
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\', '/')
$compiler = (Resolve-Path -LiteralPath $InnoSetupCompiler).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.TrimEnd('\', '/') -ieq $publish -or $output.StartsWith($publish + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be outside PublishDirectory to avoid recursively copying the build stage.'
}
$version = (Get-Item -LiteralPath (Join-Path $publish 'EDock.exe')).VersionInfo.ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a release EDock ProductVersion.' }
$runtime = Get-Content -LiteralPath (Join-Path $publish 'EDock.runtimeconfig.json') -Raw | ConvertFrom-Json
if (@($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -like '8.0.*' }).Count -ne 1) {
    throw 'Installer requires a self-contained .NET 8 publish.'
}
foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'EDock.pri', 'App.xbf', 'Assets/EDock.ico', 'NativeThirdPartyNotices.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) { throw "Missing distribution file: $name" }
}
$nativeDirectory = Join-Path $publish 'Native'
$nativeHelpers = [ordered]@{
    'EDock.Taskbar.0.14.5.dll' = 'DDA83CAE80E3AF91A21AF47A087247A47C369585FF39236E49758B664B90BE4D'
    'EDock.StartMenu.0.16.15.dll' = '2748270C47FCF2C61E2F2E40E066D2186C52FC2C515CDC8BB30A33B44AC8FD5B'
    'EDock.StartButton.0.16.18.1.dll' = 'BCC3F35BD07F6B4912348D0B60664CD9A352DD99FB9A8C79F810CDA2ACD5CEC8'
}
if (-not (Test-Path -LiteralPath $nativeDirectory -PathType Container)) {
    throw 'Missing Native directory: the installer requires the taskbar, Start menu and Start button helpers.'
}
foreach ($name in $nativeHelpers.Keys) {
    $native = Join-Path $nativeDirectory $name
    if (-not (Test-Path -LiteralPath $native -PathType Leaf)) { throw "Missing required Native helper: $name" }
    if ((Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ne $nativeHelpers[$name]) {
        throw "Native helper checksum mismatch: $name. Restore the verified helper, or review the change and assign a new immutable versioned filename."
    }
}
foreach ($entry in Get-ChildItem -LiteralPath $nativeDirectory -Force) {
    if ($entry.PSIsContainer -or -not $nativeHelpers.Contains($entry.Name)) {
        throw "Unexpected Native payload: $($entry.Name). Only the three verified EDock Native helpers are allowed."
    }
}
# Component-based Windows App SDK publishes omit the old AI/ML/Widgets files.
# Upgrades remove only these exact known program files. Refuse to build if a
# future release needs one again, before it can delete a required dependency.
$retiredComponents = Join-Path $PSScriptRoot 'retired-sdk-components.iss'
foreach ($line in Get-Content -LiteralPath $retiredComponents) {
    $text = $line.Trim()
    if (-not $text -or $text.StartsWith(';') -or $text -eq '[InstallDelete]') { continue }
    if ($text -notmatch '^Type: (files|dirifempty); Name: "\{app\}\\([A-Za-z0-9_.-]+(?:\\[A-Za-z0-9_.-]+)?)"$') {
        throw "Unsafe retired component cleanup entry: $text"
    }
    $relative = $Matches[2]
    if ($relative -match '(^|\\)\.\.?($|\\)' -or $relative -match '^(Native|Shortcuts|StartButtonImages)(\\|$)') {
        throw "Unsafe retired component cleanup path: $relative"
    }
    if (Test-Path -LiteralPath (Join-Path $publish $relative)) {
        throw "Retired SDK component is required by this publish: $relative. Remove its cleanup entry before packaging."
    }
}
if (-not $LicenseDirectory) {
    if (Test-Path -LiteralPath (Join-Path $publish 'Licenses') -PathType Container) {
        $LicenseDirectory = Join-Path $publish 'Licenses'
    } else {
        $LicenseDirectory = Join-Path $PSScriptRoot '../Licenses'
    }
}
$licenses = (Resolve-Path -LiteralPath $LicenseDirectory).Path
foreach ($name in @('dotnet-LICENSE.txt', 'dotnet-THIRD-PARTY-NOTICES.txt', 'WindowsAppSDK-LICENSE.txt', 'WindowsAppSDK-NOTICE.txt', 'README.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $licenses $name) -PathType Leaf)) { throw "Missing bundled dependency notice: $name" }
}
$baseName = "EDock-$version-setup-win-x64"
$installer = Join-Path $output ($baseName + '.exe')
$hashFile = Join-Path $output ("EDock-$version-setup-SHA256.txt")
foreach ($path in @($installer, $hashFile)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to replace an existing release: $path" }
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$stage = Join-Path $output ('.installer-build-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse -Force) {
    $relative = $file.FullName.Substring($publish.Length + 1)
    if ($file.Extension -ieq '.pdb' -or $relative -match '(^|[\\/])(settings\.json|Shortcuts|bin|obj|\.git)([\\/]|$)') {
        throw "Unexpected private or build file in publish: $relative"
    }
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($env:USERPROFILE) {
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            if ($encoding.GetString($bytes).IndexOf($env:USERPROFILE, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Personal build path in distribution: $relative"
            }
        }
    }
    $target = Join-Path $stage $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::Copy($file.FullName, $target, $false)
}
$licenseStage = Join-Path $stage 'Licenses'
[IO.Directory]::CreateDirectory($licenseStage) | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $licenses -File -Recurse -Force) {
    $relative = $file.FullName.Substring($licenses.Length + 1)
    $target = Join-Path $licenseStage $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::Copy($file.FullName, $target, $true)
}
$utf8 = [Text.UTF8Encoding]::new($true)
$information = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-info.txt') -Raw
[IO.File]::WriteAllText((Join-Path $stage '使用说明.txt'), "EDock $version`r`n`r`n" + $information, $utf8)
$inventory = @(Get-ChildItem -LiteralPath $stage -File -Recurse -Force | ForEach-Object {
    [ordered]@{ Path = $_.FullName.Substring($stage.Length + 1); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$compilerLog = Join-Path $output ($baseName + '-compile.log')
& $compiler "/DPublishDirectory=$stage" "/DOutputDirectory=$output" (Join-Path $PSScriptRoot 'EDock.iss') 2>&1 | Tee-Object -FilePath $compilerLog
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Inno Setup compilation failed. See $compilerLog" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($hashFile, "$hash  $baseName.exe`r`n", $utf8)
$report = [ordered]@{
    Version = $version
    Installer = $installer
    Bytes = (Get-Item -LiteralPath $installer).Length
    SHA256 = $hash
    Compiler = $compiler
    CompilerVersion = (Get-Item -LiteralPath $compiler).VersionInfo.ProductVersion
    Stage = $stage
    PayloadFileCount = $inventory.Count
    Payload = $inventory
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output ($baseName + '-build-report.json')) -Encoding utf8
Write-Output "Created $installer"
