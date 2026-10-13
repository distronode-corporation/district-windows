#!/usr/bin/env python3
"""Each package declares French exactly when the build switched French on.

    python3 scripts/check-languages.py --french false PACKAGE...   # no package may declare fr-CA
    python3 scripts/check-languages.py --french true PACKAGE...    # every package must
    python3 scripts/check-languages.py --self-test                  # prove the rule works

French ships only in a build made with -p:DistrictFrench=true
(src/DistrictAI/DistrictAI.csproj). Until then nobody may see it, and the Store
must not list the app as speaking it. Two places in a package say which
languages it has, and both are read:

    AppxManifest.xml   the Package/Resources/Resource elements the packaging
                       writes from the resources it indexed (EN-US, FR-CA)
    resources.pri      the language qualifiers of the indexed resources,
                       found as UTF-16 text in the file (makepri dump shows the
                       same, but needs Windows)

A PACKAGE is an .msix, or an .msixupload / .msixbundle holding .msix files,
each of which is read. With --french false, a French language (fr, fr-CA,
any fr-*) anywhere in either place is a finding; with --french true, both
places must name fr-CA in every package.

The spike for this (district-windows PR #67) found that a .resw kept out of a
build with `PRIResource Remove` still reached resources.pri: this check is
what proves a package is English only, rather than the project file.
"""

from __future__ import annotations

import io
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

FOUNDATION = "{http://schemas.microsoft.com/appx/manifest/foundation/windows10}"

# A language tag whose primary subtag is French, as a whole tag: fr, fr-CA,
# FR-FR, fr-Latn-CA. Not "frame" or "afr".
FRENCH = re.compile(r"(?<![A-Za-z0-9-])fr(?:-[A-Za-z0-9]{1,8})*(?![A-Za-z0-9-])", re.IGNORECASE)
# A language qualifier in resources.pri: a tag of letters, digits and dashes
# with a region, in UTF-16 (what the PRI's qualifier values are stored as).
PRI_TAG = re.compile(r"(?<![A-Za-z0-9-])[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})+(?![A-Za-z0-9-])")


def manifest_languages(xml: bytes) -> list[str]:
    root = ET.fromstring(xml)
    return [r.get("Language", "") for r in root.iter(f"{FOUNDATION}Resource")]


def pri_languages(pri: bytes) -> set[str]:
    """The language-like tags written in UTF-16 anywhere in `pri`, upper case."""
    found: set[str] = set()
    for start in (0, 1):
        text = pri[start:].decode("utf-16-le", errors="ignore")
        found.update(tag.upper() for tag in PRI_TAG.findall(text))
    return found


def french(tags) -> list[str]:
    return sorted({t for t in tags if FRENCH.fullmatch(t)})


def msix_parts(path: str, data: bytes) -> list[tuple[str, bytes, bytes]]:
    """(label, AppxManifest.xml, resources.pri) of each .msix in `data`."""
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = archive.namelist()
        if "AppxManifest.xml" in names:
            pri = archive.read("resources.pri") if "resources.pri" in names else b""
            return [(path, archive.read("AppxManifest.xml"), pri)]
        inner = [n for n in names if n.lower().endswith(".msix")]
        parts: list[tuple[str, bytes, bytes]] = []
        for name in inner:
            parts.extend(msix_parts(f"{path}!{name}", archive.read(name)))
        return parts


def problems(path: str, data: bytes, want_french: bool) -> list[str]:
    found = []
    parts = msix_parts(path, data)
    if not parts:
        return [f"{path}: no .msix package in it"]
    for label, manifest, pri in parts:
        if not pri:
            found.append(f"{label}: no resources.pri")
            continue
        tags = pri_languages(pri)
        # The reader must see the English every package has, or a French it
        # missed would pass for none.
        if "EN-US" not in tags:
            found.append(f"{label}: no EN-US language read from resources.pri, so its languages cannot be told")
            continue
        in_manifest = french(manifest_languages(manifest))
        in_pri = french(tags)
        if want_french:
            if "FR-CA" not in {t.upper() for t in in_manifest}:
                found.append(f"{label}: the manifest's Resources do not declare fr-CA (French is on)")
            if "FR-CA" not in in_pri:
                found.append(f"{label}: resources.pri has no fr-CA resource (French is on)")
        else:
            if in_manifest:
                found.append(f"{label}: the manifest's Resources declare {', '.join(in_manifest)} while French is off")
            if in_pri:
                found.append(f"{label}: resources.pri holds {', '.join(in_pri)} resources while French is off")
    return found


def _msix(languages: list[str], pri_tags: list[str]) -> bytes:
    resources = "".join(f'<Resource Language="{tag}" />' for tag in languages)
    manifest = f'<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Resources>{resources}</Resources></Package>'
    pri = b"mrm_pri2\0\0" + b"".join(b"\x01\x02" + tag.encode("utf-16-le") + b"\0\0" for tag in pri_tags)
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as archive:
        archive.writestr("AppxManifest.xml", manifest)
        archive.writestr("resources.pri", pri)
    return out.getvalue()


def _bundle(*packages: bytes) -> bytes:
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as archive:
        for i, package in enumerate(packages):
            archive.writestr(f"DistrictAI_{i}_x64.msix", package)
    return out.getvalue()


def self_test() -> int:
    english = _msix(["EN-US"], ["EN-US", "SCALE-100"])
    both = _msix(["EN-US", "FR-CA"], ["EN-US", "FR-CA"])
    cases = [
        ("English, French off", english, False, 0),
        ("English, French on", english, True, 2),
        ("French in both, French off", both, False, 2),
        ("French in both, French on", both, True, 0),
        ("French only in the PRI, French off", _msix(["EN-US"], ["EN-US", "FR-CA"]), False, 1),
        ("French only in the manifest, French off", _msix(["EN-US", "fr-CA"], ["EN-US"]), False, 1),
        ("another French, French off", _msix(["EN-US"], ["EN-US", "fr-FR"]), False, 1),
        ("an .msixupload holding a French package", _bundle(english, both), False, 2),
        ("an .msixupload, all English", _bundle(english, english), False, 0),
        ("an archive with no package", _bundle(), False, 1),
        ("a resources.pri with no language read", _msix(["EN-US"], ["SCALE-100"]), False, 1),
    ]
    failed = 0
    for name, data, want, expected in cases:
        got = problems("p.msix", data, want)
        if len(got) != expected:
            failed += 1
            print(f"self-test: {name}: expected {expected} problem(s), got {got}")
    for word in ("frame", "afr-CA", "FR_CA"):
        if FRENCH.fullmatch(word):
            failed += 1
            print(f"self-test: {word!r} is not a French language tag")
    # And on disk, as the release runs it.
    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "DistrictAI_1.0.0.0_x64.msix"
        path.write_bytes(both)
        if main(["--french", "false", str(path)]) != 1 or main(["--french", "true", str(path)]) != 0:
            failed += 1
            print("self-test: main should fail a French package with French off and pass it with French on")
    if failed:
        return 1
    print(f"self-test passed: {len(cases) + 4} cases")
    return 0


def main(argv: list[str]) -> int:
    if argv == ["--self-test"]:
        return self_test()
    if len(argv) < 3 or argv[0] != "--french" or argv[1] not in ("true", "false"):
        print(__doc__)
        return 2
    want = argv[1] == "true"
    found: list[str] = []
    for path in argv[2:]:
        found.extend(problems(path, Path(path).read_bytes(), want))
    for line in found:
        print(f"check-languages: {line}")
    if found:
        return 1
    print(f"{len(argv) - 2} package(s): French {'declared' if want else 'absent'}, as DistrictFrench={argv[1]} says")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
