#!/usr/bin/env bash
# Prüft, ob web/engine.js dieselben Verläufe rechnet wie die C#-Mock-Engine.
set -euo pipefail
cd "$(dirname "$0")/../.."
OUT=$(mktemp -d); trap 'rm -rf "$OUT"' EXIT
S=Assets/NotfallSIM/Scripts
mcs -nologo -r:System.Runtime.Serialization.dll -out:"$OUT/parity.exe" $S/Core/*.cs $S/Data/DataModels.cs $S/Data/ScenarioGenerator.cs $S/Engines/MockPhysiologyEngine.cs Tests/parity/Parity.cs
mono "$OUT/parity.exe" > "$OUT/csharp.txt"
D=Assets/StreamingAssets/NotfallSIM
mono "$OUT/parity.exe" gen "$D" > "$OUT/generator.txt"
node Tests/parity/parity.js "$OUT/csharp.txt" "$OUT/generator.txt" "$D"
