param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'Run inside the networkless Linux workflow-check harness.' }
$generated = & (Join-Path $RepositoryRoot 'scripts/sonar/templates/Build-NuGetAcceptanceWorkflow.ps1') -RepositoryRoot $RepositoryRoot
$registered = Join-Path $RepositoryRoot '.github/workflows/nuget-acceptance.yml'
if ((Get-FileHash $generated).Hash -cne (Get-FileHash $registered).Hash) {
    throw 'Checked-in acceptance workflow differs from the reviewed generator.'
}
$workflow = [IO.File]::ReadAllText($registered)
if ($workflow -match 'NuGet/login|nuget push|id-token:\s*write|secrets\.NUGET|continue-on-error|workflow_dispatch') {
    throw 'The acceptance workflow must not publish, obtain credentials, bypass failure or allow manual input.'
}
foreach ($required in @(
    'environment: sonar-nuget-acceptance', '--nuget-verify-acceptance-intent',
    'kind: "acceptance-intent", acceptanceOnly: true',
    'kind: "acceptance-outcomes", acceptanceOnly: true',
    'if env -i PATH="$PATH" cat -- "$package" > /dev/null; then',
    'bash scripts/sonar/templates/Test-NuGetAcceptanceIntent.sh',
    'workflows: ["NuGet preparation (runner test)"]',
    '^sonar-poc/nuget/[a-f0-9]{32}/v',
    'test "$GITHUB_REF" = refs/heads/main',
    'test "$GITHUB_RUN_ATTEMPT" = 1'
)) { if (-not $workflow.Contains($required)) { throw "Missing acceptance boundary: $required" } }

$probe = Join-Path $RepositoryRoot 'scripts/sonar/templates/Test-NuGetAcceptanceIntent.sh'
& shellcheck $probe
if ($LASTEXITCODE -ne 0) { throw 'Acceptance probe lint failed.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('nuget-acceptance-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory("$root/bin") | Out-Null
$shim = @'
#!/usr/bin/env bash
set -euo pipefail
test "$#" = 22
test "$1" = fixture.dll
test "$2" = --root
test "$4" = --nuget-verify-acceptance-intent -o "$4" = --nuget-verify-intent
test "${11}" = --tag
test "${12}" = sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0
if [[ "$GITHUB_WORKFLOW_REF" == */nuget-publish.yml@refs/heads/main ]]; then scenario=production-context
elif [[ "$GITHUB_RUN_ATTEMPT" == 2 ]]; then scenario=rerun
elif [[ "$INTENT_SHA256" == 000* ]]; then scenario=changed-content
elif [[ "$INTENT_ARTIFACT_DIGEST" == 000* ]]; then scenario=changed-archive
elif [[ "$4" == --nuget-verify-intent ]]; then scenario=production-verifier
else exit 93
fi
printf '%s\n' "$scenario" >> "$FIXTURE_CALLS"
if [[ "$FIXTURE_FAULT" == "$scenario" ]]; then exit 0; fi
if [[ "$FIXTURE_FAULT" == crash ]]; then exit 124; fi
if [[ "$FIXTURE_FAULT" == receipt ]]; then printf '{"releaseEligible":false}\n'; fi
exit 1
'@
[IO.File]::WriteAllText("$root/bin/dotnet", $shim + "`n", [Text.UTF8Encoding]::new($false))
& chmod +x "$root/bin/dotnet"
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare verifier fixture.' }
$checks = [Collections.Generic.List[object]]::new()
try {
    $scenarios = @('valid', 'production-context', 'rerun', 'changed-content', 'changed-archive', 'production-verifier', 'crash', 'receipt', 'credentials')
    foreach ($scenario in $scenarios) {
        $case = Join-Path $root $scenario
        [IO.Directory]::CreateDirectory($case) | Out-Null
        $start = [Diagnostics.ProcessStartInfo]::new('bash')
        $start.ArgumentList.Add($probe)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $values = @{
            PATH="$root/bin:" + $env:PATH; PUBLISH_ROOT=$case
            GITHUB_REPOSITORY='andregoepel/app-foundation'; GITHUB_RUN_ATTEMPT='1'
            GITHUB_WORKFLOW_REF='andregoepel/app-foundation/.github/workflows/nuget-acceptance.yml@refs/heads/main'
            VERIFIER_DLL='fixture.dll'; REPOSITORY_ROOT=$RepositoryRoot; CANDIDATE_SHA=('a' * 40)
            PACKAGE_VERSION='2.2.0'; READINESS_RUN='400'; PREPARATION_RUN='500'; PREPARATION_ATTEMPT='1'
            RELEASE_TAG='sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0'
            HANDOFF_ROOT=$case; MANIFEST_SHA256=('b' * 64); TAG_OBJECT=('c' * 40)
            INTENT_SHA256=('d' * 64); INTENT_ARTIFACT_DIGEST=('e' * 64)
            FIXTURE_FAULT=$scenario; FIXTURE_CALLS="$case/calls"
        }
        if ($scenario -ceq 'credentials') { $values.NUGET_API_KEY = 'synthetic-key' }
        foreach ($pair in $values.GetEnumerator()) { $start.Environment[$pair.Key] = $pair.Value }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            $process.Start() | Out-Null
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Acceptance probe timed out.' }
            if (($process.ExitCode -eq 0) -ne ($scenario -ceq 'valid')) {
                throw "Unexpected probe result ($scenario): $($stdout.Result) $($stderr.Result)"
            }
            if ($scenario -ceq 'valid' -and @(Get-Content "$case/calls").Count -ne 5) { throw 'Not all negative checks ran.' }
            if ($scenario -ceq 'credentials' -and (Test-Path "$case/calls")) { throw 'Verifier ran with a publication credential.' }
            $checks.Add(@{scenario=$scenario; passed=$true})
        }
        finally { $process.Dispose() }
    }
    @{synthetic=$true; liveRunnerProof=$false; passed=$checks.Count; failed=0; checks=$checks} |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ResultsDirectory 'acceptance-workflow-checks.json')
}
finally { Remove-Item -LiteralPath $root -Recurse -Force }
