# NuGet preparation provenance and production integration (#244)

Status: verifier and recording-sink tests prepared. Production wiring is pending;
the NuGet barrier remains inactive. No existing Actions, policy or credential is
changed by these components.

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

## Required producer: `.github/workflows/nuget-prepare.yml`

This file does not exist yet. The following is the concrete integration contract
for its later reviewed implementation, not a workflow that can be enabled as-is.

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
   The event-capture/origin writer must be implemented and tested; the present
   verifier does not create an origin or make producer assertions trustworthy by
   itself.
4. Upload the exact manifest/package batch and the separate origin artifact. The
   origin must refer to the actual attempt and manifest bytes. Required producer
   step names should be `Verify tag push`, `Prepare NuGet batch`, and
   `Publish origin`, in job `prepare`; review and pin the complete workflow bytes.
5. Obtain the assigned workflow ID from GitHub and register it with the reviewed
   workflow hash and bounded maximum age in trusted consumer configuration. No
   guessed ID, self-declared hash or automatic trust-on-first-use is allowed.

## Required repository-local consumer in `ci.yml`

This section requires a separately reviewed change to the existing publisher.

1. Keep the NuGet credential-bearing job repository-local. Replace the current
   ungated tag publication route when activating; leaving it available would
   bypass the new barrier. Bind the completed preparation event's run/attempt to
   the approved producer and current main before consuming its artifacts.
2. Bootstrap only reviewed tools from the trusted main candidate, with immutable
   action revisions. Add an explicit consumer CLI that composes
   `NuGetPreparedHandoff` and the publication sequence. The current console
   entry point still exposes only the preparation dry run; it must not silently
   turn a rehearsal receipt into publication authorization.
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

The remaining work is the producer/event writer, trusted policy registration,
consumer CLI and credential/upload adapter, protected environments and account
controls, followed by the runner acceptance and activation PR. This document is
not a claim that #244 is complete.

References: [GitHub workflow-run API](https://docs.github.com/en/rest/actions/workflow-runs),
[GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts),
[GitHub context](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#github-context).
