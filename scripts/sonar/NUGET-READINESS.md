# NuGet publication barrier preparation (#244)

Status: dry-run implementation, transferred-batch verification and non-publishing
sequence rehearsal merged; authenticated preparation-origin verification and
tag-event origin preparation and consumer implemented; producer workflow prepared
with its only job explicitly disabled and that revision registered in the consumer policy;
a separate handoff-rehearsal workflow is also prepared with its only job disabled;
**not activated**. Existing Actions,
NuGet credentials, feed settings and other repositories are unchanged. Nothing in
this preparation publishes a package, creates a tag or starts a release. Issue
#244 stays open until the remaining publication integration criteria below pass.

## Publication inventory

The only repository automation entry point is `ci.yml`'s repository-local
`pack-and-publish` job, triggered by explicit `v*` tags. It calls
`andregoepel/workflows/.github/actions/nuget-publish@main`. That composite action
currently checks out the tag, packs, exchanges OIDC credentials with `NuGet/login`,
and pushes packages. It has no Sonar readiness check. This existing route remains
active and unchanged by the preparation.

| Package | Prepared package and symbol checks |
| --- | --- |
| AndreGoepel.AppFoundation.Core | Required |
| AndreGoepel.AppFoundation.ServiceDefaults | Required |
| AndreGoepel.AppFoundation.MailService | Required |
| AndreGoepel.AppFoundation | Required |
| AndreGoepel.AppFoundation.Hosting | Required |
| AndreGoepel.AppFoundation.Aspire | Required |

The current publish action's default and the repository's sole configured feed
are `https://api.nuget.org/v3/index.json`. Symbols use the same NuGet.org publishing
route. The prepared command accepts neither additional feeds nor arbitrary
projects. It requires twelve artifacts: one `.nupkg` and one `.snupkg` per package.
The shared action can accept a feed override, but this repository does not use one.
No other repository publishing script or manual release workflow was found.

Local `dotnet pack` is allowed; it does not publish. Direct local uploads, manual
API-key uploads, symbol-only retries and future alternative workflows must be
included in the eventual credential policy. CI cannot prevent an independently
authorized NuGet account owner from uploading outside GitHub. Before activation,
inventory and revoke or restrict alternative publication keys through the account
owner; do not claim that a workflow guard alone controls them. No keys are changed
as part of this preparation.

## Prepared dry-run command

The existing C# tool has a `--nuget-dry-run` entry point. From a clean checkout of
the current main commit, with the documented SDK, Docker, development certificate
and Playwright prerequisites already available:

```powershell
./scripts/sonar/analyze.ps1 --nuget-dry-run --sha <CURRENT_MAIN_SHA> --version 2.2.0 --readiness-run <RUN_ID>
```

```bash
bash scripts/sonar/analyze.sh --nuget-dry-run --sha <CURRENT_MAIN_SHA> --version 2.2.0 --readiness-run <RUN_ID>
```

The sample version is a local test value, not a release instruction. No tag is
required. Optional `--tag v2.2.0` additionally checks an existing remote tag;
lightweight and annotated tags are resolved to their commit with a bounded depth.
The initial version policy accepts stable `X.Y.Z` versions only. Historical
commits, arbitrary refs, prereleases, feed overrides and stale-evidence overrides
are excluded. No command creates, moves or deletes a tag.

Provide `APPFOUNDATION_GITHUB_TOKEN` with repository Actions/Contents read access,
plus the existing `APPFOUNDATION_SONAR_TOKEN` and
`APPFOUNDATION_SONAR_VERIFY_TOKEN` through a protected operator boundary. These
are verification credentials, not NuGet credentials. The installed Sonar server
requires the existing analysis account to read the CE task and baseline; this
command only performs GET requests and never submits a scan. Do not broaden its
permissions. The command removes these environment values before spawning builds.

The preparation rejects the standard NuGet API-key variables and GitHub's OIDC
request variables. Run it without `id-token: write`. Build the bootstrap without
publication credentials as well. No login, credential exchange, package-upload API
or `nuget push` command exists in the dry-run implementation.

Use a fresh checkout with `core.autocrlf=false` to match the Linux producer's source
bytes. Untracked source and modified files fail verification; this deliberately
prevents uncommitted local changes from inheriting a main receipt. Generated output
remains under ignored `artifacts/sonar/nuget-<unique-id>/`. Do not run concurrent
builds or analyses in the same checkout.

## Evidence verification

Before starting package work, the verifier requires:

1. A clean checkout at the candidate SHA, which must still be GitHub's main tip.
   An optional tag must resolve to that same commit at both verification boundaries.
2. The newest readiness run for that SHA from workflow ID `387320916`, expected
   workflow path, repository, main branch and `workflow_run` event. Its current
   attempt must have completed successfully. Missing or incomplete inventories fail.
3. Exactly one readiness and analysis artifact named for that SHA/run/attempt,
   bound to the producer and not expired. Archive digests are checked against
   GitHub. Downloads are bounded, allow only the expected JSON entry, and do not
   extract arbitrary paths. GitHub authentication is not forwarded to signed
   artifact storage URLs.
4. Receipt schemas, source snapshot, analysis-summary checksum, contract checksum,
   policy version/fingerprint, workflow revision and baseline all agree. Receipt
   lifetime is bounded by the existing one-hour/two-hour policy. The complete
   analysis must include passing tests for every contracted suite, including E2E.
   Policy hashing explicitly uses LF serialization on all platforms, preserving
   existing Linux producer fingerprints when verification runs on Windows.
5. Current CI/E2E attempts, mandatory jobs and steps, shared-workflow revisions and
   timestamps still exactly match the receipt, using the existing evidence verifier.
6. The central Sonar server, completed task, latest analysis, source revision and
   run build string agree. The live quality gate, profiles, small-change setting,
   coverage exclusions and baseline must still satisfy the reviewed policy. Missing
   live metrics are not replaced with stale receipt values.

API errors, malformed/missing evidence, failed gates, changed attempts, newer main
commits and expired receipts fail closed. Re-run the trusted main CI/E2E/readiness
sequence when evidence expires; downloading an old artifact again does not renew it.

## Versioned package validation

After initial readiness verification the builder cleans Release output and performs
locked restores. It compares evaluated compilation properties and item inventories
for every solution project between the default main build and the requested release
version. This covers source/project/package references, analyzers, additional files,
resources, content and Razor inputs. A difference blocks preparation and requires
review and appropriate analysis; it is not silently treated as a version-only change.

It then rebuilds the entire solution at the release version, audits transitive
dependencies, discovers and runs all six test suites including E2E, and checks fresh
test reports against discovery. Pack uses `--no-build --no-restore` with the same
version and source revision, preserving the tested binaries.

The package validator requires the exact twelve-file inventory, package IDs,
versions, repository commit and internal package dependency versions. Assembly and
symbol bytes must match the corresponding versioned build outputs. Every output is
hashed and mapped to the sole permitted feed. Final readiness verification repeats
after building; changed or expired evidence prevents a successful dry-run receipt.

Evaluated input comparison is not a general proof about arbitrary custom MSBuild
targets or source generators. The current repository's version property supplies
package/assembly metadata. New version-dependent generated behavior or custom
packing targets require explicit review and additional analysis before publication.

`dry-run.json` is a local manifest with `dryRun: true` and
`releaseEligible: false`. It is not an upload authorization. Failed runs may retain
private diagnostic files or packages, but never write a successful manifest. Do
not consume loose packages or a manifest from an earlier run.

## Later integration, requiring separate approval

Keep the publish job in this repository and retain explicit version-tag releases.
NuGet trusted publishing binds the running job's workflow identity. Moving it into
another repository's reusable workflow would require a different publishing policy.
See [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

The existing composite action cannot simply run after this check: it would check
out and repack again. The approved integration must publish the exact validated
package set, without a second build or mutable checkout.

Prepare the eventual flow in this order:

1. Require a tag push in this repository and bind the event's commit and immutable
   tag object to current main. Preserve that binding through credential acquisition
   and reject retargeting. Do not add a main-success publication trigger.
2. Run preparation in a job without OIDC or NuGet upload credentials, with fresh
   checkout, pinned tool/action revisions and verified readiness. The staging/main
   analysis environment remains main-only; do not broaden it to tags. Design a
   separate protected verification boundary for tag releases before activation.
3. Transfer the verified package set and manifest from that exact preparation run
   and attempt. In the repository-local publish job, independently recheck current
   main/tag, live readiness and every package hash immediately before NuGet login.
   The current dry-run CLI deliberately does not implement this credential-bearing
   production mode. Its evidence-verification component is reusable there.
4. Only after all checks pass, exchange OIDC credentials. Upload the exact manifest
   files to the allowlisted feed, including symbols. No wildcard scan of unrelated
   output, repack, `always()` publication, `continue-on-error` bypass, alternative
   feed or unverified manual retry is allowed. Revalidate before each later upload
   batch. A partial NuGet upload cannot be rolled back; record accepted files and
   require fresh verification for retrying the remaining identical bytes.
5. Update this repository's reviewed CI workflow hash in `main-policy.json` as part
   of the separately approved workflow change. Changing the existing CI file alone
   would intentionally invalidate main readiness. Do not change the shared workflow
   repository or other consumers in this PoC.
6. Audit the six packages' trusted-publisher settings and any alternative keys or
   symbol-only/manual paths. Prove ordering using an instrumented non-publishing
   sink before enabling a real upload. A real release needs separate authorization.

The active Actions files are intentionally not edited or replaced here.

## Local transferred-batch verification

`NuGetHandoff` prepares the verification component for a later publication job.
It is currently reachable only from tests: there is no CLI entry point, workflow
call, OIDC exchange or upload adapter. Its result retains `dryRun: true` and
`releaseEligible: false` and must not authorize publication.

The input requires a verified main candidate, its exact release tag, a manifest
SHA-256 digest and the original Git tag object's SHA. The digest and tag-object
identity must come from a separately authenticated preparation/event boundary;
reading these values from the same untrusted transfer would provide no provenance.
`NuGetPreparedHandoff` now authenticates those pins against a preparation-origin
artifact before using the receiving verifier. The production producer/event
boundary and its activation remain pending; see the integration design below.

The verifier checks the following before returning a successful rehearsal result:

1. The remote tag still has the original object SHA and peels to the candidate
   commit, for both lightweight and annotated tags. Replacing an annotated tag
   with another object pointing to the same commit is rejected too.
2. Current main readiness passes the existing live verifier.
3. `dry-run.json` matches the pinned digest and has the expected schema, dry-run
   flags, source, version, readiness run/attempt and unchanged expiry. A receipt
   cannot extend readiness lifetime or turn itself into publication authorization.
4. The manifest and transferred directory contain exactly the six package/symbol
   pairs, correct IDs/versions, allowlisted feed and valid hashes. Missing/extra
   files, nested directories, linked paths and modified bytes are rejected. No
   rebuild or repack is performed in the receiving directory.
5. Readiness, tag identity, manifest and all package hashes are checked again,
   followed by another expiry check. A later retry starts the checks again and
   cannot reuse a previous successful result after evidence expires.

The receiving verifier relies on the pinned manifest from the existing producer's
versioned build and package-content validation. It does not independently prove
that an arbitrary caller-supplied digest came from a trusted GitHub run. The future
consumer must verify the preparation workflow/run/attempt and artifact provenance,
then bind the exact verified bytes through credential acquisition and upload.

Forty-five local tests cover the receiving component, including annotated tag
retargeting, manifest/package/symbol tampering, malicious inventory/feed entries,
changes during final verification and expired retries. Tests copy a batch produced
by the real dry-run orchestration with synthetic build output into a separate
receiving directory. No public package or actual tag is created.

## Non-publishing sequence rehearsal

`NuGetPublicationRehearsal` connects the receiving verifier to an instrumented
`INuGetRehearsalSink`. The only sink implementation is in the test project: its
login and upload methods record events and supplied bytes. There is no NuGet
client, OIDC exchange, credential, network upload, CLI entry point or workflow
integration. A successful receipt remains `dryRun: true`, `releaseEligible: false`.

The sequence verifies the complete transferred batch, captures the selected
package bytes in private memory and checks their hashes, then repeats the complete
handoff verification before simulated login. It verifies the full batch again
before each simulated file upload. The sink receives a read-only stream over the
captured bytes, so replacing a source file at the upload boundary cannot substitute
the content already selected for that upload. A changed file or evidence stops
subsequent uploads. These distributed checks do not constitute an atomic lock on
GitHub or Sonar state; the future credential-bearing adapter must preserve this
ordering and byte ownership through the actual upload.

A manual rehearsal can select an explicit, unique subset of manifest files,
including symbols only. Every invocation starts with full live verification of
the original twelve-file batch; an earlier success or partial acceptance is never
reused as authorization. A missing package therefore also blocks a symbol-only
retry. Unknown, duplicate or path-traversing selections are rejected before login.

Login or upload failure stops the sequence with no automatic retry. The test sink
records only acknowledged accepts. An exception or lost response does not prove
that an upload was rejected: a future operator must reconcile the remote outcome
before selecting remaining files. Partial acceptance cannot be rolled back, and
the rehearsal does not persist or authenticate a production retry ledger.

Thirty-eight cases exercise ordering with the real handoff and readiness
verifiers, invalid or changing evidence, exact-byte transfer, login/upload failure,
partial and symbol-only retries, cancellation and uncertain upload outcomes. The
fixture prepares and transfers synthetic packages through the existing builder;
all API responses and sink effects remain in memory. These tests prove the local
orchestration, not enforcement by the unchanged publisher or authenticated
provenance of the inputs.

## Acceptance evidence and remaining integration

The preparation-origin verifier and its production wiring requirements are
documented in [NUGET-INTEGRATION.md](NUGET-INTEGRATION.md). It adds 66 cases around
the existing rehearsal and artifact reader. There is still no active preparation
workflow or publication CLI. The assigned workflow ID, disabled file revision
and four required successful steps are registered in `nuget-policy.json`.
Registration cannot make a skipped job valid, and enabling the producer requires
another review of the changed workflow hash. It does not activate the NuGet barrier.

The positive intended-runner drill passed on Ubuntu 24.04:
[run 39101513675, attempt 1](https://github.com/andregoepel/app-foundation/actions/runs/39101513675).
It checked main commit `a0451c10af7232bdb63e3b75d7b3dc5568d3d4d1` against readiness
run `39100314932`, prepared all six packages and six symbol packages, and produced
only a manifest with `dryRun: true` and `releaseEligible: false`. No package was
published. Its temporary workflow branch, protected environment and token copies
were deleted; the workflow was never merged. This historical result does not
extend the receipt's lifetime.

The local acceptance suite now connects the actual `NuGetDryRun`,
`ReleaseReadiness`, GitHub and Sonar HTTP clients, archive reader, package builder,
report validation and package validator. A recording HTTP handler supplies
synthetic API responses and real ZIP bytes; a recording process adapter supplies
synthetic build/test output and package files. Unexpected destinations, non-GET
requests and commands outside the preparation allowlist fail the tests. No real
credentials, network services, package uploads or release tags are used by these
automated tests. Separate CLI tests execute the built console tool and each
platform's bootstrap script in child processes.

| Scenario | Required result | Test coverage |
| --- | --- | --- |
| Complete evidence, with or without an existing tag | Six package/symbol pairs; initial and final live checks; non-authorizing manifest | `NuGetAcceptanceTests.Run_CompleteEvidence_ValidatesAllPackagesWithoutPublication` |
| Failed gate, wrong SHA, unmerged/dirty source, stale/missing evidence, missing E2E, unknown workflow/revision | Stop before build, test or pack | `Run_InvalidInitialEvidence_StopsBeforePreparation` |
| Older attempt, newer failed producer, modified source/summary, changed policy/baseline or wrong tag | Reject without falling back to an earlier green result | `Run_InvalidInitialEvidence_StopsBeforePreparation` |
| Evidence expires or main/tag, gate, CI/E2E evidence, policy or baseline changes during packing | Reject the whole prepared batch; no success manifest | `Run_EvidenceChangesDuringPacking_RejectsPreparedPackages` |
| Version-dependent inputs, failed build/audit/discovery/tests, skipped tests | Stop before packing | `Run_VersionedPreparationFails_StopsBeforePacking` |
| Missing symbols, extra package, wrong version/commit/dependency/binary/symbols, duplicate ZIP entry | Reject the whole package set | `Run_InvalidPackageSet_RejectsWholeBatch` |
| Package or symbol bytes change during final verification | Reject final manifest | `Run_PackageChangesDuringFinalVerification_RejectsManifest` |
| HTTP 401/403/429/503, corrupted archive, untrusted storage redirect, malformed JSON or cancellation | Fail without preparation or a success manifest | `Run_ApiFailure_StopsBeforePreparation`, `Run_UnreadableEvidence_StopsBeforePreparation` |
| Previous successful manifest exists but current evidence is missing/expired | Reject without reusing or renewing the earlier manifest | `Run_PreviousSuccessfulManifest_DoesNotReplaceFreshVerification` |
| OIDC request variables or package/symbol credentials already present | CLI rejects before configuration; bootstrap rejects before restore/build | `NuGetCommandTests.Run_PublicationCredential_ExitsBeforeReadingConfiguration`, `Bootstrap_PublicationCredential_ExitsBeforeRestoreOrBuild` |
| Manual publish, alternate package/symbol feed, API key, stale override or inconsistent tag option | CLI rejects before preparation | `Run_ManualPublicationOrBypassOption_ExitsBeforePreparation` |
| Missing read credentials | CLI rejects without package preparation | `Run_MissingReadCredentials_ExitsWithoutPreparingPackages` |

The 72 additional cases passed locally on Windows and Linux, bringing the tooling
suite to 396 passing tests with no skips. Windows exercised the PowerShell
bootstrap; Linux exercised Bash. The Linux container had no Docker socket or host
network access and was removed afterward. Additional operator checks ran the real
CLI against a clean main checkout and the edited checkout: wrong SHA, dirty source
and an unrelated producer run all returned exit code 1 before creating any package
preparation directory. Only the unrelated-producer check needed live GitHub reads;
none required a Sonar request or Sonar credentials.

These tests establish rejection and ordering in the implemented preparation path.
They do not establish enforcement by the unchanged production publisher: that
path still has no call to this verifier, and the current CLI has no credential
exchange or upload operation. The simulated build output is not another real
versioned build; the separate successful intended-runner drill supplies that
positive evidence.

Before closing #244, the separately approved production integration must still:

1. Bind the actual tag event and immutable tag object through the repository-local
   credential-bearing job, including retargeting checks. The local handoff verifier
   checks an explicitly supplied tag-object identity, and the prepared provenance
   layer authenticates its origin artifact. The `--nuget-prepare` command now
   captures the event and writes its origin after final batch verification; the
   producer workflow now exists with a hard-disabled job. Actual workflow invocation
   and tag-payload proof remain missing from production.
2. Validate the exact transferred package/symbol batch and manifest independently
   in that job, with fresh live readiness and hashes immediately before credential
   acquisition. The `--nuget-rehearse` consumer now composes the provenance and
   handoff verifiers with a discard sink; its policy pins the disabled producer
   revision. The new disabled rehearsal workflow prepares artifact downloads and
   the consumer invocation without publishing. Review the enabled revisions,
   connect them on the intended runner and test
   OIDC/login/upload ordering against that real workflow handoff.
3. Integrate the tested non-publishing sequence with the actual credential/upload
   adapter and authenticated preparation boundary. Preserve fresh verification and
   byte ownership for each upload and manual/symbol retry; reconcile uncertain
   remote outcomes and persist trusted partial-publication records. The current
   recording sink establishes local ordering only.
4. Inventory the account-level trusted-publisher policies and alternative keys for
   all six packages. The repository secret inventory contains only `NUGET_USER`;
   this does not prove that no organization/environment or external NuGet account
   key exists. Restrict alternative paths through the account owner before activation.
5. Review and run the changed tests and eventual integration on the trusted runner
   after their own approved commit/merge steps. Existing Actions and other
   repositories remain unchanged during this local preparation.

Do not close #244 or activate publication based solely on the local test matrix.

Implementation references:
[GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts),
[dotnet pack](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-pack).
