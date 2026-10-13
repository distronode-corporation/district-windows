#!/usr/bin/env python3
"""Pseudo-locale for hard-coded-text hunts (i18n/README.md).

    pseudo.py <in.toml> --out <out.toml>

Writes a copy of a catalog whose `fr` values are the pseudo of `en`, so an app can run the
pseudo-locale in the French slot: every string an app shows that is NOT wrapped in ‹ › came
from somewhere other than the catalog, and the +40% length shows where French will not fit.

`pseudo(text)` keeps `{name}` placeholders, `{{` `}}` literal braces, printf-style specifiers,
U+00A0 and every control character (written back as escapes) exactly, and the plural shape
(the same categories as `en`) of each entry.

Python 3.11+, standard library only. Non-ASCII characters are written with chr() so the
source stays readable in a diff.
"""
from __future__ import annotations

import argparse
import math
import re
import sys
import tomllib
from datetime import date, datetime, time
from pathlib import Path

OPEN = chr(0x2039)   # single left-pointing angle quotation mark
CLOSE = chr(0x203A)  # single right-pointing angle quotation mark
NBSP = chr(0x00A0)
PAD = "~"
EXPANSION = 0.4
# a e i o u y and capitals -> the acute form
VOWELS = {c: chr(cp) for c, cp in zip("aeiouyAEIOUY",
                                      (0xE1, 0xE9, 0xED, 0xF3, 0xFA, 0xFD, 0xC1, 0xC9, 0xCD, 0xD3, 0xDA, 0xDD))}
# Kept verbatim: {name}, {{, }}, and %s %d %@ %1$s %% style specifiers.
KEEP = re.compile(r"\{[a-z][a-z0-9_]*\}|\{\{|\}\}|%(?:\d+\$)?[sd@f]|%%")


def pseudo(text: str) -> str:
    """Wrap in ‹ ›, accent the vowels, and pad by 40% of the input length."""
    if not text:
        return text
    out, last = [], 0
    for m in KEEP.finditer(text):
        out.append("".join(VOWELS.get(c, c) for c in text[last:m.start()]))
        out.append(m.group(0))
        last = m.end()
    out.append("".join(VOWELS.get(c, c) for c in text[last:]))
    return OPEN + "".join(out) + PAD * math.ceil(len(text) * EXPANSION) + CLOSE


# ---------------------------------------------------------------------------------------------
# A small canonical-enough TOML emitter (the generator's own emitter lives in l10n.py; this
# tool stays independent of it).

BARE = re.compile(r"[A-Za-z0-9_-]+")


def enc_key(k: str) -> str:
    return k if BARE.fullmatch(k) else enc_str(k)


def enc_str(s: str) -> str:
    out = []
    for ch in s:
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif ch == "\n":
            out.append("\\n")
        elif ch == "\t":
            out.append("\\t")
        elif ch == "\r":
            out.append("\\r")
        elif ord(ch) < 0x20 or ord(ch) == 0x7F or ch == NBSP or ch in (chr(0x202F), chr(0x2009)):
            out.append(f"\\u{ord(ch):04X}")
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def enc_value(v: object) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return repr(v)
    if isinstance(v, str):
        return enc_str(v)
    if isinstance(v, (datetime, date, time)):
        return v.isoformat()
    if isinstance(v, list):
        return "[" + ", ".join(enc_value(x) for x in v) + "]"
    if isinstance(v, dict):
        if not v:
            return "{}"
        return "{ " + ", ".join(f"{enc_key(k)} = {enc_value(x)}" for k, x in v.items()) + " }"
    raise TypeError(f"cannot write {type(v).__name__}")


def _is_entry(table: dict) -> bool:
    return "en" in table


def _subtable(table: dict, k: str, v: object) -> bool:
    if not isinstance(v, dict):
        return False
    return not (_is_entry(table) and k in ("en", "fr", "args", "id"))


def emit(data: dict) -> str:
    lines: list[str] = []

    def table(path: list[str], t: dict) -> None:
        scalars = [(k, v) for k, v in t.items() if not _subtable(t, k, v)]
        if scalars:
            if path:
                if lines:
                    lines.append("")
                lines.append("[" + ".".join(enc_key(p) for p in path) + "]")
            for k, v in scalars:
                if k in ("en", "fr") and isinstance(v, dict) and _is_entry(t):
                    for cat, s in v.items():
                        lines.append(f"{k}.{enc_key(cat)} = {enc_value(s)}")
                else:
                    lines.append(f"{enc_key(k)} = {enc_value(v)}")
        for k, v in t.items():
            if _subtable(t, k, v):
                table(path + [k], v)

    table([], data)
    return "\n".join(lines) + "\n"


def pseudo_entry(e: dict) -> dict:
    """The entry with fr = pseudo(en) right after en; fr_variant dropped, state draft."""
    out: dict = {}
    for k, v in e.items():
        if k in ("fr", "state", "fr_variant"):
            continue
        out[k] = v
        if k == "en":
            out["fr"] = pseudo(v) if isinstance(v, str) else {cat: pseudo(s) for cat, s in v.items()}
    if "fr" in out:
        out["state"] = "draft"
    return out


def pseudo_catalog(data: dict) -> dict:
    def walk(t: dict, top: bool) -> dict:
        t = pseudo_entry(t) if _is_entry(t) and not top else dict(t)
        for k, v in list(t.items()):
            if _subtable(t, k, v) and not (top and k == "meta"):
                t[k] = walk(v, False)
        return t
    return walk(data, True)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="pseudo.py", description="write a pseudo-locale catalog (fr = pseudo(en))")
    ap.add_argument("input", help="catalog .toml")
    ap.add_argument("--out", required=True, help="output .toml ('-' for stdout)")
    a = ap.parse_args(argv)
    src = Path(a.input)
    data = tomllib.loads(src.read_text(encoding="utf-8"))
    result = pseudo_catalog(data)
    text = f"# Generated by i18n/tools/pseudo.py from {src.name}: fr = pseudo(en). Do not edit.\n" + emit(result)
    if tomllib.loads(text) != result:  # the emitter must round-trip exactly
        print("pseudo.py: internal: output does not round-trip", file=sys.stderr)
        return 2
    if a.out == "-":
        sys.stdout.write(text)
    else:
        Path(a.out).write_text(text, encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
