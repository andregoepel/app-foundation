"""Full-scope local Sonar analysis. No deployment or central-upload capability."""

import argparse
import base64
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

import coverage as python_coverage

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = json.loads((Path(__file__).with_name("contract.json")).read_text())
GENERATED = {"bin", "obj", ".git", ".sonarqube", ".sonar-analysis.lock", "artifacts", "__pycache__", "node_modules"}

if __name__ == "__main__":
    _runtime_coverage = python_coverage.Coverage(data_file=None, source=[str(Path(__file__).parent)], config_file=False)
    _runtime_coverage.start()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def source_snapshot(root):
    files = {}
    for directory, directories, names in os.walk(root):
        directories[:] = sorted(d for d in directories if d not in GENERATED)
        for name in sorted(names):
            path = Path(directory) / name
            if name == ".git":
                continue
            require(not path.is_symlink(), "Source symlinks require an explicit mapping review.")
            files[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    digest = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
    return {"sha256": digest, "files": files}


def validate_target(url, project, server_id):
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme == "http" and parsed.hostname in {"localhost", "127.0.0.1", "::1"}
            and parsed.port == 9000 and parsed.path in {"", "/"}
            and not parsed.username and not parsed.password and not parsed.query and not parsed.fragment,
            "Only the loopback Sonar prototype on port 9000 is allowed.")
    require(re.fullmatch(r"app-foundation-local(?:-[a-z0-9]+)*", project), "Central project keys are forbidden.")
    require(server_id and server_id != "AA2DD71C-AaFDycIuQLOXJXX6YWNZ", "A separate local server ID is required.")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("Sonar redirects are forbidden.")


def api(url, endpoint, token=None, **query):
    request = urllib.request.Request(url.rstrip("/") + "/api/" + endpoint + "?" + urllib.parse.urlencode(query))
    if token:
        request.add_header("Authorization", "Basic " + base64.b64encode((token + ":").encode()).decode())
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    with opener.open(request, timeout=30) as response:
        return json.load(response)


def fresh_xml(path, started):
    require(path.is_file() and path.stat().st_size > 0, f"Missing or empty report: {path.name}")
    require(path.stat().st_mtime >= started, f"Stale report: {path.name}")
    return ET.parse(path).getroot()


def discovery_names(output, minimum):
    names = [line.strip() for line in output.splitlines() if line.startswith("  AndreGoepel.")]
    totals = re.findall(r"^Discovered (\d+) tests\.$", output, re.MULTILINE)
    require(len(totals) == 1 and int(totals[0]) == len(names) >= minimum,
            "Incomplete MTP discovery; review the inventory before changing its minimum.")
    require(len(set(names)) == len(names), "Ambiguous duplicate discovered test names.")
    return names


def validate_tests(path, started, discovered):
    document = fresh_xml(path, started)
    assemblies = document.findall("assembly")
    tests = document.findall(".//test")
    require(len(assemblies) == 1 and int(assemblies[0].get("errors", "1")) == 0, "Test assembly errors.")
    # xUnit's XML reporter escapes quotes in theory display names; MTP discovery does not.
    require(Counter(test.get("name", "").replace('\\"', '"') for test in tests) == Counter(discovered), "Executed tests differ from discovery.")
    require(all(test.get("result") == "Pass" for test in tests), "Failed, skipped or incomplete tests.")
    require(int(assemblies[0].get("total", "0")) == len(discovered), "Incorrect test-result totals.")
    return len(tests)


def validate_coverage(path, started, root, required=(), verified_sources=None):
    document = fresh_xml(path, started)
    mapped, covered = set(), set()
    checksums = {}
    for module in document.findall("./modules/module"):
        # Shared NuGet assemblies have their own external sources; only this repo's
        # modules must map to this checkout. Unknown external modules cannot count.
        name = module.get("name", "").removesuffix(".dll")
        if not any((root / folder / name / (name + ".csproj")).is_file() for folder in ("src", "samples", "tests")):
            continue
        ids = {}
        for source in module.findall("./source_files/source_file"):
            source_path = Path(source.get("path", "")).resolve()
            require(source_path.is_relative_to(root), f"Coverage source outside this checkout: {name}")
            relative = source_path.relative_to(root).as_posix()
            if "obj" in source_path.relative_to(root).parts:
                continue
            require(source_path.is_file(), f"Coverage source missing: {relative}")
            algorithm = source.get("checksum_type", "").lower()
            data = source_path.read_bytes()
            sha256 = hashlib.sha256(data).hexdigest()
            if algorithm:
                require(algorithm in {"sha1", "sha256"}, f"Unsupported source checksum: {relative}")
                require(hashlib.new(algorithm, data).hexdigest().lower() == source.get("checksum", "").lower(),
                        f"Coverage source checksum mismatch: {relative}")
            else:
                # Microsoft's merge drops checksums. Only accept sources already
                # verified against the fresh, unmerged reports in this same run.
                require(verified_sources and verified_sources.get(relative) == sha256, f"Unverified merged source: {relative}")
            checksums[relative] = sha256
            ids[source.get("id")] = relative
        for item in module.findall("./functions/function/ranges/range"):
            relative = ids.get(item.get("source_id"))
            if relative and relative.startswith(("src/", "samples/")):
                mapped.add(relative)
                if item.get("covered") == "yes":
                    covered.add(relative)
    require(mapped and covered, "Coverage contains no mapped, executed production sources.")
    require(set(required) <= covered, "Missing executed integration/E2E coverage anchors: " + str(set(required) - covered))
    return {"mappedSources": sorted(mapped), "executedSources": sorted(covered), "sourceChecksums": checksums}


def validate_audit(document, project_count):
    require(len(document.get("projects", [])) == project_count, "Incomplete dependency audit.")
    require(not document.get("problems"), "Dependency audit could not complete.")
    for project in document["projects"]:
        for framework in project.get("frameworks", []):
            for group in ("topLevelPackages", "transitivePackages"):
                require(not any(p.get("vulnerabilities") for p in framework.get(group, [])), "Known vulnerable dependency blocks analysis.")


def validate_project_scope(scanner_dir, root, projects):
    found = set()
    for path in (scanner_dir / "out").glob("*/ProjectInfo.xml"):
        document = ET.parse(path).getroot()
        values = {node.tag.split("}")[-1]: node.text for node in document}
        require(values.get("IsExcluded") == "false", "Scanner excluded a solution project.")
        project = Path(values["FullPath"]).resolve()
        require(project.is_relative_to(root), "Scanner project lies outside this checkout.")
        found.add(project.relative_to(root).as_posix())
    require(found == set(projects), "Scanner project scope differs from the full solution.")
    return sorted(found)


def run(args):
    validate_target(args.url, args.project, args.server_id)
    status = api(args.url, "system/status")
    require(status.get("id") == args.server_id and status.get("status") == "UP"
            and status.get("version") == CONTRACT["serverVersion"], "Unexpected local server identity, version or status.")
    token = os.environ.pop("APPFOUNDATION_SONAR_TOKEN", "")
    verifier = os.environ.pop("APPFOUNDATION_SONAR_VERIFY_TOKEN", "")
    require(token, "Set APPFOUNDATION_SONAR_TOKEN to a local project analysis token.")
    require(verifier, "Set APPFOUNDATION_SONAR_VERIFY_TOKEN to a separate local token with project Browse permission.")
    require(python_coverage.__version__ == "7.16.2", "Install the pinned scripts/sonar/requirements.txt in your Python environment.")
    os.umask(0o077)
    lock = ROOT / ".sonar-analysis.lock"
    lock.mkdir()  # Fail instead of racing another analysis in this checkout.
    scanner_dir = ROOT / ".sonarqube"
    output = ROOT / "artifacts" / "sonar" / args.run_name
    scanner_owned = False
    output_owned = False
    summary = {"schemaVersion": 1, "releaseEligible": False, "state": "failed", "platform": sys.platform,
               "project": args.project, "serverId": args.server_id, "sdk": CONTRACT["sdk"], "tests": {}}
    try:
        require(not scanner_dir.exists(), "Stale .sonarqube directory: inspect and remove it before a new run.")
        output.mkdir(parents=True, exist_ok=False)
        output_owned = True
        private = output / "private"
        private.mkdir()
        publish = output / "publish"
        publish.mkdir()
        started = time.time()
        summary["startedAt"] = started
        snapshot = source_snapshot(ROOT)
        summary["sourceSha256"] = snapshot["sha256"]
        summary["contractSha256"] = hashlib.sha256(Path(__file__).with_name("contract.json").read_bytes()).hexdigest()

        def command(label, command_args, allow_failure=False):
            print(label, flush=True)
            try:
                result = subprocess.run(command_args, cwd=ROOT, env={**os.environ, "DOTNET_CLI_UI_LANGUAGE": "en-US", "NO_COLOR": "1"},
                                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=1800)
            except subprocess.TimeoutExpired:
                raise ValueError(f"{label} exceeded its 30-minute limit.") from None
            text = result.stdout.replace(token, "[REDACTED]").replace(verifier, "[REDACTED]")
            (private / (label + ".log")).write_text(text, encoding="utf-8")
            require(allow_failure or result.returncode == 0, f"{label} failed ({result.returncode}); see its private log.")
            return text, result.returncode

        version, _ = command("sdk", ["dotnet", "--version"])
        require(version.strip() == CONTRACT["sdk"], "SDK version differs from the contract.")
        command("tool-restore", ["dotnet", "tool", "restore"])
        command("format", ["dotnet", "csharpier", "check", "."])
        solution = CONTRACT["solution"]
        projects = [p.get("Path") for p in ET.parse(ROOT / solution).findall(".//Project")]
        require(set(p.split("/")[1] for p in projects if p.startswith("tests/")) == set(CONTRACT["tests"]), "Test project inventory drift.")
        command("restore", ["dotnet", "restore", solution, "--locked-mode"])
        audit, _ = command("audit", ["dotnet", "list", solution, "package", "--vulnerable", "--include-transitive", "--format", "json", "--no-restore"])
        validate_audit(json.loads(audit), len(projects))
        command("ordinary-build", ["dotnet", "build", solution, "-c", "Release", "--no-restore", "--no-incremental", "--disable-build-servers", "-p:SonarQubeTargetsImported=true"])
        # The ordinary build is complete before scanner begin can replace analyzers
        # or disable WarningsAsErrors. Every run repeats this build without caching.
        command("playwright", ["pwsh", str(ROOT / "tests/AndreGoepel.AppFoundation.E2ETests/bin/Release/net10.0/playwright.ps1"), "install", "chromium"])
        reports = private / "reports"
        reports.mkdir()
        python_data = reports / "python-tests.coverage"
        command("runner-tests", [sys.executable, "-m", "coverage", "run", "--data-file=" + str(python_data),
                "--source=scripts/sonar", "-m", "unittest", "discover", "-s", "scripts/sonar", "-p", "test_*.py"])
        merged = reports / "coverage.xml"
        python_report = reports / "python-coverage.xml"
        settings = api(args.url, "settings/values", token, component=args.project, keys="sonar.coverage.exclusions")
        require(not any(s.get("value") or s.get("values") for s in settings.get("settings", [])), "Server coverage exclusions require review.")
        scanner_owned = True
        command("scanner-begin", ["dotnet", "sonarscanner", "begin", "/k:" + args.project,
                "/d:sonar.host.url=" + args.url, "/d:sonar.token=" + token, "/d:sonar.scanner.scanAll=true",
                "/d:sonar.scm.disabled=true", "/d:sonar.qualitygate.wait=true", "/d:sonar.qualitygate.timeout=300",
                "/d:sonar.exclusions=" + ",".join(CONTRACT["sourceExclusions"]),
                "/d:sonar.python.version=3.12,3.13,3.14",
                "/d:sonar.python.coverage.reportPaths=" + str(python_report),
                "/d:sonar.cs.vstest.reportsPaths=" + str(reports / "*" / "tests.trx"),
                "/d:sonar.cs.vscoveragexml.reportsPaths=" + str(merged)])
        command("analysis-build", ["dotnet", "build", solution, "-c", "Release", "--no-restore", "--no-incremental", "--disable-build-servers"])
        summary["analyzedProjects"] = validate_project_scope(scanner_dir, ROOT, projects)
        coverage_files = []
        verified_sources = {}
        expected_mapped, expected_executed = set(), set()
        for project, minimum in CONTRACT["tests"].items():
            test_args = ["dotnet", "test", "--project", f"tests/{project}/{project}.csproj", "-c", "Release", "--no-build", "--no-restore", "--no-ansi", "--no-progress"]
            discovery, _ = command(project + "-discovery", test_args + ["--list-tests"])
            names = discovery_names(discovery, minimum)
            test_dir = reports / project
            test_dir.mkdir()
            coverage = test_dir / "coverage.xml"
            test_args += ["--timeout", "10m", "--minimum-expected-tests", str(len(names)), "--report-xunit-xml",
                          "--report-xunit-xml-filename", "tests.xml", "--report-xunit-trx",
                          "--report-xunit-trx-filename", "tests.trx", "--results-directory", str(test_dir)]
            # Pass the executable and each argument separately; no shell or nested quoting.
            command(project, ["dotnet", "coverage", "collect", "-f", "xml", "-o", str(coverage), "--"] + test_args)
            count = validate_tests(test_dir / "tests.xml", started, names)
            anchors = CONTRACT["coverageAnchors"] if project.endswith("E2ETests") else ()
            evidence = validate_coverage(coverage, started, ROOT, anchors)
            verified_sources.update(evidence.pop("sourceChecksums"))
            expected_mapped.update(evidence["mappedSources"])
            expected_executed.update(evidence["executedSources"])
            summary["tests"][project] = {"discovered": len(names), "passed": count, **evidence}
            coverage_files.append(str(coverage))
        command("coverage-merge", ["dotnet", "coverage", "merge", *coverage_files, "-f", "xml", "-o", str(merged)])
        merged_evidence = validate_coverage(merged, started, ROOT, CONTRACT["coverageAnchors"], verified_sources)
        require(set(merged_evidence["mappedSources"]) == expected_mapped
                and set(merged_evidence["executedSources"]) == expected_executed, "Merged coverage differs from its validated inputs.")
        merged_evidence.pop("sourceChecksums")
        summary["coverage"] = merged_evidence
        runtime_coverage = python_coverage.Coverage.current()
        require(runtime_coverage is not None, "Python runner coverage was not started.")
        runtime_coverage.stop()
        test_data = python_coverage.CoverageData(basename=str(python_data))
        test_data.read()
        runtime_coverage.get_data().update(test_data)
        runtime_coverage.xml_report(outfile=str(python_report))
        python_xml = fresh_xml(python_report, started)
        require(int(python_xml.get("lines-covered", "0")) > 0, "Python coverage is empty.")
        summary["pythonCoverage"] = {"linesCovered": int(python_xml.get("lines-covered")), "linesValid": int(python_xml.get("lines-valid"))}
        require(source_snapshot(ROOT) == snapshot, "Sources changed during analysis; upload refused.")
        _, exit_code = command("scanner-end", ["dotnet", "sonarscanner", "end", "/d:sonar.token=" + token], allow_failure=True)
        task_file = scanner_dir / "out" / ".sonar" / "report-task.txt"
        require(task_file.is_file(), "Scanner did not produce a task receipt.")
        receipt = dict(line.split("=", 1) for line in task_file.read_text().splitlines() if "=" in line)
        task_id = receipt.get("ceTaskId")
        require(task_id and receipt.get("projectKey") == args.project, "Invalid task receipt.")
        task = api(args.url, "ce/task", token, id=task_id)["task"]
        require(task.get("status") == "SUCCESS" and task.get("componentKey") == args.project, "Analysis task did not succeed.")
        analysis_id = task["analysisId"]
        summary.update(taskId=task_id, analysisId=analysis_id, scannerExitCode=exit_code)
        latest = api(args.url, "project_analyses/search", verifier, project=args.project, ps=1)["analyses"]
        require(latest and latest[0]["key"] == analysis_id, "Another scan replaced this project's latest analysis.")
        gate = api(args.url, "qualitygates/project_status", verifier, analysisId=analysis_id)["projectStatus"]
        require(gate.get("conditions"), "An empty gate is not evidence.")
        assignment = api(args.url, "qualitygates/get_by_project", verifier, project=args.project)
        require(assignment.get("qualityGate", {}).get("name") == CONTRACT["gate"], "Unexpected quality gate.")
        measures = api(args.url, "measures/component", verifier, component=args.project,
                       metricKeys="coverage,lines_to_cover,uncovered_lines,ncloc,violations,security_hotspots,tests,test_success_density")["component"]["measures"]
        metrics = {m["metric"]: m.get("value") for m in measures}
        require(float(metrics.get("lines_to_cover", 0)) > 0 and "coverage" in metrics, "Coverage was not imported.")
        end_log = (private / "scanner-end.log").read_text()
        require(re.search(r"[1-9]\d* main files with coverage", end_log), "Scanner did not confirm mapped coverage import.")
        require(source_snapshot(ROOT) == snapshot, "Sources changed before verification completed.")
        summary.update(state="complete", taskId=task_id, analysisId=analysis_id, gate=gate, measures=metrics, scannerExitCode=exit_code)
        require(exit_code == 0 and gate["status"] == "OK", "Full analysis completed, but its quality gate failed.")
    finally:
        token = None
        verifier = None
        summary["finishedAt"] = time.time()
        if output_owned and (output / "publish").is_dir():
            (output / "publish" / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
        if scanner_owned and scanner_dir.is_dir():
            require(scanner_dir.resolve().parent == ROOT and not scanner_dir.is_symlink(), "Unsafe scanner cleanup path.")
            shutil.rmtree(scanner_dir)
        lock.rmdir()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:9000")
    parser.add_argument("--project", default="app-foundation-local")
    parser.add_argument("--server-id", required=True, help="Expected ID of the separate local Sonar server")
    parser.add_argument("--run-name", required=True, help="New, unique output directory name; reports cannot be reused")
    args = parser.parse_args()
    require(re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", args.run_name), "Invalid run name.")
    try:
        run(args)
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
