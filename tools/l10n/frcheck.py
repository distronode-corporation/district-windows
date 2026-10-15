#!/usr/bin/env python3
"""French (fr-CA) gates for the native apps' catalogs and translation batches.

    frcheck.py fix <files...>      normalize French typography in place (TOML catalogs, JSONL batches)
    frcheck.py lint <files...>     every README French rule; exit 1 on any error
    frcheck.py all <repo-root>     lint every catalog under a repo, plus cross-catalog consistency
    frcheck.py rules               print the rule table
    frcheck.py --self-test         run the unittest suites beside this file

Spec: i18n/README.md (The catalog; Term base, memory and codes; The French rules).
House style: docs/i18n_fr_ca_glossary.md. The website's own conventions, which these lists
reuse: distronode-website/src/lib/i18n/README.md and CLAUDE.md, money.ts `normalizeSpaces`.

Python 3.11+, standard library only. Invisible and typographic characters are written with
chr() so every one of them is visible in a diff.
"""
from __future__ import annotations

import argparse
import copy
import json
import math
import os
import re
import sys
import tomllib
import unicodedata
import unittest
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable

NBSP = chr(0x00A0)
NNBSP = chr(0x202F)
THIN = chr(0x2009)
EMDASH = chr(0x2014)
HBAR = chr(0x2015)
ENDASH = chr(0x2013)
RSQUO = chr(0x2019)
LAQUO = chr(0x00AB)
RAQUO = chr(0x00BB)
ELLIPSIS = chr(0x2026)
INVISIBLE = {chr(c) for c in (0x200B, 0x200C, 0x200D, 0x2060, 0xFEFF, 0x00AD)}
# money.ts `normalizeSpaces` maps both of these to U+00A0; the README names U+202F.
NARROW_SPACES = {NNBSP, THIN}
SPACE_LIKE = {" ", NBSP}
PLURAL_CATS = ("one", "other", "many")
FR_PLURAL_REQUIRED = ("one", "other")
TOOLS_DIR = Path(__file__).resolve().parent
MONOREPO_ROOT = TOOLS_DIR.parent.parent
DEFAULT_TERMBASE = MONOREPO_ROOT / "i18n" / "fr-CA" / "termbase.jsonl"
DEFAULT_TM = MONOREPO_ROOT / "i18n" / "fr-CA" / "tm.jsonl"

# id -> (default severity, one-line meaning). Some rules downgrade to "warn" in a named case,
# noted in the meaning.
RULES: dict[str, tuple[str, str]] = {
    "PARSE": ("error", "the file does not parse (TOML or JSON Lines)"),
    "PH-PARITY": ("error", "placeholder names differ between en and fr, a placeholder is not in args, or a brace is unbalanced"),
    "OS-WORD-APPLE-VARIANT": ("error", "batch unit also on Apple: fr has a non-Apple OS word (Paramètres for Settings) and needs fr_apple; or fr_apple on a unit with no Apple catalog"),
    "PLURAL-ONE-COUNT": ("error", "fr.one (which covers 0 and 1) and fr.many must contain the selecting int placeholder"),
    "PLURAL-CATS": ("error", "plural shape differs: en one/other needs fr one/other (many optional), and exactly one int arg"),
    "TYPO-NBSP": ("error", "U+00A0 before a punctuation : (one followed by a space or the end), % and $ signs, », and after «; U+202F/U+2009 become U+00A0 (fix repairs)"),
    "TYPO-SPACE-BEFORE": ("error", "no space before ; ! ? (fix repairs)"),
    "TYPO-EMDASH": ("error", "no em-dash (U+2014/U+2015) or spaced en-dash in fr; a warning for an em-dash in en"),
    "TYPO-APOS": ("error", "typographic apostrophe U+2019 in fr; use ' (fix repairs)"),
    "TYPO-NUMBER": ("error", "English-style number (1,234.5, 1,234,567); warn on a decimal point or plain-space thousands"),
    "TYPO-MONEY": ("error", "currency before the amount ($50, CA$50); fr-CA writes 50 $ CA"),
    "TYPO-TIME": ("warn", "clock time with a colon or AM/PM; fr-CA writes 9 h, 13 h 30"),
    "TYPO-INVISIBLE": ("error", "zero-width or soft-hyphen character, or a literal (unescaped) NBSP in a TOML file"),
    "CAPS-ACCENT": ("error", "capital letter missing its accent (Etat, Ecran); a warning for a bare initial A (could be avoir)"),
    "SENTENCE-CASE": ("warn", "Title Case in a title, button, menu, label or link; French uses sentence case"),
    "REG-TU": ("error", "tutoiement (tu, te, toi, ton, ta, tes, t'); vouvoiement only"),
    "NEVER": ("error", "a never-translate name in en is missing or altered in fr"),
    "TERM-STRICT": ("error", "a strict glossary term in en is not rendered with its fr term"),
    "BAN": ("error", "banned phrase (données personnelles, enregistrement, e-mail, chat, AI, cookies, termbase bans); 'enregistrement' only after a negation (aucun enregistrement)"),
    "OS-WORD": ("error", "apple catalogs say Réglages, others Paramètres; termbase os rows for the platform (a command word as the whole string only on a button, menu, link, tooltip or a11y)"),
    "UNTRANSLATED": ("error", "fr equals en without same_ok"),
    "LEN": ("warn", "fr longer than max (default ceil(len(en)*1.3), at least len(en)+4)"),
    "TM-DRIFT": ("error", "fr differs from the memory for the same en and ctx without fr_variant; warn against ctx '*' or a draft row"),
    "XCAT-DRIFT": ("error", "(all) the same en and ctx have different fr across catalogs without fr_variant"),
    "STORE-APPLE": ("error", "apple catalog, screen not allowed: web/browser/dashboard/domain/sign-up/trial wording, or a destination or offer en lacks"),
    "URL-PARITY": ("warn", "fr names a URL, domain or email address en does not"),
    "WAIVE-ERROR": ("error", "an entry's waive table names an unknown or error-only id, or gives an empty reason"),
}

# --------------------------------------------------------------------------------------------
# Findings


@dataclass
class Finding:
    file: str
    key: str
    rule: str
    message: str
    severity: str = ""

    def __post_init__(self) -> None:
        if not self.severity:
            self.severity = RULES[self.rule][0]

    def line(self) -> str:
        tag = "" if self.severity == "error" else f"({self.severity}) "  # (warn) or (waived)
        return f"{self.file}:{self.key}: {self.rule} {tag}{self.message}"

    def as_dict(self) -> dict:
        return {"file": self.file, "key": self.key, "rule": self.rule,
                "severity": self.severity, "message": self.message}


def show(s: str, limit: int = 70) -> str:
    """A value as a reader can see it: invisible spaces written as escapes."""
    out = (s.replace(NBSP, "\\u00A0").replace(NNBSP, "\\u202F").replace(THIN, "\\u2009")
           .replace("\n", "\\n"))
    for ch in INVISIBLE:
        out = out.replace(ch, f"\\u{ord(ch):04X}")
    return out if len(out) <= limit else out[: limit - 3] + "..."


# --------------------------------------------------------------------------------------------
# Placeholders: {name}; {{ and }} are literal braces.

PH_NAME = re.compile(r"[a-z][a-z0-9_]*")


def scan_placeholders(text: str) -> tuple[list[tuple[int, int, str]], list[int]]:
    """Return (placeholders as (start, end, name), positions of stray braces)."""
    found: list[tuple[int, int, str]] = []
    stray: list[int] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "{":
            if text.startswith("{{", i):
                i += 2
                continue
            close = text.find("}", i + 1)
            name = text[i + 1:close] if close != -1 else ""
            if close != -1 and PH_NAME.fullmatch(name):
                found.append((i, close + 1, name))
                i = close + 1
                continue
            stray.append(i)
        elif c == "}":
            if text.startswith("}}", i):
                i += 2
                continue
            stray.append(i)
        i += 1
    return found, stray


def placeholder_names(text: str) -> set[str]:
    return {name for _, _, name in scan_placeholders(text)[0]}


# --------------------------------------------------------------------------------------------
# Typography rules over "cells": one decoded character each, with the raw source text it came
# from (raw None = newly written, encoded by the emitter) and `pre`, raw text of zero-width
# source (a TOML line continuation) that sits before it.


@dataclass
class Cell:
    ch: str
    raw: str | None
    pre: str = ""
    prot: bool = False


def cells_of(text: str) -> list[Cell]:
    return protect(Cells([Cell(c, c) for c in text]))


class Cells(list):
    trailing: str = ""


def protect(cells: Cells) -> Cells:
    text = "".join(c.ch for c in cells)
    for start, end, _ in scan_placeholders(text)[0]:
        for k in range(start, end):
            cells[k].prot = True
    return cells


def text_of(cells: Iterable[Cell]) -> str:
    return "".join(c.ch for c in cells)


def _new(ch: str) -> Cell:
    return Cell(ch, None)


def _replace_run(cells: Cells, j: int, i: int) -> Cells:
    """Replace cells[j:i] (a run of spaces) with one U+00A0, keeping any `pre` raw."""
    pre = "".join(c.pre for c in cells[j:i])
    if i - j == 1 and cells[j].ch == NBSP:
        return cells
    cell = Cell(NBSP, None, pre)
    out = Cells(cells[:j] + [cell] + cells[i:])
    out.trailing = cells.trailing
    return out


def _delete_run(cells: Cells, j: int, i: int) -> Cells:
    pre = "".join(c.pre for c in cells[j:i])
    rest = cells[i:]
    out = Cells(cells[:j] + rest)
    out.trailing = cells.trailing
    if pre:
        if rest:
            rest[0].pre = pre + rest[0].pre
        else:
            out.trailing = pre + out.trailing
    return out


def _run_before(cells: Cells, i: int) -> int:
    j = i
    while j > 0 and cells[j - 1].ch in SPACE_LIKE and not cells[j - 1].prot:
        j -= 1
    return j


def rule_narrow_spaces(cells: Cells) -> Cells:
    """U+202F and U+2009 become U+00A0 (README Spaces; money.ts normalizeSpaces)."""
    for c in cells:
        if c.ch in NARROW_SPACES and not c.prot:
            c.ch, c.raw = NBSP, None
    return cells


def rule_apostrophe(cells: Cells) -> Cells:
    for c in cells:
        if c.ch == RSQUO and not c.prot:
            c.ch, c.raw = "'", None
    return cells


def _ch(cells: Cells, k: int) -> str:
    return cells[k].ch if 0 <= k < len(cells) else ""


def _wants_nbsp_before(cells: Cells, i: int) -> bool:
    ch, prev, nxt, nxt2 = cells[i].ch, _ch(cells, i - 1), _ch(cells, i + 1), _ch(cells, i + 2)
    if i == 0:
        return False
    # A colon with a non-space character on both sides is not punctuation (HH:MM, usager:mot,
    # deploy:origins, localhost:3000, URLs): only a colon followed by whitespace or the end is.
    if ch == ":":
        return prev != ":" and (nxt == "" or nxt.isspace())
    # % and $ inside a token (%s %1$s %20 ${x} {n}%d) are not the sign either.
    if ch == "%":
        if prev == "%" or (nxt and (nxt.isalnum() or nxt in "@%{$_")):
            return False
        return True
    if ch == "$":
        if nxt and (nxt.isalnum() or nxt in "{_"):
            return False
        k = i - 1                               # %1$s
        while k >= 0 and cells[k].ch.isdigit():
            k -= 1
        if k < i - 1 and _ch(cells, k) == "%":
            return False
        return True
    return ch == RAQUO


def _insert_ok(prev: str, ch: str) -> bool:
    if not prev or prev in SPACE_LIKE or prev == "\n":
        return False
    if ch == ":":
        return prev.isalnum() or prev in "})]%$." + RAQUO + ELLIPSIS
    if ch in "%$":
        return prev.isdigit() or prev == "}"
    return prev != LAQUO  # the closing guillemet


def rule_nbsp(cells: Cells) -> Cells:
    """U+00A0 before : % $ » and after «: a space becomes U+00A0, a missing one is inserted."""
    i = 0
    while i < len(cells):
        c = cells[i]
        if not c.prot and c.ch in ":%$" + RAQUO and _wants_nbsp_before(cells, i):
            j = _run_before(cells, i)
            if j < i:
                cells = _replace_run(cells, j, i)
                i = j + 1
            elif _insert_ok(_ch(cells, i - 1), c.ch):
                cells.insert(i, _new(NBSP))
                i += 1
        elif not c.prot and c.ch == LAQUO:
            k = i + 1
            while k < len(cells) and cells[k].ch in SPACE_LIKE and not cells[k].prot:
                k += 1
            if k > i + 1:
                cells = _replace_run(cells, i + 1, k)
            else:
                nxt = _ch(cells, i + 1)
                if nxt and nxt not in SPACE_LIKE and nxt not in ("\n", RAQUO):
                    cells.insert(i + 1, _new(NBSP))
            i += 1
        i += 1
    return cells


def rule_space_before(cells: Cells) -> Cells:
    """No space before ; ! ? (glossary Register and style: the Canadian convention)."""
    i = 0
    while i < len(cells):
        c = cells[i]
        if not c.prot and c.ch in ";!?":
            j = _run_before(cells, i)
            if 0 < j < i and cells[j - 1].ch != "\n":
                cells = _delete_run(cells, j, i)
                i = j
        i += 1
    return cells


FIX_PIPELINE: list[tuple[str, Callable[[Cells], Cells]]] = [
    ("TYPO-NBSP", rule_narrow_spaces),
    ("TYPO-APOS", rule_apostrophe),
    ("TYPO-NBSP", rule_nbsp),
    ("TYPO-SPACE-BEFORE", rule_space_before),
]


def fix_cells(cells: Cells) -> Cells:
    for _, fn in FIX_PIPELINE:
        cells = fn(cells)
    return cells


def fix_text(text: str) -> str:
    """The French typography fix on a plain string (what `fix` writes, decoded)."""
    return text_of(fix_cells(cells_of(text)))


# --------------------------------------------------------------------------------------------
# Report-only typography (fix cannot repair these safely)

RE_NUM_BOTH = re.compile(r"(?<![\d.,])\d{1,3}(?:,\d{3})+\.\d+(?![\d.,])")
RE_NUM_GROUPS = re.compile(r"(?<![\d.,])\d{1,3}(?:,\d{3}){2,}(?![\d.,])")
RE_NUM_POINT = re.compile(r"(?<![\w.])\d+\.\d+(?![\w.])")
RE_NUM_SPACE_THOUSANDS = re.compile(r"(?<![\d ])\d{1,3}(?: \d{3})+(?![\d])")
RE_MONEY_PREFIX = re.compile(r"(?:\b(?:CA|US|C)\$|\$\s?\d)")
RE_TIME = re.compile(r"\b\d{1,2}:\d{2}\b|\b\d{1,2}\s?(?:AM|PM|am|pm|a\.m\.|p\.m\.)(?!\w)")
RE_SPACED_ENDASH = re.compile(rf"(?:^|\s){ENDASH}(?:\s|$)")


def report_only(text: str, fr: bool) -> list[tuple[str, str, str]]:
    """(rule, severity, message) for what `fix` leaves alone."""
    out: list[tuple[str, str, str]] = []
    if EMDASH in text or HBAR in text:
        out.append(("TYPO-EMDASH", "error", "em-dash: use a comma, colon or parentheses"))
    if any(ch in INVISIBLE for ch in text):
        out.append(("TYPO-INVISIBLE", "error", f"invisible character in {show(text)}"))
    if not fr:
        return out
    if RE_SPACED_ENDASH.search(text):
        out.append(("TYPO-EMDASH", "error", "en-dash used as a dash: use a comma, colon or parentheses"))
    for m in RE_NUM_BOTH.finditer(text):
        out.append(("TYPO-NUMBER", "error", f"English-style number {m.group(0)!r}: decimal comma, U+00A0 thousands"))
    for m in RE_NUM_GROUPS.finditer(text):
        out.append(("TYPO-NUMBER", "error", f"English-style thousands {m.group(0)!r}: U+00A0 thousands"))
    for m in RE_NUM_POINT.finditer(text):
        if not RE_NUM_BOTH.search(text):
            out.append(("TYPO-NUMBER", "warn", f"decimal point in {m.group(0)!r}: fr-CA uses a decimal comma (a version number is fine)"))
    for m in RE_NUM_SPACE_THOUSANDS.finditer(text):
        out.append(("TYPO-NUMBER", "warn", f"plain space in {m.group(0)!r}: thousands take U+00A0 (a phone number is fine)"))
    if RE_MONEY_PREFIX.search(text):
        out.append(("TYPO-MONEY", "error", "currency before the amount: write 50\\u00A0$ CA"))
    if RE_TIME.search(text):
        out.append(("TYPO-TIME", "warn", "clock time: fr-CA writes 9 h, 13 h 30"))
    return out


# --------------------------------------------------------------------------------------------
# Source rewriting: TOML string tokens and JSON string tokens, decoded into cells and re-encoded
# only where a cell changed, so untouched escapes keep their exact bytes.

_TOML_SIMPLE_ESC = {"b": "\b", "t": "\t", "n": "\n", "f": "\f", "r": "\r", '"': '"', "\\": "\\", "e": "\x1b"}
_JSON_SIMPLE_ESC = {"b": "\b", "t": "\t", "n": "\n", "f": "\f", "r": "\r", '"': '"', "\\": "\\", "/": "/"}


class ScanError(Exception):
    pass


def _toml_basic_cells(raw: str, multiline: bool) -> Cells:
    cells = Cells()
    pre = ""
    i, n = 0, len(raw)
    if multiline and raw.startswith("\r\n"):
        pre, i = "\r\n", 2
    elif multiline and raw.startswith("\n"):
        pre, i = "\n", 1
    while i < n:
        c = raw[i]
        if c == "\\":
            nx = raw[i + 1] if i + 1 < n else ""
            if nx in _TOML_SIMPLE_ESC:
                cells.append(Cell(_TOML_SIMPLE_ESC[nx], raw[i:i + 2], pre))
                i += 2
            elif nx in ("u", "U", "x"):
                width = {"u": 4, "U": 8, "x": 2}[nx]
                hexs = raw[i + 2:i + 2 + width]
                if len(hexs) != width:
                    raise ScanError("bad escape")
                cells.append(Cell(chr(int(hexs, 16)), raw[i:i + 2 + width], pre))
                i += 2 + width
            elif multiline and nx in " \t\r\n":
                j = i + 1
                while j < n and raw[j] in " \t\r\n":
                    j += 1
                pre += raw[i:j]
                i = j
                continue
            else:
                raise ScanError(f"bad escape \\{nx}")
        else:
            cells.append(Cell(c, c, pre))
            i += 1
        pre = ""
    cells.trailing = pre
    return cells


def _toml_literal_cells(raw: str, multiline: bool) -> Cells:
    cells = Cells()
    i, pre = 0, ""
    if multiline and raw.startswith("\r\n"):
        pre, i = "\r\n", 2
    elif multiline and raw.startswith("\n"):
        pre, i = "\n", 1
    for c in raw[i:]:
        cells.append(Cell(c, None, pre))  # re-encoded as a basic string when anything changes
        pre = ""
    cells.trailing = pre
    return cells


def _encode_char(ch: str, multiline: bool, json_style: bool) -> str:
    if ch == "\\":
        return "\\\\"
    if ch == '"':
        return '\\"'
    if ch == NBSP or ch in NARROW_SPACES or ch in INVISIBLE:
        return f"\\u{ord(ch):04X}"
    if ch == "\n":
        return "\n" if multiline else "\\n"
    if ch == "\t":
        return "\\t"
    if ch == "\r":
        return "\\r"
    if ord(ch) < 0x20 or ord(ch) == 0x7F:
        return f"\\u{ord(ch):04x}" if json_style else f"\\u{ord(ch):04X}"
    return ch


def _encode_cells(cells: Cells, multiline: bool, json_style: bool) -> str:
    parts = []
    for c in cells:
        parts.append(c.pre)
        if c.raw is None or c.raw == NBSP:  # a literal NBSP is rewritten as its escape
            parts.append(_encode_char(c.ch, multiline, json_style))
        else:
            parts.append(c.raw)
    parts.append(cells.trailing)
    return "".join(parts)


@dataclass
class Token:
    start: int
    end: int
    kind: str  # basic, mlbasic, literal, mlliteral, json
    path: tuple[str, ...]


class TomlScanner:
    """Finds every string token in a TOML document with its full key path. Values only; it
    trusts tomllib for validity (the file is parsed before and after any rewrite)."""

    BARE = re.compile(r"[A-Za-z0-9_-]+")
    SCALAR = re.compile(r"[^\s,\]\}#]+")

    def __init__(self, text: str):
        self.t = text
        self.i = 0
        self.tokens: list[Token] = []

    def scan(self) -> list[Token]:
        t = self.t
        table: list[str] = []
        while True:
            self._skip_blank()
            if self.i >= len(t):
                return self.tokens
            if t[self.i] == "[":
                arr = t.startswith("[[", self.i)
                self.i += 2 if arr else 1
                table = self._key()
                self._ws()
                close = "]]" if arr else "]"
                if not t.startswith(close, self.i):
                    raise ScanError(f"bad table header at {self.i}")
                self.i += len(close)
            else:
                key = self._key()
                self._ws()
                if not t.startswith("=", self.i):
                    raise ScanError(f"expected = at {self.i}")
                self.i += 1
                self._ws()
                self._value(table + key)

    def _ws(self) -> None:
        while self.i < len(self.t) and self.t[self.i] in " \t":
            self.i += 1

    def _skip_blank(self) -> None:
        t = self.t
        while self.i < len(t):
            c = t[self.i]
            if c in " \t\r\n":
                self.i += 1
            elif c == "#":
                nl = t.find("\n", self.i)
                self.i = len(t) if nl == -1 else nl
            else:
                return

    def _key(self) -> list[str]:
        parts: list[str] = []
        t = self.t
        while True:
            self._ws()
            if t.startswith('"', self.i):
                end = self._basic_end(self.i)
                parts.append(tomllib.loads(f"k = {t[self.i:end]}")["k"])
                self.i = end
            elif t.startswith("'", self.i):
                end = t.index("'", self.i + 1) + 1
                parts.append(t[self.i + 1:end - 1])
                self.i = end
            else:
                m = self.BARE.match(t, self.i)
                if not m:
                    raise ScanError(f"bad key at {self.i}")
                parts.append(m.group(0))
                self.i = m.end()
            self._ws()
            if t.startswith(".", self.i):
                self.i += 1
                continue
            return parts

    def _basic_end(self, i: int) -> int:
        t = self.t
        j = i + 1
        while j < len(t):
            c = t[j]
            if c == "\\":
                j += 2
                continue
            if c == '"':
                return j + 1
            if c == "\n":
                break
            j += 1
        raise ScanError(f"unterminated string at {i}")

    def _ml_end(self, i: int, quote: str) -> int:
        t = self.t
        j = i + 3
        while j < len(t):
            if quote == '"' and t[j] == "\\":
                j += 2
                continue
            if t.startswith(quote * 3, j):
                end = j + 3
                extra = 0
                while extra < 2 and end < len(t) and t[end] == quote:
                    end += 1
                    extra += 1
                return end
            j += 1
        raise ScanError(f"unterminated multi-line string at {i}")

    def _value(self, path: list[str]) -> None:
        t, i = self.t, self.i
        if t.startswith('"""', i):
            end = self._ml_end(i, '"')
            self.tokens.append(Token(i, end, "mlbasic", tuple(path)))
            self.i = end
        elif t.startswith("'''", i):
            end = self._ml_end(i, "'")
            self.tokens.append(Token(i, end, "mlliteral", tuple(path)))
            self.i = end
        elif t.startswith('"', i):
            end = self._basic_end(i)
            self.tokens.append(Token(i, end, "basic", tuple(path)))
            self.i = end
        elif t.startswith("'", i):
            end = t.index("'", i + 1) + 1
            self.tokens.append(Token(i, end, "literal", tuple(path)))
            self.i = end
        elif t.startswith("{", i):
            self.i += 1
            while True:
                self._ws()
                if t.startswith("}", self.i):
                    self.i += 1
                    return
                key = self._key()
                self._ws()
                if not t.startswith("=", self.i):
                    raise ScanError(f"expected = at {self.i}")
                self.i += 1
                self._ws()
                self._value(path + key)
                self._ws()
                if t.startswith(",", self.i):
                    self.i += 1
        elif t.startswith("[", i):
            self.i += 1
            idx = 0
            while True:
                self._skip_blank()
                if t.startswith("]", self.i):
                    self.i += 1
                    return
                self._value(path + [str(idx)])
                idx += 1
                self._skip_blank()
                if t.startswith(",", self.i):
                    self.i += 1
        else:
            m = self.SCALAR.match(t, i)
            if not m:
                raise ScanError(f"bad value at {i}")
            self.i = m.end()


def is_fr_path(path: tuple[str, ...]) -> bool:
    if not path or path[0] == "meta":
        return False
    if path[-1] == "fr":
        return True
    return len(path) >= 2 and path[-2] == "fr" and path[-1] in PLURAL_CATS


def _token_cells(text: str, tok: Token) -> tuple[Cells, bool]:
    raw = text[tok.start:tok.end]
    if tok.kind == "basic":
        return _toml_basic_cells(raw[1:-1], False), False
    if tok.kind == "mlbasic":
        return _toml_basic_cells(raw[3:-3], True), True
    if tok.kind == "literal":
        return _toml_literal_cells(raw[1:-1], False), False
    return _toml_literal_cells(raw[3:-3], True), True


def _set_path(data: dict, path: tuple[str, ...], value: object) -> None:
    for p in path[:-1]:
        data = data[p]
    data[path[-1]] = value


def _get_path(data: dict, path: tuple[str, ...]) -> object:
    for p in path:
        data = data[p]
    return data


def fix_toml_text(text: str) -> tuple[str, list[tuple[str, str]]]:
    """Return (new text, [(key, decoded new fr)]). Raises ScanError or TOMLDecodeError."""
    before = tomllib.loads(text)
    tokens = TomlScanner(text).scan()
    pieces: list[str] = []
    last = 0
    changed: list[tuple[str, str]] = []
    expected = copy.deepcopy(before)
    for tok in tokens:
        if not is_fr_path(tok.path):
            continue
        cells, multiline = _token_cells(text, tok)
        cells = protect(cells)
        cells = fix_cells(cells)
        body = _encode_cells(cells, multiline, json_style=False)
        quote = '"""' if multiline else '"'
        new_tok = quote + body + quote
        if new_tok == text[tok.start:tok.end]:
            continue
        pieces.append(text[last:tok.start])
        pieces.append(new_tok)
        last = tok.end
        new_value = text_of(cells)
        _set_path(expected, tok.path, new_value)
        changed.append((".".join(tok.path), new_value))
    pieces.append(text[last:])
    out = "".join(pieces)
    after = tomllib.loads(out)
    if after != expected:  # the rewrite must change French values only, and exactly as fix_text
        raise ScanError("internal: rewritten TOML does not round-trip; file left untouched")
    return out, changed


def _json_string_end(s: str, i: int) -> int:
    j = i + 1
    while j < len(s):
        c = s[j]
        if c == "\\":
            j += 2
            continue
        if c == '"':
            return j + 1
        j += 1
    raise ScanError("unterminated JSON string")


def _json_value_end(s: str, i: int) -> int:
    c = s[i]
    if c == '"':
        return _json_string_end(s, i)
    if c in "{[":
        depth, j = 0, i
        while j < len(s):
            ch = s[j]
            if ch == '"':
                j = _json_string_end(s, j)
                continue
            if ch in "{[":
                depth += 1
            elif ch in "}]":
                depth -= 1
                if depth == 0:
                    return j + 1
            j += 1
        raise ScanError("unterminated JSON container")
    m = re.compile(r"[^,\]\}\s]+").match(s, i)
    if not m:
        raise ScanError("bad JSON value")
    return m.end()


def _json_members(s: str, i: int) -> list[tuple[str, int, int]]:
    """Members of the object starting at s[i] == '{' as (key, value start, value end)."""
    out = []
    j = i + 1
    ws = re.compile(r"\s*")
    while True:
        j = ws.match(s, j).end()
        if s[j] == "}":
            return out
        kend = _json_string_end(s, j)
        key = json.loads(s[j:kend])
        j = ws.match(s, kend).end()
        if s[j] != ":":
            raise ScanError("expected :")
        j = ws.match(s, j + 1).end()
        vend = _json_value_end(s, j)
        out.append((key, j, vend))
        j = ws.match(s, vend).end()
        if s[j] == ",":
            j += 1


def _json_cells(raw: str) -> Cells:
    cells = Cells()
    i, n = 0, len(raw)
    while i < n:
        c = raw[i]
        if c == "\\":
            nx = raw[i + 1]
            if nx in _JSON_SIMPLE_ESC:
                cells.append(Cell(_JSON_SIMPLE_ESC[nx], raw[i:i + 2]))
                i += 2
                continue
            if nx == "u":
                cp = int(raw[i + 2:i + 6], 16)
                if 0xD800 <= cp < 0xDC00 and raw.startswith("\\u", i + 6):
                    lo = int(raw[i + 8:i + 12], 16)
                    cells.append(Cell(chr(0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00)), raw[i:i + 12]))
                    i += 12
                    continue
                cells.append(Cell(chr(cp), raw[i:i + 6]))
                i += 6
                continue
            raise ScanError(f"bad JSON escape \\{nx}")
        cells.append(Cell(c, c))
        i += 1
    return cells


def row_label(row: dict, n: int) -> str:
    """How a JSONL row is named in output: its key, else its unit id, else its line."""
    return str(row.get("key") or row.get("id") or f"line {n}")


FR_FIELDS = ("fr", "fr_apple")  # a batch line's French, and its Apple variant (README W3)


def fix_jsonl_line(line: str) -> tuple[str, list[str]]:
    """Fix the `fr` field (string, or plural object of strings) of one JSONL object line."""
    if not line.strip():
        return line, []
    obj = json.loads(line)
    if not isinstance(obj, dict):
        raise ScanError("a JSONL line must be an object")
    start = len(line) - len(line.lstrip())
    spans: list[tuple[int, int, tuple[str, ...]]] = []
    for key, vs, ve in _json_members(line, start):
        if key not in FR_FIELDS:
            continue
        if line[vs] == '"':
            spans.append((vs, ve, (key,)))
        elif line[vs] == "{":
            for sub, s2, e2 in _json_members(line, vs):
                if sub in PLURAL_CATS and line[s2] == '"':
                    spans.append((s2, e2, (key, sub)))
    pieces, last, changed = [], 0, []
    expected = json.loads(line)
    for vs, ve, path in sorted(spans):
        cells = protect(_json_cells(line[vs + 1:ve - 1]))
        cells = fix_cells(cells)
        new_tok = '"' + _encode_cells(cells, False, json_style=True) + '"'
        if new_tok == line[vs:ve]:
            continue
        pieces += [line[last:vs], new_tok]
        last = ve
        _set_path(expected, path, text_of(cells))
        changed.append(".".join(path))
    pieces.append(line[last:])
    out = "".join(pieces)
    if json.loads(out) != expected:
        raise ScanError("internal: rewritten JSON line does not round-trip")
    return out, changed


def _values_for_report(path: Path, text: str) -> list[tuple[str, str]]:
    """(key, fr value) pairs of a fixed file, for the report-only checks."""
    out = []
    if path.suffix == ".toml":
        for e in toml_entries(tomllib.loads(text)):
            for form, value in forms(e.get("fr")):
                out.append((e["_key"] + form, value))
    else:
        for n, line in enumerate(text.splitlines(), 1):
            if line.strip():
                obj = json.loads(line)
                for field_ in FR_FIELDS:
                    suffix = "" if field_ == "fr" else "@apple"
                    for form, value in forms(obj.get(field_)):
                        out.append((row_label(obj, n) + suffix + form, value))
    return out


def cmd_fix(files: list[str]) -> int:
    status = 0
    for name in files:
        path = Path(name)
        text = path.read_text(encoding="utf-8")
        try:
            if path.suffix == ".toml":
                new, changed = fix_toml_text(text)
                keys = [k for k, _ in changed]
            elif path.suffix == ".jsonl":
                lines = text.splitlines(keepends=True)
                out, keys = [], []
                for n, line in enumerate(lines, 1):
                    body = line.rstrip("\r\n")
                    fixed, ch = fix_jsonl_line(body)
                    out.append(fixed + line[len(body):])
                    if ch:
                        keys.append(row_label(json.loads(body), n))
                new = "".join(out)
            else:
                print(f"{name}: not a .toml or .jsonl file", file=sys.stderr)
                status = 2
                continue
        except (ScanError, tomllib.TOMLDecodeError, json.JSONDecodeError, KeyError, IndexError) as exc:
            print(f"{name}: PARSE {exc}", file=sys.stderr)
            status = 2
            continue
        if new != text:
            path.write_text(new, encoding="utf-8")
            print(f"{name}: fixed {len(keys)} value(s)" + (f": {', '.join(keys)}" if keys else ""))
        for key, value in _values_for_report(path, new):
            for rule, sev, msg in report_only(value, fr=True):
                print(Finding(name, key, rule, msg + " (not fixed)", sev).line())
    return status


# --------------------------------------------------------------------------------------------
# Term base and memory


@dataclass
class TermBase:
    terms: list[dict] = field(default_factory=list)
    never: list[str] = field(default_factory=list)
    bans: list[dict] = field(default_factory=list)
    os: list[dict] = field(default_factory=list)


# Glossary "Marks and product names: never translated" (docs/i18n_fr_ca_glossary.md).
GLOSSARY_NEVER = ["Distronode", "District AI", "District HQ", "District Desk", "District Scheduling",
                  "District Studio", "API", "CRM", "SIP", "Twilio", "LiveKit", "Stripe"]


def read_jsonl(path: Path) -> list[dict]:
    rows = []
    for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if line.strip():
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError as exc:
                raise SystemExit(f"{path}:{n}: not JSON: {exc}")
    return rows


def load_termbase(path: Path | None) -> TermBase:
    tb = TermBase(never=list(GLOSSARY_NEVER))
    if path is None or not path.is_file():
        return tb
    for row in read_jsonl(path):
        kind = row.get("kind")
        if kind == "term":
            tb.terms.append(row)
        elif kind == "never" and row.get("text") and row["text"] not in tb.never:
            tb.never.append(row["text"])
        elif kind == "ban":
            tb.bans.append(row)
        elif kind == "os":
            tb.os.append(row)
    return tb


def norm_key(s: object) -> str:
    """The memory key (README): whitespace collapsed, U+202F (and U+2009) -> U+00A0."""
    if isinstance(s, dict):
        return json.dumps({k: norm_key(v) for k, v in sorted(s.items())}, ensure_ascii=False)
    s = str(s)
    for ch in NARROW_SPACES:
        s = s.replace(ch, NBSP)
    return re.sub(r"[ \t\r\n]+", " ", s).strip()


def load_tm(path: Path | None) -> dict[tuple[str, str], list[dict]]:
    tm: dict[tuple[str, str], list[dict]] = {}
    if path is None or not path.is_file():
        return tm
    for row in read_jsonl(path):
        if "en" in row and "fr" in row:
            tm.setdefault((norm_key(row["en"]), row.get("ctx", "*")), []).append(row)
    return tm


# --------------------------------------------------------------------------------------------
# Entries


def toml_entries(data: dict, prefix: tuple[str, ...] = ()) -> list[dict]:
    out = []
    for k, v in data.items():
        if not prefix and k == "meta":
            continue
        if isinstance(v, dict):
            if "en" in v:
                e = dict(v)
                e["_key"] = ".".join(prefix + (k,))
                out.append(e)
            out.extend(toml_entries({kk: vv for kk, vv in v.items()
                                     if isinstance(vv, dict) and kk not in ("en", "fr", "args", "id")},
                                    prefix + (k,)))
    return out


def forms(value: object) -> list[tuple[str, str]]:
    if isinstance(value, str):
        return [("", value)]
    if isinstance(value, dict):
        return [(f".{k}", v) for k, v in value.items() if isinstance(v, str)]
    return []


def en_for_form(en: object, form: str) -> str | None:
    if isinstance(en, str):
        return en if form == "" else None
    if isinstance(en, dict):
        cat = form.lstrip(".")
        return en.get(cat) if cat in en else en.get("other")
    return None


def platform_of(meta: dict, override: str | None) -> str | None:
    """apple | android | windows | gtk, or None (rust, neutral): from a catalog's [meta] or a
    batch unit's platform and apple fields; an explicit --platform wins."""
    if override:
        return override
    if meta.get("apple") is True:
        return "apple"
    return {"android": "android", "csharp": "windows", "gtk": "gtk"}.get(str(meta.get("platform", "")))


# --------------------------------------------------------------------------------------------
# Text helpers for the language rules

RE_URLISH = re.compile(r"https?://\S+|www\.\S+|[\w.+-]+@[\w-]+(?:\.[\w-]+)+|\b[\w-]+(?:\.[\w-]+)*\.(?:com|ca|io|net|org|fr|app|dev)\b", re.I)
RE_WORD = re.compile(r"[^\W\d_]+(?:['-][^\W\d_]+)*")
LETTERS = re.compile(r"[^\W\d_]")


def strip_accents(s: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")


def mask(text: str, names: Iterable[str]) -> str:
    """Blank out placeholders, URLs/emails/domains and never-translate names (same length)."""
    chars = list(text)
    spans = [(s, e) for s, e, _ in scan_placeholders(text)[0]]
    spans += [m.span() for m in RE_URLISH.finditer(text)]
    for name in names:
        spans += [m.span() for m in re.finditer(rf"(?<!\w){re.escape(name)}(?!\w)", text)]
    for s, e in spans:
        for k in range(s, e):
            chars[k] = " "
    return "".join(chars)


def sentence_initial(text: str, pos: int) -> bool:
    k = pos - 1
    while k >= 0 and text[k] in " " + NBSP + "\t":
        k -= 1
    return k < 0 or text[k] in ".!?\n:(" + LAQUO + ELLIPSIS + '"'


# Glossary Register and style: accented capitals (État, À propos). Unaccented key -> correct
# lowercase spelling. Common UI words whose first letter carries an accent.
ACCENT_WORDS = {strip_accents(w): w for w in """
    état états étape étapes écran écrans échec échecs échéance échéances élément éléments équipe
    équipes événement événements également êtes être éteindre étiquette étiquettes évaluation
    économie électronique émetteur énoncé épingler épinglé épinglés été étendre étendu édition
    écouter écoutez écoute échange échanges éviter évitez écrire écrivez éducation île îles ça
    élevé élevée émis émise énergie
""".split()}


def caps_accent_issues(text: str) -> list[tuple[str, str]]:
    """(severity, message). A bare initial « A » is often the verb avoir (« A demandé STOP »),
    so it only warns; the curated words are errors."""
    out = []
    for m in RE_WORD.finditer(text):
        for part in re.finditer(r"[^\W\d_]+", m.group(0)):
            w, start = part.group(0), m.start() + part.start()
            if w == "A":
                after = text[start + 1:start + 3]
                if sentence_initial(text, start) and after[:1] in (" ", NBSP) and after[1:2].islower():
                    out.append(("warn", "'A' starting a phrase: 'À' unless it is the verb avoir"))
                continue
            correct = ACCENT_WORDS.get(strip_accents(w.lower()))
            if not correct:
                continue
            if w.isupper():
                if len(w) > 2 and w != correct.upper():  # two capitals are an acronym (CA)
                    out.append(("error", f"{w!r} should be {correct.upper()!r}"))
            elif w[0].isupper() and w[0] != correct[0].upper():
                out.append(("error", f"{w!r} should be {correct[0].upper() + correct[1:]!r}"))
    return out


# Proper nouns that keep their capital mid-sentence in French.
PROPER_NOUNS = {strip_accents(w.lower()) for w in """
    Canada Québec Quebec Montréal Montreal Ontario Europe Francfort Frankfurt Singapour Asie Amérique
    États-Unis Unis Loi IA Apple Google Microsoft Android Windows Linux macOS iPhone iPad Mac Store
    Play Réglages Paramètres Bluetooth Wi-Fi Internet LPRPDE LPRPS CAI RGPD
""".split()}
SENTENCE_CASE_CTX = {"title", "button", "menu", "label", "link"}


def sentence_case_issues(fr: str, en: str, names: Iterable[str], os_words: Iterable[str]) -> list[str]:
    masked = mask(fr, names)
    en_caps = {strip_accents(m.group(0).lower()) for m in RE_WORD.finditer(en or "") if m.group(0)[0].isupper()}
    allowed = PROPER_NOUNS | en_caps | {strip_accents(w.lower()) for n in list(names) + list(os_words)
                                        for w in RE_WORD.findall(n)}
    out = []
    for m in RE_WORD.finditer(masked):
        w = m.group(0)
        if not w[0].isupper() or w.isupper() or any(c.isupper() for c in w[1:]):
            continue
        if sentence_initial(masked, m.start()) or strip_accents(w.lower()) in allowed:
            continue
        out.append(w)
    return out


# Register: vouvoiement only. Word-boundary match on lowercased text; the allowlist covers the
# noun "ton" (tone) and names (capitalized mid-sentence, or all caps).
RE_TU = re.compile(r"\b(?:tu|te|toi|ton|ta|tes)\b|\bt'(?=[^\W\d_])")
TON_NOUN_BEFORE = re.compile(r"\b(?:le|un|du|au|ce|cet|même|quel|bon|nouveau|son|mon|votre|notre|leur|chaque)\s+$")
TON_NOUN_AFTER = re.compile(r"^\s+(?:de\s+(?:la\s+)?voix|neutre|professionnel|amical|chaleureux|formel)\b")
# ton/ta/tes as a determiner always has a noun after it: the whole string (« Ton » for
# "Sentiment"), the word before end or punctuation, or before de/du/des/d' is not one.
TU_DET_NOT_AFTER = re.compile(r"^(?:\s*$|\s*[^\w\s]|\s+(?:de|du|des)\b|\s+d['\u2019])")


def tu_issues(text: str, names: Iterable[str]) -> list[str]:
    masked = mask(text, names)
    low = masked.lower()
    out = []
    for m in RE_TU.finditer(low):
        word = m.group(0)
        orig = masked[m.start():m.end()].rstrip("'")
        if word == "ton" and (TON_NOUN_BEFORE.search(low[:m.start()]) or TON_NOUN_AFTER.search(low[m.end():])):
            continue  # the noun: le ton de la voix
        if word in ("ton", "ta", "tes") and TU_DET_NOT_AFTER.search(low[m.end():]):
            continue  # not a determiner: « Ton » alone, « le ton. », « ton de »
        if orig.isupper() and len(orig) > 1:
            continue  # an acronym (TA, TES)
        if orig[0].isupper() and not sentence_initial(masked, m.start()):
            continue  # a name mid-sentence (Tu, Ta)
        out.append(masked[m.start():m.end()])
    return out


def _phrase_rx(phrase: str) -> re.Pattern:
    words = [re.escape(w) for w in phrase.split()]
    return re.compile(r"(?<![\w-])" + r"\s+".join(words) + r"(?![\w-])", re.I)


def _plural_rx(phrase: str) -> re.Pattern:
    words = [re.escape(w) + r"(?:s|x)?" for w in phrase.split()]
    sep = "[\\s" + NBSP + "]+"
    return re.compile(r"(?<!\w)" + sep.join(words) + r"(?!\w)", re.I)


RE_NAME_SEQ = re.compile(r"\b[A-Z][\w'-]*(?:[ \t]+[A-Z][\w'-]*)+")


def mask_names_with(text: str, term: str) -> str:
    """Blank out capitalized multi-word names that contain `term` (Google Consent Mode,
    LinkedIn Insight Tag, Vertex AI) so the term inside a name is not read as prose."""
    rx = re.compile(r"(?<![\w-])" + re.escape(term) + r"(?![\w-])", re.I)

    def blank(m: re.Match) -> str:
        seq = m.group(0)
        if not rx.search(seq):
            return seq
        # A name needs a capitalized word besides the term and besides a sentence-initial word,
        # so « Utiliser AI » or "Use AI" at the start of a sentence stays prose.
        words = [(w.start(), w.group(0)) for w in re.finditer(r"\S+", seq)]
        others = [w for off, w in words if not rx.fullmatch(w)
                  and not (off == 0 and sentence_initial(text, m.start()))]
        return " " * len(seq) if others else seq
    return RE_NAME_SEQ.sub(blank, text)


def _en_term_rx(term: str) -> re.Pattern:
    words = [re.escape(w) for w in term.split()]
    return re.compile(r"(?<![\w-])" + r"\s+".join(words) + r"(?:s|es)?(?![\w-])", re.I)


# A denial, in the same sentence and before the word (« aucun enregistrement », « plutôt qu'un
# enregistrement audio », « n'offre pas d'enregistrement »).
APOS = "['" + RSQUO + "]"
DENIAL = re.compile(r"\b(?:aucune?|sans|jamais|ni)\b|\bpas\s+(?:d" + APOS + r"|de\b)|\bn" + APOS + r".*\bpas\b"
                    r"|\bne\b.*\bpas\b|\bplutôt\s+qu" + APOS + r"une?\b|\bau\s+lieu\s+d" + APOS + r"une?\b", re.I)
RE_ENREG = re.compile(r"\benregistrements?\b", re.I)
TU_WORDS = {"tu", "te", "toi", "ton", "ta", "tes"}


def is_denied(text: str, pos: int) -> bool:
    """True when a denial precedes `pos` in the same sentence."""
    start = max(text.rfind(c, 0, pos) for c in ".!?\n") + 1
    return bool(DENIAL.search(text[start:pos]))

# README bans (« données personnelles », « enregistrement d'appel », « e-mail », « chat ») and
# glossary "never" rows; the website enforces the first in socialmeta/__tests__/brand.test.ts.
BUILTIN_BANS: list[tuple[re.Pattern, str, str]] = [
    (re.compile(r"\bdonnées\s+personnelles\b", re.I), "renseignements personnels", "Quebec statutory term (glossary)"),
    (re.compile(r"(?<![\w@.])e-?mails?(?![\w@])|(?<![\w@.])mails?(?![\w@.])", re.I), "courriel", "OQLF (glossary)"),
    (re.compile(r"(?<![\w-])chats?(?![\w-])", re.I), "clavardage", "OQLF (glossary)"),
    (re.compile(r"\blignes?\s+commerciales?\b", re.I), "ligne d'affaires", "rebrand house term (glossary)"),
    (re.compile(r"(?<![\w-])cookies?(?![\w-])(?!\s*\))", re.I), "témoins (cookies) on first use, then témoins", "OQLF (glossary)"),
    (re.compile(r"\bAI\b"), "IA", "IA for AI in prose (glossary); District AI stays"),
]


def ban_issues(fr: str, names: Iterable[str], tb: TermBase) -> list[tuple[str, str]]:
    """(severity, message)."""
    masked = mask(fr, names)
    out = []
    for rx, use, why in BUILTIN_BANS:
        target = mask_names_with(masked, "AI") if use == "IA" else masked  # Google Cloud Vertex AI
        m = rx.search(target)
        if m:
            out.append(("error", f"{m.group(0)!r} is banned: use {use!r} ({why})"))
    for m in RE_ENREG.finditer(masked):
        if is_denied(masked, m.start()):
            continue
        out.append(("error", f"{m.group(0)!r}: the product records no audio and registration is 'inscription'; "
                             "allowed only in a denial (aucun enregistrement)"))
    for row in tb.bans:
        phrase = row.get("fr", "")
        if not phrase or phrase.lower() in TU_WORDS:
            continue  # REG-TU owns these, with its noun and name allowlist
        hit = _phrase_rx(phrase).search(masked)
        if hit and RE_ENREG.search(phrase) and is_denied(masked, hit.start()):
            continue  # the denial form (« aucun enregistrement d'appel »)
        if hit:
            out.append(("error", f"{phrase!r} is banned: use {row.get('use', '?')!r}"
                                 + (f" ({row['reason']})" if row.get("reason") else "")))
    return out


STORE_PATTERNS = [
    (re.compile(r"\bsites?\s+web\b", re.I), "site Web"),
    (re.compile(r"\bsur\s+le\s+web\b", re.I), "sur le Web"),
    (re.compile(r"\bnavigateurs?\b", re.I), "navigateur"),
    (re.compile(r"\btableaux?\s+de\s+bord\b", re.I), "tableau de bord"),
    (re.compile(r"distronode\.(?:com|ca)\b", re.I), "distronode.com/.ca"),
    # Account sign-up and trials only. The noun « inscription » is also the glossary's word for a
    # number registration (A2P/10DLC, toll-free), a compliance noun, so it counts only when
    # followed within three words by au service, gratuite, compte or à District. The participle
    # « inscrit » (a registered number) is not sign-up either, nor is « inscri… » followed within
    # three words by « liste » (« Inscrire à la liste d'exclusion » registers a do-not-call entry).
    (re.compile(r"\binscri(?:re|s|vez|vons|vent)\b(?!(?:\s+\S+){0,2}?\s+listes?\b)", re.I),
     "sign-up wording (s'inscrire)"),
    (re.compile(r"\binscriptions?(?:\s+\S+){0,2}?\s+(?:au\s+service|gratuites?|compte|à\s+District)\b", re.I),
     "sign-up wording (inscription au service)"),
    (re.compile(r"\bcré(?:er|ez)\s+(?:un|votre)\s+compte\b|\bcréation\s+(?:de|du|d'un)\s+compte\b", re.I),
     "sign-up wording (créer un compte)"),
    (re.compile(r"\bouvr\w*\s+un\s+compte\b", re.I), "sign-up wording (ouvrir un compte)"),
    (re.compile(r"\babonnez-vous\b", re.I), "sign-up wording (abonnez-vous)"),
    # Trial offers only: a bare « essai » is a test call (« appel d'essai ») or a retry (« Nouvel essai »).
    (re.compile(r"\bessais?\s+gratuits?\b|\bpériodes?\s+d['\u2019]essai\b|\bversions?\s+d['\u2019]essai\b"
                r"|\bessais?\s+de\s+\d+\s+(?:jours?|mois)\b|\bessayez\s+gratuitement\b|\bà\s+l['\u2019]essai\b", re.I),
     "trial wording (essai gratuit)"),
]
RE_OFFER_FR = re.compile(r"\b(?:gratuit\w*|offert\w*|offr(?:e|es|ons|ez|ent)|rabais|réduction\w*|promo\w*)\b|\d\s?%", re.I)
# « pas » and « jamais » negate on their own; « plus » only after « ne » or « n' », since
# « un mois de plus offert » is an offer
RE_PAS_BEFORE = re.compile(r"(?:\b(?:pas|jamais)|\bn(?:e\s+|['\u2019])(?:\S+\s+){0,2}plus)(?:\s+\S+){0,2}\s*$", re.I)


def offer_in(value: str) -> str | None:
    """The first offer word in fr. « offert(e)(s) » three words after « pas », « jamais » or
    « ne ... plus » means "not (or no longer) available" (« n'est pas encore offerte »,
    « n'est plus offerte »), not an offer."""
    for m in RE_OFFER_FR.finditer(value):
        if m.group(0).lower().startswith("offert") and RE_PAS_BEFORE.search(value[:m.start()]):
            continue
        return m.group(0)
    return None


# An OS term names the OS element only as the whole string ("Done", "Settings..."), in quotes,
# or after a verb or preposition that names a UI element ("Tap Done", "in Settings"); a word
# like "No" inside "No caller ID" is prose.
OS_UI_LEAD = r"(?i:\b(?:tap|click|press|select|choose|open|in|under|from|to|then|hit|use)\s+(?:the\s+)?)"
OS_QUOTES_OPEN = "[\"'\u201c\u2018\u00ab]"
OS_QUOTES_CLOSE = "[\"'\u201d\u2019\u00bb]"
# Most OS rows name a command (Open, Done, Clear, Back). As the whole string, the English is
# that command only on a control: a status label "open" (an invoice) is \u00ab Ouverte \u00bb, a toast
# "Done" is \u00ab C'est fait \u00bb. So a whole-string match applies in these ctx only, unless the row
# names a place or thing of the OS, which reads the same on a title or a label. A termbase os row
# may list its own `ctx` to replace both defaults. A quoted or led mention ('Tap "Done"',
# "in Settings") names the control in any ctx.
OS_COMMAND_CTX = frozenset({"button", "menu", "link", "tooltip", "a11y"})
OS_NOUN_TERMS = frozenset({"about", "camera", "help", "microphone", "notifications", "preferences",
                           "settings", "system settings"})
RE_OS_QUALIFIER = re.compile(r"\s*\(([^)]*)\)$")


def os_term_parts(term: str) -> tuple[str, str | None]:
    """("Edit (menu)") -> ("Edit", "menu"); ("Open") -> ("Open", None)."""
    m = RE_OS_QUALIFIER.search(term)
    return (term[:m.start()].strip(), m.group(1).strip().lower()) if m else (term.strip(), None)


def os_term_applies(term: str, en_text: str, ctx: str | None = None,
                    siblings: frozenset[str] = frozenset(), ctxs: list[str] | None = None) -> bool:
    """Whether the English names this OS element, so its French must hold the row's French.

    `ctx` is the entry's ctx (None: any). A qualified row ("Edit (menu)") claims a whole string
    in that ctx and a mention followed by the qualifier ("the Edit menu"); `siblings` are the
    qualifiers of other rows for the same term on the platform, which the plain row leaves to
    them. `ctxs` is the row's own ctx list, when the termbase gives one."""
    base, qual = os_term_parts(term)
    core = re.sub(r"[.!?:;,\u2026]+$", "", en_text.strip()).strip()
    if core.lower() == base.lower():
        if ctx is None:
            return True
        if qual:
            return ctx == qual
        if ctx in siblings:
            return False
        if ctxs is not None:
            return ctx in ctxs
        return ctx in OS_COMMAND_CTX or base.lower() in OS_NOUN_TERMS
    if qual:
        after = rf"(?=\s+{re.escape(qual)}\b)"
    else:
        after = "".join(rf"(?!\s+{re.escape(q)}\b)" for q in sorted(siblings))
    t = re.escape(base)
    if re.search(OS_QUOTES_OPEN + r"\s*" + t + r"\s*" + OS_QUOTES_CLOSE + after, en_text, re.I):
        return True
    return bool(re.search(OS_UI_LEAD + t + r"(?!\w)" + after, en_text))


def os_siblings(rows: list[dict]) -> dict[str, frozenset[str]]:
    """Lowercased base term -> the qualifiers of the qualified rows for it ("edit" -> {"menu"})."""
    out: dict[str, set[str]] = {}
    for r in rows:
        base, qual = os_term_parts(str(r.get("en") or ""))
        if qual:
            out.setdefault(base.lower(), set()).add(qual)
    return {k: frozenset(v) for k, v in out.items()}
RE_OFFER_EN = re.compile(r"\b(?:free|discount\w*|off|promo\w*|save|offer\w*)\b|%", re.I)


def urls_of(text: str) -> set[str]:
    return {m.group(0).lower().rstrip(".,;:)") for m in RE_URLISH.finditer(text)}


# --------------------------------------------------------------------------------------------
# Lint


@dataclass
class Ctx:
    file: str
    tb: TermBase
    tm: dict
    platform: str | None
    allow_screens: set[str]
    findings: list[Finding] = field(default_factory=list)

    def add(self, key: str, rule: str, msg: str, sev: str = "") -> None:
        self.findings.append(Finding(self.file, key, rule, msg, sev))


def typography_findings(ctx: Ctx, key: str, value: str) -> None:
    for rule, fn in FIX_PIPELINE:
        before = cells_of(value)
        after = text_of(fn(before))
        if after != value:
            ctx.add(key, rule, f"{show(value)} -> {show(after)} (frcheck.py fix repairs it)")
    for rule, sev, msg in report_only(value, fr=True):
        ctx.add(key, rule, msg, sev)


# Ids that can be reported as a warning, and so can be waived. A rule that warns only in a named
# case (TYPO-NUMBER, TM-DRIFT) is waivable for that case only: its error findings stay.
WAIVABLE = {"LEN", "SENTENCE-CASE", "TYPO-TIME", "URL-PARITY", "TYPO-NUMBER", "TM-DRIFT", "TYPO-EMDASH",
            "CAPS-ACCENT"}
# A rule a catalog of unknown platform (rust, neutral) is held to: any non-Apple app platform.
NON_APPLE_APPS = {"android", "windows", "gtk"}


def term_applies(row: dict, platform: str | None) -> bool:
    """A termbase term row with "platforms" applies only there (README Term base)."""
    plats = row.get("platforms")
    if not plats:
        return True
    if platform:
        return platform in plats
    return bool(NON_APPLE_APPS.intersection(plats))


def lint_entry(ctx: Ctx, e: dict) -> None:
    """Every rule on one entry, then the entry's `waive` table (README Fields): a waived warning
    is kept with severity "waived" (shown under --show-waived); errors are never waived."""
    start = len(ctx.findings)
    _lint_entry(ctx, e)
    waive = e.get("waive")
    if waive is None:
        return
    key = e["_key"]
    if not isinstance(waive, dict):
        ctx.add(key, "WAIVE-ERROR", "waive must be a table of warning id = reason")
        return
    granted: dict[str, str] = {}
    for rid, reason in waive.items():
        if rid not in RULES:
            ctx.add(key, "WAIVE-ERROR", f"waive names unknown rule {rid!r}")
        elif rid not in WAIVABLE:
            ctx.add(key, "WAIVE-ERROR", f"{rid} is an error and can never be waived")
        elif not isinstance(reason, str) or not reason.strip():
            ctx.add(key, "WAIVE-ERROR", f"waive {rid} needs a recorded reason")
        else:
            granted[rid] = reason.strip()
    for f in ctx.findings[start:]:
        if f.severity == "warn" and f.rule in granted:
            f.severity = "waived"
            f.message += f" [waived: {granted[f.rule]}]"


def _lint_entry(ctx: Ctx, e: dict) -> None:
    key = e["_key"]
    en, fr = e.get("en"), e.get("fr")
    args = e.get("args") if isinstance(e.get("args"), dict) else None
    for form, value in forms(en):
        if EMDASH in value or HBAR in value:
            ctx.add(key + form, "TYPO-EMDASH", "em-dash in en (warn: app English stays byte-identical "
                    "while keys are extracted; fixing it is a product change)", "warn")
    if fr is None:
        return
    # Plural shape
    if isinstance(en, dict):
        if not isinstance(fr, dict):
            ctx.add(key, "PLURAL-CATS", "en is plural (one/other); fr must be fr.one and fr.other")
        else:
            missing = [c for c in FR_PLURAL_REQUIRED if c not in fr]
            extra = [c for c in fr if c not in PLURAL_CATS]
            if missing:
                ctx.add(key, "PLURAL-CATS", f"fr lacks {', '.join('fr.' + c for c in missing)}")
            if extra:
                ctx.add(key, "PLURAL-CATS", f"unknown French plural categories {extra}")
        if args is not None:
            ints = [k for k, v in args.items() if v == "int"]
            if len(ints) != 1:
                ctx.add(key, "PLURAL-CATS", f"a plural entry declares exactly one int arg, found {len(ints)}")
            elif isinstance(fr, dict):
                # French `one` covers 0 AND 1, so « Un appel » is wrong for 0 (Android lint
                # ImpliedQuantity). English `one` may say "One call"; this is French only.
                for cat in ("one", "many"):
                    form = fr.get(cat)
                    if isinstance(form, str) and ints[0] not in placeholder_names(form):
                        ctx.add(f"{key}.{cat}", "PLURAL-ONE-COUNT",
                                f"fr.{cat} must contain {{{ints[0]}}}: French '{cat}' also covers "
                                + ("0" if cat == "one" else "large counts"))
    elif en is not None and isinstance(fr, dict):
        ctx.add(key, "PLURAL-CATS", "en is a single string; fr must not be plural")
    # Placeholders
    if en is not None:
        en_names = set().union(*[placeholder_names(v) for _, v in forms(en)]) if forms(en) else set()
        fr_names = set().union(*[placeholder_names(v) for _, v in forms(fr)]) if forms(fr) else set()
        if en_names != fr_names:
            ctx.add(key, "PH-PARITY", f"placeholders en {sorted(en_names)} vs fr {sorted(fr_names)}")
        if args is not None:
            undeclared = (en_names | fr_names) - set(args)
            if undeclared:
                ctx.add(key, "PH-PARITY", f"placeholders not declared in args: {sorted(undeclared)}")
    for form, value in forms(fr):
        if scan_placeholders(value)[1]:
            ctx.add(key + form, "PH-PARITY", f"unbalanced brace in {show(value)} (literal braces are {{{{ and }}}})")
    # Per French form
    is_apple = ctx.platform == "apple"
    names = ctx.tb.never
    os_rows = [r for r in ctx.tb.os if r.get("platform") in ("all", ctx.platform)]
    siblings = os_siblings(os_rows)
    for form, value in forms(fr):
        k = key + form
        en_text = en_for_form(en, form) if en is not None else None
        typography_findings(ctx, k, value)
        for sev_, msg in caps_accent_issues(mask(value, names)):
            ctx.add(k, "CAPS-ACCENT", msg, sev_)
        if e.get("ctx") in SENTENCE_CASE_CTX:
            words = sentence_case_issues(value, en_text or "", names, [r.get("fr", "") for r in os_rows])
            if words:
                ctx.add(k, "SENTENCE-CASE", f"capitalized mid-phrase: {', '.join(words)} (sentence case)")
        for w in tu_issues(value, names):
            ctx.add(k, "REG-TU", f"tutoiement {w!r}: vouvoiement only")
        for sev, msg in ban_issues(value, names, ctx.tb):
            ctx.add(k, "BAN", msg, sev)
        if is_apple and re.search(r"\bparamètres\b", value, re.I):
            ctx.add(k, "OS-WORD", "apple catalog: the OS word is 'Réglages', not 'Paramètres'")
        if not is_apple and re.search(r"\bréglages\b", value, re.I):
            ctx.add(k, "OS-WORD", "non-apple catalog: the OS word is 'Paramètres', not 'Réglages'")
        if is_apple and e.get("screen") not in ctx.allow_screens:
            for rx, what in STORE_PATTERNS:
                if rx.search(value):
                    ctx.add(k, "STORE-APPLE", f"store policy (screen {e.get('screen')!r}): {what}")
        if en_text is None:
            continue
        for name in names:
            if re.search(rf"(?<!\w){re.escape(name)}(?!\w)", en_text) and \
                    not re.search(rf"(?<!\w){re.escape(name)}(?!\w)", value):
                ctx.add(k, "NEVER", f"{name!r} stays as written in French")
        en_prose = mask(en_text, names)  # never names (District AI) are not prose
        for row in ctx.tb.terms:
            if row.get("strict") and term_applies(row, ctx.platform) and row.get("en") and row.get("fr") \
                    and _en_term_rx(row["en"]).search(mask_names_with(en_prose, row["en"])) \
                    and not _plural_rx(row["fr"]).search(value):
                ctx.add(k, "TERM-STRICT", f"en {row['en']!r} must be fr {row['fr']!r}")
        for row in os_rows:
            if not (row.get("en") and row.get("fr")):
                continue
            base, qual = os_term_parts(row["en"])
            sibs = frozenset() if qual else siblings.get(base.lower(), frozenset())
            row_ctx = row.get("ctx") if isinstance(row.get("ctx"), list) else None
            if os_term_applies(row["en"], en_text, e.get("ctx"), sibs, row_ctx) \
                    and not re.search(rf"(?<!\w){re.escape(row['fr'])}(?!\w)", value, re.I):
                ctx.add(k, "OS-WORD", f"OS word {row['en']!r} is {row['fr']!r} on {row.get('platform')}")
        if value.strip() == en_text.strip() and not e.get("same_ok") \
                and LETTERS.search(mask(en_text, [])):
            ctx.add(k, "UNTRANSLATED", "fr equals en (set same_ok = true if intended)")
        budget = e.get("max") if isinstance(e.get("max"), int) else max(math.ceil(len(en_text) * 1.3), len(en_text) + 4)
        if len(value) > budget:
            ctx.add(k, "LEN", f"{len(value)} characters, budget {budget}")
        fr_urls, en_urls = urls_of(value), urls_of(en_text)
        added = sorted(fr_urls - en_urls)
        if added:
            if is_apple:
                ctx.add(k, "STORE-APPLE", f"fr adds a destination en lacks: {added}")
            else:
                ctx.add(k, "URL-PARITY", f"fr names {added}, en does not")
        offer = offer_in(value) if is_apple else None
        if offer and not RE_OFFER_EN.search(en_text):
            ctx.add(k, "STORE-APPLE", f"fr adds an offer en lacks: {offer!r}")
    # Memory
    if en is not None and not e.get("fr_variant"):
        for c, sev in ((e.get("ctx"), "error"), ("*", "warn")):
            if c is None:
                continue
            for row in ctx.tm.get((norm_key(en), c), []):
                if norm_key(row["fr"]) != norm_key(fr):
                    s = sev if row.get("state", "reviewed") in ("reviewed", "signed") else "warn"
                    ctx.add(key, "TM-DRIFT", f"memory ({c}) says {show(str(row['fr']))}; set fr_variant to differ", s)


def lint_toml(path: Path, tb: TermBase, tm: dict, platform: str | None, allow: set[str]) -> tuple[list[Finding], list[dict], dict]:
    text = path.read_text(encoding="utf-8")
    ctx = Ctx(str(path), tb, tm, None, allow)
    try:
        data = tomllib.loads(text)
    except tomllib.TOMLDecodeError as exc:
        ctx.add("-", "PARSE", str(exc))
        return ctx.findings, [], {}
    meta = data.get("meta", {}) if isinstance(data.get("meta"), dict) else {}
    ctx.platform = platform_of(meta, platform)
    for n, line in enumerate(text.splitlines(), 1):
        bad = [c for c in line if c in INVISIBLE or c == NBSP or c in NARROW_SPACES]
        if bad:
            ctx.add(f"line {n}", "TYPO-INVISIBLE", "literal " + ", ".join(sorted({f"U+{ord(c):04X}" for c in bad}))
                    + " in the file: write it as an escape (\\u00A0)")
    entries = toml_entries(data)
    for e in entries:
        lint_entry(ctx, e)
    return ctx.findings, entries, meta


def lint_jsonl(path: Path, tb: TermBase, tm: dict, platform: str | None, allow: set[str]) -> list[Finding]:
    ctx = Ctx(str(path), tb, tm, platform, allow)
    try:
        rows = read_jsonl(path)
    except SystemExit as exc:
        ctx.add("-", "PARSE", str(exc))
        return ctx.findings
    # A batch's out.jsonl carries key, catalog, fr, comment; the English and context come from
    # the in.jsonl beside it (README Translation flow).
    source: dict[tuple, dict] = {}
    sibling = path.with_name("in.jsonl")
    if sibling.is_file() and sibling.resolve() != path.resolve():
        # A unit is found by its key or its id: out.jsonl and final.jsonl rows may carry only `id`.
        for row in read_jsonl(sibling):
            for ident in (row.get("key"), row.get("id")):
                if ident is not None:
                    source.setdefault((row.get("catalog"), ident), row)
                    source.setdefault((None, ident), row)
    for n, row in enumerate(rows, 1):
        if "fr" not in row or row["fr"] is None:
            continue
        src: dict = {}
        for ident in (row.get("key"), row.get("id")):
            if ident is not None:
                src = source.get((row.get("catalog"), ident)) or source.get((None, ident)) or {}
                if src:
                    break
        e = {**src, **row}
        e["_key"] = row_label(row, n)
        lint_unit(ctx, e, platform)
    return ctx.findings


def _non_apple_platform(e: dict) -> str | None:
    """The one non-Apple app platform of a unit seen in several catalogs, else None."""
    plats = {platform_of({"platform": p}, None) for p in e.get("platforms") or [] if p != "swift"}
    plats.discard(None)
    if len(plats) == 1:
        return plats.pop()
    return platform_of({"platform": e.get("platform")}, None) if e.get("platform") != "swift" else None


def apple_variant_words(e: dict, tb: TermBase) -> list[tuple[str, str]]:
    """(form key, word): a non-Apple OS word in fr where the English names the OS thing, so the
    Apple catalogs need their own French (« Réglages » for « Paramètres »)."""
    pairs = [("Settings", "Paramètres")]
    for row in tb.os:
        if row.get("platform") in NON_APPLE_APPS and row.get("en") and row.get("fr"):
            if any(r.get("platform") == "apple" and r.get("en") == row["en"]
                   and str(r.get("fr", "")).lower() != row["fr"].lower() for r in tb.os):
                pairs.append((row["en"], row["fr"]))
    out = []
    for form, value in forms(e.get("fr")):
        en_text = en_for_form(e.get("en"), form) or ""
        for en_word, fr_word in dict.fromkeys(pairs):
            if re.search(rf"(?<!\w){re.escape(en_word)}(?!\w)", en_text) and \
                    re.search(rf"(?<!\w){re.escape(fr_word)}(?!\w)", value, re.I):
                out.append((form, fr_word))
    return out


def lint_unit(ctx: Ctx, e: dict, override: str | None) -> None:
    """One batch unit (README W3). `fr` gets the non-Apple rules unless the unit is Apple-only;
    `fr_apple` gets the Apple rules; an also_apple unit without fr_apple must pass the Apple
    store rules with fr, and needs fr_apple for a non-Apple OS word. --platform overrides fr's."""
    key = e["_key"]
    apple, also = bool(e.get("apple")), bool(e.get("also_apple"))
    fr_apple = e.get("fr_apple")
    if override:
        ctx.platform = override
    elif apple and not also:
        ctx.platform = "apple"
    elif apple:
        ctx.platform = _non_apple_platform(e)
    else:
        ctx.platform = platform_of({"platform": e.get("platform")}, None)
    base = {k: v for k, v in e.items() if k != "fr_apple"}
    lint_entry(ctx, base)
    if fr_apple is not None:
        if not apple:
            ctx.add(key, "OS-WORD-APPLE-VARIANT", "fr_apple on a unit with no Apple catalog: remove it")
            return
        ctx.platform = "apple"
        lint_entry(ctx, {**base, "fr": fr_apple, "_key": key + "@apple"})
        return
    if also and base.get("fr") is not None:
        sub = Ctx(ctx.file, ctx.tb, ctx.tm, "apple", ctx.allow_screens)
        lint_entry(sub, base)
        ctx.findings += [f for f in sub.findings if f.rule == "STORE-APPLE"]
        for form, word in apple_variant_words(base, ctx.tb):
            ctx.add(key + form, "OS-WORD-APPLE-VARIANT",
                    f"{word!r} is the non-Apple OS word and this unit also ships on Apple: needs fr_apple")


def lint_files(files: list[str], tb: TermBase, tm: dict, platform: str | None, allow: set[str]) -> list[Finding]:
    out: list[Finding] = []
    for name in files:
        path = Path(name)
        if path.suffix == ".toml":
            out += lint_toml(path, tb, tm, platform, allow)[0]
        elif path.suffix == ".jsonl":
            out += lint_jsonl(path, tb, tm, platform, allow)
        else:
            out.append(Finding(name, "-", "PARSE", "not a .toml or .jsonl file"))
    return out


CATALOG_DIRS = {"l10n", "catalog", "i18n"}
PRUNE_DIRS = {".git", "node_modules", "testdata", "build", "target", ".gradle", "DerivedData",
              "bin", "obj", ".venv", "venv", "dist", ".next", "out", "Pods", "__pycache__"}


def find_catalogs(root: Path) -> list[Path]:
    found = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in PRUNE_DIRS)
        rel = Path(dirpath).relative_to(root).parts
        if not CATALOG_DIRS.intersection(rel):
            continue
        for f in sorted(filenames):
            if not f.endswith(".toml"):
                continue
            p = Path(dirpath) / f
            try:
                meta = tomllib.loads(p.read_text(encoding="utf-8")).get("meta")
            except (tomllib.TOMLDecodeError, UnicodeDecodeError):
                found.append(p)  # lint reports the parse error
                continue
            if isinstance(meta, dict) and meta.get("catalog"):
                found.append(p)
    return found


def cmd_all(root: Path, tb: TermBase, tm: dict, allow: set[str]) -> list[Finding]:
    findings: list[Finding] = []
    groups: dict[tuple[str, str], list[tuple[str, str, object]]] = {}
    catalogs = find_catalogs(root)
    if not catalogs:
        print(f"{root}: no catalogs found", file=sys.stderr)
    for path in catalogs:
        f, entries, _ = lint_toml(path, tb, tm, None, allow)
        findings += f
        for e in entries:
            if e.get("fr") is not None and not e.get("fr_variant"):
                groups.setdefault((norm_key(e["en"]), str(e.get("ctx"))), []).append((str(path), e["_key"], e["fr"]))
    for (en, c), rows in groups.items():
        variants = {norm_key(fr) for _, _, fr in rows}
        if len(variants) > 1:
            listing = "; ".join(f"{Path(p).name}:{k} = {show(norm_key(fr), 40)}" for p, k, fr in rows)
            for p, k, _ in rows:
                findings.append(Finding(p, k, "XCAT-DRIFT", f"en {show(en, 40)!s} ({c}) has {len(variants)} French versions: {listing}"))
    return findings


def emit(findings: list[Finding], as_json: bool, show_waived: bool = False) -> int:
    if not show_waived:
        findings = [f for f in findings if f.severity != "waived"]
    if as_json:
        print(json.dumps([f.as_dict() for f in findings], ensure_ascii=False, indent=1))
    else:
        for f in findings:
            print(f.line())
        errors = sum(f.severity == "error" for f in findings)
        warns = sum(f.severity == "warn" for f in findings)
        print(f"{errors} error(s), {warns} warning(s)", file=sys.stderr)
    return 1 if any(f.severity == "error" for f in findings) else 0


def self_test() -> int:
    suite = unittest.defaultTestLoader.discover(str(TOOLS_DIR), pattern="test_*.py", top_level_dir=str(TOOLS_DIR))
    result = unittest.TextTestRunner(verbosity=1).run(suite)
    return 0 if result.wasSuccessful() else 1


def _opt_path(value: str | None, default: Path) -> Path | None:
    if value is None:
        return default if default.is_file() else None
    return Path(value) if value else None  # an empty string means "none"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="frcheck.py", description=__doc__.split("\n\n")[0])
    ap.add_argument("--self-test", action="store_true", help="run the unittest suites beside this file")
    sub = ap.add_subparsers(dest="cmd")
    p_fix = sub.add_parser("fix", help="normalize French typography in place")
    p_fix.add_argument("files", nargs="+")
    shared = argparse.ArgumentParser(add_help=False)
    shared.add_argument("--termbase", help="termbase.jsonl ('' for none; default i18n/fr-CA/termbase.jsonl)")
    shared.add_argument("--tm", help="tm.jsonl ('' for none; default i18n/fr-CA/tm.jsonl)")
    shared.add_argument("--allow-screen", action="extend", nargs="+", default=[], metavar="S",
                        help="screens exempt from the Apple store rules")
    shared.add_argument("--json", action="store_true")
    shared.add_argument("--show-waived", action="store_true", help="also print warnings an entry waives")
    p_lint = sub.add_parser("lint", parents=[shared], help="check every French rule")
    p_lint.add_argument("files", nargs="+")
    p_lint.add_argument("--platform", choices=["apple", "android", "windows", "gtk"])
    p_all = sub.add_parser("all", parents=[shared], help="lint every catalog under a repo")
    p_all.add_argument("root")
    sub.add_parser("rules", help="print the rule table")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if a.cmd == "fix":
        return cmd_fix(a.files)
    if a.cmd == "rules":
        for rid, (sev, meaning) in RULES.items():
            print(f"{rid:18} {sev:5} {meaning}")
        return 0
    if a.cmd in ("lint", "all"):
        tb = load_termbase(_opt_path(a.termbase, DEFAULT_TERMBASE))
        tm = load_tm(_opt_path(a.tm, DEFAULT_TM))
        allow = set(a.allow_screen)
        if a.cmd == "lint":
            return emit(lint_files(a.files, tb, tm, a.platform, allow), a.json, a.show_waived)
        return emit(cmd_all(Path(a.root), tb, tm, allow), a.json, a.show_waived)
    ap.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
