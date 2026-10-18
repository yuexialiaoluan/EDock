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
$native = Join-Path $publish 'Native/EDock.Taskbar.0.14.3.dll'
if ((Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ne '381DC6A0A87C69FB4303572AF0E517BA05D6422AB0DAA205B73CA1268173E801') {
    throw 'Native helper changed: review it and give the new helper an immutable versioned filename.'
}
if (@(Get-ChildItem -LiteralPath (Join-Path $publish 'Native') -File).Count -ne 1) { throw 'Unexpected Native payload.' }
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
