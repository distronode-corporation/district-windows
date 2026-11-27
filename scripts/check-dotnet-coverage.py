#!/usr/bin/env python3

# SPDX-FileCopyrightText: Distronode Corporation
#
# SPDX-License-Identifier: Apache-2.0

"""Hold the C# assemblies' line coverage to coverage-floors-dotnet.toml.

    python3 scripts/check-dotnet-coverage.py DIR [DIR ...]  # check the reports
    python3 scripts/check-dotnet-coverage.py --self-test    # prove each rule works

Each DIR is searched for the coverage.cobertura.xml files that
`dotnet test --collect:"XPlat Code Coverage"` (coverlet.collector) writes. Each
<package> in a report is one assembly. A line counts once per assembly (by its
file's full path and line number), as covered if any report ran it, so
DistrictAI.Core's number includes what the Presentation tests run of it.

coverage-floors-dotnet.toml holds one table per assembly:

    [assemblies."DistrictAI.Core"]
    floor = 60
    reason = "One line saying why the floor is where it is."

The check prints each assembly's line coverage and fails when an assembly is
under its floor (compared in whole lines), is missing from every report, has
zero lines while its floor is above 0, or has an entry without a whole-number
floor from 0 to 100 or without a reason. Floors only ratchet up, as in
coverage-floors.toml.

Python 3.11 or newer, standard library only.
"""

from __future__ import annotations

import sys
import tempfile
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FLOORS_FILE = ROOT / "coverage-floors-dotnet.toml"


def resolve(sources: list[str], filename: str) -> str:
    """The class's file as a full path. Cobertura names it relative to one of
    the report's <source> roots, and each report picks its own roots, so the
    same file reads differently in the Core and the Presentation reports."""
    for source in sources:
        if Path(source, filename).is_file():
            return str(Path(source, filename))
    return str(Path(sources[0], filename))


def read_reports(dirs: list[Path]) -> dict[str, dict[tuple[str, int], bool]]:
    """Per assembly, every (file, line) and whether any report ran it."""
    lines: dict[str, dict[tuple[str, int], bool]] = {}
    for d in dirs:
        for report in sorted(d.rglob("coverage.cobertura.xml")):
            root = ET.parse(report).getroot()
            sources = [s.text or "" for s in root.iter("source")] or [""]
            for package in root.iter("package"):
                seen = lines.setdefault(package.get("name", ""), {})
                for cls in package.iter("class"):
                    filename = resolve(sources, cls.get("filename", ""))
                    for line in cls.iter("line"):
                        key = (filename, int(line.get("number", "0")))
                        seen[key] = seen.get(key, False) or int(line.get("hits", "0")) > 0
    return lines


def check(floors_text: str, lines: dict[str, dict[tuple[str, int], bool]]) -> list[str]:
    """Print the table and return the failures."""
    failures: list[str] = []
    assemblies = tomllib.loads(floors_text).get("assemblies", {})
    if not assemblies:
        return ["the floors file lists no assembly"]
    for name, entry in sorted(assemblies.items()):
        floor = entry.get("floor")
        if not isinstance(floor, int) or isinstance(floor, bool) or not 0 <= floor <= 100:
            failures.append(f"{name}: floor must be a whole number from 0 to 100")
            continue
        if not str(entry.get("reason", "")).strip():
            failures.append(f"{name}: no reason")
        if name not in lines:
            failures.append(f"{name}: missing from every report")
            continue
        total = len(lines[name])
        covered = sum(lines[name].values())
        if total == 0:
            if floor > 0:
                failures.append(f"{name}: zero measurable lines")
            continue
        pct = 100 * covered / total
        print(f"{name}: {covered}/{total} lines, {pct:.2f}% (floor {floor})")
        if covered * 100 < floor * total:
            failures.append(f"{name}: {pct:.2f}% is below its floor of {floor}")
        elif int(pct) > floor:
            print(f"  note: the floor can be raised to {int(pct)}")
    return failures


REPORT = """<?xml version="1.0"?>
<coverage><packages><package name="{name}"><classes>
<class filename="a.cs"><lines>{lines}</lines></class>
</classes></package></packages></coverage>
"""


def self_test() -> int:
    floors = '[assemblies."A"]\nfloor = 50\nreason = "r"\n'
    cases = [
        ("at the floor", floors, "A", (1, 2), True),
        ("below the floor", floors, "A", (1, 3), False),
        ("missing", floors, "B", (1, 1), False),
        ("empty", floors, "A", (0, 0), False),
        ("no reason", '[assemblies."A"]\nfloor = 0\n', "A", (1, 1), False),
        ("bad floor", '[assemblies."A"]\nfloor = 101\nreason = "r"\n', "A", (1, 1), False),
        ("no entries", "", "A", (1, 1), False),
    ]
    bad = 0
    for label, floors_text, name, (hit, total), want_pass in cases:
        with tempfile.TemporaryDirectory() as tmp:
            body = "".join(
                f'<line number="{i}" hits="{1 if i <= hit else 0}"/>' for i in range(1, total + 1)
            )
            Path(tmp, "x").mkdir()
            Path(tmp, "x", "coverage.cobertura.xml").write_text(REPORT.format(name=name, lines=body))
            got_pass = not check(floors_text, read_reports([Path(tmp)]))
        if got_pass != want_pass:
            print(f"SELF-TEST FAIL: {label}")
            bad += 1
    # One file, two reports with different <source> roots: one line, covered once.
    with tempfile.TemporaryDirectory() as tmp:
        Path(tmp, "src", "A").mkdir(parents=True)
        Path(tmp, "src", "A", "a.cs").write_text("")
        for i, (source, filename, hits) in enumerate(
            [(f"{tmp}/src/", "A/a.cs", 0), (f"{tmp}/src/A/", "a.cs", 1)]
        ):
            Path(tmp, f"r{i}").mkdir()
            Path(tmp, f"r{i}", "coverage.cobertura.xml").write_text(
                REPORT.replace("<packages>", f"<sources><source>{source}</source></sources><packages>")
                .replace('filename="a.cs"', f'filename="{filename}"')
                .format(name="A", lines=f'<line number="1" hits="{hits}"/>')
            )
        merged = read_reports([Path(tmp)])["A"]
        if list(merged.values()) != [True]:
            print("SELF-TEST FAIL: one file under two source roots")
            bad += 1
    print("self-test:", "FAIL" if bad else f"all {len(cases) + 1} cases answer as expected")
    return 1 if bad else 0


def main(argv: list[str]) -> int:
    if argv == ["--self-test"]:
        return self_test()
    if not argv or any(a.startswith("-") for a in argv):
        print(__doc__)
        return 2
    failures = check(FLOORS_FILE.read_text(encoding="utf-8"), read_reports([Path(a) for a in argv]))
    for f in failures:
        print(f"::error::{f}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
