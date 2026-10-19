# NuGet preparation provenance and production integration (#244)

Status: verifier, tag-event capture, origin preparation and non-publishing consumer
commands prepared, with disabled producer and handoff-rehearsal workflows. The
producer identity is registered in the consumer policy. Production activation is
pending; the NuGet barrier remains inactive. Existing workflows and credentials
are unchanged by the new rehearsal workflow.

## Implemented preparation-origin verification

`NuGetProvenance` uses the existing `IGitHubClient` and `GitHubArtifacts` reader.
Its policy supplies an independently reviewed workflow ID, exact workflow-file
SHA-256, maximum age and required job/step names. These values are trusted consumer
configuration; accepting them from an artifact, release input or producer output
would remove the trust boundary. An absent/zero workflow ID fails closed.

The consumer pins a preparation run ID and attempt for the entire operation.
The verifier requires the newest run in a complete tag/SHA inventory, from this
repository and `.github/workflows/nuget-prepare.yml`, with a successful `push`
event for the exact candidate SHA and version tag. It rejects unknown reusable
workflows, future or expired runs, other attempts and incomplete job inventories.
Every required job and step must have succeeded in the current attempt.

The exact workflow file is fetched at the candidate SHA and checked against the
reviewed hash. This policy review must establish a tag-only producer trigger and
the event-capture logic described below: a REST run's branch name alone does not
prove a tag push rather than a branch with the same name.

The origin artifact name is `nuget-origin-<sha>-<run-id>-<attempt>`. Its GitHub
metadata must bind it to that run/SHA/tag, have a valid lifetime and size, and have
been created during the current attempt. The existing artifact reader verifies
the archive digest, follows only approved signed-storage redirects without
forwarding the GitHub token, and accepts only a single `origin.json` ZIP entry.

`origin.json` is a schema-1 `NuGetPreparationOrigin` containing:

- Repository, candidate SHA, exact version tag and its originally captured Git object SHA.
- Preparation run ID/attempt and the main readiness run ID.
- SHA-256 of the exact `dry-run.json` bytes and the preparation timestamp.
- `dryRun: true` and `releaseEligible: false`.

Every field must agree with the proposed handoff. Run/attempt and artifact identity
are rechecked after download. `NuGetPreparedHandoff` repeats provenance verification
around the existing package/readiness verifier, rejecting replacement artifacts.
The rehearsal uses this composition before simulated login and each file upload;
it never falls back to unauthenticated pins when verification fails.

The 66 added cases exercise this path with real artifact ZIP/digest handling,
synthetic GitHub/Sonar responses and the actual package handoff/rehearsal. They
cover unapproved workflow content, forks, superseded runs/attempts, skipped steps,
stale or replaced artifacts, malformed or mismatched origins, and races during
download, readiness, login and subsequent uploads. No real tag or package upload
is created. These are verifier integration tests, not live producer proof.

## Implemented producer command

The console tool and both bootstrap scripts accept:

```text
--nuget-prepare --sha SHA --version X.Y.Z --readiness-run ID --tag vX.Y.Z
```

The console tool additionally requires `--root ROOT` before these arguments;
`analyze.ps1` and `analyze.sh` supply their repository root automatically. This
mode is reserved for the future reviewed `.github/workflows/nuget-prepare.yml`
tag-push job. It does not register or enable that workflow. The ordinary
`--nuget-dry-run` remains available without a tag-push context and does not write
an origin artifact.

Both bootstraps reject OIDC and NuGet publication credentials before restore or
build. The command repeats that check and captures `GITHUB_EVENT_PATH` and the
runner context before configuration, network checks or package preparation. The
event must create the exact version-tag ref in this repository, with a zero
previous SHA, `created: true`, `deleted: false` and `forced: false`. Repository,
workspace, ref type, workflow path/revision, candidate SHA, run ID and attempt
must agree. The event file is bounded to 1 MiB; malformed/missing data fails
before preparation. Captured values are held in memory and are not reread from
the event file after packing.

The command preserves the original event's `after` value and requires it to match
the live tag object's SHA before any packing. It resolves that object to the
candidate commit and repeats the object check through `NuGetHandoff` after
packing. A tag changed to another annotated object for the same commit is rejected.
It never fills a missing original object identity from a later ref lookup.

GitHub describes `after` as the commit SHA after the push. Actual annotated-tag
payload behavior is still an acceptance dependency: this implementation accepts
only events whose `after` value pins the exact ref object. If an annotated-tag
event supplies only the peeled commit, preparation rejects it. Synthetic tests
exercise both exact-object fixtures and this rejection; they do not establish
which payload GitHub sends for annotated tags. Do not claim production support
for that case until the intended-runner evidence exists. There is no permissive
fallback or manual override.

After the existing versioned dry run, the command independently validates the
complete manifest/package batch with fresh readiness and the captured tag object.
Only then does it write `origin/origin.json` beside `dry-run.json`. It writes a
pending file first and renames it after serialization, refuses an existing origin
directory, and never overwrites an earlier origin. Failures before origin creation
can leave diagnostic package/dry-run output; those files confer no authority.
The consumer still independently verifies live readiness and every transferred
byte, so an origin file alone cannot authorize publication.

Successful output is JSON with `manifestPath`, `originPath`, `artifactName`,
`dryRun: true` and `releaseEligible: false`. The future workflow must upload only
`origin.json` at the root of the named origin artifact, separately from the
manifest and all twelve package/symbol files, and only after command success.
No artifact upload, NuGet login, tag creation or package publication occurs in
the command itself.

When `GITHUB_OUTPUT` is present, successful preparation also writes five step
outputs: `manifest-path`, `packages-path`, `origin-path`, `origin-artifact-name`
and `batch-artifact-name`. The runner output file must already exist, be empty,
unlinked and inside `RUNNER_TEMP`. Export requires the expected batch/origin
layout under the checkout's `artifacts/sonar`, matching artifact identity and
existing unlinked files/directories. Line breaks and artifact glob characters
are rejected. Failed preparation never exports paths; an export failure fails
the command. A standalone invocation without `GITHUB_OUTPUT` retains JSON output.
These outputs locate files only; they confer no publication authority.

Runner variables and a local event file are not independently authenticated
credentials. The reviewed workflow and protected environment must establish
their source, including a trusted tool bootstrap before this command runs.
The consumer's independently registered workflow ID/hash and GitHub artifact
verification remain essential; setting these variables locally is not acceptance
evidence or publication authorization.

## Implemented non-publishing consumer command

The console tool and both bootstraps accept this exact argument order:

```text
--nuget-rehearse --sha SHA --version X.Y.Z --readiness-run ID --tag vX.Y.Z --handoff ABSOLUTE_DIRECTORY --manifest-sha256 HASH --tag-object SHA --preparation-run ID --preparation-attempt NUMBER [--files FILE ...]
```

The console tool requires `--root ROOT` first; the bootstraps supply it. The
handoff directory contains `dry-run.json` and `packages/` with all six package and
six symbol files. The caller supplies candidate pins, which are independently
matched against the authenticated origin artifact; local files cannot establish
their own authority. No local `origin.json` is trusted or consumed.

`NuGetConsumer` composes `NuGetPreparedHandoff` with the publication rehearsal and
a fixed `NuGetDiscardSink`. Provenance, current main readiness, original tag
object and the complete batch are checked before simulated login and before
every simulated upload. The sink consumes the immutable snapshot stream and
checks its SHA-256. It has no HTTP client, process runner, credential acquisition,
package output or upload operation. Successful JSON lists `accepted` file evidence
and explicitly retains `dryRun: true`, `releaseEligible: false`. Failure returns
exit code 1 without a success receipt; cancellation stops the sequence.

Without `--files`, all twelve files are exercised. An explicit selection must
contain unique, exact filenames from this version's six package/symbol pairs;
the entire transferred batch still has to verify, even for a symbol-only retry.
This selection simulates a retry; it is not a trusted publication ledger and
does not assert that any previous upload succeeded.

The policy is read only from `scripts/sonar/nuget-policy.json` in the trusted
checkout, before read credentials or network access. It now pins the independently
registered identity below. Missing, zero or malformed registration still fails
closed. There is no CLI policy path, workflow identity override,
alternate feed, publish switch or bypass option. A file in a locally edited
checkout is not evidence that the workflow has been approved; the future runner
must bootstrap the reviewed tool and policy from the trusted main candidate.

Both bootstraps reject OIDC and NuGet credentials before restore/build, and the
console repeats that check. The consumer uses the existing read credentials and
removes them from the process environment before running the read-only Git
checks. It does not build or repack the transferred packages. This command does
not alter the current tag publisher or activate the publishing barrier.

Tests exercise the real consumer composition with synthetic service responses
and authenticated artifact ZIPs, including all twelve files, symbol-only
selection, failed gate, tampered bytes, unapproved/superseded provenance and
changes during consumption. CLI/bootstrap tests verify rejection before network
or build work and prevent credential values from appearing in diagnostic output.
Live producer/consumer acceptance remains pending while the producer job is disabled.

## Registered producer identity

GitHub assigned workflow ID `388401675` after PR #269 merged at
`f5a21c922d69846a6b3a0796acdd707adaadd8e0`. The workflow file at that exact commit
was retrieved through the GitHub contents API and compared byte-for-byte by hash
with the checkout before registration:

| Consumer policy field | Reviewed value |
| --- | --- |
| Workflow path | `.github/workflows/nuget-prepare.yml` |
| Workflow ID | `388401675` |
| Workflow SHA-256 | `696637113d981093a5d1ceb6389f3bb5deadc06f487e1592223f37eeeb8ea4e2` |
| Maximum producer age | 120 minutes; existing readiness expiry still applies independently |
| Required job | `prepare` |
| Required successful steps | `Verify tag push`, `Prepare NuGet batch`, `Publish package batch`, `Publish origin` |

This registers the **disabled revision**, not an executable production producer.
GitHub's workflow registry reports `state: active`, but the job's literal
`if: ${{ false }}` still prevents execution. The consumer rejects a skipped job
even when the overall run reports success. No environment, secret, trigger,
permission or publishing route is changed by this policy registration.

Removing the guard changes the workflow hash and must be accompanied by a newly
reviewed policy hash. Do not compute or trust that replacement automatically at
runtime. The current registration cannot be reused as approval for activation.

The registered-policy tests load the actual checked-in policy and workflow bytes
and run the real provenance/handoff/rehearsal composition with synthetic service
responses. They verify all twelve files in the synthetic success case and reject
unsuccessful jobs, each missing/skipped required step, and a changed enablement
guard before artifact download or simulated login. The synthetic success metadata
does not claim that this disabled workflow has run successfully on GitHub.

## Disabled producer workflow: `.github/workflows/nuget-prepare.yml`

The file is now prepared with `if: ${{ false }}` on its only job. No repository
variable or dispatch input can enable it. Removing that guard requires a separate
reviewed change after the prerequisites below. Existing workflows remain unchanged;
no protected environment, token copy or policy registration is created by this file.
An intentionally skipped job is not successful preparation evidence.

The workflow targets `v*` tag pushes only and grants `contents: read` and
`actions: read`. It pins checkout, .NET setup and artifact upload actions, uses
Ubuntu 24.04, disables checkout credential persistence and serializes runs per
tag. Before checkout/build, `Verify tag push` validates repository, ref, stable
version, workflow revision, original event creation flags and current main SHA.
It selects the newest returned readiness run for that SHA without filtering away
failed/running attempts; the existing readiness verifier must authenticate it.
The event-file digest is captured and checked again before calling preparation.

Read credentials are scoped to their individual steps. The batch step uses the
existing preparation command and its structured outputs. `Publish package batch`
uploads only `dry-run.json` and `packages/`, preserving their common parent as the
artifact root; `Publish origin` then uploads only `origin.json`, separately.
Both uploads require command success and existing files, retain artifacts for one
day, and use names bound to SHA/run/attempt. Neither uses an `always()` condition.
No reports, build logs, credentials, NuGet login or publishing action are included.

The intentional constant-false guard is the sole expected actionlint diagnostic;
local validation suppresses only that exact diagnostic, retaining all other checks.
The guard must remain present in this preparation PR.

The remaining integration prerequisites are:

1. Trigger on explicit version-tag pushes only. Do not trigger preparation or
   publishing from successful main analysis. Capture and validate the runner
   context and original push event before running repository builds. Require the
   expected repository, tag ref, workflow revision and candidate SHA. Reject tag
   deletion/retarget events and preserve the original tag object. Validate actual
   lightweight/annotated-tag payload behavior on the runner; do not derive an
   "original" tag identity by reading a potentially retargeted ref later.
2. Use `contents: read` and `actions: read`, with no `id-token: write` or NuGet key.
   Use a separate protected `sonar-nuget-prepare` environment restricted to the
   approved release tags. Keep `sonar-main` main-only. Reviewer/tag-protection
   rules and scoped Sonar verifier token copies require operator configuration.
3. Resolve the candidate to the current main tip and current trusted readiness.
   Prepare all six package/symbol pairs with the existing dry-run builder, then
   check the original tag object and readiness again before writing the origin.
   Use the implemented `--nuget-prepare` command to capture the event, prepare the
   batch and write its origin. The reviewed workflow must invoke it and establish
   authentic runner inputs; the command does not authenticate itself.
4. Upload the exact manifest/package batch and the separate origin artifact. The
   origin must refer to the actual attempt and manifest bytes. Required producer
   step names are `Verify tag push`, `Prepare NuGet batch`, `Publish package batch`
   and `Publish origin`, in job `prepare`; review and pin the complete workflow bytes.
5. The assigned workflow ID and disabled revision are now registered as described
   above. Review and pin the final enabled revision separately. No guessed ID,
   self-declared hash or automatic trust-on-first-use is allowed.

## Disabled handoff rehearsal: `.github/workflows/nuget-rehearse.yml`

This new workflow prepares the intended-runner connection between the two existing
commands. Its only job also has `if: ${{ false }}`. It cannot be enabled through a
variable or dispatch input. It listens only for completed runs of the named
preparation workflow, not successful CI or main analysis. It has `contents: read`
and `actions: read`, no OIDC permission and no NuGet credential or upload step.

Before checkout, `Verify preparation event` requires the expected repository and
workflow context on main, the assigned producer ID/path, a successful tag-push
run, a stable version tag, positive run/attempt IDs and the exact current main SHA.
The consumer workflow revision must match that candidate. It checks out that
immutable SHA with credential persistence disabled and uses the pinned .NET setup
action. It never checks out or executes files from an artifact.

`Download preparation artifacts` uses the installed GitHub CLI to download the
exact origin and batch artifact names from the event's run/attempt into a unique
temporary directory outside the checkout. The batch retains `dry-run.json` and
`packages/` at its root. The bounded origin JSON supplies candidate manifest,
tag-object and readiness pins after syntax/context checks. Those downloaded pins
remain untrusted: they do not grant permission or replace the authenticated origin
that the C# verifier independently fetches and checks against the reviewed policy.

`Rehearse verified package batch` invokes `--nuget-rehearse` for all twelve files,
using the captured run and attempt rather than selecting a newer attempt or an
older successful run. Its read credentials are scoped to that step. The existing
consumer validates live producer identity/revision/jobs, authenticated origin,
current main readiness, original tag and every transferred byte before the
discard sink's simulated login and each simulated upload. A rerun, expired evidence,
changed main, failed verification or command failure fails the job with no fallback.
It does not repack or add a second GitHub API client.

Cleanup runs after success or failure and only removes the generated directory
after checking its parent, name and resolved path. No downloaded batch or rehearsal
receipt is uploaded as release authority. The normal command output is diagnostic
and remains `dryRun: true`, `releaseEligible: false`.

Before enabling this job, create a separately protected, **main-only**
`sonar-nuget-rehearse` environment with the existing two scoped Sonar read-token
copies. The producer's proposed `sonar-nuget-prepare` environment remains tag-only;
`sonar-main` remains unchanged. Neither new environment exists yet. Review the
producer enablement/hash and consumer guard removal together, then demonstrate
the actual event, artifact layout, protections and negative cases on the intended
runner. Offline shell fixtures and the existing C# consumer tests are preparation
evidence only. No actual artifact handoff has run through this disabled workflow.

To repeat the offline shell checks on Linux with PowerShell, Bash and jq installed:

```sh
pwsh -NoProfile -File tests/AndreGoepel.AppFoundation.Sonar.Tests/Workflows/Verify-NuGetRehearsalWorkflow.ps1
```

The script extracts the workflow's actual shell blocks and substitutes local
GitHub/download and verifier commands. It checks event rejection, exact artifact
names and CLI arguments, malformed origins, download/verifier failures and cleanup
boundaries without credentials or network access. It writes a diagnostic result
under `artifacts/sonar/workflow-tests` and removes its temporary fixtures. This
standalone check leaves existing CI/E2E workflows unchanged.

## Required repository-local consumer in `ci.yml`

This section requires a separately reviewed change to the existing publisher.

1. Keep the NuGet credential-bearing job repository-local. Replace the current
   ungated tag publication route when activating; leaving it available would
   bypass the new barrier. Bind the completed preparation event's run/attempt to
   the approved producer and current main before consuming its artifacts.
2. Bootstrap only reviewed tools and `nuget-policy.json` from the trusted main
   candidate, with immutable action revisions. The disabled handoff workflow now
   prepares the `--nuget-rehearse` invocation; demonstrate its actual authenticated
   preparation handoff first. A later publishing adapter needs
   separate review; a rehearsal receipt never grants publication authorization.
3. Verify original tag/event identity, producer provenance, main readiness and all
   transferred bytes before NuGet login. The credential-bearing boundary needs
   scoped read credentials without widening `sonar-main` access. Repository build
   commands must not inherit publication credentials or OIDC request variables.
4. Implement and test the real credential/upload adapter against a non-publishing
   sink first. Preserve the verified bytes through the actual upload; the current
   composite action cannot be reused unchanged because it checks out and repacks.
   Revalidate before every later package/symbol upload and retry. Define secure
   recording and reconciliation of partial or uncertain remote outcomes.
5. Update the reviewed CI hash in `main-policy.json` with the approved workflow
   change. Run CI/E2E and central Sonar on the new main before relying on readiness.
6. Inventory the six packages' NuGet trusted-publisher policies and all alternative
   API keys/manual/symbol routes. Restrict bypass credentials through the account
   owner before activation. Neither this verifier nor GitHub Actions controls an
   independently authorized external NuGet key.

## Acceptance boundary

Before activation, demonstrate the actual producer/consumer exchange on the
intended runner with a non-publishing sink, including negative provenance,
ordering, expiry, tampering and retry cases. Keep synthetic event fixtures clearly
identified; they cannot replace evidence that the real trigger and environment
protections bind the same inputs. A real release remains separately authorized.

The remaining work is producer/rehearsal enablement and actual tag-payload proof,
review of the enabled producer's policy hash, credential/upload adapter, protected environments and account
controls, followed by the runner acceptance and activation PR. This document is
not a claim that #244 is complete.

References: [GitHub workflow-run API](https://docs.github.com/en/rest/actions/workflow-runs),
[GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts),
[GitHub push payload](https://docs.github.com/en/webhooks/webhook-events-and-payloads#push),
[GitHub context](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#github-context),
[job conditions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions),
[step outputs](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#setting-an-output-parameter),
[artifact upload paths](https://github.com/actions/upload-artifact#upload-using-multiple-paths-and-exclusions),
[workflow-run triggers](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run),
[GitHub CLI artifact downloads](https://cli.github.com/manual/gh_run_download).
