# NuGet publisher standby (#244)

This revision removes the legacy `ci.yml` publishing job and its `v*` trigger.
Main/PR CI and the ServiceDefaults drift check remain. The replacement
preparation and publishing jobs are **unconditionally disabled**. Merging this
revision pauses releases from the new main revision; it does not activate
publishing or complete #244.

## Registered files and verification

| File | Standby contract |
| --- | --- |
| `.github/workflows/ci.yml` | Main and PR validation only; no publishing job, OIDC permission or tag trigger. |
| `.github/workflows/nuget-prepare.yml` | Name `NuGet preparation`; `v*` trigger, strict stable `vX.Y.Z` validation, existing workflow ID `388401675`; job guard starts with literal `false &&`. |
| `.github/workflows/nuget-publish.yml` | Exact rendering of the disabled caller/step templates; successful `NuGet preparation` completion trigger; job and all fifteen steps retain literal `false &&` guards. |
| `scripts/sonar/main-policy.json` | Pins the changed CI file; baseline, thresholds, required jobs and freshness limits are unchanged. |
| `scripts/sonar/nuget-policy.json` | Pins the changed preparation file; identity, required steps and maximum age are unchanged. |

The publishing file retains the reviewed `NuGet/login` and explicit-file
`dotnet nuget push` sequence behind its disabled guards. No custom upload adapter
or persistent NuGet key is introduced.

The existing template-check Action also checks the registered files: rendering
parity, disabled jobs, absence of the legacy route and matching policy hashes.
Mutation cases reject accidental enablement, an additional preparation job,
manual preparation, a restored tag publisher, workflow drift and stale hashes.
Synthetic shell cases exercise the actual stable-tag preparation script and
reject acceptance tags. They do not execute GitHub workflows or obtain credentials.

`nuget-rehearse.yml` and `nuget-acceptance.yml` remain byte-for-byte unchanged.
They listen for `NuGet preparation (runner test)` and retain the isolated
`sonar-poc/nuget/...` namespace. The renamed, disabled producer does not supply
new runs to them. Historical runs are retained; production verification still
rejects acceptance identities and evidence.

## Evidence already collected

Before standby, main `c93e02fb1949ef011436d3791e4d19c345014b58` passed
[CI](https://github.com/andregoepel/app-foundation/actions/runs/39431013482),
[E2E](https://github.com/andregoepel/app-foundation/actions/runs/39431013670) and
[Sonar readiness](https://github.com/andregoepel/app-foundation/actions/runs/39431256762).
The unchanged central baseline is `899f364a-65bb-4f5e-9840-103b3ecc7825`.

The [preparation](https://github.com/andregoepel/app-foundation/actions/runs/39432569197),
[rehearsal](https://github.com/andregoepel/app-foundation/actions/runs/39433484674) and
[acceptance](https://github.com/andregoepel/app-foundation/actions/runs/39433484898)
runs passed with required reviewer approval, twelve package/symbol files,
stored intent/outcomes and five live rejection probes. Independent inspection
confirmed `acceptanceOnly: true`, `releaseEligible: false`,
`authorizesRetry: false` and `remoteAcceptanceVerified: false`.
Login was simulated and files were discarded locally. Temporary test
environments, Sonar token copies and the test tag were removed.

These are historical results, not a valid receipt for this revision. The central
readiness workflow analyzes merged main only. Before merge, use local prototype
Sonar analysis; after merge, require fresh exact-main CI, E2E and central Sonar.
Do not weaken the trusted-main boundary to analyze a PR centrally.

## Account controls remain a blocker

The authenticated NuGet audit on 2026-10-20 found sole owner `andre.goepel` for
all six AppFoundation packages and no listed persistent API keys. Eight active
trusted-publisher policies nevertheless use package scope `*`, allow new package
IDs and versions, and have no environment restriction. The legacy AppFoundation
policy still trusts `ci.yml`; seven other repository policies also overlap.

**This PR changes none of those policies.** Old tags can still contain the legacy
publisher and its identity remains trusted by NuGet. The signed-in owner also
retains manual package/symbol upload and policy-administration authority.
Removing a job from current main is therefore not complete account enforcement.

The reviewed local mapping assigns the 15 existing package IDs to seven intended
repository-specific policies. Its proposed AppFoundation replacement is:

| Field | Proposed value, not applied |
| --- | --- |
| NuGet owner | `andre.goepel` |
| GitHub owner / immutable ID | `andregoepel` / `22834588` |
| Repository / immutable ID | `app-foundation` / `1113145682` |
| Workflow filename | `nuget-publish.yml` |
| Required environment | `nuget-publish` |
| Scope | The six exact IDs below, one per line; push only new versions; no unlist/relist. |

```text
AndreGoepel.AppFoundation
AndreGoepel.AppFoundation.Aspire
AndreGoepel.AppFoundation.Core
AndreGoepel.AppFoundation.Hosting
AndreGoepel.AppFoundation.MailService
AndreGoepel.AppFoundation.ServiceDefaults
```

The proposed peer-policy narrowing and retirement of `Github Workflows` need
separate authorization and a final usage check. Other repositories are not
onboarded to Sonar by this work.

## Next approval boundaries

1. Review and merge standby, then validate its exact main revision. No release
   tag is needed and neither replacement job should execute.
2. Under an explicitly approved release pause, re-audit account controls,
   narrow overlapping grants and retire the legacy `ci.yml` policy. Allow
   already-issued credentials to expire; do not assume policy edits revoke them.
3. Configure the reviewed environments and new exact-scope NuGet policy while
   both jobs remain disabled. Preparation uses `sonar-nuget-prepare` with tag
   rules; publishing uses `nuget-publish` restricted to branch `main`.
   Require `andregoepel` review, allow self-review and disable administrator
   bypass. Preserve `sonar-main` and existing release-tag/branch protection.
4. Review the complete non-publishing acceptance evidence and any remaining
   official-login validation requirement. The discard drill does not establish
   successful NuGet authentication or remote publication.
5. Only with explicit activation approval, enable the reviewed production
   guards and update the preparation policy hash in another PR. A real release
   needs separate approval, an unused stable version and fresh readiness.

Never restore the ungated publisher or wildcard credentials as a fallback.
The first-attempt and single-run-per-main-SHA checks remain: do not consume a
production attempt for a registration test, rerun it or delete history to permit
another attempt. Missing/uncertain publication records require reconciliation;
they never authorize automatic retry, symbol-only recovery or repacking.

External archive selection and integration remain deferred until after #247
acceptance and detailed operator review. Local/GitHub records are not verified
independent durable retention. Keep #244 open pending the remaining controls.
