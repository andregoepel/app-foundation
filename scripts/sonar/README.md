# app-foundation analysis prototype (#242)

The C# console tool in `tools/AndreGoepel.AppFoundation.Sonar` performs full-solution
analysis on a **separate local SonarQube instance**. It uses the repository's SDK
and `AndreGoepel.Core.Result`. PowerShell and Bash provide equivalent launchers;
there is no Python dependency.

The tool never deploys, publishes packages, commits or pushes. The separate
[NuGet dry run](NUGET-READINESS.md) prepares the #244 publication barrier without
changing active Actions. Local receipts always
have `releaseEligible: false`. The separate, initially disabled trusted-main entry
point and its activation requirements are documented in [MAIN-READINESS.md](MAIN-READINESS.md).
Solution builds/tests automatically include the console tool and its xUnit
project. The [NuGet standby migration](NUGET-STANDBY.md) removes the legacy CI
publisher and registers disabled replacement jobs. Account-policy cutover and
publishing activation remain pending under #244.

The combined [release eligibility contract](RELEASE-ELIGIBILITY.md) documents
publication-record inspection/replay rejection and the non-deploying staging
harness, including their acceptance evidence and remaining operational boundaries.

## Prerequisites

- .NET SDK **10.0.402**, pinned without roll-forward; PowerShell **7+**; Docker.
- A local ASP.NET development certificate (`dotnet dev-certs https`) for E2E.
- SonarQube Community Build **26.10.0.133078** on loopback port 9000, with a
  separate server ID and an `app-foundation-local` project (optional local suffix).
- The reviewed **AppFoundation PoC 1.0.0** gate/profiles from #241 assigned locally,
  with no coverage exclusions. Never supply a staging credential or project key.
- A local project analysis token and a separate local verifier token with project
  Browse permission. Audit inherited group permissions; verifier requests only read.
- Chromium runtime dependencies on Linux. Provision them explicitly after building
  using the generated `playwright.ps1 install --with-deps chromium` on a disposable
  Ubuntu runner. The analysis tool installs Chromium itself.

Tools are pinned in `.config/dotnet-tools.json`: CSharpier **1.3.0**,
SonarScanner **12.0.0**, dotnet-coverage **18.12.0**. Restore is locked.
AppHost/E2E retain existing lockfile opt-outs for OS-specific Aspire dependencies;
their packages still undergo the full audit. This is a reproducibility limitation.

A disposable Linux container on Docker Desktop needs host networking to reach
local Sonar, `/var/run/docker.sock.raw:/var/run/docker.sock`,
`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`, and
`DOTNET_ASPIRE_CONTAINER_RUNTIME=docker`. This grants control over local Docker;
use it only in an explicitly approved private environment. Create the development
certificate inside the container. Native Linux runners need separately verified
Docker provisioning. See [Testcontainers guidance](https://dotnet.testcontainers.org/dind/).

## Usage

Compare the local `/api/system/status` server ID with your recorded prototype
identity. Supply `APPFOUNDATION_SONAR_TOKEN` and `APPFOUNDATION_SONAR_VERIFY_TOKEN`
from protected storage without echoing them.

```powershell
# PowerShell 7; local credentials already loaded
./scripts/sonar/analyze.ps1 --server-id <local-server-id> --run-name windows-001
Remove-Item Env:APPFOUNDATION_SONAR_TOKEN
Remove-Item Env:APPFOUNDATION_SONAR_VERIFY_TOKEN
```

```bash
# Local credentials already loaded
bash scripts/sonar/analyze.sh --server-id <local-server-id> --run-name linux-001
unset APPFOUNDATION_SONAR_TOKEN APPFOUNDATION_SONAR_VERIFY_TOKEN
```

Launchers restore/publish a disposable copy under `artifacts/sonar-bootstrap/`
and remove it afterward. This lets the tool rebuild its own project on Windows
without locking output files. Tokens are withheld from bootstrap builds and child
process environments. The bootstrap is not coverage evidence; xUnit exercises the
rebuilt console assembly under dotnet-coverage.

Use a new run name each time. There is no test filter or coverage-reuse option.
Do not edit sources or analyze the same local project concurrently. An exclusively
created `.sonar-analysis.lock` file protects the checkout. Inspect that exact file
and `.sonarqube` after interruptions before removing stale state. Existing reports
are never overwritten.

## Validation and evidence

Sequence: tool restore → formatting → locked restore → vulnerability audit →
ordinary Release rebuild → scanner begin → fresh rebuild → complete discovery and
coverage → validated merge → scanner end → task-specific gate verification.
All solution projects must appear in scanner metadata. The ordinary build disables
scanner imports and retains warnings-as-errors; vulnerabilities at every severity
block separately. CSharpier remains the formatter. Only generated output, scanner
artifacts and vendored libraries are excluded; no authored coverage exclusions apply.

The starting inventory is **337 tests**: 93 foundation, 29 mail, 8 Aspire,
5 integration, 14 E2E, and 188 console-tool tests. `contract.json` records minimum
counts. MTP discovers all tests; executed xUnit names must match exactly, with no
skips/failures. New tests are automatically included.

All six coverage reports must be fresh, with executed production sources and
checksums matching this checkout. E2E independently covers the sample AppHost,
sample Program and hosting initialization. The tool's own xUnit suite supplies
console coverage. Microsoft's merge drops checksums, so merged sources must match
the union of verified inputs. Source snapshots must stay unchanged throughout.
TRX and merged Visual Studio coverage XML are imported into Sonar.

```bash
dotnet test --project tests/AndreGoepel.AppFoundation.Sonar.Tests/AndreGoepel.AppFoundation.Sonar.Tests.csproj -c Release
```

Regression tests cover discovery/results, stale/empty coverage, source mapping and
checksums, child-process anchors, target restrictions, audits, pipeline ordering,
preserved evidence, cleanup, credential handling and rejected task/gate receipts.
HTTP interactions are mocked; tests never contact Sonar.

`artifacts/sonar/<run>/publish/summary.json` is the **only publishable output**:
test counts, source/contract hashes, scope, task/analysis IDs, measures and gate.
When a test process fails or its results fail validation, the optional `testFailure`
field records the suite, process exit code (null if the process could not complete),
report availability, result counts and up to 50 failed test methods. Method names
must match discovery; theory arguments are omitted and unrecognized names withheld.
Failure categories are a fixed vocabulary (`timeout`, `cancelled`, `browser`,
`test-framework`, `other`), not exception messages or stack traces. Assembly-level
errors are counted separately. Missing, stale or unreadable reports are marked
`unavailable`; malformed, oversized or unexpected XML is marked `invalid`.
These diagnostics never make failed evidence eligible or permit scanner upload.
Successful summaries retain their existing schema and omit `testFailure`.
Raw logs, coverage, XML/TRX and browser traces stay private; never upload a whole
workspace. Git and CSharpier ignore generated files. The trusted-main workflow
already retains the sanitized summary on failure; it does not retain private logs.

For an intermittent E2E failure, inspect the exact run attempt's summary first.
Reproduce locally with coverage enabled and retain the private report and trace.
A successful unchanged retry demonstrates recovery, not the original failure's
cause. If no report was retained, record that limitation rather than inferring a
particular failing test from timing alone.

Scanner calls require the analysis token as an argument; local process inspection
can expose it, so use a private account. Logs redact both tokens, timeout errors
omit arguments, and cleanup removes owned scanner state. Redirects/proxies are
disabled. The default local entry point rejects central URLs, keys and the known
central server ID. Only the explicit trusted-main entry point can use the pinned
central target, after validating GitHub context and successful main CI/E2E evidence.
Verification checks the submitted analysis, latest result, gate assignment and
coverage import. Empty/unexpected gates or overwritten results fail.

A passing local result is not an approved main baseline. The central
SPECIFIC_ANALYSIS baseline must follow a complete trusted-main run; #240's
transport smoke test cannot establish it.

References: [scanner behavior](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/scanners/dotnet/using),
[coverage import](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/test-coverage/dotnet-test-coverage),
[dotnet-coverage](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage).
