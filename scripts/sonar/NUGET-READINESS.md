# NuGet publication barrier preparation (#244)

Status: local implementation for review; **not activated**. Existing Actions,
NuGet credentials, feed settings and other repositories are unchanged. Nothing in
this preparation publishes a package, creates a tag or starts a release. Issue
#244 stays open until the trusted-runner and integration criteria below pass.

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

## Acceptance still required before closing #244

Local unit tests exercise positive evidence, source/run/artifact substitution,
expired receipts, missing E2E, failed gates, policy drift, tag movement, package and
symbol mismatches, and the final verification boundary. A local versioned build and
pack smoke test can use a clearly marked test harness without claiming trusted
readiness for uncommitted work.

After review and merge, run the actual verifier on the intended trusted runner with
fresh main readiness, against a non-publishing sink, for all six packages. Repeat
the negative matrix and instrument credential/upload calls to prove zero calls on
every rejected path. Test package-transfer tampering, tag-event/retarget binding,
expiry during preparation and manual/symbol retries. Activate only after approving
the existing-workflow and credential-policy changes. Keep #244 open until these
checks and every issue acceptance criterion are fulfilled.

Implementation references:
[GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts),
[dotnet pack](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-pack).
