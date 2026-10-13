#!/usr/bin/env python3
"""Generate what this repository's string catalogs produce, or check that what
is committed is what they produce now.

    python3 scripts/gen-l10n.py              # write every target below
    python3 scripts/gen-l10n.py --check      # fail if a catalog is invalid or a target is stale (CI)
    python3 scripts/gen-l10n.py --self-test  # prove the XAML check catches a missing string

Each target is one or more catalogs (TOML, i18n/README.md in the monorepo) and
one backend of the vendored generator, tools/l10n/l10n.py. --check runs
`l10n.py check` on each target's catalogs with the gen comparison (`--gen
BACKEND:OUT`), which fails on an invalid or non-canonical catalog and on a
generated file that differs byte for byte from what the catalog produces.

Both modes then read the XAML: every `{x:Bind l10n:S.Name}` (a property, or a
method called with arguments) under src/ must name a string the generated C#
declares, and a file that uses `l10n:S.` must declare
`xmlns:l10n="using:DistrictAI.L10n"`. The XAML compiler reports a missing name
only as WMC9999, on Windows; this says which file, which line and which name.

The French is written later (v3.0, W3); until then every generated French
branch carries the English and `FR_COMPLETE` / `FrComplete` is false. Nothing
here requires French: the release build that switches French on runs
`l10n.py check --require fr-CA` itself.

Python 3.11 or newer, standard library only.
"""

from __future__ import annotations

import argparse
import importlib.util
import re
import subprocess
import sys
import tempfile
from collections.abc import Iterable
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOL = ROOT / "tools" / "l10n" / "l10n.py"

# (catalogs, backend, output, backend options), paths from the repository root.
TARGETS: list[tuple[list[str], str, str, dict[str, str]]] = [
    # district-ffi's own words, in the core's locale; a count reads as the
    # core writes one (`1204` in English, `1 204` in French).
    (
        ["crates/district-ffi/l10n/ffi.toml"],
        "rust",
        "crates/district-ffi/src/generated/strings.rs",
        {"locale_path": "district_l10n::Locale", "int_fmt": "district_l10n::format::integer"},
    ),
    (["src/DistrictAI/l10n/ui.toml"], "csharp", "src/DistrictAI.Presentation/L10n/Strings.g.cs", {"namespace": "DistrictAI.L10n"}),
]

# The test that calls every generated Rust function in both languages, held to
# its catalog's text (and keeps the French arms covered): (catalog, module the
# functions are in, output).
ACCESSOR_TESTS = [
    ("crates/district-ffi/l10n/ffi.toml", "crate::l10n::s", "crates/district-ffi/src/generated/accessors.rs"),
]

# The package manifest's strings (`ms-resource:AppDisplayName`), as .resw files
# the resw backend writes: (catalog, the backend's `name`, where each language's
# file goes). The English is a resource of the app project. The French lives
# outside the project folder, because the MSIX tooling indexes every .resw
# under it into resources.pri, a `PRIResource Remove` notwithstanding:
# DistrictAI.csproj links it in only with -p:DistrictFrench=true.
RESW = [
    (
        "src/DistrictAI/l10n/manifest.toml",
        "Resources",
        {
            "en-US": "src/DistrictAI/Strings/en-US/Resources.resw",
            "fr-CA": "src/DistrictAI.Strings/fr-CA/Resources.resw",
        },
    ),
]

# The XAML namespace the pages reach the generated class through.
XMLNS = 'xmlns:l10n="using:DistrictAI.L10n"'
XMLNS_ANY = re.compile(r'xmlns:l10n="([^"]*)"')
USE = re.compile(r"\{x:Bind\s+l10n:S\.([A-Za-z_][A-Za-z0-9_]*)")
ANY_USE = re.compile(r"\bl10n:S\.")
MEMBER = re.compile(r"^\s*public static string ([A-Za-z_][A-Za-z0-9_]*)\b", re.MULTILINE)
COMMENT = re.compile(r"<!--.*?-->", re.DOTALL)


def run(args: list[str], quiet: bool = False) -> int:
    """Runs the vendored tool with `args`, from the repository root; with
    `quiet`, what it prints is shown only when it fails."""
    done = subprocess.run(
        [sys.executable, "-I", str(TOOL), *args], cwd=ROOT, check=False, capture_output=quiet, text=True
    )
    if quiet and done.returncode != 0:
        print(done.stdout + done.stderr, file=sys.stderr, end="")
    return done.returncode


def load_tool():
    spec = importlib.util.spec_from_file_location("l10n", TOOL)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def integer(locale: str, n: int) -> str:
    """district_l10n::format::integer: plain digits in English, grouped by
    no-break spaces in French."""
    digits = str(abs(n))
    if locale == "FrCa":
        digits = "".join(("\u00a0" if i and (len(digits) - i) % 3 == 0 else "") + d for i, d in enumerate(digits))
    return ("-" if n < 0 else "") + digits


def accessor_test(tool, catalog_path: str, module: str) -> str:
    """Every function generated from `catalog_path`, called in both languages
    with each string argument passed as its own placeholder, so the expected
    text is the catalog's (the French, or the English while there is none)."""
    catalog = tool.load_catalog(ROOT / catalog_path)
    lines = [
        f"// @generated by scripts/gen-l10n.py from {catalog_path}. Do not edit; run the script.",
        "",
        "use district_l10n::Locale;",
        "",
        f"use {module};" if module.endswith("::s") else f"use {module} as s;",
        "",
        "#[test]",
        f"fn every_{catalog.name}_function_says_what_its_catalog_says() {{",
    ]
    for entry in sorted(catalog.entries.values(), key=lambda e: e.segs):
        for n in [1, 7, 0, 1_000_000] if entry.plural else [1_204]:
            values = {name: (n if kind == "int" else "{" + name + "}") for name, kind in entry.args.items()}
            for locale, lang, forms in (
                ("En", "en", entry.en),
                ("FrCa", *(("fr", entry.fr) if entry.fr is not None else ("en", entry.en))),
            ):
                args = [f"Locale::{locale}"] + [
                    str(values[name]) if kind == "int" else rust_str(tool, values[name]) for name, kind in entry.args.items()
                ]
                text = expected(tool, entry, locale, lang, forms, values)
                lines.append(f"    assert_eq!(s::{tool.rust_name(entry)}({', '.join(args)}), {rust_str(tool, text)});")
    lines.append("}")
    return "\n".join(lines) + "\n"


def expected(tool, entry, locale: str, lang: str, forms, values: dict) -> str:
    if isinstance(forms, dict):
        category = tool.plural_category(lang, values[entry.selector()])
        text = forms.get(category, forms["other"])
    else:
        text = forms
    out = []
    for kind, part in tool.parse_text(text)[0]:
        if kind == "lit":
            out.append(part)
        elif isinstance(values[part], int):
            out.append(integer(locale, values[part]))
        else:
            out.append(values[part])
    return "".join(out)


def rust_str(tool, text: str) -> str:
    return '"' + tool.escape_c_like(text, "rust") + '"'


def opt_args(opts: dict[str, str]) -> list[str]:
    return [arg for key, value in opts.items() for arg in ("--opt", f"{key}={value}")]


def generated_members(source: str) -> set[str]:
    """The strings a generated C# file declares: `public static string Name` (property or method)."""
    return set(MEMBER.findall(source))


def check_xaml(files: Iterable[tuple[str, str]], members: set[str], catalogs: list[str]) -> list[str]:
    """Problems in `files` ((path, text) pairs), one readable line each."""
    problems: list[str] = []
    origin = ", ".join(catalogs)
    for path, text in files:
        # Comments keep their line breaks, so line numbers stay right.
        code = COMMENT.sub(lambda m: re.sub(r"[^\n]", " ", m.group(0)), text)
        if not ANY_USE.search(code):
            continue
        declared = XMLNS_ANY.search(code)
        if declared is None:
            line = code.count("\n", 0, ANY_USE.search(code).start()) + 1
            problems.append(f"{path}:{line}: uses l10n:S. but does not declare {XMLNS}")
        elif declared.group(0) != XMLNS:
            line = code.count("\n", 0, declared.start()) + 1
            problems.append(f"{path}:{line}: declares {declared.group(0)}, not {XMLNS}")
        for use in USE.finditer(code):
            name = use.group(1)
            if name not in members:
                line = code.count("\n", 0, use.start()) + 1
                problems.append(
                    f"{path}:{line}: {{x:Bind l10n:S.{name}}} names no string: no key in {origin} generates S.{name}"
                )
    return problems


def xaml_files() -> list[tuple[str, str]]:
    """Every XAML file under src/, outside build output."""
    out = []
    for path in sorted((ROOT / "src").rglob("*.xaml")):
        rel = path.relative_to(ROOT)
        if {"bin", "obj"} & set(rel.parts):
            continue
        out.append((rel.as_posix(), path.read_text(encoding="utf-8")))
    return out


def check_all_xaml() -> bool:
    """Reads the generated C# of every csharp target and checks the XAML against it."""
    members: set[str] = set()
    catalogs: list[str] = []
    for cats, backend, out, _ in TARGETS:
        if backend != "csharp":
            continue
        catalogs += cats
        generated = ROOT / out
        if generated.is_file():
            members |= generated_members(generated.read_text(encoding="utf-8"))
    problems = check_xaml(xaml_files(), members, catalogs)
    for problem in problems:
        print(problem, file=sys.stderr)
    if problems:
        print(f"gen-l10n: {len(problems)} XAML problem(s)", file=sys.stderr)
    return not problems


def self_test() -> int:
    generated = "\n".join(
        [
            "    public static partial class S",
            "    {",
            '        public static string CallsPlaceCall => Current == Locale.FrCa ? "Place a call" : "Place a call";',
            "        public static string CallsCount(long count)",
            "        public static Locale Current { get; set; } = Locale.En;",
            "    }",
        ]
    )
    members = generated_members(generated)
    cats = ["src/DistrictAI/l10n/ui.toml"]
    head = f'<UserControl\n    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n    {XMLNS}>\n'
    cases: list[tuple[str, str, list[str]]] = [
        ("a known property", head + '<Button Content="{x:Bind l10n:S.CallsPlaceCall}" />\n', []),
        ("a known method", head + '<TextBlock Text="{x:Bind l10n:S.CallsCount(ViewModel.Count), Mode=OneWay}" />\n', []),
        ("no l10n at all", '<UserControl>\n<Button Content="{x:Bind ViewModel.Label}" />\n', []),
        ("a name in a comment only", head + "<!-- {x:Bind l10n:S.Nothing} -->\n", []),
        (
            "a misspelt name",
            head + '<Button\n    Content="{x:Bind l10n:S.CallsPlaceCal}" />\n',
            [
                "Page.xaml:5: {x:Bind l10n:S.CallsPlaceCal} names no string: "
                "no key in src/DistrictAI/l10n/ui.toml generates S.CallsPlaceCal"
            ],
        ),
        (
            "a name the generated class has, but not as a string",
            head + '<TextBlock Text="{x:Bind l10n:S.Current}" />\n',
            ["Page.xaml:4: {x:Bind l10n:S.Current} names no string: no key in src/DistrictAI/l10n/ui.toml generates S.Current"],
        ),
        (
            "no xmlns:l10n",
            '<UserControl>\n<Button Content="{x:Bind l10n:S.CallsPlaceCall}" />\n',
            [f"Page.xaml:2: uses l10n:S. but does not declare {XMLNS}"],
        ),
        (
            "xmlns:l10n elsewhere",
            '<UserControl xmlns:l10n="using:DistrictAI.Other">\n<Button Content="{x:Bind l10n:S.CallsPlaceCall}" />\n',
            [f'Page.xaml:1: declares xmlns:l10n="using:DistrictAI.Other", not {XMLNS}'],
        ),
    ]
    failures = 0
    for name, text, expected in cases:
        got = check_xaml([("Page.xaml", text)], members, cats)
        ok = got == expected
        failures += not ok
        print(f"  {'pass' if ok else 'FAIL'}  {name}: expected {expected}, got {got}")
    expected_members = {"CallsPlaceCall", "CallsCount"}
    ok = members == expected_members
    failures += not ok
    print(f"  {'pass' if ok else 'FAIL'}  members read: expected {sorted(expected_members)}, got {sorted(members)}")
    if failures:
        print(f"\nself-test FAILED: {failures} case(s) did not answer as expected", file=sys.stderr)
        return 1
    print(f"\nself-test passed: {len(cases) + 1} cases")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="fail if anything committed is stale")
    mode.add_argument("--self-test", action="store_true", help="prove the XAML check catches a missing string")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()
    failed = False
    for catalogs, backend, out, opts in TARGETS:
        if args.check:
            code = run(["check", *catalogs, "--gen", f"{backend}:{out}", *opt_args(opts)])
        else:
            code = run(["fmt", *catalogs]) or run(["gen", "--backend", backend, "--out", out, *opt_args(opts), *catalogs])
        if code != 0:
            print(f"gen-l10n: {backend} from {', '.join(catalogs)} failed", file=sys.stderr)
            failed = True
    if failed:
        return 1
    tool = load_tool()
    for catalog, module, out in ACCESSOR_TESTS:
        text = accessor_test(tool, catalog, module)
        path = ROOT / out
        if not args.check:
            path.write_text(text, encoding="utf-8")
        elif not path.is_file() or path.read_text(encoding="utf-8") != text:
            print(f"gen-l10n: {out} is not what {catalog} produces; run python3 scripts/gen-l10n.py", file=sys.stderr)
            failed = True
    for catalog, name, outputs in RESW:
        if not resw(catalog, name, outputs, args.check):
            failed = True
    if not check_all_xaml():
        failed = True
    return 1 if failed else 0


def resw(catalog: str, name: str, outputs: dict[str, str], check: bool) -> bool:
    """Writes (or, with `check`, compares) the .resw files of `catalog`, each
    language's where `outputs` puts it. False on a problem, said on stderr."""
    if run(["check" if check else "fmt", catalog]) != 0:
        print(f"gen-l10n: {catalog} is not a valid, canonical catalog", file=sys.stderr)
        return False
    with tempfile.TemporaryDirectory() as tmp:
        if run(["gen", "--backend", "resw", "--out", tmp, "--opt", f"name={name}", catalog], quiet=True) != 0:
            print(f"gen-l10n: resw from {catalog} failed", file=sys.stderr)
            return False
        ok = True
        for folder, out in outputs.items():
            text = (Path(tmp) / folder / f"{name}.resw").read_text(encoding="utf-8")
            path = ROOT / out
            if not check:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")
            elif not path.is_file() or path.read_text(encoding="utf-8") != text:
                print(f"gen-l10n: {out} is not what {catalog} produces; run python3 scripts/gen-l10n.py", file=sys.stderr)
                ok = False
        return ok


if __name__ == "__main__":
    sys.exit(main())
