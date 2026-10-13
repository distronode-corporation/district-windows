#!/usr/bin/env python3
"""l10n.py: the native apps' string catalogs, from TOML to typed accessors per platform.

The catalog format and the rules enforced here are in i18n/README.md. Python 3.11 or newer,
standard library only. Every problem prints as one line, `path:key: reason`, and exits 1.
"""

from __future__ import annotations

import argparse
import dataclasses
import datetime
import difflib
import hashlib
import json
import math
import re
import shutil
import subprocess
import sys
import tomllib
from pathlib import Path

TOOL = "i18n/tools/l10n.py"

SEGMENT = re.compile(r"[a-z][a-z0-9_]*\Z")
PLACEHOLDER = re.compile(r"[a-z][a-z0-9_]*\Z")
CATALOG_NAME = re.compile(r"[a-z][a-z0-9_]*\Z")
BARE_KEY = re.compile(r"[A-Za-z0-9_-]+\Z")

CTX = frozenset(
    "button title label body error a11y placeholder menu toast notification store legal "
    "tooltip link format".split()
)
TIERS = ("T0", "T1", "T2", "T3", "T4")
DEFAULT_TIER = "T3"
ARG_TYPES = frozenset("int str money date time duration percent".split())
STATES = ("draft", "reviewed", "signed")
DONE_STATES = ("reviewed", "signed")
PLATFORMS = frozenset("rust swift android csharp gtk neutral".split())

META_ORDER = ("catalog", "platform", "apple")
FIELD_ORDER = (
    "en", "fr", "ctx", "screen", "tier", "fixed", "args", "max", "state", "same_ok", "fr_variant", "waive", "id", "note",
)
PLURAL_ORDER = ("one", "many", "other")
EN_FORMS = ("one", "other")
FR_FORMS_REQUIRED = ("one", "other")
FR_FORMS_ALLOWED = ("one", "many", "other")

ID_PATTERNS = {
    "rust": re.compile(r"[a-z_][a-z0-9_]*\Z"),
    "swift": re.compile(r"[A-Z][A-Za-z0-9_]*\.[a-z_][A-Za-z0-9_]*\Z"),
    "csharp": re.compile(r"[A-Z][A-Za-z0-9_]*\Z"),
    "android": re.compile(r"[a-z][a-z0-9_]*\Z"),
    "apple": re.compile(r"[^\x00-\x1f\"\\]+\Z"),
    "resw": re.compile(r"[A-Za-z][A-Za-z0-9_]*\Z"),
}
ID_KEYS = tuple(ID_PATTERNS)

RUST_KEYWORDS = frozenset(
    "as async await break const continue crate dyn else enum extern false fn for gen if impl in "
    "let loop match mod move mut pub ref return self Self static struct super trait true type "
    "unsafe use where while abstract become box do final macro override priv try typeof unsized "
    "virtual yield".split()
)
SWIFT_KEYWORDS = frozenset(
    "associatedtype class deinit enum extension fileprivate func import init inout internal let "
    "open operator private precedencegroup protocol public rethrows static struct subscript "
    "typealias var break case catch continue default defer do else fallthrough for guard if in "
    "repeat return throw switch where while Any as await false is nil self Self super throws "
    "true try".split()
)
CSHARP_KEYWORDS = frozenset(
    "abstract as base bool break byte case catch char checked class const continue decimal "
    "default delegate do double else enum event explicit extern false finally fixed float for "
    "foreach goto if implicit in int interface internal is lock long namespace new null object "
    "operator out override params private protected public readonly ref return sbyte sealed "
    "short sizeof stackalloc static string struct switch this throw true try typeof uint ulong "
    "unchecked unsafe ushort using virtual void volatile while".split()
)
# Names the generated C# class declares itself, so no entry may take them.
CSHARP_RESERVED = frozenset({"Locale", "Current", "S"})
SWIFT_RESERVED_ENUMS = frozenset({"L10nRuntime", "L10nLocale"})


# ---------------------------------------------------------------------------------------------
# Problems


class Problems:
    """Collects `path:key: reason` lines."""

    def __init__(self) -> None:
        self.lines: list[str] = []

    def add(self, path: object, key: str | None, reason: str) -> None:
        self.lines.append(f"{path}:{key}: {reason}" if key else f"{path}: {reason}")

    def __bool__(self) -> bool:
        return bool(self.lines)

    def emit(self) -> int:
        for line in self.lines:
            print(line, file=sys.stderr)
        return 1 if self.lines else 0


class L10nError(Exception):
    """A fatal problem; its lines are already formatted."""

    def __init__(self, lines: list[str]) -> None:
        super().__init__("\n".join(lines))
        self.lines = lines


# ---------------------------------------------------------------------------------------------
# Catalog model


@dataclasses.dataclass
class Entry:
    key: str
    segs: tuple[str, ...]
    fields: dict
    catalog: "Catalog"

    @property
    def en(self):
        return self.fields["en"]

    @property
    def fr(self):
        return self.fields.get("fr")

    @property
    def plural(self) -> bool:
        return isinstance(self.fields.get("en"), dict)

    @property
    def fixed(self) -> bool:
        return self.fields.get("fixed") is True

    @property
    def args(self) -> dict:
        a = self.fields.get("args")
        return a if isinstance(a, dict) else {}

    @property
    def tier(self) -> str:
        return self.fields.get("tier", DEFAULT_TIER)

    @property
    def ids(self) -> dict:
        i = self.fields.get("id")
        return i if isinstance(i, dict) else {}

    def en_forms(self) -> dict[str, str]:
        return dict(self.en) if self.plural else {"": self.en}

    def selector(self) -> str | None:
        ints = [n for n, t in self.args.items() if t == "int"]
        return ints[0] if self.plural and len(ints) == 1 else None

    def max_len(self) -> int:
        if isinstance(self.fields.get("max"), int) and not isinstance(self.fields.get("max"), bool):
            return self.fields["max"]
        return default_max(self.en_forms().values())


def default_max(texts) -> int:
    n = max((len(t) for t in texts if isinstance(t, str)), default=0)
    return max(math.ceil(n * 1.3), n + 4)


@dataclasses.dataclass
class Catalog:
    path: Path
    data: dict
    meta: dict | None
    entries: dict[str, Entry]
    structural: list[str]  # problems that make the file impossible to rewrite

    @property
    def name(self) -> str:
        if isinstance(self.meta, dict) and isinstance(self.meta.get("catalog"), str):
            return self.meta["catalog"]
        return self.path.stem


def load_catalog(path: Path) -> Catalog:
    """Parses one catalog. Raises L10nError when the file cannot be read or parsed."""
    try:
        text = path.read_bytes().decode("utf-8")
    except OSError as exc:
        raise L10nError([f"{path}: cannot read: {exc.strerror}"]) from exc
    except UnicodeDecodeError as exc:
        raise L10nError([f"{path}: not UTF-8: {exc.reason}"]) from exc
    try:
        data = tomllib.loads(text)
    except tomllib.TOMLDecodeError as exc:
        raise L10nError([f"{path}: TOML parse error: {exc}"]) from exc
    return catalog_from_data(path, data)


def catalog_from_data(path: Path, data: dict) -> Catalog:
    cat = Catalog(path=path, data=data, meta=None, entries={}, structural=[])
    meta = data.get("meta")
    if meta is not None:
        if isinstance(meta, dict):
            cat.meta = meta
        else:
            cat.structural.append(f"{path}:meta: must be a table")

    def walk(table: dict, prefix: tuple[str, ...]) -> None:
        for k, v in table.items():
            segs = prefix + (k,)
            dotted = ".".join(segs)
            if not prefix and k == "meta":
                continue
            if not isinstance(v, dict):
                cat.structural.append(f"{path}:{dotted}: a value outside any entry (an entry is a table with en)")
                continue
            if "en" in v:
                cat.entries[dotted] = Entry(key=dotted, segs=segs, fields=v, catalog=cat)
            elif not v:
                cat.structural.append(f"{path}:{dotted}: empty table")
            elif any(not isinstance(x, dict) for x in v.values()):
                cat.structural.append(f"{path}:{dotted}: table has fields but no en")
            else:
                walk(v, segs)

    walk(data, ())
    return cat


def load_all(paths: list[str]) -> list[Catalog]:
    cats, lines = [], []
    for p in paths:
        try:
            cats.append(load_catalog(Path(p)))
        except L10nError as exc:
            lines.extend(exc.lines)
    if lines:
        raise L10nError(lines)
    return cats


# ---------------------------------------------------------------------------------------------
# Placeholders


def parse_text(s: str) -> tuple[list[tuple[str, str]], list[str]]:
    """Splits a string into ('lit', text) and ('arg', name) parts; `{{` and `}}` are braces."""
    parts: list[tuple[str, str]] = []
    errors: list[str] = []
    buf: list[str] = []
    i = 0

    def flush() -> None:
        if buf:
            parts.append(("lit", "".join(buf)))
            buf.clear()

    while i < len(s):
        c = s[i]
        if c == "{":
            if s.startswith("{{", i):
                buf.append("{")
                i += 2
                continue
            j = s.find("}", i)
            name = s[i + 1 : j] if j != -1 else ""
            if j == -1 or not PLACEHOLDER.match(name):
                errors.append(f"unescaped '{{' at column {i + 1} (a literal brace is written {{{{)")
                buf.append("{")
                i += 1
                continue
            flush()
            parts.append(("arg", name))
            i = j + 1
        elif c == "}":
            if s.startswith("}}", i):
                buf.append("}")
                i += 2
                continue
            errors.append(f"unescaped '}}' at column {i + 1} (a literal brace is written }}}})")
            buf.append("}")
            i += 1
        else:
            buf.append(c)
            i += 1
    flush()
    return parts, errors


def placeholder_names(s: str) -> set[str]:
    return {v for k, v in parse_text(s)[0] if k == "arg"}


def unbrace(s: str) -> str:
    """The display text of a string without placeholders: `{{` -> `{`."""
    return "".join(v if k == "lit" else "{" + v + "}" for k, v in parse_text(s)[0])


# ---------------------------------------------------------------------------------------------
# Validation


def _forms_problem(value, required, allowed, label) -> str | None:
    if not isinstance(value, dict):
        return None
    missing = [f for f in required if f not in value]
    extra = [f for f in value if f not in allowed]
    if missing:
        return f"plural {label} lacks {', '.join(label + '.' + m for m in missing)}"
    if extra:
        return f"plural {label} has unknown form {', '.join(label + '.' + str(x) for x in extra)}"
    return None


def check_text_parity(entry: Entry, fr, add, label: str = "fr") -> None:
    """Placeholder rules for `fr` against the entry's English and args (shared with import)."""
    args = set(entry.args)
    en_forms = entry.en_forms()
    fr_forms = dict(fr) if isinstance(fr, dict) else {"": fr}
    en_union = set().union(*(placeholder_names(t) for t in en_forms.values() if isinstance(t, str)))
    fr_union: set[str] = set()
    for form, text in fr_forms.items():
        if not isinstance(text, str):
            continue
        parts, errs = parse_text(text)
        where = f"{label}.{form}" if form else label
        for e in errs:
            add(f"{where}: {e}")
        names = {v for k, v in parts if k == "arg"}
        for n in sorted(names - args):
            add(f"{where} uses undeclared placeholder {{{n}}}")
        if not entry.plural and names != en_union:
            add(f"{where} placeholders {sorted(names)} differ from en {sorted(en_union)}")
        fr_union |= names
    if entry.plural and fr_union != en_union:
        add(f"{label} placeholders {sorted(fr_union)} differ from en {sorted(en_union)}")


def validate_entry(e: Entry, add) -> None:
    f = e.fields
    for seg in e.segs:
        if not SEGMENT.match(seg):
            add(f"key segment {seg!r} must match [a-z][a-z0-9_]*")
    if len(e.segs) < 2:
        add("key needs at least two segments (the first names the group)")
    for name in f:
        if name not in FIELD_ORDER:
            add(f"unknown field {name!r}")

    # args
    args = f.get("args", {})
    if not isinstance(args, dict):
        add("args must be a table of name = type")
        args = {}
    for name, typ in args.items():
        if not PLACEHOLDER.match(name):
            add(f"arg name {name!r} must match [a-z][a-z0-9_]*")
        if typ not in ARG_TYPES:
            add(f"arg {name!r} has type {typ!r}, not one of {' '.join(sorted(ARG_TYPES))}")

    # en and fr shapes
    en = f["en"]
    shape_ok = True
    if isinstance(en, dict):
        msg = _forms_problem(en, EN_FORMS, EN_FORMS, "en")
        if msg:
            add(msg)
            shape_ok = False
        for form, text in en.items():
            if not isinstance(text, str) or not text:
                add(f"en.{form} must be a non-empty string")
                shape_ok = False
        ints = [n for n, t in args.items() if t == "int"]
        if len(ints) != 1:
            add(f"plural entry needs exactly one int arg, has {len(ints)}")
    elif not isinstance(en, str) or not en:
        add("en must be a non-empty string or a plural table")
        shape_ok = False

    fr = f.get("fr")
    if fr is not None:
        if isinstance(en, dict) and not isinstance(fr, dict):
            add("fr must be a plural table (fr.one, fr.other) because en is")
            shape_ok = False
        elif isinstance(en, str) and not isinstance(fr, str):
            add("fr must be a string because en is not plural")
            shape_ok = False
        elif isinstance(fr, dict):
            msg = _forms_problem(fr, FR_FORMS_REQUIRED, FR_FORMS_ALLOWED, "fr")
            if msg:
                add(msg)
                shape_ok = False
            for form, text in fr.items():
                if not isinstance(text, str) or not text:
                    add(f"fr.{form} must be a non-empty string")
                    shape_ok = False
        elif not fr:
            add("fr must not be empty (leave it out until translated)")

    # placeholders: en against args, then fr against en
    if shape_ok:
        en_union: set[str] = set()
        for form, text in e.en_forms().items():
            parts, errs = parse_text(text)
            where = f"en.{form}" if form else "en"
            for err in errs:
                add(f"{where}: {err}")
            names = {v for k, v in parts if k == "arg"}
            for n in sorted(names - set(args)):
                add(f"{where} uses undeclared placeholder {{{n}}}")
            if not e.plural:
                for n in sorted(set(args) - names):
                    add(f"arg {n!r} is declared but not used in en")
            en_union |= names
        if e.plural:
            for n in sorted(set(args) - en_union):
                add(f"arg {n!r} is declared but not used in en")
        if fr is not None:
            check_text_parity(e, fr, add)

    for label, value in (("en", en), ("fr", fr)):
        texts = value.values() if isinstance(value, dict) else [value]
        if any(isinstance(t, str) and "\u202f" in t for t in texts):
            add(f"{label} contains U+202F; the only non-breaking space is U+00A0")

    # metadata fields
    ctx = f.get("ctx")
    if ctx is None:
        add("missing ctx")
    elif ctx not in CTX:
        add(f"ctx {ctx!r} is not one of {' '.join(sorted(CTX))}")
    screen = f.get("screen")
    if screen is None:
        add("missing screen")
    elif not isinstance(screen, str) or not screen.strip():
        add("screen must be a non-empty string")
    if "tier" in f and f["tier"] not in TIERS:
        add(f"tier {f['tier']!r} is not one of {' '.join(TIERS)}")
    if "max" in f and (not isinstance(f["max"], int) or isinstance(f["max"], bool) or f["max"] < 1):
        add("max must be a positive integer")
    if "state" in f:
        if fr is None:
            add("state is set but fr is absent")
        if f["state"] not in STATES:
            add(f"state {f['state']!r} is not one of {' '.join(STATES)}")
    if "same_ok" in f and not isinstance(f["same_ok"], bool):
        add("same_ok must be true or false")
    if "fr_variant" in f and (not isinstance(f["fr_variant"], str) or not f["fr_variant"].strip()):
        add("fr_variant must be a non-empty string")
    if "fixed" in f:
        if not isinstance(f["fixed"], bool):
            add("fixed must be true or false")
        elif f["fixed"]:
            for name in ("fr", "state", "max", "fr_variant", "same_ok"):
                if name in f:
                    add(f"a fixed entry is never translated, so it must not have {name}")
    if "waive" in f:
        waive = f["waive"]
        if not isinstance(waive, dict):
            add("waive must be a table of frcheck warning id = reason")
        else:
            for wid, reason in waive.items():
                if not isinstance(reason, str) or not reason.strip():
                    add(f"waive.{wid} needs a non-empty reason string")
    if "note" in f and not isinstance(f["note"], str):
        add("note must be a string")
    if "id" in f:
        ids = f["id"]
        if not isinstance(ids, dict):
            add("id must be a table of platform = name")
        else:
            for plat, name in ids.items():
                if plat not in ID_PATTERNS:
                    add(f"id.{plat}: unknown platform (one of {' '.join(ID_KEYS)})")
                elif not isinstance(name, str) or not ID_PATTERNS[plat].match(name):
                    add(f"id.{plat} {name!r} is not a valid {plat} name")
                elif plat == "rust" and name in RUST_KEYWORDS:
                    add(f"id.rust {name!r} is a Rust keyword")


def validate_meta(cat: Catalog, problems: Problems) -> None:
    meta = cat.meta
    if meta is None:
        problems.add(cat.path, "meta", "missing [meta] table")
        return
    for k in meta:
        if k not in META_ORDER:
            problems.add(cat.path, "meta", f"unknown field {k!r}")
    name = meta.get("catalog")
    if not isinstance(name, str) or not CATALOG_NAME.match(name):
        problems.add(cat.path, "meta", "catalog must be a name matching [a-z][a-z0-9_]*")
    if meta.get("platform") not in PLATFORMS:
        problems.add(cat.path, "meta", f"platform must be one of {' '.join(sorted(PLATFORMS))}")
    if "apple" in meta and not isinstance(meta["apple"], bool):
        problems.add(cat.path, "meta", "apple must be true or false")


# Generated names per platform, as (namespace, name) pairs; an entry may yield several (plurals
# in apple-strings and resw are one string per form).


def pascal(s: str) -> str:
    return "".join(p[:1].upper() + p[1:] for p in re.split(r"[._]", s) if p)


def camel(s: str) -> str:
    parts = [p for p in re.split(r"[._]", s) if p]
    return parts[0] + "".join(p[:1].upper() + p[1:] for p in parts[1:]) if parts else s


def rust_name(e: Entry) -> str:
    return e.ids.get("rust") or e.key.replace(".", "_")


def swift_name(e: Entry, suffix: str = "Copy") -> tuple[str, str]:
    if "swift" in e.ids:
        enum, member = e.ids["swift"].split(".", 1)
        return enum, member
    return pascal(e.segs[0]) + suffix, camel(".".join(e.segs[1:]))


def csharp_name(e: Entry) -> str:
    return e.ids.get("csharp") or pascal(e.key)


def android_name(e: Entry) -> str:
    return e.ids.get("android") or e.key.replace(".", "_")


def apple_names(e: Entry) -> list[str]:
    base = e.ids.get("apple") or e.key
    return [f"{base}.{f}" for f in PLURAL_ORDER] if e.plural else [base]


def resw_names(e: Entry) -> list[str]:
    base = e.ids.get("resw") or pascal(e.key)
    return [f"{base}_{f.capitalize()}" for f in PLURAL_ORDER] if e.plural else [base]


NAMERS = {
    "rust": lambda e: [rust_name(e)],
    "swift": lambda e: [".".join(swift_name(e))],
    "csharp": lambda e: [csharp_name(e)],
    "android": lambda e: [android_name(e)],
    "apple": apple_names,
    "resw": resw_names,
}


def validate(cats: list[Catalog]) -> Problems:
    """Every catalog rule in README.md, plus collisions across the catalogs given together."""
    problems = Problems()
    for cat in cats:
        problems.lines.extend(cat.structural)
        validate_meta(cat, problems)
        for e in cat.entries.values():
            validate_entry(e, lambda reason, e=e: problems.add(e.catalog.path, e.key, reason))

    seen_names: dict[str, Catalog] = {}
    for cat in cats:
        if cat.name in seen_names:
            problems.add(cat.path, "meta", f"catalog name {cat.name!r} is also used by {seen_names[cat.name].path}")
        seen_names.setdefault(cat.name, cat)

    seen_keys: dict[str, Entry] = {}
    for e in all_entries(cats):
        if e.key in seen_keys:
            problems.add(e.catalog.path, e.key, f"key is also defined in {seen_keys[e.key].catalog.path}")
        seen_keys.setdefault(e.key, e)

    for plat, namer in NAMERS.items():
        taken: dict[str, Entry] = {}
        for e in all_entries(cats):
            try:
                names = namer(e)
            except (ValueError, TypeError):
                continue  # an invalid id override, reported above
            for name in names:
                if not isinstance(name, str):
                    continue
                if plat == "csharp" and name in CSHARP_RESERVED:
                    problems.add(e.catalog.path, e.key, f"csharp name {name!r} is reserved by the generated class")
                if plat == "swift" and name.split(".")[0] in SWIFT_RESERVED_ENUMS:
                    problems.add(e.catalog.path, e.key, f"swift name {name!r} uses a reserved runtime enum")
                other = taken.get(name)
                if other is not None and other.key != e.key:
                    problems.add(e.catalog.path, e.key, f"{plat} name {name!r} collides with {other.key}")
                taken.setdefault(name, e)
    return problems


def all_entries(cats: list[Catalog]) -> list[Entry]:
    out = [e for c in cats for e in c.entries.values()]
    out.sort(key=lambda e: (e.segs, str(e.catalog.path)))
    return out


def load_valid(paths: list[str]) -> list[Catalog]:
    cats = load_all(paths)
    problems = validate(cats)
    if problems:
        raise L10nError(problems.lines)
    return cats


# ---------------------------------------------------------------------------------------------
# Canonical TOML


def toml_str(s: str) -> str:
    out = ['"']
    for ch in s:
        if ch == '"':
            out.append('\\"')
        elif ch == "\\":
            out.append("\\\\")
        elif ch == "\n":
            out.append("\\n")
        elif ch == "\t":
            out.append("\\t")
        elif ch == "\r":
            out.append("\\r")
        elif ch == " " or ch.isprintable():
            out.append(ch)
        else:
            cp = ord(ch)
            out.append(f"\\u{cp:04X}" if cp <= 0xFFFF else f"\\U{cp:08X}")
    out.append('"')
    return "".join(out)


def toml_key(k: str) -> str:
    return k if BARE_KEY.match(k) else toml_str(k)


def toml_value(v) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        if math.isnan(v):
            return "nan"
        if math.isinf(v):
            return "inf" if v > 0 else "-inf"
        return repr(v)
    if isinstance(v, str):
        return toml_str(v)
    if isinstance(v, list):
        return "[" + ", ".join(toml_value(x) for x in v) + "]"
    if isinstance(v, dict):
        if not v:
            return "{}"
        return "{ " + ", ".join(f"{toml_key(k)} = {toml_value(x)}" for k, x in v.items()) + " }"
    raise ValueError(f"unsupported TOML value of type {type(v).__name__}")


def ordered(d: dict, order: tuple[str, ...]) -> list[str]:
    return [k for k in order if k in d] + sorted(k for k in d if k not in order)


def emit_catalog(cat: Catalog) -> str:
    """The canonical text of a catalog. Raises L10nError on a structural problem."""
    if cat.structural:
        raise L10nError(cat.structural)
    lines: list[str] = []
    try:
        if cat.meta is not None:
            lines.append("[meta]")
            for k in ordered(cat.meta, META_ORDER):
                lines.append(f"{toml_key(k)} = {toml_value(cat.meta[k])}")
        for e in sorted(cat.entries.values(), key=lambda x: x.segs):
            if lines:
                lines.append("")
            lines.append("[" + ".".join(toml_key(s) for s in e.segs) + "]")
            for field in ordered(e.fields, FIELD_ORDER):
                v = e.fields[field]
                if field in ("en", "fr") and isinstance(v, dict) and v:
                    for form in ordered(v, PLURAL_ORDER):
                        lines.append(f"{field}.{toml_key(form)} = {toml_value(v[form])}")
                elif field == "args" and isinstance(v, dict):
                    # declared order is the positional order on Android: never re-sort it
                    lines.append(f"args = {toml_value(v)}")
                elif isinstance(v, dict):
                    lines.append(f"{toml_key(field)} = {toml_value({k: v[k] for k in ordered(v, ID_KEYS)})}")
                else:
                    lines.append(f"{toml_key(field)} = {toml_value(v)}")
    except ValueError as exc:
        raise L10nError([f"{cat.path}: {exc}"]) from exc
    return "\n".join(lines) + "\n" if lines else ""


def first_difference(a: str, b: str) -> int:
    for i, (x, y) in enumerate(zip(a.splitlines(), b.splitlines()), start=1):
        if x != y:
            return i
    return min(len(a.splitlines()), len(b.splitlines())) + 1


# ---------------------------------------------------------------------------------------------
# Plural selection (CLDR): English one = 1; French one = 0 and 1, many = non-zero multiples of
# a million, other = everything else.


def plural_category(lang: str, n: int) -> str:
    if lang == "en":
        return "one" if n == 1 else "other"
    if n in (0, 1):
        return "one"
    if n != 0 and n % 1_000_000 == 0:
        return "many"
    return "other"


def plural_conditions(lang: str, var: str) -> dict[str, str]:
    if lang == "en":
        return {"one": f"{var} == 1"}
    return {"one": f"{var} == 0 || {var} == 1", "many": f"{var} != 0 && {var} % 1_000_000 == 0"}


def plural_groups(lang: str, forms: dict[str, str]) -> list[tuple[list[str], str]]:
    """Groups categories with the same text, in check order; the last group holds `other`."""
    cats = ["one", "other"] if lang == "en" else ["one", "many", "other"]
    texts = {c: forms.get(c, forms["other"]) for c in cats}
    groups: dict[str, list[str]] = {}
    for c in cats:
        groups.setdefault(texts[c], []).append(c)
    out = [(cs, t) for t, cs in groups.items() if "other" not in cs]
    out.append((groups[texts["other"]], texts["other"]))
    return out


def plural_condition(lang: str, cats: list[str], var: str) -> str:
    conds = plural_conditions(lang, var)
    parts = [conds[c] for c in cats]
    if len(parts) == 1:
        return parts[0]
    return " || ".join(f"({p})" for p in parts)


def fr_arm(e: Entry) -> tuple[str, object]:
    """The language whose selection rules apply in the French arm, and its text."""
    return ("fr", e.fr) if e.fr is not None else ("en", e.en)


# ---------------------------------------------------------------------------------------------
# Backends


def header_names(cats: list[Catalog]) -> str:
    return ", ".join(f'"{c.name}"' for c in cats)


def generated_line(cats: list[Catalog]) -> str:
    return f"@generated by {TOOL} from catalog {header_names(cats)}. Do not edit; run l10n.py gen."


def fr_complete(entries: list[Entry]) -> bool:
    return all(e.fr is not None or e.fixed for e in entries)


def untranslated(entries: list[Entry]) -> int:
    """Entries that still need French (a fixed entry never does)."""
    return sum(1 for e in entries if e.fr is None and not e.fixed)


def escape_c_like(s: str, style: str) -> str:
    """String literal body for rust, swift or csharp."""
    out = []
    for ch in s:
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif ch == "\n":
            out.append("\\n")
        elif ch == "\r":
            out.append("\\r")
        elif ch == "\t":
            out.append("\\t")
        elif ch == "\0":
            out.append("\\0")
        elif ch == " " or ch.isprintable():
            out.append(ch)
        else:
            cp = ord(ch)
            if style == "csharp":
                out.append(f"\\u{cp:04X}" if cp <= 0xFFFF else f"\\U{cp:08X}")
            else:
                out.append(f"\\u{{{cp:04X}}}")
    return "".join(out)


# rust


def rust_ident(name: str) -> str:
    return name + "_" if name in RUST_KEYWORDS or name == "l" else name


def rust_expr(text: str, has_args: bool, args: dict | None = None, int_fmt: str | None = None) -> str:
    """With `int_fmt`, an int arg renders as `int_fmt(l, n)` (a fn(Locale, i64) -> String)."""
    parts = parse_text(text)[0]
    if not has_args:
        return '"' + escape_c_like(unbrace(text), "rust") + '"'
    if not any(k == "arg" for k, _ in parts):
        return '"' + escape_c_like(unbrace(text), "rust") + '".to_owned()'
    body, positional = [], []
    for k, v in parts:
        if k == "lit":
            body.append(escape_c_like(v.replace("{", "{{").replace("}", "}}"), "rust"))
        elif int_fmt and (args or {}).get(v) == "int":
            body.append("{}")
            positional.append(f"{int_fmt}(l, {rust_ident(v)})")
        else:
            body.append("{" + rust_ident(v) + "}")
    return 'format!("' + "".join(body) + '"' + "".join(", " + p for p in positional) + ")"


def rust_arm(lang: str, value, e: Entry, indent: str, int_fmt: str | None = None) -> list[str]:
    has_args = bool(e.args)
    if not isinstance(value, dict):
        return [f"{indent}{rust_expr(value, has_args, e.args, int_fmt)}"]
    var = rust_ident(e.selector())
    groups = plural_groups(lang, value)
    if len(groups) == 1:
        return [f"{indent}{rust_expr(groups[0][1], True, e.args, int_fmt)}"]
    out = []
    for i, (cats, text) in enumerate(groups):
        if i == 0:
            out.append(f"{indent}if {plural_condition(lang, cats, var)} {{")
        elif i < len(groups) - 1:
            out.append(f"{indent}}} else if {plural_condition(lang, cats, var)} {{")
        else:
            out.append(f"{indent}}} else {{")
        out.append(f"{indent}    {rust_expr(text, True, e.args, int_fmt)}")
    out.append(f"{indent}}}")
    return out


def gen_rust(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    locale_path = opts.get("locale_path", "crate::Locale")
    complete = opts.get("complete_name", "FR_COMPLETE")
    use = f"use {locale_path};" if locale_path.rsplit("::", 1)[-1] == "Locale" else f"use {locale_path} as Locale;"
    L = [f"// {generated_line(cats)}", "", use, ""]
    L.append("/// True when every entry has French; until then the French arm carries the English.")
    L.append(f"pub const {complete}: bool = {'true' if fr_complete(entries) else 'false'};")
    for e in entries:
        params = ["l: Locale"] + [f"{rust_ident(n)}: {'i64' if t == 'int' else '&str'}" for n, t in e.args.items()]
        ret = "String" if e.args else "&'static str"
        L.append("")
        L.append(f"/// `{e.key}`")
        L.append("#[must_use]")
        if len(params) > 7:
            L.append("#[allow(clippy::too_many_arguments)]")
        L.append(f"pub fn {rust_name(e)}({', '.join(params)}) -> {ret} {{")
        L.append("    match l {")
        fr_lang, fr_value = fr_arm(e)
        for variant, lang, value in (("En", "en", e.en), ("FrCa", fr_lang, fr_value)):
            arm = rust_arm(lang, value, e, "            ", opts.get("int_fmt"))
            if len(arm) == 1:
                L.append(f"        Locale::{variant} => {arm[0].strip()},")
            else:
                L.append(f"        Locale::{variant} => {{")
                L.extend(arm)
                L.append("        }")
        L.append("    }")
        L.append("}")
    return [(out, "\n".join(L) + "\n")]


# swift


def swift_ident(name: str) -> str:
    return f"`{name}`" if name in SWIFT_KEYWORDS else name


def swift_literal(text: str, has_args: bool, args: dict | None = None, int_fmt: str | None = None) -> str:
    """With `int_fmt`, an int arg renders as `\\(int_fmt(n))` (Int -> String, reading the locale itself)."""
    if not has_args:
        return '"' + escape_c_like(unbrace(text), "swift") + '"'
    out = []
    for k, v in parse_text(text)[0]:
        if k == "lit":
            out.append(escape_c_like(v, "swift"))
        elif int_fmt and (args or {}).get(v) == "int":
            out.append(f"\\({int_fmt}({swift_ident(v)}))")
        else:
            out.append("\\(" + swift_ident(v) + ")")
    return '"' + "".join(out) + '"'


def swift_plural_body(lang: str, value: dict, var: str, indent: str, args: dict, int_fmt: str | None) -> list[str]:
    groups = plural_groups(lang, value)
    out = []
    for cats, text in groups[:-1]:
        out.append(f"{indent}if {plural_condition(lang, cats, var)} {{")
        out.append(f"{indent}    return {swift_literal(text, True, args, int_fmt)}")
        out.append(f"{indent}}}")
    out.append(f"{indent}return {swift_literal(groups[-1][1], True, args, int_fmt)}")
    return out


def gen_swift(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    suffix = opts.get("enum_suffix", "Copy")
    locale = opts.get("locale_expr", "L10nRuntime.locale")
    runtime = opts.get("runtime", "generated")
    access = opts.get("access", "public")
    complete = opts.get("complete_name", "FR_COMPLETE")
    int_fmt = opts.get("int_fmt")
    if runtime not in ("generated", "external"):
        raise L10nError([f"swift: opt runtime must be generated or external, not {runtime!r}"])
    if access not in ("public", "internal", "package"):
        raise L10nError([f"swift: opt access must be public, internal or package, not {access!r}"])
    a = access + " "
    L = [f"// {generated_line(cats)}", "// swiftlint:disable all", ""]
    if runtime == "generated":
        L += [
            f"{a}enum L10nLocale: Equatable, Sendable {{",
            "    case en",
            "    case frCA",
            "}",
            "",
            f"{a}enum L10nRuntime {{",
            "    /// The app sets this once at launch and whenever the user changes language.",
            f"    {a}nonisolated(unsafe) static var locale: L10nLocale = .en",
            "}",
            "",
        ]
    L.append("/// True when every entry has French; until then the French branch carries the English.")
    L.append(f"{a}let {complete}: Bool = {'true' if fr_complete(entries) else 'false'}")
    groups: dict[str, list[Entry]] = {}
    for e in entries:
        groups.setdefault(swift_name(e, suffix)[0], []).append(e)
    for enum in sorted(groups):
        L += ["", f"{a}enum {enum} {{"]
        for i, e in enumerate(groups[enum]):
            member = swift_ident(swift_name(e, suffix)[1])
            fr_lang, fr_value = fr_arm(e)
            if i:
                L.append("")
            L.append(f"    /// `{e.key}`")
            if not e.args:
                L.append(
                    f"    {a}static var {member}: String {{ {locale} == .frCA ? "
                    f"{swift_literal(fr_value, False)} : {swift_literal(e.en, False)} }}"
                )
                continue
            params = ", ".join(f"_ {swift_ident(n)}: {'Int' if t == 'int' else 'String'}" for n, t in e.args.items())
            L.append(f"    {a}static func {member}({params}) -> String {{")
            if not e.plural:
                L.append(
                    f"        {locale} == .frCA ? {swift_literal(fr_value, True, e.args, int_fmt)} : "
                    f"{swift_literal(e.en, True, e.args, int_fmt)}"
                )
            else:
                var = swift_ident(e.selector())
                L.append(f"        if {locale} == .frCA {{")
                L += swift_plural_body(fr_lang, fr_value, var, "            ", e.args, int_fmt)
                L.append("        }")
                L += swift_plural_body("en", e.en, var, "        ", e.args, int_fmt)
            L.append("    }")
        L.append("}")
    return [(out, "\n".join(L) + "\n")]


# csharp


def csharp_ident(name: str) -> str:
    return "@" + name if name in CSHARP_KEYWORDS else name


def csharp_expr(text: str, has_args: bool, args: dict, int_fmt: str | None = None) -> str:
    """With `int_fmt`, an int arg renders as `int_fmt(n)` (long -> string)."""
    if not has_args:
        return '"' + escape_c_like(unbrace(text), "csharp") + '"'
    pieces = []
    for k, v in parse_text(text)[0]:
        if k == "lit":
            pieces.append('"' + escape_c_like(v, "csharp") + '"')
        elif args.get(v) == "int" and int_fmt:
            pieces.append(f"{int_fmt}({csharp_ident(v)})")
        elif args.get(v) == "int":
            pieces.append(f"{csharp_ident(v)}.ToString(CultureInfo.InvariantCulture)")
        else:
            pieces.append(csharp_ident(v))
    if not pieces:
        return '""'
    if len(pieces) == 1 and pieces[0].startswith('"'):
        return pieces[0]
    return "string.Concat(" + ", ".join(pieces) + ")" if len(pieces) > 1 else pieces[0]


def csharp_plural_body(lang: str, value: dict, e: Entry, indent: str, int_fmt: str | None) -> list[str]:
    var = csharp_ident(e.selector())
    groups = plural_groups(lang, value)
    out = []
    for cats, text in groups[:-1]:
        out.append(f"{indent}if ({plural_condition(lang, cats, var)})")
        out.append(f"{indent}{{")
        out.append(f"{indent}    return {csharp_expr(text, True, e.args, int_fmt)};")
        out.append(f"{indent}}}")
    out.append(f"{indent}return {csharp_expr(groups[-1][1], True, e.args, int_fmt)};")
    return out


def gen_csharp(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    ns = opts.get("namespace")
    runtime = opts.get("runtime", "generated")
    complete = opts.get("complete_name", "FrComplete")
    int_fmt = opts.get("int_fmt")
    if runtime not in ("generated", "external"):
        raise L10nError([f"csharp: opt runtime must be generated or external, not {runtime!r}"])
    body: list[str] = []
    if runtime == "generated":
        body += [
            "/// <summary>The languages the generated strings know.</summary>",
            "public enum Locale",
            "{",
            "    En,",
            "    FrCa,",
            "}",
            "",
            "/// <summary>The app sets this at launch and whenever the user changes language.</summary>",
            "public static Locale Current { get; set; } = Locale.En;",
            "",
        ]
    body.append("/// <summary>True when every entry has French; until then the French branch carries the English.</summary>")
    body.append(f"public const bool {complete} = {'true' if fr_complete(entries) else 'false'};")
    for e in entries:
        name = csharp_name(e)
        fr_lang, fr_value = fr_arm(e)
        body += ["", f"/// <summary><c>{e.key}</c></summary>"]
        if not e.args:
            body.append(
                f"public static string {name} => Current == Locale.FrCa ? "
                f"{csharp_expr(fr_value, False, {})} : {csharp_expr(e.en, False, {})};"
            )
            continue
        params = ", ".join(f"{'long' if t == 'int' else 'string'} {csharp_ident(n)}" for n, t in e.args.items())
        if not e.plural:
            body.append(
                f"public static string {name}({params}) => Current == Locale.FrCa ? "
                f"{csharp_expr(fr_value, True, e.args, int_fmt)} : {csharp_expr(e.en, True, e.args, int_fmt)};"
            )
            continue
        body += [f"public static string {name}({params})", "{", "    if (Current == Locale.FrCa)", "    {"]
        body += csharp_plural_body(fr_lang, fr_value, e, "        ", int_fmt)
        body.append("    }")
        body += csharp_plural_body("en", e.en, e, "    ", int_fmt)
        body.append("}")
    uses_culture = not int_fmt and any(t == "int" for e in entries for t in e.args.values())
    L = ["// <auto-generated>", f"// {generated_line(cats)}", "// </auto-generated>", "#nullable enable", ""]
    if uses_culture:
        L += ["using System.Globalization;", ""]
    indent = ""
    if ns:
        if not re.match(r"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\Z", ns):
            raise L10nError([f"csharp: opt namespace {ns!r} is not a valid namespace"])
        L += [f"namespace {ns}", "{"]
        indent = "    "
    L += [f"{indent}public static partial class S", f"{indent}{{"]
    L += [f"{indent}    {line}" if line else "" for line in body]
    L.append(f"{indent}}}")
    if ns:
        L.append("}")
    return [(out, "\n".join(L) + "\n")]


# xml helpers


def xml_text(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def xml_attr(s: str) -> str:
    return xml_text(s).replace('"', "&quot;")


# android


def android_escape(text: str, args: dict | None, int_as_string: bool = False) -> str:
    """Android string resource text; `args` given means the string goes through String.format.

    With `int_as_string`, an int arg is `%N$s`: the caller passes the formatted number as the format
    arg and the raw int as the quantity to getQuantityString."""
    if args:
        order = list(args)
        pieces = []
        for k, v in parse_text(text)[0]:
            if k == "lit":
                pieces.append(("lit", v))
            else:
                spec = "d" if args[v] == "int" and not int_as_string else "s"
                pieces.append(("raw", f"%{order.index(v) + 1}${spec}"))
    else:
        pieces = [("lit", unbrace(text))]
    out: list[str] = []
    flat = "".join(v for _, v in pieces)
    pos = 0
    for kind, v in pieces:
        if kind == "raw":
            out.append(v)
            pos += len(v)
            continue
        for ch in v:
            at_edge = pos == 0 or pos == len(flat) - 1
            prev_space = pos > 0 and flat[pos - 1] == " "
            if ch == "\\":
                out.append("\\\\")
            elif ch == "'":
                out.append("\\'")
            elif ch == '"':
                out.append('\\"')
            elif ch == "\n":
                out.append("\\n")
            elif ch == "\t":
                out.append("\\t")
            elif ch in "@?" and pos == 0:
                out.append("\\" + ch)
            elif ch == "%" and args:
                out.append("%%")
            elif ch == "&":
                out.append("&amp;")
            elif ch == "<":
                out.append("&lt;")
            elif ch == ">":
                out.append("&gt;")
            elif ch == " " and (at_edge or prev_space):
                out.append("\\u0020")  # aapt trims edges and collapses runs of spaces
            elif ch == " " or ch.isprintable():
                out.append(ch)
            else:
                cp = ord(ch)
                if cp > 0xFFFF:
                    cp -= 0x10000
                    out.append(f"\\u{0xD800 + (cp >> 10):04X}\\u{0xDC00 + (cp & 0x3FF):04X}")
                else:
                    out.append(f"\\u{cp:04X}")
            pos += 1
    return "".join(out)


def android_file(cats, entries, lang: str, int_as_string: bool = False) -> str:
    tools_ns = lang == "en" and untranslated(entries)
    L = ['<?xml version="1.0" encoding="utf-8"?>', f"<!-- {generated_line(cats)} -->"]
    if tools_ns:
        L.append('<resources xmlns:tools="http://schemas.android.com/tools" tools:ignore="MissingTranslation">')
    else:
        L.append("<resources>")
    for e in entries:
        value = e.en if lang == "en" else e.fr
        if value is None:
            continue
        name = android_name(e)
        fixed = ' translatable="false"' if e.fixed else ""
        if not e.plural:
            text = android_escape(value, e.args or None, int_as_string)
            extra = fixed + (' formatted="false"' if not e.args and "%" in value else "")
            L.append(f'    <string name="{name}"{extra}>{text}</string>')
            continue
        L.append(f'    <plurals name="{name}"{fixed}>')
        forms = ("one", "other") if lang == "en" else ("one", "many", "other")
        for form in forms:
            text = value.get(form, value["other"])
            L.append(f'        <item quantity="{form}">{android_escape(text, e.args, int_as_string)}</item>')
        L.append("    </plurals>")
    L.append("</resources>")
    return "\n".join(L) + "\n"


def gen_android(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    name = opts.get("name", "_".join(c.name for c in cats))
    flag = opts.get("int_as_string", "false")
    if flag not in ("true", "false"):
        raise L10nError([f"android: opt int_as_string must be true or false, not {flag!r}"])
    as_string = flag == "true"
    missing = untranslated(entries)
    if missing and not opts.get("_quiet"):
        print(f"android: {missing} entries without fr left out of values-fr-rCA/{name}.xml", file=sys.stderr)
    return [
        (out / "values" / f"{name}.xml", android_file(cats, entries, "en", as_string)),
        (out / "values-fr-rCA" / f"{name}.xml", android_file(cats, entries, "fr", as_string)),
    ]


# po


def po_str(s: str) -> str:
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
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


FR_PLURAL_FORMS = "nplurals=3; plural=(n == 0 || n == 1) ? 0 : (n != 0 && n % 1000000 == 0) ? 1 : 2;"
EN_PLURAL_FORMS = "nplurals=2; plural=(n != 1);"


def po_file(cats, entries, lang: str | None) -> str:
    project = "_".join(c.name for c in cats)
    head = [
        f"Project-Id-Version: {project}",
        "Report-Msgid-Bugs-To: ",
        f"Last-Translator: {TOOL}",
        f"Language-Team: {'fr-CA' if lang else 'none'}",
        f"Language: {'fr_CA' if lang else ''}",
        "MIME-Version: 1.0",
        "Content-Type: text/plain; charset=UTF-8",
        "Content-Transfer-Encoding: 8bit",
        f"Plural-Forms: {FR_PLURAL_FORMS if lang else EN_PLURAL_FORMS}",
    ]
    L = [f"# {generated_line(cats)}", 'msgid ""', 'msgstr ""'] + [po_str(h + "\n") for h in head]
    for e in entries:
        L.append("")
        L.append(f"#. screen: {e.fields['screen']}; ctx: {e.fields['ctx']}; tier: {e.tier}")
        for line in e.fields.get("note", "").splitlines():
            L.append(f"#. {line}".rstrip())
        L.append(f"msgctxt {po_str(e.key)}")
        if not e.plural:
            L.append(f"msgid {po_str(e.en)}")
            fr = e.en if e.fixed else e.fr
            L.append(f"msgstr {po_str(fr if lang and fr is not None else '')}")
            continue
        L.append(f"msgid {po_str(e.en['one'])}")
        L.append(f"msgid_plural {po_str(e.en['other'])}")
        if lang:
            fr = e.en if e.fixed else (e.fr or {})
            forms = [fr.get("one", ""), fr.get("many", fr.get("other", "")), fr.get("other", "")]
        else:
            forms = ["", ""]
        for i, text in enumerate(forms):
            L.append(f"msgstr[{i}] {po_str(text)}")
    return "\n".join(L) + "\n"


def gen_po(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    name = opts.get("name", "_".join(c.name for c in cats))
    return [(out / f"{name}.pot", po_file(cats, entries, None)), (out / "fr.po", po_file(cats, entries, "fr"))]


# apple-strings


def strings_escape(s: str) -> str:
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
        elif ch == " " or ch.isprintable():
            out.append(ch)
        else:
            cp = ord(ch)
            if cp > 0xFFFF:
                cp -= 0x10000
                out.append(f"\\U{0xD800 + (cp >> 10):04X}\\U{0xDC00 + (cp & 0x3FF):04X}")
            else:
                out.append(f"\\U{cp:04X}")
    return "".join(out)


def display_forms(e: Entry, lang: str) -> list[tuple[str, str]]:
    """(name, text) per string for flat formats; plain strings unbraced, others raw catalog text."""
    value = e.en if lang == "en" else (e.fr if e.fr is not None else e.en)
    if not e.plural:
        return [("", value if e.args else unbrace(value))]
    return [(f, value.get(f, value["other"])) for f in PLURAL_ORDER]


def gen_apple_strings(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    name = opts.get("name", "_".join(c.name for c in cats))
    missing = untranslated(entries)
    if missing and not opts.get("_quiet"):
        print(f"apple-strings: {missing} entries without fr carry the English in fr-CA.lproj/{name}.strings", file=sys.stderr)
    files = []
    for lang, folder in (("en", "en.lproj"), ("fr", "fr-CA.lproj")):
        L = [f"/* {generated_line(cats)} */"]
        for e in entries:
            names = apple_names(e)
            for i, (_, text) in enumerate(display_forms(e, lang)):
                L.append(f'"{strings_escape(names[i])}" = "{strings_escape(text)}";')
        files.append((out / folder / f"{name}.strings", "\n".join(L) + "\n"))
    return files


# resw


RESW_HEADER = [
    '  <resheader name="resmimetype">',
    "    <value>text/microsoft-resx</value>",
    "  </resheader>",
    '  <resheader name="version">',
    "    <value>2.0</value>",
    "  </resheader>",
    '  <resheader name="reader">',
    "    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>",
    "  </resheader>",
    '  <resheader name="writer">',
    "    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>",
    "  </resheader>",
]


def resw_escape(s: str) -> str:
    out = []
    for ch in xml_text(s):
        if ch in "\n\t" or not (ch == " " or ch.isprintable()):
            cp = ord(ch)
            if cp < 0x20 and ch not in "\n\t\r":
                raise L10nError([f"resw: control character U+{cp:04X} cannot be written to XML"])
            out.append(f"&#x{cp:X};")
        else:
            out.append(ch)
    return "".join(out)


def gen_resw(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    name = opts.get("name", "_".join(c.name for c in cats))
    missing = untranslated(entries)
    if missing and not opts.get("_quiet"):
        print(f"resw: {missing} entries without fr carry the English in fr-CA/{name}.resw", file=sys.stderr)
    files = []
    for lang, folder in (("en", "en-US"), ("fr", "fr-CA")):
        L = ['<?xml version="1.0" encoding="utf-8"?>', f"<!-- {generated_line(cats)} -->", "<root>"] + RESW_HEADER
        for e in entries:
            names = resw_names(e)
            for i, (_, text) in enumerate(display_forms(e, lang)):
                L.append(f'  <data name="{xml_attr(names[i])}" xml:space="preserve">')
                L.append(f"    <value>{resw_escape(text)}</value>")
                L.append(f"    <comment>{xml_text(e.key)}</comment>")
                L.append("  </data>")
        L.append("</root>")
        files.append((out / folder / f"{name}.resw", "\n".join(L) + "\n"))
    return files


# json


def gen_json(cats, entries, opts, out: Path) -> list[tuple[Path, str]]:
    obj = {}
    for e in entries:
        obj[e.key] = {
            "en": e.en,
            "fr": e.fr,
            "ctx": e.fields["ctx"],
            "screen": e.fields["screen"],
            "tier": e.tier,
            "args": e.args,
            "state": e.fields.get("state"),
        }
        if e.fixed:
            obj[e.key]["fixed"] = True
    return [(out, json.dumps(obj, ensure_ascii=False, indent=2) + "\n")]


BACKENDS = {
    "rust": (gen_rust, {"locale_path", "complete_name", "int_fmt"}),
    "swift": (gen_swift, {"enum_suffix", "locale_expr", "runtime", "access", "complete_name", "int_fmt"}),
    "csharp": (gen_csharp, {"namespace", "runtime", "complete_name", "int_fmt"}),
    "android": (gen_android, {"name", "int_as_string"}),
    "po": (gen_po, {"name"}),
    "apple-strings": (gen_apple_strings, {"name"}),
    "resw": (gen_resw, {"name"}),
    "json": (gen_json, set()),
}


def parse_opts(raw: list[str] | None) -> dict[str, str]:
    opts = {}
    for item in raw or []:
        if "=" not in item:
            raise L10nError([f"--opt {item!r}: expected key=value"])
        k, v = item.split("=", 1)
        opts[k] = v
    return opts


def generate(backend: str, cats: list[Catalog], opts: dict, out: Path) -> list[tuple[Path, str]]:
    if backend not in BACKENDS:
        raise L10nError([f"unknown backend {backend!r} (one of {' '.join(BACKENDS)})"])
    fn, known = BACKENDS[backend]
    unknown = sorted(k for k in opts if k not in known and not k.startswith("_"))
    if unknown:
        raise L10nError([f"{backend}: unknown opt {', '.join(unknown)} (known: {' '.join(sorted(known)) or 'none'})"])
    return fn(cats, all_entries(cats), opts, out)


# ---------------------------------------------------------------------------------------------
# Commands


def cmd_fmt(args) -> int:
    cats = load_all(args.catalogs)
    problems = Problems()
    for cat in cats:
        try:
            text = emit_catalog(cat)
        except L10nError as exc:
            problems.lines.extend(exc.lines)
            continue
        if cat.path.read_bytes() != text.encode("utf-8"):
            cat.path.write_bytes(text.encode("utf-8"))
            print(f"formatted {cat.path}")
    return problems.emit()


def cmd_check(args) -> int:
    cats = load_all(args.catalogs)
    problems = validate(cats)
    for cat in cats:
        if cat.structural:
            continue
        try:
            text = emit_catalog(cat)
        except L10nError as exc:
            problems.lines.extend(exc.lines)
            continue
        current = cat.path.read_bytes().decode("utf-8")
        if current != text:
            problems.add(cat.path, None, f"not canonical from line {first_difference(current, text)} (run l10n.py fmt)")
    if args.require:
        for e in all_entries(cats):
            if e.fixed:
                continue
            if e.fr is None:
                problems.add(e.catalog.path, e.key, "fr-CA required: fr is missing")
            elif e.fields.get("state") not in DONE_STATES:
                problems.add(e.catalog.path, e.key, f"fr-CA required: state is {e.fields.get('state', 'unset')!r}, needs reviewed or signed")
    if args.gen and not problems:
        opts = parse_opts(args.opt)
        opts["_quiet"] = "1"
        all_known = set().union(*(BACKENDS[b.split(':', 1)[0]][1] for b in args.gen if b.split(':', 1)[0] in BACKENDS))
        for k in opts:
            if k not in all_known and not k.startswith("_"):
                problems.add("--opt", None, f"{k!r} is not an opt of any --gen backend")
        for spec in args.gen:
            if ":" not in spec:
                problems.add("--gen", None, f"{spec!r}: expected BACKEND:OUT")
                continue
            backend, out = spec.split(":", 1)
            if backend not in BACKENDS:
                problems.add("--gen", None, f"unknown backend {backend!r}")
                continue
            known = BACKENDS[backend][1]
            try:
                files = generate(backend, cats, {k: v for k, v in opts.items() if k in known or k.startswith("_")}, Path(out))
            except L10nError as exc:
                problems.lines.extend(exc.lines)
                continue
            for path, content in files:
                if not path.exists():
                    problems.add(path, None, f"missing; run l10n.py gen --backend {backend}")
                elif path.read_bytes() != content.encode("utf-8"):
                    problems.add(path, None, f"differs from what gen --backend {backend} writes (run l10n.py gen)")
    return problems.emit()


def cmd_gen(args) -> int:
    cats = load_valid(args.catalogs)
    files = generate(args.backend, cats, parse_opts(args.opt), Path(args.out))
    for path, content in files:
        path.parent.mkdir(parents=True, exist_ok=True)
        data = content.encode("utf-8")
        if not path.exists() or path.read_bytes() != data:
            path.write_bytes(data)
            print(f"wrote {path}")
    return 0


def jsonl_dumps(obj) -> str:
    """One JSON line, UTF-8 kept readable but every invisible character escaped (U+00A0 -> \\u00a0)."""
    out = []
    for ch in json.dumps(obj, ensure_ascii=False):
        if ch == " " or ch.isprintable():
            out.append(ch)
        else:
            out.append(json.dumps(ch)[1:-1])
    return "".join(out)


def normalize_en(s: str) -> str:
    """Runs of ASCII whitespace become one space; U+00A0 is kept and U+202F becomes U+00A0."""
    return re.sub(r"[ \t\r\n\f\v]+", " ", s).replace("\u202f", "\u00a0")


def memory_key(en) -> str:
    """A string's normalized English; a plural's is the JSON of its normalized forms, sorted keys."""
    if isinstance(en, dict):
        return json.dumps({k: normalize_en(v) for k, v in en.items()}, sort_keys=True)
    return normalize_en(en)


def load_tm(path: Path) -> list[dict]:
    rows = []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        raise L10nError([f"{path}: cannot read: {exc.strerror}"]) from exc
    bad = []
    for i, line in enumerate(lines, start=1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as exc:
            bad.append(f"{path}:{i}: not JSON: {exc.msg}")
            continue
        if not isinstance(row, dict) or not _tm_text(row.get("en")) or not _tm_text(row.get("fr")):
            bad.append(f"{path}:{i}: a memory row needs en and fr (strings, or plural objects of strings)")
            continue
        rows.append(row)
    if bad:
        raise L10nError(bad)
    return rows


def _tm_text(v) -> bool:
    return isinstance(v, str) or (isinstance(v, dict) and bool(v) and all(isinstance(x, str) for x in v.values()))


class Memory:
    def __init__(self, rows: list[dict]) -> None:
        self.rows = rows
        self.exact: dict[tuple[str, str], list[dict]] = {}
        for r in rows:
            self.exact.setdefault((memory_key(r["en"]), r.get("ctx", "*")), []).append(r)
        self.by_en: dict[str, list[dict]] = {}
        for r in rows:
            self.by_en.setdefault(memory_key(r["en"]), []).append(r)

    @staticmethod
    def _row(r: dict, match: str, score: float | None = None) -> dict:
        out = {"match": match, "en": r["en"], "fr": r["fr"], "ctx": r.get("ctx", "*")}
        if r.get("state"):
            out["state"] = r["state"]
        if score is not None:
            out["score"] = round(score, 3)
        return out

    def lookup(self, en, ctx: str) -> list[dict]:
        key = memory_key(en)
        out = [self._row(r, "exact") for r in self.exact.get((key, ctx), [])]
        if ctx != "*":
            out += [self._row(r, "exact-any-ctx") for r in self.exact.get((key, "*"), [])]
        scored = []
        sm = difflib.SequenceMatcher(autojunk=False)
        sm.set_seq2(key)
        for cand, rows in self.by_en.items():
            if cand == key:
                continue
            sm.set_seq1(cand)
            if sm.real_quick_ratio() < 0.8 or sm.quick_ratio() < 0.8:
                continue
            ratio = sm.ratio()
            if ratio >= 0.8:
                for r in rows:
                    scored.append((-ratio, cand, r.get("ctx", "*"), r))
        scored.sort(key=lambda t: t[:3])
        out += [self._row(r, "fuzzy", -neg) for neg, _, _, r in scored[:3]]
        return out


def default_tm_path() -> Path:
    return Path(__file__).resolve().parent.parent / "fr-CA" / "tm.jsonl"


def export_units(cats: list[Catalog], tier: str | None, memory: Memory) -> list[dict]:
    entries = all_entries(cats)
    by_screen: dict[str, list[Entry]] = {}
    for e in entries:
        by_screen.setdefault(e.fields["screen"], []).append(e)
    units = []
    for e in entries:
        if e.fr is not None or e.fixed or (tier and e.tier != tier):
            continue
        siblings = []
        for s in by_screen[e.fields["screen"]]:
            if s.key == e.key:
                continue
            sib = {"key": s.key, "en": s.en}
            if s.fr is not None:
                sib["fr"] = s.fr
            siblings.append(sib)
            if len(siblings) == 8:
                break
        meta = e.catalog.meta or {}
        units.append({
            "key": e.key,
            "catalog": e.catalog.name,
            "platform": meta.get("platform"),
            "apple": meta.get("apple", False),
            "en": e.en,
            "ctx": e.fields["ctx"],
            "screen": e.fields["screen"],
            "tier": e.tier,
            "note": e.fields.get("note", ""),
            "args": e.args,
            "max": e.max_len(),
            "siblings": siblings,
            "tm": memory.lookup(e.en, e.fields["ctx"]),
        })
    return units


def batch_units(units: list[dict], size: int | None) -> list[list[dict]]:
    """Packs whole screens into batches of at most `size` units; a larger screen is its own batch."""
    screens: dict[str, list[dict]] = {}
    for u in units:
        screens.setdefault(u["screen"], []).append(u)
    if not size:
        return [units] if units else []
    batches: list[list[dict]] = []
    cur: list[dict] = []
    for group in screens.values():
        if cur and len(cur) + len(group) > size:
            batches.append(cur)
            cur = []
        cur = cur + group
        if len(cur) >= size:
            batches.append(cur)
            cur = []
    if cur:
        batches.append(cur)
    return batches


def cmd_export(args) -> int:
    cats = load_valid(args.catalogs)
    if args.batch_size is not None and args.batch_size < 1:
        raise L10nError(["--batch-size must be at least 1"])
    tm_path = Path(args.tm) if args.tm else default_tm_path()
    rows = load_tm(tm_path) if (args.tm or tm_path.exists()) else []
    units = export_units(cats, args.tier, Memory(rows))
    batches = batch_units(units, args.batch_size)
    if not args.out_dir:
        for batch in batches:
            for u in batch:
                sys.stdout.write(jsonl_dumps(u) + "\n")
        return 0
    out_dir = Path(args.out_dir)
    targets = [out_dir / f"B{args.first_batch + i:02d}" / "in.jsonl" for i in range(len(batches))]
    existing = [t for t in targets if t.exists()]
    if existing:
        raise L10nError([f"{t}: already exists; refusing to overwrite" for t in existing])
    for target, batch in zip(targets, batches):
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("".join(jsonl_dumps(u) + "\n" for u in batch), encoding="utf-8")
        print(f"wrote {target} ({len(batch)} units)", file=sys.stderr)
    return 0


def cmd_import(args) -> int:
    cats = load_all(args.catalog)
    pre = validate(cats)
    if pre:
        raise L10nError(pre.lines)
    by_name = {c.name: c for c in cats}
    problems = Problems()
    src = Path(args.out)
    try:
        lines = src.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        raise L10nError([f"{src}: cannot read: {exc.strerror}"]) from exc
    seen: set[tuple[str, str]] = set()
    updates: list[tuple[Entry, object]] = []
    for lineno, line in enumerate(lines, start=1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as exc:
            problems.add(src, None, f"line {lineno}: not JSON: {exc.msg}")
            continue
        key = row.get("key") if isinstance(row, dict) else None
        if not isinstance(key, str) or "fr" not in row:
            problems.add(src, None, f"line {lineno}: a row needs key and fr")
            continue
        cname = row.get("catalog")
        if cname is None and len(cats) == 1:
            cname = cats[0].name
        cat = by_name.get(cname)
        if cat is None:
            problems.add(src, key, f"line {lineno}: catalog {cname!r} is not among --catalog {' '.join(sorted(by_name))}")
            continue
        entry = cat.entries.get(key)
        if entry is None:
            problems.add(src, key, f"line {lineno}: unknown key in catalog {cat.name!r}")
            continue
        if entry.fixed:
            problems.add(src, key, f"line {lineno}: a fixed entry is never translated")
            continue
        if (cat.name, key) in seen:
            problems.add(src, key, f"line {lineno}: key appears twice")
            continue
        seen.add((cat.name, key))
        fr = row["fr"]
        reasons: list[str] = []
        if entry.plural:
            if not isinstance(fr, dict):
                reasons.append("plural shape mismatch: en is plural, fr must be an object with one and other")
            else:
                msg = _forms_problem(fr, FR_FORMS_REQUIRED, FR_FORMS_ALLOWED, "fr")
                if msg:
                    reasons.append(f"plural shape mismatch: {msg}")
                elif any(not isinstance(t, str) or not t for t in fr.values()):
                    reasons.append("plural shape mismatch: every fr form must be a non-empty string")
        elif not isinstance(fr, str) or not fr:
            reasons.append("shape mismatch: en is a string, fr must be a non-empty string")
        if not reasons:
            check_text_parity(entry, fr, lambda r: reasons.append(f"placeholder mismatch: {r}"))
        for r in reasons:
            problems.add(src, key, f"line {lineno}: {r}")
        if not reasons:
            updates.append((entry, fr))
    if problems:
        return problems.emit()
    for entry, fr in updates:
        entry.fields["fr"] = fr
        entry.fields["state"] = args.state
    post = validate(cats)
    if post:
        raise L10nError(post.lines)
    touched = {id(e.catalog): e.catalog for e, _ in updates}
    for cat in touched.values():
        cat.path.write_bytes(emit_catalog(cat).encode("utf-8"))
        print(f"wrote {cat.path}", file=sys.stderr)
    print(f"imported {len(updates)} entries as {args.state}", file=sys.stderr)
    return 0


VENDORED = ("l10n.py", "frcheck.py", "pseudo.py")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(src: Path, *argv: str) -> str:
    try:
        done = subprocess.run(["git", "-C", str(src), *argv], capture_output=True, text=True, check=True)
    except (OSError, subprocess.CalledProcessError) as exc:
        raise L10nError([f"{src}: git {' '.join(argv)} failed; vendor runs from a monorepo checkout"]) from exc
    return done.stdout.strip()


def cmd_vendor(args) -> int:
    dest = Path(args.dest)
    if args.check:
        return vendor_check(dest)
    src = Path(__file__).resolve().parent
    if dest.resolve() == src:
        raise L10nError([f"{dest}: is the source directory"])
    files = [n for n in VENDORED if (src / n).exists()]
    commit = git(src, "rev-parse", "HEAD")
    top = Path(git(src, "rev-parse", "--show-toplevel"))
    dirty = bool(git(src, "status", "--porcelain", "--", *files))
    dest.mkdir(parents=True, exist_ok=True)
    for n in files:
        shutil.copyfile(src / n, dest / n)
    date = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    source = {
        "commit": commit,
        "dirty": dirty,
        "source": src.relative_to(top).as_posix(),
        "date": date,
        "files": files,
    }
    text = f"# {TOOL} vendor wrote this; l10n.py vendor --check verifies SHA256SUMS.\n"
    text += "".join(f"{k} = {toml_value(v)}\n" for k, v in source.items())
    (dest / "SOURCE.toml").write_text(text, encoding="utf-8")
    (dest / "SHA256SUMS").write_text("".join(f"{sha256(dest / n)}  {n}\n" for n in sorted(files)), encoding="utf-8")
    print(f"vendored {' '.join(files)} at {commit[:12]}{' (dirty)' if dirty else ''} into {dest}", file=sys.stderr)
    return 0


def vendor_check(dest: Path) -> int:
    problems = Problems()
    sums = dest / "SHA256SUMS"
    source = dest / "SOURCE.toml"
    if not source.exists():
        problems.add(source, None, "missing")
    if not sums.exists():
        problems.add(sums, None, "missing")
        return problems.emit()
    recorded: dict[str, str] = {}
    for i, line in enumerate(sums.read_text(encoding="utf-8").splitlines(), start=1):
        m = re.match(r"([0-9a-f]{64})  (\S+)\Z", line)
        if not m:
            problems.add(sums, None, f"line {i}: not `<sha256>  <file>`")
            continue
        recorded[m.group(2)] = m.group(1)
    if source.exists():
        try:
            meta = tomllib.loads(source.read_text(encoding="utf-8"))
            if sorted(meta.get("files", [])) != sorted(recorded):
                problems.add(source, None, "files differ from SHA256SUMS")
        except tomllib.TOMLDecodeError as exc:
            problems.add(source, None, f"TOML parse error: {exc}")
    for name, digest in sorted(recorded.items()):
        path = dest / name
        if not path.exists():
            problems.add(path, None, "missing")
        elif sha256(path) != digest:
            problems.add(path, None, "modified since vendoring (sha256 differs); re-vendor from the monorepo")
    for name in VENDORED:
        if (dest / name).exists() and name not in recorded:
            problems.add(dest / name, None, "not recorded in SHA256SUMS")
    return problems.emit()


def cmd_stats(args) -> int:
    cats = load_all(args.catalogs)
    total = []
    for cat in cats:
        entries = list(cat.entries.values())
        total += entries
        print(f"{cat.path} ({cat.name}): {len(entries)} entries, {untranslated(entries)} fr missing, "
              f"{sum(e.fixed for e in entries)} fixed")
    if len(cats) > 1:
        print(f"total: {len(total)} entries, {untranslated(total)} fr missing, {sum(e.fixed for e in total)} fixed")
    print("by tier:")
    for t in TIERS:
        es = [e for e in total if e.tier == t]
        if es:
            print(f"  {t}: {len(es)} entries, {untranslated(es)} fr missing")
    print("by state:")
    counts: dict[str, int] = {}
    for e in total:
        s = "fixed" if e.fixed else "fr missing" if e.fr is None else str(e.fields.get("state", "unset"))
        counts[s] = counts.get(s, 0) + 1
    for s in (*STATES, "unset", "fr missing", "fixed"):
        if counts.get(s):
            print(f"  {s}: {counts[s]}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="l10n.py",
        description="String catalogs (i18n/README.md) to typed accessors per platform, with fr-CA export and import.",
    )
    sub = p.add_subparsers(dest="command", required=True, metavar="COMMAND")

    s = sub.add_parser("fmt", help="rewrite catalogs in canonical order and quoting, in place")
    s.add_argument("catalogs", nargs="+")
    s.set_defaults(func=cmd_fmt)

    s = sub.add_parser("check", help="fail on an invalid or non-canonical catalog, missing French, or a stale generated file")
    s.add_argument("catalogs", nargs="+")
    s.add_argument("--require", choices=["fr-CA"], help="every entry needs fr with state reviewed or signed")
    s.add_argument("--gen", action="append", metavar="BACKEND:OUT", help="a generated output that must match byte for byte")
    s.add_argument("--opt", action="append", metavar="K=V", help="backend option for every --gen that knows it")
    s.set_defaults(func=cmd_check)

    s = sub.add_parser("gen", help="generate accessors or resource files for one backend")
    s.add_argument("catalogs", nargs="+")
    s.add_argument("--backend", required=True, choices=sorted(BACKENDS))
    s.add_argument("--out", required=True, help="output file (rust swift csharp json) or directory (android po apple-strings resw)")
    s.add_argument("--opt", action="append", metavar="K=V", help="backend option, e.g. locale_path=crate::i18n::Locale")
    s.set_defaults(func=cmd_gen)

    s = sub.add_parser("export", help="write translation units (JSONL) for entries missing French")
    s.add_argument("catalogs", nargs="+")
    s.add_argument("--missing", required=True, choices=["fr-CA"])
    s.add_argument("--batch-size", type=int, help="units per batch; a screen is never split")
    s.add_argument("--tier", choices=TIERS, help="only entries of this tier")
    s.add_argument("--tm", help="translation memory (default: i18n/fr-CA/tm.jsonl when present)")
    s.add_argument("--out-dir", help="write <dir>/Bnn/in.jsonl per batch instead of stdout")
    s.add_argument("--first-batch", type=int, default=1, help="number of the first batch directory (default 1)")
    s.set_defaults(func=cmd_export)

    s = sub.add_parser("import", help="write reviewed translations (out.jsonl) into catalogs")
    s.add_argument("out", metavar="OUT_JSONL")
    s.add_argument("--catalog", action="append", required=True, help="catalog file; repeat for a batch spanning catalogs")
    s.add_argument("--state", choices=STATES, default="draft")
    s.set_defaults(func=cmd_import)

    s = sub.add_parser("vendor", help="copy the tools into an app repo with SOURCE.toml and SHA256SUMS, or verify a copy")
    s.add_argument("--dest", required=True)
    s.add_argument("--check", action="store_true", help="verify the vendored files against SHA256SUMS")
    s.set_defaults(func=cmd_vendor)

    s = sub.add_parser("stats", help="count entries, missing French, by tier and by state")
    s.add_argument("catalogs", nargs="+")
    s.set_defaults(func=cmd_stats)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except L10nError as exc:
        for line in exc.lines:
            print(line, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
