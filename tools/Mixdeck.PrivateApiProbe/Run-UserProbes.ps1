param(
    [string]$EndpointId,
    [string]$MixdeckExe = '%USERPROFILE%\AppData\Local\MixdeckApp\current\Mixdeck.exe'
)

# À lancer par Claude dans la session de bureau de user, après le build.
# Les roundtrips restaurent les préférences dans finally, y compris après Ctrl+C.
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = '%USERPROFILE%\.dotnet'
$probe = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\win-x64\Mixdeck.PrivateApiProbe.exe'
if (-not (Test-Path -LiteralPath $probe)) { throw 'Compilez Mixdeck.slnx avant de lancer les sondes.' }
$logDirectory = Join-Path $PSScriptRoot 'artifacts\user-probes'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ('session-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')

function Invoke-CheckedProbe([string[]]$ProbeArguments) {
    Write-Host ('SONDE : ' + ($ProbeArguments -join ' '))
    & $probe @ProbeArguments
    $probeExit = $LASTEXITCODE
    Write-Host "Code de sortie : $probeExit"
    if ($probeExit -ne 0) { throw "Sonde refusée (code $probeExit). Consultez les HRESULT et les traces de restauration." }
}

Start-Transcript -LiteralPath $logPath | Out-Null
try {
    Invoke-CheckedProbe -ProbeArguments @('qi-negative')
    $endpointsJson = & $probe route-list
    if ($LASTEXITCODE -ne 0) { throw 'Impossible de lire les sorties actives.' }
    $endpointsJson | Out-Host
    $endpoints = ($endpointsJson -join [Environment]::NewLine) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($EndpointId)) {
        $aux = @($endpoints | Where-Object { $_.Name -like 'Voicemeeter AUX Input*' })
        if ($aux.Count -ne 1) { throw 'Relancez avec -EndpointId et un identifiant exact affiché par route-list.' }
        $EndpointId = $aux[0].Id
    }
    Write-Host 'Essai audio : 3 s sur la sortie initiale, 10 s sur la sortie choisie, puis restauration.'
    Invoke-CheckedProbe -ProbeArguments @('route-roundtrip', $EndpointId)
    Invoke-CheckedProbe -ProbeArguments @('tray-get', $MixdeckExe)
    Write-Host 'Essai tray : visible 8 s, rangée 8 s, puis restauration exacte. Observez la barre des tâches.'
    Invoke-CheckedProbe -ProbeArguments @('tray-roundtrip', $MixdeckExe)
    Write-Host 'Ajoutez au rapport vos constats auditif et visuel ; les HRESULT seuls ne les prouvent pas.'
}
finally {
    Stop-Transcript | Out-Null
    Write-Host "Traces : $logPath"
}
