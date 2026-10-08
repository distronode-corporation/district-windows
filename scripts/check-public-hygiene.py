#!/usr/bin/env python3
"""Fail on content that must not appear in this public repository.

    python3 scripts/check-public-hygiene.py              # scan the tree
    python3 scripts/check-public-hygiene.py --self-test  # prove each rule works

The scan reads every tracked file plus every untracked file git does not ignore,
so a new file is checked before anyone commits it. Binary files (any NUL byte) are
skipped. Four rules:

    dash   An em dash (U+2014) or an en dash (U+2013), anywhere. Use commas,
           periods or parentheses.
    phone  A phone number in E.164 form, other than the fictional North American
           range +1 NPA 555-0100 to 555-0199.
    host   A host name under distronode.com or distronode.ca other than the public
           website (www.distronode.com, distronode.com, distronode.ca,
           www.distronode.ca).
    email  An email address other than the project's contact addresses, the
           commit-attribution forms (noreply@anthropic.com, and GitHub's
           noreply@github.com and *@users.noreply.github.com), and addresses at
           example.com, which RFC 2606 reserves for examples and which the
           vendored contract fixtures under contracts/ use.

`--self-test` plants a violation of each rule, and look-alikes that must NOT trip
one, and fails unless every rule answers as expected. It also builds a throwaway
git repository to prove which files the scan reads. CI runs it before the scan, so
a rule that has stopped matching anything fails loudly instead of passing quietly.

This file spells the two dash characters as escapes and assembles its planted
violations at run time, so that it passes its own scan.
"""

from __future__ import annotations

import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

DASHES = {"\u2014": "U+2014 EM DASH", "\u2013": "U+2013 EN DASH"}

# A plus sign, a country code and 7 to 15 digits in all, with single spaces
# (no-break spaces included, U+00A0 and U+202F, which word processors and some
# locales put in numbers), hyphens, dots or parentheses allowed between digits. Not preceded by a word
# character or another plus, so semver build metadata (`1.0.0+20260101`) and
# `C++` do not match, and not followed by a digit, so an over-long digit run is
# not read as a phone number.
PHONE = re.compile(r"(?<![\w+])\+[1-9](?:[ \u00a0\u202f.()\-]{0,2}\d){6,14}(?!\d)")

# One or more labels, then distronode.com or distronode.ca. A label may hold an
# underscore, which DNS allows outside host names proper (service records, and
# names some tools write). The bare domains have no label in front and so never
# match, which is what allows them.
HOST = re.compile(
    r"(?<![\w.\-])((?:[a-z0-9_\-]+\.)+distronode\.(?:com|ca))(?![\w\-])",
    re.IGNORECASE,
)
ALLOWED_HOSTS = {
    "www.distronode.com",
    "distronode.com",
    "distronode.ca",
    "www.distronode.ca",
}

# The top-level domain must be letters, so `crate@0.6.0` (a version) and
# `action@<sha>` (a pinned action) are not addresses.
EMAIL = re.compile(
    r"(?<![\w.%+\-])[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}(?![\w\-])"
)
ALLOWED_EMAILS = {
    "opensource@distronode.com",
    "noreply@anthropic.com",
    "noreply@github.com",
}
ALLOWED_EMAIL_SUFFIXES = ("@users.noreply.github.com",)
# The whole domain, compared exactly, so a subdomain of example.com, a look-alike
# domain, or a longer domain that merely starts with example.com is still a finding.
ALLOWED_EMAIL_DOMAINS = {"example.com"}


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    column: int
    rule: str
    detail: str

    def __str__(self) -> str:
        return f"{self.path}:{self.line}:{self.column}: [{self.rule}] {self.detail}"


def fictional_phone(match: str) -> bool:
    """+1, any area code, then 555-0100 through 555-0199."""
    digits = re.sub(r"\D", "", match)
    return (
        len(digits) == 11
        and digits[0] == "1"
        and digits[4:7] == "555"
        and 100 <= int(digits[7:]) <= 199
    )


def allowed_email(address: str) -> bool:
    address = address.lower()
    return (
        address in ALLOWED_EMAILS
        or address.endswith(ALLOWED_EMAIL_SUFFIXES)
        or address.rpartition("@")[2] in ALLOWED_EMAIL_DOMAINS
    )


def scan_text(text: str, path: str = "<text>") -> list[Finding]:
    findings: list[Finding] = []
    for number, line in enumerate(text.splitlines(), start=1):
        for column, char in enumerate(line, start=1):
            if char in DASHES:
                findings.append(Finding(path, number, column, "dash", DASHES[char]))
        for m in PHONE.finditer(line):
            if not fictional_phone(m.group(0)):
                findings.append(
                    Finding(path, number, m.start() + 1, "phone", f"{m.group(0)!r} is not in +1 NPA 555-0100..0199")
                )
        for m in HOST.finditer(line):
            if m.group(1).lower() not in ALLOWED_HOSTS:
                findings.append(
                    Finding(path, number, m.start(1) + 1, "host", f"{m.group(1)!r} is not the public website")
                )
        for m in EMAIL.finditer(line):
            if not allowed_email(m.group(0)):
                findings.append(
                    Finding(path, number, m.start() + 1, "email", f"{m.group(0)!r} is not an allowed address")
                )
    return findings


def git_files(root: Path) -> list[str]:
    """Tracked files, plus untracked files that .gitignore does not exclude."""

    def ls(*args: str) -> list[str]:
        out = subprocess.run(
            ["git", "ls-files", "-z", *args],
            cwd=root,
            check=True,
            capture_output=True,
        ).stdout
        return [p for p in out.decode("utf-8").split("\0") if p]

    return sorted(set(ls()) | set(ls("--others", "--exclude-standard")))


def scan_tree(root: Path) -> tuple[int, list[Finding]]:
    scanned = 0
    findings: list[Finding] = []
    for rel in git_files(root):
        path = root / rel
        # A tracked file deleted in the working tree, a submodule, a symlink: none
        # of them is a text file this repository publishes as such.
        if path.is_symlink() or not path.is_file():
            continue
        data = path.read_bytes()
        if b"\0" in data:
            continue
        scanned += 1
        findings.extend(scan_text(data.decode("utf-8", errors="replace"), rel))
    return scanned, findings


def self_test() -> int:
    em, en, plus, at = "\u2014", "\u2013", "+", "@"
    nbsp, narrow = "\u00a0", "\u202f"
    zone = "distronode" + ".com"

    # Everything here is allowed, including the look-alikes each rule has to leave
    # alone. Allowed content may be written literally; it passes the scan too.
    clean = "\n".join(
        [
            "Plain ASCII with a hyphen-minus - and a --flag.",
            "Fictional: +1 212 555 0100, +1 (416) 555-0199, +12125550142.",
            f"Fictional with no-break spaces: +1{nbsp}212{nbsp}555{nbsp}0142, +1{narrow}416{narrow}555{narrow}0199.",
            "Versions: 1.0.0+20260926, oo7@0.6.0, actions/checkout@3d3c42e5aac5.",
            "C++ and a+b and 1+2=3.",
            "https://www.distronode.com/pricing and https://www.distronode.ca/fr",
            "distronode.com, distronode.ca, @distronode-com, distronode-corporation.",
            "Mail opensource@distronode.com or Opensource@Distronode.com.",
            "Co-Authored-By: someone <noreply@anthropic.com>",
            "12345+someone@users.noreply.github.com and noreply@github.com",
            "Fixture addresses: ada@example.com, Caller@Example.com.",
        ]
    )

    cases: list[tuple[str, str, set[str]]] = [
        ("clean sample", clean, set()),
        ("em dash", f"one{em}two", {"dash"}),
        ("en dash", f"pages 1{en}2", {"dash"}),
        ("E.164 number, spaced", f"call {plus}44 20 7946 0958 today", {"phone"}),
        ("E.164 number, compact", f"{plus}442079460958", {"phone"}),
        ("NANP 555 number above the range", f"{plus}1 212 555 0200", {"phone"}),
        ("NANP 555 number below the range", f"{plus}1-212-555-0099", {"phone"}),
        ("NANP number outside 555", f"{plus}1 (212) 734-0142", {"phone"}),
        ("number spaced with no-break spaces", f"{plus}1{nbsp}416{nbsp}734{nbsp}0142", {"phone"}),
        ("number spaced with narrow no-break spaces", f"{plus}44{narrow}20{narrow}7946{narrow}0958", {"phone"}),
        ("subdomain of the .com", f"https://api.{zone}/v1", {"host"}),
        ("subdomain of the .ca", "see origin." + "distronode.ca", {"host"}),
        ("host name in capitals", "API." + zone.upper(), {"host"}),
        ("host name with an underscore", f"svc_internal.{zone}", {"host"}),
        ("underscore in a deeper label", f"a.b_c.{zone}", {"host"}),
        ("personal address", f"someone{at}example.org", {"email"}),
        ("other address at the domain", f"security{at}{zone}", {"email"}),
        ("address at a subdomain", f"ops{at}mail.{zone}", {"email", "host"}),
        ("address at a look-alike of example.com", f"ada{at}notexample.com", {"email"}),
        ("address under example.com", f"ada{at}example.com.attacker.test", {"email"}),
        ("address at a subdomain of example.com", f"ada{at}mail.example.com", {"email"}),
    ]

    failures = 0
    for name, text, expected in cases:
        got = {f.rule for f in scan_text(text)}
        ok = got == expected
        failures += not ok
        print(f"  {'pass' if ok else 'FAIL'}  {name}: expected {sorted(expected)}, got {sorted(got)}")

    # Which files the scan reads: tracked and untracked-but-not-ignored text files
    # are read; ignored files and binary files are not.
    with tempfile.TemporaryDirectory() as tmp:
        repo = Path(tmp)
        subprocess.run(["git", "init", "-q"], cwd=repo, check=True)
        bad = f"a{em}b\n"
        (repo / ".gitignore").write_text("ignored.txt\n", encoding="utf-8")
        (repo / "tracked.txt").write_text(bad, encoding="utf-8")
        (repo / "untracked.txt").write_text(bad, encoding="utf-8")
        (repo / "ignored.txt").write_text(bad, encoding="utf-8")
        (repo / "binary.bin").write_bytes(b"\0" + bad.encode("utf-8"))
        subprocess.run(["git", "add", "tracked.txt"], cwd=repo, check=True)
        _, findings = scan_tree(repo)
        got = sorted({f.path for f in findings})
        expected = ["tracked.txt", "untracked.txt"]
        ok = got == expected
        failures += not ok
        print(f"  {'pass' if ok else 'FAIL'}  file selection: expected {expected}, got {got}")

    if failures:
        print(f"\nself-test FAILED: {failures} case(s) did not answer as expected", file=sys.stderr)
        return 1
    print(f"\nself-test passed: {len(cases) + 1} cases")
    return 0


def main(argv: list[str]) -> int:
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) > 1:
        print(__doc__, file=sys.stderr)
        return 2

    scanned, findings = scan_tree(ROOT)
    for finding in findings:
        print(finding)
    if findings:
        print(f"\n{len(findings)} finding(s) in {scanned} files", file=sys.stderr)
        return 1
    print(f"{scanned} files scanned, no findings")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
