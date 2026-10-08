#!/usr/bin/env python3
"""The two flavours' manifests are the same app under two identities.

    python3 scripts/check-flavours.py              # check the manifests
    python3 scripts/check-flavours.py --self-test  # prove each rule works

src/DistrictAI/Package.appxmanifest is the Store flavour and
Package.GitHub.appxmanifest the GitHub one. They install side by side as
separate packages, so these must differ: the identity name, the publisher,
and the notification activator's COM class (one class can belong to one
package). The display names may differ. Everything else (capabilities, the
protocol, extensions, versions, logos) must be identical, or the two copies
quietly stop being the same app. Comments are not compared.
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STORE = ROOT / "src/DistrictAI/Package.appxmanifest"
GITHUB = ROOT / "src/DistrictAI/Package.GitHub.appxmanifest"

FOUNDATION = "{http://schemas.microsoft.com/appx/manifest/foundation/windows10}"
UAP = "{http://schemas.microsoft.com/appx/manifest/uap/windows10}"


def facts(text: str) -> dict[str, str]:
    root = ET.fromstring(text)
    identity = root.find(f"{FOUNDATION}Identity")
    found = {
        "name": identity.get("Name", ""),
        "publisher": identity.get("Publisher", ""),
        "clsids": sorted({e.get("ToastActivatorCLSID") or e.get("Id") or "" for e in root.iter()
                          if e.tag.endswith("ToastNotificationActivation") or e.tag.endswith("}Class")}),
    }
    return found


def normalised(text: str) -> str:
    """The manifest with what may differ blanked out, as canonical XML."""
    root = ET.fromstring(text)
    for element in root.iter():
        for key in ("Name", "Publisher", "ToastActivatorCLSID", "Id", "DisplayName"):
            if key in element.attrib and (element.tag.endswith("Identity") or key in ("ToastActivatorCLSID", "DisplayName")
                                           or (key == "Id" and element.tag.endswith("}Class"))):
                element.set(key, "*")
        if element.tag in (f"{FOUNDATION}DisplayName", f"{UAP}DisplayName") and element.text:
            if element.tag == f"{FOUNDATION}DisplayName":
                element.text = "*"
    return ET.canonicalize(ET.tostring(root, encoding="unicode"), strip_text=True)


def problems(store: str, github: str) -> list[str]:
    found = []
    a, b = facts(store), facts(github)
    if a["name"] == b["name"]:
        found.append(f"both flavours have the identity name {a['name']!r}")
    if a["publisher"] == b["publisher"]:
        found.append(f"both flavours have the publisher {a['publisher']!r}")
    if set(a["clsids"]) & set(b["clsids"]):
        found.append(f"the flavours share a COM class: {sorted(set(a['clsids']) & set(b['clsids']))}")
    if len(a["clsids"]) != 1 or len(b["clsids"]) != 1:
        found.append("each flavour's activator and its COM class must be one GUID")
    if normalised(store) != normalised(github):
        found.append("the flavours differ beyond identity, publisher, display names and the activator class")
    return found


def self_test() -> int:
    store = STORE.read_text(encoding="utf-8")
    github = GITHUB.read_text(encoding="utf-8")
    cases = [
        ("the committed pair", store, github, False),
        ("the same identity", store, github.replace("Distronode.DistrictAI.GitHub", "Distronode.DistrictAI.Placeholder"), True),
        ("a capability added to one", store, github.replace("<rescap:Capability Name=\"runFullTrust\" />",
                                                             "<rescap:Capability Name=\"runFullTrust\" /><DeviceCapability Name=\"webcam\" />"), True),
        ("one class shared", store, github.replace(facts(github)["clsids"][0], facts(store)["clsids"][0]), True),
    ]
    failed = 0
    for name, a, b, expect in cases:
        if bool(problems(a, b)) != expect:
            failed += 1
            print(f"self-test: {name}: expected {'a problem' if expect else 'none'}, got {problems(a, b)}")
    if failed:
        return 1
    print(f"self-test passed: {len(cases)} cases")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    found = problems(STORE.read_text(encoding="utf-8"), GITHUB.read_text(encoding="utf-8"))
    for line in found:
        print(f"check-flavours: {line}")
    if not found:
        print("the Store and GitHub manifests are one app under two identities")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
