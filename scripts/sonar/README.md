# app-foundation analysis prototype (#242)

This runner performs full-solution analysis on a **separate local SonarQube instance**.
It does not publish packages, deploy, commit, push, or change GitHub Actions. Its
summary always has `releaseEligible: false`. Central trusted-main upload and
release barriers remain separate work under #243–#245.

## Prerequisites

- .NET SDK **10.0.402**, pinned without roll-forward in `global.json`.
- Python **3.12+**, PowerShell **7+**, and a working local Docker runtime.
- Coverage.py **7.16.2**, pinned in `requirements.txt`, installed in an isolated
  environment: `python -m venv artifacts/sonar-venv`, activate it, then
  `python -m pip install -r scripts/sonar/requirements.txt`. On Linux use `python3`
  and install the distribution's venv prerequisite if needed.
- SonarQube Community Build **26.10.0.133078** on loopback port 9000, with a
  separate server ID and an `app-foundation-local` project (optional local suffix).
- The reviewed **AppFoundation PoC 1.0.0** gate/profiles from #241 assigned locally,
  no coverage exclusions, and a **local** project analysis token. Never supply a
  staging credential. Redirects, central keys and the known central server ID are rejected.
- A separate local verifier token with project Browse permission. Audit inherited
  group permissions when provisioning it; the runner makes only read requests
  with this token. Central service accounts are not used for local scans.
- Chromium runtime dependencies on Linux. On a disposable Ubuntu runner, after
  building, use the generated `playwright.ps1 install --with-deps chromium` script
  as an explicit host provisioning step. The runner installs the browser itself.

Repository tools are restored from `.config/dotnet-tools.json`: CSharpier 1.3.0,
SonarScanner for .NET 12.0.0, and dotnet-coverage 18.12.0. Existing NuGet package
versions remain centrally pinned. Restore uses `--locked-mode`; the existing
AppHost/E2E opt-outs from committed lockfiles remain because Aspire uses
OS-specific orchestration dependencies. Their resolved versions must still pass
the full dependency audit. This is a documented reproducibility limitation.

## Windows and Linux

Obtain the expected server ID from the local `/api/system/status` endpoint and
compare it with your prototype's recorded identity. Load the local token into
`APPFOUNDATION_SONAR_TOKEN` from protected storage without echoing it.
Load the verifier into `APPFOUNDATION_SONAR_VERIFY_TOKEN` in the same way.

```powershell
# PowerShell 7; token already loaded from protected storage
./scripts/sonar/analyze.ps1 --server-id <local-server-id> --run-name windows-001
Remove-Item Env:APPFOUNDATION_SONAR_TOKEN
Remove-Item Env:APPFOUNDATION_SONAR_VERIFY_TOKEN
```

```bash
# Python 3.12+ and PowerShell 7; token already loaded from protected storage
bash scripts/sonar/analyze.sh --server-id <local-server-id> --run-name linux-001
unset APPFOUNDATION_SONAR_TOKEN
unset APPFOUNDATION_SONAR_VERIFY_TOKEN
```

Both entrypoints execute the same implementation. Each run name must be new.
There is no reuse-coverage switch and no test filter. Run from a private checkout;
do not edit source while a scan is running, or run another scan of the same local
project concurrently. A directory lock prevents concurrent use of one checkout.
After an interrupted run, inspect the exact `.sonarqube` and
`.sonar-analysis.lock` directories before removing them and starting a fresh run.

The sequence is: tool restore → format check → locked package restore → complete
dependency audit → ordinary Release rebuild → scanner begin → fresh Release
rebuild → discovery and coverage for **every** test project → validated merge →
scanner end and gate → task-specific verification. The full solution must appear
in scanner project metadata. `scanAll=true` includes additional supported file
types; generated output and vendored frontend libraries are excluded deliberately.
No authored sources are excluded from coverage. Razor/C# and sample hosts remain
in scope. The `artifacts/**` exclusion implements #241's generated-output policy.

The ordinary build explicitly ignores scanner imports and retains the repository's
warnings-as-errors policy. No scanner success can waive its failure. All reported
dependency vulnerabilities, including moderate ones, block the runner separately.

## Test and coverage integrity

The reviewed starting inventory on main is **149 tests**: 93 foundation, 29 mail,
8 Aspire, 5 integration, and 14 E2E. `contract.json` holds minimum counts, not a
filter. MTP discovers the complete current inventory; executed xUnit result names
must match discovery exactly, with no skips or failures. New tests are included
automatically. Removing tests requires a review of the contract.

`dotnet-coverage collect` instruments each test process and its child processes,
including the Aspire sample host. Every report must be newly created, contain
executed production sources, and match source checksums from the current checkout.
Coverage for this repo's assemblies cannot resolve to another checkout. External
NuGet sources are not counted as this repo's evidence. Generated `obj` files are
not authored sources. The E2E report must itself contain executed sample AppHost,
sample Program and hosting initialization code; a unit-only report cannot pass.
The merge tool removes checksums, so the merged report must match the source set
and verified checksums from the five fresh inputs. The source snapshot must
remain unchanged through submission and verification. Fresh TRX results are also
passed to Sonar for test execution import.
Coverage for the Python tooling is collected from both its rejection tests and
the actual analysis run, then imported as Cobertura before scanner end. Post-upload
verification/finalizer lines executed after this export remain uncovered unless
exercised by the tests; no source exclusions conceal them.
Sonar must confirm coverage import, report nonzero coverable lines, and return
nonempty evaluated gate conditions for the exact submitted analysis.

```bash
python3 -m unittest discover -s scripts/sonar -p 'test_*.py'
```

The validator tests cover missing discovery, changed executed tests, skipped tests,
missing/stale/empty coverage, wrong source paths/checksums, missing child-process
coverage, central-target rejection, and incomplete/vulnerable dependency audits.

## Evidence and credentials

`artifacts/sonar/<run>/publish/summary.json` is the **only publishable output**.
It contains test counts, source/contract hashes, project scope, task and analysis
IDs, imported metrics and the gate result. Raw coverage, xUnit output and logs stay
under `private/`; they can include test fixture configuration and local paths.
Never upload `private/`, `bin/`, `obj/`, `.sonarqube/`, browser traces, or a whole
workspace archive. No artifact publication is wired by this issue.

Both tokens are removed from the runner's environment before launching build/test
processes. Only scanner calls receive it as a process argument, as required by
the .NET scanner. Local process inspection can therefore expose it; use a private
developer/runner account. Scanner logs redact that token, timeout errors omit
arguments, and the generated scanner directory is removed in the finalizer.
The scanner polls its CE task using the analysis token; the independent verifier
reads analysis history, gate assignment and measures using the Browse token.
CSharpier and Git ignore scanner artifacts; CSharpier remains the formatter and
no Sonar rules are suppressed to accommodate it.

Passing this local check is not an approved main baseline. Remediation still needs
separately authorized review/commit/PR integration. The central SPECIFIC_ANALYSIS
baseline must only be set after a complete trusted-main run; #240's two-line
transport smoke test cannot be used for it.

References: [scanner build behavior](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/scanners/dotnet/using),
[coverage import](https://docs.sonarsource.com/sonarqube-community-build/analyzing-source-code/test-coverage/dotnet-test-coverage),
[dotnet-coverage](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage).
