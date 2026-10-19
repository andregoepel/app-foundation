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
- `attempts.tsv` is private, local diagnostic data only. A `started` row means the
  remote outcome may be unknown; `cli-success` does not prove package indexing.
  The log does not supply durable, authenticated attempt records or cross-run
  replay protection. Persist trusted intent **before** dispatch and reconcile
  remote outcomes before introducing any retry path; preserve that evidence before
  cleanup. Do not treat the local file as satisfying this requirement.
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

Official references: [NuGet/login](https://github.com/NuGet/login/tree/8d196754b4036150537f80ac539e15c2f1028841),
[dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push),
[NuGet environment variables](https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-environment-variables).
