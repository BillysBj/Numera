#requires -Version 5
# =============================================================================
# Numera lokal starten: Docker-Infra + API + Worker + Web-App, dann Browser oeffnen.
# Doppelklick auf Start-Numera.cmd (im Projekt-Root) ruft dieses Skript auf.
# =============================================================================
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
Set-Location $repo

Write-Host '== Numera startet ==' -ForegroundColor Cyan

# 1) Docker-Infra (Postgres, Keycloak, Mailpit, KoSIT)
Write-Host 'Starte Docker-Infra...'
docker compose up -d | Out-Null

# 2) Warten bis Postgres gesund ist
Write-Host -NoNewline 'Warte auf Postgres'
for ($i = 0; $i -lt 40; $i++) {
    $h = (docker inspect -f '{{.State.Health.Status}}' numera-postgres 2>$null)
    if ($h -eq 'healthy') { break }
    Start-Sleep 2; Write-Host -NoNewline '.'
}
Write-Host ' ok'

# 3) Warten bis Keycloak bereit ist (Discovery-Endpoint antwortet)
Write-Host -NoNewline 'Warte auf Keycloak'
for ($i = 0; $i -lt 60; $i++) {
    try {
        if ((Invoke-WebRequest 'http://localhost:8080/realms/numera/.well-known/openid-configuration' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) { break }
    } catch {}
    Start-Sleep 2; Write-Host -NoNewline '.'
}
Write-Host ' ok'

# 4) API, Worker und Web-App je in einem eigenen Fenster starten
#    (Fenster offen lassen zeigt die Logs; Schliessen = Dienst stoppen.)
Write-Host 'Starte API, Worker und Web-App...'
Start-Process powershell -ArgumentList '-NoExit', '-Command', "`$Host.UI.RawUI.WindowTitle='Numera API'; Set-Location '$repo'; & '$dotnet' run --project src/Numera.Api -c Release"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "`$Host.UI.RawUI.WindowTitle='Numera Worker'; Set-Location '$repo'; `$env:ASPNETCORE_ENVIRONMENT='Development'; & '$dotnet' run --project src/Numera.Worker -c Release --no-launch-profile"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "`$Host.UI.RawUI.WindowTitle='Numera Web'; Set-Location '$repo/web'; npm run dev"

# 5) Warten bis die Web-App antwortet, dann im Browser oeffnen
Write-Host -NoNewline 'Warte auf die Web-App'
for ($i = 0; $i -lt 60; $i++) {
    try {
        if ((Invoke-WebRequest 'http://localhost:5173' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) { break }
    } catch {}
    Start-Sleep 2; Write-Host -NoNewline '.'
}
Write-Host ' ok'

Start-Process 'http://localhost:5173'
Write-Host ''
Write-Host 'Numera laeuft:  http://localhost:5173' -ForegroundColor Green
Write-Host 'Zum Stoppen:    Stop-Numera.cmd (oder die drei Fenster schliessen)'
