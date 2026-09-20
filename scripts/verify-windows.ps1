<#
.SYNOPSIS
    Verifies the ConPTY backend on a real Windows machine.

.DESCRIPTION
    The Windows backend was written on macOS and could not be executed there. This script is how a
    person on Windows finds out whether it works. It builds the solution, runs the Windows-tagged
    cases, and prints a pass or fail summary.

    Run it from anywhere:  pwsh scripts/verify-windows.ps1

.NOTES
    A failure here is expected to be informative rather than surprising. Record what happened in the
    Phase 3 Phase Summary of plans/pty-test-harness.md, whichever way it goes.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'TestPty.slnx'
$testProject = Join-Path $repositoryRoot 'tests/TestPty.Tests'

if (-not $IsWindows) {
    Write-Host 'This script verifies ConPTY and must run on Windows.' -ForegroundColor Yellow
    exit 2
}

Write-Host "Building $solution ($Configuration)..." -ForegroundColor Cyan
dotnet build $solution --configuration $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: the solution did not build.' -ForegroundColor Red
    exit 1
}

Write-Host 'Running the Windows backend cases...' -ForegroundColor Cyan
dotnet test $testProject --configuration $Configuration --no-build --filter 'FullyQualifiedName~WindowsBackend'
$backendResult = $LASTEXITCODE

Write-Host 'Running the whole suite...' -ForegroundColor Cyan
dotnet test $testProject --configuration $Configuration --no-build
$suiteResult = $LASTEXITCODE

Write-Host ''
Write-Host '--- Summary ---' -ForegroundColor Cyan
Write-Host ("Windows backend cases : {0}" -f $(if ($backendResult -eq 0) { 'PASS' } else { 'FAIL' }))
Write-Host ("Whole suite           : {0}" -f $(if ($suiteResult -eq 0) { 'PASS' } else { 'FAIL' }))

if ($backendResult -ne 0 -or $suiteResult -ne 0) {
    Write-Host 'FAIL' -ForegroundColor Red
    exit 1
}

Write-Host 'PASS' -ForegroundColor Green
exit 0
