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
    $names = @('Verify full handoff before login', 'Obtain temporary NuGet credentials', 'Push each reverified package and symbol file', 'Remove private publishing files')
    $positions = @($names | ForEach-Object { [Array]::IndexOf($lines, "  - name: $_") })
    if (@($positions | Where-Object { $_ -lt 0 }).Count -ne 0 -or ($positions -join ',') -ne (($positions | Sort-Object) -join ',')) { throw 'Step order differs.' }
    $guards = @($lines | Where-Object { $_.StartsWith('    if:') })
    if ($guards.Count -ne 4 -or @($guards | Where-Object { $_ -notin @('    if: ${{ false && success() }}', '    if: ${{ false && always() }}') }).Count -ne 0) { throw 'Every fragment step must remain disabled.' }
    if (-not $template.Contains('uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841') -or -not $template.Contains('user: ${{ secrets.NUGET_USER }}')) { throw 'Official login contract differs.' }
    if (@($lines | Where-Object { $_ -match '^\s*(on:|continue-on-error:|retry:)' }).Count -ne 0) { throw 'Fragment cannot trigger or bypass failure.' }

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
    Extract-Step $names[2] "$root/push.sh"
    Extract-Step $names[3] "$root/cleanup.sh"

    $shim = @'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == "$VERIFIER_DLL" ]]; then
  expected=("$VERIFIER_DLL" --root "$REPOSITORY_ROOT" --nuget-verify-handoff --sha "$CANDIDATE_SHA" --version "$PACKAGE_VERSION" --readiness-run "$READINESS_RUN" --tag "$RELEASE_TAG" --handoff "$HANDOFF_ROOT/batch" --manifest-sha256 "$MANIFEST_SHA256" --tag-object "$TAG_OBJECT" --preparation-run "$PREPARATION_RUN" --preparation-attempt "$PREPARATION_ATTEMPT")
  test "$#" -eq "${#expected[@]}"
  index=0
  for argument in "$@"; do test "$argument" = "${expected[$index]}"; index=$((index + 1)); done
  for name in ACTIONS_ID_TOKEN_REQUEST_TOKEN ACTIONS_ID_TOKEN_REQUEST_URL NUGET_API_KEY NUGET_AUTH_TOKEN NUGET_SYMBOL_API_KEY; do test -z "${!name:-}"; done
  for name in APPFOUNDATION_GITHUB_TOKEN APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN; do test "${!name:-}" = synthetic-read-key; done
  count=$(cat "$FIXTURE_VERIFY_COUNT")
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

    $cases = @('valid', 'verifier-before-login', 'cancel-before-login', 'invalid-json', 'invalid-receipt', 'missing-key', 'login-failure', 'verifier-after-login', 'cancel-after-login', 'verifier-after-three', 'changed-inventory', 'changed-byte', 'changed-symbol-byte', 'push-failure', 'push-cancelled', 'changed-config', 'repeat', 'workflow-rerun', 'acceptance-tag', 'wrong-version', 'foreign-repository')
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
            'verifier-after-login' { $values.FIXTURE_FAIL_VERIFY = '2' }
            'cancel-after-login' { $values.FIXTURE_FAIL_VERIFY = '2'; $values.FIXTURE_VERIFY_EXIT = '130' }
            'verifier-after-three' { $values.FIXTURE_FAIL_VERIFY = '5' }
            'changed-inventory' { $values.FIXTURE_CHANGE_INVENTORY = '2' }
            'changed-byte' { $values.FIXTURE_TAMPER_VERIFY = '2' }
            'changed-symbol-byte' { $values.FIXTURE_TAMPER_VERIFY = '3'; $values.FIXTURE_TAMPER_FILE = "$caseRoot/handoff/batch/packages/AndreGoepel.AppFoundation.2.2.0.snupkg" }
            'push-failure' { $values.FIXTURE_FAIL_PUSH = '3' }
            'push-cancelled' { $values.FIXTURE_FAIL_PUSH = '3'; $values.FIXTURE_PUSH_EXIT = '130' }
            'workflow-rerun' { $values.GITHUB_RUN_ATTEMPT = '2' }
            'acceptance-tag' { $values.RELEASE_TAG = 'sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0' }
            'wrong-version' { $values.PACKAGE_VERSION = '9.9.9' }
            'foreign-repository' { $values.GITHUB_REPOSITORY = 'fork/app-foundation' }
        }

        # Act
        $before = Invoke-Step "$root/verify.sh" $values
        $final = $before
        $output = [IO.File]::ReadAllLines($values.GITHUB_OUTPUT)
        if ($output.Length -eq 1) { $values.PUBLISH_ROOT = $output[0].Substring(5) }
        if ($before.Code -eq 0) {
            [IO.File]::AppendAllText($values.FIXTURE_EVENTS, "login`n", $utf8)
            if ($case -eq 'login-failure') { $final = @{Code = 23; Text = ''} }
            else {
                $values.NUGET_API_KEY = if ($case -eq 'missing-key') { '' } else { 'synthetic-nuget-key' }
                $values.NUGET_SYMBOL_API_KEY = 'synthetic-nuget-key'
                if ($case -eq 'changed-config') { [IO.File]::WriteAllText((Join-Path $values.PUBLISH_ROOT 'NuGet.Config'), '<configuration><apikeys /></configuration>', $utf8) }
                $final = Invoke-Step "$root/push.sh" $values
                if ($case -eq 'repeat' -and $final.Code -eq 0) { $final = Invoke-Step "$root/push.sh" $values }
            }
        }

        # Assert
        $events = [IO.File]::ReadAllLines($values.FIXTURE_EVENTS)
        $pushes = [int][IO.File]::ReadAllText($values.FIXTURE_PUSH_COUNT)
        $verifications = [int][IO.File]::ReadAllText($values.FIXTURE_VERIFY_COUNT)
        $expectedPushes = switch ($case) { 'valid' {12} 'repeat' {12} 'push-failure' {3} 'push-cancelled' {3} 'verifier-after-three' {3} 'changed-symbol-byte' {1} default {0} }
        if (($final.Code -eq 0) -ne ($case -eq 'valid') -or $pushes -ne $expectedPushes) { throw "$case failed: code $($final.Code), pushes $pushes. $($final.Text)" }
        if ($before.Code -ne 0 -and $events -contains 'login') { throw "$case reached login after failed verification." }
        for ($index = 0; $index -lt $events.Length; $index++) {
            if ($events[$index] -like 'push:*' -and ($index -eq 0 -or $events[$index - 1] -notlike 'verify:*')) { throw "$case push preceded its fresh check." }
        }
        if ($case -eq 'valid' -or $case -eq 'repeat') {
            if ($verifications -ne 13 -or $events[0] -ne 'verify:1' -or $events[1] -ne 'login') { throw "$case did not verify before login and all twelve files." }
            if (@($events | Where-Object { $_ -like 'push:*.snupkg' }).Count -ne 6) { throw 'Symbols did not get independent commands.' }
        }
        if ($case -in @('push-failure', 'push-cancelled')) {
            if ($final.Code -ne [int]$values.FIXTURE_PUSH_EXIT -or $verifications -ne 4) { throw "$case did not stop at the first uncertain upload." }
            if (-not ([IO.File]::ReadAllText((Join-Path $values.PUBLISH_ROOT 'attempts.tsv'))).Contains("uncertain-exit-$($values.FIXTURE_PUSH_EXIT)")) { throw "$case lost local uncertainty diagnostics." }
        }
        if ($case -in @('cancel-before-login', 'cancel-after-login') -and $final.Code -ne 130) { throw "$case cancellation exit changed." }
        if ($values.ContainsKey('PUBLISH_ROOT')) {
            $cleanup = Invoke-Step "$root/cleanup.sh" $values
            if ($cleanup.Code -ne 0 -or (Test-Path -LiteralPath $values.PUBLISH_ROOT)) { throw "$case cleanup failed." }
        }
        $results.Add(@{name = $case; exitCode = $final.Code; pushes = $pushes; verifications = $verifications})
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
