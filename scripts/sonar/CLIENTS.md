# Sonar and GitHub client decisions

Issues: #253 and #254. Evaluated October 22, 2026 against the accepted
app-foundation PoC and SonarQube Community Build `26.10.0.133078`.

## Decisions

Use the pinned official `dotnet-sonarscanner` 12.0.0 for analysis/upload and
Quality Gate waiting, and `dotnet-coverage` 18.12.0 for coverage. Keep a small
read-only Sonar adapter for the policy and analysis checks those tools do not
perform. No additional Sonar SDK passed the capability/maintenance review below.

Use pinned **Octokit.NET 14.0.0** for GitHub JSON API transport, authentication,
user-agent construction and API error classification. Keep the existing strict
evidence validators and receipt schemas. This change adds no package dependency
to the published AppFoundation libraries; Octokit belongs only to the analysis
console tool and its tests.

Existing Actions stay unchanged. Native job dependencies cannot replace the
checks joining the current independent CI, E2E and readiness workflows within
that constraint. No workflow migration, publishing activation, policy change or
new central baseline is part of these issues' implementation.

## Sonar capability comparison

| Candidate | Evidence | Decision |
| --- | --- | --- |
| Official SonarScanner for .NET | Already pinned; builds the report, uploads it, and waits with `sonar.qualitygate.wait=true` and a bounded timeout | Retain as the single scanning/waiting mechanism |
| SonarSource `sonarqube-webclient-dotnet` | Repository archived February 23, 2023; LGPL-3.0; no maintained compatibility contract for the pinned 2026 Community Build | Do not add an archived SDK |
| `SonarQube.Net` 1.0.5 | Published May 7, 2021; source describes SonarQube 8.x; MIT; package brings older Flurl/XML dependencies | No demonstrated support for the required current policy/identity contract; do not adopt |
| `SQ.Api` 1.0.8 | Published March 31, 2026; MIT, .NET 10; package references Microsoft.Extensions.Http, logging abstractions, Polly and Polly.Extensions.Http | Its published source/repository URL returned 404 during evaluation. Required endpoint behavior, redirect/token handling and compatibility could not be verified; do not infer suitability from its coverage claims |
| Existing `SonarClient` | GET-only adapter using BCL HTTP/JSON, per-request credentials, cancellation and a 30-second HTTP timeout; redirects/proxy disabled by the production client factory | Retain for the specific reads below, with contract tests; not a general-purpose Sonar SDK |

The newer package was considered, rather than assuming that only the two older
clients exist. The 404 is an observed review limitation, not a claim that the
package is malicious or permanently unavailable. Revisit the selection when a
candidate's maintained source and required behavior can be verified.

## Remaining Sonar reads

| Endpoint | Purpose beyond scanning/waiting |
| --- | --- |
| `system/status` | Match the expected server identity, version and readiness |
| `settings/values` | Reject changed coverage exclusions or small-change exemptions |
| `ce/task` | Bind the scanner's task receipt to a successful task, project and analysis |
| `project_analyses/search` | Reject superseded analyses; check revision/build identity |
| `qualitygates/project_status` | Inspect the exact submitted analysis and retain its conditions |
| `qualitygates/get_by_project` | Verify the assigned gate |
| `qualitygates/show` | Compare the gate definition with the reviewed policy |
| `qualityprofiles/search` | Verify assigned profiles, rule counts and revision timestamps |
| `measures/component` | Confirm coverage import, test metrics and required new-code measures |
| `new_code_periods/show` | Verify the approved specific-analysis baseline |

The scanner performs all polling for task completion/Quality Gate status. The
subsequent reads are verification, not a second polling loop. Producer and
release-consumer checks occur at distinct trust boundaries: removing a later
read would permit supersession or policy changes between analysis and use.
The adapter rejects malformed/non-object JSON; missing required fields still
fail in the existing validators. Credentials remain per request because scan
and verification permissions are deliberately separate.

## Octokit compatibility and narrow exceptions

Octokit 14.0.0 is MIT-licensed, targets .NET Standard 2.0 and declares no package
dependencies. The published package and pinned source were inspected. Required
workflow fields include `run_attempt`, `referenced_workflows`, repository identity,
source SHA and attempt-specific job/step results. The release consumers also use
refs, annotated tags, workflow contents, artifact inventories and producer runs.

All paths below are relative to `repos/andregoepel/app-foundation/`:

| Endpoint family | Required evidence | Responsibility |
| --- | --- | --- |
| `git/ref/heads/main`, `git/ref/tags/{tag}`, `git/tags/{sha}` | Current commit, immutable release ref, annotated-tag target | Release identity and supersession |
| `actions/workflows/{workflow}` | Registered workflow ID and path | Producer identity |
| `actions/workflows/{workflow}/runs` | Complete bounded inventory, latest run for the exact SHA/event/ref | Independent-workflow coordination and replay rejection |
| `actions/runs/{run}` | Source repositories, SHA/ref/event/path, status/conclusion, current attempt, start time and `referenced_workflows` | Run identity, freshness and reviewed shared workflow revision |
| `actions/runs/{run}/attempts/{attempt}/jobs` | Complete jobs, names/conclusions/start times and mandatory steps | Exact-attempt CI/E2E/producer verification |
| `actions/runs/{run}/artifacts` | Complete inventory, producer binding, ID/name/size/expiry and digest | Receipt and publication evidence selection |
| `contents/{path}?ref={sha}` | Exact workflow contents at the candidate revision | Reviewed producer configuration |
| `actions/artifacts/{id}/zip` | Verified archive and single expected evidence entry | Binary-download exception described below |

GitHub permissions remain `contents: read` and `actions: read`; no additional
credential or scope is introduced. Sonar retains separate project-analysis and
Browse-only verification credentials. The existing analysis credential reads the
task and new-code baseline APIs where Browse-only access is insufficient. Tokens
remain transient inputs and are never included in receipts or logs.

All these JSON reads use Octokit's cancellable `Connection.Get<T>` API. Its
serializer extension uses `System.Text.Json` with `JsonElement` so required-field
absence, original field names, unknown future fields and artifact `digest` remain
available to the existing fail-closed validators. This deliberately avoids
mapping JSON into SDK models and then reconstructing JSON for the validators.
In particular, the 14.0.0 artifact model has no `digest` property. It also avoids
the non-cancellable convenience methods for these evidence requests.

The small HTTP policy handler adds the existing pinned GitHub API version,
rejects redirects before Octokit's own redirect handling, and limits buffered
JSON to 4 MiB. A linked 30-second token covers sending and body buffering. API
errors, including rate limits, stop validation without an automatic retry; raw
API error bodies are not exposed in CLI exception output. Cancellation reaches
the underlying request. Requests cannot escape the app-foundation repository.

`GitHubArtifacts` remains the narrowly scoped binary-download exception. The
SDK's `DownloadArtifact` follows redirects and exposes no cancellation token;
it does not enforce our storage-host allowlist, archive SHA-256, byte limit or
single-entry contract. The existing reader inspects the first redirect, sends
no GitHub credential to signed storage, bounds both compressed and expanded
data, and checks the exact archive digest before reading the expected entry.
Do not replace it with a convenience download that weakens these checks.

Inventory handling also stays explicit: current consumers request bounded
pages and reject `total_count` mismatches. They do not silently validate only the
first page. Switching to automatic SDK pagination could mix changing run/job
inventories, so incomplete inventories continue to block readiness/publication.

## Coordination and compatibility

The current sequence remains:

1. CI and E2E run independently for main.
2. The existing readiness workflow starts after CI, waits only while required
   evidence is still pending, and validates the exact runs, attempts and jobs.
3. Full analysis runs; official scanner waiting completes before receipt checks.
4. The runner rechecks main, mandatory evidence and Sonar identity/policy before
   issuing its bounded-lifetime receipt.
5. NuGet/staging consumers revalidate that receipt and current state before use.

`needs` can coordinate jobs within one workflow (including reusable workflows
called as jobs); it cannot join the current independent runs. Therefore the
bounded CI/E2E wait and later freshness/supersession checks remain necessary.
Any future workflow consolidation needs a separate review of the producer and
consumer graph. No second CLI/SDK transport is introduced for JSON endpoints.

Summary schema 2, readiness schema 1, CLI arguments, environment variables,
source/contract hashing, policy thresholds and the baseline are unchanged.
The #244/#245 consumers require no migration. A failed, missing, skipped,
cancelled, stale or superseded input continues to block eligibility.

## Validation and rollback

The regression suite exercises raw evidence fields, invalid JSON, API errors,
rate limits, redirects, request cancellation and response limits. Existing
tests cover actual Sonar profile timestamps, policy drift, wrong task/analysis,
wrong SHA, incomplete inventories, rerun attempts, skipped jobs, stale receipts,
and NuGet/staging behavior through their production adapters.

Before merging, retain Windows/Linux suite results, complete local Sonar coverage,
a real local failed-gate/recovery pair and PR CI/E2E results. A local analysis is
not central main readiness. The unchanged main workflow validates the merged
revision only after the separately authorized merge. Never upload a branch to
the central Community Build project's main history to simulate PR analysis.

Rollback is a reviewed revert of this PR's tool, test, documentation and two
lockfile changes, followed by locked restore and the same checks. No database,
token, policy, receipt-schema or workflow rollback is required. Publishing stays
disabled throughout; do not add a silent runtime fallback to the old GitHub
transport when Octokit returns an error.

## Sources

- [Official scanner usage](https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/scanners/dotnet/using)
- [Scanner gate wait and timeout](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/analysis-parameters/parameters-not-settable-in-ui)
- [Archived SonarSource .NET client](https://github.com/SonarSource/sonarqube-webclient-dotnet)
- [SonarQube.Net package](https://www.nuget.org/packages/SonarQube.Net/1.0.5) and [source](https://github.com/lvermeulen/SonarQube.Net)
- [SQ.Api package](https://www.nuget.org/packages/SQ.Api/1.0.8) and [published package metadata](https://api.nuget.org/v3-flatcontainer/sq.api/1.0.8/sq.api.nuspec)
- [Octokit package](https://www.nuget.org/packages/Octokit/14.0.0), [connection API](https://github.com/octokit/octokit.net/blob/v14.0.0/Octokit/Http/Connection.cs), [HTTP adapter](https://github.com/octokit/octokit.net/blob/v14.0.0/Octokit/Http/HttpClientAdapter.cs), [artifact model](https://github.com/octokit/octokit.net/blob/v14.0.0/Octokit/Models/Response/Artifact.cs), [artifact client](https://github.com/octokit/octokit.net/blob/v14.0.0/Octokit/Clients/ActionsArtifactsClient.cs)
- [GitHub job dependencies](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-jobs)
