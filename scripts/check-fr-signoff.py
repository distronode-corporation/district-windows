#!/usr/bin/env python3
"""Fail when a string catalog is not the one the French sign-off names.

    python3 scripts/check-fr-signoff.py              # the tree against i18n/fr-CA/signoff.json
    python3 scripts/check-fr-signoff.py --self-test  # the rules, against made-up trees

i18n/fr-CA/signoff.json records the review of a release's Canadian French: who
signed it off, the release and the commit the catalogs were read at, and the
SHA-256 of every string catalog at that commit:

    {"signed_off_by": "...", "release": "v1.2.3", "commit": "<40 hex>",
     "catalogs": [{"path": "l10n/app.toml", "sha256": "<64 hex>"}, ...]}

A catalog is what `tools/l10n/frcheck.py all` lints (its find_catalogs): a .toml
file with a `[meta] catalog` key, in a directory named l10n, catalog or i18n,
build output pruned. The check fails, and names each catalog, when one's bytes
differ from the hash the sign-off records, when one the sign-off lists is gone,
or when one exists that the sign-off does not list. A changed catalog is printed
with its hash in the tree, which is what a renewed sign-off records.

Run by the release workflow's guard, on the tag build only: catalogs change
between releases, and the sign-off is renewed per release (CONTRIBUTING.md), so
pull requests run `--self-test` alone. Python 3.11 or newer, standard library
only.
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SIGNOFF = Path("i18n/fr-CA/signoff.json")

# No __pycache__ in tools/l10n, whose files `l10n.py vendor --check` pins.
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / "tools" / "l10n"))
from frcheck import find_catalogs  # noqa: E402  (the vendored tools, one definition of a catalog)

HEX40 = re.compile(r"^[0-9a-f]{40}$")
HEX64 = re.compile(r"^[0-9a-f]{64}$")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tree_catalogs(root: Path) -> dict[str, str]:
    """Each catalog under `root`, by its path relative to `root`, to its SHA-256."""
    return {p.relative_to(root).as_posix(): sha256(p) for p in find_catalogs(root)}


def read_signoff(root: Path) -> tuple[dict, list[str]]:
    """The sign-off's fields and catalogs (path to hash), and what is wrong with
    the file itself. Any error here fails the check before a hash is compared."""
    path = root / SIGNOFF
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return {}, [f"{SIGNOFF} is missing"]
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        return {}, [f"{SIGNOFF} is not valid JSON: {exc}"]
    if not isinstance(data, dict):
        return {}, [f"{SIGNOFF} must be a JSON object"]
    errors = []
    for key in ("signed_off_by", "release", "commit"):
        if not isinstance(data.get(key), str) or not data[key].strip():
            errors.append(f"{SIGNOFF}: `{key}` must be a non-empty string")
    if isinstance(data.get("commit"), str) and not HEX40.match(data["commit"]):
        errors.append(f"{SIGNOFF}: `commit` must be a full 40-character commit id")
    rows = data.get("catalogs")
    recorded: dict[str, str] = {}
    if not isinstance(rows, list) or not rows:
        errors.append(f"{SIGNOFF}: `catalogs` must be a non-empty list")
        rows = []
    for n, row in enumerate(rows):
        if not isinstance(row, dict) or set(row) != {"path", "sha256"}:
            errors.append(f"{SIGNOFF}: catalogs[{n}] must be exactly {{\"path\", \"sha256\"}}")
            continue
        p, h = row["path"], row["sha256"]
        if not isinstance(p, str) or not p or not isinstance(h, str) or not HEX64.match(h):
            errors.append(f"{SIGNOFF}: catalogs[{n}] needs a path and a lowercase 64-hex sha256")
        elif p in recorded:
            errors.append(f"{SIGNOFF}: {p} is listed twice")
        else:
            recorded[p] = h
    data["catalogs"] = recorded
    return data, errors


def compare(recorded: dict[str, str], tree: dict[str, str]) -> list[str]:
    """One line per catalog that is not as signed off, in path order."""
    out = []
    for p in sorted(recorded.keys() | tree.keys()):
        if p not in tree:
            out.append(f"{p}: signed off, but no longer a catalog in the tree")
        elif p not in recorded:
            out.append(f"{p}: a catalog the sign-off does not list (sha256 {tree[p]})")
        elif recorded[p] != tree[p]:
            out.append(f"{p}: changed since the sign-off (signed off {recorded[p]}, now {tree[p]})")
    return out


def check(root: Path) -> tuple[list[str], dict]:
    data, errors = read_signoff(root)
    if errors:
        return errors, data
    return compare(data["catalogs"], tree_catalogs(root)), data


def self_test() -> int:
    """The rules, against made-up trees."""
    failures = []

    def expect(label: str, got, want) -> None:
        if got != want:
            failures.append(f"{label}: got {got!r}, want {want!r}")

    catalog = '[meta]\ncatalog = "app"\n\n[hello]\nen = "Hello"\nfr = "Bonjour"\n'
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        (root / "l10n").mkdir()
        (root / "l10n" / "app.toml").write_text(catalog, encoding="utf-8")
        # Not catalogs: no [meta] catalog key, outside a catalog directory, or
        # inside pruned build output.
        (root / "l10n" / "SOURCE.toml").write_text('commit = "x"\n', encoding="utf-8")
        (root / "src").mkdir()
        (root / "src" / "loose.toml").write_text(catalog, encoding="utf-8")
        (root / "target" / "l10n").mkdir(parents=True)
        (root / "target" / "l10n" / "built.toml").write_text(catalog, encoding="utf-8")

        expect("only the real catalog is found", sorted(tree_catalogs(root)), ["l10n/app.toml"])
        expect("no sign-off fails", check(root)[0], [f"{SIGNOFF} is missing"])

        good = hashlib.sha256(catalog.encode()).hexdigest()
        base = {"signed_off_by": "review", "release": "v1.0.0", "commit": "a" * 40,
                "catalogs": [{"path": "l10n/app.toml", "sha256": good}]}

        def write(doc) -> None:
            (root / SIGNOFF).parent.mkdir(parents=True, exist_ok=True)
            (root / SIGNOFF).write_text(json.dumps(doc), encoding="utf-8")

        write(base)
        expect("the signed-off tree passes", check(root)[0], [])

        (root / "l10n" / "app.toml").write_text(catalog.replace("Bonjour", "Salut"), encoding="utf-8")
        found = check(root)[0]
        expect("a changed catalog is named", [line.split(":")[0] for line in found], ["l10n/app.toml"])
        expect("and called changed", "changed since the sign-off" in found[0], True)
        (root / "l10n" / "app.toml").write_text(catalog, encoding="utf-8")

        (root / "l10n" / "new.toml").write_text(catalog.replace('"app"', '"new"'), encoding="utf-8")
        found = check(root)[0]
        expect("an unlisted catalog is named", [line.split(":")[0] for line in found], ["l10n/new.toml"])
        (root / "l10n" / "new.toml").unlink()

        (root / "l10n" / "app.toml").unlink()
        found = check(root)[0]
        expect("a removed catalog is named", [line.split(":")[0] for line in found], ["l10n/app.toml"])
        (root / "l10n" / "app.toml").write_text(catalog, encoding="utf-8")

        for label, doc in (
            ("a short commit id", {**base, "commit": "abc1234"}),
            ("no signer", {**base, "signed_off_by": ""}),
            ("no catalogs", {**base, "catalogs": []}),
            ("an uppercase hash", {**base, "catalogs": [{"path": "l10n/app.toml", "sha256": good.upper()}]}),
            ("an extra field", {**base, "catalogs": [{"path": "l10n/app.toml", "sha256": good, "x": 1}]}),
            ("a path listed twice", {**base, "catalogs": base["catalogs"] * 2}),
        ):
            write(doc)
            expect(f"{label} is refused", len(check(root)[0]), 1)
        (root / SIGNOFF).write_text("{", encoding="utf-8")
        expect("broken JSON is refused", len(check(root)[0]), 1)

    for failure in failures:
        print(f"ERROR: self-test: {failure}", file=sys.stderr)
    if failures:
        return 1
    print("self-test passed")
    return 0


def main(argv: list[str]) -> int:
    if argv[1:] == ["--self-test"]:
        return self_test()
    if argv[1:]:
        print(__doc__, file=sys.stderr)
        return 2
    errors, data = check(ROOT)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        if isinstance(data.get("release"), str) and isinstance(data.get("commit"), str):
            print(
                f"\nThe French was signed off for {data['release']} at {data['commit'][:12]}."
                " A release whose catalogs changed needs the manager's review of the French"
                f" and a new {SIGNOFF} recording the catalogs' new hashes (CONTRIBUTING.md).",
                file=sys.stderr,
            )
        return 1
    n = len(data["catalogs"])
    print(f"{n} catalog(s) match the French sign-off for {data['release']} ({data['signed_off_by']})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
