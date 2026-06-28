@echo off
REM Publica a nova versao do Pulse1x em uma pasta de "staging" (C:\Pulse1x\staging).
REM Voce NAO precisa fechar o app manualmente: a proxima vez que ele for aberto
REM (pelo atalho da Area de Trabalho), ele mesmo detecta a versao nova no staging,
REM se atualiza e reabre automaticamente.

cd /d "%~dp0"

echo Publicando nova versao em C:\Pulse1x\staging ...
dotnet publish "src\Pulse1x.App\Pulse1x.App.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "C:\Pulse1x\staging" --nologo

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Falha ao publicar. Veja o erro acima.
    pause
    exit /b 1
)

echo.
echo Nova versao pronta em C:\Pulse1x\staging.
echo Abra o Pulse1x pelo atalho da Area de Trabalho: ele vai se atualizar automaticamente.
pause
