#!/usr/bin/env python3
"""Generate what this repository's string catalogs produce, or check that what
is committed is what they produce now.

    python3 scripts/gen-l10n.py            # write every target below
    python3 scripts/gen-l10n.py --check    # fail if a catalog is invalid or a target is stale (CI)

Each target is one or more catalogs (TOML, i18n/README.md in the monorepo) and
one backend of the vendored generator, tools/l10n/l10n.py. --check runs
`l10n.py check` on each target's catalogs with the gen comparison (`--gen
BACKEND:OUT`), which fails on an invalid or non-canonical catalog and on a
generated file that differs byte for byte from what the catalog produces.

The French is written later (v3.0, W3); until then every generated French
branch carries the English and `FR_COMPLETE` / `FrComplete` is false. Nothing
here requires French: the release build that switches French on runs
`l10n.py check --require fr-CA` itself.

Python 3.11 or newer, standard library only.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOL = ROOT / "tools" / "l10n" / "l10n.py"

# (catalogs, backend, output, backend options), paths from the repository root.
TARGETS: list[tuple[list[str], str, str, dict[str, str]]] = []


def run(args: list[str]) -> int:
    """Runs the vendored tool with `args`, from the repository root."""
    return subprocess.run([sys.executable, "-I", str(TOOL), *args], cwd=ROOT, check=False).returncode


def opt_args(opts: dict[str, str]) -> list[str]:
    return [arg for key, value in opts.items() for arg in ("--opt", f"{key}={value}")]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    parser.add_argument("--check", action="store_true", help="fail if anything committed is stale")
    args = parser.parse_args(argv)
    failed = False
    for catalogs, backend, out, opts in TARGETS:
        if args.check:
            code = run(["check", *catalogs, "--gen", f"{backend}:{out}", *opt_args(opts)])
        else:
            code = run(["fmt", *catalogs]) or run(["gen", "--backend", backend, "--out", out, *opt_args(opts), *catalogs])
        if code != 0:
            print(f"gen-l10n: {backend} from {', '.join(catalogs)} failed", file=sys.stderr)
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
