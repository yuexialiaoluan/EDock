; EDock per-user installer. Keep AppId, default directory and configuration paths
; stable across releases so newer installers upgrade the existing installation.
#ifndef PublishDirectory
  #error PublishDirectory must name a prepared self-contained distribution.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must name the installer output directory.
#endif
#define AppVersion GetStringFileInfo(PublishDirectory + "\EDock.exe", "ProductVersion")
#define NativeHelper "EDock.Taskbar.0.14.5.dll"
#define NativeSHA256 "dda83cae80e3af91a21af47a087247a47c369585ff39236e49758b664b90be4d"
#define StartMenuHelper "EDock.StartMenu.0.16.15.dll"
#define StartMenuSHA256 "2748270c47fcf2c61e2f2e40e066d2186c52fc2c515cdc8bb30a33b44ac8fd5b"
#define StartButtonHelper "EDock.StartButton.0.16.18.1.dll"
#define StartButtonSHA256 "bcc3f35bd07f6b4912348d0b60664cd9a352dd99fb9a8c79f810cda2acd5cec8"
#define UserProfilePath "{localappdata}\EDock"
#define UserBackupPath "{localappdata}\EDock-backups"

[Setup]
AppId={{98E0EE4C-142F-4B79-94EC-A78E17ED59BB}
AppName=EDock
AppVersion={#AppVersion}
AppPublisher=yuexialiaoluan
AppPublisherURL=https://github.com/yuexialiaoluan/EDock
AppSupportURL=https://github.com/yuexialiaoluan/EDock/issues
AppUpdatesURL=https://github.com/yuexialiaoluan/EDock/releases
DefaultDirName={localappdata}\Programs\EDock
DefaultGroupName=EDock
PrivilegesRequired=lowest
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes
MinVersion=10.0.22000
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
AppMutex=Local\EDock.Sidebar
SetupMutex=Local\EDock.Setup
CloseApplications=no
RestartApplications=no
AlwaysRestart=no
WizardStyle=modern
SetupIconFile={#PublishDirectory}\Assets\EDock.ico
UninstallDisplayIcon={app}\EDock.exe
UninstallDisplayName=EDock
OutputDir={#OutputDirectory}
OutputBaseFilename=EDock-{#AppVersion}-setup-win-x64
Compression=lzma2/normal
SolidCompression=yes
LZMANumBlockThreads=2
InfoBeforeFile=installer-info.txt
VersionInfoVersion={#AppVersion}
VersionInfoDescription=EDock 安装程序

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Excludes: "Native\*"; Flags: ignoreversion recursesubdirs createallsubdirs
; This immutable helper can remain loaded in Explorer after EDock exits.
; Reuse its existing verified bytes and never request Explorer or Windows restart.
Source: "{#PublishDirectory}\Native\{#NativeHelper}"; DestDir: "{app}\Native"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#PublishDirectory}\Native\{#StartMenuHelper}"; DestDir: "{app}\Native"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#PublishDirectory}\Native\{#StartButtonHelper}"; DestDir: "{app}\Native"; Flags: onlyifdoesntexist uninsneveruninstall

#include "retired-sdk-components.iss"

[Icons]
Name: "{group}\EDock"; Filename: "{app}\EDock.exe"; WorkingDir: "{app}"
Name: "{group}\卸载 EDock"; Filename: "{uninstallexe}"
Name: "{autodesktop}\EDock"; Filename: "{app}\EDock.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\EDock.exe"; Description: "运行 EDock"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  FileAttributeDirectory = $10;
  FileAttributeReparsePoint = $400;
  InvalidFileAttributes = $FFFFFFFF;
  ErrorFileNotFound = 2;
  ErrorPathNotFound = 3;
  ErrorNoMoreFiles = 18;
type
  TNativeFindData = record
    Attributes: LongWord;
    CreationTimeLow, CreationTimeHigh: LongWord;
    AccessTimeLow, AccessTimeHigh: LongWord;
    WriteTimeLow, WriteTimeHigh: LongWord;
    SizeHigh, SizeLow, Reserved0, Reserved1: LongWord;
    FileName: array[0..259] of Char;
    AlternateName: array[0..13] of Char;
  end;
var
  BackupComplete: Boolean;
  BackupDirectory: String;
  BackupFileCount, BackupDirectoryCount: Integer;

function GetFileAttributesW(FileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function NativeFindFirstFile(FileName: String; var FindData: TNativeFindData): Integer;
  external 'FindFirstFileW@kernel32.dll stdcall';
function NativeFindNextFile(FindHandle: Integer; var FindData: TNativeFindData): Boolean;
  external 'FindNextFileW@kernel32.dll stdcall';
function NativeFindClose(FindHandle: Integer): Boolean;
  external 'FindClose@kernel32.dll stdcall';

procedure CheckPathAncestors(Path: String);
var
  CurrentPath, ParentPath: String;
  Attributes, ErrorCode: LongWord;
begin
  CurrentPath := RemoveBackslashUnlessRoot(ExpandFileName(Path));
  while CurrentPath <> '' do
  begin
    Attributes := GetFileAttributesW(CurrentPath);
    if Attributes = InvalidFileAttributes then
    begin
      ErrorCode := DLLGetLastError;
      if (ErrorCode <> ErrorFileNotFound) and (ErrorCode <> ErrorPathNotFound) then
        RaiseException('无法检查配置或备份路径，Windows 错误 ' + IntToStr(ErrorCode) + '：' + CurrentPath);
    end
    else if (Attributes and FileAttributeReparsePoint) <> 0 then
      RaiseException('配置或备份路径包含链接或连接点，无法保证完整备份：' + CurrentPath);
    ParentPath := RemoveBackslashUnlessRoot(ExtractFileDir(CurrentPath));
    if ParentPath = CurrentPath then exit;
    CurrentPath := ParentPath;
  end;
end;

procedure CopyProfileTree(SourceDirectory, DestinationDirectory: String);
var
  FindData: TNativeFindData;
  FindHandle, Index: Integer;
  ErrorCode: LongWord;
  Name, SourcePath, DestinationPath: String;
begin
  CheckPathAncestors(SourceDirectory);
  CheckPathAncestors(DestinationDirectory);
  if not ForceDirectories(DestinationDirectory) then
    RaiseException('无法创建配置备份目录：' + DestinationDirectory);
  CheckPathAncestors(DestinationDirectory);
  BackupDirectoryCount := BackupDirectoryCount + 1;
  FindHandle := NativeFindFirstFile(AddBackslash(SourceDirectory) + '*', FindData);
  if FindHandle = -1 then
  begin
    ErrorCode := DLLGetLastError;
    if ErrorCode <> ErrorFileNotFound then
      RaiseException('无法枚举配置目录，Windows 错误 ' + IntToStr(ErrorCode) + '：' + SourceDirectory);
    exit;
  end;
  try
    while True do
    begin
      Name := '';
      Index := 0;
      while (Index < 260) and (FindData.FileName[Index] <> #0) do
      begin
        Name := Name + FindData.FileName[Index];
        Index := Index + 1;
      end;
      if (Name <> '.') and (Name <> '..') then
      begin
        SourcePath := AddBackslash(SourceDirectory) + Name;
        DestinationPath := AddBackslash(DestinationDirectory) + Name;
        if (FindData.Attributes and FileAttributeReparsePoint) <> 0 then
          RaiseException('配置包含链接或连接点，无法保证完整备份：' + SourcePath);
        if (FindData.Attributes and FileAttributeDirectory) <> 0 then
          CopyProfileTree(SourcePath, DestinationPath)
        else
        begin
          if not FileCopy(SourcePath, DestinationPath, True) then
            RaiseException('无法备份配置文件：' + SourcePath);
          if CompareText(GetSHA256OfFile(SourcePath), GetSHA256OfFile(DestinationPath)) <> 0 then
            RaiseException('配置备份校验失败：' + SourcePath);
          BackupFileCount := BackupFileCount + 1;
        end;
      end;
      if not NativeFindNextFile(FindHandle, FindData) then
      begin
        ErrorCode := DLLGetLastError;
        if ErrorCode <> ErrorNoMoreFiles then
          RaiseException('配置目录枚举中断，Windows 错误 ' + IntToStr(ErrorCode) + '：' + SourceDirectory);
        break;
      end;
    end;
  finally
    NativeFindClose(FindHandle);
  end;
end;

procedure BackupExistingProfile;
var
  ProfileDirectory, BackupRoot, BackupBase, PendingDirectory: String;
  Attributes, ErrorCode: LongWord;
  Suffix: Integer;
begin
  if BackupComplete then exit;
  ProfileDirectory := ExpandConstant('{#UserProfilePath}');
  CheckPathAncestors(ProfileDirectory);
  Attributes := GetFileAttributesW(ProfileDirectory);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := DLLGetLastError;
    if (ErrorCode <> ErrorFileNotFound) and (ErrorCode <> ErrorPathNotFound) then
      RaiseException('无法读取配置目录，Windows 错误 ' + IntToStr(ErrorCode) + '。');
    BackupComplete := True;
    exit;
  end;
  if (Attributes and FileAttributeDirectory) = 0 then
    RaiseException('配置路径不是文件夹：' + ProfileDirectory);
  BackupRoot := ExpandConstant('{#UserBackupPath}');
  CheckPathAncestors(BackupRoot);
  if Pos(Lowercase(AddBackslash(ExpandFileName(ProfileDirectory))),
      Lowercase(AddBackslash(ExpandFileName(BackupRoot)))) = 1 then
    RaiseException('备份位置不能位于配置目录内部。');
  BackupBase := AddBackslash(BackupRoot) + 'before-upgrade-{#AppVersion}-' +
    GetDateTimeString('yyyymmdd-hhnnss-zzz', '-', ':');
  BackupDirectory := BackupBase;
  Suffix := 0;
  while DirExists(BackupDirectory) or FileExists(BackupDirectory) or
      DirExists(BackupDirectory + '.incomplete') or FileExists(BackupDirectory + '.incomplete') do
  begin
    Suffix := Suffix + 1;
    BackupDirectory := BackupBase + '-' + IntToStr(Suffix);
  end;
  PendingDirectory := BackupDirectory + '.incomplete';
  BackupFileCount := 0;
  BackupDirectoryCount := 0;
  Log('Backing up existing EDock configuration to ' + BackupDirectory);
  CopyProfileTree(ProfileDirectory, PendingDirectory);
  if not RenameFile(PendingDirectory, BackupDirectory) then
    RaiseException('无法完成配置备份目录：' + PendingDirectory);
  BackupComplete := True;
  Log('EDock configuration backup completed and every file verified: ' +
    IntToStr(BackupFileCount) + ' files, ' + IntToStr(BackupDirectoryCount) + ' directories.');
end;

procedure VerifyExistingHelper(const Name, ExpectedHash: String);
var
  ExistingHelper: String;
begin
  ExistingHelper := ExpandConstant('{app}\Native\') + Name;
  if FileExists(ExistingHelper) then
    if CompareText(GetSHA256OfFile(ExistingHelper), ExpectedHash) <> 0 then
      RaiseException('现有系统外观组件与本安装包不一致：' + Name + #13#10 +
        '请保留个人配置，选择新的程序文件夹，或在下次正常登录 Windows 后重试。');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  try
    VerifyExistingHelper('{#NativeHelper}', '{#NativeSHA256}');
    VerifyExistingHelper('{#StartMenuHelper}', '{#StartMenuSHA256}');
    VerifyExistingHelper('{#StartButtonHelper}', '{#StartButtonSHA256}');
    BackupExistingProfile;
  except
    Result := '安装尚未开始：' + GetExceptionMessage + #13#10 +
      '程序文件与原配置未被替换。请解决上述问题后重试。';
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := MsgBox('卸载前请先在 EDock 设置中关闭开机启动，并从托盘退出 EDock。' + #13#10 +
      '卸载仅删除程序文件，个人配置和自动备份都会保留。是否继续？',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure RemoveAppearanceHelper(const Name: String);
var
  HelperPath: String;
begin
  HelperPath := ExpandConstant('{app}\Native\') + Name;
  if FileExists(HelperPath) then
    if not DeleteFile(HelperPath) then
      Log('System appearance helper is still loaded by Windows; leaving this file in place: ' + HelperPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveAppearanceHelper('{#NativeHelper}');
    RemoveAppearanceHelper('{#StartMenuHelper}');
    RemoveAppearanceHelper('{#StartButtonHelper}');
    RemoveDir(ExpandConstant('{app}\Native'));
    RemoveDir(ExpandConstant('{app}'));
  end;
end;
