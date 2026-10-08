#!/usr/bin/env bash
# Writes the C# bindings for each crate that has them: crates/district-ffi into
# src/DistrictAI.Core/Generated, and the spike crate into tests/DistrictAI.Spikes.
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

# Each crate with C# bindings: the crate, and where its bindings live.
targets=(
  "district-ffi:src/DistrictAI.Core/Generated"
  "district-spikes:tests/DistrictAI.Spikes/Generated"
)

case "$(uname -s)" in
  Linux) prefix="lib" suffix=".so" ;;
  Darwin) prefix="lib" suffix=".dylib" ;;
  *) prefix="" suffix=".dll" ;;
esac

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
status=0
for target in "${targets[@]}"; do
  crate="${target%%:*}"
  out="$root/${target#*:}"
  lib_name="${crate//-/_}"
  cargo build --locked -p "$crate" --manifest-path "$root/Cargo.toml"
  mkdir -p "$tmp/$crate"
  uniffi-bindgen-cs --library "$root/target/debug/${prefix}${lib_name}${suffix}" \
    --config "$root/crates/$crate/uniffi.toml" \
    --out-dir "$tmp/$crate" --no-format
  file="$tmp/$crate/$lib_name.cs"
  python3 - "$file" <<'PY'
import sys
path = sys.argv[1]
text = open(path, encoding="utf-8").read()
for dash in ("\u2014", "\u2013"):
    text = text.replace(" " + dash + " ", ", ").replace(" " + dash, ",").replace(dash, ", ")
open(path, "w", encoding="utf-8").write(text)
PY
  if $check; then
    if ! diff -u "$out/$lib_name.cs" "$file"; then
      echo "::error::${target#*:}/$lib_name.cs is not what crates/$crate generates. Run scripts/generate-bindings.sh and commit the result."
      status=1
    else
      echo "The committed bindings match crates/$crate."
    fi
  else
    mkdir -p "$out"
    cp "$file" "$out/$lib_name.cs"
    echo "Wrote $out/$lib_name.cs"
  fi
done
exit "$status"
