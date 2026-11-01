; CAD Master installer (Inno Setup 6). Built by installer\release.ps1, which passes:
;   /DAppVersion=<VERSION file>   /DStageDir=<folder with the built add-in>
;
; Per-user install, no admin for the files:
;   %LOCALAPPDATA%\Programs\CAD Master\
;     app-<version>\      the add-in (CADMaster.dll, ui, plugin, python, ...). Versioned, so an update can
;                         install next to a version a running SOLIDWORKS has locked; the previous one is
;                         kept for rolling back.
;     addin_20.png        icon for SOLIDWORKS' add-in list (stable path)
; Registry (same keys as tools\registration.ps1):
;   HKCU  COM class -> app-<version>\CADMaster.dll, AddInsStartup, Software\CADMaster (install info)
;   HKLM  SOFTWARE\SolidWorks\AddIns\{guid}: SOLIDWORKS reads its add-in list only from HKLM, so this one
;         key is written through a single Windows administrator prompt (reg.exe, elevated).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef StageDir
  #define StageDir "..\build\stage"
#endif

#define AddinGuid "C4D7A1E2-5B39-4F6A-9E1D-2A8B7C3F5D10"
#define ProgId "CADMaster.SwAddin"
#define AsmName "CADMaster, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
#define VerDir "app-" + AppVersion
#define RepoUrl "https://github.com/Nilove2018/cad-master-solidworks"

[Setup]
AppId={{8B1F6C2E-6A2D-4C1B-9B7E-0CADA57E5001}
AppName=CAD Master
AppVersion={#AppVersion}
AppVerName=CAD Master {#AppVersion}
AppPublisher=Nilove Mandal
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=Copyright (c) 2026 Nilove Mandal
VersionInfoVersion={#Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)}
VersionInfoDescription=CAD Master setup: Claude Code inside SOLIDWORKS
DefaultDirName={localappdata}\Programs\CAD Master
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\build\release
OutputBaseFilename=CADMaster-Setup-{#AppVersion}
SetupIconFile=..\assets\cadmaster.ico
UninstallDisplayIcon={app}\cadmaster.ico
UninstallDisplayName=CAD Master for SOLIDWORKS
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
; CAD Master.exe won't start SOLIDWORKS while this exists: SOLIDWORKS would load a half-installed add-in
SetupMutex=CADMaster.Setup
SetupLogging=yes

[Messages]
WelcomeLabel2=This installs [name/ver]: Claude Code inside SOLIDWORKS.%n%nYou can keep SOLIDWORKS open; the new version loads the next time SOLIDWORKS starts.%n%nWindows will ask once for administrator approval, so SOLIDWORKS lists CAD Master in its add-ins.

[Tasks]
Name: "desktopicon"; Description: "Put a CAD Master shortcut on the desktop"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}\{#VerDir}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#StageDir}\icons\addin_20.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\cadmaster.ico"; DestDir: "{app}"; Flags: ignoreversion
; The launcher sits at a stable path for the shortcuts; in-app updates don't need to replace it
Source: "{#StageDir}\CAD Master.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\CAD Master\CAD Master"; Filename: "{app}\CAD Master.exe"; IconFilename: "{app}\cadmaster.ico"; Comment: "Open SOLIDWORKS with the CAD Master panel"
Name: "{userprograms}\CAD Master\Uninstall CAD Master"; Filename: "{uninstallexe}"; IconFilename: "{app}\cadmaster.ico"; Comment: "Remove CAD Master from this PC"
Name: "{userdesktop}\CAD Master"; Filename: "{app}\CAD Master.exe"; IconFilename: "{app}\cadmaster.ico"; Comment: "Open SOLIDWORKS with the CAD Master panel"; Tasks: desktopicon

[Registry]
; Per-user COM class (HKCU overrides HKLM, including the InprocServer32\1.0.0.0 subkey .NET reads)
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}"; ValueType: string; ValueData: "{#ProgId}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32"; ValueType: string; ValueData: "mscoree.dll"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32"; ValueType: string; ValueName: "Class"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AsmName}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:CodeBase}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "Class"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AsmName}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:CodeBase}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\ProgId"; ValueType: string; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{{#AddinGuid}}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\CLSID"; ValueType: string; ValueData: "{{{#AddinGuid}}"
; Load at SOLIDWORKS start-up
Root: HKCU; Subkey: "Software\SolidWorks\AddInsStartup\{{{#AddinGuid}}"; ValueType: dword; ValueData: 1; Flags: uninsdeletekey
; Install info for the in-app updater
Root: HKCU; Subkey: "Software\CADMaster"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\CADMaster"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"
Root: HKCU; Subkey: "Software\CADMaster"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKCU; Subkey: "Software\CADMaster"; ValueType: string; ValueName: "AppDir"; ValueData: "{app}\{#VerDir}"

[UninstallDelete]
; Every app-<version> folder, including ones the in-app updater added
Type: filesandordirs; Name: "{app}"

[Code]
const
  AddinKey = 'SOFTWARE\SolidWorks\AddIns\{{#AddinGuid}}';
  WebView2Client = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Client32 = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  PreviousVersion: String;

function CodeBase(Param: String): String;
var S: String;
begin
  S := ExpandConstant('{app}\{#VerDir}\CADMaster.dll');
  StringChangeEx(S, '\', '/', True);
  Result := 'file:///' + S;
end;

function IsProcessRunning(Name: String): Boolean;
var Locator, Svc, Found: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Svc := Locator.ConnectServer('.', 'root\CIMV2');
    Found := Svc.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = "' + Name + '"');
    Result := Found.Count > 0;
  except
  end;
end;

function SolidWorksInstalled: Boolean;
var Names: TArrayOfString; I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\SolidWorks', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if (Pos('SOLIDWORKS 20', Names[I]) = 1) and RegKeyExists(HKLM64, 'SOFTWARE\SolidWorks\' + Names[I] + '\Setup') then
        Result := True;
end;

function WebView2Installed: Boolean;
var V: String;
begin
  Result := (RegQueryStringValue(HKLM64, WebView2Client32, 'pv', V) and (V <> '') and (V <> '0.0.0.0')) or
            (RegQueryStringValue(HKLM64, WebView2Client, 'pv', V) and (V <> '') and (V <> '0.0.0.0')) or
            (RegQueryStringValue(HKCU, WebView2Client, 'pv', V) and (V <> '') and (V <> '0.0.0.0'));
end;

function InitializeSetup: Boolean;
var Code: Integer;
begin
  Result := True;
  if not RegQueryStringValue(HKCU, 'Software\CADMaster', 'Version', PreviousVersion) then PreviousVersion := '';
  if not SolidWorksInstalled then
    Result := MsgBox('SOLIDWORKS doesn''t seem to be installed on this PC. CAD Master is an add-in for SOLIDWORKS 2025.' + #13#10#13#10 +
                     'Install anyway?', mbConfirmation, MB_YESNO) = IDYES;
  if Result and not WebView2Installed then
    if MsgBox('CAD Master needs the Microsoft Edge WebView2 Runtime (part of Windows 11, a free download for Windows 10).' + #13#10#13#10 +
              'Open the download page now? You can finish installing CAD Master either way.', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://developer.microsoft.com/microsoft-edge/webview2/', '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

// Runs reg.exe elevated (one administrator prompt) to write or delete SOLIDWORKS' add-in list entry.
function RunElevatedReg(Args: String): Boolean;
var Code: Integer;
begin
  Result := ShellExec('runas', ExpandConstant('{cmd}'), '/c ' + Args, '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

procedure RegisterWithSolidWorks;
var Key, Icon, Args: String;
begin
  Key := '"HKLM\' + AddinKey + '"';
  Icon := ExpandConstant('{app}\addin_20.png');
  if RegKeyExists(HKLM64, AddinKey) then
  begin
    // Present from an earlier install: nothing to do unless the icon path moved.
    if RegQueryStringValue(HKLM64, AddinKey, 'Icon Path', Args) and (CompareText(Args, Icon) = 0) then Exit;
  end;
  Args := 'reg add ' + Key + ' /ve /t REG_DWORD /d 0 /f /reg:64' +
          ' && reg add ' + Key + ' /v Title /t REG_SZ /d "CAD Master" /f /reg:64' +
          ' && reg add ' + Key + ' /v Description /t REG_SZ /d "Claude Code inside SOLIDWORKS: plan, design and automate with Claude" /f /reg:64' +
          ' && reg add ' + Key + ' /v "Icon Path" /t REG_SZ /d "' + Icon + '" /f /reg:64';
  if not RunElevatedReg(Args) then
    MsgBox('CAD Master is installed, but SOLIDWORKS won''t list it until Windows administrator approval is given.' + #13#10#13#10 +
           'Run this installer again and choose Yes when Windows asks.', mbInformation, MB_OK);
end;

// Keep the version just installed and the one installed before it (for rolling back); delete other
// app-* folders. A folder a running SOLIDWORKS still has locked is skipped and removed next time.
procedure RemoveOldVersions;
var FR: TFindRec; Keep: String;
begin
  Keep := '';
  if (PreviousVersion <> '') and (PreviousVersion <> '{#AppVersion}') then Keep := 'app-' + PreviousVersion;
  if FindFirst(ExpandConstant('{app}\app-*'), FR) then
  try
    repeat
      if ((FR.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
         (CompareText(FR.Name, '{#VerDir}') <> 0) and (CompareText(FR.Name, Keep) <> 0) then
        DelTree(ExpandConstant('{app}\') + FR.Name, True, True, True);
    until not FindNext(FR);
  finally
    FindClose(FR);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RegisterWithSolidWorks;
    RemoveOldVersions;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and IsProcessRunning('SLDWORKS.exe') then
    WizardForm.FinishedLabel.Caption := 'CAD Master {#AppVersion} is installed.' + #13#10#13#10 +
      'SOLIDWORKS is running: restart it to load this version.';
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if IsProcessRunning('SLDWORKS.exe') then
  begin
    MsgBox('Please close SOLIDWORKS first, then uninstall CAD Master again.', mbInformation, MB_OK);
    Result := False;
  end;
end;

// Deletes %LOCALAPPDATA%\CADMaster item by item, so one file in use (a leftover WebView2 process can hold
// the WebView2 cache) doesn't stop the rest being deleted.
procedure DeleteUserData;
var Dir: String; FR: TFindRec;
begin
  Dir := ExpandConstant('{localappdata}\CADMaster');
  if FindFirst(Dir + '\*', FR) then
  try
    repeat
      if (FR.Name <> '.') and (FR.Name <> '..') then
        if (FR.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          DelTree(Dir + '\' + FR.Name, True, True, True)
        else
          DeleteFile(Dir + '\' + FR.Name);
    until not FindNext(FR);
  finally
    FindClose(FR);
  end;
  RemoveDir(Dir);
  if DirExists(Dir) then
    MsgBox('Some CAD Master files were in use and were not deleted. Restart Windows, then delete this folder:' + #13#10#13#10 + Dir,
           mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegKeyExists(HKLM64, AddinKey) then
      if not RunElevatedReg('reg delete "HKLM\' + AddinKey + '" /f /reg:64') then
        MsgBox('SOLIDWORKS still lists CAD Master in Tools > Add-Ins because Windows administrator approval wasn''t given. ' +
               'It does nothing; you can untick it there.', mbInformation, MB_OK);
    if MsgBox('Also delete your CAD Master settings, logs and history (%LOCALAPPDATA%\CADMaster)?' + #13#10#13#10 +
              'Your designs and your Claude Code chats are not affected.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DeleteUserData;
  end;
end;
