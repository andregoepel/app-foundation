# NuGet preparation provenance and production integration (#244)

Status: verifier, tag-event capture, origin preparation and non-publishing consumer
commands implemented, with acceptance-only producer and handoff workflows. The
runner-test producer revision is registered in the consumer policy. Production
activation is pending; the NuGet barrier remains inactive. Other Actions and existing credentials
remain unchanged. The runner test still needs PR review, merge and protected access.

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
Live producer/consumer acceptance remains pending until this revision is merged,
protected environments exist and the actual drill passes.

## Non-release acceptance tags

The non-publishing commands accept this fixed test namespace:

```text
sonar-poc/nuget/<32 lowercase hexadecimal characters>/vX.Y.Z
```

For example, `sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0`
uses package version `2.2.0`. Each drill uses a new identifier. The full tag is
preserved through event capture, run/artifact provenance, origin and live ref
checks. Matching only the version is insufficient. Other prefixes, encoded
separators, extra segments, uppercase identifiers and mismatched versions fail.
The C# non-publishing commands still accept `vX.Y.Z`; these runner workflows do not.

The unchanged `ci.yml` tag trigger is `v*`, and its publisher requires
`refs/tags/v...`. The test namespace matches neither. Before an actual drill,
recheck the then-current triggers and publishing paths. No command or workflow
in this test creates, moves or deletes a tag.

The 34 acceptance-tag cases use synthetic service responses with real preparation
and provenance/handoff/consumer orchestration. They cover original object fixtures,
cross-drill or release-tag substitution, failed gates, stale evidence, retargeting
and modified packages. They do not establish GitHub's actual tag-event payload.

## Registered producer identity

GitHub assigned producer ID `388401675` and consumer ID `388924588`. Their file
paths remain unchanged. The producer's display name is now `NuGet preparation
(runner test)`; the consumer subscribes to that exact name.

| Consumer policy field | Reviewed runner-test value |
| --- | --- |
| Workflow path | `.github/workflows/nuget-prepare.yml` |
| Workflow ID | `388401675` |
| Workflow SHA-256 | `966440b8b3464f309fae99e8f796262795683693b737ec03d9a9e4f660e2cf25` |
| Maximum producer age | 120 minutes; readiness expiry also applies |
| Required job | `prepare` |
| Required successful steps | `Verify tag push`, `Prepare NuGet batch`, `Publish package batch`, `Publish origin` |

The hash covers the exact LF bytes committed in the runner-test PR. It is not
computed from downloaded artifacts or automatically trusted at runtime. This
replaces the previously registered disabled revision. Skipped jobs, missing steps,
other attempts and unreviewed revisions still fail, even if a run reports success.
The registered-policy tests use the actual file and policy with synthetic success
metadata and a test tag; changing the trigger to `v*` without a new hash is rejected
before artifact download or simulated login.

## Runner-test producer: `.github/workflows/nuget-prepare.yml`

Only `sonar-poc/nuget/*/v*` pushes can start preparation. The job guard requires
this repository, a push event and the fixed test prefix. The first shell step
validates the exact tag grammar, original creation flags, repository, ref type,
workflow revision and current main SHA before checkout or build. It extracts the
package version separately and selects the newest returned readiness run without
filtering away failed/running runs; the C# verifier must authenticate readiness.

The original event-file digest is checked again before preparation. The step
summary records the validated full tag, candidate commit, original push `after`
and readiness run. This diagnostic evidence helps establish actual annotated-tag
payload behavior; it confers no publication authority. A payload/ref-object
mismatch must stop the drill, never be repaired by a later ref lookup.

The workflow uses Ubuntu 24.04, pinned actions, credential-free checkout and
per-tag concurrency. Permissions remain `contents: read` and `actions: read`.
It uses the existing `--nuget-prepare` command after browser/test prerequisites.
Read credentials are scoped to the command step. No OIDC or NuGet credential is
available, and no publication adapter is called.

After successful preparation, `Publish package batch` uploads only `dry-run.json`
and `packages/` at their common root; `Publish origin` uploads only `origin.json`
in its separate run/attempt-bound artifact. These steps require command success
and existing files, retain artifacts for one day and include no private logs.
An artifact upload is not a NuGet upload. The origin and batch remain
`dryRun: true`, `releaseEligible: false`.

## Runner-test consumer: `.github/workflows/nuget-rehearse.yml`

`NuGet handoff rehearsal (runner test)` listens for completed runs of the named
producer. Its job guard requires this repository, producer ID `388401675`, a
successful push run and a test-tag prefix. Main CI/Sonar success cannot trigger it.
The first shell step independently checks the full event: producer repository,
path/ID, successful status, exact test-tag grammar, positive run/attempt IDs and
candidate SHA equal to current main and this workflow revision.

It checks out that exact SHA with credential persistence disabled. GitHub CLI
reads the exact origin and batch artifact names from the captured run/attempt into
a unique temporary directory outside the checkout. The bounded origin supplies
candidate pins only; the C# consumer independently authenticates the origin
archive, producer revision/jobs, current readiness, tag object and every package
byte. Artifact contents are never executed, and no second build or pack occurs.

The consumer passes the extracted version and full tag to `--nuget-rehearse`,
exercising all twelve files through `NuGetDiscardSink`. Verification repeats
before simulated login and every file. Failures and cancellation propagate; there
is no fallback to an earlier green run or unauthenticated pins. Cleanup accepts
only the generated directory under `RUNNER_TEMP`, rejects symlinks/traversal and
runs after success or failure. Rehearsal output is diagnostic and is not uploaded
as release authority.

## Operator sequence after review and merge

This PR changes only the two former disabled workflows, their producer policy
hash, tests and documentation. Other Actions, `main-policy.json`, central Sonar
policy/baseline and all other repositories remain unchanged. The checked-in job
conditions permit the runner test after merge; there is no variable/dispatch
switch. This PR creates no environment, token, remote tag or NuGet release.

1. Review and merge the PR. Verify CI, E2E and trusted Sonar readiness for that
   exact new main SHA. Confirm the registered workflow IDs/paths and producer file
   hash against GitHub. An earlier main receipt cannot authorize the new revision.
2. Configure protected environments before creating tags:

   | Environment | Allowed ref type/pattern | Credentials |
   | --- | --- | --- |
   | `sonar-nuget-prepare` | Tags only: `sonar-poc/nuget/*/v*` | Copies of the two existing scoped Sonar tokens |
   | `sonar-nuget-rehearse` | Branch only: `main` | Copies of the two existing scoped Sonar tokens |
   | `sonar-main` | Existing branch-only `main` restriction | Unchanged |

   Use the existing `APPFOUNDATION_SONAR_TOKEN` and
   `APPFOUNDATION_SONAR_VERIFY_TOKEN` names. Review environment access/reviewer and
   tag-protection rules with the operator; do not broaden Sonar permissions or add
   OIDC/NuGet publishing credentials. The new environments do not exist as part
   of this PR. Fresh readiness is required after any approval delay causes expiry.
3. Recheck that the unchanged publisher cannot match the test namespace. For a
   lightweight drill, create one new test tag on the exact current main commit;
   record the original ref object and push event. Wait for preparation and consumer
   completion, collecting run IDs, attempts, artifact digests and diagnostic output.
4. Repeat sequentially with a different test identifier and an annotated tag on
   the same verified main SHA, refreshing readiness if necessary. Record both the
   tag object and peeled commit. An annotated payload containing only the peeled
   commit is expected to fail the existing immutable-object check. Investigate that
   evidence in a separate reviewed fix; never relax the check during the drill.
5. Demonstrate rejection for invalid provenance, failed/stale readiness, changed
   tag/package bytes and retry/cancellation cases. Keep synthetic fixtures distinct
   from actual runner evidence. Do not modify central policy, baseline, CI/E2E or
   the existing publisher to manufacture a successful result.
6. Remove only the drill tags and temporary token copies/environments after evidence
   is collected; preserve sanitized diagnostics. Tag deletion cannot produce a
   valid preparation origin. Revoke only credentials issued specifically for this
   drill; removing copies must not revoke the existing Sonar accounts' tokens.
   Verify cleanup.

Successful acceptance proves non-publishing runner handoff. It does not activate
NuGet protection or prove an actual credential/upload adapter. A future publisher
must accept only release tags, never treat `IsPreparationTag` or a rehearsal
receipt as publication authorization.

## Repeatable offline verification

On Linux with PowerShell, Bash and jq:

```sh
pwsh -NoProfile -File tests/AndreGoepel.AppFoundation.Sonar.Tests/Workflows/Verify-NuGetRunnerWorkflows.ps1
```

The script executes the actual shell blocks from both workflows with local GitHub
and command substitutes. It checks producer event rejection/digest binding,
version/full-tag propagation, consumer event/origin rejection, artifact names,
failed/cancelled commands and cleanup boundaries. No network or credentials are
used. Result JSON explicitly identifies synthetic evidence and no live runner
proof. Both YAML files also pass actionlint and ShellCheck without suppressions.
These checks do not execute GitHub trigger, job-condition or environment policy
semantics; those remain part of the actual runner acceptance.

## Remaining production integration (#244)

1. Implement and test the credential/upload adapter against a non-publishing sink.
   Preserve independent verification immediately before credentials and each file,
   immutable bytes, and trusted recording/reconciliation of partial or uncertain
   outcomes. There is currently no production publishing mode.
2. Audit all six packages' trusted-publisher policies and alternative API keys,
   including manual and symbol-only routes. Repository secret inventory alone
   cannot prove account-level controls; restrict bypasses with the account owner.
3. In a separately approved activation PR, replace the existing ungated local
   publisher. It must not remain an alternate route. The current shared composite
   checks out and repacks, so it cannot publish this verified handoff unchanged.
4. Update the reviewed CI hash in `main-policy.json` with that eventual CI change;
   revalidate main CI/E2E/Sonar. Keep release credentials in the repository-local
   job required by NuGet trusted publishing, with reviewed actions and policies.
5. Demonstrate all negative/positive runner acceptance criteria before resolving
   #244. A real release remains separately authorized; test success alone cannot
   activate it or authorize onboarding another repository.

References: [GitHub push payload](https://docs.github.com/en/webhooks/webhook-events-and-payloads#push),
[GitHub context](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#github-context),
[workflow-run triggers](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run),
[GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts),
[GitHub CLI downloads](https://cli.github.com/manual/gh_run_download).
