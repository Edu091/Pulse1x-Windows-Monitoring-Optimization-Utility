; Instalador silencioso do Pulse1x. A cópia de arquivos/atalhos roda elevada (PrivilegesRequired=admin)
; e suporta /VERYSILENT de verdade (sem nenhuma UI) — diferente do Pulse1x.App.exe em si, que sempre
; abre sua janela principal. Aqui só a ETAPA DE INSTALAÇÃO é silenciosa; abrir o app depois é que mostra UAC+janela.
;
; AppVersion e SourceExeDir são parametrizáveis via linha de comando (/DAppVersion=X.Y.Z
; /DSourceExeDir=caminho), usados pelo workflow de release do GitHub Actions — que publica o
; .exe num diretório temporário do runner, diferente do caminho fixo usado no publish local
; (atualizar_pulse1x.bat). Os #ifndef abaixo mantêm o build manual funcionando sem argumentos.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExeDir
  #define SourceExeDir "C:\Pulse1x\release-public"
#endif
#ifndef OutputDir
  #define OutputDir "C:\Pulse1x\installer-output"
#endif

[Setup]
AppId={{8C2E7B1A-9F4D-4A6B-9E3C-1D7F5A2B6C90}
AppName=Pulse1x
AppVersion={#AppVersion}
; Mesmo nome do mutex de instância única do app (App.xaml.cs) — permite ao Inno Setup detectar
; com precisão o Pulse1x em execução para /CLOSEAPPLICATIONS e /RESTARTAPPLICATIONS (usado pelo
; GitHubUpdateService ao atualizar com o app já aberto).
AppMutex=Pulse1x_SingleInstance_Mutex
AppPublisher=Eduardo Almeida Bedin
AppPublisherURL=https://github.com/Edu091/Pulse1x-Windows-Monitoring-Optimization-Utility
DefaultDirName={autopf}\Pulse1x
DefaultGroupName=Pulse1x
UninstallDisplayIcon={app}\Pulse1x.App.exe
OutputDir={#OutputDir}
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
Source: "{#SourceExeDir}\Pulse1x.App.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; AppUserModelID igual ao definido em App.xaml.cs (SetCurrentProcessExplicitAppUserModelID) — mantém
; a identidade do app estável perante o Shell (Menu Iniciar/busca/barra de tarefas) entre atualizações,
; já que o .exe é substituído no mesmo caminho a cada nova versão instalada.
Name: "{group}\Pulse1x"; Filename: "{app}\Pulse1x.App.exe"; AppUserModelID: "Pulse1x.App"
Name: "{group}\{cm:UninstallProgram,Pulse1x}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Pulse1x"; Filename: "{app}\Pulse1x.App.exe"; Tasks: desktopicon; AppUserModelID: "Pulse1x.App"

[Run]
Filename: "{app}\Pulse1x.App.exe"; Description: "{cm:LaunchProgram,Pulse1x}"; Flags: nowait postinstall skipifsilent
