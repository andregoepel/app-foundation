param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $RepositoryRoot 'artifacts/sonar/standby-workflow-checks' }
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null

$rendered = & (Join-Path $RepositoryRoot 'scripts/sonar/templates/Build-NuGetPublishWorkflow.ps1') -RepositoryRoot $RepositoryRoot
$expectedPublisher = [IO.File]::ReadAllText($rendered)
$mainPolicy = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'scripts/sonar/main-policy.json') -Raw | ConvertFrom-Json
$preparationPolicy = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'scripts/sonar/nuget-policy.json') -Raw | ConvertFrom-Json
$producerGuard = '    if: ${{ false && github.repository == ''andregoepel/app-foundation'' && github.event_name == ''push'' && startsWith(github.ref, ''refs/tags/v'') }}'
$baseline = @{
    Ci = [IO.File]::ReadAllText((Join-Path $RepositoryRoot '.github/workflows/ci.yml'))
    Producer = [IO.File]::ReadAllText((Join-Path $RepositoryRoot '.github/workflows/nuget-prepare.yml'))
    Publisher = [IO.File]::ReadAllText((Join-Path $RepositoryRoot '.github/workflows/nuget-publish.yml'))
    CiHash = @($mainPolicy.workflows | Where-Object path -CEQ '.github/workflows/ci.yml').fileSha256
    ProducerHash = $preparationPolicy.workflowSha256
}

function Get-TextHash([string]$text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function Assert-Standby([hashtable]$bundle) {
    if ($bundle.Ci -match 'pack-and-publish:|id-token:|NuGet/login|nuget push|nuget-publish@|(?m)^\s+tags:') {
        throw 'CI must not contain the legacy publication route or a tag trigger.'
    }
    if ($bundle.Ci -notmatch 'branches: \["main"\]' -or $bundle.Ci -notmatch 'pull_request:' -or
        $bundle.Ci -notmatch '  ci:' -or $bundle.Ci -notmatch '  servicedefaults-drift:') {
        throw 'Main/PR CI and the drift check must remain.'
    }
    if ($bundle.Producer -notmatch '(?m)^name: NuGet preparation$' -or
        $bundle.Producer -notmatch '(?m)^    tags: \["v\*"\]$' -or
        $bundle.Producer -match 'workflow_dispatch:|workflow_call:|id-token:|NuGet/login|nuget push') {
        throw 'The preparation trigger or permissions differ from standby.'
    }
    $jobs = $bundle.Producer.Substring($bundle.Producer.IndexOf("`njobs:`n", [StringComparison]::Ordinal))
    if (([regex]::Matches($jobs, '(?m)^  [\w-]+:').Value -join ',') -cne '  prepare:' -or
        ([regex]::Matches($jobs, '(?m)^    if:.*').Value -join ',') -cne $producerGuard) {
        throw 'Preparation must have exactly one unconditionally disabled job.'
    }
    if ($bundle.Publisher -cne $expectedPublisher) {
        throw 'The registered publisher must match the complete disabled template.'
    }
    if ($bundle.CiHash -cne (Get-TextHash $bundle.Ci) -or
        $bundle.ProducerHash -cne (Get-TextHash $bundle.Producer)) {
        throw 'Reviewed workflow hashes must match the standby files.'
    }
}

$results = [Collections.Generic.List[object]]::new()
foreach ($case in @(
    'standby', 'legacy-job', 'ci-tag-trigger', 'missing-main-check',
    'enabled-preparation', 'extra-preparation-job', 'manual-preparation',
    'enabled-publisher', 'publisher-drift', 'stale-ci-hash', 'stale-preparation-hash'
)) {
    # Arrange
    $bundle = $baseline.Clone()
    switch ($case) {
        'legacy-job' { $bundle.Ci += "`n  pack-and-publish:`n    runs-on: ubuntu-latest`n" }
        'ci-tag-trigger' { $bundle.Ci = $bundle.Ci.Replace('  push:', "  push:`n    tags: [`"v*`"]") }
        'missing-main-check' { $bundle.Ci = $bundle.Ci.Replace('  ci:', '  removed-ci:') }
        'enabled-preparation' { $bundle.Producer = $bundle.Producer.Replace('${{ false && ', '${{ ') }
        'extra-preparation-job' { $bundle.Producer += "`n  other:`n    runs-on: ubuntu-latest`n" }
        'manual-preparation' { $bundle.Producer = $bundle.Producer.Replace("on:`n", "on:`n  workflow_dispatch:`n") }
        'enabled-publisher' { $bundle.Publisher = $bundle.Publisher.Replace('${{ false && ', '${{ ') }
        'publisher-drift' { $bundle.Publisher = $bundle.Publisher.Replace('environment: nuget-publish', 'environment: other') }
        'stale-ci-hash' { $bundle.CiHash = '0' * 64 }
        'stale-preparation-hash' { $bundle.ProducerHash = '0' * 64 }
    }

    # Act
    $rejected = $false
    try { Assert-Standby $bundle } catch { $rejected = $true }

    # Assert
    if ($rejected -ne ($case -cne 'standby')) { throw "$case did not enforce standby." }
    $results.Add(@{name=$case; rejected=$rejected})
}

@{passed=$results.Count; failed=0; synthetic=$true; liveRunnerProof=$false; cases=$results} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'standby-workflow-checks.json')
Write-Output "$($results.Count) standby configuration cases passed; no workflow was executed."
