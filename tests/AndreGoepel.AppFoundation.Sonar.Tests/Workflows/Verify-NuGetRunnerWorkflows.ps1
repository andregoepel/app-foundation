param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) {
    throw 'Run these workflow shell checks on Linux with pwsh, bash and jq installed.'
}
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $RepositoryRoot 'artifacts/sonar/workflow-tests'
}

[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$workflow = [IO.File]::ReadAllLines((Join-Path $RepositoryRoot '.github/workflows/nuget-rehearse.yml'))
$producerWorkflow = [IO.File]::ReadAllLines((Join-Path $RepositoryRoot '.github/workflows/nuget-prepare.yml'))
$root = Join-Path ([IO.Path]::GetTempPath()) ('nuget-rehearsal-tests-' + [Guid]::NewGuid().ToString('N'))
try {
    $utf8 = [Text.UTF8Encoding]::new($false)
    foreach ($directory in @($root, "$root/bin", "$root/scripts/sonar", "$root/temp")) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    function Extract-Step([string]$name, [string]$path, [string[]]$lines = $workflow) {
        $start = [Array]::IndexOf($lines, "      - name: $name")
        if ($start -lt 0) { throw "Missing step $name" }
        while ($lines[$start] -ne '        run: |') { $start++ }
        $script = [Collections.Generic.List[string]]::new()
        for ($index=$start+1; $index -lt $lines.Length; $index++) {
            if ($lines[$index].StartsWith('          ')) { $script.Add($lines[$index].Substring(10)) }
            elseif ($lines[$index].Length -eq 0) { $script.Add('') }
            else { break }
        }
        [IO.File]::WriteAllText($path, [string]::Join("`n", $script) + "`n", $utf8)
    }
    Extract-Step 'Verify preparation event' "$root/preflight.sh"
    Extract-Step 'Download preparation artifacts' "$root/download.sh"
    Extract-Step 'Rehearse verified package batch' "$root/rehearse.sh"
    Extract-Step 'Remove downloaded artifacts' "$root/cleanup.sh"
    Extract-Step 'Verify tag push' "$root/producer-event.sh" $producerWorkflow
    Extract-Step 'Prepare NuGet batch' "$root/prepare.sh" $producerWorkflow
    $shim = @'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$FIXTURE_GH_CALLS"
if [[ "$1" == api && "$2" == repos/andregoepel/app-foundation/git/ref/heads/main ]]; then
    if [[ "$FIXTURE_FAIL" == api ]]; then exit 21; fi
    printf '%s\n' "$FIXTURE_MAIN"
elif [[ "$1" == api && "$2" == --method && "$3" == GET && "$4" == repos/andregoepel/app-foundation/actions/workflows/387320916/runs ]]; then
    test "$5" = -f && test "$6" = branch=main
    test "$7" = -f && test "$8" = event=workflow_run
    test "$9" = -f && test "${10}" = "head_sha=$GITHUB_SHA"
    test "${11}" = -f && test "${12}" = per_page=100 && test "${13}" = --jq
    if [[ "$FIXTURE_FAIL" == readiness ]]; then exit 26; fi
    printf '%s\n' "$FIXTURE_READINESS"
elif [[ "$1" == run && "$2" == download ]]; then
    test "$3" = 500
    test "$4" = --repo && test "$5" = andregoepel/app-foundation
    test "$6" = --name && test "$8" = --dir
    if [[ "$7" == "nuget-origin-$CANDIDATE_SHA-500-1" ]]; then
        if [[ "$FIXTURE_FAIL" == origin-download ]]; then exit 22; fi
        mkdir -p "$9"
        if [[ "$FIXTURE_FAIL" == origin-missing ]]; then exit 0; fi
        if [[ "$FIXTURE_FAIL" == origin-link ]]; then
            ln -s "$FIXTURE_ORIGIN" "$9/origin.json"
        else
            cp -- "$FIXTURE_ORIGIN" "$9/origin.json"
        fi
    elif [[ "$7" == "nuget-batch-$CANDIDATE_SHA-500-1" ]]; then
        if [[ "$FIXTURE_FAIL" == batch-download ]]; then exit 23; fi
        mkdir -p "$9/packages"
        printf '%s' '{}' > "$9/dry-run.json"
    else
        exit 24
    fi
else
    exit 25
fi
'@
    [IO.File]::WriteAllText("$root/bin/gh", $shim + "`n", $utf8)
    & chmod +x "$root/bin/gh"
    [IO.File]::WriteAllText("$root/scripts/sonar/analyze.sh", "#!/usr/bin/env bash`nprintf '%s\n' `"`$@`" > `"`$FIXTURE_CAPTURE`"`nexit `"`$FIXTURE_EXIT`"`n", $utf8)
    function Invoke-Step([string]$path, [hashtable]$values) {
        $start = [Diagnostics.ProcessStartInfo]::new('bash')
        $start.ArgumentList.Add($path)
        $start.WorkingDirectory = $root
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.UseShellExecute = $false
        foreach ($pair in $values.GetEnumerator()) { $start.Environment[$pair.Key] = [string]$pair.Value }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        $process.Start() | Out-Null
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Fixture timed out' }
        $result = @{ Code=$process.ExitCode; Text=$stdout.Result + $stderr.Result }
        $process.Dispose()
        return $result
    }
    $sha = 'a' * 40
    $tagPrefix = 'sonar-poc/nuget/0123456789abcdef0123456789abcdef/'
    $tag = $tagPrefix + 'v2.2.0'
    $productionTag = 'v2.2.0'
    $base = @{
        PATH="$root/bin:$env:PATH"; RUNNER_TEMP="$root/temp"; FIXTURE_FAIL='none'; FIXTURE_MAIN=$sha;
        GITHUB_REPOSITORY='andregoepel/app-foundation'; GITHUB_EVENT_NAME='workflow_run'; GITHUB_REF='refs/heads/main';
        GITHUB_WORKFLOW_REF='andregoepel/app-foundation/.github/workflows/nuget-rehearse.yml@refs/heads/main';
        GITHUB_WORKFLOW_SHA=$sha; GITHUB_SHA=$sha; CANDIDATE_SHA=$sha; PREPARATION_RUN='500';
        PREPARATION_ATTEMPT='1'; RELEASE_TAG=$tag; PACKAGE_VERSION='2.2.0'; FIXTURE_EXIT='0'
    }
    $passed = 0
    $cases=@('lightweight','annotated','repository','event','ref-type','branch','workflow-sha','workflow-ref','invalid-sha','acceptance-tag','other-prefix','leading-zero','version-too-long','prerelease','tag-newline','payload-repository','payload-ref','created','deleted','forced','before','after','after-zero','after-newline','main','api-failure','readiness-failure','readiness-zero','readiness-invalid','malformed','missing')
    foreach ($case in $cases) {
        # Arrange
        $values=$base.Clone()
        $values.GITHUB_EVENT_NAME='push'
        $values.GITHUB_REF_TYPE='tag'
        $values.GITHUB_REF_NAME=$productionTag
        $values.GITHUB_REF="refs/tags/$productionTag"
        $values.GITHUB_WORKFLOW_REF="andregoepel/app-foundation/.github/workflows/nuget-prepare.yml@refs/tags/$productionTag"
        $values.GITHUB_EVENT_PATH="$root/producer-event-$case.json"
        $values.GITHUB_OUTPUT="$root/producer-output-$case"
        $values.GITHUB_STEP_SUMMARY="$root/producer-summary-$case"
        $values.FIXTURE_GH_CALLS="$root/producer-gh-$case"
        $values.FIXTURE_READINESS='100'
        $after=if ($case -eq 'annotated') {'b'*40} else {$sha}
        $payload=@{repository=@{full_name='andregoepel/app-foundation'};ref="refs/tags/$productionTag";created=$true;deleted=$false;forced=$false;before=('0'*40);after=$after}
        switch ($case) {
            'repository' { $values.GITHUB_REPOSITORY='fork/app-foundation' }
            'event' { $values.GITHUB_EVENT_NAME='workflow_dispatch' }
            'ref-type' { $values.GITHUB_REF_TYPE='branch' }
            'branch' { $values.GITHUB_REF="refs/heads/$tag" }
            'workflow-sha' { $values.GITHUB_WORKFLOW_SHA='b'*40 }
            'workflow-ref' { $values.GITHUB_WORKFLOW_REF='other/workflow@refs/heads/main' }
            'invalid-sha' { $values.GITHUB_SHA='invalid';$values.GITHUB_WORKFLOW_SHA='invalid' }
            'acceptance-tag' { $values.GITHUB_REF_NAME=$tag }
            'other-prefix' { $values.GITHUB_REF_NAME='release/'+$productionTag }
            'leading-zero' { $values.GITHUB_REF_NAME='v02.2.0' }
            'version-too-long' { $values.GITHUB_REF_NAME='v123456.2.0' }
            'prerelease' { $values.GITHUB_REF_NAME=$productionTag+'-preview' }
            'tag-newline' { $values.GITHUB_REF_NAME=$productionTag+"`n" }
            'payload-repository' { $payload.repository.full_name='fork/app-foundation' }
            'payload-ref' { $payload.ref="refs/tags/$tag" }
            'created' { $payload.created=$false }
            'deleted' { $payload.deleted=$true }
            'forced' { $payload.forced=$true }
            'before' { $payload.before='b'*40 }
            'after' { $payload.after='invalid' }
            'after-zero' { $payload.after='0'*40 }
            'after-newline' { $payload.after=$sha+"`n" }
            'main' { $values.FIXTURE_MAIN='b'*40 }
            'api-failure' { $values.FIXTURE_FAIL='api' }
            'readiness-failure' { $values.FIXTURE_FAIL='readiness' }
            'readiness-zero' { $values.FIXTURE_READINESS='0' }
            'readiness-invalid' { $values.FIXTURE_READINESS='invalid' }
        }
        if ($case -in @('acceptance-tag','other-prefix','leading-zero','version-too-long','prerelease','tag-newline')) {
            $values.GITHUB_REF='refs/tags/'+$values.GITHUB_REF_NAME
            $values.GITHUB_WORKFLOW_REF='andregoepel/app-foundation/.github/workflows/nuget-prepare.yml@'+$values.GITHUB_REF
            $payload.ref=$values.GITHUB_REF
        }
        [IO.File]::WriteAllText($values.GITHUB_OUTPUT, '', $utf8)
        [IO.File]::WriteAllText($values.GITHUB_STEP_SUMMARY, '', $utf8)
        if ($case -ne 'missing') { [IO.File]::WriteAllText($values.GITHUB_EVENT_PATH, $(if ($case -eq 'malformed') {'{'} else {$payload | ConvertTo-Json -Depth 5}), $utf8) }

        # Act
        $result=Invoke-Step "$root/producer-event.sh" $values

        # Assert
        $valid=$case -in @('lightweight','annotated')
        if (($result.Code -eq 0) -ne $valid) { throw "$case producer event returned $($result.Code): $($result.Text)" }
        $output=[IO.File]::ReadAllText($values.GITHUB_OUTPUT)
        $summary=[IO.File]::ReadAllText($values.GITHUB_STEP_SUMMARY)
        if ($valid) {
            $digest=(Get-FileHash -LiteralPath $values.GITHUB_EVENT_PATH -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($output -ne "version=2.2.0`nreadiness-run=100`nevent-sha256=$digest`n") { throw "$case producer outputs differ" }
            if (-not $summary.Contains($productionTag) -or -not $summary.Contains($after) -or -not $summary.Contains($sha)) { throw "$case event evidence missing" }
            $validEvent=$values.GITHUB_EVENT_PATH
            $validDigest=$digest
        } elseif ($output.Length -ne 0 -or $summary.Length -ne 0) { throw "$case exported producer outputs" }
        $passed++
    }
    foreach ($case in @('valid','changed-event','failed-verifier','cancelled-verifier')) {
        # Arrange
        $values=$base.Clone()
        $values.GITHUB_REF_NAME=$productionTag
        $values.GITHUB_EVENT_PATH=$validEvent
        $values.READINESS_RUN='100'
        $values.EVENT_SHA256=if ($case -eq 'changed-event') {'0'*64} else {$validDigest}
        $values.FIXTURE_CAPTURE="$root/prepare-invocation-$case"
        $values.FIXTURE_EXIT=if ($case -eq 'failed-verifier') {'1'} elseif ($case -eq 'cancelled-verifier') {'130'} else {'0'}

        # Act
        $result=Invoke-Step "$root/prepare.sh" $values

        # Assert
        if ($case -eq 'changed-event') {
            if ($result.Code -eq 0 -or (Test-Path $values.FIXTURE_CAPTURE)) { throw 'Changed event reached preparation' }
        } else {
            if ($result.Code -ne [int]$values.FIXTURE_EXIT) { throw "$case preparation exit code was not propagated" }
            $actual=[IO.File]::ReadAllLines($values.FIXTURE_CAPTURE)
            $expected=@('--nuget-prepare','--sha',$sha,'--version','2.2.0','--readiness-run','100','--tag',$productionTag)
            if ([string]::Join('|',$actual) -ne [string]::Join('|',$expected)) { throw "$case preparation arguments differ" }
        }
        $passed++
    }
    $cases = @('valid','repository','event','ref','workflow-ref','workflow-sha','invalid-sha','action','payload-repository','producer-repository','fork','producer-id','producer-path','producer-event','producer-running','producer-failed','producer-skipped','producer-sha','tag-leading-zero','tag-prerelease','tag-injection','tag-too-long','release-tag','other-prefix','uppercase-id','short-id','tag-newline','run-zero','run-fraction','run-string','run-too-large','attempt-zero','attempt-fraction','attempt-too-large','stale-main','api-failure','malformed','missing')
    foreach ($case in $cases) {
        # Arrange
        $values=$base.Clone()
        $values.GITHUB_OUTPUT="$root/preflight-output-$case"
        $values.GITHUB_EVENT_PATH="$root/event-$case.json"
        $values.FIXTURE_GH_CALLS="$root/preflight-gh-$case"
        $producer=@{ repository=@{full_name='andregoepel/app-foundation'}; head_repository=@{full_name='andregoepel/app-foundation'}; workflow_id=388401675; path='.github/workflows/nuget-prepare.yml'; event='push'; status='completed'; conclusion='success'; head_sha=$sha; head_branch=$tag; id=500; run_attempt=1 }
        $payload=@{action='completed';repository=@{full_name='andregoepel/app-foundation'};workflow_run=$producer}
        switch ($case) {
            'repository' { $values.GITHUB_REPOSITORY='fork/app-foundation' }
            'event' { $values.GITHUB_EVENT_NAME='workflow_dispatch' }
            'ref' { $values.GITHUB_REF='refs/heads/other' }
            'workflow-ref' { $values.GITHUB_WORKFLOW_REF='other/workflow@refs/heads/main' }
            'workflow-sha' { $values.GITHUB_WORKFLOW_SHA='b'*40 }
            'invalid-sha' { $values.GITHUB_SHA='invalid'; $values.GITHUB_WORKFLOW_SHA='invalid' }
            'action' { $payload.action='requested' }
            'payload-repository' { $payload.repository.full_name='fork/app-foundation' }
            'producer-repository' { $producer.repository.full_name='fork/app-foundation' }
            'fork' { $producer.head_repository.full_name='fork/app-foundation' }
            'producer-id' { $producer.workflow_id=900 }
            'producer-path' { $producer.path='.github/workflows/ci.yml' }
            'producer-event' { $producer.event='workflow_dispatch' }
            'producer-running' { $producer.status='in_progress' }
            'producer-failed' { $producer.conclusion='failure' }
            'producer-skipped' { $producer.conclusion='skipped' }
            'producer-sha' { $producer.head_sha='b'*40 }
            'tag-leading-zero' { $producer.head_branch=$tagPrefix+'v02.2.0' }
            'tag-prerelease' { $producer.head_branch=$tag+'-preview' }
            'tag-injection' { $producer.head_branch=$tag+"`nsha=other" }
            'tag-too-long' { $producer.head_branch=$tagPrefix+'v123456.2.0' }
            'release-tag' { $producer.head_branch='v2.2.0' }
            'other-prefix' { $producer.head_branch=$tag.Replace('sonar-poc', 'other') }
            'uppercase-id' { $producer.head_branch=$tag.Replace('abcdef', 'ABCDEF') }
            'short-id' { $producer.head_branch='sonar-poc/nuget/short/v2.2.0' }
            'tag-newline' { $producer.head_branch=$tag+"`n" }
            'run-zero' { $producer.id=0 }
            'run-fraction' { $producer.id=1.5 }
            'run-string' { $producer.id='500' }
            'run-too-large' { $producer.id=9007199254740992 }
            'attempt-zero' { $producer.run_attempt=0 }
            'attempt-fraction' { $producer.run_attempt=1.5 }
            'attempt-too-large' { $producer.run_attempt=2147483648 }
            'stale-main' { $values.FIXTURE_MAIN='b'*40 }
            'api-failure' { $values.FIXTURE_FAIL='api' }
        }
        [IO.File]::WriteAllText($values.GITHUB_OUTPUT, '', $utf8)
        if ($case -ne 'missing') { [IO.File]::WriteAllText($values.GITHUB_EVENT_PATH, $(if ($case -eq 'malformed') {'{'} else {$payload | ConvertTo-Json -Depth 5}), $utf8) }

        # Act
        $result = Invoke-Step "$root/preflight.sh" $values

        # Assert
        if (($result.Code -eq 0) -ne ($case -eq 'valid')) { throw "$case preflight returned $($result.Code): $($result.Text)" }
        $output=[IO.File]::ReadAllText($values.GITHUB_OUTPUT)
        if ($case -eq 'valid') {
            if ($output -ne "sha=$sha`nrun=500`nattempt=1`ntag=$tag`nversion=2.2.0`n") { throw 'Preflight outputs differ' }
        } elseif ($output.Length -ne 0) { throw "$case exported preflight outputs" }
        $passed++
    }
    $cases=@('valid','origin-download','batch-download','origin-missing','origin-link','malformed','oversized','schema','repository','sha','tag','run','attempt','not-dry','eligible','readiness-zero','readiness-string','readiness-fraction','readiness-too-large','tag-object','manifest-injection')
    foreach ($case in $cases) {
        # Arrange
        $values=$base.Clone()
        $values.GITHUB_OUTPUT="$root/download-output-$case"
        $values.FIXTURE_GH_CALLS="$root/download-gh-$case"
        $values.FIXTURE_ORIGIN="$root/origin-$case.json"
        $origin=@{schemaVersion=1;repository='andregoepel/app-foundation';sha=$sha;tag=$tag;runId=500;attempt=1;dryRun=$true;releaseEligible=$false;readinessRunId=100;tagObjectSha=('b'*40);manifestSha256=('c'*64)}
        switch ($case) {
            'schema' { $origin.schemaVersion=2 }
            'repository' { $origin.repository='fork/app-foundation' }
            'sha' { $origin.sha='b'*40 }
            'tag' { $origin.tag='v2.2.0' }
            'run' { $origin.runId=501 }
            'attempt' { $origin.attempt=2 }
            'not-dry' { $origin.dryRun=$false }
            'eligible' { $origin.releaseEligible=$true }
            'readiness-zero' { $origin.readinessRunId=0 }
            'readiness-string' { $origin.readinessRunId='100' }
            'readiness-fraction' { $origin.readinessRunId=1.5 }
            'readiness-too-large' { $origin.readinessRunId=9007199254740992 }
            'tag-object' { $origin.tagObjectSha='invalid' }
            'manifest-injection' { $origin.manifestSha256=('c'*64)+"`nroot=/other" }
            default { $values.FIXTURE_FAIL=$case }
        }
        $json=if ($case -eq 'malformed') {'{'} elseif ($case -eq 'oversized') {' ' * 1048577} else {$origin | ConvertTo-Json}
        [IO.File]::WriteAllText($values.FIXTURE_ORIGIN, $json, $utf8)
        [IO.File]::WriteAllText($values.GITHUB_OUTPUT, '', $utf8)

        # Act
        $result = Invoke-Step "$root/download.sh" $values

        # Assert
        if (($result.Code -eq 0) -ne ($case -eq 'valid')) { throw "$case download returned $($result.Code): $($result.Text)" }
        $lines=[IO.File]::ReadAllLines($values.GITHUB_OUTPUT)
        $handoff=$lines[0].Substring(5)
        if ($case -eq 'valid') {
            if ($lines.Length -ne 4 -or $lines[1] -ne 'readiness=100' -or $lines[2] -ne ('tag-object='+('b'*40)) -or $lines[3] -ne ('manifest='+('c'*64))) { throw 'Download outputs differ' }
            $validHandoff=$handoff
        } else {
            if ($lines.Length -ne 1) { throw "$case exported pins on failure" }
            $cleanup=$base.Clone();$cleanup.HANDOFF_ROOT=$handoff
            if ((Invoke-Step "$root/cleanup.sh" $cleanup).Code -ne 0 -or (Test-Path $handoff)) { throw "$case failed cleanup" }
        }
        $passed++
    }
    foreach ($case in @('valid','failed-verifier','cancelled-verifier')) {
        # Arrange
        $values = $base.Clone()
        $values.HANDOFF_ROOT = $validHandoff
        $values.READINESS_RUN = '100'
        $values.TAG_OBJECT = 'b' * 40
        $values.MANIFEST_SHA256 = 'c' * 64
        $values.FIXTURE_CAPTURE = "$root/invocation-$case"
        $values.FIXTURE_EXIT = if ($case -eq 'valid') {'0'} elseif ($case -eq 'cancelled-verifier') {'130'} else {'1'}

        # Act
        $result = Invoke-Step "$root/rehearse.sh" $values

        # Assert
        if ($result.Code -ne [int]$values.FIXTURE_EXIT) { throw "$case exit code was not propagated" }
        $actual=[IO.File]::ReadAllLines($values.FIXTURE_CAPTURE)
        $expected=@('--nuget-rehearse','--sha',$sha,'--version','2.2.0','--readiness-run','100','--tag',$tag,'--handoff',"$validHandoff/batch",'--manifest-sha256',('c'*64),'--tag-object',('b'*40),'--preparation-run','500','--preparation-attempt','1')
        if ([string]::Join('|',$actual) -ne [string]::Join('|',$expected)) { throw "$case consumer arguments differ" }
        $passed++
    }
    foreach ($case in @('valid','outside-parent','wrong-name','traversal','symlink')) {
        # Arrange
        $values = $base.Clone()
        $candidate=switch ($case) {
            'valid' { $validHandoff }
            'outside-parent' { "$root/nuget-rehearsal.12345678" }
            'wrong-name' { "$root/temp/other" }
            'traversal' { "$root/temp/../nuget-rehearsal.12345678" }
            'symlink' { "$root/temp/nuget-rehearsal.87654321" }
        }
        if ($case -eq 'symlink') {
            [IO.Directory]::CreateDirectory("$root/sentinel") | Out-Null
            & ln -s "$root/sentinel" $candidate
        } else { [IO.Directory]::CreateDirectory($candidate) | Out-Null }
        $values.HANDOFF_ROOT = $candidate

        # Act
        $result = Invoke-Step "$root/cleanup.sh" $values

        # Assert
        if (($result.Code -eq 0) -ne ($case -eq 'valid')) { throw "$case cleanup return mismatch" }
        if ((Test-Path $candidate) -ne ($case -ne 'valid')) { throw "$case cleanup target mismatch" }
        $passed++
    }
    @{passed=$passed;failed=0;synthetic=$true;liveRunnerProof=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'workflow-checks.json')
    Write-Output "$passed actual workflow shell cases passed; synthetic events, downloads and verifier invocation only."

} finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
