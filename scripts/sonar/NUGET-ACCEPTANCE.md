# NuGet release controls and remaining acceptance (#244)

Status: preparation only. The new publishing path remains disabled. This document
and the proposed ruleset do not change GitHub settings, NuGet policies, Actions,
credentials or tags. Merging them does not authorize activation or a real release.

## Evidence already available

| Boundary | Evidence | Limit |
| --- | --- | --- |
| Exact-main CI, E2E and Sonar readiness | #243; versioned policy and baseline; current main readiness workflow | A receipt expires and is valid only for its exact source and run. |
| Six packages and six symbol files | [Lightweight preparation](https://github.com/andregoepel/app-foundation/actions/runs/39192189177/attempts/1), [consumer](https://github.com/andregoepel/app-foundation/actions/runs/39192627767/attempts/1), [annotated preparation](https://github.com/andregoepel/app-foundation/actions/runs/39193143150/attempts/1), [consumer](https://github.com/andregoepel/app-foundation/actions/runs/39193529177/attempts/1) | Actual hosted-runner handoff into a discard sink; historical receipts have expired. |
| Failed/stale readiness, wrong source/provenance, changed tag/package bytes, partial outcomes | C# integration tests and the 209 shell cases | Most negative cases use synthetic services; they do not prove the future publisher's GitHub scheduling or account restrictions. |
| Disabled official-tool publication flow | `nuget-publish.caller.yml` and `nuget-publish.steps.yml`; [main template checks after #280](https://github.com/andregoepel/app-foundation/actions/runs/39392091850) | Real runner execution of synthetic shell tests; no official login or package upload. |

For main `efe080ec53a7230cf9b328df143b72b943cae7d2`,
[CI](https://github.com/andregoepel/app-foundation/actions/runs/39392092711) passed
944 tests, [E2E](https://github.com/andregoepel/app-foundation/actions/runs/39392092371)
passed 14, and [Sonar](https://github.com/andregoepel/app-foundation/actions/runs/39392275091)
passed all 958 with gate OK and zero findings/hotspots. The baseline was unchanged.
This historical evidence does not authorize a later candidate or release.

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

## Proposed immutable release tags

[`templates/nuget-release-tags.ruleset.json`](templates/nuget-release-tags.ruleset.json)
is a reviewable GitHub repository-ruleset payload:

- Target only tags matching `refs/tags/v*`, consistent with the current release
  namespace; no exclusions.
- Block updates and deletions with no bypass actors.
- Allow initial tag creation under existing repository permissions. It does not
  authorize a release or validate its SHA, version, readiness or package bytes.
- Leave branch rules and the `sonar-poc/...` acceptance namespaces unchanged.

The payload contains `"enforcement": "active"` for eventual application. It is a
file outside `.github/workflows`; nothing reads or applies it automatically.
An operator must review and separately apply it to **app-foundation only**, record
the returned ruleset ID, and verify the effective configuration afterward.

This closes the ordinary delete/recreate or retarget route while the ruleset is
enforced. The existing first-attempt checks and original tag-object verification
remain necessary. It is not a durable publication ledger, an atomic upload lock,
or protection against someone who can change the ruleset itself. Do not treat
tag immutability as proof that a package version has never reached NuGet.

For the later, separately authorized enforcement drill, use a temporary ruleset
with the same controls and a unique `sonar-poc/tag-protection/...` prefix. First
recheck all current workflow triggers to ensure that prefix cannot publish.
Demonstrate initial creation, then rejection of update and deletion for both
lightweight and annotated tags. Remove the temporary ruleset before removing its
test tags. Never create a `v*` tag for this test. A Git push dry run alone is not
proof that server-side enforcement rejects the mutation.

The final operator audit must inspect `bypass_actors`, including inherited
rulesets. GitHub returns that property only to callers with write access to the
ruleset. A missing field from a read-only response does **not** mean an empty
bypass list. Keep this audit outside the publishing job; do not add an
administration token to the runner to make the field visible.

## Remaining acceptance sequence

| Step | Required completion evidence | Current state |
| --- | --- | --- |
| 1. Release control review | Applied tag rules and safe enforcement drill; approved environment/reviewer policy; complete six-package account audit | Ruleset prepared; settings not applied; account audit pending. |
| 2. Publication record and recovery boundary | Independently authenticated intent/outcomes, retention/retrieval and cross-run replay decisions; missing or uncertain records block any retry | Intent/outcome creation is prepared; independent recovery verification remains open. |
| 3. Final non-publishing runner drill | Actual job/event ordering and artifact storage, full twelve-file success, all required rejection cases before login/upload, cancellation and lost-record cases | Earlier preparation/discard drills passed; final caller acceptance remains open. |
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

The protected environment must accept only branch `main`: the future caller runs
on `workflow_run`, not on the producer's tag ref. Before creating it, name the
reviewer(s) and explicitly decide self-review behavior; the policy must be usable
by the actual operator. Disable administrator bypass and record the reviewed
configuration. Scope the existing Sonar verification credentials to the necessary
steps, and keep NuGet credentials in the official login/push boundary.

Do not broaden a NuGet policy or revoke a shared key without checking which other
repositories/packages depend on it. If a shared credential permits a bypass,
record the blocker and arrange a scoped migration with the owner. Other repositories
remain outside this PoC.

References:
[GitHub tag update/deletion rules](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets),
[ruleset API and bypass visibility](https://docs.github.com/en/rest/repos/rules#get-a-repository-ruleset),
[NuGet trusted-publisher owner, scope and environment controls](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
