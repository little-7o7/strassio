<#
.SYNOPSIS
    Собирает Strassio и копирует аддон в папку Addons каждой найденной версии CorelDRAW.

.DESCRIPTION
    Скрипт сам находит установленные CorelDRAW (X7 и новее, 32 и 64 бит): сначала по реестру,
    потом обычным поиском CorelDRW.exe в Program Files. Рядом с CorelDRW.exe лежит папка Addons —
    туда и ставится плагин, в подпапку Strassio.

    Папка Program Files защищена Windows, поэтому запускать нужно от имени администратора.
    CorelDRAW при этом должен быть ЗАКРЫТ — иначе файлы заняты и Windows не даст их заменить.

.PARAMETER Configuration
    Debug (по умолчанию) или Release. Debug кладёт рядом .pdb — в сообщениях об ошибках
    будут видны номера строк, так проще разбираться.

.PARAMETER NoBuild
    Не собирать заново, взять уже собранные файлы.

.PARAMETER ListOnly
    Ничего не копировать, только показать найденные версии CorelDRAW.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install-addon.ps1
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoBuild,
    [switch] $ListOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Strassio.Corel\Strassio.Corel.csproj'
$outputDir = Join-Path $repoRoot "src\Strassio.Corel\bin\$Configuration\net48"

function Write-Step($text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Write-Ok($text) { Write-Host "   $text" -ForegroundColor Green }
function Write-Warn($text) { Write-Host "   $text" -ForegroundColor Yellow }
function Write-Fail($text) { Write-Host "   $text" -ForegroundColor Red }

# --- Поиск установленных CorelDRAW -------------------------------------------------------------

function Get-CorelInstallations {
    $found = @{}

    # 1. Реестр: Corel записывает сюда путь к папке Programs каждой установленной версии.
    $registryRoots = @(
        'HKLM:\SOFTWARE\Corel\Setup',
        'HKLM:\SOFTWARE\WOW6432Node\Corel\Setup'
    )

    foreach ($root in $registryRoots) {
        if (-not (Test-Path $root)) { continue }

        Get-ChildItem -Path $root -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $props = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
            foreach ($name in @('ProgramsDir', 'Programs64Dir', 'InstallDir', 'ProductInstallPath')) {
                $value = $props.$name
                if ([string]::IsNullOrWhiteSpace($value)) { continue }

                $exe = Join-Path $value 'CorelDRW.exe'
                if (Test-Path $exe) { $found[(Resolve-Path $exe).Path] = $true }
            }
        }
    }

    # 2. Обычный поиск по диску — работает и когда в реестре ничего внятного нет.
    $searchRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) |
        Where-Object { $_ -and (Test-Path (Join-Path $_ 'Corel')) } |
        ForEach-Object { Join-Path $_ 'Corel' }

    foreach ($root in $searchRoots) {
        Get-ChildItem -Path $root -Filter 'CorelDRW.exe' -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object { $found[$_.FullName] = $true }
    }

    $result = @()
    foreach ($exePath in $found.Keys) {
        $programsDir = Split-Path -Parent $exePath        # ...\Programs64  или  ...\Programs
        $suiteDir = Split-Path -Parent $programsDir       # ...\CorelDRAW Graphics Suite 2021
        $versionInfo = (Get-Item $exePath).VersionInfo

        # Inline-if внутри @{} не понимает PowerShell 5.1 — считаем заранее.
        $bitness = '32 бита'
        if ((Split-Path -Leaf $programsDir) -eq 'Programs64') { $bitness = '64 бита' }

        # У новых версий папка называется просто числом (…\CorelDRAW Graphics Suite\Programs64),
        # у старых — целиком (…\CorelDRAW Graphics Suite 2018\Programs64).
        $name = Split-Path -Leaf $suiteDir
        if ($name -match '^\d+$') {
            $name = (Split-Path -Leaf (Split-Path -Parent $suiteDir)) + ' ' + $name
        }

        $result += [pscustomobject]@{
            Name        = $name
            Version     = $versionInfo.ProductVersion
            Bitness     = $bitness
            ProgramsDir = $programsDir
            AddonDir    = Join-Path $programsDir 'Addons\Strassio'
        }
    }

    return $result | Sort-Object Name, Bitness
}

# --- Проверки ----------------------------------------------------------------------------------

Write-Step 'Проверка окружения'

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if ($isAdmin) {
    Write-Ok 'Права администратора есть.'
} else {
    Write-Warn 'Нет прав администратора — копирование в Program Files, скорее всего, не пройдёт.'
    Write-Warn 'Закройте это окно и запустите PowerShell «от имени администратора».'
}

$running = Get-Process -Name 'CorelDRW' -ErrorAction SilentlyContinue
if ($running) {
    Write-Fail 'CorelDRAW сейчас запущен — файлы плагина заняты, заменить их нельзя.'
    Write-Fail 'Закройте CorelDRAW полностью и запустите скрипт заново.'
    if (-not $ListOnly) { exit 1 }
} else {
    Write-Ok 'CorelDRAW закрыт.'
}

# --- Сборка ------------------------------------------------------------------------------------

if (-not $NoBuild -and -not $ListOnly) {
    Write-Step "Сборка ($Configuration)"

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Fail 'Не найден dotnet. Соберите проект в Visual Studio и запустите скрипт с ключом -NoBuild.'
        exit 1
    }

    & dotnet build $projectPath -c $Configuration -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'Сборка не прошла — смотрите ошибки выше.'
        exit 1
    }

    Write-Ok 'Собрано.'
}

# --- Что копируем ------------------------------------------------------------------------------

$filesToCopy = @('Strassio.Corel.dll', 'Strassio.Core.dll', 'AppUI.xslt', 'UserUI.xslt', 'CorelDrw.addon')
$optionalFiles = @('Strassio.Corel.pdb', 'Strassio.Core.pdb')

if (-not $ListOnly) {
    foreach ($file in $filesToCopy) {
        $path = Join-Path $outputDir $file
        if (-not (Test-Path $path)) {
            Write-Fail "Не найден файл сборки: $path"
            Write-Fail 'Соберите проект (без ключа -NoBuild) и попробуйте снова.'
            exit 1
        }
    }
}

# --- Поиск CorelDRAW ---------------------------------------------------------------------------

Write-Step 'Поиск установленных CorelDRAW'

$installations = @(Get-CorelInstallations)

if ($installations.Count -eq 0) {
    Write-Fail 'CorelDRAW не найден ни в реестре, ни в Program Files.'
    Write-Fail 'Скопируйте файлы вручную — см. docs/УСТАНОВКА.md.'
    exit 1
}

foreach ($install in $installations) {
    Write-Ok "$($install.Name)  [$($install.Bitness), версия $($install.Version)]"
    Write-Host "      $($install.AddonDir)" -ForegroundColor DarkGray
}

if ($ListOnly) { exit 0 }

# --- Копирование -------------------------------------------------------------------------------

Write-Step 'Копирование аддона'

$installed = 0
foreach ($install in $installations) {
    try {
        New-Item -ItemType Directory -Path $install.AddonDir -Force | Out-Null

        foreach ($file in $filesToCopy) {
            Copy-Item -Path (Join-Path $outputDir $file) -Destination $install.AddonDir -Force
        }

        foreach ($file in $optionalFiles) {
            $path = Join-Path $outputDir $file
            if (Test-Path $path) { Copy-Item -Path $path -Destination $install.AddonDir -Force }
        }

        Write-Ok "$($install.Name) — готово."
        $installed++
    } catch {
        Write-Fail "$($install.Name) — не получилось: $($_.Exception.Message)"
    }
}

Write-Step 'Итог'

if ($installed -eq 0) {
    Write-Fail 'Ни в одну версию скопировать не удалось.'
    exit 1
}

Write-Ok "Установлено в $installed из $($installations.Count) версий CorelDRAW."
Write-Host ''
Write-Host '   Дальше:' -ForegroundColor White
Write-Host '   1. Запустите CorelDRAW и создайте новый документ.'
Write-Host '   2. Вверху должна появиться панель «Strassio» с кнопкой.'
Write-Host '      Если панели нет — «Окно → Докеры → Strassio».'
Write-Host '   3. Нажмите кнопку — справа откроется докер Strassio.'
Write-Host ''
