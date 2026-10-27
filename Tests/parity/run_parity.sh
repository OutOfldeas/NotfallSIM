#!/usr/bin/env bash
# Prüft, ob web/engine.js dieselben Verläufe rechnet wie die C#-Mock-Engine.
set -euo pipefail
cd "$(dirname "$0")/../.."
OUT=$(mktemp -d); trap 'rm -rf "$OUT"' EXIT
S=Assets/NotfallSIM/Scripts
mcs -nologo -out:"$OUT/parity.exe" $S/Core/*.cs $S/Data/DataModels.cs $S/Engines/MockPhysiologyEngine.cs Tests/parity/Parity.cs
mono "$OUT/parity.exe" > "$OUT/csharp.txt"
node Tests/parity/parity.js "$OUT/csharp.txt"
