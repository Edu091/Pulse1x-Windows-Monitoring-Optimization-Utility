; Instalador silencioso do Pulse1x. A cópia de arquivos/atalhos roda elevada (PrivilegesRequired=admin)
; e suporta /VERYSILENT de verdade (sem nenhuma UI) — diferente do Pulse1x.App.exe em si, que sempre
; abre sua janela principal. Aqui só a ETAPA DE INSTALAÇÃO é silenciosa; abrir o app depois é que mostra UAC+janela.
;
; AppVersion e SourceExeDir são parametrizáveis via linha de comando (/DAppVersion=X.Y.Z
; /DSourceExeDir=caminho), usados pelo workflow de release do GitHub Actions — que publica o
; .exe num diretório temporário do runner, diferente do caminho fixo usado no publish local
; (atualizar_pulse1x.bat). Os #ifndef abaixo mantêm o build manual funcionando sem argumentos.
#ifndef AppVersion
  #define AppVersion "1.10.0"
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
; Os DOIS nomes: o instalador roda elevado e, nesse contexto, pode não enxergar um mutex criado
; na sessão do usuário — sem ver o app aberto, ele não o fecharia e falharia ao substituir o .exe
; em uso. O "Global\" resolve isso; o local é mantido para continuar detectando versões antigas
; (1.4.6 e anteriores), que só criavam esse.
AppMutex=Global\Pulse1x_SingleInstance_Mutex,Pulse1x_SingleInstance_Mutex
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

; ---- Atualização por cima, sem desinstalar ----
; O AppId acima é a identidade da instalação: com ele igual, rodar um Setup mais novo ATUALIZA a
; instalação existente em vez de criar uma segunda. As diretivas abaixo tornam isso explícito e
; tiram os atritos que apareciam ao atualizar com o app aberto:
;   • CloseApplications   — o instalador fecha o Pulse1x em execução (achado pelo AppMutex) em vez
;                           de falhar com "arquivo em uso";
;   • RestartApplications — e o reabre ao terminar, para a atualização ser transparente;
;   • UsePreviousAppDir   — reinstala na mesma pasta escolhida da primeira vez;
;   • UsePreviousTasks    — mantém a escolha de atalho na Área de Trabalho.
CloseApplications=yes
RestartApplications=yes
CloseApplicationsFilter=Pulse1x.App.exe
UsePreviousAppDir=yes
UsePreviousTasks=yes
; A versão aparece corretamente em "Aplicativos instalados" e permite ao Windows comparar builds.
VersionInfoVersion={#AppVersion}
UninstallDisplayName=Pulse1x

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
; shellexec é essencial aqui: por padrão o Inno Setup lança entradas [Run] via CreateProcess, que NÃO
; consegue elevar um processo (só ShellExecute/UAC conseguem). Como Pulse1x.App.exe exige elevação
; (requireAdministrator no app.manifest), sem essa flag o lançamento pós-instalação falha com
; "CreateProcess falhou; código 740: a operação solicitada requer elevação" (bug corrigido em 2026-07-13).
Filename: "{app}\Pulse1x.App.exe"; Description: "{cm:LaunchProgram,Pulse1x}"; Flags: nowait postinstall skipifsilent shellexec
