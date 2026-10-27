#!/usr/bin/env bash
# Kompiliert die Unity-unabhängigen Teile (Core, Mock-Engine, Datenmodelle) mit den Tests
# und führt sie aus. Braucht mono (Paket mono-mcs bzw. mono-complete).
set -euo pipefail
cd "$(dirname "$0")/.."

SCRIPTS=Assets/NotfallSIM/Scripts
OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

mcs -nologo -warnaserror- \
    -r:System.Runtime.Serialization.dll \
    -out:"$OUT/tests.exe" \
    $SCRIPTS/Core/*.cs \
    $SCRIPTS/Data/DataModels.cs \
    $SCRIPTS/Engines/MockPhysiologyEngine.cs \
    Tests/NotfallSimTests.cs

mono "$OUT/tests.exe" Assets/StreamingAssets/NotfallSIM
