<#
.SYNOPSIS
    Собирает Strassio в Release и делает установщик Strassio-Setup-<версия>.exe.

.DESCRIPTION
    Готовый .exe кладётся в installer\out\. Его можно просто передать кому угодно —
    он сам найдёт установленные CorelDRAW и предложит выбрать, куда ставить.

    Нужен Inno Setup 6 (https://jrsoftware.org/isdl.php) — бесплатный.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>

[CmdletBinding()]
param(
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Strassio.Corel\Strassio.Corel.csproj'
$issPath = Join-Path $PSScriptRoot 'Strassio.iss'
$outDir = Join-Path $PSScriptRoot 'out'

function Write-Step($text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

# --- Сборка плагина ----------------------------------------------------------------------------

if (-not $NoBuild) {
    Write-Step 'Сборка плагина (Release)'

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host '   Не найден dotnet. Соберите Strassio.sln в Visual Studio и запустите с ключом -NoBuild.' -ForegroundColor Red
        exit 1
    }

    & dotnet build $projectPath -c Release -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host '   Сборка не прошла — смотрите ошибки выше.' -ForegroundColor Red
        exit 1
    }

    Write-Host '   Собрано.' -ForegroundColor Green
}

# --- Обфускация (защита кода, docs/SPEC.md 13.7) -----------------------------------------------
# Obfuscar (бесплатный, ставится как локальный инструмент dotnet: .config\dotnet-tools.json) запутывает
# имена и прячет строки в трёх DLL плагина; установщик берёт уже запутанные копии.

$obfuscatedDir = Join-Path $repoRoot 'src\Strassio.Corel\bin\Release\net48\obfuscated'
$issDefines = @()
if (-not $NoBuild) {
    Write-Step 'Обфускация (Obfuscar)'
    Push-Location $repoRoot
    & dotnet tool restore | Out-Null
    Pop-Location
    Push-Location $PSScriptRoot
    & dotnet tool run obfuscar.console obfuscar.xml | Out-Null
    $obfuscarExit = $LASTEXITCODE
    Pop-Location
    if ($obfuscarExit -ne 0 -or -not (Test-Path (Join-Path $obfuscatedDir 'Strassio.Licensing.dll'))) {
        Write-Host '   Обфускация не прошла — установщик без защиты кода собирать нельзя.' -ForegroundColor Red
        exit 1
    }

    $issDefines += "/DPluginDllDir=$obfuscatedDir"
    Write-Host '   Готово: Strassio.Corel, Strassio.Core, Strassio.Licensing запутаны.' -ForegroundColor Green
}

# --- Поиск компилятора Inno Setup --------------------------------------------------------------

Write-Step 'Поиск Inno Setup'

$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)

$iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}

if (-not $iscc) {
    Write-Host '   Не найден Inno Setup 6.' -ForegroundColor Red
    Write-Host '   Скачайте бесплатно: https://jrsoftware.org/isdl.php' -ForegroundColor Red
    exit 1
}

Write-Host "   $iscc" -ForegroundColor Green

# --- Сборка установщика ------------------------------------------------------------------------

Write-Step 'Сборка установщика'

& $iscc @issDefines $issPath
if ($LASTEXITCODE -ne 0) {
    Write-Host '   Установщик собрать не удалось — смотрите ошибки выше.' -ForegroundColor Red
    exit 1
}

Write-Step 'Готово'

Get-ChildItem -Path $outDir -Filter '*.exe' | ForEach-Object {
    Write-Host "   $($_.FullName)" -ForegroundColor Green
    Write-Host ("   размер: {0:N1} МБ" -f ($_.Length / 1MB)) -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '   Закройте CorelDRAW и запустите этот .exe.' -ForegroundColor White
Write-Host ''
