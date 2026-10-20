param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'Run on Linux with pwsh, bash and jq in a networkless container.' }
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $RepositoryRoot 'artifacts/sonar/publish-caller-tests' }
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$rendered = & (Join-Path $RepositoryRoot 'scripts/sonar/templates/Build-NuGetPublishWorkflow.ps1') -RepositoryRoot $RepositoryRoot
$lines = [IO.File]::ReadAllLines($rendered)
$text = $lines -join "`n"
$root = Join-Path ([IO.Path]::GetTempPath()) ('nuget-caller-tests-' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$results = [Collections.Generic.List[object]]::new()

try {
    [IO.Directory]::CreateDirectory("$root/bin") | Out-Null
    foreach ($required in @(
        '    workflows: ["NuGet preparation"]', '    types: [completed]', 'permissions: {}',
        '  group: app-foundation-nuget-publish', '  cancel-in-progress: false',
        '    environment: nuget-publish', '    runs-on: ubuntu-24.04',
        '      contents: read', '      actions: read', '      id-token: write',
        '          ref: ${{ steps.event.outputs.sha }}', '          fetch-depth: 0',
        '          persist-credentials: false', '          global-json-file: global.json',
        'uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1',
        'uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68'
    )) { if (-not $text.Contains($required)) { throw "Caller contract missing: $required" } }
    if ($text -match 'workflow_dispatch:|workflow_call:|continue-on-error:|contents: write|actions: write|INSERT DISABLED') {
        throw 'Unexpected trigger, permissions, bypass or unresolved insertion marker.'
    }
    $names = @(
        'Validate publication trigger', 'Check out trusted candidate', 'Set up pinned SDK',
        'Bootstrap trusted verifier', 'Download publication handoff', 'Export publication context',
        'Verify full handoff before login', 'Preserve publication intent before login',
        'Reverify after preserving intent', 'Obtain temporary NuGet credentials',
        'Push each reverified package and symbol file', 'Record publication outcomes',
        'Preserve publication outcomes', 'Remove private publishing files',
        'Remove verifier and downloaded handoff'
    )
    $actualNames = @($lines | Where-Object { $_.StartsWith('      - name: ') } | ForEach-Object { $_.Substring(14) })
    if (($actualNames -join ',') -cne ($names -join ',')) { throw 'Caller/fragment step order differs.' }
    $fragment = [IO.File]::ReadAllLines((Join-Path $RepositoryRoot 'scripts/sonar/templates/nuget-publish.steps.yml'))
    $fragmentStart = [Array]::IndexOf($fragment, 'steps:') + 1
    $expectedFragment = ($fragment[$fragmentStart..($fragment.Length - 1)] | ForEach-Object { if ($_.Length -gt 0) { '    ' + $_ } else { '' } }) -join "`n"
    if (-not $text.Contains($expectedFragment.TrimEnd("`n"))) { throw 'Rendered fragment differs from the tested source.' }
    if (-not $text.Contains("        if: `${{ false && always() && (steps.publish-intent.outcome != 'success' || steps.stored-outcomes.outcome == 'success') }}")) {
        throw 'Caller cleanup must preserve unrecorded publication evidence.'
    }

    function Extract-Step([string]$name, [string]$target) {
        $index = [Array]::IndexOf($lines, "      - name: $name")
        while ($index -lt $lines.Length -and $lines[$index] -ne '        run: |') { $index++ }
        if ($index -eq $lines.Length) { throw "Missing shell step: $name" }
        $body = [Collections.Generic.List[string]]::new()
        for ($index++; $index -lt $lines.Length; $index++) {
            if ($lines[$index].StartsWith('          ')) { $body.Add($lines[$index].Substring(10)) }
            elseif ($lines[$index].Length -eq 0) { $body.Add('') }
            else { break }
        }
        [IO.File]::WriteAllText($target, ($body -join "`n") + "`n", $utf8)
    }
    foreach ($entry in @{
        trigger = $names[0]; bootstrap = $names[3]; download = $names[4];
        context = $names[5]; verify = $names[6]; cleanup = $names[14]
    }.GetEnumerator()) { Extract-Step $entry.Value "$root/$($entry.Key).sh" }

    $gh = @'
#!/usr/bin/env bash
set -euo pipefail
test "$GH_TOKEN" = synthetic-read-key
if [[ "$1" == api ]]; then
  test "$#" = 4 && test "$2" = repos/andregoepel/app-foundation/git/ref/heads/main
  test "$3" = --jq && test "$4" = .object.sha
  printf 'main\n' >> "$FIXTURE_EVENTS"
  if [[ "$FIXTURE_CASE" == api-failure ]]; then exit 21; fi
  printf '%s\n' "$FIXTURE_MAIN"
elif [[ "$1" == run && "$2" == download ]]; then
  test "$#" = 9 && test "$3" = 500 && test "$4" = --repo && test "$5" = andregoepel/app-foundation
  test "$6" = --name && test "$8" = --dir
  if [[ "$7" == "nuget-origin-$CANDIDATE_SHA-500-1" ]]; then
    printf 'origin\n' >> "$FIXTURE_EVENTS"
    if [[ "$FIXTURE_CASE" == origin-download ]]; then exit 22; fi
    mkdir -p "$9"
    if [[ "$FIXTURE_CASE" == origin-missing ]]; then exit 0; fi
    if [[ "$FIXTURE_CASE" == origin-link ]]; then ln -s "$FIXTURE_ORIGIN" "$9/origin.json"
    else cp -- "$FIXTURE_ORIGIN" "$9/origin.json"; fi
  elif [[ "$7" == "nuget-batch-$CANDIDATE_SHA-500-1" ]]; then
    printf 'batch\n' >> "$FIXTURE_EVENTS"
    if [[ "$FIXTURE_CASE" == batch-download ]]; then exit 23; fi
    mkdir -p "$9/packages"
    printf '{}\n' > "$9/dry-run.json"
    printf 'artifact-must-not-execute\n' > "$9/AndreGoepel.AppFoundation.Sonar.dll"
  else exit 24; fi
else exit 25; fi
'@
    $dotnet = @'
#!/usr/bin/env bash
set -euo pipefail
for name in ACTIONS_ID_TOKEN_REQUEST_TOKEN ACTIONS_ID_TOKEN_REQUEST_URL NUGET_API_KEY NUGET_SYMBOL_API_KEY NUGET_AUTH_TOKEN; do test -z "${!name:-}"; done
project=tools/AndreGoepel.AppFoundation.Sonar/AndreGoepel.AppFoundation.Sonar.csproj
case "$1" in
  restore|publish)
    for name in APPFOUNDATION_GITHUB_TOKEN APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN GITHUB_TOKEN GH_TOKEN; do test -z "${!name:-}"; done
    test "$2" = "$project"
    printf '%s\n' "$1" >> "$FIXTURE_EVENTS"
    if [[ "$FIXTURE_CASE" == "bootstrap-$1" ]]; then exit 26; fi
    if [[ "$1" == restore ]]; then test "$#" = 3 && test "$3" = --locked-mode
    else
      test "$#" = 9 && test "$3" = -c && test "$4" = Release && test "$5" = --no-restore
      test "$6" = --no-self-contained && test "$7" = -p:SonarQubeTargetsImported=true && test "$8" = -o
      if [[ "$FIXTURE_CASE" == bootstrap-missing-dll ]]; then exit 0; fi
      printf 'trusted-verifier\n' > "$9/AndreGoepel.AppFoundation.Sonar.dll"
    fi ;;
  *)
    test "$1" = "$VERIFIER_DLL" && test "$(cat -- "$1")" = trusted-verifier
    for name in APPFOUNDATION_GITHUB_TOKEN APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN; do test "${!name:-}" = synthetic-read-key; done
    expected=("$VERIFIER_DLL" --root "$GITHUB_WORKSPACE" --nuget-verify-handoff --sha "$GITHUB_SHA" --version 2.2.0 --readiness-run 100 --tag v2.2.0 --handoff "$HANDOFF_ROOT/batch" --manifest-sha256 "$FIXTURE_MANIFEST" --tag-object "$FIXTURE_TAG_OBJECT" --preparation-run 500 --preparation-attempt 1)
    test "$#" = "${#expected[@]}"
    index=0
    for argument in "$@"; do test "$argument" = "${expected[$index]}"; index=$((index + 1)); done
    printf 'verify\n' >> "$FIXTURE_EVENTS"
    if [[ "$FIXTURE_CASE" == verification-failure ]]; then exit 27; fi
    jq -n '{dryRun: true, releaseEligible: false, packages: [range(0;12) | {file: ("fixture-" + tostring), sha256: "fixture"}]}'
    ;;
esac
'@
    $git = @'
#!/usr/bin/env bash
set -euo pipefail
test "$#" = 2 && test "$1" = rev-parse && test "$2" = HEAD
printf '%s\n' "$FIXTURE_CHECKOUT"
'@
    foreach ($shim in @{gh=$gh; dotnet=$dotnet; git=$git}.GetEnumerator()) {
        [IO.File]::WriteAllText("$root/bin/$($shim.Key)", $shim.Value + "`n", $utf8)
        & chmod +x "$root/bin/$($shim.Key)"
        if ($LASTEXITCODE -ne 0) { throw 'Could not prepare command substitute.' }
    }

    function Invoke-Step([string]$step, [hashtable]$values) {
        $start = [Diagnostics.ProcessStartInfo]::new('bash')
        $start.ArgumentList.Add("$root/$step.sh")
        $start.WorkingDirectory = $values.GITHUB_WORKSPACE
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($entry in $values.GetEnumerator()) { $start.Environment[$entry.Key] = [string]$entry.Value }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            $process.Start() | Out-Null
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Offline step timed out.' }
            $result = @{Code=$process.ExitCode; Text=$stdout.Result + $stderr.Result}
            if ($result.Text -match 'synthetic-(read|oidc|nuget)-key') { throw 'Credential appeared in diagnostics.' }
            return $result
        } finally { $process.Dispose() }
    }
    function Read-Output([string]$path) {
        $values = @{}
        foreach ($line in [IO.File]::ReadAllLines($path)) {
            $pair = $line.Split('=', 2)
            if ($pair.Length -ne 2 -or $values.ContainsKey($pair[0])) { throw 'Invalid/duplicate step output.' }
            $values[$pair[0]] = $pair[1]
        }
        return $values
    }
    $cases = @(
        'valid', 'repository', 'event', 'ref', 'rerun', 'workflow-ref', 'workflow-sha',
        'invalid-sha', 'main-changed', 'api-failure', 'missing-event', 'linked-event',
        'oversized-event', 'malformed-event', 'payload-repository', 'producer-repository',
        'producer-fork', 'producer-id', 'producer-path', 'producer-name', 'producer-event',
        'producer-failure', 'producer-running', 'producer-sha', 'producer-rerun',
        'producer-run-zero', 'producer-run-string', 'producer-run-fraction', 'producer-run-overflow',
        'acceptance-tag', 'prerelease-tag', 'tag-newline', 'checkout-sha',
        'bootstrap-restore', 'bootstrap-publish', 'bootstrap-missing-dll',
        'origin-download', 'origin-missing', 'origin-link', 'origin-oversized', 'origin-malformed',
        'origin-repository', 'origin-sha', 'origin-tag', 'origin-run', 'origin-attempt',
        'origin-release-eligible', 'origin-readiness-zero', 'origin-readiness-overflow',
        'origin-tag-newline', 'origin-manifest-newline', 'batch-download',
        'context-verifier-substitution', 'context-tag', 'context-newline',
        'context-traversal', 'context-link', 'verification-failure'
    )
    foreach ($case in $cases) {
        # Arrange
        $caseRoot = "$root/$case"
        foreach ($directory in @("$caseRoot/source", "$caseRoot/temp")) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
        foreach ($file in @('events', 'trigger-output', 'bootstrap-output', 'download-output', 'verify-output', 'env')) { [IO.File]::WriteAllText("$caseRoot/$file", '', $utf8) }
        $sha = 'a' * 40
        $payload = @{
            action='completed'; repository=@{full_name='andregoepel/app-foundation'};
            workflow_run=@{repository=@{full_name='andregoepel/app-foundation'}; head_repository=@{full_name='andregoepel/app-foundation'};
                workflow_id=388401675; path='.github/workflows/nuget-prepare.yml'; name='NuGet preparation'; event='push';
                status='completed'; conclusion='success'; head_sha=$sha; head_branch='v2.2.0'; id=500; run_attempt=1}
        }
        $origin = @{schemaVersion=1; repository='andregoepel/app-foundation'; sha=$sha; tag='v2.2.0'; runId=500; attempt=1;
            dryRun=$true; releaseEligible=$false; readinessRunId=100; tagObjectSha=('b'*40); manifestSha256=('c'*64)}
        $values = @{
            PATH="$root/bin:$env:PATH"; RUNNER_TEMP="$caseRoot/temp"; GITHUB_WORKSPACE="$caseRoot/source";
            GITHUB_REPOSITORY='andregoepel/app-foundation'; GITHUB_EVENT_NAME='workflow_run'; GITHUB_REF='refs/heads/main';
            GITHUB_RUN_ID='600'; GITHUB_RUN_ATTEMPT='1'; GITHUB_SHA=$sha; GITHUB_WORKFLOW_SHA=$sha;
            GITHUB_WORKFLOW_REF='andregoepel/app-foundation/.github/workflows/nuget-publish.yml@refs/heads/main';
            GITHUB_EVENT_PATH="$caseRoot/event.json"; GITHUB_OUTPUT="$caseRoot/trigger-output"; GITHUB_ENV="$caseRoot/env";
            GH_TOKEN='synthetic-read-key'; GITHUB_TOKEN='synthetic-read-key';
            APPFOUNDATION_GITHUB_TOKEN='synthetic-read-key'; APPFOUNDATION_SONAR_TOKEN='synthetic-read-key'; APPFOUNDATION_SONAR_VERIFY_TOKEN='synthetic-read-key';
            ACTIONS_ID_TOKEN_REQUEST_TOKEN='synthetic-oidc-key'; ACTIONS_ID_TOKEN_REQUEST_URL='https://oidc.invalid';
            NUGET_API_KEY='synthetic-nuget-key'; NUGET_SYMBOL_API_KEY='synthetic-nuget-key'; NUGET_AUTH_TOKEN='synthetic-nuget-key';
            FIXTURE_CASE=$case; FIXTURE_EVENTS="$caseRoot/events"; FIXTURE_MAIN=$sha; FIXTURE_CHECKOUT=$sha;
            FIXTURE_ORIGIN="$caseRoot/origin.json"; FIXTURE_MANIFEST=('c'*64); FIXTURE_TAG_OBJECT=('b'*40)
        }
        switch ($case) {
            'repository' { $values.GITHUB_REPOSITORY='fork/app-foundation' }
            'event' { $values.GITHUB_EVENT_NAME='workflow_dispatch' }
            'ref' { $values.GITHUB_REF='refs/heads/feature' }
            'rerun' { $values.GITHUB_RUN_ATTEMPT='2' }
            'workflow-ref' { $values.GITHUB_WORKFLOW_REF='andregoepel/app-foundation/.github/workflows/other.yml@refs/heads/main' }
            'workflow-sha' { $values.GITHUB_WORKFLOW_SHA='b'*40 }
            'invalid-sha' { $values.GITHUB_SHA='invalid'; $values.GITHUB_WORKFLOW_SHA='invalid' }
            'main-changed' { $values.FIXTURE_MAIN='b'*40 }
            'payload-repository' { $payload.repository.full_name='fork/app-foundation' }
            'producer-repository' { $payload.workflow_run.repository.full_name='fork/app-foundation' }
            'producer-fork' { $payload.workflow_run.head_repository.full_name='fork/app-foundation' }
            'producer-id' { $payload.workflow_run.workflow_id=1 }
            'producer-path' { $payload.workflow_run.path='.github/workflows/other.yml' }
            'producer-name' { $payload.workflow_run.name='NuGet preparation (runner test)' }
            'producer-event' { $payload.workflow_run.event='workflow_dispatch' }
            'producer-failure' { $payload.workflow_run.conclusion='failure' }
            'producer-running' { $payload.workflow_run.status='in_progress' }
            'producer-sha' { $payload.workflow_run.head_sha='b'*40 }
            'producer-rerun' { $payload.workflow_run.run_attempt=2 }
            'producer-run-zero' { $payload.workflow_run.id=0 }
            'producer-run-string' { $payload.workflow_run.id='500' }
            'producer-run-fraction' { $payload.workflow_run.id=1.5 }
            'producer-run-overflow' { $payload.workflow_run.id=9007199254740992 }
            'acceptance-tag' { $payload.workflow_run.head_branch='sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0' }
            'prerelease-tag' { $payload.workflow_run.head_branch='v2.2.0-preview' }
            'tag-newline' { $payload.workflow_run.head_branch="v2.2.0`n" }
            'checkout-sha' { $values.FIXTURE_CHECKOUT='b'*40 }
            'origin-repository' { $origin.repository='fork/app-foundation' }
            'origin-sha' { $origin.sha='b'*40 }
            'origin-tag' { $origin.tag='v9.9.9' }
            'origin-run' { $origin.runId=501 }
            'origin-attempt' { $origin.attempt=2 }
            'origin-release-eligible' { $origin.releaseEligible=$true }
            'origin-readiness-zero' { $origin.readinessRunId=0 }
            'origin-readiness-overflow' { $origin.readinessRunId=9007199254740992 }
            'origin-tag-newline' { $origin.tagObjectSha += "`n" }
            'origin-manifest-newline' { $origin.manifestSha256 += "`n" }
        }
        [IO.File]::WriteAllText($values.GITHUB_EVENT_PATH, ($payload | ConvertTo-Json -Depth 6), $utf8)
        [IO.File]::WriteAllText($values.FIXTURE_ORIGIN, ($origin | ConvertTo-Json -Depth 6), $utf8)
        switch ($case) {
            'missing-event' { Remove-Item -LiteralPath $values.GITHUB_EVENT_PATH }
            'linked-event' { Move-Item -LiteralPath $values.GITHUB_EVENT_PATH -Destination "$caseRoot/event-target"; & ln -s "$caseRoot/event-target" $values.GITHUB_EVENT_PATH }
            'oversized-event' { [IO.File]::WriteAllText($values.GITHUB_EVENT_PATH, (' ' * 1048577), $utf8) }
            'malformed-event' { [IO.File]::WriteAllText($values.GITHUB_EVENT_PATH, '{', $utf8) }
            'origin-oversized' { [IO.File]::WriteAllText($values.FIXTURE_ORIGIN, (' ' * 1048577), $utf8) }
            'origin-malformed' { [IO.File]::WriteAllText($values.FIXTURE_ORIGIN, '{', $utf8) }
        }

        # Act
        $stage = 'trigger'
        $result = Invoke-Step $stage $values
        $bootstrap = @{}
        $download = @{}
        if ($result.Code -eq 0) {
            $event = Read-Output $values.GITHUB_OUTPUT
            $values.CANDIDATE_SHA=$event.sha
            $values.PREPARATION_RUN=$event.run
            $values.PREPARATION_ATTEMPT=$event.attempt
            $values.RELEASE_TAG=$event.tag
            $values.PACKAGE_VERSION=$event.version
            $values.GITHUB_OUTPUT="$caseRoot/bootstrap-output"
            $stage='bootstrap'
            $result=Invoke-Step $stage $values
            $bootstrap=Read-Output $values.GITHUB_OUTPUT
        }
        if ($result.Code -eq 0) {
            $values.GITHUB_OUTPUT="$caseRoot/download-output"
            $stage='download'
            $result=Invoke-Step $stage $values
            $download=Read-Output $values.GITHUB_OUTPUT
        }
        if ($result.Code -eq 0) {
            $values.BOOTSTRAP_ROOT=$bootstrap.root
            $values.VERIFIER_DLL=$bootstrap.dll
            $values.HANDOFF_ROOT=$download.root
            $values.READINESS_RUN=$download.readiness
            $values.TAG_OBJECT=$download.'tag-object'
            $values.MANIFEST_SHA256=$download.manifest
            switch ($case) {
                'context-verifier-substitution' { $values.VERIFIER_DLL="$($download.root)/batch/AndreGoepel.AppFoundation.Sonar.dll" }
                'context-tag' { $values.RELEASE_TAG='v9.9.9' }
                'context-newline' { $values.MANIFEST_SHA256 += "`nINJECTED=value" }
                'context-traversal' { $values.HANDOFF_ROOT="$($download.root)/../$(Split-Path -Leaf $download.root)" }
                'context-link' { & ln -s $download.root "$caseRoot/temp/nuget-handoff.12345678"; $values.HANDOFF_ROOT="$caseRoot/temp/nuget-handoff.12345678" }
            }
            $stage='context'
            $result=Invoke-Step $stage $values
        }
        $context=@{}
        if ($result.Code -eq 0) {
            $context=Read-Output $values.GITHUB_ENV
            foreach ($entry in $context.GetEnumerator()) { $values[$entry.Key]=$entry.Value }
            $values.GITHUB_OUTPUT="$caseRoot/verify-output"
            $stage='verify'
            $result=Invoke-Step $stage $values
        }

        # Assert
        if (($result.Code -eq 0) -ne ($case -eq 'valid')) { throw "$case at $stage returned $($result.Code): $($result.Text)" }
        $events=[IO.File]::ReadAllLines($values.FIXTURE_EVENTS)
        if ($case -eq 'valid') {
            if (($events -join ',') -ne 'main,restore,publish,origin,batch,verify' -or $context.Count -ne 11) { throw 'Trusted bootstrap/download/verification order differs.' }
            if ($context.REPOSITORY_ROOT -cne $values.GITHUB_WORKSPACE -or $context.VERIFIER_DLL -cne $bootstrap.dll -or $context.HANDOFF_ROOT -cne $download.root) { throw 'Context paths changed.' }
            if ($context.READINESS_RUN -ne '100' -or $context.PREPARATION_RUN -ne '500' -or $context.PREPARATION_ATTEMPT -ne '1' -or $context.RELEASE_TAG -ne 'v2.2.0' -or $context.PACKAGE_VERSION -ne '2.2.0') { throw 'Context identities changed.' }
        }
        if ($stage -ne 'verify' -and $events -contains 'verify') { throw "$case reached verification after a caller failure." }
        if ($stage -eq 'trigger' -and ($bootstrap.Count -ne 0 -or $events -contains 'restore' -or $events -contains 'origin')) { throw 'Invalid trigger reached source bootstrap or artifacts.' }
        if ($stage -eq 'context' -and ([IO.File]::ReadAllText($values.GITHUB_ENV)).Length -ne 0) { throw 'Rejected context exported partial values.' }
        if (([IO.File]::ReadAllText($values.GITHUB_ENV)) -match 'synthetic-|NUGET_|TOKEN|INJECTED') { throw 'Context exported credentials or injected variables.' }
        $cleanupValues=$values.Clone()
        $cleanupValues.BOOTSTRAP_ROOT=$bootstrap.root
        $cleanupValues.HANDOFF_ROOT=$download.root
        $cleanup=Invoke-Step 'cleanup' $cleanupValues
        if ($cleanup.Code -ne 0) { throw "$case failed caller cleanup: $($cleanup.Text)" }
        foreach ($path in @($bootstrap.root, $download.root)) {
            if ($path -and (Test-Path -LiteralPath $path)) { throw "$case retained a completed fixture directory." }
        }
        $results.Add(@{name=$case; stage=$stage; exitCode=$result.Code})
    }
    foreach ($case in @('outside-parent', 'wrong-name', 'traversal', 'symlink')) {
        # Arrange
        $temp="$root/cleanup-temp"
        [IO.Directory]::CreateDirectory($temp) | Out-Null
        $candidate=switch ($case) {
            'outside-parent' { "$root/nuget-verifier.12345678" }
            'wrong-name' { "$temp/unrelated" }
            'traversal' { "$temp/../nuget-verifier.12345678" }
            'symlink' { "$temp/nuget-handoff.87654321" }
        }
        if ($case -eq 'symlink') {
            [IO.Directory]::CreateDirectory("$root/sentinel") | Out-Null
            & ln -s "$root/sentinel" $candidate
        } else { [IO.Directory]::CreateDirectory($candidate) | Out-Null }
        $values=@{RUNNER_TEMP=$temp; BOOTSTRAP_ROOT=$candidate; HANDOFF_ROOT=''; GITHUB_WORKSPACE=$root}

        # Act
        $result=Invoke-Step 'cleanup' $values

        # Assert
        if ($result.Code -eq 0 -or -not (Test-Path -LiteralPath $candidate)) { throw "Cleanup escaped its boundary: $case" }
        $results.Add(@{name="cleanup-$case"; exitCode=$result.Code})
    }
    @{passed=$results.Count; failed=0; synthetic=$true; liveRunnerProof=$false;
        workflowSha256=(Get-FileHash $rendered -Algorithm SHA256).Hash.ToLowerInvariant(); cases=$results} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'publish-caller-checks.json')
    Write-Output "$($results.Count) offline caller cases passed. GitHub, git and dotnet are substitutes; no login or publication occurred."
} finally {
    if (Test-Path -LiteralPath $root) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($root)) -ne [IO.Path]::GetTempPath().TrimEnd('/')) { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
