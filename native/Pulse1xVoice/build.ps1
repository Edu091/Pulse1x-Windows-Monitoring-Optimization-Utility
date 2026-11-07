# Compila o plugin de voz (Pulse1xVoice.dll, VST2 64 bits) e o coloca nos recursos do app.
#
# Requer o Zig 0.13.0 (https://ziglang.org/download/) — um compilador C portátil, sem instalação.
# Uso:  .\build.ps1 -Zig "C:\caminho\zig.exe"
# A DLL gerada é versionada em src\Pulse1x.App\Resources\Native para que o build do app não
# dependa do Zig; só é preciso recompilar quando pulse1x_voice.c mudar.
param([string]$Zig = "zig")

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here "..\..\src\Pulse1x.App\Resources\Native\Pulse1xVoice.dll"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

& $Zig cc -shared -O2 -target x86_64-windows-gnu -fno-sanitize=all -s `
    -o $out (Join-Path $here "pulse1x_voice.c")
if ($LASTEXITCODE -ne 0) { throw "zig cc falhou ($LASTEXITCODE)" }

# zig cc também gera .lib/.pdb ao lado; o app só precisa da DLL.
Get-ChildItem (Split-Path $out) -Include *.lib, *.pdb -Recurse | Remove-Item -Force
Write-Host "OK: $out"
