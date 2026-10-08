#!/usr/bin/env python3
"""The two flavours' manifests are the same app under two identities.

    python3 scripts/check-flavours.py              # check the manifests
    python3 scripts/check-flavours.py --self-test  # prove each rule works

src/DistrictAI/Package.appxmanifest is the Store flavour and
Package.GitHub.appxmanifest the GitHub one. They are separate packages, so
these must differ: the identity name, the publisher, and the notification
activator's COM class (one class can belong to one package; within each
manifest the activator and the COM server name the same single class). The
package's display name (Properties/DisplayName and the tile's
VisualElements/@DisplayName) may differ. Everything else (the version,
capabilities, the protocol, the startup task, logos, descriptions) must be
identical, or the two copies quietly stop being the same app. Comments are not
compared.

The ids that Windows scopes to a package (Application/@Id, the startup task's
TaskId) may be equal in both; the protocol is the same scheme on purpose.
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
DESKTOP = "{http://schemas.microsoft.com/appx/manifest/desktop/windows10}"
COM = "{http://schemas.microsoft.com/appx/manifest/com/windows10}"

ACTIVATOR = f"{DESKTOP}ToastNotificationActivation"
COM_CLASS = f"{COM}Class"


def facts(text: str) -> dict[str, object]:
    root = ET.fromstring(text)
    identity = root.find(f"{FOUNDATION}Identity")
    if identity is None:
        raise ValueError("no Identity element")
    return {
        "name": identity.get("Name", ""),
        "publisher": identity.get("Publisher", ""),
        "version": identity.get("Version", ""),
        "activators": sorted(e.get("ToastActivatorCLSID", "").lower() for e in root.iter(ACTIVATOR)),
        "classes": sorted(e.get("Id", "").lower() for e in root.iter(COM_CLASS)),
    }


def normalised(text: str) -> str:
    """The manifest with what may differ blanked out, as canonical XML."""
    root = ET.fromstring(text)
    identity = root.find(f"{FOUNDATION}Identity")
    if identity is not None:
        for key in ("Name", "Publisher"):
            if key in identity.attrib:
                identity.set(key, "*")
    display = root.find(f"{FOUNDATION}Properties/{FOUNDATION}DisplayName")
    if display is not None:
        display.text = "*"
    for visual in root.iter(f"{UAP}VisualElements"):
        if "DisplayName" in visual.attrib:
            visual.set("DisplayName", "*")
    for element in root.iter(ACTIVATOR):
        element.set("ToastActivatorCLSID", "*")
    for element in root.iter(COM_CLASS):
        element.set("Id", "*")
    return ET.canonicalize(ET.tostring(root, encoding="unicode"), strip_text=True)


def problems(store: str, github: str) -> list[str]:
    found = []
    a, b = facts(store), facts(github)
    if a["name"] == b["name"]:
        found.append(f"both flavours have the identity name {a['name']!r}")
    if a["publisher"] == b["publisher"]:
        found.append(f"both flavours have the publisher {a['publisher']!r}")
    if a["version"] != b["version"]:
        found.append(f"the versions differ: Store {a['version']!r}, GitHub {b['version']!r}")
    for label, f in (("Store", a), ("GitHub", b)):
        guids = set(f["activators"]) | set(f["classes"])
        if len(f["activators"]) != 1 or len(f["classes"]) != 1 or len(guids) != 1:
            found.append(f"the {label} flavour's activator and its COM class must be one GUID, found "
                         f"activators {f['activators']} and classes {f['classes']}")
    shared = (set(a["activators"]) | set(a["classes"])) & (set(b["activators"]) | set(b["classes"]))
    if shared:
        found.append(f"the flavours share a COM class: {sorted(shared)}")
    if normalised(store) != normalised(github):
        found.append("the flavours differ beyond identity, publisher, display name and the activator class")
    return found


def self_test() -> int:
    store = STORE.read_text(encoding="utf-8")
    github = GITHUB.read_text(encoding="utf-8")
    s, g = facts(store), facts(github)
    store_class, github_class = s["classes"][0], g["classes"][0]
    cases = [
        ("the committed pair", github, False),
        ("the same identity name", github.replace(f'Name="{g["name"]}"', f'Name="{s["name"]}"'), True),
        ("the same publisher", github.replace(f'Publisher="{g["publisher"]}"', f'Publisher="{s["publisher"]}"'), True),
        ("another version", github.replace(f'Version="{g["version"]}"', 'Version="9.9.9.0"'), True),
        ("one class shared", github.replace(github_class, store_class), True),
        ("the activator and the class disagree",
         github.replace(f'ToastActivatorCLSID="{github_class}"', 'ToastActivatorCLSID="00000000-0000-0000-0000-000000000001"'), True),
        ("a capability added to one", github.replace('<rescap:Capability Name="runFullTrust" />',
                                                     '<rescap:Capability Name="runFullTrust" /><DeviceCapability Name="webcam" />'), True),
        ("the protocol renamed", github.replace('<uap:Protocol Name="districtai">', '<uap:Protocol Name="districtai-github">'), True),
        ("the startup task's display name changed",
         github.replace('TaskId="DistrictAIStartup" Enabled="false" DisplayName="District AI"',
                        'TaskId="DistrictAIStartup" Enabled="false" DisplayName="Other"'), True),
        ("the description changed", github.replace('Description="District AI is', 'Description="District AI was'), True),
        ("only the display name changed", github.replace("<DisplayName>District AI (GitHub)</DisplayName>",
                                                         "<DisplayName>District AI (other)</DisplayName>"), False),
    ]
    failed = 0
    for name, variant, expect in cases:
        if name != "the committed pair" and variant == github:
            failed += 1
            print(f"self-test: {name}: the mutation did not apply to the GitHub manifest")
            continue
        found = problems(store, variant)
        if bool(found) != expect:
            failed += 1
            print(f"self-test: {name}: expected {'a problem' if expect else 'none'}, got {found}")
    if failed:
        return 1
    print(f"self-test passed: {len(cases)} cases")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    if sys.argv[1:]:
        print(__doc__)
        return 2
    found = problems(STORE.read_text(encoding="utf-8"), GITHUB.read_text(encoding="utf-8"))
    for line in found:
        print(f"check-flavours: {line}")
    if not found:
        print("the Store and GitHub manifests are one app under two identities")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
