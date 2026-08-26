<#
.SYNOPSIS
    Backend quality gate: build, format check, tests, and the deployable publish output.

.DESCRIPTION
    The single documented backend verification entry point (spec FR-050).
    Takes no arguments so a future pipeline can call it unchanged.
    Exits non-zero on the first failure.

    Requires: .NET SDK per global.json, and a running container runtime for the integration suite
    (it provisions its own disposable SQL Server - see docs/testing.md).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'backend/Crm.sln'
$publishOutput = Join-Path $repoRoot 'backend/artifacts/publish'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Name (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Push-Location $repoRoot
try {
    Invoke-Step 'restore' { dotnet restore $solution }
    Invoke-Step 'build (warnings are errors)' { dotnet build $solution --configuration Release --no-restore }
    Invoke-Step 'format check' { dotnet format $solution --verify-no-changes --no-restore }
    Invoke-Step 'tests (unit, integration, architecture)' { dotnet test $solution --configuration Release --no-build }
    Invoke-Step 'publish (IIS deployment artifact)' {
        dotnet publish (Join-Path $repoRoot 'backend/src/Crm.Api/Crm.Api.csproj') `
            --configuration Release --no-build --output $publishOutput
    }
}
finally {
    Pop-Location
}

# Validate the artifact here rather than discovering it is wrong at deployment time (spec FR-008).
Write-Host ''
Write-Host '==> deployment artifact' -ForegroundColor Cyan

$required = @('Crm.Api.dll', 'Crm.Api.runtimeconfig.json', 'appsettings.json', 'appsettings.Production.json', 'web.config')
$missing = @()

foreach ($item in $required) {
    if (-not (Test-Path (Join-Path $publishOutput $item))) {
        $missing += $item
    }
}

if ($missing.Count -gt 0) {
    Write-Host "FAILED: the publish output is missing $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

# A secrets file inside the published folder is exactly what FR-008 forbids.
$leakedSecrets = Get-ChildItem -Path $publishOutput -Filter 'secrets*.json' -Recurse -ErrorAction SilentlyContinue

if ($leakedSecrets) {
    Write-Host "FAILED: secret files found in the publish output: $($leakedSecrets.Name -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Backend verification passed.' -ForegroundColor Green
exit 0
