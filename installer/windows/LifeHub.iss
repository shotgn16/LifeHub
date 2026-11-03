; LifeHub for Windows — Inno Setup script (built by .github/workflows/desktop.yml).
; Everything LifeHub needs is inside: the app with its own .NET runtime, and Microsoft Edge WebView2
; (the part of Windows that draws the app window; only installed if this PC doesn't have it yet).
; After installing, LifeHub opens its setup assistant: this PC only, PC + phone, household server,
; Docker or Home Assistant — and offers to install the AI (Ollama).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\out\win-x64"
#endif

[Setup]
AppId={{6E0C7B9E-3C3B-4B8F-9E51-2A0D5C1F4E21}
AppName=LifeHub
AppVersion={#AppVersion}
AppVerName=LifeHub {#AppVersion}
AppPublisher=LifeHub
AppPublisherURL=https://github.com/shotgn16/LifeHub
AppSupportURL=https://github.com/shotgn16/LifeHub/tree/main/docs/manual
DefaultDirName={autopf}\LifeHub
DefaultGroupName=LifeHub
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist
OutputBaseFilename=LifeHub-{#AppVersion}-Windows-x64-Setup
SetupIconFile=..\..\lifehub\src\LifeHub\Desktop\lifehub.ico
UninstallDisplayIcon={app}\LifeHub.exe
UninstallDisplayName=LifeHub
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
MinVersion=10.0.17763

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
FinishedLabel=LifeHub is installed.%n%nWhen it opens, a short setup assistant asks how you want to use it (just this PC, this PC + your phone, a household server, Docker or Home Assistant) and can install the free AI for you.

[Tasks]
Name: "desktopicon"; Description: "Put a LifeHub shortcut on the desktop"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{autoprograms}\LifeHub"; Filename: "{app}\LifeHub.exe"; Comment: "Your household organiser"
Name: "{autoprograms}\LifeHub setup assistant"; Filename: "{app}\LifeHub.exe"; Parameters: "--setup"; Comment: "Change how LifeHub runs on this PC"
Name: "{autodesktop}\LifeHub"; Filename: "{app}\LifeHub.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 (draws the LifeHub window)…"; Check: NeedsWebView2; Flags: waituntilterminated
Filename: "{app}\LifeHub.exe"; Description: "Open LifeHub and set it up"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stops and removes LifeHub's background service / sign-in task if one was set up.
Filename: "{app}\LifeHub.exe"; Parameters: "--uninstall-service"; RunOnceId: "RemoveService"; Flags: runhidden waituntilterminated

[Code]
const WebView2Key = '\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasVersion(Root: Integer; Key: String): Boolean;
var v: String;
begin
  Result := RegQueryStringValue(Root, Key, 'pv', v) and (v <> '') and (v <> '0.0.0.0');
end;

function NeedsWebView2: Boolean;
begin
  Result := not (HasVersion(HKLM, 'SOFTWARE\WOW6432Node' + WebView2Key) or
                 HasVersion(HKLM, 'SOFTWARE' + WebView2Key) or
                 HasVersion(HKCU, 'Software' + WebView2Key));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var data: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    data := ExpandConstant('{localappdata}\LifeHub');
    if DirExists(data) and not UninstallSilent then
      if MsgBox('Also delete your LifeHub data (tasks, notes, settings and backups)?' + #13#10 + #13#10 +
                'Choose No to keep it for a later reinstall. It is in:' + #13#10 + data,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(data, True, True, True);
  end;
end;
