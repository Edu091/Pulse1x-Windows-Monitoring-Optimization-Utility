; Instalador silencioso do Pulse1x. A cópia de arquivos/atalhos roda elevada (PrivilegesRequired=admin)
; e suporta /VERYSILENT de verdade (sem nenhuma UI) — diferente do Pulse1x.App.exe em si, que sempre
; abre sua janela principal. Aqui só a ETAPA DE INSTALAÇÃO é silenciosa; abrir o app depois é que mostra UAC+janela.
#define AppVersion "1.0.0"

[Setup]
AppId={{8C2E7B1A-9F4D-4A6B-9E3C-1D7F5A2B6C90}
AppName=Pulse1x
AppVersion={#AppVersion}
AppPublisher=Eduardo Almeida Bedin
AppPublisherURL=https://github.com/Edu091/Pulse1x-Windows-Monitoring-Optimization-Utility
DefaultDirName={autopf}\Pulse1x
DefaultGroupName=Pulse1x
UninstallDisplayIcon={app}\Pulse1x.App.exe
OutputDir=C:\Pulse1x\installer-output
OutputBaseFilename=Pulse1x-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\Pulse1x.App\Resources\pulse1x.ico
DisableProgramGroupPage=yes
WizardStyle=modern

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "C:\Pulse1x\release-public\Pulse1x.App.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Pulse1x"; Filename: "{app}\Pulse1x.App.exe"
Name: "{group}\{cm:UninstallProgram,Pulse1x}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Pulse1x"; Filename: "{app}\Pulse1x.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Pulse1x.App.exe"; Description: "{cm:LaunchProgram,Pulse1x}"; Flags: nowait postinstall skipifsilent
