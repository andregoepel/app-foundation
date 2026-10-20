param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'Run on Linux in a networkless container or network namespace.' }
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $RepositoryRoot 'artifacts/sonar/nuget-workflow-checks' }

foreach ($name in @(
    'APPFOUNDATION_GITHUB_TOKEN', 'APPFOUNDATION_SONAR_TOKEN', 'APPFOUNDATION_SONAR_VERIFY_TOKEN',
    'GH_TOKEN', 'GITHUB_TOKEN', 'ACTIONS_RUNTIME_TOKEN',
    'ACTIONS_ID_TOKEN_REQUEST_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_URL',
    'NUGET_API_KEY', 'NUGET_AUTH_TOKEN', 'NUGET_SYMBOL_API_KEY'
)) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw "Remove $name before running synthetic checks." }
}

$interfaces = @(
    Get-Content -LiteralPath '/proc/net/dev' | Select-Object -Skip 2 |
        ForEach-Object { ($_ -split ':', 2)[0].Trim() }
)
if ($interfaces.Count -ne 1 -or $interfaces[0] -cne 'lo') {
    throw 'Synthetic checks require a network namespace containing only loopback.'
}

foreach ($name in @('pwsh', 'bash', 'jq', 'shellcheck', 'actionlint')) {
    Get-Command $name -CommandType Application -ErrorAction Stop | Out-Null
}

if (Test-Path -LiteralPath $ResultsDirectory) {
    throw 'Use a fresh results directory; previous evidence must not mask a failed run.'
}
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null

$rendered = & (Join-Path $RepositoryRoot 'scripts/sonar/templates/Build-NuGetPublishWorkflow.ps1') -RepositoryRoot $RepositoryRoot
$workflows = @(
    $rendered,
    (Join-Path $RepositoryRoot '.github/workflows/sonar-nuget-template-checks.yml'),
    (Join-Path $RepositoryRoot '.github/workflows/nuget-prepare.yml'),
    (Join-Path $RepositoryRoot '.github/workflows/nuget-rehearse.yml'),
    (Join-Path $RepositoryRoot '.github/workflows/nuget-acceptance.yml')
)
& actionlint @workflows
if ($LASTEXITCODE -ne 0) { throw 'Workflow lint failed.' }

$suites = @(
    @{Script='Verify-NuGetPublishCaller.ps1'; Report='publish-caller-checks.json'},
    @{Script='Verify-NuGetPublishSteps.ps1'; Report='publish-step-checks.json'},
    @{Script='Verify-NuGetRunnerWorkflows.ps1'; Report='workflow-checks.json'},
    @{Script='Verify-NuGetAcceptanceWorkflow.ps1'; Report='acceptance-workflow-checks.json'}
)
$checks = [Collections.Generic.List[object]]::new()

foreach ($suite in $suites) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot $suite.Script) -RepositoryRoot $RepositoryRoot -ResultsDirectory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw "$($suite.Script) failed." }

    $report = Get-Content -LiteralPath (Join-Path $ResultsDirectory $suite.Report) -Raw | ConvertFrom-Json
    if ($report.failed -cne 0 -or $report.passed -le 0 -or $report.synthetic -cne $true -or $report.liveRunnerProof -cne $false) {
        throw "$($suite.Script) did not produce successful synthetic evidence."
    }
    $checks.Add(@{suite=$suite.Script; passed=$report.passed; report=$suite.Report})
}

@{
    schemaVersion=1
    synthetic=$true
    livePublishingProof=$false
    networkInterfaces=$interfaces
    passed=($checks | Measure-Object -Property passed -Sum).Sum
    failed=0
    workflowSha256=(Get-FileHash $rendered -Algorithm SHA256).Hash.ToLowerInvariant()
    checks=$checks
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.json')

Write-Output "$(($checks | Measure-Object -Property passed -Sum).Sum) synthetic shell cases passed with no external network interface."
