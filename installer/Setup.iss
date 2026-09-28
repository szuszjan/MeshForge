; Skrypt instalatora MeshForge (Inno Setup 6).
; Kompilacja: ISCC.exe Setup.iss   (albo otwórz w Inno Setup Compiler i naciśnij F9)
; Wymaga wcześniejszego "dotnet publish" - patrz komendę w installer/build.ps1.

#define MyAppName "MeshForge"
#define MyAppVersion "1.1.1"
#define MyAppExeName "MeshForge.App.exe"
#define MyPublishDir "..\publish\win-x64"

[Setup]
AppId={{8A8437DE-ACD3-4E04-8AF4-1D0C21B41C5E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
OutputDir=..\installer_output
OutputBaseFilename=MeshForge-Setup-{#MyAppVersion}
SetupIconFile=..\src\MeshForge.App\Assets\icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe skróty:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\przyklady\*"; DestDir: "{app}\przyklady"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Odinstaluj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Uruchom {#MyAppName}"; Flags: nowait postinstall skipifsilent
