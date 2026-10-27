param(
    [string]$WorkspacePath = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path,
    [string]$Exe
)
$ErrorActionPreference = 'Stop'
. (Join-Path $WorkspacePath 'work/v10-ui-common.ps1')
. (Join-Path $WorkspacePath 'work/v08-ui-functions.ps1')
if (!$Exe) { $Exe = Join-Path $WorkspacePath 'outputs/EDock/src/EDock/bin/x64/Release/net8.0-windows10.0.26100.0/win-x64/EDock.exe' }
$profilePath = Join-Path $WorkspacePath ('work/v11-image-close-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $profilePath
$settingsPath = Join-Path $profilePath 'settings.json'
@{
    LeftEnabled=$true; RightEnabled=$false; AutoHide=$false; SeparateSideSettings=$false
    BackgroundStyle='Solid'; BackgroundImagePath=''
} | ConvertTo-Json | Set-Content $settingsPath -Encoding utf8
foreach ($name in @('one','two','three')) {
    $bitmap = [Drawing.Bitmap]::new(80,80)
    try { $bitmap.Save((Join-Path $profilePath "$name.png"),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}
$process = Start-Process -FilePath $Exe -ArgumentList @('--profile',('"'+$profilePath+'"'),'--settings') -WindowStyle Hidden -PassThru
$AppProcessId = $process.Id
$report = [ordered]@{}
function Settings { Get-Content $settingsPath -Raw | ConvertFrom-Json }
function OpenSettings {
    InvokeName (Root 'EDock · 左侧') '设置'
    Start-Sleep -Milliseconds 250
    Root 'EDock 设置'
}
function InputPattern($root) {
    $inputElement = Find $root '背景图片' ([System.Windows.Automation.ValuePattern]::Pattern)
    [System.Windows.Automation.ValuePattern]$inputElement.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
}
function CloseSettings($handle) {
    $null = [E7]::PostMessage($handle,0x0010,[UIntPtr]::Zero,[IntPtr]::Zero)
}
function WaitClosed($handle) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ([E7]::IsWindow($handle) -and $timer.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 25 }
    Assert (![E7]::IsWindow($handle)) 'Settings failed to close after validating its pending image.'
}
try {
    Start-Sleep -Milliseconds 1800
    Assert (!$process.HasExited) 'Isolated app failed to start.'
    $r = Root 'EDock 设置'
    Combo $r '背景材质' '图片背景'
    $inputPattern = InputPattern $r
    $handle = Window 'EDock 设置'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $inputPattern.SetValue((Join-Path $profilePath 'one.png'))
    CloseSettings $handle
    $closeRequestedMs = $watch.ElapsedMilliseconds
    WaitClosed $handle
    Assert ($closeRequestedMs -lt 250) 'Test did not close inside the image debounce window.'
    Assert ((Settings).BackgroundImagePath -eq (Join-Path $profilePath 'one.png')) 'Immediate close lost the valid image.'
    Assert ((Settings).BackgroundStyle -eq 'Image') 'Immediate close lost the image style.'
    $report.ValidImageImmediateClose = $true
    $report.CloseRequestedMs = $closeRequestedMs

    $r = OpenSettings
    $inputPattern = InputPattern $r
    $handle = Window 'EDock 设置'
    $inputPattern.SetValue((Join-Path $profilePath 'two.png'))
    $inputPattern.SetValue((Join-Path $profilePath 'missing.png'))
    CloseSettings $handle
    WaitClosed $handle
    Assert ((Settings).BackgroundImagePath -eq (Join-Path $profilePath 'one.png')) 'Invalid replacement saved a stale image or lost the last valid path.'
    $report.InvalidImageClosesAndPreservesLastValid = $true

    $r = OpenSettings
    $inputPattern = InputPattern $r
    $handle = Window 'EDock 设置'
    $inputPattern.SetValue((Join-Path $profilePath 'two.png'))
    Start-Sleep -Milliseconds 25
    CloseSettings $handle
    Start-Sleep -Milliseconds 25
    $inputPattern.SetValue((Join-Path $profilePath 'three.png'))
    WaitClosed $handle
    Assert ((Settings).BackgroundImagePath -eq (Join-Path $profilePath 'three.png')) 'Close did not recheck image input changed while awaiting validation.'
    $report.InputDuringCloseUsesLatestVersion = $true

    $r = OpenSettings
    $inputPattern = InputPattern $r
    $exitElement = Find $r '退出 EDock' ([System.Windows.Automation.InvokePattern]::Pattern)
    $exitPattern = [System.Windows.Automation.InvokePattern]$exitElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $watch.Restart()
    $inputPattern.SetValue((Join-Path $profilePath 'two.png'))
    $exitPattern.Invoke()
    $exitRequestedMs = $watch.ElapsedMilliseconds
    Assert ($exitRequestedMs -lt 250) 'Test did not exit inside the image debounce window.'
    Assert ($process.WaitForExit(5000)) 'Isolated app failed to exit.'
    Assert ((Settings).BackgroundImagePath -eq (Join-Path $profilePath 'two.png')) 'Immediate app exit lost the valid image.'
    Assert (!(Test-Path (Join-Path $profilePath 'error.log'))) 'Isolated app logged an unhandled error.'
    $report.ValidImageImmediateAppExit = $true
    $report.ExitRequestedMs = $exitRequestedMs
    $report.Profile = $profilePath
    $report | ConvertTo-Json | Tee-Object -FilePath (Join-Path $WorkspacePath 'work/v11-image-close-report.json')
}
finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
}
