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
    # Empreinte du script officiel le 2026-11-13. Un fichier different n'est pas execute.
    $expectedHash = 'E8B873E18A81E5C4CD8AB69D84DAC8FEAD291D50B3C44633CD7FDDAD709A13D6'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    $actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) { Fail "Empreinte inattendue de dotnet-install.ps1 ($actualHash)." }
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
$version = "$baseVersion-local.$(Get-Date -Format 'yyyyMMddHHmmss')"

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

# --- 5. Integration FL Studio (optionnelle) -------------------------------------------------

try {
    Write-Step 'Integration FL Studio'

    $hardwareDirs = @(
        (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Image-Line\FL Studio\Settings\Hardware'),
        (Join-Path $env:USERPROFILE 'Documents\Image-Line\FL Studio\Settings\Hardware')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique

    if (@($hardwareDirs).Count -eq 0) {
        Write-Host 'FL Studio non trouve, etape ignoree.'
    } else {
        $sourceScript = Join-Path $repoRoot 'fl\Mixdeck\device_Mixdeck.py'
        if (-not (Test-Path -LiteralPath $sourceScript -PathType Leaf)) {
            throw "Script FL Studio introuvable : $sourceScript"
        }
        foreach ($hardwareDir in $hardwareDirs) {
            $mixdeckDir = Join-Path $hardwareDir 'Mixdeck'
            New-Item -ItemType Directory -Path $mixdeckDir -Force | Out-Null
            $destination = Join-Path $mixdeckDir 'device_Mixdeck.py'
            Copy-Item -LiteralPath $sourceScript -Destination $destination -Force
            Write-Host "Script FL Studio : $destination"
        }
    }

    $loopMidiExePaths = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Tobias Erichsen\loopMIDI\loopMIDI.exe'),
        (Join-Path $env:ProgramFiles 'Tobias Erichsen\loopMIDI\loopMIDI.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
    $loopMidiUninstallKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $loopMidiRegistered = $false
    foreach ($key in $loopMidiUninstallKeys) {
        if (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue | Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -match 'loopMIDI' } | Select-Object -First 1) {
            $loopMidiRegistered = $true
            break
        }
    }
    $loopMidiInstalled = (@($loopMidiExePaths).Count -gt 0) -or $loopMidiRegistered

    $loopMidiUrl = 'https://www.tobias-erichsen.de/software/loopmidi.html'
    if ($loopMidiInstalled) {
        Write-Host 'loopMIDI est installe.'
    } elseif ($NoPrompt -or -not (Ask-YesNo 'Installer loopMIDI ? Il cree les ports MIDI dont FL Studio a besoin.')) {
        Write-Host "loopMIDI non installe. Installez-le ensuite : $loopMidiUrl" -ForegroundColor Yellow
    } else {
        $winget = Get-Command -Name winget -ErrorAction SilentlyContinue
        if ($null -eq $winget) {
            Write-Host "winget indisponible. Installez loopMIDI ici : $loopMidiUrl" -ForegroundColor Yellow
        } else {
            Write-Host 'Installation de loopMIDI...'
            & $winget.Source install --id TobiasErichsen.loopMIDI -e --silent --accept-source-agreements --accept-package-agreements
            if ($LASTEXITCODE -ne 0) {
                Write-Host "winget a retourne le code $LASTEXITCODE. Installez loopMIDI : $loopMidiUrl" -ForegroundColor Yellow
            } else {
                Write-Host 'loopMIDI installe.'
            }
        }
    }
    $loopMidiExe = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Tobias Erichsen\loopMIDI\loopMIDI.exe'),
        (Join-Path $env:ProgramFiles 'Tobias Erichsen\loopMIDI\loopMIDI.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1

    # Ports loopMIDI : la liste est gardee dans le registre (nom du port = valeur DWORD 1) et lue au demarrage.
    # loopMIDI reecrit cette liste en quittant : l'arreter AVANT d'ecrire, puis le relancer.
    $portNames = @('Mixdeck to FL', 'FL to Mixdeck')
    $portsKey = 'HKCU:\Software\Tobias Erichsen\loopMIDI\Ports'
    $existingPorts = @()
    if (Test-Path -LiteralPath $portsKey) { $existingPorts = @((Get-Item -LiteralPath $portsKey).GetValueNames()) }
    $missingPorts = @($portNames | Where-Object { $existingPorts -notcontains $_ })
    if ($missingPorts.Count -eq 0) {
        Write-Host 'Ports loopMIDI deja crees.'
    } elseif ($null -eq $loopMidiExe) {
        Write-Host 'loopMIDI introuvable : ports non crees. Creez "Mixdeck to FL" et "FL to Mixdeck" dans loopMIDI.' -ForegroundColor Yellow
    } elseif ($NoPrompt -or -not (Ask-YesNo 'Creer les ports MIDI « Mixdeck to FL » et « FL to Mixdeck » ?')) {
        Write-Host 'Ports loopMIDI non crees.' -ForegroundColor Yellow
    } else {
        Get-Process -Name loopMIDI -ErrorAction SilentlyContinue | ForEach-Object {
            $_.CloseMainWindow() | Out-Null
            if (-not $_.WaitForExit(5000)) { Stop-Process -Id $_.Id -Force }
        }
        # New-Item -Force recree une cle existante en effacant ses valeurs : ne creer que si elle manque.
        if (-not (Test-Path -LiteralPath $portsKey)) { New-Item -Path $portsKey -Force | Out-Null }
        foreach ($port in $missingPorts) {
            New-ItemProperty -LiteralPath $portsKey -Name $port -PropertyType DWord -Value 1 -Force | Out-Null
        }
        Write-Host "Ports loopMIDI crees : $($missingPorts -join ', ')"
    }
    if ($null -ne $loopMidiExe -and -not $NoPrompt -and (Ask-YesNo 'Lancer loopMIDI a l''ouverture de session ?')) {
        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        if ($null -eq (Get-ItemProperty -LiteralPath $runKey -Name loopMIDI -ErrorAction SilentlyContinue)) {
            New-ItemProperty -LiteralPath $runKey -Name loopMIDI -PropertyType String -Value $loopMidiExe -Force | Out-Null
            Write-Host 'loopMIDI demarrera avec Windows.'
        }
        if (-not (Get-Process -Name loopMIDI -ErrorAction SilentlyContinue)) {
            Start-Process -FilePath $loopMidiExe -WindowStyle Minimized
        }
    }

    # Reglages MIDI de FL (Options > MIDI), gardes dans le registre sous Devices. FL les reecrit en quittant :
    # il doit etre ferme pendant l'ecriture.
    $flKeys = @(Get-ChildItem -LiteralPath 'HKCU:\Software\Image-Line' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like 'FL Studio*' -and (Test-Path -LiteralPath (Join-Path $_.PSPath 'General')) })
    if ($flKeys.Count -eq 0) {
        Write-Host 'Reglages FL Studio introuvables (FL jamais lance ?) : configurez Options > MIDI a la main.' -ForegroundColor Yellow
    } elseif ($NoPrompt -or -not (Ask-YesNo 'Ecrire les reglages MIDI de FL Studio (Mixdeck to FL / FL to Mixdeck) ?')) {
        Write-Host 'Reglages MIDI de FL Studio non ecrits.' -ForegroundColor Yellow
    } else {
        while (Get-Process -Name FL64, FL -ErrorAction SilentlyContinue) {
            if ($NoPrompt) { break }
            Read-Host 'FL Studio est ouvert. Fermez-le (en enregistrant votre projet), puis appuyez sur Entree' | Out-Null
        }
        if (Get-Process -Name FL64, FL -ErrorAction SilentlyContinue) {
            Write-Host 'FL Studio ouvert : configuration MIDI non ecrite. Relancez l''installation FL fermee.' -ForegroundColor Yellow
        } else {
            foreach ($flKey in $flKeys) {
                $devices = Join-Path $flKey.PSPath 'Devices'
                $inputs = Join-Path $devices 'MIDI input'
                $outputs = Join-Path $devices 'MIDI output'
                # Port 10, sauf s'il est deja pris par un autre peripherique.
                $usedPorts = @(Get-ChildItem -LiteralPath $inputs, $outputs -ErrorAction SilentlyContinue |
                    Where-Object { $portNames -notcontains $_.PSChildName } |
                    ForEach-Object { [int]"$($_.GetValue('Port', '-1'))" } | Where-Object { $_ -ge 0 })
                $flPort = 10
                while ($usedPorts -contains $flPort) { $flPort++ }

                $values = @(
                    @{ Key = Join-Path $inputs 'Mixdeck to FL'; Set = @{ Type = '-3'; ScriptFolder = 'Mixdeck'; Port = "$flPort"; Enabled = '1' } },
                    @{ Key = Join-Path $inputs 'FL to Mixdeck'; Set = @{ Enabled = '0'; Port = '-1' } },
                    @{ Key = Join-Path $outputs 'FL to Mixdeck'; Set = @{ Port = "$flPort"; Sync = '0'; SendAllNotesOff = '1' } },
                    @{ Key = Join-Path $outputs 'Mixdeck to FL'; Set = @{ Port = '-1'; Sync = '0'; SendAllNotesOff = '1' } },
                    @{ Key = Join-Path $flKey.PSPath 'General'; Set = @{ 'Enable MIDI input' = '1'; 'Enable MIDI output' = '1' } }
                )
                foreach ($entry in $values) {
                    if (-not (Test-Path -LiteralPath $entry.Key)) { New-Item -Path $entry.Key -Force | Out-Null }
                    foreach ($name in $entry.Set.Keys) {
                        New-ItemProperty -LiteralPath $entry.Key -Name $name -PropertyType String -Value $entry.Set[$name] -Force | Out-Null
                    }
                    if ($entry.Key -notlike '*General' -and $null -eq (Get-ItemProperty -LiteralPath $entry.Key -Name IDString -ErrorAction SilentlyContinue)) {
                        New-ItemProperty -LiteralPath $entry.Key -Name IDString -PropertyType String -Value '-' -Force | Out-Null
                    }
                }
                Write-Host "$($flKey.PSChildName) configure : entree ""Mixdeck to FL"" (type Mixdeck, port $flPort), sortie ""FL to Mixdeck"" (port $flPort)."
            }
        }
    }
} catch {
    Write-Host "Avertissement : integration FL Studio incomplete : $($_.Exception.Message)" -ForegroundColor Yellow
}

exit 0
