#define MyAppName "OmniHID Taskbar Battery Indicator"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.4"
#endif
#define MyAppPublisher "nikpsov"
#define MyAppURL "https://github.com/nikpsov/omni-hid-taskbar-battery-indicator"
#define MyAppExeName "OmniHidTaskbar.exe"

[Setup]
AppId={{E8B7A1C2-5D4E-4A3B-8C9F-2E1A3D4B5C6E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
ArchitecturesInstallIn64BitMode=x64
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=omni-hid-taskbar-v{#MyAppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.RemoveSettingsPrompt=Do you want to delete all configuration and settings files (%APPDATA%\OmniHidTaskbar)?
russian.RemoveSettingsPrompt=Удалить все файлы настроек и конфигурации программы (%APPDATA%\OmniHidTaskbar)?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Run at Windows startup"; GroupDescription: "Additional tasks:"

[Files]
Source: "..\bin\OmniHidTaskbar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\OmniHidTaskbarDebug.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\OmniHid.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.ru.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: files; Name: "{app}\settings.json"
Type: files; Name: "{app}\debug.log*"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OmniHidTaskbar"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/im ""OmniHidTaskbar.exe"" /f /t"; Flags: runhidden; RunOnceId: "Kill GUI exe"
Filename: "taskkill"; Parameters: "/im ""OmniHidTaskbarDebug.exe"" /f /t"; Flags: runhidden; RunOnceId: "Kill Debug exe"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/im OmniHidTaskbar.exe /f /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill', '/im OmniHidTaskbarDebug.exe /f /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/im OmniHidTaskbar.exe /f /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill', '/im OmniHidTaskbarDebug.exe /f /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\OmniHidTaskbar');
    if DirExists(AppDataDir) then
    begin
      if (not UninstallSilent()) and (MsgBox(CustomMessage('RemoveSettingsPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      begin
        DelTree(AppDataDir, True, True, True);
      end;
    end;
  end;
end;