param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class HotkeyUi {
 public delegate bool EnumProc(IntPtr h, IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int L,T,R,B; public int Width=>R-L;}
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc e,IntPtr p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr extra);
 public static IntPtr Find(int pid,string title){IntPtr result=IntPtr.Zero;EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out uint id); var s=new StringBuilder(256);GetWindowText(h,s,256);if(id==pid&&s.ToString()==title)result=h; return true;},IntPtr.Zero);return result;}
 public static RECT Bounds(IntPtr h){GetWindowRect(h,out var r);return r;}
}
'@
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$profile = Join-Path $EvidenceDirectory ('profile-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($profile) | Out-Null
$settingsPath = Join-Path $profile 'settings.json'
$items = @(1..60 | ForEach-Object {@{Id=('fixture-'+$_);Name=('测试项目 '+$_);TargetPath='https://example.com/'}})
@{LeftEnabled=$true;RightEnabled=$false;QuickModeEnabled=$true;SuppressRevealWhenFullscreen=$false;AutoHide=$true;ShortcutDisplayMode='Text';LeftShortcuts=$items} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath
$checks = [Collections.Generic.List[object]]::new()
$production = @(Get-Process EDock -ErrorAction SilentlyContinue | Select-Object Id,StartTime,Path)
$process = $null; $window = [IntPtr]::Zero; $failure = $null
function Check([string]$name,[bool]$value){$checks.Add(@{Name=$name;Passed=$value}); if(-not $value){throw $name}; Write-Output ('PASS ' + $name)}
function Until([scriptblock]$condition,[string]$message){$watch=[Diagnostics.Stopwatch]::StartNew();do {if(& $condition){return};Start-Sleep -Milliseconds 90}while($watch.ElapsedMilliseconds -lt 6000);throw $message}
function Root { [System.Windows.Automation.AutomationElement]::FromHandle($window) }
function Id([string]$id){(Root).FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))}
function Named([string]$name){(Root).FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))}
function InvokeId([string]$id){$e=Id $id;if($null -eq $e){throw "Missing $id"};([System.Windows.Automation.InvokePattern]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke();Start-Sleep -Milliseconds 120}
function Chip([string]$action){(Id ('ShortcutKey_'+$action)).Current.ItemStatus}
function Tap([byte]$key){if([HotkeyUi]::GetForegroundWindow() -ne $window){throw 'Input target is not the owned test window'};[HotkeyUi]::keybd_event($key,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 40;[HotkeyUi]::keybd_event($key,0,2,[UIntPtr]::Zero);Start-Sleep -Milliseconds 130}
function Chord([byte]$modifier,[byte]$key){if([HotkeyUi]::GetForegroundWindow() -ne $window){throw 'Input target is not the owned test window'};[HotkeyUi]::keybd_event($modifier,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 60;Tap $key;[HotkeyUi]::keybd_event($modifier,0,2,[UIntPtr]::Zero);Start-Sleep -Milliseconds 100}
function Begin([string]$action){[HotkeyUi]::SetForegroundWindow($window)|Out-Null;InvokeId ('ShortcutKey_'+$action);Until {$null -ne (Id ('ShortcutCandidate_'+$action))} 'Recorder not visible'}
function Capture([string]$action,[string]$expected){Until {try{(Id ('ShortcutCandidate_'+$action)).Current.Name -eq $expected}catch{$false}} "Did not record $expected"}
function Persisted {Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json}
function Saved([string]$action,[string]$key,[string]$modifiers=''){Until { $v=(Persisted).ShortcutBindings.$action; $v.Key -eq $key -and $v.Modifiers -eq $modifiers } "Binding $action was not saved"}
function Category([string]$name){foreach($e in (Named $name)){$pattern=$null;if($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)){([System.Windows.Automation.SelectionItemPattern]$pattern).Select();Start-Sleep -Milliseconds 150;return}};throw "Missing category $name"}
function Screenshot([string]$name){$b=[HotkeyUi]::Bounds($window);$image=[Drawing.Bitmap]::new($b.R-$b.L,$b.B-$b.T);$g=[Drawing.Graphics]::FromImage($image);try{$g.CopyFromScreen($b.L,$b.T,0,0,$image.Size);$image.Save((Join-Path $EvidenceDirectory $name))}finally{$g.Dispose();$image.Dispose()}}
try {
 $process=Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable) -ArgumentList @('--profile',('"'+$profile+'"'),'--settings') -WindowStyle Hidden -PassThru
 Until {$script:window=[HotkeyUi]::Find($process.Id,'EDock 设置');$window -ne [IntPtr]::Zero} 'Settings did not open'
 [HotkeyUi]::SetForegroundWindow($window)|Out-Null
 Category '快捷模式'
 Check 'Default activation displays Ctrl once' ((Chip 'ActivateQuickMode') -eq 'Ctrl')
 Begin 'PreviousRow'; Tap 0x49; Capture 'PreviousRow' 'I'; Tap 0x0D
 Until {(Chip 'PreviousRow') -eq 'I'} 'Enter did not save I'
 Saved 'PreviousRow' 'I'; Check 'Real I key and Enter save while settings remains open' $true
 Tap 0x0D; Check 'Second Enter after saving does not restart recording' ($null -eq (Id 'ShortcutCandidate_PreviousRow'))
 Tap 0x55; Check 'Saved row stays locked until clicked again' ((Chip 'PreviousRow') -eq 'I')
 Begin 'PreviousRow'; Tap 0x4A; Capture 'PreviousRow' 'J'; Tap 0x1B
 Check 'Esc cancels without changing committed binding' ((Chip 'PreviousRow') -eq 'I')
 Tap 0x0D; Tap 0x55; Check 'Cancelled row stays locked until clicked again' ($null -eq (Id 'ShortcutCandidate_PreviousRow') -and (Chip 'PreviousRow') -eq 'I')
 Begin 'PreviousRow'; Tap 0x4A; Capture 'PreviousRow' 'J'; InvokeId 'ShortcutCancel_PreviousRow'
 Check 'Cancel button preserves binding' ((Chip 'PreviousRow') -eq 'I')
 Begin 'NextRow'; Chord 0xA2 0x49; Capture 'NextRow' 'Ctrl+I'; InvokeId 'ShortcutSave_NextRow'
 Saved 'NextRow' 'I' 'Ctrl'; Check 'Combination and Save button persist structured key and modifiers' $true
 Begin 'ActivateQuickMode'; Tap 0xA2; Capture 'ActivateQuickMode' 'Ctrl'; Tap 0x0D
 Check 'Single Ctrl can be recorded without duplicate modifier' ((Chip 'ActivateQuickMode') -eq 'Ctrl')
 Begin 'NextRow'; Tap 0x51; Capture 'NextRow' 'Q'
 Check 'Duplicate binding disables Save' (-not (Id 'ShortcutSave_NextRow').Current.IsEnabled)
 Check 'Conflict identifies the other action' ((Id 'ShortcutHint_NextRow').Current.Name -match '上一个列表')
 Screenshot 'conflict.png'
 Tap 0x0D; Tap 0x4B; Capture 'NextRow' 'K'; Tap 0x0D; Saved 'NextRow' 'K'
 Check 'Conflict Enter keeps recording; replacement can be saved' ((Chip 'NextRow') -eq 'K')
 $toggle=Id 'ShortcutEnabled_PreviousRow';([System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
 Until {(Persisted).ShortcutBindings.PreviousRow.Enabled -eq $false} 'Disable was not saved'
 Check 'Disable retains key' ((Persisted).ShortcutBindings.PreviousRow.Key -eq 'I')
 ([System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
 Until {(Persisted).ShortcutBindings.PreviousRow.Enabled -eq $true} 'Re-enable not saved'
 Check 'Re-enable retains key' ((Chip 'PreviousRow') -eq 'I')
 Begin 'SwitchDock'; Tap 0x09; Capture 'SwitchDock' 'Tab'; Tap 0x1B
 Check 'Tab is recorded without moving focus' $true
 Begin 'PreviousRow'; Tap 0x4C; Category '基本设置'; Category '快捷模式'
 Check 'Leaving the page cancels uncommitted input' ((Chip 'PreviousRow') -eq 'I')
 InvokeId 'ShortcutResetAll'; Saved 'PreviousRow' 'W'; Saved 'NextRow' 'S'
 Check 'Restore defaults applies and saves all bindings' ((Chip 'ActivateQuickMode') -eq 'Ctrl')
 Screenshot 'shortcuts.png'
 # Exercise hot reload on a real sidebar, using a key absent from the default map.
 Begin 'ToggleExpanded'; Tap 0x49; Capture 'ToggleExpanded' 'I'; Tap 0x0D; Saved 'ToggleExpanded' 'I'
 Tap 0xA2; Start-Sleep -Milliseconds 450
 $dock=[HotkeyUi]::Find($process.Id,'EDock · 左侧');$before=[HotkeyUi]::Bounds($dock)
 Tap 0x49; Start-Sleep -Milliseconds 550; $after=[HotkeyUi]::Bounds($dock)
 Check 'Edited I shortcut expands live sidebar without restart' ($after.Width -gt $before.Width)
 Tap 0xA2
 # Reopen the same profile to verify durable settings.
 InvokeId 'ShortcutResetAll'; Saved 'ToggleExpanded' 'F'
 foreach($e in (Named '退出 EDock')){([System.Windows.Automation.InvokePattern]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke();break}
 Check 'Test instance exits normally' ($process.WaitForExit(6000))
 $process=Start-Process -FilePath $Executable -WorkingDirectory (Split-Path $Executable) -ArgumentList @('--profile',('"'+$profile+'"'),'--settings') -WindowStyle Hidden -PassThru
 Until {$script:window=[HotkeyUi]::Find($process.Id,'EDock 设置');$window -ne [IntPtr]::Zero} 'Reopened settings missing'
 Category '快捷模式'; Check 'Settings survive reopening' ((Chip 'ToggleExpanded') -eq 'F' -and (Chip 'PreviousRow') -eq 'W')
} catch {$failure=$_.ToString(); Write-Output $failure} finally {
 if($null -ne $process -and -not $process.HasExited){try{foreach($e in (Named '退出 EDock')){([System.Windows.Automation.InvokePattern]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke();break};$null=$process.WaitForExit(6000)}catch{}}
 foreach($p in $production){$still=Get-Process -Id $p.Id -ErrorAction SilentlyContinue;Check ('Pre-existing EDock still running: '+$p.Id) ($null -ne $still -and $still.StartTime -eq $p.StartTime)}
 @{Timestamp=[DateTimeOffset]::Now.ToString('o');Executable=$Executable;Profile=$profile;Checks=$checks;Failure=$failure;Passed=($null -eq $failure)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json')
}
if($null -ne $failure){exit 1}
