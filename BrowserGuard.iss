;BrowserGuard Setup--

[Setup]
AppName=BrowserGuard
AppVerName=BrowserGuard
VersionInfoVersion=1.0.0.0
AppVersion=1.0.0.0
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
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DefaultGroupName=BrowserGuard
UninstallDisplayIcon={app}\BrowserGuard.exe

[Registry]
Root: HKLM; Subkey: "Software\BrowserGuard"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "Path"; ValueData: "{app}\"
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "ClientType"; ValueData: ""
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "Version"; ValueData: "1.0.0.0"
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "Configfile"; ValueData: "{app}\BrowserGuard.json"
Root: HKLM; Subkey: "Software\BrowserGuard"; ValueType: string; ValueName: "ExtensionExecfile"; ValueData: "{app}\BrowserGuard.exe"

Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "Path"; ValueData: "{app}\"
Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "ClientType"; ValueData: ""
Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "Version"; ValueData: "1.0.0.0"
Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "Rulefile"; ValueData: "{app}\BrowserGuard.json"
Root: HKLM; Subkey: "Software\WOW6432Node\BrowserGuard"; ValueType: string; ValueName: "ExtensionExecfile"; ValueData: "{app}\BrowserGuard.exe"


;Edge
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.clear_code.browser_guard"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.clear_code.browser_guard"; ValueType: string; ValueData: "{app}\BrowserGuardHost\edge.json";

[Languages]
Name: jp; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
; 既定は OFF。グループポリシーで管理する運用を標準とし、
; レジストリ直接書き込みは明示的に選択した場合のみ行う。
Name: "forcelist"; Description: "拡張機能を Edge のポリシーに登録して自動的にインストールする"; GroupDescription: "ブラウザー拡張機能:"; Flags: unchecked


[Files]
;Config
Source: "Resources\BrowserGuard.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

;Host
Source: "BrowserGuard\bin\Release\net8.0\publish\win-x86\*.dll"; DestDir: "{app}\BrowserGuardHost";Flags: ignoreversion;permissions:users-readexec admins-full system-full
Source: "BrowserGuard\bin\Release\net8.0\publish\win-x86\*.exe"; DestDir: "{app}\BrowserGuardHost";Flags: ignoreversion;permissions:users-readexec admins-full system-full

;Native Messaging Host Manifest
Source: "Resources\edge.json"; DestDir: "{app}\BrowserGuardHost";Flags: ignoreversion;permissions:users-readexec admins-full system-full

;Extension
Source: "webextensions\BrowserGuardEdge.crx"; DestDir: "{app}\BrowserGuardExtension";Flags: ignoreversion;permissions:users-readexec admins-full system-full

;Update Manifest
Source: "Resources\manifest.xml"; DestDir: "{app}\BrowserGuardExtension";Flags: ignoreversion;permissions:users-readexec admins-full system-full

[Dirs]
Name: "{app}";Permissions: users-modify

[Run] 
Filename: "{sys}\icacls.exe";Parameters: """{app}\BrowserGuard.exe"" /inheritance:r"; Flags: runhidden shellexec
Filename: "{sys}\icacls.exe";Parameters: """{app}\BrowserGuardHost\edge.json"" /inheritance:r"; Flags: runhidden shellexec

[UninstallRun]

[Code]
const
  // Determined by the signing key (webextensions\pem\edge.pem).
  // Must match the appid in Resources\manifest.xml.
  ExtensionId = 'ddniogodiahgpmfkljajobgkaecabnif';
  // SOFTWARE\Policies is shared between the 32 and 64 bit registry views.
  ForcelistKey = 'SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist';
  // Records that this installer wrote the policy, so that an entry set up by
  // hand or by a group policy is not removed on uninstall.
  OwnKey = 'Software\BrowserGuard';
  RegisteredFlag = 'ForcelistRegistered';

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
    'グループポリシーで「サイレント インストールされる拡張機能を制御する」' +
    '(ExtensionInstallForcelist) が構成されている環境では、' +
    'グループポリシーの設定が優先され、ここで登録した内容は上書きされます。' +
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

// Force installing the extension from the locally hosted update manifest.
// Entries are named with sequential numbers, so an unused slot is picked,
// and an entry left by a previous install is reused instead of duplicated.
function FindOwnForcelistValueName(var ValueName: String): Boolean;
var
  Names: TArrayOfString;
  Data: String;
  i: Integer;
begin
  Result := False;
  if not RegGetValueNames(HKEY_LOCAL_MACHINE, ForcelistKey, Names) then
    Exit;
  for i := 0 to GetArrayLength(Names) - 1 do
  begin
    if RegQueryStringValue(HKEY_LOCAL_MACHINE, ForcelistKey, Names[i], Data) then
    begin
      if Pos(ExtensionId + ';', Data) = 1 then
      begin
        ValueName := Names[i];
        Result := True;
        Exit;
      end;
    end;
  end;
end;

procedure RegisterForcelist();
var
  ValueName, Entry: String;
  Slot: Integer;
begin
  Entry := ExtensionId + ';' + GetExtensionFolderUrl() + '/manifest.xml';

  if not FindOwnForcelistValueName(ValueName) then
  begin
    Slot := 1;
    while RegValueExists(HKEY_LOCAL_MACHINE, ForcelistKey, IntToStr(Slot)) do
      Slot := Slot + 1;
    ValueName := IntToStr(Slot);
  end;

  if RegWriteStringValue(HKEY_LOCAL_MACHINE, ForcelistKey, ValueName, Entry) then
    RegWriteStringValue(HKEY_LOCAL_MACHINE, OwnKey, RegisteredFlag, '1')
  else
    Log('Cannot write policy value: ' + ForcelistKey + '\' + ValueName);
end;

// Only the entry belonging to this extension is removed, so policies set for
// other extensions are left untouched.
procedure UnregisterForcelist();
var
  ValueName: String;
begin
  while FindOwnForcelistValueName(ValueName) do
  begin
    if not RegDeleteValue(HKEY_LOCAL_MACHINE, ForcelistKey, ValueName) then
    begin
      Log('Cannot delete policy value: ' + ForcelistKey + '\' + ValueName);
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ExpandUpdateManifest();
    if WizardIsTaskSelected('forcelist') then
      RegisterForcelist();
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
      UnregisterForcelist();
  end;
end;
