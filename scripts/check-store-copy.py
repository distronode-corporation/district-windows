#!/usr/bin/env python3
"""Fail on sign-up and purchase wording in the app's user-facing strings.

    python3 scripts/check-store-copy.py              # scan the app
    python3 scripts/check-store-copy.py --self-test  # prove the rule works

District AI 1.0 for Windows is listed in the Microsoft Store as a client for an
existing District AI account: nobody signs up in the app and nothing is bought
in it. The listing has to stay true, so the app's strings may not offer either.
These phrases are refused, case-insensitively and as whole words:

    sign up, sign-up, signup, create an account, create account,
    buy, pricing, price, subscribe (subscription), free trial

Where the scan looks, and what counts as a user-facing string there:

    src/DistrictAI/**/*.xaml        the attributes that put text on screen or
                                    in front of a screen reader (TEXT_ATTRIBUTES
                                    below) and the text inside an element
    src/DistrictAI/**/*.cs          C# string literals (regular, verbatim,
                                    interpolated and raw); comments are skipped
    src/DistrictAI.Presentation/**/*.cs
                                    the same, in the view models
    **/*.resw                       every <value> of a resource file
    crates/district-ffi/src/**/*.rs Rust string literals, which become the
                                    projections' text; comments are skipped
    crates/district-ffi/tests/snapshots/*.json
                                    every string value, at any depth (object
                                    keys are field names, not copy, and are
                                    skipped): the text the core projects for
                                    each screen state, such as plan names and
                                    amounts, which no source literal holds

A snapshot finding is reported by file, JSON path (`$.screen.Account.view.x`)
and the whole string that matched, and is tied to the screen it comes from: the
variant under `screen`, or `shell` for the chrome around every screen.

A finding that is genuinely fine goes in ALLOW below, keyed by the exact
`path:line` (a snapshot's by `path:$.json.path`) and with the reason. An ALLOW entry that no longer matches anything
is itself a finding, so the list cannot outlive what it excuses.

`--self-test` plants each phrase in each kind of file, and look-alikes that
must not trip it (comments, code, words that merely contain a phrase), and
fails unless every case answers as expected.

Per-screen rules (packet K4, not built yet): today every screen gets the same
rule, `phrases_for_screen` returns PHRASES whatever the screen. When 2.0 adds
billing, checkout, marketplace and welcome screens that may say "sign up" or
"price", K4 splits PHRASES into the words those screens may use and the rest,
and has `phrases_for_screen` return the narrower pattern for a screen in a set
of allowed names. Snapshot findings already carry their screen, so nothing else
in the snapshot scan changes; source literals have no screen (`None`) and keep
the full rule.
"""

from __future__ import annotations

import json
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Whole words, so "buyer's" is a finding but "buoyant", "priceless" or
# "unsubscribed" are not: the word boundary on both sides holds the match to the
# listed forms.
PHRASES = re.compile(
    r"\b(?:"
    r"sign[\s\-]*up"
    r"|create\s+(?:an\s+)?account"
    r"|buy(?:s|ing|er|ers)?"
    r"|pric(?:e|es|ed|ing)"
    r"|subscri(?:be|bes|bed|bing|ber|bers|ption|ptions)"
    r"|free\s+trials?"
    r")\b",
    re.IGNORECASE,
)


def phrases_for_screen(screen: str | None) -> re.Pattern[str]:
    """The refused wording for text shown on `screen` (None: not tied to one).

    One rule for every screen today; K4 narrows it per screen (module docstring).
    """
    return PHRASES

# `path:line` (forward slashes, relative to the repository root) to the reason
# the line is fine. None is expected for 1.0.
ALLOW: dict[str, str] = {}

# XAML attributes whose value reaches the screen or a screen reader. A binding
# (`{x:Bind ...}`, `{Binding ...}`) is code, not copy: what it shows comes from a
# C# or Rust string, which the scan reads where it is written.
TEXT_ATTRIBUTES = {
    "Text",
    "Content",
    "Header",
    "PlaceholderText",
    "AutomationProperties.Name",
    "AutomationProperties.HelpText",
    "ToolTipService.ToolTip",
    "Title",
    "Description",
    "Message",
    "Label",
    "PrimaryButtonText",
    "SecondaryButtonText",
    "CloseButtonText",
    "OnContent",
    "OffContent",
}


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    phrase: str
    where: str
    # Snapshot findings only: the JSON path of the value, the whole value, and
    # the screen it comes from.
    pointer: str = ""
    text: str = ""
    screen: str | None = None

    @property
    def key(self) -> str:
        return f"{self.path}:{self.pointer or self.line}"

    def __str__(self) -> str:
        if self.pointer:
            return f"{self.key}: {self.phrase!r} in {self.where} ({self.screen} screen): {self.text!r}"
        return f"{self.key}: {self.phrase!r} in {self.where}"


def _line_at(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def _phrases(path: str, text: str, start: int, body: str, where: str) -> list[Finding]:
    """Findings in `body`, which begins at `start` in the file's `text`."""
    return [
        Finding(path, _line_at(text, start + m.start()), m.group(0), where)
        for m in PHRASES.finditer(body)
    ]


# ---------------------------------------------------------------------------
# C#

def csharp_strings(text: str) -> list[tuple[int, str]]:
    """Every string literal in C# source, as (offset of its body, body).

    Comments and character literals are skipped. An interpolated string's holes
    are returned with its text; they are code, and code rarely holds the phrases.
    """
    out: list[tuple[int, str]] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == "'":
            # A character literal: 'x', '\n', 'A', '"'.
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            i = j + 1
            continue
        # Prefixes: $, @, $@, @$, and any number of $ before a raw string.
        j = i
        while j < n and text[j] in "$@":
            j += 1
        prefix = text[i:j]
        if j < n and text[j] == '"' and (not prefix or set(prefix) <= {"$", "@"}):
            if text.startswith('"""', j):
                quotes = 3
                while j + quotes < n and text[j + quotes] == '"':
                    quotes += 1
                fence = '"' * quotes
                end = text.find(fence, j + quotes)
                end = n if end < 0 else end
                out.append((j + quotes, text[j + quotes:end]))
                i = end + quotes
                continue
            verbatim = "@" in prefix
            k = j + 1
            while k < n:
                if verbatim and text.startswith('""', k):
                    k += 2
                    continue
                if not verbatim and text[k] == "\\":
                    k += 2
                    continue
                if text[k] == '"' or (not verbatim and text[k] == "\n"):
                    break
                k += 1
            out.append((j + 1, text[j + 1:k]))
            i = k + 1
            continue
        i = max(j, i + 1)
    return out


def scan_csharp(path: str, text: str) -> list[Finding]:
    found: list[Finding] = []
    for start, body in csharp_strings(text):
        found.extend(_phrases(path, text, start, body, "a C# string"))
    return found


# ---------------------------------------------------------------------------
# Rust

def rust_strings(text: str) -> list[tuple[int, str]]:
    """Every string literal in Rust source, as (offset of its body, body).

    Line and (nested) block comments are skipped, doc comments with them, and so
    are character literals and lifetimes. Raw (`r"..."`, `r#"..."#`) and byte
    strings are read like any other.
    """
    out: list[tuple[int, str]] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            depth, j = 1, i + 2
            while j < n and depth:
                if text.startswith("/*", j):
                    depth, j = depth + 1, j + 2
                elif text.startswith("*/", j):
                    depth, j = depth - 1, j + 2
                else:
                    j += 1
            i = j
            continue
        if c == "'":
            # '\n', '\u{41}', 'x' are characters; 'a (no closing quote right
            # after one character) is a lifetime or a label.
            if i + 1 < n and text[i + 1] == "\\":
                j = i + 2
                while j < n and text[j] != "'":
                    j += 1
                i = j + 1
            elif i + 2 < n and text[i + 2] == "'":
                i += 3
            else:
                i += 1
            continue
        # An identifier is skipped whole, so the r or b of `for` or `sub"` is
        # never read as a raw or byte string prefix.
        m = re.match(r"(?:b?r(#*)\"|b?\")", text[i:i + 300]) if (i == 0 or not (text[i - 1].isalnum() or text[i - 1] == "_")) else None
        if m:
            body_start = i + m.end()
            if "r" in m.group(0):
                fence = '"' + m.group(1)
                end = text.find(fence, body_start)
                end = n if end < 0 else end
                out.append((body_start, text[body_start:end]))
                i = end + len(fence)
            else:
                k = body_start
                while k < n and text[k] != '"':
                    k += 2 if text[k] == "\\" else 1
                out.append((body_start, text[body_start:k]))
                i = k + 1
            continue
        if c.isalnum() or c == "_":
            j = i
            while j < n and (text[j].isalnum() or text[j] == "_"):
                j += 1
            i = j
            continue
        i += 1
    return out


def scan_rust(path: str, text: str) -> list[Finding]:
    found: list[Finding] = []
    for start, body in rust_strings(text):
        found.extend(_phrases(path, text, start, body, "a Rust string"))
    return found


# ---------------------------------------------------------------------------
# XAML and .resw

def _xml_lines(text: str) -> tuple[ET.Element, dict[int, int]]:
    """The parsed document, with each element mapped to its source line.

    ElementTree does not record positions, so the line comes from the parser's
    position when it reads each start tag.
    """
    parser = ET.XMLPullParser(events=("start",))
    lines: dict[int, int] = {}
    root: ET.Element | None = None
    for number, line in enumerate(text.splitlines(keepends=True), start=1):
        parser.feed(line)
        for _, element in parser.read_events():
            lines.setdefault(id(element), number)
            if root is None:
                root = element
    parser.close()
    for _, element in parser.read_events():
        lines.setdefault(id(element), len(text.splitlines()))
    if root is None:
        raise ValueError("no root element")
    return root, lines


def _attribute_line(text: str, element_line: int, name: str, value: str) -> int:
    """The line an attribute is written on, searched from its element's line on.

    An element's start tag can span lines; the pull parser reports the tag's
    last line, so the search starts a few lines up and takes the first match.
    """
    lines = text.splitlines()
    pattern = re.compile(r"(?<![\w.:])" + re.escape(name) + r"\s*=\s*([\"'])" + re.escape(value.split("\n")[0][:40]))
    for number in range(max(1, element_line - 60), min(len(lines), element_line + 1) + 1):
        if pattern.search(lines[number - 1]):
            return number
    return element_line


def _local(name: str) -> str:
    return name.rsplit("}", 1)[-1]


def scan_xaml(path: str, text: str) -> list[Finding]:
    root, lines = _xml_lines(text)
    found: list[Finding] = []
    for element in root.iter():
        line = lines.get(id(element), 1)
        for raw_name, value in element.attrib.items():
            name = _local(raw_name)
            if name not in TEXT_ATTRIBUTES or value.lstrip().startswith("{"):
                continue
            at = _attribute_line(text, line, name, value)
            for m in PHRASES.finditer(value):
                found.append(Finding(path, at, m.group(0), f"XAML {name}"))
        if element.text and element.text.strip() and not element.text.strip().startswith("{"):
            for m in PHRASES.finditer(element.text):
                found.append(Finding(path, line, m.group(0), f"XAML <{_local(element.tag)}> text"))
    return found


def scan_resw(path: str, text: str) -> list[Finding]:
    root, lines = _xml_lines(text)
    found: list[Finding] = []
    for element in root.iter():
        if _local(element.tag) != "value" or not element.text:
            continue
        for m in PHRASES.finditer(element.text):
            found.append(Finding(path, lines.get(id(element), 1), m.group(0), "a .resw value"))
    return found


# ---------------------------------------------------------------------------
# Projection snapshots (JSON)

_IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")


def json_strings(value: object, pointer: str = "$") -> list[tuple[str, str]]:
    """Every string value under `value`, as (JSON path, string). Keys are not values."""
    if isinstance(value, str):
        return [(pointer, value)]
    out: list[tuple[str, str]] = []
    if isinstance(value, dict):
        for key, child in value.items():
            step = f".{key}" if _IDENTIFIER.match(key) else f"[{json.dumps(key)}]"
            out.extend(json_strings(child, pointer + step))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            out.extend(json_strings(child, f"{pointer}[{index}]"))
    return out


def _screen_of(document: object, pointer: str) -> str | None:
    """The screen a value belongs to: the variant under `screen`, else `shell`."""
    if pointer.startswith("$.screen") and isinstance(document, dict):
        screen = document.get("screen")
        if isinstance(screen, dict) and len(screen) == 1:
            return next(iter(screen))
        if isinstance(screen, str):
            return screen
    if pointer.startswith("$.shell"):
        return "shell"
    return None


def scan_snapshot(path: str, text: str) -> list[Finding]:
    document = json.loads(text)
    found: list[Finding] = []
    for pointer, value in json_strings(document):
        screen = _screen_of(document, pointer)
        for m in phrases_for_screen(screen).finditer(value):
            found.append(Finding(path, 1, m.group(0), "a projection snapshot", pointer, value, screen))
    return found


# ---------------------------------------------------------------------------

SNAPSHOTS = "crates/district-ffi/tests/snapshots/*.json"
SKIP_DIRS = {"bin", "obj", "target", ".git", "AppPackages"}


def _files(root: Path, pattern: str) -> list[Path]:
    return sorted(
        p for p in root.glob(pattern)
        if p.is_file() and not SKIP_DIRS.intersection(p.relative_to(root).parts)
    )


def scan_tree(root: Path) -> tuple[int, list[Finding]]:
    plan = [
        ("src/DistrictAI/**/*.xaml", scan_xaml),
        ("src/DistrictAI/**/*.cs", scan_csharp),
        ("src/DistrictAI.Presentation/**/*.cs", scan_csharp),
        ("**/*.resw", scan_resw),
        ("crates/district-ffi/src/**/*.rs", scan_rust),
        (SNAPSHOTS, scan_snapshot),
    ]
    scanned = 0
    found: list[Finding] = []
    for pattern, scan in plan:
        for path in _files(root, pattern):
            scanned += 1
            rel = path.relative_to(root).as_posix()
            found.extend(scan(rel, path.read_text(encoding="utf-8-sig")))
    return scanned, found


def verdict(found: list[Finding], allow: dict[str, str]) -> tuple[list[Finding], list[str]]:
    """The findings ALLOW does not excuse, and the ALLOW keys that excuse nothing."""
    keys = {f.key for f in found}
    return [f for f in found if f.key not in allow], sorted(k for k in allow if k not in keys)


def self_test() -> int:
    failed = 0
    total = 0

    def expect(label: str, got: list[Finding], want: int) -> None:
        nonlocal failed, total
        total += 1
        if len(got) != want:
            failed += 1
            print(f"self-test: {label}: expected {want} finding(s), got {len(got)}: {[str(f) for f in got]}")

    phrases = [
        "Sign up", "SIGN-UP", "signup", "Create an account", "create account",
        "Buy", "buying", "Pricing", "price", "Subscribe", "subscription", "Free trial",
    ]
    for phrase in phrases:
        expect(f"C# {phrase!r}", scan_csharp("a.cs", f'var s = "{phrase} now";\n'), 1)
        expect(f"Rust {phrase!r}", scan_rust("a.rs", f'let s = "{phrase} now";\n'), 1)
        expect(
            f"XAML Content {phrase!r}",
            scan_xaml("a.xaml", f'<Page xmlns="urn:x"><Button Content="{phrase}" /></Page>\n'),
            1,
        )
        expect(
            f".resw {phrase!r}",
            scan_resw("a.resw", f'<root><data name="x"><value>{phrase}</value></data></root>\n'),
            1,
        )

    # Every kind of C# literal is read, on the line the phrase is on.
    cs = (
        'class A {\n'
        '  string a = @"verbatim ""quoted""\n'
        'Buy it";\n'
        '  string b = $"Hello {name}, sign up";\n'
        '  string c = """\n'
        '    raw free trial\n'
        '    """;\n'
        '  string d = $@"Pricing {x}";\n'
        '}\n'
    )
    got = scan_csharp("a.cs", cs)
    expect("C# literal kinds", got, 4)
    if [f.line for f in got] != [3, 4, 6, 8]:
        failed += 1
        print(f"self-test: C# lines should be [3, 4, 6, 8], got {[f.line for f in got]}")

    # C# that must NOT be a finding: comments, code, character literals, and
    # words that only contain a phrase.
    cs_clean = (
        '// Sign up is not offered; nothing to buy.\n'
        '/* subscribe to the price feed */\n'
        '/// <summary>Create an account elsewhere.</summary>\n'
        'void Subscribe(Price price) { var q = \'"\'; Buy(); }\n'
        'var s = "Signed in. A buoyant, priceless, unsubscribed day.";\n'
    )
    expect("C# comments and code", scan_csharp("a.cs", cs_clean), 0)
    expect("C# escaped quote keeps the string open", scan_csharp("a.cs", 'var t = "a \\" sign up";\n'), 1)

    # Rust: raw and byte strings are read; comments, doc comments, lifetimes,
    # characters and identifiers are not.
    rs = (
        "/// Sign up is never offered.\n"
        "// buy\n"
        "/* outer /* nested price */ still a comment: subscribe */\n"
        "fn subscribe<'a>(price: &'a str) -> char { let q = '\"'; 'x' }\n"
        "fn f() { for_buy(); let a = r#\"raw \"Pricing\" here\"#; let b = b\"sign-up\"; }\n"
    )
    got = scan_rust("a.rs", rs)
    expect("Rust kinds", got, 2)
    if [f.line for f in got] != [5, 5]:
        failed += 1
        print(f"self-test: Rust lines should be [5, 5], got {[f.line for f in got]}")

    # XAML: each text attribute and element text count; bindings, other
    # attributes and x:Name do not.
    xaml = (
        '<Page xmlns="urn:x" xmlns:x="urn:xx">\n'
        '  <TextBlock Text="Sign up today" />\n'
        '  <TextBox\n'
        '      x:Name="BuyBox"\n'
        '      PlaceholderText="Free trial code" />\n'
        '  <Button AutomationProperties.Name="Buy" Content="{x:Bind ViewModel.Price}" Tag="price" />\n'
        '  <ContentDialog Header="Pricing" PrimaryButtonText="Subscribe" />\n'
        '  <TextBlock>Create an account</TextBlock>\n'
        '  <!-- Sign up is not offered -->\n'
        '</Page>\n'
    )
    got = scan_xaml("a.xaml", xaml)
    expect("XAML attributes and text", got, 6)
    if sorted(f.line for f in got) != [2, 5, 6, 7, 7, 8]:
        failed += 1
        print(f"self-test: XAML lines should be [2, 5, 6, 7, 7, 8], got {sorted(f.line for f in got)}")

    # Snapshots: a string value at any depth is read, with its JSON path, text
    # and screen; keys, numbers, booleans and nulls are not.
    snapshot = json.dumps({
        "screen": {"Billing": {"view": {
            "plans": [{"name": "Team", "caption": "Free trial for 14 days"}],
            "subscription": None,
            "price": 12,
            "buy": True,
            "pricing_note": "Billed in the browser",
        }}},
        "shell": {"notice": {"text": "Sign up on the web"}, "Subscribe": "ok"},
    }, indent=2)
    got = scan_snapshot("s.json", snapshot)
    expect("snapshot nested values, keys ignored", got, 2)
    want = [
        ("s.json:$.screen.Billing.view.plans[0].caption", "Free trial", "Free trial for 14 days", "Billing"),
        ("s.json:$.shell.notice.text", "Sign up", "Sign up on the web", "shell"),
    ]
    total += 1
    if [(f.key, f.phrase, f.text, f.screen) for f in got] != want:
        failed += 1
        print(f"self-test: snapshot findings should be {want}, got {[(f.key, f.phrase, f.text, f.screen) for f in got]}")
    got = scan_snapshot("s.json", '{"a b": ["x", {"y": "buy"}]}')
    expect("snapshot value under a quoted key", got, 1)
    total += 1
    if [f.key for f in got] != ['s.json:$["a b"][1].y']:
        failed += 1
        print(f"self-test: a key that is not an identifier should be quoted in the JSON path, got {[f.key for f in got]}")

    # The tree walk reads the six places and nothing else, and ALLOW excuses by
    # exact path:line and reports a stale entry.
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        for rel, body in {
            "src/DistrictAI/Views/P.xaml": '<Page xmlns="urn:x"><Button Content="Buy" /></Page>\n',
            "src/DistrictAI/A.cs": 'var s = "Sign up";\n',
            "src/DistrictAI.Presentation/Billing/BillingViewModel.cs": 'var s = "Free trial";\n',
            "src/DistrictAI.Presentation/obj/Generated.cs": 'var s = "Sign up";\n',
            "src/DistrictAI/Strings/en-US/Resources.resw": "<root><data name=\"a\"><value>Pricing</value></data></root>\n",
            "crates/district-ffi/src/screen.rs": 'const S: &str = "Subscribe";\n',
            "src/DistrictAI/obj/Generated.cs": 'var s = "Sign up";\n',
            "src/DistrictAI.Core/Generated/x.cs": 'var s = "Sign up";\n',
            "crates/other/src/lib.rs": 'const S: &str = "Buy";\n',
            "crates/district-ffi/tests/snapshots/billing.json": '{"screen": {"Billing": {"view": {"cta": "Subscribe"}}}}\n',
            "crates/district-ffi/tests/snapshots/nested/x.json": '{"t": "Buy"}\n',
            "crates/district-ffi/tests/fixtures/x.json": '{"t": "Buy"}\n',
        }.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(body, encoding="utf-8")
        scanned, found = scan_tree(root)
        expect("tree", found, 6)
        if scanned != 6:
            failed += 1
            print(f"self-test: the tree walk should read 6 files, read {scanned}")
        left, stale = verdict(found, {
            "src/DistrictAI/A.cs:1": "test",
            "crates/district-ffi/tests/snapshots/billing.json:$.screen.Billing.view.cta": "test",
            "src/DistrictAI/A.cs:2": "stale",
        })
        total += 1
        if len(left) != 4 or stale != ["src/DistrictAI/A.cs:2"]:
            failed += 1
            print(f"self-test: ALLOW should excuse one finding and report one stale key, got {left} {stale}")

    if failed:
        print(f"self-test FAILED: {failed} of {total} cases")
        return 1
    print(f"self-test passed: {total} cases")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    if sys.argv[1:]:
        print(__doc__)
        return 2
    scanned, found = scan_tree(ROOT)
    left, stale = verdict(found, ALLOW)
    for finding in left:
        print(finding)
    for key in stale:
        print(f"{key}: ALLOW entry matches nothing any more; remove it")
    if left or stale:
        print(
            "District AI 1.0 offers no sign-up and no purchase in the app, and its Store "
            "listing says so. Reword the string, or, if it is genuinely fine, add its "
            "path:line (a snapshot's path:$.json.path) to ALLOW in scripts/check-store-copy.py "
            "with the reason. A snapshot finding is text the core projects: reword it where the "
            "core writes it, then refresh the snapshot."
        )
        return 1
    if scanned == 0:
        print("no files scanned: the scan's globs no longer match the tree")
        return 1
    if not _files(ROOT, SNAPSHOTS):
        print(f"no projection snapshots at {SNAPSHOTS}: the snapshot scan read nothing")
        return 1
    print(f"store copy: {scanned} files, no sign-up or purchase wording")
    return 0


if __name__ == "__main__":
    sys.exit(main())
