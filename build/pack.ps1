# Empaquette Mixdeck en local : tests, publication autonome, puis vpk pack.
# PowerShell 5.1. Le SDK est cherche dans %USERPROFILE%\.dotnet, puis dans le PATH.
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, HelpMessage = 'Version semver, par exemple 0.1.0.')]
    [string]$Version,

    [Parameter(HelpMessage = 'Publie l''application mais n''appelle pas vpk pack.')]
    [switch]$SkipPack,

    [Parameter(HelpMessage = 'Ne lance pas les tests avant la publication.')]
    [switch]$SkipTests,

    [Parameter(HelpMessage = 'Dossier de sortie du paquet Velopack (artifacts par defaut).')]
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'

if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message"
}

function Fail {
    param(
        [string]$Message,
        [int]$Code = 1
    )
    Write-Host "ERREUR: $Message" -ForegroundColor Red
    exit $Code
}

trap {
    Write-Host "ERREUR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($Version -notmatch '^\d+\.\d+\.\d+([-+][0-9A-Za-z.-]+)?$') {
    Fail "Version invalide : '$Version'. Format attendu : 0.1.0"
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $repoRoot
Write-Step "Depot : $repoRoot"

function Find-DotNet {
    $preferred = '%USERPROFILE%\.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $preferred) {
        return $preferred
    }
    $command = Get-Command -Name dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command -and $command.Source) {
        return $command.Source
    }
    return $null
}

$dotnet = Find-DotNet
if (-not $dotnet) {
    Fail "SDK .NET introuvable. Installez le SDK 10 (global.json demande 10.0.403) ou placez dotnet.exe dans %USERPROFILE%\.dotnet."
}

$dotnetRoot = Split-Path -Parent $dotnet
$sdkDir = Join-Path $dotnetRoot 'sdk'
if (-not (Test-Path -LiteralPath $sdkDir)) {
    Fail "dotnet trouve ($dotnet) mais aucun SDK dans $sdkDir. Utilisez le SDK de %USERPROFILE%\.dotnet."
}

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_NOLOGO = '1'
$env:PATH = $dotnetRoot + ';' + $env:PATH
Write-Step "SDK : $dotnet"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )
    Write-Step ("dotnet " + ($ArgumentList -join ' '))
    & $dotnet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet a retourne le code $LASTEXITCODE." $LASTEXITCODE
    }
}

Invoke-DotNet -ArgumentList @('tool', 'restore')
if ($SkipTests) {
    Write-Step "Tests ignores (-SkipTests)."
} else {
    Invoke-DotNet -ArgumentList @('test', 'Mixdeck.slnx', '-c', 'Release', '-nologo')
}

$publishDir = Join-Path $repoRoot 'publish'
if (Test-Path -LiteralPath $publishDir) {
    Write-Step "Suppression de la publication precedente."
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Invoke-DotNet -ArgumentList @(
    'publish', 'src/Mixdeck.App/Mixdeck.App.csproj',
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained',
    "-p:Version=$Version",
    '-o', $publishDir,
    '-nologo'
)

$mainExe = Join-Path $publishDir 'Mixdeck.exe'
if (-not (Test-Path -LiteralPath $mainExe)) {
    Fail "Publication terminee sans Mixdeck.exe dans $publishDir."
}
Write-Step "Application publiee : $mainExe"

if ($SkipPack) {
    Write-Step "Empaquetage Velopack ignore (-SkipPack)."
    Write-Host "Termine (version $Version, sans paquet)."
    exit 0
}

$icon = Join-Path $repoRoot 'src\Mixdeck.App\Assets\Mixdeck.ico'
if (-not (Test-Path -LiteralPath $icon)) {
    Fail "Icone introuvable : $icon. vpk pack l'exige (--icon)."
}

$artifacts = if ($OutputDir) { [System.IO.Path]::GetFullPath($OutputDir) } else { Join-Path $repoRoot 'artifacts' }
if (-not (Test-Path -LiteralPath $artifacts)) {
    New-Item -ItemType Directory -Path $artifacts | Out-Null
}

Write-Step "Empaquetage Velopack vers $artifacts"
Invoke-DotNet -ArgumentList @(
    'tool', 'run', 'vpk', '--',
    'pack',
    '--packId', 'MixdeckApp',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'Mixdeck.exe',
    '--packTitle', 'Mixdeck',
    '--runtime', 'win-x64',
    '--icon', $icon,
    '--outputDir', $artifacts
)

Write-Host "Termine. Paquet Velopack dans $artifacts (version $Version)."
exit 0
