"""Run with: python -m unittest discover -s scripts/sonar -p 'test_*.py'."""

import hashlib
import os
from pathlib import Path
import tempfile
import time
import unittest
import xml.etree.ElementTree as ET

import analyze


class ValidationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        self.started = time.time() - 1

    def coverage(self, source_path=None, checksum=None, covered="yes"):
        project = self.root / "src" / "Example"
        project.mkdir(exist_ok=True, parents=True)
        (project / "Example.csproj").write_text("<Project />")
        source = project / "Example.cs"
        source.write_text("class Example {}\n")
        document = ET.Element("results")
        module = ET.SubElement(ET.SubElement(document, "modules"), "module", name="Example")
        sources = ET.SubElement(module, "source_files")
        ET.SubElement(sources, "source_file", id="1", path=str(source_path or source), checksum_type="SHA256",
                      checksum=checksum or hashlib.sha256(source.read_bytes()).hexdigest())
        ranges = ET.SubElement(ET.SubElement(ET.SubElement(module, "functions"), "function"), "ranges")
        ET.SubElement(ranges, "range", source_id="1", covered=covered)
        report = self.root / "coverage.xml"
        ET.ElementTree(document).write(report)
        return report

    def results(self, names, outcome="Pass"):
        document = ET.Element("assemblies")
        assembly = ET.SubElement(document, "assembly", errors="0", total=str(len(names)))
        collection = ET.SubElement(assembly, "collection")
        for name in names:
            ET.SubElement(collection, "test", name=name, result=outcome)
        report = self.root / "tests.xml"
        ET.ElementTree(document).write(report)
        return report

    def test_target_central_or_redirect_capable_url_rejected(self):
        # Arrange / Act / Assert
        for url in ["https://sonar.andregoepel.dev", "http://127.0.0.1:9000/path", "http://user@localhost:9000",
                    "http://localhost:9000?remote=true", "http://example.com:9000", "https://localhost:9000"]:
            with self.subTest(url=url), self.assertRaises(ValueError):
                analyze.validate_target(url, "app-foundation-local", "local-id")

    def test_target_central_key_or_identity_rejected(self):
        # Arrange / Act / Assert
        for project, server_id in [("app-foundation", "local-id"), ("app-foundation-local", "AA2DD71C-AaFDycIuQLOXJXX6YWNZ")]:
            with self.subTest(project=project), self.assertRaises(ValueError):
                analyze.validate_target("http://127.0.0.1:9000", project, server_id)

    def test_discovery_missing_or_duplicate_tests_rejected(self):
        # Arrange / Act / Assert
        for output in ["Discovered 0 tests.\n", "  AndreGoepel.Test\nDiscovered 2 tests.\n",
                       "  AndreGoepel.Test\n  AndreGoepel.Test\nDiscovered 2 tests.\n"]:
            with self.subTest(output=output), self.assertRaises(ValueError):
                analyze.discovery_names(output, 1)

    def test_results_missing_different_or_skipped_tests_rejected(self):
        # Arrange / Act / Assert
        for names, outcome in [([], "Pass"), (["other"], "Pass"), (["expected"], "Skip")]:
            with self.subTest(names=names), self.assertRaises(ValueError):
                analyze.validate_tests(self.results(names, outcome), self.started, ["expected"])

    def test_results_complete_execution_accepted(self):
        # Arrange
        report = self.results(["one", "two"])
        # Act / Assert
        self.assertEqual(2, analyze.validate_tests(report, self.started, ["two", "one"]))

    def test_results_xunit_escaped_theory_name_matches_discovery(self):
        # Arrange
        report = self.results(['Test(value: \\"example\\")'])
        # Act / Assert
        self.assertEqual(1, analyze.validate_tests(report, self.started, ['Test(value: "example")']))

    def test_coverage_empty_missing_or_stale_rejected(self):
        # Arrange
        report = self.root / "missing.xml"
        # Act / Assert
        with self.assertRaises(ValueError):
            analyze.validate_coverage(report, self.started, self.root)
        report.write_text("<results />")
        with self.assertRaises(ValueError):
            analyze.validate_coverage(report, self.started, self.root)
        report = self.coverage()
        os.utime(report, (self.started - 10, self.started - 10))
        with self.assertRaises(ValueError):
            analyze.validate_coverage(report, self.started, self.root)

    def test_coverage_wrong_checkout_or_checksum_rejected(self):
        # Arrange / Act / Assert
        for options in [{"source_path": self.root.parent / "other.cs"}, {"checksum": "0" * 64}]:
            with self.subTest(options=options), self.assertRaises(ValueError):
                analyze.validate_coverage(self.coverage(**options), self.started, self.root)

    def test_coverage_missing_child_process_anchor_rejected(self):
        # Arrange
        report = self.coverage()
        # Act / Assert
        with self.assertRaises(ValueError):
            analyze.validate_coverage(report, self.started, self.root, ["samples/AppHost/Program.cs"])

    def test_coverage_matching_sources_and_anchor_accepted(self):
        # Arrange
        report = self.coverage()
        # Act
        result = analyze.validate_coverage(report, self.started, self.root, ["src/Example/Example.cs"])
        # Assert
        self.assertEqual(["src/Example/Example.cs"], result["executedSources"])

    def test_merged_coverage_without_checksum_requires_verified_inputs(self):
        # Arrange
        report = self.coverage()
        verified = analyze.validate_coverage(report, self.started, self.root)["sourceChecksums"]
        document = ET.parse(report)
        source = document.find(".//source_file")
        del source.attrib["checksum_type"]
        del source.attrib["checksum"]
        document.write(report)
        # Act / Assert
        with self.assertRaises(ValueError):
            analyze.validate_coverage(report, self.started, self.root)
        result = analyze.validate_coverage(report, self.started, self.root, verified_sources=verified)
        self.assertEqual(["src/Example/Example.cs"], result["executedSources"])

    def test_source_snapshot_changes_when_authored_content_changes(self):
        # Arrange
        source = self.root / "source.cs"
        source.write_text("before")
        before = analyze.source_snapshot(self.root)
        # Act
        source.write_text("after")
        # Assert
        self.assertNotEqual(before, analyze.source_snapshot(self.root))

    def test_audit_vulnerable_or_incomplete_projects_rejected(self):
        # Arrange / Act / Assert
        for data in [{"projects": []}, {"projects": [{"frameworks": [{"transitivePackages": [{"vulnerabilities": ["CVE"]}]}]}]}]:
            with self.subTest(data=data), self.assertRaises(ValueError):
                analyze.validate_audit(data, 1)


if __name__ == "__main__":
    unittest.main()
