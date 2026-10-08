#!/usr/bin/env python3
"""Fail on the word `unsafe` in any hand-written Rust source under crates/*/src.

    python3 scripts/check-unsafe.py              # scan the crates
    python3 scripts/check-unsafe.py --self-test  # prove the rule works

Every crate already sets `unsafe_code = "forbid"`, which the compiler enforces.
This is the second line: the lint does not look inside a macro's expansion, so
an `unsafe` block handed to a macro, an `#[allow(unsafe_code)]`, or a lowered
lint level in a crate's Cargo.toml would pass the compiler and not this. The
only `unsafe` code in the build is what UniFFI's macros generate.

Comments are read too, on purpose: a comment that needs the word can say
"not safe", and nothing that could hide code gets a pass.
"""

from __future__ import annotations

import re
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WORD = re.compile(r"\bunsafe\b|unsafe_code\s*=\s*\"(?:allow|warn)\"|allow\(\s*unsafe_code")


def findings(root: Path) -> list[str]:
    found = []
    for path in sorted(root.glob("crates/*/src/**/*.rs")):
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            if WORD.search(line):
                found.append(f"{path.relative_to(root)}:{number}: {line.strip()}")
    for path in sorted(root.glob("crates/*/Cargo.toml")) + [root / "Cargo.toml"]:
        if not path.is_file():
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            if re.match(r'\s*unsafe_code\s*=\s*"(allow|warn|deny)"', line):
                found.append(f"{path.relative_to(root)}:{number}: {line.strip()} (must be forbid)")
    return found


def self_test() -> int:
    cases = {
        "fn a() { unsafe { } }\n": True,
        "#[allow(unsafe_code)]\nfn b() {}\n": True,
        "unsafe fn c() {}\n": True,
        "fn safe_enough() {} // not safe to call twice\n": False,
        "let unsafely = 1;\n": False,
    }
    failed = 0
    for text, expected in cases.items():
        with tempfile.TemporaryDirectory() as tmp:
            src = Path(tmp, "crates", "x", "src")
            src.mkdir(parents=True)
            (src / "lib.rs").write_text(text, encoding="utf-8")
            got = bool(findings(Path(tmp)))
        if got != expected:
            failed += 1
            print(f"self-test: {text!r} should {'' if expected else 'not '}be a finding")
    with tempfile.TemporaryDirectory() as tmp:
        crate = Path(tmp, "crates", "x")
        (crate / "src").mkdir(parents=True)
        (crate / "Cargo.toml").write_text('[lints.rust]\nunsafe_code = "allow"\n', encoding="utf-8")
        if not findings(Path(tmp)):
            failed += 1
            print("self-test: a lowered unsafe_code level should be a finding")
    if failed:
        return 1
    print(f"self-test passed: {len(cases) + 1} cases")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    found = findings(ROOT)
    for line in found:
        print(line)
    if found:
        print("unsafe code outside UniFFI's generated scaffolding; see scripts/check-unsafe.py")
        return 1
    print("no hand-written unsafe code")
    return 0


if __name__ == "__main__":
    sys.exit(main())
