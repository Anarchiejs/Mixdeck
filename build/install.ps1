# Installe Mixdeck sur ce PC depuis les sources, comme une vraie version :
# SDK .NET si besoin, Voicemeeter si besoin, compilation, paquet Velopack, puis installation.
# Lance par "Installer Mixdeck.cmd" a la racine du depot. PowerShell 5.1, sans droits administrateur.
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(HelpMessage = 'Lance aussi les tests avant d''installer.')]
    [switch]$RunTests,

    [Parameter(HelpMessage = 'Ne pose aucune question (Voicemeeter n''est alors pas installe s''il manque).')]
    [switch]$NoPrompt
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Fail {
    param([string]$Message)
    Write-Host ''
    Write-Host "ERREUR : $Message" -ForegroundColor Red
    exit 1
}

trap {
    Fail $_.Exception.Message
}

function Ask-YesNo {
    param([string]$Question)
    if ($NoPrompt) { return $false }
    $answer = Read-Host "$Question (O/N)"
    return $answer -match '^(o|oui|y|yes)$'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $repoRoot

Write-Host '============================================'
Write-Host '   Installation de Mixdeck'
Write-Host '============================================'

if (-not [Environment]::Is64BitOperatingSystem) {
    Fail 'Mixdeck demande Windows 64 bits.'
}

# --- 1. SDK .NET 10 (global.json : 10.0.403, report latestFeature) ---------------------------

$minimumSdk = [version]'10.0.403'

function Test-Sdk {
    param([string]$DotNetExe)
    if (-not $DotNetExe -or -not (Test-Path -LiteralPath $DotNetExe)) { return $false }
    $lines = & $DotNetExe --list-sdks 2>$null
    foreach ($line in @($lines)) {
        if ("$line" -match '^(\d+)\.(\d+)\.(\d+)') {
            $v = [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])"
            if ($v.Major -eq 10 -and $v -ge $minimumSdk) { return $true }
        }
    }
    return $false
}

$userDotNetDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$candidates = @(
    (Join-Path $userDotNetDir 'dotnet.exe'),
    (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
)
$pathDotNet = Get-Command -Name dotnet -ErrorAction SilentlyContinue
if ($null -ne $pathDotNet -and $pathDotNet.Source) { $candidates += $pathDotNet.Source }

Write-Step 'Recherche du SDK .NET 10'
$dotnet = $candidates | Where-Object { Test-Sdk $_ } | Select-Object -First 1

if (-not $dotnet) {
    Write-Host 'SDK .NET 10 absent : telechargement depuis Microsoft (dot.net), installation pour cet utilisateur.'
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 10.0 -InstallDir $userDotNetDir -NoPath
    if ($LASTEXITCODE -ne 0) { Fail "L'installation du SDK .NET a echoue (code $LASTEXITCODE)." }
    $dotnet = Join-Path $userDotNetDir 'dotnet.exe'
    if (-not (Test-Sdk $dotnet)) { Fail "SDK .NET installe mais introuvable dans $userDotNetDir." }
}

# pack.ps1 prend le premier dotnet du PATH.
$env:PATH = (Split-Path -Parent $dotnet) + ';' + $env:PATH
Write-Host "SDK : $dotnet"

# --- 2. Voicemeeter ------------------------------------------------------------------------

Write-Step 'Recherche de Voicemeeter'
$vmKeys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}'
)
$vmInstalled = @($vmKeys | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0

if ($vmInstalled) {
    Write-Host 'Voicemeeter est installe.'
} else {
    Write-Host 'Voicemeeter n''est pas installe. Mixdeck en a besoin pour fonctionner.' -ForegroundColor Yellow
    $winget = Get-Command -Name winget -ErrorAction SilentlyContinue
    if ($null -ne $winget -and (Ask-YesNo 'Installer Voicemeeter Banana maintenant avec winget ?')) {
        & $winget.Source install --id VB-Audio.Voicemeeter.Banana -e --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            Write-Host "winget a retourne le code $LASTEXITCODE. Installez Voicemeeter a la main : https://vb-audio.com/Voicemeeter/" -ForegroundColor Yellow
        } else {
            Write-Host 'Voicemeeter installe. Redemarrez Windows apres l''installation de Mixdeck pour activer ses pilotes audio.' -ForegroundColor Yellow
        }
    } else {
        Write-Host 'Mixdeck sera installe quand meme. Installez Voicemeeter ensuite : https://vb-audio.com/Voicemeeter/' -ForegroundColor Yellow
    }
}

# --- 3. Compilation et paquet Velopack ------------------------------------------------------

# Version locale toujours croissante, et plus petite que la version publiee de meme numero :
# les mises a jour GitHub prendront le relais des la premiere publication.
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$baseVersion = "$($props.Project.PropertyGroup[0].Version)".Trim()
if ($baseVersion -notmatch '^\d+\.\d+\.\d+$') { $baseVersion = '0.1.0' }
$version = "$baseVersion-local.$(Get-Date -Format 'yyyyMMddHHmm')"

$outputDir = Join-Path $repoRoot 'artifacts\install'
if (Test-Path -LiteralPath $outputDir) { Remove-Item -LiteralPath $outputDir -Recurse -Force }

Write-Step "Compilation de Mixdeck $version (quelques minutes la premiere fois)"
$packArgs = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $PSScriptRoot 'pack.ps1'),
    '-Version', $version,
    '-OutputDir', $outputDir
)
if (-not $RunTests) { $packArgs += '-SkipTests' }
& powershell @packArgs
if ($LASTEXITCODE -ne 0) { Fail "La compilation a echoue (code $LASTEXITCODE). Le detail est au-dessus." }

$setup = Get-ChildItem -LiteralPath $outputDir -Filter '*Setup.exe' | Select-Object -First 1
if ($null -eq $setup) { Fail "Installateur introuvable dans $outputDir." }

# --- 4. Installation ------------------------------------------------------------------------

$running = Get-Process -Name Mixdeck -ErrorAction SilentlyContinue
if ($running) {
    Write-Step 'Fermeture de Mixdeck en cours d''execution'
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$installDir = Join-Path $env:LOCALAPPDATA 'MixdeckApp'
$updateExe = Join-Path $installDir 'Update.exe'
$manifest = Join-Path $installDir 'current\sq.version'
if ((Test-Path -LiteralPath $updateExe) -and (Test-Path -LiteralPath $manifest)) {
    # Deja installe : Setup.exe ouvrirait une boite "reparer / ecraser". On passe par la mise a jour Velopack.
    $package = Get-ChildItem -LiteralPath $outputDir -Filter '*-full.nupkg' | Select-Object -First 1
    if ($null -eq $package) { Fail "Paquet complet introuvable dans $outputDir." }
    Write-Step 'Mise a jour de l''installation existante'
    $exe = $updateExe
    $arguments = @('apply', '--silent', '--package', "`"$($package.FullName)`"")
} else {
    Write-Step 'Installation'
    $exe = $setup.FullName
    $arguments = @('--silent')
}
# Pas de -Wait : il attendrait aussi Mixdeck, que l'installateur lance a la fin.
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { Fail "L'installateur a retourne le code $($process.ExitCode). Journal : $env:LOCALAPPDATA\velopack\velopack.log" }

# Le code de retour ne suffit pas : on relit la version vraiment installee.
$installedVersion = $null
for ($i = 0; $i -lt 30 -and $installedVersion -ne $version; $i++) {
    if (Test-Path -LiteralPath $manifest) {
        [xml]$nuspec = Get-Content -LiteralPath $manifest -Raw
        $installedVersion = "$($nuspec.package.metadata.version)".Trim()
    }
    if ($installedVersion -ne $version) { Start-Sleep -Seconds 1 }
}
if ($installedVersion -ne $version) {
    Fail "Installation non confirmee (version installee : '$installedVersion', attendue : '$version'). Journal : $env:LOCALAPPDATA\velopack\velopack.log"
}

# Setup --silent ne lance pas l'application, la mise a jour si.
Start-Sleep -Seconds 2
if (-not (Get-Process -Name Mixdeck -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath (Join-Path $installDir 'Mixdeck.exe') -WorkingDirectory $installDir
}

Write-Host ''
Write-Host '============================================' -ForegroundColor Green
Write-Host "   Mixdeck $version est installe." -ForegroundColor Green
Write-Host '============================================' -ForegroundColor Green
Write-Host "Dossier       : $env:LOCALAPPDATA\MixdeckApp"
Write-Host 'Menu Demarrer : Mixdeck (raccourci aussi sur le Bureau)'
Write-Host 'Desinstaller  : Parametres > Applications > Mixdeck'
Write-Host 'Mettre a jour : relancer ce script apres un git pull.'
exit 0
