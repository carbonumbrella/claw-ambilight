; Installer for ClawAmbilight (Inno Setup 6). Built by ..\build.cmd (dotnet publish -> ..\publish\fd, then ISCC).
; Per-user install (no admin). If the .NET 10 Desktop Runtime is missing it is downloaded from
; Microsoft and installed (that step alone shows UAC).

#define AppName "Подсветка Claw"
#define AppExe "ClawAmbilight.exe"
#define AppVersion GetVersionNumbersString("..\publish\fd\ClawAmbilight.exe")
#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{6C1B7E2A-4F3D-4C8E-9B5A-2E7D1C0A9F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Carbon108
AppComments=Подсветка стиков и ABXY на MSI Claw по картинке на экране
DefaultDirName={localappdata}\Programs\ClawAmbilight
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\publish\installer
OutputBaseFilename=ClawAmbilight-Setup-{#AppVersion}
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "autostart"; Description: "Запускать вместе с Windows (в трее)"; GroupDescription: "Дополнительно:"
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "..\publish\fd\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClawAmbilight"; \
  ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ClawAmbilight"; \
  Flags: deletevalue; Check: not WizardIsTaskSelected('autostart')

[Run]
Filename: "{app}\{#AppExe}"; Description: "Открыть настройки подсветки"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; graceful exit turns the LEDs off before the files go away
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; RunOnceId: "QuitApp"; Flags: runhidden waituntilterminated

[UninstallDelete]
; settings in %APPDATA%\ClawAmbilight are kept on purpose (a reinstall keeps the user's setup)
Type: dirifempty; Name: "{app}"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function RuntimeInstalled: Boolean;
var
  R: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), R);
  if Result then FindClose(R);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Загрузка .NET 10', 'Для работы нужна среда .NET 10 Desktop Runtime от Microsoft.', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and not RuntimeInstalled then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox('Не удалось скачать .NET 10: ' + AddPeriod(GetExceptionMessage) + #13#10 +
          'Установите .NET 10 Desktop Runtime вручную с dotnet.microsoft.com.', mbError, MB_OK, IDOK);
        Result := False;
        Exit;
      end;
    finally
      DownloadPage.Hide;
    end;
    // ShellExec (not Exec) so the runtime installer can raise its own UAC prompt
    if not ShellExec('', ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'), '/install /quiet /norestart', '',
                     SW_SHOW, ewWaitUntilTerminated, Code) or not RuntimeInstalled then
    begin
      SuppressibleMsgBox('.NET 10 не установился (код ' + IntToStr(Code) + ').', mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // stop a running copy (any install location) so the exe can be replaced; it turns the LEDs off
  ExtractTemporaryFile('{#AppExe}');
  if RuntimeInstalled then
    Exec(ExpandConstant('{tmp}\{#AppExe}'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(800);
  Result := '';
end;
