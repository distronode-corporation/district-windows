#!/usr/bin/env bash
#
# Build the OS-neutral assemblies twice from clean and fail unless every output
# assembly and PDB is byte for byte the same.
#
#     scripts/check-deterministic-build.sh
#
# Covers DistrictAI.Core and DistrictAI.Presentation (net10.0), Release, with
# CI=true so ContinuousIntegrationBuild is on, as in CI. Directory.Build.props
# sets Deterministic. The WinUI app (net10.0-windows) builds only on Windows
# and is not covered here.
#
# Each build deletes every bin/ and obj/ under src/ first and runs with
# --no-incremental, so the second build cannot reuse the first one's outputs.
# The NuGet packages are restored once, from the locked lock files. Expected
# differences: none.
set -euo pipefail

cd "$(dirname "$0")/.."
export CI=true
OUT=${RUNNER_TEMP:-/tmp}/deterministic
rm -rf "$OUT"
mkdir -p "$OUT"

projects=(src/DistrictAI.Core/DistrictAI.Core.csproj src/DistrictAI.Presentation/DistrictAI.Presentation.csproj)

for n in 1 2; do
  echo "== build $n"
  rm -rf src/DistrictAI.Core/bin src/DistrictAI.Core/obj \
    src/DistrictAI.Presentation/bin src/DistrictAI.Presentation/obj
  for p in "${projects[@]}"; do
    dotnet build "$p" -c Release --nologo --no-incremental -v quiet
  done
  for name in DistrictAI.Core DistrictAI.Presentation; do
    mkdir -p "$OUT/$n/$name"
    cp "src/$name/bin/Release/net10.0/$name.dll" "src/$name/bin/Release/net10.0/$name.pdb" "$OUT/$n/$name/"
  done
  (cd "$OUT/$n" && sha256sum -- */* | sort -k2) | tee "$OUT/$n.sha256"
done

if diff -u "$OUT/1.sha256" "$OUT/2.sha256"; then
  echo "PASS: $(wc -l < "$OUT/1.sha256") files identical across both builds"
else
  echo "FAIL: the two builds differ (both copies are in $OUT)"
  exit 1
fi
