#!/usr/bin/env python3
"""Say whether a district_ffi.dll was built with the `scripted` feature.

    python3 scripts/check-scripted.py --absent  PATH...   # fail if any is scripted
    python3 scripts/check-scripted.py --present PATH...   # fail if any is not
    python3 scripts/check-scripted.py --self-test         # prove the check works

A scripted build (crates/district-ffi/src/scripted.rs) runs the core against
made-up data when its command line asks for a scene, and must never ship. Its
DLL carries MARKER below, the constant of the same name in scripted.rs; a
build without the feature has none of that file's code, so none of its text.

Each PATH is a district_ffi.dll, or a package that holds one: an .msix or
.appx, or an .msixupload or .msixbundle holding those, read as the zip files
they are, to any depth. A package with no district_ffi.dll in it fails either
way, so a check pointed at the wrong file cannot pass.
"""

from __future__ import annotations

import io
import sys
import tempfile
import zipfile
from pathlib import Path

# The same text as MARKER in crates/district-ffi/src/scripted.rs.
MARKER = b"district-ffi scripted scenes: not for release"
DLL = "district_ffi.dll"
PACKAGES = (".msix", ".appx", ".msixupload", ".msixbundle", ".appxupload", ".appxbundle", ".zip")


def dlls(name: str, data: bytes) -> list[tuple[str, bytes]]:
    """Every district_ffi.dll in `data` (a DLL or a package), with where it was."""
    if name.lower().endswith(PACKAGES):
        found = []
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for entry in archive.namelist():
                inner = archive.read(entry) if entry.lower().endswith(PACKAGES + (DLL,)) else None
                if inner is not None:
                    found += dlls(f"{name}!{entry}", inner)
        return found
    if name.replace("\\", "/").rsplit("/", 1)[-1].rsplit("!", 1)[-1].lower().endswith(DLL):
        return [(name, data)]
    return []


def check(paths: list[str], want_scripted: bool) -> list[str]:
    problems = []
    for path in paths:
        found = dlls(path, Path(path).read_bytes())
        if not found:
            problems.append(f"{path}: no {DLL} in it")
        for where, data in found:
            scripted = MARKER in data
            print(f"{where}: {'scripted' if scripted else 'not scripted'} ({len(data)} bytes)")
            if scripted != want_scripted:
                problems.append(f"{where}: {'built with' if scripted else 'built without'} the scripted feature")
    return problems


def self_test() -> None:
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        plain = b"MZ" + b"\0" * 64 + b"district core" + b"\0" * 64
        scripted = plain + MARKER + b"\0"
        (root / DLL).write_bytes(plain)
        assert check([str(root / DLL)], False) == []
        assert check([str(root / DLL)], True)
        (root / DLL).write_bytes(scripted)
        assert check([str(root / DLL)], True) == []
        assert check([str(root / DLL)], False)

        def package(path: Path, entries: dict[str, bytes]) -> bytes:
            with zipfile.ZipFile(path, "w") as archive:
                for name, data in entries.items():
                    archive.writestr(name, data)
            return path.read_bytes()

        msix = package(root / "a.msix", {"AppxManifest.xml": b"<Package/>", DLL: scripted})
        assert check([str(root / "a.msix")], False)
        package(root / "a.msixupload", {"a_x64.msix": msix})
        assert check([str(root / "a.msixupload")], False)
        assert check([str(root / "a.msixupload")], True) == []
        package(root / "clean.msix", {"AppxManifest.xml": b"<Package/>", DLL: plain})
        assert check([str(root / "clean.msix")], False) == []
        # A package without the DLL never passes.
        package(root / "empty.msix", {"AppxManifest.xml": b"<Package/>"})
        assert check([str(root / "empty.msix")], False)
        assert check([str(root / "empty.msix")], True)
    # The marker is still the one scripted.rs defines.
    source = (Path(__file__).resolve().parent.parent / "crates/district-ffi/src/scripted.rs").read_text()
    assert f'MARKER: &str = "{MARKER.decode()}"' in source, "MARKER differs from scripted.rs"
    print("check-scripted self-test: ok")


def main(argv: list[str]) -> int:
    if argv == ["--self-test"]:
        self_test()
        return 0
    if len(argv) < 2 or argv[0] not in ("--absent", "--present"):
        print(__doc__, file=sys.stderr)
        return 2
    problems = check(argv[1:], argv[0] == "--present")
    for problem in problems:
        print(f"error: {problem}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
