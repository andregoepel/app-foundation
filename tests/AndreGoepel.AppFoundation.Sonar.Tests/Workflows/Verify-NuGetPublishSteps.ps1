param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'Run on Linux with pwsh, bash and jq; use a networkless container.' }
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $RepositoryRoot 'artifacts/sonar/publish-step-tests' }
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$templatePath = Join-Path $RepositoryRoot 'scripts/sonar/templates/nuget-publish.steps.yml'
$template = [IO.File]::ReadAllText($templatePath)
$lines = [IO.File]::ReadAllLines($templatePath)
$root = Join-Path ([IO.Path]::GetTempPath()) ('nuget-publish-tests-' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$results = [Collections.Generic.List[object]]::new()

try {
    [IO.Directory]::CreateDirectory("$root/bin") | Out-Null
    $names = @('Verify full handoff before login', 'Preserve publication intent before login', 'Reverify after preserving intent', 'Obtain temporary NuGet credentials', 'Push each reverified package and symbol file', 'Record publication outcomes', 'Preserve publication outcomes', 'Remove private publishing files')
    $positions = @($names | ForEach-Object { [Array]::IndexOf($lines, "  - name: $_") })
    if (@($positions | Where-Object { $_ -lt 0 }).Count -ne 0 -or ($positions -join ',') -ne (($positions | Sort-Object) -join ',')) { throw 'Step order differs.' }
    $guards = @($lines | Where-Object { $_.StartsWith('    if:') })
    $expectedGuards = @(
        '    if: ${{ false && success() }}', '    if: ${{ false && success() }}',
        '    if: ${{ false && success() }}', '    if: ${{ false && success() }}',
        '    if: ${{ false && success() }}',
        "    if: `${{ false && always() && steps.publish-intent.outcome == 'success' }}",
        "    if: `${{ false && always() && steps.publish-outcomes.outcome == 'success' }}",
        "    if: `${{ false && always() && steps.verified-handoff.outputs.root != '' && (steps.publish-intent.outcome != 'success' || steps.stored-outcomes.outcome == 'success') }}"
    )
    if (($guards -join "`n") -cne ($expectedGuards -join "`n")) { throw 'Disabled guards or failure-preservation ordering differ.' }
    if (-not $template.Contains('uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841') -or -not $template.Contains('user: ${{ secrets.NUGET_USER }}')) { throw 'Official login contract differs.' }
    if (@($lines | Where-Object { $_ -match '^\s*(on:|continue-on-error:|retry:)' }).Count -ne 0) { throw 'Fragment cannot trigger or bypass failure.' }
    foreach ($record in @('intent', 'outcomes')) {
        $name = if ($record -eq 'intent') { $names[1] } else { $names[6] }
        $start = [Array]::IndexOf($lines, "  - name: $name")
        $end = $start + 1
        while ($end -lt $lines.Length -and -not $lines[$end].StartsWith('  - name:')) { $end++ }
        $block = $lines[$start..($end - 1)] -join "`n"
        foreach ($required in @(
            'uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9',
            "name: nuget-publish-$record-`${{ github.run_id }}-`${{ github.run_attempt }}",
            "path: `${{ steps.verified-handoff.outputs.root }}/$record.json",
            'if-no-files-found: error', 'retention-days: 90', 'overwrite: false', 'include-hidden-files: false'
        )) { if (-not $block.Contains($required)) { throw "$record artifact contract differs: $required" } }
    }

    function Extract-Step([string]$name, [string]$path) {
        $start = [Array]::IndexOf($lines, "  - name: $name")
        while ($start -lt $lines.Length -and $lines[$start] -ne '    run: |') { $start++ }
        if ($start -eq $lines.Length) { throw "Missing shell block: $name" }
        $script = [Collections.Generic.List[string]]::new()
        for ($index = $start + 1; $index -lt $lines.Length; $index++) {
            if ($lines[$index].StartsWith('      ')) { $script.Add($lines[$index].Substring(6)) }
            elseif ($lines[$index].Length -eq 0) { $script.Add('') }
            else { break }
        }
        [IO.File]::WriteAllText($path, ($script -join "`n") + "`n", $utf8)
    }
    Extract-Step $names[0] "$root/verify.sh"
    Extract-Step $names[2] "$root/confirm.sh"
    Extract-Step $names[4] "$root/push.sh"
    Extract-Step $names[5] "$root/outcomes.sh"
    Extract-Step $names[7] "$root/cleanup.sh"

    $shim = @'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == "$VERIFIER_DLL" ]]; then
  count=$(cat "$FIXTURE_VERIFY_COUNT")
  mode=--nuget-verify-handoff
  if [[ "$count" == 1 ]]; then
    mode=--nuget-verify-intent
    test "$GITHUB_EVENT_NAME" = workflow_run
    test "$GITHUB_REF" = refs/heads/main
    test "$INTENT_ARTIFACT_ID" = 700
    test "$INTENT_ARTIFACT_DIGEST" = dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd
    test -n "$INTENT_SHA256"
  fi
  expected=("$VERIFIER_DLL" --root "$REPOSITORY_ROOT" "$mode" --sha "$CANDIDATE_SHA" --version "$PACKAGE_VERSION" --readiness-run "$READINESS_RUN" --tag "$RELEASE_TAG" --handoff "$HANDOFF_ROOT/batch" --manifest-sha256 "$MANIFEST_SHA256" --tag-object "$TAG_OBJECT" --preparation-run "$PREPARATION_RUN" --preparation-attempt "$PREPARATION_ATTEMPT")
  test "$#" -eq "${#expected[@]}"
  index=0
  for argument in "$@"; do test "$argument" = "${expected[$index]}"; index=$((index + 1)); done
  for name in ACTIONS_ID_TOKEN_REQUEST_TOKEN ACTIONS_ID_TOKEN_REQUEST_URL NUGET_API_KEY NUGET_AUTH_TOKEN NUGET_SYMBOL_API_KEY; do test -z "${!name:-}"; done
  for name in APPFOUNDATION_GITHUB_TOKEN APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN; do test "${!name:-}" = synthetic-read-key; done
  count=$((count + 1))
  printf '%s' "$count" > "$FIXTURE_VERIFY_COUNT"
  printf 'verify:%s\n' "$count" >> "$FIXTURE_EVENTS"
  if [[ "$count" == "$FIXTURE_FAIL_VERIFY" ]]; then exit "$FIXTURE_VERIFY_EXIT"; fi
  if [[ "$FIXTURE_RECEIPT" == malformed ]]; then printf '{'; exit 0; fi
  if [[ "$FIXTURE_RECEIPT" == unauthorized ]]; then jq '.releaseEligible = true' "$FIXTURE_INVENTORY"; exit 0; fi
  if [[ "$count" == "$FIXTURE_CHANGE_INVENTORY" ]]; then
    jq '.packages[0].sha256 = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"' "$FIXTURE_INVENTORY"
  else
    cat "$FIXTURE_INVENTORY"
  fi
  if [[ "$count" == "$FIXTURE_TAMPER_VERIFY" ]]; then printf 'changed-after-verification' > "$FIXTURE_TAMPER_FILE"; fi
elif [[ "$1" == nuget && "$2" == push ]]; then
  for name in APPFOUNDATION_GITHUB_TOKEN APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN GITHUB_TOKEN GH_TOKEN ACTIONS_ID_TOKEN_REQUEST_TOKEN ACTIONS_ID_TOKEN_REQUEST_URL NUGET_AUTH_TOKEN; do test -z "${!name:-}"; done
  test "$NUGET_API_KEY" = synthetic-nuget-key
  test "$NUGET_SYMBOL_API_KEY" = synthetic-nuget-key
  file=$(basename -- "$3")
  test "$3" = "$HANDOFF_ROOT/batch/packages/$file"
  expected=(nuget push "$3" --source https://api.nuget.org/v3/index.json --configfile "$PUBLISH_ROOT/NuGet.Config" --timeout 300)
  case "$file" in
    *.snupkg) expected+=(--symbol-source https://www.nuget.org/api/v2/symbolpackage --no-service-endpoint) ;;
    *.nupkg) expected+=(--no-symbols) ;;
    *) exit 90 ;;
  esac
  test "$#" -eq "${#expected[@]}"
  index=0
  for argument in "$@"; do test "$argument" = "${expected[$index]}"; index=$((index + 1)); done
  digest=$(sha256sum -- "$3")
  test "${digest%% *}" = "$(jq -r --arg file "$file" '.packages[] | select(.file == $file) | .sha256' "$FIXTURE_INVENTORY")"
  count=$(cat "$FIXTURE_PUSH_COUNT")
  count=$((count + 1))
  printf '%s' "$count" > "$FIXTURE_PUSH_COUNT"
  printf 'push:%s:%s\n' "$count" "$file" >> "$FIXTURE_EVENTS"
  if [[ "$count" == "$FIXTURE_FAIL_PUSH" ]]; then exit "$FIXTURE_PUSH_EXIT"; fi
else
  echo 'Unexpected dotnet command in offline fixture.' >&2
  exit 91
fi
'@
    [IO.File]::WriteAllText("$root/bin/dotnet", $shim + "`n", $utf8)
    & chmod +x "$root/bin/dotnet"
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare dotnet substitute.' }

    function Invoke-Step([string]$path, [hashtable]$values) {
        $start = [Diagnostics.ProcessStartInfo]::new('bash')
        $start.ArgumentList.Add($path)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($pair in $values.GetEnumerator()) { $start.Environment[$pair.Key] = [string]$pair.Value }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            $process.Start() | Out-Null
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'Offline step timed out.' }
            $result = @{ Code = $process.ExitCode; Text = $stdout.Result + $stderr.Result }
            if ($result.Text -match 'synthetic-(nuget|read|oidc)-key') { throw 'Synthetic credential leaked into output.' }
            return $result
        } finally { $process.Dispose() }
    }

    $cases = @('valid', 'verifier-before-login', 'cancel-before-login', 'invalid-json', 'invalid-receipt', 'missing-key', 'login-failure', 'verifier-after-login', 'cancel-after-login', 'verifier-after-three', 'changed-inventory', 'changed-byte', 'changed-symbol-byte', 'push-failure', 'push-cancelled', 'changed-config', 'repeat', 'workflow-rerun', 'acceptance-tag', 'wrong-version', 'foreign-repository', 'workflow-sha', 'workflow-ref', 'intent-upload-failure', 'missing-intent-id', 'invalid-intent-digest', 'changed-intent', 'stale-after-intent', 'changed-inventory-after-intent', 'outcome-upload-failure', 'lost-attempt-log', 'truncated-last-outcome', 'malformed-attempts', 'out-of-order-attempts', 'duplicate-attempt', 'unknown-attempt-file', 'uncertain-then-continued', 'symlink-attempts', 'oversized-attempts')
    $cases += @('missing-log-success', 'incomplete-success')
    $damagedLogs = @('malformed-attempts', 'out-of-order-attempts', 'duplicate-attempt', 'unknown-attempt-file', 'uncertain-then-continued', 'symlink-attempts', 'oversized-attempts', 'missing-log-success', 'incomplete-success')
    $afterPush = @('outcome-upload-failure', 'lost-attempt-log', 'truncated-last-outcome') + $damagedLogs
    foreach ($case in $cases) {
        # Arrange
        $caseRoot = "$root/$case"
        foreach ($directory in @("$caseRoot/temp", "$caseRoot/source", "$caseRoot/verifier", "$caseRoot/handoff/batch/packages")) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
        [IO.File]::WriteAllText("$caseRoot/verifier/Verifier.dll", '', $utf8)
        $packages = foreach ($id in @('AndreGoepel.AppFoundation.Core', 'AndreGoepel.AppFoundation.ServiceDefaults', 'AndreGoepel.AppFoundation.MailService', 'AndreGoepel.AppFoundation', 'AndreGoepel.AppFoundation.Hosting', 'AndreGoepel.AppFoundation.Aspire')) {
            foreach ($extension in @('nupkg', 'snupkg')) {
                $file = "$id.2.2.0.$extension"
                $path = "$caseRoot/handoff/batch/packages/$file"
                [IO.File]::WriteAllText($path, "fixture:$file", $utf8)
                @{ file = $file; packageId = $id; version = '2.2.0'; sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant(); feed = 'https://api.nuget.org/v3/index.json' }
            }
        }
        [IO.File]::WriteAllText("$caseRoot/inventory.json", (@{packages = @($packages); dryRun = $true; releaseEligible = $false} | ConvertTo-Json -Depth 5), $utf8)
        foreach ($file in @('events', 'output')) { [IO.File]::WriteAllText("$caseRoot/$file", '', $utf8) }
        foreach ($file in @('verify-count', 'push-count')) { [IO.File]::WriteAllText("$caseRoot/$file", '0', $utf8) }
        $values = @{
            PATH = "$root/bin:$env:PATH"; RUNNER_TEMP = "$caseRoot/temp"; GITHUB_OUTPUT = "$caseRoot/output";
            GITHUB_REPOSITORY = 'andregoepel/app-foundation'; GITHUB_RUN_ATTEMPT = '1';
            GITHUB_EVENT_NAME = 'workflow_run'; GITHUB_REF = 'refs/heads/main';
            GITHUB_RUN_ID = '600'; GITHUB_SHA = 'a' * 40; GITHUB_WORKFLOW_SHA = 'a' * 40;
            GITHUB_WORKFLOW_REF = 'andregoepel/app-foundation/.github/workflows/nuget-publish.yml@refs/heads/main';
            INTENT_ARTIFACT_ID = '700'; INTENT_ARTIFACT_DIGEST = 'd' * 64;
            VERIFIER_DLL = "$caseRoot/verifier/Verifier.dll"; REPOSITORY_ROOT = "$caseRoot/source";
            CANDIDATE_SHA = 'a' * 40; PACKAGE_VERSION = '2.2.0'; RELEASE_TAG = 'v2.2.0'; READINESS_RUN = '100';
            HANDOFF_ROOT = "$caseRoot/handoff"; MANIFEST_SHA256 = 'b' * 64; TAG_OBJECT = 'c' * 40;
            PREPARATION_RUN = '500'; PREPARATION_ATTEMPT = '1';
            APPFOUNDATION_GITHUB_TOKEN = 'synthetic-read-key'; APPFOUNDATION_SONAR_TOKEN = 'synthetic-read-key'; APPFOUNDATION_SONAR_VERIFY_TOKEN = 'synthetic-read-key';
            GITHUB_TOKEN = 'synthetic-read-key'; GH_TOKEN = 'synthetic-read-key';
            ACTIONS_ID_TOKEN_REQUEST_TOKEN = 'synthetic-oidc-key'; ACTIONS_ID_TOKEN_REQUEST_URL = 'https://oidc.invalid';
            NUGET_API_KEY = ''; NUGET_SYMBOL_API_KEY = ''; NUGET_AUTH_TOKEN = '';
            FIXTURE_EVENTS = "$caseRoot/events"; FIXTURE_INVENTORY = "$caseRoot/inventory.json";
            FIXTURE_VERIFY_COUNT = "$caseRoot/verify-count"; FIXTURE_PUSH_COUNT = "$caseRoot/push-count";
            FIXTURE_FAIL_VERIFY = '0'; FIXTURE_VERIFY_EXIT = '1'; FIXTURE_FAIL_PUSH = '0'; FIXTURE_PUSH_EXIT = '21';
            FIXTURE_RECEIPT = 'valid'; FIXTURE_CHANGE_INVENTORY = '0'; FIXTURE_TAMPER_VERIFY = '0';
            FIXTURE_TAMPER_FILE = "$caseRoot/handoff/batch/packages/AndreGoepel.AppFoundation.2.2.0.nupkg"
        }
        switch ($case) {
            'verifier-before-login' { $values.FIXTURE_FAIL_VERIFY = '1' }
            'cancel-before-login' { $values.FIXTURE_FAIL_VERIFY = '1'; $values.FIXTURE_VERIFY_EXIT = '130' }
            'invalid-json' { $values.FIXTURE_RECEIPT = 'malformed' }
            'invalid-receipt' { $values.FIXTURE_RECEIPT = 'unauthorized' }
            'verifier-after-login' { $values.FIXTURE_FAIL_VERIFY = '3' }
            'cancel-after-login' { $values.FIXTURE_FAIL_VERIFY = '3'; $values.FIXTURE_VERIFY_EXIT = '130' }
            'verifier-after-three' { $values.FIXTURE_FAIL_VERIFY = '6' }
            'changed-inventory' { $values.FIXTURE_CHANGE_INVENTORY = '3' }
            'changed-byte' { $values.FIXTURE_TAMPER_VERIFY = '3' }
            'changed-symbol-byte' { $values.FIXTURE_TAMPER_VERIFY = '4'; $values.FIXTURE_TAMPER_FILE = "$caseRoot/handoff/batch/packages/AndreGoepel.AppFoundation.2.2.0.snupkg" }
            'push-failure' { $values.FIXTURE_FAIL_PUSH = '3' }
            'push-cancelled' { $values.FIXTURE_FAIL_PUSH = '3'; $values.FIXTURE_PUSH_EXIT = '130' }
            'workflow-rerun' { $values.GITHUB_RUN_ATTEMPT = '2' }
            'acceptance-tag' { $values.RELEASE_TAG = 'sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0' }
            'wrong-version' { $values.PACKAGE_VERSION = '9.9.9' }
            'foreign-repository' { $values.GITHUB_REPOSITORY = 'fork/app-foundation' }
            'workflow-sha' { $values.GITHUB_WORKFLOW_SHA = 'f' * 40 }
            'workflow-ref' { $values.GITHUB_WORKFLOW_REF = 'andregoepel/app-foundation/.github/workflows/unknown.yml@refs/heads/main' }
            'missing-intent-id' { $values.INTENT_ARTIFACT_ID = '' }
            'invalid-intent-digest' { $values.INTENT_ARTIFACT_DIGEST = 'invalid' }
            'stale-after-intent' { $values.FIXTURE_FAIL_VERIFY = '2' }
            'changed-inventory-after-intent' { $values.FIXTURE_CHANGE_INVENTORY = '2' }
        }

        # Act
        $before = Invoke-Step "$root/verify.sh" $values
        $final = $before
        $intentStored = $false
        $outcomesStored = $false
        $values.LOGIN_OUTCOME = 'skipped'
        $values.PUSH_OUTCOME = 'skipped'
        $output = [IO.File]::ReadAllLines($values.GITHUB_OUTPUT)
        foreach ($line in $output) {
            if ($line.StartsWith('root=')) { $values.PUBLISH_ROOT = $line.Substring(5) }
            if ($line.StartsWith('intent-sha256=')) { $values.INTENT_SHA256 = $line.Substring(14) }
        }
        if ($before.Code -eq 0) {
            [IO.File]::AppendAllText($values.FIXTURE_EVENTS, "store-intent`n", $utf8)
            if ($case -eq 'intent-upload-failure') { $final = @{Code = 24; Text = ''} }
            else {
                $intentStored = $true
                Copy-Item -LiteralPath (Join-Path $values.PUBLISH_ROOT 'intent.json') -Destination "$caseRoot/stored-intent.json"
                if ($case -eq 'changed-intent') { [IO.File]::AppendAllText((Join-Path $values.PUBLISH_ROOT 'intent.json'), ' ', $utf8) }
                $final = Invoke-Step "$root/confirm.sh" $values
            }
        }
        if ($final.Code -eq 0) {
            [IO.File]::AppendAllText($values.FIXTURE_EVENTS, "login`n", $utf8)
            if ($case -eq 'login-failure') { $values.LOGIN_OUTCOME = 'failure'; $final = @{Code = 23; Text = ''} }
            else {
                $values.LOGIN_OUTCOME = 'success'
                $values.NUGET_API_KEY = if ($case -eq 'missing-key') { '' } else { 'synthetic-nuget-key' }
                $values.NUGET_SYMBOL_API_KEY = 'synthetic-nuget-key'
                if ($case -eq 'changed-config') { [IO.File]::WriteAllText((Join-Path $values.PUBLISH_ROOT 'NuGet.Config'), '<configuration><apikeys /></configuration>', $utf8) }
                $final = Invoke-Step "$root/push.sh" $values
                if ($case -eq 'repeat' -and $final.Code -eq 0) { $final = Invoke-Step "$root/push.sh" $values }
                $values.PUSH_OUTCOME = if ($final.Code -eq 0) { 'success' } elseif ($final.Code -eq 130) { 'cancelled' } else { 'failure' }
            }
        }
        if ($case -in $afterPush) {
            $log = Join-Path $values.PUBLISH_ROOT 'attempts.tsv'
            $rows = [IO.File]::ReadAllLines($log)
            switch ($case) {
                'lost-attempt-log' { Remove-Item -LiteralPath $log; $values.PUSH_OUTCOME = 'cancelled' }
                'truncated-last-outcome' { [IO.File]::WriteAllLines($log, $rows[0..22], $utf8); $values.PUSH_OUTCOME = 'cancelled' }
                'missing-log-success' { Remove-Item -LiteralPath $log }
                'incomplete-success' { [IO.File]::WriteAllLines($log, $rows[0..22], $utf8) }
                'malformed-attempts' { [IO.File]::WriteAllText($log, "malformed`n", $utf8) }
                'out-of-order-attempts' { [IO.File]::WriteAllLines($log, @($rows[1], $rows[0]), $utf8) }
                'duplicate-attempt' { [IO.File]::WriteAllLines($log, @($rows[0], $rows[0]), $utf8) }
                'unknown-attempt-file' { [IO.File]::WriteAllText($log, "unknown.nupkg`tstarted`n", $utf8) }
                'uncertain-then-continued' { $rows[1] = $rows[1].Replace('cli-success', 'uncertain-exit-21'); [IO.File]::WriteAllLines($log, $rows, $utf8) }
                'symlink-attempts' { Move-Item -LiteralPath $log -Destination "$caseRoot/log-target"; & ln -s "$caseRoot/log-target" $log }
                'oversized-attempts' { [IO.File]::WriteAllText($log, ('x' * 16385), $utf8) }
            }
        }
        $recorded = $null
        if ($intentStored) {
            $recorded = Invoke-Step "$root/outcomes.sh" $values
            if ($recorded.Code -eq 0 -and $case -ne 'outcome-upload-failure') {
                Copy-Item -LiteralPath (Join-Path $values.PUBLISH_ROOT 'outcomes.json') -Destination "$caseRoot/stored-outcomes.json"
                $outcomesStored = $true
            }
        }

        # Assert
        $events = [IO.File]::ReadAllLines($values.FIXTURE_EVENTS)
        $pushes = [int][IO.File]::ReadAllText($values.FIXTURE_PUSH_COUNT)
        $verifications = [int][IO.File]::ReadAllText($values.FIXTURE_VERIFY_COUNT)
        $expectedPushes = if ($case -in $afterPush) { 12 } else { switch ($case) { 'valid' {12} 'repeat' {12} 'push-failure' {3} 'push-cancelled' {3} 'verifier-after-three' {3} 'changed-symbol-byte' {1} default {0} } }
        if (($final.Code -eq 0) -ne ($case -eq 'valid' -or $case -in $afterPush) -or $pushes -ne $expectedPushes) { throw "$case failed: code $($final.Code), pushes $pushes. $($final.Text)" }
        if ($before.Code -ne 0 -and $events -contains 'login') { throw "$case reached login after failed verification." }
        for ($index = 0; $index -lt $events.Length; $index++) {
            if ($events[$index] -like 'push:*' -and ($index -eq 0 -or $events[$index - 1] -notlike 'verify:*')) { throw "$case push preceded its fresh check." }
        }
        if ($case -eq 'valid' -or $case -eq 'repeat') {
            if ($verifications -ne 14 -or ($events[0..3] -join ',') -ne 'verify:1,store-intent,verify:2,login') { throw "$case did not store intent and verify before login and all twelve files." }
            if (@($events | Where-Object { $_ -like 'push:*.snupkg' }).Count -ne 6) { throw 'Symbols did not get independent commands.' }
        }
        if ($case -in @('push-failure', 'push-cancelled')) {
            if ($final.Code -ne [int]$values.FIXTURE_PUSH_EXIT -or $verifications -ne 5) { throw "$case did not stop at the first uncertain upload." }
            if (-not ([IO.File]::ReadAllText((Join-Path $values.PUBLISH_ROOT 'attempts.tsv'))).Contains("uncertain-exit-$($values.FIXTURE_PUSH_EXIT)")) { throw "$case lost local uncertainty diagnostics." }
        }
        if ($case -in @('cancel-before-login', 'cancel-after-login') -and $final.Code -ne 130) { throw "$case cancellation exit changed." }
        if ($case -in @('intent-upload-failure', 'missing-intent-id', 'invalid-intent-digest', 'changed-intent', 'stale-after-intent', 'changed-inventory-after-intent') -and $events -contains 'login') { throw "$case reached login without preserved current intent." }
        if ($intentStored) {
            $intent = Get-Content -LiteralPath "$caseRoot/stored-intent.json" -Raw | ConvertFrom-Json
            if ($intent.packages.Count -ne 12 -or $intent.publishRunId -ne '600' -or $intent.publishAttempt -ne '1' -or $intent.preparationRunId -ne '500' -or $intent.preparationAttempt -ne '1' -or $intent.readinessRunId -ne '100' -or $intent.manifestSha256 -cne ('b' * 64) -or $intent.tagObjectSha -cne ('c' * 40) -or $intent.authorizesRetry -ne $false) { throw "$case lost intent identity." }
            $invalidRecord = $case -in ($damagedLogs + @('missing-intent-id', 'invalid-intent-digest', 'changed-intent'))
            if (($recorded.Code -eq 0) -eq $invalidRecord) { throw "$case outcome validation differs. $($recorded.Text)" }
            if ($recorded.Code -eq 0) {
                $outcomes = Get-Content -LiteralPath (Join-Path $values.PUBLISH_ROOT 'outcomes.json') -Raw | ConvertFrom-Json
                if ($outcomes.authorizesRetry -ne $false -or $outcomes.remoteAcceptanceVerified -ne $false -or $outcomes.intentSha256 -cne $values.INTENT_SHA256 -or $outcomes.intentArtifactId -ne '700' -or $outcomes.intentArtifactDigest -cne ('d' * 64) -or $outcomes.packages.Count -ne 12) { throw "$case outcome identity/authority differs." }
                $counts = @{}
                foreach ($state in @('cli-success', 'uncertain', 'unrecorded', 'not-dispatched')) { $counts[$state] = @($outcomes.packages | Where-Object cliObservation -eq $state).Count }
                if ($case -eq 'valid' -and $counts['cli-success'] -ne 12) { throw 'Successful CLI observations missing.' }
                if ($case -in @('push-failure', 'push-cancelled') -and ($counts['cli-success'] -ne 2 -or $counts['uncertain'] -ne 1 -or $counts['unrecorded'] -ne 9)) { throw 'Partial uncertainty lost.' }
                if ($case -eq 'lost-attempt-log' -and $counts['unrecorded'] -ne 12) { throw 'Missing log must preserve uncertainty.' }
                if ($case -eq 'truncated-last-outcome' -and ($counts['cli-success'] -ne 11 -or $counts['uncertain'] -ne 1)) { throw 'Interrupted last result must remain uncertain.' }
                if ($values.PUSH_OUTCOME -eq 'skipped' -and $counts['not-dispatched'] -ne 12) { throw 'Skipped push status differs.' }
            }
            foreach ($record in @("$caseRoot/stored-intent.json", (Join-Path $values.PUBLISH_ROOT 'outcomes.json'))) {
                if ((Test-Path -LiteralPath $record) -and ([IO.File]::ReadAllText($record) -match 'synthetic-(nuget|read|oidc)-key|/tmp/|NuGet.Config')) { throw 'Record contains credentials or private runner paths.' }
            }
        }
        if ($values.ContainsKey('PUBLISH_ROOT')) {
            if (-not $intentStored -or $outcomesStored) {
                $cleanup = Invoke-Step "$root/cleanup.sh" $values
                if ($cleanup.Code -ne 0 -or (Test-Path -LiteralPath $values.PUBLISH_ROOT)) { throw "$case cleanup failed." }
            } elseif (-not (Test-Path -LiteralPath $values.PUBLISH_ROOT)) { throw "$case lost unpreserved local evidence." }
        }
        $results.Add(@{name = $case; exitCode = $final.Code; pushes = $pushes; verifications = $verifications; intentStored = $intentStored; outcomesStored = $outcomesStored})
    }

    foreach ($case in @('outside-parent', 'wrong-name', 'traversal', 'symlink')) {
        # Arrange
        $values = @{RUNNER_TEMP = "$root/cleanup-temp"}
        [IO.Directory]::CreateDirectory($values.RUNNER_TEMP) | Out-Null
        $candidate = switch ($case) {
            'outside-parent' { "$root/nuget-publish.12345678" }
            'wrong-name' { "$root/cleanup-temp/unrelated" }
            'traversal' { "$root/cleanup-temp/../nuget-publish.12345678" }
            'symlink' { "$root/cleanup-temp/nuget-publish.87654321" }
        }
        if ($case -eq 'symlink') {
            [IO.Directory]::CreateDirectory("$root/sentinel") | Out-Null
            & ln -s "$root/sentinel" $candidate
        } else { [IO.Directory]::CreateDirectory($candidate) | Out-Null }
        $values.PUBLISH_ROOT = $candidate

        # Act
        $result = Invoke-Step "$root/cleanup.sh" $values

        # Assert
        if ($result.Code -eq 0 -or -not (Test-Path -LiteralPath $candidate)) { throw "$case cleanup escaped its owned directory." }
        $results.Add(@{name = "cleanup-$case"; exitCode = $result.Code})
    }

    @{passed = $results.Count; failed = 0; synthetic = $true; liveRunnerProof = $false; templateSha256 = (Get-FileHash $templatePath -Algorithm SHA256).Hash.ToLowerInvariant(); cases = $results} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'publish-step-checks.json')
    Write-Output "$($results.Count) offline publishing-step cases passed. Login and dotnet are substitutes; no publication was performed."
} finally {
    if (Test-Path -LiteralPath $root) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($root)) -ne [IO.Path]::GetTempPath().TrimEnd('/')) { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
