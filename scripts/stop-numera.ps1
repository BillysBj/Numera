#requires -Version 5
# Numera lokal stoppen: API/Web (Ports 5080/5173), Worker (dotnet) und Docker-Infra.
$ErrorActionPreference = 'SilentlyContinue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo

Write-Host 'Stoppe Numera...'

# API (5080) + Web (5173) ueber ihre Ports beenden
foreach ($port in 5080, 5173) {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
}

# Worker (dotnet-Prozess, der Numera.Worker ausfuehrt)
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match 'Numera\.Worker' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

# Docker-Infra anhalten (Daten bleiben im Volume erhalten)
docker compose stop | Out-Null

Write-Host 'Numera gestoppt. (Daten bleiben erhalten; naechster Start via Start-Numera.cmd)'
