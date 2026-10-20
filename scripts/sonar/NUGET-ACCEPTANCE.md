# NuGet release controls and remaining acceptance (#244)

Status: release-tag protection applied after the explicitly approved enforcement
drill on 2026-10-20. The production publishing path remains disabled. A separate
`nuget-acceptance.yml` workflow prepares real GitHub job/artifact checks with
simulated login and a local discard sink. Existing workflow files, branch rules,
NuGet policies and credentials are unchanged. Merging this change does not
authorize a real release.

## Evidence already available

| Boundary | Evidence | Limit |
| --- | --- | --- |
| Exact-main CI, E2E and Sonar readiness | #243; versioned policy and baseline; current main readiness workflow | A receipt expires and is valid only for its exact source and run. |
| Six packages and six symbol files | [Lightweight preparation](https://github.com/andregoepel/app-foundation/actions/runs/39192189177/attempts/1), [consumer](https://github.com/andregoepel/app-foundation/actions/runs/39192627767/attempts/1), [annotated preparation](https://github.com/andregoepel/app-foundation/actions/runs/39193143150/attempts/1), [consumer](https://github.com/andregoepel/app-foundation/actions/runs/39193529177/attempts/1) | Actual hosted-runner handoff into a discard sink; historical receipts have expired. |
| Failed/stale readiness, wrong source/provenance, changed tag/package bytes, partial outcomes | C# integration tests and the 209 shell cases | Most negative cases use synthetic services; they do not prove the future publisher's GitHub scheduling or account restrictions. |
| Disabled official-tool publication flow | `nuget-publish.caller.yml` and `nuget-publish.steps.yml`; [main template checks after #280](https://github.com/andregoepel/app-foundation/actions/runs/39392091850) | Real runner execution of synthetic shell tests; no official login or package upload. |
| Immutable release tags | [Active ruleset 25464148](https://github.com/andregoepel/app-foundation/rules/25464148); four server-side rejection cases below | Protects tag objects while enforced; it is not proof of publication history. |

For main `efe080ec53a7230cf9b328df143b72b943cae7d2`,
[CI](https://github.com/andregoepel/app-foundation/actions/runs/39392092711) passed
944 tests, [E2E](https://github.com/andregoepel/app-foundation/actions/runs/39392092371)
passed 14, and [Sonar](https://github.com/andregoepel/app-foundation/actions/runs/39392275091)
passed all 958 with gate OK and zero findings/hotspots. The baseline was unchanged.
This historical evidence does not authorize a later candidate or release.

After #281 merged at `3ec44019b94bc8f2c76d6160aa74ceae201f634e`,
[CI](https://github.com/andregoepel/app-foundation/actions/runs/39397059536) passed
944 tests with no build warnings/errors and no known vulnerable packages across
15 projects; its publishing job was skipped.
[E2E](https://github.com/andregoepel/app-foundation/actions/runs/39397059274)
passed 14 tests, and
[template checks](https://github.com/andregoepel/app-foundation/actions/runs/39397058722)
passed all 209 synthetic cases with only the loopback interface available.
[Sonar readiness](https://github.com/andregoepel/app-foundation/actions/runs/39397255130)
passed all 958 tests, with gate OK, zero findings/hotspots, 88.8% overall coverage
and 96.4% new-code coverage. Analysis `eeac6a6a-2ea0-4e76-9f82-95abfff6778e`
used the unchanged baseline `899f364a-65bb-4f5e-9840-103b3ecc7825`.
The readiness receipt matched the exact main SHA, workflow revision, CI/E2E
attempts and analysis bytes (SHA-256
`75b74e30caca05aac5fba8e46e4d43318a88ed8254f0db5138fc3ea4b8fc1297`).
These are historical validation results; the receipt expires after one hour.

The new path uses `NuGet/login` and `dotnet nuget push`. No additional upload
client is needed. Passing shell tests does not close the operator and live-flow
criteria below. Keep #244 open until every remaining item has evidence.

## Read-only release-control audit

Observed on 2026-10-20 after #280 merged at
`efe080ec53a7230cf9b328df143b72b943cae7d2`:

- GitHub returned two active repository rulesets, both targeting the default
  **branch**. No tag ruleset was returned. Neither existing ruleset protects
  release tags against update or deletion.
- The only configured environment was `sonar-main`. The future `nuget-publish`
  environment and its release approvals/ref restrictions are not configured.
- The repository secret inventory contained `NUGET_USER`. This is not an audit
  of NuGet API keys, organization-level credentials, other package owners or
  trusted-publisher policies.
- The existing `ci.yml` still contains the original tag publisher. Its job is
  skipped on main/PR checks, but a real `v*` tag can still invoke that route.
- NuGet account-level policy and API-key controls have not been verified.

These are point-in-time observations, not an attestation of future configuration.
Repeat the audit before activation and after any control change.

## Applied immutable release tags

[`templates/nuget-release-tags.ruleset.json`](templates/nuget-release-tags.ruleset.json)
is the reviewed GitHub repository-ruleset payload applied as
[ruleset 25464148](https://github.com/andregoepel/app-foundation/rules/25464148):

- Target only tags matching `refs/tags/v*`, consistent with the current release
  namespace; no exclusions.
- Block updates and deletions with no bypass actors.
- Allow initial tag creation under existing repository permissions. It does not
  authorize a release or validate its SHA, version, readiness or package bytes.
- Leave branch rules and the `sonar-poc/...` acceptance namespaces unchanged.

The payload contains `"enforcement": "active"`. It is a file outside
`.github/workflows`; nothing reads or applies it automatically. The operator
applied it to **app-foundation only** after the explicitly approved drill passed.
A fresh API read verified active enforcement, the exact `v*` scope, no exclusions,
an explicitly empty `bypass_actors` list, and both update/deletion restrictions.
GitHub normalizes the tag update rule by omitting its
`update_allows_fetch_and_merge: false` parameter from the response.

This closes the ordinary delete/recreate or retarget route while the ruleset is
enforced. The existing first-attempt checks and original tag-object verification
remain necessary. It is not a durable publication ledger, an atomic upload lock,
or protection against someone who can change the ruleset itself. Do not treat
tag immutability as proof that a package version has never reached NuGet.

### Completed enforcement drill

On 2026-10-20, temporary ruleset `25463925` applied the same controls only to
`refs/tags/sonar-poc/tag-protection/808e552a3f86480ba83d5c611e3e4d7a/*`.
The operator checked all seven workflow triggers before creating the test tags:
this prefix matched neither the `v*` publisher nor the
`sonar-poc/nuget/*/v*` preparation trigger. Initial creation of `lightweight` and
`annotated` under that prefix succeeded, both pointing to main `3ec44019...`.

| Tag | Mutation attempted | Server response | Verification after rejection |
| --- | --- | --- | --- |
| Lightweight | Force update to previous main `efe080ec53a7230cf9b328df143b72b943cae7d2` | `GH013`: Cannot update this protected ref. | Original commit object retained. |
| Lightweight | Delete ref | `GH013`: Cannot delete this tag | Original commit object retained. |
| Annotated | Replace the annotation object while retaining the same commit | `GH013`: Cannot update this protected ref. | Original annotation object retained. |
| Annotated | Delete ref | `GH013`: Cannot delete this tag | Original annotation object retained. |

The original annotated object was `c1b12d454b0673a830284e286aba4072f1e549bc`;
the rejected replacement was `3f4f0750c114f51049d212fae655b3aeed54a428`.
Each update used an exact lease on the original object, each command returned
exit code 1 with a server rules violation, and each follow-up remote read confirmed
the original object. These were actual push attempts, not Git dry runs.

Only after all four cases passed was release ruleset `25464148` created from the
payload merged in #281. Cleanup completed at 09:06:26 UTC: the temporary ruleset
was removed before deleting both temporary remote and local refs. Final reads
confirmed their absence and the permanent rule's presence. The complete before/
after API responses for branch rulesets `17153070` and `18198948` were unchanged.
The workflow-run inventory checked at 09:07:59 UTC showed no runs created since
09:02:00 UTC, covering the tag drill. No release tag, package, login or upload was
created by the drill.

Raw local evidence is retained under the ignored
`artifacts/sonar/acceptance-244/tag-protection/` directory: payloads and API
responses, exact object IDs, all four rejection logs, cleanup results and main
validation receipts. This document records the outcome; future activation still
requires a fresh operator audit. Repeat any enforcement drill only under an
explicitly approved isolated namespace after rechecking workflow triggers.

The final operator audit must inspect `bypass_actors`, including inherited
rulesets. GitHub returns that property only to callers with write access to the
ruleset. A missing field from a read-only response does **not** mean an empty
bypass list. Keep this audit outside the publishing job; do not add an
administration token to the runner to make the field visible.

## Remaining acceptance sequence

| Step | Required completion evidence | Current state |
| --- | --- | --- |
| 1. Release control review | Applied tag rules and safe enforcement drill; approved environment/reviewer policy; complete six-package account audit | Tag rules applied and four-case drill passed. Operator selected `andregoepel` as required reviewer with self-approval allowed; configuration and account audit remain pending. |
| 2. Publication record and recovery boundary | Independently authenticated intent/outcomes and cross-run replay decisions; missing or uncertain records block any retry | Intent/outcome retrieval and strict single-run replay rejection are tested offline. Actual service acceptance remains open. Independent external archive work is explicitly deferred until after #247 acceptance. |
| 3. Final non-publishing runner drill | Actual job/event ordering and artifact storage, full twelve-file success, all required rejection cases before login/upload, cancellation and lost-record cases | Separate acceptance workflow prepared below; live results must be recorded after merge. Earlier preparation/discard drills passed. |
| 4. Separately approved activation | Replace the old tag publisher, register production producer identity/hash, configure the protected environment and NuGet policy, update the reviewed CI hash | Prohibited by the current task scope. |
| 5. Acceptance review | Fresh exact-main CI/E2E/Sonar evidence, complete barrier matrix, no alternate publication route, operator sign-off | #244 stays open. No real release is needed or authorized. |

Do not add an automatic or symbol-only retry path while step 2 is incomplete.
After an interrupted upload, stop and retain the intended bytes and available
records. A missing outcome, a CLI error or a `409` cannot authorize a retry.
Even a successful CLI exit requires independent remote reconciliation before
selecting remaining files. Never repack or replace a published version.

For step 3, the negative matrix must include failed/missing Sonar, wrong SHA,
unmerged source, stale readiness, missing E2E, unknown/superseded workflow or
attempt, changed tag/package bytes, missing/failed intent storage, and changed
evidence after storage. Verify that main success alone cannot start a release.
Separate synthetic rejection evidence from actual GitHub event, environment and
artifact-service evidence. No public dummy package or real release tag is needed.

## Prepared runner acceptance

`.github/workflows/nuget-acceptance.yml` is generated from the disabled production
caller and steps by `templates/Build-NuGetAcceptanceWorkflow.ps1`. The generator
requires every expected substitution and rejects publication commands, NuGet
credential bindings and OIDC permission. Existing production templates remain
disabled and unchanged. The existing template-check job also tests generation
parity and the negative probe; its workflow file is unchanged.

The acceptance workflow receives only completion events from **NuGet preparation
(runner test)**, checks its registered ID/path and successful first attempt, and
accepts only `sonar-poc/nuget/<32 lowercase hex characters>/vX.Y.Z`. It must run at
the current main tip using that revision's workflow. Main/PR success alone does
not satisfy the trigger. There is no manual dispatch or caller-supplied command.

It reuses the production sequence: authenticate the full handoff, store the
twelve-file intent, retrieve and authenticate the stored artifact, reverify
readiness and packages, then store linked outcomes. The two side effects differ:

- The login step only checks that NuGet/OIDC credentials are absent and reports a
  simulation. It never invokes `NuGet/login`.
- Each verified file is read into `/dev/null` by a process with an empty
  environment except `PATH`. No upload executable, endpoint or NuGet key is used.

Step names intentionally match the production job-state verifier. In this
workflow, `cli-success` describes the discard command only. Intent/outcome kinds
are `acceptance-intent` / `acceptance-outcomes` and include `acceptanceOnly: true`.
`--nuget-verify-acceptance-intent` and `--nuget-inspect-acceptance` pin the separate
workflow and test-tag namespace. Production modes reject this context. The
inspection reports `acceptanceOnly: true`, `releaseEligible: false`,
`authorizesRetry: false` and `remoteAcceptanceVerified: false`.

Before the positive stored-intent check, the runner must reject five probes:
production workflow context, attempt 2, changed intent content hash, changed
artifact archive hash, and the production verifier applied to the test context.
Each must fail without returning a receipt. A subsequent positive check and the
complete discard sequence must succeed; rejected probes alone are not sufficient.
Failures of gate/SHA/E2E/freshness, storage, cancellation and missing-record
handling also retain their existing synthetic tests. Do not relabel those tests
as live service evidence.

After review and merge:

1. Configure temporary `sonar-nuget-prepare`, `sonar-nuget-rehearse` and
   `sonar-nuget-acceptance` environments with required reviewer `andregoepel`,
   self-review allowed, administrator bypass disabled and only the intended refs.
   Preparation uses the acceptance tag pattern; both consumers use branch `main`.
   The unchanged rehearsal workflow also receives this event, so protect its
   environment as part of the drill. Copy only the required Sonar token secrets,
   never NuGet keys. Verify the settings readback.
2. Obtain fresh exact-main CI, E2E and successful Sonar readiness. Create one
   explicitly approved non-release test tag at that SHA. No `v*` tag is needed.
3. Approve the test jobs. Retain run/attempt IDs, original tag object, origin,
   batch, intent/outcome artifact IDs and both archive/content hashes. Confirm
   the five rejections, twelve discarded files and successful artifact storage.
4. Before readiness expires, inspect the completed attempt with
   `--nuget-inspect-acceptance`, the same handoff arguments and the inspection
   pins shown in [release eligibility](RELEASE-ELIGIBILITY.md). Confirm all twelve
   observations and the acceptance-only flags.
5. Record actual versus synthetic coverage separately, then remove the temporary
   environments and test tag. Preserve the evidence; do not delete workflow runs
   to bypass replay protection.

The strict single-run-per-main-SHA rule also applies to acceptance. A prior,
skipped, failed or repeated acceptance run blocks another attempt for that SHA;
plan the test prerequisites before creating its tag. This change prepares the
drill, but does not claim it passed or close #244.

## NuGet account and environment review

Inspect each package through every owner account or organization capable of
publishing it. Record policy names/IDs, scopes, workflow/environment restrictions
and whether alternative keys or routes remain; never copy API-key values.

| Package ID | Owners and trusted-publisher policies | Alternative API keys and manual/symbol routes |
| --- | --- | --- |
| `AndreGoepel.AppFoundation.Core` | Not inspected | Not inspected |
| `AndreGoepel.AppFoundation.ServiceDefaults` | Not inspected | Not inspected |
| `AndreGoepel.AppFoundation.MailService` | Not inspected | Not inspected |
| `AndreGoepel.AppFoundation` | Not inspected | Not inspected |
| `AndreGoepel.AppFoundation.Hosting` | Not inspected | Not inspected |
| `AndreGoepel.AppFoundation.Aspire` | Not inspected | Not inspected |

The proposed production policy is restricted to repository owner `andregoepel`,
repository `app-foundation`, workflow filename `nuget-publish.yml`, and environment
`nuget-publish`. Its scopes must cover only the intended package IDs and publication
operations. Review every applicable policy, including broader wildcard policies,
and all long-lived keys that could bypass readiness. Repository secret names or
the absence of a checked-in key cannot establish this.

The protected production environment must accept only branch `main`: the future
caller runs on `workflow_run`, not on the producer's tag ref. André selected
`andregoepel` as required reviewer and explicitly allowed self-review because he
is the sole maintainer. Manual approval remains required. Disable administrator
bypass and record the configuration readback. This decision is prepared, not an
assertion that `nuget-publish` exists or is configured. Scope the existing Sonar
verification credentials to the necessary steps, and keep NuGet credentials in
the official login/push boundary.

Do not broaden a NuGet policy or revoke a shared key without checking which other
repositories/packages depend on it. If a shared credential permits a bypass,
record the blocker and arrange a scoped migration with the owner. Other repositories
remain outside this PoC.

## Deferred until after PoC acceptance

On 2026-10-20 the operator moved external backup/archive provider evaluation,
provisioning and integration to the end of epic #238, **after completion and
explicit acceptance under #247**, followed by his detailed review. Preserve the
proposal to evaluate an EU Backblaze B2 bucket for encrypted off-host backups and
release evidence; provider, cost, retention, access and restore details remain
undecided. Do not provision a bucket, credentials or exporter in this block.

This work is not a prerequisite for non-publishing PoC acceptance. Available
local/GitHub evidence is still retained, missing records still block retry, and
independent durable retention must not be reported as verified. Publication
activation remains a separate decision. #246 still covers database-aware backup,
isolated restore and monitoring within the revised scope.

References:
[GitHub tag update/deletion rules](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets),
[ruleset API and bypass visibility](https://docs.github.com/en/rest/repos/rules#get-a-repository-ruleset),
[NuGet trusted-publisher owner, scope and environment controls](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
