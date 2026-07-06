; ------------------------------------------------------------
; NOVATLAS - Nova-Fiches Setup (Inno Setup)
; Version : 2.3.0.99
; A placer à la racine du dossier :
; C:\Users\micro\Downloads\00 - DEV\Nova-Fiches_2.3.0.99_FIX17_UI_LEVE_XY_DISPLAY
;
; Source attendue : Installer\staging\publish
; Sortie MSI/EXE  : Installer\out
; ------------------------------------------------------------

#define MyAppName        "Nova-Fiches"
#define MyCompanyName    "NOVATLAS"
#define MyAppExeName     "NovaFiches.exe"

#ifndef MyAppVersion
  #define MyAppVersion "2.3.0.99"
#endif

[Setup]
AppId={{B5B4A6B2-5D4E-4D45-9E74-9C4C8B8F0A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyCompanyName}

DefaultDirName={autopf}\{#MyCompanyName}\{#MyAppName}
DefaultGroupName={#MyCompanyName}\{#MyAppName}

OutputDir=Installer\out
OutputBaseFilename=NovaFiches_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

PrivilegesRequired=admin
WizardStyle=modern
DisableProgramGroupPage=yes

CloseApplications=yes
CloseApplicationsFilter=NovaFiches.exe
RestartApplications=no

UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer une icône sur le Bureau"; GroupDescription: "Raccourcis:"; Flags: unchecked
Name: "startmenuicon"; Description: "Créer un raccourci dans le menu Démarrer"; GroupDescription: "Raccourcis:"; Flags: checkedonce

[Files]
; Publication .NET générée par src\NovaFiches\build.ps1
Source: "Installer\staging\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; WebView2 offline installer optionnel.
; Si absent, la compilation continue.
; Emplacement attendu si besoin : Installer\inno\Prerequisites\WebView2.exe
Source: "Installer\inno\Prerequisites\WebView2.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist; Check: not WebView2RuntimeExists()

[Icons]
Name: "{commondesktop}\{#MyCompanyName} - {#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonprograms}\{#MyCompanyName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{commonprograms}\{#MyCompanyName}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{tmp}\WebView2.exe"; Parameters: "/install /silent /norestart"; \
  StatusMsg: "Préparation du moteur Microsoft WebView2..."; \
  Flags: waituntilterminated runhidden skipifdoesntexist; \
  Check: not WebView2RuntimeExists()

Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function WebView2RuntimeExists(): Boolean;
begin
  Result := False;

  if RegKeyExists(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') then Result := True;
  if not Result then if RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') then Result := True;
  if not Result then if RegKeyExists(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') then Result := True;

  if Result then Exit;

  if FileExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application\msedgewebview2.exe')) then Result := True;
  if not Result then if DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')) then Result := True;
  if not Result then if DirExists(ExpandConstant('{pf}\Microsoft\EdgeWebView\Application')) then Result := True;
end;

