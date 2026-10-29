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
    $SCRIPTS/Data/ScenarioGenerator.cs \
    $SCRIPTS/Data/Examination.cs \
    $SCRIPTS/Data/DiagnosisEvaluation.cs \
    $SCRIPTS/Data/Coach.cs \
    $SCRIPTS/Engines/MockPhysiologyEngine.cs \
    $SCRIPTS/Crash/Biomechanics.cs \
    $SCRIPTS/Anatomy/AnatomyFile.cs \
    $SCRIPTS/Anatomy/AnatomyInjuryMap.cs \
    $SCRIPTS/Anatomy/TissueDatabase.cs \
    $SCRIPTS/Anatomy/SkinMarks.cs \
    $SCRIPTS/Anatomy/SkinPerfusion.cs \
    $SCRIPTS/Anatomy/BloodFlow.cs \
    $SCRIPTS/Anatomy/CrashMarks.cs \
    $SCRIPTS/Crash/InjuryRisk.cs \
    $SCRIPTS/Crash/OrganMechanics.cs \
    $SCRIPTS/Crash/MuscleTone.cs \
    $SCRIPTS/Crash/ImpactModel.cs \
    Tests/NotfallSimTests.cs

mono "$OUT/tests.exe" Assets/StreamingAssets/NotfallSIM
