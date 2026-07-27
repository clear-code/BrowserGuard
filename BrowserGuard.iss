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
