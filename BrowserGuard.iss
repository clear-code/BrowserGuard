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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ExpandUpdateManifest();
end;
