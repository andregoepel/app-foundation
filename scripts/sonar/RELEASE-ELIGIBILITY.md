# Release eligibility: NuGet records and staging contract

Related to #244 and #245, under PoC epic #238. Publication-record checks and the
non-deploying staging contract reuse the existing GitHub/artifact clients and
`ReleaseReadiness`. The separate [NuGet acceptance workflow](NUGET-ACCEPTANCE.md#prepared-runner-acceptance)
uses the same record verifier with explicit test context; it adds no uploader.

## Verified main after #284

Main `b1bb47bbb6a1c18dbc28dceb91e92325ed74e674`, merged through #284:

| Evidence | Result |
| --- | --- |
| [CI 39414470674](https://github.com/andregoepel/app-foundation/actions/runs/39414470674) | Passed, 1,198 tests |
| [E2E 39414470759](https://github.com/andregoepel/app-foundation/actions/runs/39414470759) | Passed, 14 tests |
| [Template checks 39414470225](https://github.com/andregoepel/app-foundation/actions/runs/39414470225) | Passed |
| [Sonar readiness 39414668032](https://github.com/andregoepel/app-foundation/actions/runs/39414668032) | Passed, 1,212 tests; quality gate OK |

Sonar analysis `2ca77368-7421-4bfb-9581-8ceae7ddc8e2` reported zero findings and
hotspots, 89.4% overall and 95.2% new-code coverage, against unchanged baseline
`899f364a-65bb-4f5e-9840-103b3ecc7825`. The actual-readiness staging harness passed
23 cases and two invalid-input probes; [#245 is complete](https://github.com/andregoepel/app-foundation/issues/245#issuecomment-6290016070).
The six-package dry run passed on the same SHA, producing twelve package/symbol
files without publishing. These receipts are historical and expire; they do not
authorize a later candidate.

## NuGet publication records

`--nuget-verify-intent` now checks the complete publishing workflow run inventory
for the candidate's main SHA before and after retrieving the intent. Exactly one
first-attempt run must exist. Missing/incomplete inventory, prior/competing runs
and reruns block before login.

This is deliberately conservative: at most one publication attempt per main
commit, including when another version tag targets that commit. A stopped attempt
cannot be resumed through another run or tag. Do not delete runs to bypass the
check. GitHub history is not an append-only ledger: privileged deletion, retention
expiry and unrecorded alternate routes remain production activation blockers.

`--nuget-inspect-publication` authenticates a completed attempt against:

- Current main readiness, preparation provenance and the exact twelve-file handoff.
- The registered repository-local workflow/revision, single-run inventory,
  first attempt and completed job/step outcomes.
- The retrieved intent artifact, archive digest, content hash and full inventory.
- The outcome artifact's producer, expiry, digest, successful storage step,
  intent linkage, ordered package observations and CLI event sequence.
- Fresh intent, run/job/artifact and original handoff checks after inspection.

Use the existing read-only GitHub and Sonar credentials. Never supply NuGet keys
or OIDC request credentials. Example argument shape:

```bash
bash scripts/sonar/analyze.sh --nuget-inspect-publication \
  --sha "$MAIN_SHA" --version "$VERSION" --readiness-run "$READINESS_RUN" \
  --tag "v$VERSION" --handoff "$HANDOFF_DIRECTORY" \
  --manifest-sha256 "$MANIFEST_SHA256" --tag-object "$TAG_OBJECT_SHA" \
  --preparation-run "$PREPARATION_RUN" --preparation-attempt 1 \
  --publish-run "$PUBLISH_RUN" --intent-artifact "$INTENT_ARTIFACT_ID" \
  --intent-archive-sha256 "$INTENT_ARCHIVE_SHA256" --intent-sha256 "$INTENT_SHA256"
```

PowerShell accepts the same arguments. Hash arguments are lowercase SHA-256
without a `sha256:` prefix. Use independently retained pins from the reviewed
attempt, not pins supplied by an arbitrary downloaded manifest. The production
workflow is not registered yet; this boundary is tested against synthetic GitHub
responses using the real artifact ZIP/digest reader.

An authenticated missing outcome reports `outcomesAvailable: false` and twelve
`unrecorded` observations. Expired, ambiguous or inconsistent artifacts fail.
Every successful inspection report has `authorizesRetry: false`,
`remoteAcceptanceVerified: false` and `releaseEligible: false`.

| Observation | What it establishes |
| --- | --- |
| `not-dispatched` | The authenticated job skipped push and recorded no dispatch events. |
| `unrecorded` | No usable per-file event was retained; publication may have happened. |
| `uncertain` | Dispatch started without a recorded successful CLI exit; publication may have happened. |
| `cli-success` | The CLI returned zero; remote package and symbol availability remains unverified. |

No observation authorizes retry, including symbol-only retry. Inspection requires
fresh readiness and current main; it is not an expired-release recovery override.
For an older release, retain evidence and stop for operator reconciliation.

### Retention and recovery

The disabled production templates and the acceptance workflow retain intent/
outcomes for 90 days without overwriting. The operator explicitly deferred
independent external backup/archive evaluation and integration until **after
PoC completion and #247 acceptance**, followed by detailed review. The proposal
to evaluate encrypted EU Backblaze B2 storage is recorded at the end of #238 and
in [NuGet acceptance](NUGET-ACCEPTANCE.md#deferred-until-after-poc-acceptance).

That later block will cover original package/symbol bytes, tag objects, manifest/
record hashes, run IDs/attempts, authenticated metadata and incident decisions;
expiry, export/retrieval ownership and recovery after GitHub deletion must then
be tested. No credentials belong in the archive. External storage is currently
unimplemented, not a prerequisite for non-publishing PoC acceptance, and must not
be described as verified durable retention. Missing-record/retry rejection stays
in force; this deferral does not authorize production activation.

After interruption, preserve the exact files. Independently reconcile remote
packages and symbols before considering a separately reviewed recovery. A CLI
error, `409`, missing outcome, deleted run or expired readiness cannot authorize
repeat upload or repacking. This PR implements no remote-feed reconciliation,
durable ledger or privileged recovery override.

## Staging contract version 1

app-foundation is a library repository. This PoC deploys neither its sample nor
any host application. Candidate manifests are separate from trusted observations:

| Source | Required binding |
| --- | --- |
| Candidate | Schema 1, repository/current main SHA, readiness run/analysis/source hash, build run/attempt, immutable image, staging target and operator generation |
| Actual readiness verifier | Existing exact-main checkout, CI/E2E identities/steps, artifact digests, Sonar task/latest analysis, gate/policy/baseline, source hashes and lifetime |
| Independent build observer | Repository/head repository, main push, successful first attempt, approved workflow ID/path/hash/revision, readiness/source binding, authenticated provenance artifact, sequence and lifetime |
| Independent registry observer | The exact same image repository and `@sha256:` digest as the build and candidate |
| Operator-owned state | Fixed staging entrypoint, matching generation, candidate newer than the applied build sequence, idle state, no pending recovery or production selection |

Readiness and observed deployment evidence are checked twice. Changed state,
reordered candidates, mutable tags, wrong SHA/digest, unknown/changed workflows,
forks, missing provenance, reruns, failed/expired evidence, missing E2E/gate and
operator conflicts are rejected.

The PoC deployment observer is explicitly synthetic: workflow, registry and
operator values are fixtures. It performs no registry HTTP call, image build/pull,
SSH operation, privileged command or host mutation. It is not a production
provenance adapter; its image repository and entrypoint are identifiers only.

The executable harness checks actual readiness before/after its matrix. Inside
the matrix it uses frozen readiness and synthetic deployment fixtures:

```bash
bash scripts/sonar/analyze.sh --staging-contract \
  --sha "$MAIN_SHA" --readiness-run "$READINESS_RUN"
```

Run from a clean checkout of that exact current main revision with existing
read-only GitHub and Sonar credentials. Reports bind the actual SHA/run/analysis
and list expected versus observed outcomes. Actual readiness rejection returns
a failing report with no synthetic acceptance cases. All reports declare
`syntheticDeploymentEvidence: true`, `deploymentEligible: false`, `dryRun: true`.
They are not operator authorization tokens.

The matrix's missing-E2E/gate/expiry cases inject explicit synthetic readiness
rejections. Separate integration tests pass altered GitHub/Sonar evidence through
the real `ReleaseReadiness`, proving rejection before image evidence is read.

### First host-app onboarding requirements

After explicit PoC acceptance, select a real host repository separately. Register
its exact build workflow/revision and immutable registry identity. Implement
authenticated API/attestation observers; never use self-authored manifests or
caller-selected workflow policy as authority. Verify provenance archive contents,
digests and registry binding.

The target operator owns privileged entrypoints, target mapping, lock and durable
state. Acquire the host lock, recheck evidence/generation inside it, atomically
record the attempted candidate, deploy the exact digest, check health, and persist
success or recovery-required state before releasing the lock. Test concurrent/
reordered requests, crashes, rollback and failed health checks. This PoC's
read-only state snapshot does not establish those runtime guarantees.

Production requires separate explicit operator selection and approval. Never infer
it from a branch, label or submitted command. Real positive/negative deployment
tests, digest binding and recovery are required during that first onboarding.
Existing applications, deployment tools and operator repos are unchanged here.

## Bundled completion plan

| Work package | This PR | Remaining boundary |
| --- | --- | --- |
| #244 NuGet barrier | Outcome inspection, strict replay rejection, regression/command tests, recovery policy | Durable retention/recovery, account/environment audit, final non-publishing runner drill, separately approved activation |
| #245 staging handoff | Versioned contract, executable non-deploying harness, independent synthetic observations, positive/negative tests | Successful actual-main-readiness harness after merge; live deployment proof during first approved host onboarding |
| #239 + #246 operations | No changes | Group infrastructure audit, backup/restore, monitoring and operating documentation into a later work package |
| #247 PoC acceptance | No changes | Observation window and explicit acceptance; other repos remain excluded |

Keep publishing inactive and existing Actions unchanged. Account/environment
decisions remain in [NUGET-ACCEPTANCE.md](NUGET-ACCEPTANCE.md). Sonar/GitHub client
library consolidation in #253/#254 remains post-PoC work.
