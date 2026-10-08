#!/usr/bin/env python3
"""Read a Windows App Certification Kit report and fail when the package failed.

    python scripts/check-wack-report.py <report.xml>   # judge a report
    python scripts/check-wack-report.py --self-test    # prove the reading works

scripts/run-wack.ps1 runs `appcert.exe test` and hands its report here. The
report's root element carries OVERALL_RESULT, and each <TEST> under a
<REQUIREMENT> carries a <RESULT> and the <MESSAGES> that explain it:

    <REPORT OVERALL_RESULT="FAIL" ...>
      <REQUIREMENTS>
        <REQUIREMENT NUMBER="1" TITLE="...">
          <TEST INDEX="1" NAME="..." DESCRIPTION="...">
            <RESULT><![CDATA[FAIL]]></RESULT>
            <MESSAGES><MESSAGE TEXT="..." /></MESSAGES>

The verdict is OVERALL_RESULT: FAIL fails, anything else passes. Every test
whose result is FAIL is printed with its messages either way, as an error when
the report failed and as a warning when it passed (an optional test can fail
without failing the report). A report with no OVERALL_RESULT or no tests is
treated as a failure, because it means the kit did not run, not that it passed.
"""

from __future__ import annotations

import sys
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Verdict:
    overall: str
    tests: int
    failed: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return self.tests > 0 and self.overall.upper() not in {"", "FAIL"}


def _attr(element: ET.Element, name: str) -> str:
    for key, value in element.attrib.items():
        if key.upper() == name:
            return value.strip()
    return ""


def _children(element: ET.Element, tag: str) -> list[ET.Element]:
    return [e for e in element.iter() if e.tag.upper() == tag]


def read(path: Path) -> Verdict:
    root = ET.parse(path).getroot()
    verdict = Verdict(overall=_attr(root, "OVERALL_RESULT"), tests=0)
    for requirement in _children(root, "REQUIREMENT"):
        title = _attr(requirement, "TITLE")
        for test in _children(requirement, "TEST"):
            verdict.tests += 1
            result = next((r.text or "" for r in _children(test, "RESULT")), "").strip()
            if result.upper() != "FAIL":
                continue
            messages = [
                _attr(m, "TEXT") or (m.text or "").strip()
                for m in _children(test, "MESSAGE")
            ]
            line = f"{title} / {_attr(test, 'NAME')}"
            for message in messages:
                if message:
                    line += f"\n    {message}"
            verdict.failed.append(line)
    return verdict


def report(verdict: Verdict) -> int:
    level = "warning" if verdict.ok else "error"
    for failed in verdict.failed:
        first, _, rest = failed.partition("\n")
        print(f"::{level}::WACK test failed: {first}")
        if rest:
            print(rest)
    print(f"WACK: OVERALL_RESULT={verdict.overall or '(missing)'}, {verdict.tests} tests, {len(verdict.failed)} failed")
    if verdict.tests == 0:
        print("::error::the WACK report holds no tests; the kit did not run")
    elif not verdict.overall:
        print("::error::the WACK report has no OVERALL_RESULT")
    return 0 if verdict.ok else 1


def self_test() -> int:
    def doc(overall: str | None, results: list[str]) -> str:
        tests = "".join(
            f'<TEST INDEX="{i}" NAME="Test {i}" DESCRIPTION="d"><RESULT><![CDATA[{r}]]></RESULT>'
            f'<MESSAGES><MESSAGE TEXT="why {i}" /></MESSAGES></TEST>'
            for i, r in enumerate(results, start=1)
        )
        head = f' OVERALL_RESULT="{overall}"' if overall is not None else ""
        return (
            f'<?xml version="1.0" encoding="utf-8"?><REPORT{head} VERSION="10.0">'
            f'<REQUIREMENTS><REQUIREMENT NUMBER="1" TITLE="Req">{tests}</REQUIREMENT></REQUIREMENTS></REPORT>'
        )

    cases = [
        ("pass", doc("PASS", ["PASS", "PASS"]), True, 0),
        ("fail", doc("FAIL", ["PASS", "FAIL", "FAIL"]), False, 2),
        ("optional failure, overall pass", doc("PASS", ["FAIL", "PASS"]), True, 1),
        ("lower case fail", doc("fail", ["fail"]), False, 1),
        ("no overall result", doc(None, ["PASS"]), False, 0),
        ("no tests", doc("PASS", []), False, 0),
    ]
    failed = 0
    with tempfile.TemporaryDirectory() as tmp:
        for name, text, ok, n_failed in cases:
            path = Path(tmp, "r.xml")
            path.write_text(text, encoding="utf-8")
            verdict = read(path)
            if verdict.ok != ok or len(verdict.failed) != n_failed:
                failed += 1
                print(f"self-test: {name}: ok={verdict.ok} failed={len(verdict.failed)}, expected ok={ok} failed={n_failed}")
        path = Path(tmp, "r.xml")
        path.write_text(doc("FAIL", ["FAIL"]), encoding="utf-8")
        if "why 1" not in read(path).failed[0]:
            failed += 1
            print("self-test: a failed test's message should be printed")
    if failed:
        return 1
    print(f"self-test passed: {len(cases) + 1} cases")
    return 0


def main() -> int:
    args = sys.argv[1:]
    if args == ["--self-test"]:
        return self_test()
    if len(args) != 1:
        print(__doc__)
        return 2
    return report(read(Path(args[0])))


if __name__ == "__main__":
    sys.exit(main())
