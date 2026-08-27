<#
.SYNOPSIS
    Frontend quality gate: lint, format, translation parity, direction-neutral styles, tests, and
    the deployable build.

.DESCRIPTION
    The single documented frontend verification entry point (spec FR-050).
    Takes no arguments so a future pipeline can call it unchanged.
    Exits non-zero on the first failure.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $repoRoot 'frontend'

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

Push-Location $frontend
try {
    if (Test-Path (Join-Path $frontend 'package-lock.json')) {
        # npm ci deletes node_modules first, which fails on Windows when another process still
        # holds a handle (a dev server, a watch-mode test run, an editor). One retry clears the
        # usual transient case; a second failure means something is genuinely still running.
        Invoke-Step 'install (clean)' {
            npm ci
            if ($LASTEXITCODE -ne 0) {
                Write-Host 'npm ci failed; retrying once in case a file handle was still open...' -ForegroundColor Yellow
                Start-Sleep -Seconds 3
                npm ci
                if ($LASTEXITCODE -ne 0) {
                    Write-Host 'npm ci failed again. Close any running dev server or watch-mode test run.' -ForegroundColor Red
                }
            }
        }
    }
    else {
        Invoke-Step 'install' { npm install }
    }

    Invoke-Step 'lint' { npm run lint }
    Invoke-Step 'format check' { npm run format:check }
    Invoke-Step 'translation key parity' { npm run i18n:check }
    Invoke-Step 'direction-neutral styles' { npm run css:check }
    Invoke-Step 'tests' { npm run test:ci }
    Invoke-Step 'production build' { npm run build -- --configuration production }
}
finally {
    Pop-Location
}

# The deployable artifact is validated here rather than discovered to be wrong at deployment time
# (spec FR-008).
Write-Host ''
Write-Host '==> deployment artifact' -ForegroundColor Cyan

$browserOutput = Join-Path $frontend 'dist/crm-web/browser'
$required = @('index.html', 'web.config', 'assets/config.json', 'assets/i18n/en.json', 'assets/i18n/ar.json')
$missing = @()

foreach ($item in $required) {
    if (-not (Test-Path (Join-Path $browserOutput $item))) {
        $missing += $item
    }
}

if ($missing.Count -gt 0) {
    Write-Host "FAILED: the build output is missing $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Frontend verification passed.' -ForegroundColor Green
exit 0
