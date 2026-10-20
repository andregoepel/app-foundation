# Inactive official NuGet publishing steps

`nuget-publish.steps.yml` is a reviewable step fragment outside `.github/workflows`.
Every step is disabled. It neither registers a workflow nor replaces the existing
publisher. Do not enable or copy it into a workflow before the remaining #244
controls have been implemented and reviewed.

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
That workflow is not registered by this change. After intent storage, the fragment
checks the action's artifact ID/digest outputs, checks the local intent's byte hash
and repeats full handoff verification before login. Each file still receives its
own fresh full-batch verification before the CLI command.

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
authenticate workflow/run/attempt, artifact metadata and archive/content digests
independently; arbitrary JSON or copied artifact IDs confer no authority. Missing
or expired records must block automatic retries. Cross-run replay prevention,
longer-term retention and remote reconciliation remain activation blockers.

## Required caller setup

The future repository-local job must authenticate its trigger and preparation
origin, download the batch, use the pinned SDK and bootstrap the verifier from
reviewed source **before** obtaining NuGet credentials. Set these trusted context
variables for all steps: `VERIFIER_DLL`, `REPOSITORY_ROOT`, `CANDIDATE_SHA`,
`PACKAGE_VERSION`, `READINESS_RUN`, `RELEASE_TAG`, `HANDOFF_ROOT`, `MANIFEST_SHA256`,
`TAG_OBJECT`, `PREPARATION_RUN`, and `PREPARATION_ATTEMPT`.

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

- Production trigger/preparation policy, protected environment and official login
  action acceptance on the intended runner are not supplied by this fragment.
- Intent/outcome artifact steps are prepared, but have only offline acceptance.
  The local `attempts.tsv` remains diagnostic input, not trusted recovery authority.
  Implement independent record authentication, retention, cross-run replay
  protection and remote reconciliation before introducing a retry path.
- The fragment deliberately supports only the first complete-batch attempt.
  Workflow reruns and a second invocation in the same directory are rejected.
  Manual and symbol-only recovery still require separately reviewed selection,
  fresh full-batch verification and trusted reconciliation.
- NuGet may retry internally. The steps stop on the first nonzero exit but cannot
  prove that the server rejected an upload. Cancellation/runner loss can prevent
  final diagnostics and cleanup.
- Account-level trusted-publisher/API-key controls for all six packages and removal
  of the existing ungated publisher remain required in the separate activation PR.

## Offline validation

On Linux with PowerShell, Bash and jq, run:

```sh
pwsh -NoProfile -File tests/AndreGoepel.AppFoundation.Sonar.Tests/Workflows/Verify-NuGetPublishSteps.ps1
```

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
