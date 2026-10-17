# Trusted main readiness (#243)

The app-foundation PoC adds `.github/workflows/sonar-main.yml` independently of the
existing CI, E2E and publishing workflows. The new workflow is **disabled until
`SONAR_MAIN_ENABLED=true` is explicitly configured**. No GitHub secrets, environment
or VPS configuration is provisioned by this change. No package or application is
published. Other repositories remain outside the PoC.

## Trust and execution

The workflow listens for completion of the existing `CI` workflow on `main`. Its
job accepts only push events from this repository. It rejects failed CI or a SHA
that differs from the readiness workflow's own main revision before checkout.
There is no PR trigger, arbitrary checkout ref, manual analysis input, reusable
entry point, cache reuse or download of artifacts produced by another run.

The C# tool's explicit `--trusted-main` entry point validates the event payload,
repository, main ref, workflow path/revision, run ID/attempt and checkout. It queries
GitHub independently to confirm the candidate is still the current main tip. The
local entry point continues to reject central targets.

The verifier requires successful push runs for **the same SHA** from both existing
workflows. CI must include successful restore, tool restore, CSharpier, build, test,
vulnerability scan and ServiceDefaults drift steps; E2E must include its build,
browser installation and test step. Missing, duplicate, skipped, cancelled or
failed mandatory jobs/steps are rejected. Pending evidence is polled for at most ten
minutes; incomplete pagination is rejected. A successful overall workflow cannot
conceal a failed mandatory step or a missing E2E job. Freshness uses the current
attempt's `run_started_at`, since reruns retain the original creation time.
Mandatory jobs must start within that attempt; rerun all jobs for revalidation.

`main-policy.json` pins the workflow IDs, paths, source-file hashes and actual
reusable-workflow commit revisions observed in GitHub. Source hashes normalize
CRLF/LF only. An update to the shared workflow repository therefore requires a
reviewed policy update here, even if its mutable `@main` reference stays the same.
This change does not modify that repository.

After validating dependencies, the tool runs the same complete analysis pipeline
as the local prototype: ordinary build and audit, fresh scanner build, discovery,
all six test projects, checked coverage merge, upload and bounded quality-gate wait.
Central scans retain SCM information and explicitly set the verified revision and
a build string containing repository/run/attempt/SHA. Scanner credentials read the
exact CE task; the separate Browse-only verifier checks the resulting analysis and
gate. No extra Sonar permissions are needed.

One repository-wide concurrency group serializes central submissions without
cancelling a running submission. GitHub may replace a pending run with a newer one;
that does not create readiness for the omitted commit. The current main tip,
dependency attempts/jobs and policy are rechecked before scanner begin, immediately
before upload, during verification and before writing readiness. Superseded work
cannot publish successful evidence. A later main push invalidates an older receipt
for consumers, even if it happened just after the receipt was written.

## Sonar policy and baseline

The committed policy preserves all six `AppFoundation PoC 1.0.0` conditions. It
records the 27 selected profile names, active-rule counts and rule-update timestamps
read with the existing verifier account. Changes to these profile revisions or
gate thresholds fail verification; the original full profile XML exports remain in
private operator storage. The policy fingerprint covers this reviewed configuration
and baseline, not an export of every active rule body.

Coverage exclusions are prohibited and the small-change exemption must be disabled.
Evaluated conditions must match the approved thresholds and pass independently of
the overall gate status. A missing new-code ratio condition is accepted only with
explicit zero denominators: both new lines/conditions to cover for coverage, new
lines for duplication, or new security hotspots for hotspot review. Missing measures
are never interpreted as zero. Overall coverage and issue conditions are mandatory.

The baseline selected in `main-policy.json` is the first complete trusted-main
analysis, `899f364a-65bb-4f5e-9840-103b3ecc7825`, for commit
`4752d6dbc0e6c39c824a3ce11e72008410d524e3`. The
[initial central run](https://github.com/andregoepel/app-foundation/actions/runs/38997584666)
passed all 349 tests across six suites, analyzed all 15 projects and reported 84.8%
coverage with zero issues and hotspots. Its Sonar gate passed; readiness was blocked
because the initial committed baseline was null. The transport smoke test is not used.

Readiness requires the server's `SPECIFIC_ANALYSIS` setting to match the committed
baseline ID. Selecting the ID in this file does not change that server setting.
The tool never selects or changes a baseline automatically, and the baseline must
differ from the current analysis. The scanner account reads the baseline API;
Browse-only access to that API is denied by the installed server and is not broadened.

## Receipts and downstream obligations

`summary.json` remains analysis evidence with `releaseEligible: false`. It is saved
on success/failure when the analysis output directory was created and cleanup could
write it. Only this sanitized file is uploaded for diagnostics; raw logs, source,
coverage XML, traces and scanner state are excluded.

`readiness.json` is written only after full analysis, policy/commit verification,
scanner cleanup and a final clean-source check succeed. Schema 1 binds:

- Repository, SHA, workflow path/revision, run ID and attempt.
- Triggering CI attempt, mandatory CI/E2E runs, jobs, successful steps and shared revisions.
- Policy version/fingerprint, baseline, source snapshot and analysis-contract hash.
- CE task, exact analysis ID and SHA-256 of the sanitized analysis receipt.
- Issue/expiry times: at most one hour, also capped by the two-hour CI evidence age.

The artifact name includes SHA, run ID and attempt. An artifact is **not sufficient
authorization by itself**. The future #244/#245 consumer must retrieve it from the
expected GitHub workflow/run/attempt, require that workflow to have completed
successfully, verify the schema/hashes/policy/lifetime and recheck current main,
mandatory attempts and Sonar analysis. Unknown, stale, superseded or incomplete
evidence must block release before publishing credentials or deployment access.
Readiness never initiates a NuGet release automatically.

The analysis command has a 75-minute bound, the workflow a 90-minute bound, HTTP
requests 30 seconds, child commands 30 minutes, and scanner gate waiting five
minutes. Token/API errors and timeouts fail without readiness. Child commands and
bootstrap builds do not inherit the explicitly provided GitHub/Sonar credentials.

## Reviewed activation sequence

1. Review and merge the implementation through its own PR. Keep existing CI/E2E
   and publishing workflows unchanged.
2. Configure the `sonar-main` GitHub environment to allow only `main`, with the
   operator's chosen review protection. Provision its two secrets from protected
   operator storage: `APPFOUNDATION_SONAR_TOKEN` and
   `APPFOUNDATION_SONAR_VERIFY_TOKEN`. Do not print or paste token values into logs,
   issues, source files or PR bodies. The workflow uses its read-only GitHub token.
3. Set the repository variable `SONAR_MAIN_ENABLED=true`. Rerun the current main
   CI run, or use the next reviewed main push. The resulting main-only readiness
   workflow waits for successful E2E evidence for that same revision.
4. Inspect the full first analysis and sanitized failure receipt. Establish its
   reviewed baseline through the operator boundary, then commit that analysis UUID
   in `main-policy.json` through another reviewed change. Revalidate main CI/E2E if
   their two-hour lifetime expired.
5. Demonstrate at least two successful trusted-main runs and a controlled real
   gate failure followed by corrected success without changing thresholds or
   suppressing findings. Record the exact SHAs, workflow attempts and analysis IDs.

Disabling the variable stops new analyses; revoking credentials makes new attempts
fail. Existing app deployments and NuGet behavior are unaffected until #244/#245
introduce their separately reviewed barriers. Do not interpret disabled/skipped
readiness as release eligibility.

## Remaining live acceptance

Local tests cover context/credential boundaries, API failures, timeouts, missing
jobs/steps, policy changes, baseline rejection, submission ordering and superseded
evidence. They do not replace the GitHub-hosted main runs or a real failed Sonar
gate. Keep #241 and #243 open until their live criteria are demonstrated. No other
repository may be onboarded before the explicit acceptance recorded in #247.

References: [GitHub workflow-run events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run),
[workflow-run REST API](https://docs.github.com/en/rest/actions/workflow-runs),
[Sonar revision, build string and gate-wait parameters](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/analysis-parameters/parameters-not-settable-in-ui).
