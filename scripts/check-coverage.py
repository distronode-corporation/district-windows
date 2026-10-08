#!/usr/bin/env python3
"""Hold every workspace crate's line coverage to the floor coverage-floors.toml
sets for it.

    python3 scripts/check-coverage.py target/coverage.json  # check a report
    python3 scripts/check-coverage.py --feature district-call/livekit report.json
    python3 scripts/check-coverage.py --self-test           # prove each rule works

The report is the JSON that `cargo llvm-cov --json` writes, with or without
`--summary-only`; only each file's line summary is read. A file counts toward a
crate when it sits under that crate's `src/` directory, so the integration tests
under `tests/` are never measured. Unit tests written inside `src/` are, because
stable Rust has no way to leave them out.

coverage-floors.toml holds one table per workspace crate:

    [crates.district-model]
    floor = 100
    reason = "One line saying why the floor is where it is."

A crate whose optional feature builds code the default build does not (and so
code the `rust` job never measures) has a second table for that build, keyed by
`<crate>/<feature>`, with the same two keys:

    [features."district-call/livekit"]
    floor = 97
    reason = "One line saying why the floor is where it is."

`--feature <crate>/<feature>` checks a report made with that feature on (CI's
voice.yml makes it) against that table, for that crate alone: the other crates
in such a report are the ones the `rust` job already holds to their own floors.
Without `--feature`, the features tables are only checked for being well formed.

The check prints a table of every crate, then fails if any of these holds:

    below     A crate's line coverage is under its floor. Compared in whole
              lines, not rounded percentages: a floor of 100 means every line,
              and 99.99% is below it.
    missing   A workspace crate has no file in the report at all, whatever its
              floor. A crate the run left out was not measured at 0%; it was
              not measured.
    empty     A crate's files hold zero measurable lines while its floor is
              above 0. An empty report must never read as 100%.
    unlisted  A workspace member (from Cargo.toml) has no entry in the floors
              file.
    unknown   The floors file names a crate that is not a workspace member, or a
              features table is not keyed `<member>/<feature>`, or
              `--feature` names a table the file does not have.
    reason    An entry has no reason, or a blank one.
    floor     An entry's floor is not a whole number from 0 to 100.
    key       An entry, or the file, has a key this script does not know, which
              is usually a misspelling.
    report    The report is not a `cargo llvm-cov` JSON export.

A crate above its floor by a whole point or more gets a note saying what the
floor can be raised to. That is advice, not a failure: raising the floor is part
of the change that raised the coverage, and a reviewer asks for it.

`--self-test` runs each rule against planted reports and floors files, together
with look-alikes that must pass, and fails unless every case answers as
expected. It also builds a throwaway workspace on disk to prove the files are
found and read the way CI reads them. CI runs it in the `repo` job; the real
check runs in the `rust` job, straight after the tests that produce the report.
"""

from __future__ import annotations

import json
import sys
import tempfile
import tomllib
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent
FLOORS_FILE = "coverage-floors.toml"
EXPORT_TYPE = "llvm.coverage.json.export"
ENTRY_KEYS = {"floor", "reason"}


class ReportError(Exception):
    """The report cannot be read as a `cargo llvm-cov` JSON export."""


@dataclass(frozen=True)
class Floor:
    # None when the entry's floor is invalid; the entry still counts as present,
    # so a bad value is reported once, as itself, and not again as "unlisted".
    floor: int | None
    reason: str


@dataclass
class Tally:
    files: int = 0
    lines: int = 0
    covered: int = 0


@dataclass(frozen=True)
class Finding:
    rule: str
    message: str


@dataclass(frozen=True)
class Row:
    crate: str
    tally: Tally | None
    floor: int | None
    result: str


def load_toml(path: Path) -> dict:
    with path.open("rb") as fh:
        return tomllib.load(fh)


def workspace_members(root: Path) -> dict[str, str]:
    """Each member's directory, relative to the root, mapped to its package name.

    The package name comes from the member's own manifest, so a directory named
    differently from its package is still matched to the right floor.
    """
    members: dict[str, str] = {}
    for member in load_toml(root / "Cargo.toml")["workspace"]["members"]:
        directory = PurePosixPath(member).as_posix().rstrip("/")
        members[directory] = load_toml(root / directory / "Cargo.toml")["package"]["name"]
    return members


def read_floors(data: dict) -> tuple[dict[str, Floor], list[Finding]]:
    """Parse the floors file's contents. Every problem is a finding, not an
    exception, so one run reports all of them."""
    findings: list[Finding] = []
    floors: dict[str, Floor] = {}

    for key in sorted(set(data) - {"crates", "features"}):
        findings.append(
            Finding(
                "key",
                f"{FLOORS_FILE}: unknown top-level key {key!r}; every entry is a [crates.<name>] or "
                '[features."<crate>/<feature>"] table',
            )
        )
    entries: list[tuple[str, object]] = []
    for table in ("crates", "features"):
        section = data.get(table, {})
        if not isinstance(section, dict):
            findings.append(Finding("key", f"{FLOORS_FILE}: `{table}` must be a table of [{table}.<name>] entries"))
            continue
        for name, entry in section.items():
            # A feature's floor is keyed <crate>/<feature>, which is what keeps
            # it apart from its crate's own.
            if table == "features" and "/" not in name:
                findings.append(
                    Finding("unknown", f'{FLOORS_FILE}: [features."{name}"] is not <workspace member>/<feature>')
                )
                continue
            entries.append((name, entry))

    for name, entry in entries:
        if not isinstance(entry, dict):
            findings.append(Finding("key", f"{name}: the entry must be a table with `floor` and `reason`"))
            floors[name] = Floor(None, "")
            continue
        for key in sorted(set(entry) - ENTRY_KEYS):
            findings.append(Finding("key", f"{name}: unknown key {key!r} (the keys are `floor` and `reason`)"))

        floor = entry.get("floor")
        # bool is a subclass of int in Python, and `floor = true` is not a number.
        if isinstance(floor, bool) or not isinstance(floor, int) or not 0 <= floor <= 100:
            findings.append(Finding("floor", f"{name}: the floor must be a whole number from 0 to 100, not {floor!r}"))
            floor = None

        reason = entry.get("reason")
        if not isinstance(reason, str) or not reason.strip():
            findings.append(Finding("reason", f"{name}: the floor has no reason; say in one line why it is where it is"))
            reason = ""

        floors[name] = Floor(floor, reason.strip())
    return floors, findings


def relative_path(filename: str, roots: list[PurePosixPath]) -> PurePosixPath | None:
    """The report's path for a file, relative to the workspace root, or None for
    a file outside it (a dependency, or a report made in another checkout).

    llvm-cov writes absolute paths unless the build remapped them
    (`--remap-path-prefix`), in which case they are already relative.
    """
    path = PurePosixPath(filename)
    if not path.is_absolute():
        return path
    for root in roots:
        if path.is_relative_to(root):
            return path.relative_to(root)
    return None


def measure(report: object, root: Path, members: dict[str, str]) -> tuple[dict[str, Tally], int]:
    """Line totals per crate, and the number of files the report holds that
    belong to no crate's `src/`."""
    if not isinstance(report, dict) or report.get("type") != EXPORT_TYPE or not isinstance(report.get("data"), list):
        raise ReportError(f"not a `cargo llvm-cov --json` export (expected \"type\": \"{EXPORT_TYPE}\" and a data list)")

    # The checkout as written and with symlinks resolved, because the paths in
    # the report are whatever cargo was given.
    roots = sorted({PurePosixPath(root.absolute().as_posix()), PurePosixPath(root.resolve().as_posix())})
    sources = {PurePosixPath(directory) / "src": name for directory, name in members.items()}
    tallies = {name: Tally() for name in members.values()}
    elsewhere = 0

    try:
        for export in report["data"]:
            for entry in export["files"]:
                lines = entry["summary"]["lines"]
                count, covered = lines["count"], lines["covered"]
                if not (isinstance(count, int) and isinstance(covered, int) and 0 <= covered <= count):
                    raise ReportError(f"{entry['filename']}: line counts {covered!r} of {count!r} make no sense")
                path = relative_path(entry["filename"], roots)
                crate = None
                if path is not None:
                    crate = next((name for src, name in sources.items() if path.is_relative_to(src)), None)
                if crate is None:
                    elsewhere += 1
                    continue
                tally = tallies[crate]
                tally.files += 1
                tally.lines += count
                tally.covered += covered
    except (KeyError, TypeError) as error:
        raise ReportError(f"a file entry is missing its line summary ({error!r})") from error
    return tallies, elsewhere


def check(
    members: dict[str, str],
    floors: dict[str, Floor],
    tallies: dict[str, Tally],
) -> tuple[list[Row], list[Finding], list[str]]:
    """Apply the rules. Returns the table rows, the failures, and the notes."""
    rows: list[Row] = []
    findings: list[Finding] = []
    notes: list[str] = []

    for name in members.values():
        tally = tallies.get(name, Tally())
        entry = floors.get(name)
        floor = entry.floor if entry else None

        if entry is None:
            result = "NO FLOOR"
            findings.append(Finding("unlisted", f"{name}: a workspace member with no entry in {FLOORS_FILE}"))
        elif tally.files == 0:
            result = "MISSING"
            findings.append(
                Finding(
                    "missing",
                    f"{name}: not in the report. No file under its src/ was measured, so either the run "
                    "left the crate out (--exclude, -p) or the crate has no code to measure",
                )
            )
        elif floor is None:
            result = "BAD ENTRY"
        elif tally.lines == 0 and floor > 0:
            result = "EMPTY"
            findings.append(
                Finding("empty", f"{name}: zero measurable lines against a floor of {floor}; an empty report is not 100%")
            )
        elif tally.covered * 100 < floor * tally.lines:
            result = "BELOW"
            findings.append(
                Finding(
                    "below",
                    f"{name}: {percent(tally)} of lines ({tally.covered} of {tally.lines}) is under its floor of "
                    f"{floor}%; {uncovered_to_floor(tally, floor)} more line(s) must be covered",
                )
            )
        else:
            result = "ok"
            whole = tally.covered * 100 // tally.lines if tally.lines else 0
            if whole > floor:
                notes.append(f"{name}: at {percent(tally)}, above its floor of {floor}. The floor can be raised to {whole}.")

        rows.append(Row(name, tally if tally.files else None, floor, result))

    for name in sorted(set(floors) - set(members.values())):
        crate, slash, feature = name.partition("/")
        if not slash:
            findings.append(Finding("unknown", f"{FLOORS_FILE}: [crates.{name}] names no workspace member"))
        elif crate not in members.values() or not feature:
            findings.append(
                Finding("unknown", f'{FLOORS_FILE}: [features."{name}"] is not <workspace member>/<feature>')
            )

    return rows, findings, notes


def check_feature(
    members: dict[str, str],
    floors: dict[str, Floor],
    tallies: dict[str, Tally],
    feature: str,
) -> tuple[list[Row], list[Finding], list[str]]:
    """The rules for one crate measured with one feature on, against its
    [features."<crate>/<feature>"] floor. The rest of the report is not judged."""
    crate = feature.partition("/")[0]
    entry = floors.get(feature)
    if "/" not in feature or entry is None or crate not in members.values():
        return [], [Finding("unknown", f'{FLOORS_FILE}: there is no [features."{feature}"] table to check against')], []
    rows, findings, notes = check({"feature": crate}, {crate: entry}, {crate: tallies.get(crate, Tally())})
    rows = [Row(feature, row.tally, row.floor, row.result) for row in rows]
    return rows, findings, notes


def percent(tally: Tally) -> str:
    """Truncated, never rounded, so a crate below 100% never prints as 100.00%."""
    if tally.lines == 0:
        return "-"
    hundredths = tally.covered * 10000 // tally.lines
    return f"{hundredths // 100}.{hundredths % 100:02d}%"


def uncovered_to_floor(tally: Tally, floor: int) -> int:
    """The fewest extra covered lines that would bring the crate up to its floor."""
    return -(-(floor * tally.lines - tally.covered * 100) // 100)


def table(rows: list[Row]) -> str:
    header = ("crate", "files", "lines", "covered", "coverage", "floor", "result")
    body = [
        (
            row.crate,
            str(row.tally.files) if row.tally else "-",
            str(row.tally.lines) if row.tally else "-",
            str(row.tally.covered) if row.tally else "-",
            percent(row.tally) if row.tally else "-",
            "-" if row.floor is None else f"{row.floor}%",
            row.result,
        )
        for row in rows
    ]
    widths = [max(len(line[i]) for line in [header, *body]) for i in range(len(header))]

    def render(cells: tuple[str, ...]) -> str:
        first = cells[0].ljust(widths[0])
        middle = [cell.rjust(width) for cell, width in zip(cells[1:-1], widths[1:-1])]
        return "  " + "  ".join([first, *middle, cells[-1]]).rstrip()

    return "\n".join(render(line) for line in [header, *body])


def run(report_path: Path, root: Path, feature: str | None = None) -> int:
    """The whole check against one report, printing as it goes. 0 passes.
    With `feature`, only that crate's build with that feature is checked."""
    members = workspace_members(root)
    floors_path = root / FLOORS_FILE
    try:
        floors, findings = read_floors(load_toml(floors_path))
    except (OSError, tomllib.TOMLDecodeError) as error:
        print(f"ERROR: cannot read {FLOORS_FILE}: {error}", file=sys.stderr)
        return 1
    try:
        with report_path.open(encoding="utf-8") as fh:
            tallies, elsewhere = measure(json.load(fh), root, members)
    except (OSError, ValueError, ReportError) as error:
        print(f"ERROR: cannot read the report {report_path}: {error}", file=sys.stderr)
        return 1

    if feature is None:
        rows, failures, notes = check(members, floors, tallies)
    else:
        # The rest of the file is still well formed or not, whichever build
        # the report is of.
        findings.extend(f for f in check(members, floors, tallies)[1] if f.rule in {"unlisted", "unknown"})
        rows, failures, notes = check_feature(members, floors, tallies, feature)
    findings.extend(failures)

    print(table(rows))
    print(f"\n  {elsewhere} file(s) in the report belong to no crate's src/ and are not counted")
    for note in notes:
        print(f"  note: {note}")

    if findings:
        # The table first, then the errors, even when the two streams are piped
        # into one log and stdout is block-buffered.
        sys.stdout.flush()
        print("", file=sys.stderr)
        for finding in findings:
            print(f"ERROR: [{finding.rule}] {finding.message}", file=sys.stderr)
        print(f"\ncoverage check FAILED: {len(findings)} problem(s)", file=sys.stderr)
        return 1
    if feature is None:
        print(f"\nline coverage meets every floor in {FLOORS_FILE} ({len(rows)} crates)")
    else:
        print(f'\nline coverage meets the floor of [features."{feature}"] in {FLOORS_FILE}')
    return 0


def self_test() -> int:
    members = {
        "crates/alpha": "alpha",
        "crates/beta": "beta",
        "crates/gamma": "gamma",
    }
    root = Path("/work/tree")

    def report(*files: tuple[str, int, int]) -> dict:
        """A report in llvm-cov's shape: (path, lines, covered) per file."""
        return {
            "type": EXPORT_TYPE,
            "data": [
                {"files": [{"filename": f, "summary": {"lines": {"count": n, "covered": c}}} for f, n, c in files]}
            ],
        }

    def floors(**entries: dict) -> dict:
        return {"crates": entries}

    full = {"floor": 100, "reason": "r"}
    measured = {"floor": 80, "reason": "r"}
    zero = {"floor": 0, "reason": "r"}
    good_floors = floors(alpha=full, beta=measured, gamma=zero)
    good_files = [
        ("/work/tree/crates/alpha/src/lib.rs", 10, 10),
        ("/work/tree/crates/alpha/src/nested/deep.rs", 5, 5),
        ("/work/tree/crates/beta/src/lib.rs", 5, 4),
        ("/work/tree/crates/gamma/src/main.rs", 20, 0),
    ]

    # (name, floors file contents, report, the rules expected to fail)
    cases: list[tuple[str, dict, object, set[str]]] = [
        ("every crate at or above its floor", good_floors, report(*good_files), set()),
        (
            "look-alikes: tests/, a src-prefixed directory, a file outside the tree, a remapped path",
            good_floors,
            report(
                *good_files,
                ("/work/tree/crates/alpha/tests/it.rs", 50, 0),
                ("/work/tree/crates/alpha/src-gen/x.rs", 50, 0),
                ("/home/user/.cargo/registry/src/crates/alpha/src/lib.rs", 50, 0),
                ("crates/beta/src/remapped.rs", 5, 5),
            ),
            set(),
        ),
        (
            "a floor of 0 with zero measurable lines",
            good_floors,
            report(*good_files[:3], ("/work/tree/crates/gamma/src/main.rs", 0, 0)),
            set(),
        ),
        (
            "one uncovered line under a floor of 100",
            good_floors,
            report(("/work/tree/crates/alpha/src/lib.rs", 10000, 9999), *good_files[2:]),
            {"below"},
        ),
        (
            "a measured floor missed by one line",
            good_floors,
            report(*good_files[:2], ("/work/tree/crates/beta/src/lib.rs", 100, 79), good_files[3]),
            {"below"},
        ),
        ("a member missing from the report, floor 0", good_floors, report(*good_files[:3]), {"missing"}),
        ("a member missing from the report, floor 100", good_floors, report(*good_files[2:]), {"missing"}),
        ("an empty report", good_floors, report(), {"missing"}),
        (
            "zero measurable lines under a floor above 0",
            good_floors,
            report(("/work/tree/crates/alpha/src/lib.rs", 0, 0), *good_files[2:]),
            {"empty"},
        ),
        ("a member with no entry", floors(alpha=full, beta=measured), report(*good_files), {"unlisted"}),
        (
            "an entry for a crate that is not a member",
            floors(alpha=full, beta=measured, gamma=zero, delta=full),
            report(*good_files),
            {"unknown"},
        ),
        (
            "an entry with no reason",
            floors(alpha={"floor": 100}, beta=measured, gamma=zero),
            report(*good_files),
            {"reason"},
        ),
        (
            "an entry with a blank reason",
            floors(alpha={"floor": 100, "reason": "  "}, beta=measured, gamma=zero),
            report(*good_files),
            {"reason"},
        ),
        (
            "a floor above 100",
            floors(alpha={"floor": 101, "reason": "r"}, beta=measured, gamma=zero),
            report(*good_files),
            {"floor"},
        ),
        (
            "a fractional floor",
            floors(alpha={"floor": 99.5, "reason": "r"}, beta=measured, gamma=zero),
            report(*good_files),
            {"floor"},
        ),
        (
            "a floor written as a string",
            floors(alpha={"floor": "100", "reason": "r"}, beta=measured, gamma=zero),
            report(*good_files),
            {"floor"},
        ),
        (
            "a floor written as a boolean",
            floors(alpha={"floor": True, "reason": "r"}, beta=measured, gamma=zero),
            report(*good_files),
            {"floor"},
        ),
        (
            "a misspelt reason",
            floors(alpha={"floor": 100, "reson": "r"}, beta=measured, gamma=zero),
            report(*good_files),
            {"key", "reason"},
        ),
        (
            "an entry outside the crates table",
            {**good_floors, "alpha": full},
            report(*good_files),
            {"key"},
        ),
        ("JSON that is not an llvm-cov export", good_floors, {"data": []}, {"report"}),
        (
            "a file entry with no line summary",
            good_floors,
            {"type": EXPORT_TYPE, "data": [{"files": [{"filename": "/work/tree/crates/alpha/src/lib.rs"}]}]},
            {"report"},
        ),
    ]

    extra = {"floor": 90, "reason": "r"}
    with_extra = {**good_floors, "features": {"alpha/extra": extra}}
    cases += [
        ("a feature's own floor beside the crates'", with_extra, report(*good_files), set()),
        (
            "a feature table for a crate that is not a member",
            {**good_floors, "features": {"delta/extra": extra}},
            report(*good_files),
            {"unknown"},
        ),
        (
            "a feature table named like a crate, which must not stand in for the crate's own",
            {**good_floors, "features": {"alpha": {"floor": 0, "reason": "r"}}},
            report(("/work/tree/crates/alpha/src/lib.rs", 10, 0), *good_files[2:]),
            {"unknown", "below"},
        ),
        (
            "a feature table with no feature after its crate",
            {**good_floors, "features": {"alpha/": extra}},
            report(*good_files),
            {"unknown"},
        ),
        (
            "a feature table with no reason",
            {**good_floors, "features": {"alpha/extra": {"floor": 90}}},
            report(*good_files),
            {"reason"},
        ),
    ]
    # (name, floors file contents, report, the feature checked, the rules expected to fail)
    feature_cases: list[tuple[str, dict, object, str, set[str]]] = [
        (
            "a feature build at its floor, other crates in the report not judged",
            with_extra,
            report(("/work/tree/crates/alpha/src/lib.rs", 10, 9), ("/work/tree/crates/beta/src/lib.rs", 10, 0)),
            "alpha/extra",
            set(),
        ),
        (
            "a feature build under its floor",
            with_extra,
            report(("/work/tree/crates/alpha/src/lib.rs", 10, 8)),
            "alpha/extra",
            {"below"},
        ),
        (
            "a feature build whose crate is not in the report",
            with_extra,
            report(("/work/tree/crates/beta/src/lib.rs", 10, 10)),
            "alpha/extra",
            {"missing"},
        ),
        ("a feature with no table", with_extra, report(*good_files), "alpha/other", {"unknown"}),
        ("a feature named without its crate", with_extra, report(*good_files), "extra", {"unknown"}),
    ]

    failures = 0
    for name, floors_data, report_data, expected in cases:
        parsed, found = read_floors(floors_data)
        try:
            tallies, _ = measure(report_data, root, members)
        except ReportError:
            found.append(Finding("report", ""))
        else:
            found.extend(check(members, parsed, tallies)[1])
        got = {f.rule for f in found}
        ok = got == expected
        failures += not ok
        print(f"  {'pass' if ok else 'FAIL'}  {name}: expected {sorted(expected)}, got {sorted(got)}")
    for name, floors_data, report_data, feature, expected in feature_cases:
        parsed, found = read_floors(floors_data)
        tallies, _ = measure(report_data, root, members)
        found.extend(check_feature(members, parsed, tallies, feature)[1])
        got = {f.rule for f in found}
        ok = got == expected
        failures += not ok
        print(f"  {'pass' if ok else 'FAIL'}  {name}: expected {sorted(expected)}, got {sorted(got)}")

    # The files themselves: members are read from Cargo.toml, a package name that
    # differs from its directory is honoured, and the exit status follows the rules.
    with tempfile.TemporaryDirectory() as tmp:
        tree = Path(tmp)
        (tree / "Cargo.toml").write_text('[workspace]\nmembers = ["crates/one", "crates/two"]\n', encoding="utf-8")
        for directory, package in (("one", "pkg-one"), ("two", "pkg-two")):
            (tree / "crates" / directory / "src").mkdir(parents=True)
            (tree / "crates" / directory / "Cargo.toml").write_text(
                f'[package]\nname = "{package}"\n', encoding="utf-8"
            )
        (tree / FLOORS_FILE).write_text(
            '[crates.pkg-one]\nfloor = 100\nreason = "r"\n\n[crates.pkg-two]\nfloor = 50\nreason = "r"\n',
            encoding="utf-8",
        )
        absolute = tree.resolve().as_posix()
        passing = tree / "passing.json"
        passing.write_text(
            json.dumps(
                report((f"{absolute}/crates/one/src/lib.rs", 4, 4), (f"{absolute}/crates/two/src/lib.rs", 4, 2))
            ),
            encoding="utf-8",
        )
        failing = tree / "failing.json"
        failing.write_text(json.dumps(report((f"{absolute}/crates/one/src/lib.rs", 4, 3))), encoding="utf-8")
        garbled = tree / "garbled.json"
        garbled.write_text("{ not json", encoding="utf-8")

        checks = [
            ("a passing report on disk exits 0", passing, 0),
            ("a failing report on disk exits 1", failing, 1),
            ("an unreadable report exits 1", garbled, 1),
            ("a report that does not exist exits 1", tree / "absent.json", 1),
        ]
        for name, path, expected_status in checks:
            # The table and the errors are the check's own output; here only the
            # exit status matters, so both streams are captured and dropped.
            saved = sys.stdout, sys.stderr
            with open(tree / "out.txt", "w", encoding="utf-8") as sink:
                sys.stdout = sys.stderr = sink
                try:
                    status = run(path, tree)
                finally:
                    sys.stdout, sys.stderr = saved
            ok = status == expected_status
            failures += not ok
            print(f"  {'pass' if ok else 'FAIL'}  {name}: expected {expected_status}, got {status}")

    total = len(cases) + len(feature_cases) + len(checks)
    if failures:
        print(f"\nself-test FAILED: {failures} of {total} case(s) did not answer as expected", file=sys.stderr)
        return 1
    print(f"\nself-test passed: {total} cases")
    return 0


def main(argv: list[str]) -> int:
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) == 4 and argv[1] == "--feature" and not argv[3].startswith("-"):
        return run(Path(argv[3]), ROOT, argv[2])
    if len(argv) != 2 or argv[1].startswith("-"):
        print(__doc__, file=sys.stderr)
        return 2
    return run(Path(argv[1]), ROOT)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
