<#
.SYNOPSIS
Packages a published EDock directory as a verified portable ZIP.
.EXAMPLE
./packaging/package-portable.ps1 -PublishDirectory ./publish/win-x64 -OutputDirectory ./artifacts
.EXAMPLE
./packaging/package-portable.ps1 -PublishDirectory ./publish/lite-win-x64 -OutputDirectory ./artifacts -Lite
.NOTES
Publish first. -Lite labels and validates a framework-dependent build; it does
not remove runtime DLLs from a full build. Requires Windows PowerShell 5.1 or later.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$Lite
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath.TrimEnd([IO.Path]::DirectorySeparatorChar)
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$outputPath = [IO.Path]::GetFullPath($outputPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ($outputPath.Equals($publishPath, [StringComparison]::OrdinalIgnoreCase) -or
    $outputPath.StartsWith($publishPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be outside PublishDirectory.'
}
foreach ($name in @('EDock.exe', 'EDock.dll', 'EDock.deps.json', 'EDock.runtimeconfig.json', 'EDock.pri', 'App.xbf', 'Assets/EDock.ico', 'NativeThirdPartyNotices.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) { throw "Missing published file: $name" }
}
$exeVersion = (Get-Item -LiteralPath (Join-Path $publishPath 'EDock.exe')).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = ($exeVersion -split '\+', 2)[0] }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$') {
    throw 'Version must be a safe version number, for example 0.16.9 or 0.17.0-beta.1.'
}
if (($exeVersion -split '\+', 2)[0] -cne $Version) { throw "Version does not match EDock.exe ($exeVersion)." }

$runtime = Get-Content -LiteralPath (Join-Path $publishPath 'EDock.runtimeconfig.json') -Raw | ConvertFrom-Json
$hasSharedFramework = $null -ne $runtime.runtimeOptions.framework -or $null -ne $runtime.runtimeOptions.frameworks
if ($Lite -and !$hasSharedFramework) { throw '-Lite requires a framework-dependent publish.' }
if (!$Lite -and $hasSharedFramework) { throw 'A framework-dependent publish must be packaged with -Lite.' }
if ($Lite) {
    foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        if (Test-Path -LiteralPath (Join-Path $publishPath $name)) { throw "A lite publish must not bundle runtime implementation: $name" }
    }
    foreach ($name in @('Microsoft.WindowsAppRuntime.Bootstrap.dll', 'Microsoft.WindowsAppRuntime.Bootstrap.Net.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) { throw "Missing lite bootstrap file: $name" }
    }
} else {
    foreach ($name in @('coreclr.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) { throw "Missing bundled runtime: $name" }
    }
}

$suffix = 'portable'
if ($Lite) { $suffix += '-lite' }
$baseName = "EDock-$Version-$suffix-win-x64"
$archivePath = Join-Path $outputPath ($baseName + '.zip')
$checksumPath = Join-Path $outputPath ("EDock-$Version-$suffix-SHA256.txt")
foreach ($path in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to replace an existing output: $path" }
}

function Test-ExcludedPath([string]$RelativePath) {
    $path = $RelativePath.Replace('\', '/')
    return $path -match '(?i)(^|/)(Shortcuts|bin|obj|\.git|\.vs|\.idea)(/|$)' -or
        $path -match '(?i)(^|/)(settings\.json(?:\..*)?|error\.log|\.env(?:\..*)?|Thumbs\.db|\.DS_Store)$' -or
        $path -match '(?i)\.(pdb|user|suo|cache|lnk|url|zip|7z|log)$'
}
function Copy-PackageFile([string]$SourcePath, [string]$DestinationPath) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($DestinationPath)) | Out-Null
    [IO.File]::Copy($SourcePath, $DestinationPath, $false)
}
$utf8 = [Text.UTF8Encoding]::new($true)
# Keep temporary payload paths short for Windows PowerShell 5.1/.NET Framework.
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workingPath = Join-Path $temporaryParent ('EDockPackage-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$stagePath = Join-Path $workingPath $baseName
$temporaryArchive = Join-Path $workingPath 'package.zip'
$temporaryChecksum = Join-Path $workingPath 'SHA256.txt'
$skipped = @()
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
[IO.Directory]::CreateDirectory($stagePath) | Out-Null
try {
    $entries = @(Get-ChildItem -LiteralPath $publishPath -Recurse -Force)
    foreach ($entry in $entries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "PublishDirectory contains a link or junction: $($entry.Name)" }
    }
    foreach ($file in $entries | Where-Object { !$_.PSIsContainer }) {
        $relative = $file.FullName.Substring($publishPath.Length + 1)
        if (Test-ExcludedPath $relative) { $skipped += $relative; continue }
        Copy-PackageFile $file.FullName (Join-Path $stagePath $relative)
    }

    # A prepared distribution may already contain licenses. For a raw publish,
    # use the repository's Licenses directory beside the packaging directory.
    $licensesPath = Join-Path $stagePath 'Licenses'
    if (!(Test-Path -LiteralPath $licensesPath -PathType Container)) {
        $repositoryLicenses = Join-Path (Split-Path -Parent $PSScriptRoot) 'Licenses'
        if (!(Test-Path -LiteralPath $repositoryLicenses -PathType Container)) {
            throw 'Add dependency Licenses to PublishDirectory, or run this script from the repository packaging directory.'
        }
        foreach ($file in Get-ChildItem -LiteralPath $repositoryLicenses -File -Recurse -Force) {
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'License source contains a link.' }
            $relative = $file.FullName.Substring($repositoryLicenses.Length + 1)
            if (Test-ExcludedPath $relative) { continue }
            Copy-PackageFile $file.FullName (Join-Path $licensesPath $relative)
        }
    }
    if (@(Get-ChildItem -LiteralPath $licensesPath -File -Recurse).Count -eq 0) { throw 'Dependency Licenses must not be empty.' }

    $instructionsPath = Join-Path $stagePath '使用说明.txt'
    if (!(Test-Path -LiteralPath $instructionsPath -PathType Leaf)) {
        $runtimeNote = '完整版已附带运行环境，无需另装 .NET 或 Windows App Runtime。'
        if ($Lite) {
            $framework = @($runtime.runtimeOptions.framework) + @($runtime.runtimeOptions.frameworks)
            $framework = @($framework | Where-Object { $null -ne $_ })
            $dotnetLine = ($framework | ForEach-Object { "$($_.name) $($_.version) x64 或兼容补丁版本" }) -join '；'
            $dependencies = Get-Content -LiteralPath (Join-Path $publishPath 'EDock.deps.json') -Raw | ConvertFrom-Json
            $sdk = @($dependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Microsoft.WindowsAppSDK/' })
            if ($sdk.Count -ne 1) { throw 'Could not identify the Windows App Runtime dependency.' }
            $sdkVersion = ($sdk[0] -split '/', 2)[1]
            $sdkParts = $sdkVersion -split '\.'
            $sdkFamily = $sdkParts[0] + '.' + $sdkParts[1]
            $runtimeNote = @"
精简版需要已有 $dotnetLine，以及 Windows App Runtime $sdkFamily x64。
.NET 下载：https://dotnet.microsoft.com/download/dotnet
本构建对应的 Windows App Runtime 安装程序：
https://aka.ms/windowsappsdk/$sdkFamily/$sdkVersion/windowsappruntimeinstall-x64.exe
如果不希望补装运行环境，可使用 portable 完整版。
"@
        }
        $instructions = @"
EDock $Version · Windows 11 x64 · 免安装

1. 完整解压到固定文件夹，保留所有附属文件，不能在压缩包内直接运行。
2. 若 EDock 已在运行，先从系统托盘退出，再打开本文件夹的 EDock.exe。
3. 右键系统托盘的 EDock 图标可打开设置或退出。
4. 左右侧边栏均可关闭，需要时从托盘设置重新启用。

$runtimeNote

个人设置与导入的快捷方式保存在 %LOCALAPPDATA%\EDock，与其他版本共用。
免安装版的升级方式：退出 EDock，备份整个配置目录，再解压新版并运行。
移动程序位置后，如使用开机启动，请在新位置重新开关一次该选项。
本包不包含个人设置、快捷方式或背景图片。
第三方组件许可见 Licenses 和 NativeThirdPartyNotices.txt。
项目：https://github.com/yuexialiaoluan/EDock
"@
        [IO.File]::WriteAllText($instructionsPath, $instructions, $utf8)
    }

    $expected = @{}
    $totalBytes = 0L
    foreach ($file in Get-ChildItem -LiteralPath $stagePath -File -Recurse -Force) {
        $relative = $file.FullName.Substring($stagePath.Length + 1).Replace('\', '/')
        if (Test-ExcludedPath $relative) { throw "Unexpected staged file: $relative" }
        $expected[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $totalBytes += $file.Length
    }
    # Explicit entry names also use ZIP-standard forward slashes on .NET Framework.
    $createdArchive = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $expected.Keys | Sort-Object) {
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($createdArchive,
                (Join-Path $stagePath $relative), ($baseName + '/' + $relative), [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $createdArchive.Dispose() }
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryArchive)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix = $baseName + '/'
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            if (!$entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'ZIP root mismatch.' }
            $relative = $entry.FullName.Substring($prefix.Length)
            if (!$expected.ContainsKey($relative) -or !$seen.Add($relative)) { throw "Unexpected or duplicate ZIP entry: $relative" }
            if (Test-ExcludedPath $relative) { throw "Excluded file entered ZIP: $relative" }
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $actualHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $algorithm.Dispose() }
            if ($actualHash -cne $expected[$relative]) { throw "ZIP file hash mismatch: $relative" }
        }
    } finally { $archive.Dispose() }
    if ($seen.Count -ne $expected.Count) { throw 'ZIP file count mismatch.' }
    $checksum = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($temporaryChecksum, $checksum + '  ' + [IO.Path]::GetFileName($archivePath) + "`r`n", [Text.UTF8Encoding]::new($false))
    # File.Move refuses to overwrite, including files created during packaging.
    [IO.File]::Move($temporaryArchive, $archivePath)
    [IO.File]::Move($temporaryChecksum, $checksumPath)
    [pscustomobject]@{
        Version = $Version; Lite = [bool]$Lite; Archive = $archivePath; Hashes = $checksumPath
        SizeBytes = (Get-Item -LiteralPath $archivePath).Length
        SizeMiB = [Math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 2)
        SHA256 = $checksum; FileCount = $seen.Count; UncompressedBytes = $totalBytes
        EveryArchivedFileHashMatched = $true; ExcludedFiles = $skipped
    }
} finally {
    # Delete only the unique temporary folder created by this invocation.
    $resolvedWorking = [IO.Path]::GetFullPath($workingPath)
    $expectedParent = $temporaryParent
    if ([IO.Path]::GetDirectoryName($resolvedWorking).Equals($expectedParent, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedWorking).StartsWith('EDockPackage-', [StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $resolvedWorking -PathType Container)) {
        Remove-Item -LiteralPath $resolvedWorking -Recurse -Force
    }
}
