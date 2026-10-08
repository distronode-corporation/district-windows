#!/usr/bin/env bash
# Writes the C# bindings for crates/district-ffi into src/DistrictAI.Core/Generated.
#
#   scripts/generate-bindings.sh            # write them
#   scripts/generate-bindings.sh --check    # fail if the committed ones differ
#
# Needs uniffi-bindgen-cs at exactly UNIFFI_BINDGEN_CS_TAG on PATH (CI installs
# it with .github/actions/install-uniffi-bindgen-cs):
#
#   cargo install --locked uniffi-bindgen-cs \
#     --git https://github.com/NordSecurity/uniffi-bindgen-cs --tag v0.11.0+v0.31.0
#
# The bindings are generated from the library's own metadata ("library mode"),
# so they are the same whichever platform built it. --no-format: the output is
# the generator's own, with no formatter version in the loop, except for one
# rewrite: the generator's own comments use em dashes, which this public
# repository refuses (scripts/check-public-hygiene.py), so each becomes a comma.
set -euo pipefail

UNIFFI_BINDGEN_CS_TAG="v0.11.0+v0.31.0"
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/src/DistrictAI.Core/Generated"

check=false
case "${1:-}" in
  --check) check=true ;;
  "") ;;
  *) echo "usage: $0 [--check]" >&2; exit 2 ;;
esac

have="$(uniffi-bindgen-cs --version)"
want="uniffi-bindgen ${UNIFFI_BINDGEN_CS_TAG#v}"
if [ "$have" != "$want" ]; then
  echo "uniffi-bindgen-cs is '$have', expected '$want'" >&2
  exit 1
fi

cargo build --locked -p district-ffi --manifest-path "$root/Cargo.toml"
case "$(uname -s)" in
  Linux) lib="$root/target/debug/libdistrict_ffi.so" ;;
  Darwin) lib="$root/target/debug/libdistrict_ffi.dylib" ;;
  *) lib="$root/target/debug/district_ffi.dll" ;;
esac

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
uniffi-bindgen-cs --library "$lib" \
  --config "$root/crates/district-ffi/uniffi.toml" \
  --out-dir "$tmp" --no-format
python3 - "$tmp/district_ffi.cs" <<'PY'
import sys
path = sys.argv[1]
text = open(path, encoding="utf-8").read()
for dash in ("\u2014", "\u2013"):
    text = text.replace(" " + dash + " ", ", ").replace(" " + dash, ",").replace(dash, ", ")
open(path, "w", encoding="utf-8").write(text)
PY

if $check; then
  if ! diff -u "$out/district_ffi.cs" "$tmp/district_ffi.cs"; then
    echo "::error::src/DistrictAI.Core/Generated/district_ffi.cs is not what crates/district-ffi generates. Run scripts/generate-bindings.sh and commit the result."
    exit 1
  fi
  echo "The committed bindings match crates/district-ffi."
else
  mkdir -p "$out"
  cp "$tmp/district_ffi.cs" "$out/district_ffi.cs"
  echo "Wrote $out/district_ffi.cs"
fi
