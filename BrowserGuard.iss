;BrowserGuard Setup--

#define AppVersion "1.0.0.0"

[Setup]
AppName=BrowserGuard
AppVerName=BrowserGuard {#AppVersion}
VersionInfoVersion={#AppVersion}
AppVersion={#AppVersion}
AppMutex=BrowserGuardSetup
;DefaultDirName=C:\BrowserGuard
DefaultDirName={code:GetProgramFiles}\BrowserGuard
Compression=lzma2
SolidCompression=yes
OutputDir=SetupOutput
OutputBaseFilename=BrowserGuardSetup
AppPublisher=BrowserGuard
WizardImageStretch=no
VersionInfoDescription=BrowserGuardSetup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultGroupName=BrowserGuard
UninstallDisplayIcon={app}\BrowserGuardHost\BrowserGuard.exe

[Registry]
Root: HKLM; Subkey: "Software\BrowserGuard"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "Path"; ValueData: "{app}\"
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "ClientType"; ValueData: ""
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "Configfile"; ValueData: "{app}\BrowserGuard.json"
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "ExtensionExecfile"; ValueData: "{app}\BrowserGuardHost\BrowserGuard.exe"

;ホストを win-x86 で発行する場合にのみ必要（32bit プロセスの HKLM\SOFTWAREがWOW6432Node へリダイレクトされるため）。
;現状は win-x64 で発行しているため不要。
;Edge 本体のアーキテクチャとは無関係。
;Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; Flags: uninsdeletekey
;Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "Path"; ValueData: "{app}\"
;Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "ClientType"; ValueData: ""
;Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
;Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "Configfile"; ValueData: "{app}\BrowserGuard.json"
;Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "ExtensionExecfile"; ValueData: "{app}\BrowserGuardHost\BrowserGuard.exe"


;Edge
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.clear_code.browser_guard"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.clear_code.browser_guard"; ValueType: string; ValueData: "{app}\BrowserGuardHost\edge.json";

[Languages]
Name: jp; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
; 既定は OFF。グループポリシーで管理する運用を標準とし、
; レジストリ直接書き込みは明示的に選択した場合のみ行う。
Name: "extensionpolicy"; Description: "拡張機能を Edge のポリシーに登録して自動的にインストールする"; GroupDescription: "ブラウザー拡張機能:"; Flags: unchecked


[Files]
;Config
Source: "Resources\BrowserGuard.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

;Host
Source: "BrowserGuard\bin\Release\net8.0\publish\win-x64\*.dll"; DestDir: "{app}\BrowserGuardHost";Flags: ignoreversion;permissions:users-readexec admins-full system-full
Source: "BrowserGuard\bin\Release\net8.0\publish\win-x64\*.exe"; DestDir: "{app}\BrowserGuardHost";Flags: ignoreversion;permissions:users-readexec admins-full system-full

;Native Messaging Host Manifest
Source: "Resources\edge.json"; DestDir: "{app}\BrowserGuardHost";Flags: ignoreversion;permissions:users-readexec admins-full system-full

;Extension
Source: "webextensions\BrowserGuardEdge.crx"; DestDir: "{app}\BrowserGuardExtension";Flags: ignoreversion;permissions:users-readexec admins-full system-full

;Update Manifest
Source: "Resources\manifest.xml"; DestDir: "{app}\BrowserGuardExtension";Flags: ignoreversion;permissions:users-readexec admins-full system-full

[Dirs]
Name: "{app}";Permissions: users-modify

[Run] 
Filename: "{sys}\icacls.exe";Parameters: """{app}\BrowserGuardHost\BrowserGuard.exe"" /inheritance:r"; Flags: runhidden shellexec
Filename: "{sys}\icacls.exe";Parameters: """{app}\BrowserGuardHost\edge.json"" /inheritance:r"; Flags: runhidden shellexec

[UninstallRun]

[Code]
const
  // Determined by the signing key (webextensions\pem\edge.pem).
  // Must match the appid in Resources\manifest.xml.
  ExtensionId = 'ddniogodiahgpmfkljajobgkaecabnif';
  // The policy itself is written by the host executable's "policy" subcommand;
  // see BrowserGuard\PolicyCommand.cs for why ExtensionSettings is used rather
  // than ExtensionInstallForcelist.
  // Records that this installer wrote the policy, so that an entry set up by
  // hand or by a group policy is not removed on uninstall.
  OwnKey = 'Software\BrowserGuard';
  RegisteredFlag = 'ExtensionSettingsRegistered';

function GetProgramFiles(Param: string): string;
  begin
    if IsWin64 then Result := ExpandConstant('{pf64}')
    else Result := ExpandConstant('{pf32}')
  end;

procedure TaskKill(FileName: String);
var
  ResultCode: Integer;
begin
    Exec(ExpandConstant('taskkill.exe'), '/f /im ' + '"' + FileName + '"', '', SW_HIDE,ewWaitUntilTerminated, ResultCode);
end;
function InitializeSetup():Boolean;
begin
	TaskKill('msedge.exe');
	TaskKill('BrowserGuard.exe');
	Result := True;
end;

// Both this installer and a group policy write to the same registry location,
// so the note explains which one ends up winning.
procedure InitializeWizard();
var
  Note: TNewStaticText;
  NoteHeight: Integer;
begin
  NoteHeight := ScaleY(64);
  WizardForm.TasksList.Height := WizardForm.TasksList.Height - NoteHeight - ScaleY(8);

  Note := TNewStaticText.Create(WizardForm);
  Note.Parent := WizardForm.SelectTasksPage;
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.Left := WizardForm.TasksList.Left;
  Note.Top := WizardForm.TasksList.Top + WizardForm.TasksList.Height + ScaleY(8);
  Note.Width := WizardForm.TasksList.Width;
  Note.Height := NoteHeight;
  Note.Caption :=
    'グループポリシーで「拡張機能の管理設定を構成する」(ExtensionSettings) が' +
    '構成されている環境では、グループポリシーの設定が優先され、' +
    'ここで登録した内容は上書きされます。' +
    'グループポリシーで管理する場合は、チェックを外したままにしてください。';
end;

// Inno Setup does not expand constants inside file contents, so the
// {InstallFolder} placeholder in manifest.xml is replaced after installing.
// The result is a file URL, which is what the browser expects for update_url.
function GetExtensionFolderUrl(): String;
var
  Path: String;
begin
  Path := ExpandConstant('{app}\BrowserGuardExtension');
  StringChangeEx(Path, '\', '/', True);
  StringChangeEx(Path, ' ', '%20', True);
  Result := 'file:///' + Path;
end;

procedure ExpandUpdateManifest();
var
  FilePath: String;
  Lines: TArrayOfString;
  Url: String;
  i: Integer;
begin
  FilePath := ExpandConstant('{app}\BrowserGuardExtension\manifest.xml');
  if not LoadStringsFromFile(FilePath, Lines) then
  begin
    Log('Cannot read update manifest: ' + FilePath);
    Exit;
  end;

  Url := GetExtensionFolderUrl();
  for i := 0 to GetArrayLength(Lines) - 1 do
    StringChangeEx(Lines[i], '{InstallFolder}', Url, True);

  if not SaveStringsToFile(FilePath, Lines, False) then
    Log('Cannot write update manifest: ' + FilePath);
end;

// ExtensionSettings holds one JSON object covering every extension, so the
// existing value has to be merged rather than overwritten. Pascal Script has no
// JSON support, so the host executable does that part as a subcommand.
function RunPolicyCommand(const Arguments: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(
    ExpandConstant('{app}\BrowserGuardHost\BrowserGuard.exe'),
    'policy ' + Arguments,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if not Result then
    Log('Cannot start BrowserGuard.exe')
  else if ResultCode <> 0 then
  begin
    Log('BrowserGuard.exe policy returned ' + IntToStr(ResultCode) + ' for: ' + Arguments);
    Result := False;
  end;
end;

procedure RegisterExtensionSettings();
begin
  if RunPolicyCommand('register ' + ExtensionId +
                      ' "' + GetExtensionFolderUrl() + '/manifest.xml"') then
    RegWriteStringValue(HKEY_LOCAL_MACHINE, OwnKey, RegisteredFlag, '1');
end;

procedure UnregisterExtensionSettings();
begin
  RunPolicyCommand('unregister ' + ExtensionId);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ExpandUpdateManifest();
    if WizardIsTaskSelected('extensionpolicy') then
      RegisterExtensionSettings();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Flag: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  // Leave the policy alone unless this installer was the one that wrote it.
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, OwnKey, RegisteredFlag, Flag) then
  begin
    if Flag = '1' then
      UnregisterExtensionSettings();
  end;
end;
