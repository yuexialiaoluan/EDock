param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Publish)
$ErrorActionPreference = 'Stop'
$localSdk = Join-Path $PSScriptRoot '../../work/dotnet/dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if (Test-Path $localSdk) { (Resolve-Path $localSdk).Path } elseif ($dotnetCommand) { $dotnetCommand.Source } else { throw '请安装 .NET 8 SDK 或更新版本。' }
$env:DOTNET_ROOT = Split-Path $dotnet
$project = Join-Path $PSScriptRoot 'src/EDock/EDock.csproj'
if ($Publish) {
    & $dotnet publish $project -c $Configuration -p:Platform=x64 -r win-x64 --self-contained true -o (Join-Path $PSScriptRoot 'publish/win-x64')
} else {
    & $dotnet build $project -c $Configuration -p:Platform=x64
}
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出代码 $LASTEXITCODE" }
