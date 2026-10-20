# Inactive official NuGet publishing steps

`nuget-release-tags.ruleset.json` is the reviewed payload for active release-tag
ruleset `25464148`, separate from the workflow templates. The operator applied it
after the explicitly approved enforcement drill passed; rendering or testing
never applies it. See the [drill evidence and remaining controls](../NUGET-ACCEPTANCE.md).

`nuget-publish.steps.yml` is a reviewable step fragment outside `.github/workflows`.
Every step is disabled. The [standby migration](../NUGET-STANDBY.md) registers its
complete disabled rendering and removes the legacy CI publisher. Do not enable
publishing before the remaining #244 controls have been implemented and reviewed.

`nuget-publish.caller.yml` now supplies the disabled caller around that fragment.
`Build-NuGetPublishWorkflow.ps1` renders them into one complete YAML file under
`artifacts/sonar/publish-workflow-template/nuget-publish.yml` for review and linting.
It does not write to `.github/workflows`, execute the workflow or enable any guard.
The checked-in `.github/workflows/nuget-publish.yml` must match this output exactly.
The job and all fifteen rendered steps remain disabled; the fragment is inserted
verbatim with indentation so its tested commands have one maintained source.

The fragment connects the existing `--nuget-verify-handoff` command to the official
`NuGet/login` action and explicit-file `dotnet nuget push` commands. It verifies the
whole batch before login and again before every package/symbol command, compares
the complete inventory and checks the selected file's SHA-256 immediately before
push. Each `.nupkg` suppresses automatic symbol upload; each `.snupkg` has its own
verification and invocation. There is no pack/build, wildcard, skip-duplicate,
outer retry, alternative feed or custom NuGet client.

## Intent and outcome records

Before login, the fragment builds `intent.json` from the verified twelve-file
inventory and stores that single file through the pinned official
`actions/upload-artifact` action. Failure to store it blocks login. Its name is
`nuget-publish-intent-<run-id>-<attempt>`, with overwrite disabled and 90-day
retention requested. The record binds package IDs, versions, filenames, feeds and
hashes to the candidate/tag object, manifest, readiness and preparation runs,
publishing run/attempt and workflow revision. IDs are decimal strings to avoid
JSON-number precision loss. It contains no credentials, packages or runner paths.

The fragment requires the future caller to be
`.github/workflows/nuget-publish.yml@refs/heads/main`, running the candidate SHA.
That workflow is registered as disabled standby. After intent storage, the fragment
checks the action's artifact ID/digest outputs and the local intent's byte hash,
then invokes `--nuget-verify-intent` before login. Each file still receives its
own fresh full-batch verification before the CLI command.

The intent command uses the existing C# GitHub metadata and artifact readers.
After full handoff verification, it independently reads the publishing workflow,
current run/attempt, job steps and complete artifact inventory. It requires the
registered repository-local workflow at the verified main checkout, a first
`workflow_run` attempt on main, successful intent storage, the verification step
in progress, and login/push still pending. Missing or delayed API metadata fails
closed; actual runner timing remains part of live acceptance.

The stored artifact must have the expected name, ID, archive digest and producer
SHA, fit the size limit, be unexpired and have been created during this attempt.
The existing downloader checks the ZIP digest and permits exactly `intent.json`.
The command then checks its content hash, candidate/tag object, workflow revision,
readiness/preparation identities and all twelve package identities, feeds and
hashes. It rereads run/artifact metadata and revalidates the complete handoff before
returning the usual diagnostic inventory (`dryRun: true`, `releaseEligible: false`).
No new HTTP client or upload transport is introduced.

Use the same arguments as `--nuget-verify-handoff`, replacing only the mode.
The current job must supply `GITHUB_REPOSITORY`, `GITHUB_EVENT_NAME`, `GITHUB_REF`,
`GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, `GITHUB_SHA`, `GITHUB_WORKFLOW_REF`,
`GITHUB_WORKFLOW_SHA`, `INTENT_ARTIFACT_ID`, `INTENT_ARTIFACT_DIGEST` and
`INTENT_SHA256`. These pins are checked against independent reads, not treated as
proof by themselves. Both launchers reject OIDC/NuGet publication credentials
before bootstrap for this mode too. Both production jobs remain disabled, so no
qualifying production attempt is available under the standby configuration.

After the push step, including failure or cancellation when the runner can still
execute final steps, `outcomes.json` records observations and the intent's content
hash plus GitHub artifact ID/archive digest. It is stored as a separate
`nuget-publish-outcomes-<run-id>-<attempt>` artifact with the same retention and
no-overwrite settings. Only these two JSON files are uploaded; local working files
and raw command output are excluded.

| `cliObservation` | Meaning |
| --- | --- |
| `not-dispatched` | GitHub reports the push step skipped; no attempt rows exist. |
| `unrecorded` | The push step ran, but no row remains for this file. Remote state is unknown. |
| `uncertain` | A start was recorded without a successful CLI result, including a nonzero exit. |
| `cli-success` | The command returned zero. Remote acceptance/indexing is not independently verified. |

The finalizer validates ordered start/result pairs against the original inventory.
Malformed, reordered, duplicate, foreign, oversized or symlinked logs fail instead
of becoming reassuring outcomes. A missing log after an interrupted push leaves
every file `unrecorded`. A successful push step requires all twelve successful CLI
results. Both records retain `authorizesRetry: false`; outcomes additionally retain
`remoteAcceptanceVerified: false`.

Cleanup after stored intent requires successful outcome storage. Otherwise private
diagnostics remain for investigation while that runner survives. An interrupted
runner can lose them or fail to run finalization altogether; the already stored
intent then means **any intended file may have reached the feed**. Never infer that
missing outcomes mean no upload. An artifact-service failure after accepting intent
still blocks login, because the action did not report success.

These are retained run records, not a complete publication ledger. GitHub artifacts
can expire or be deleted with their run. The future caller/recovery path must
authenticate outcomes and historical records independently; the current-attempt
intent check above does not provide recovery authority. Arbitrary JSON or copied
artifact IDs confer no authority. Missing
or expired records must block automatic retries. Cross-run replay prevention,
longer-term retention and remote reconciliation remain activation blockers.

## Required caller setup

The caller template reserves `.github/workflows/nuget-publish.yml` as a
repository-local job, with protected environment `nuget-publish`, Ubuntu 24.04,
read-only contents/actions permissions and job-scoped `id-token: write` for the
official login step. No environment, permission or NuGet policy is created by
rendering it. One fixed concurrency group serializes this workflow's jobs with no
in-progress cancellation; it is not a durable deduplication or replay control.

Its future trigger is completion of `NuGet preparation`, not main CI or a manual
dispatch. The producer is now named `NuGet preparation`, validates stable
release tags and remains disabled with its updated policy hash. Its previous
runner-test consumers remain unchanged and dormant. Activating production still
requires the separately approved account cutover, protected environments,
reviewed enabling of the guards and another preparation policy hash update.

The caller validates the captured completion event's repository, producer ID/path,
display name, successful push status, stable `vX.Y.Z` tag and first run attempt.
It requires candidate, caller workflow revision and current main to agree before
checkout. It rejects acceptance tags, prereleases, producer reruns and caller
reruns. Event metadata is only initial screening; the existing C# verifier still
independently authenticates live producer provenance, artifacts and readiness
before intent storage/login.

The ordered caller steps then:

1. Check out exactly the validated candidate with credential persistence disabled,
   and select the SDK from `global.json`.
2. Restore and publish only the existing verifier project from that checkout into
   a private temporary directory. GitHub/Sonar/NuGet/OIDC environment credentials
   are removed from these build processes. This happens before artifact downloads.
3. Download the exact origin and complete batch artifact names from the captured
   producer run/attempt using `gh run download`. Read bounded origin pins once,
   rejecting mismatches and line breaks. Local origin JSON supplies candidate
   inputs only; it does not replace independent authenticated provenance.
4. Export the eleven context values below after checking all values and private
   paths. The verifier must come from the bootstrap directory, never the downloaded
   artifact. Rejected context exports no partial environment.
5. Enter the unchanged verification/intent/login/per-file-push/outcome sequence.
   No package rebuild/repack takes place in the caller.
6. Remove the bootstrap and handoff directories after completion. As with the
   publishing diagnostics, stored intent requires successful outcome storage
   before cleanup; failures preserve local evidence while the runner survives.

The exported context contains `VERIFIER_DLL`, `REPOSITORY_ROOT`, `CANDIDATE_SHA`,
`PACKAGE_VERSION`, `READINESS_RUN`, `RELEASE_TAG`, `HANDOFF_ROOT`, `MANIFEST_SHA256`,
`TAG_OBJECT`, `PREPARATION_RUN`, and `PREPARATION_ATTEMPT`. It contains no credentials.

The verifier DLL and repository paths must be absolute, and the complete transferred
batch must be in `HANDOFF_ROOT/batch`. Use a private runner with no concurrent writer
to its source, verifier, configuration or package files. The verifier checks the
live current-main SHA, reviewed producer, tag object and full batch. The fragment
additionally requires a real stable `vX.Y.Z` tag and rejects workflow reruns.

The future job needs reviewed environment protection, read-only GitHub/Sonar
credentials and `id-token: write` for the official login step. Verification children
receive no OIDC/NuGet credentials; CLI push children receive no GitHub/Sonar
credentials. Environment filtering is process hygiene, not isolation against a
compromised runner. NuGet keys remain step-scoped environment values and never
appear as command arguments or persisted configuration.

## Remaining activation blockers

- The caller and producer are registered as disabled standby. The production
  NuGet policy, protected environments and official login action acceptance on
  the intended runner remain pending.
- Intent/outcome artifact steps and independent pre-login intent verification
  passed the separate non-publishing runner drill recorded in
  [standby evidence](../NUGET-STANDBY.md). Login and uploads were simulated.
  The local `attempts.tsv` remains diagnostic input, not trusted recovery authority.
  Implement independent outcome/recovery record authentication, retention,
  cross-run replay protection and remote reconciliation before introducing a retry
  path. Successful intent retrieval proves neither remote package acceptance nor
  the absence of an earlier publication.
- The fragment deliberately supports only the first complete-batch attempt.
  Workflow reruns and a second invocation in the same directory are rejected.
  Manual and symbol-only recovery still require separately reviewed selection,
  fresh full-batch verification and trusted reconciliation.
- NuGet may retry internally. The steps stop on the first nonzero exit but cannot
  prove that the server rejected an upload. Cancellation/runner loss can prevent
  final diagnostics and cleanup.
- The legacy job is removed from current source. Account-level overlapping
  trusted-publisher grants, including trust in old `ci.yml` revisions, remain
  open controls in the separately approved cutover.

## Offline validation

The separate `Sonar NuGet template checks` workflow runs these suites on every
pull request to `main` and push to `main`. It uses Ubuntu 24.04, read-only repository
permission and no environment or secrets. It does not invoke the publishing
workflow, request OIDC credentials or create a tag. Its Action file is unchanged;
the invoked test suite also validates the registered standby configuration.

The workflow downloads actionlint 1.7.12 and checks its pinned archive SHA-256,
then runs `Run-NuGetWorkflowChecks.ps1` in a fresh Linux network namespace as the
runner user with an empty inherited environment. The entry point rejects service
credentials, any network interface other than loopback, and reused result directories.
It lints CI, the rendered and registered disabled publisher, preparation, this
test workflow and both acceptance consumers with actionlint/ShellCheck, then
runs the shell and standby configuration cases.
It stops on a linter or suite failure; successful aggregate evidence is written
only after every suite passes.

The upload step preserves the rendered disabled YAML and synthetic JSON reports
for seven days, including partial reports on failure. These are test diagnostics,
not readiness or publication records. The artifact name binds them to the tested
checkout SHA, run and attempt. A PR run tests GitHub's merge checkout.

This provides repeatable hosted-runner regression coverage. The actual publishing
workflow's scheduling, protected environment, intent/outcome artifact storage and
OIDC exchange still need separate non-publishing acceptance. The harness substitutes
GitHub, verification, login and upload; its `liveRunnerProof: false` flag remains
correct even when the harness itself runs on GitHub.

For a local Linux container with `--network none` and the required tools installed:

```sh
pwsh -NoProfile -File tests/AndreGoepel.AppFoundation.Sonar.Tests/Workflows/Run-NuGetWorkflowChecks.ps1
```

On Linux with PowerShell, Bash and jq, run:

```sh
pwsh -NoProfile -File scripts/sonar/templates/Build-NuGetPublishWorkflow.ps1
actionlint artifacts/sonar/publish-workflow-template/nuget-publish.yml
pwsh -NoProfile -File tests/AndreGoepel.AppFoundation.Sonar.Tests/Workflows/Verify-NuGetPublishCaller.ps1
pwsh -NoProfile -File tests/AndreGoepel.AppFoundation.Sonar.Tests/Workflows/Verify-NuGetPublishSteps.ps1
```

The caller suite renders the actual complete template and checks 62 offline cases:
trigger/source/provenance screening, credential-free verifier bootstrap, exact
artifact names, context propagation into the real fragment's verification shell,
artifact-verifier substitution, failure ordering and cleanup bounds. GitHub, git
and dotnet are substitutes. It also verifies that composition preserves the
fragment bytes and that the pinned actions, step ordering and disabled guards
remain present. The renderer runs on Windows and Linux.

The harness executes the actual shell blocks with a local `dotnet` substitute.
Login is a synthetic marker between steps; it never executes the official action,
requests OIDC credentials or contacts a feed. Acceptance runs use a container with
`--network none` after preparing PowerShell, Bash and jq. It checks ordering, argument and credential separation, package
and symbol commands, rejection before login, changes before/during publication,
partial/uncertain exits, retry refusal and cleanup bounds. This is offline shell
evidence, not proof of GitHub step scheduling or accepted live publishing.
The expanded suite also exercises intent-storage failure before login, stale or
changed evidence after storage, malformed artifact outputs, partial/missing or
damaged logs and outcome-storage failure. Artifact storage and login are simulated
by the harness; static checks cover the exact action inputs and disabled guards.

Official references: [NuGet/login](https://github.com/NuGet/login/tree/8d196754b4036150537f80ac539e15c2f1028841),
[dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push),
[NuGet environment variables](https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-environment-variables).
Artifact behavior: [pinned upload-artifact inputs and outputs](https://github.com/actions/upload-artifact/blob/cf430e030ddbb5b0abf93d22962f4752f3646cd9/action.yml).
Caller behavior: [workflow-run events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run),
[GitHub CLI artifact downloads](https://cli.github.com/manual/gh_run_download),
[NuGet trusted-publisher workflow/environment configuration](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
