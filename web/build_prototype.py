#!/usr/bin/env python3
"""Baut den Browser-Prototyp: bettet web/engine.js und die JSON-Daten aus
Assets/StreamingAssets/NotfallSIM in web/prototype.template.html ein.
Ergebnis: web/dist/notfallsim.html (eine Datei, ohne Server lauffähig)."""
import datetime
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA_DIR = ROOT / "Assets" / "StreamingAssets" / "NotfallSIM"
WEB = ROOT / "web"


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


data = {
    "injuries": load(DATA_DIR / "injuries.json"),
    "interventions": load(DATA_DIR / "interventions.json"),
    "scenarios": [load(p) for p in sorted((DATA_DIR / "scenarios").glob("*.json"))],
    "built": datetime.date.today().strftime("%d.%m.%Y"),
}
# Reihenfolge: zuerst die Grundszenarien, dann Varianten
order = ["motorrad_beckenfraktur", "pkw_spannungspneu", "escooter_sht"]
data["scenarios"].sort(key=lambda s: (order.index(s["id"]) if s["id"] in order else len(order), s["id"]))

template = (WEB / "prototype.template.html").read_text(encoding="utf-8")
engine = (WEB / "engine.js").read_text(encoding="utf-8")
payload = json.dumps(data, ensure_ascii=False).replace("</", "<\\/")
html = template.replace("/*ENGINE*/", engine).replace("/*DATA*/null", payload)

out = WEB / "dist" / "notfallsim.html"
out.parent.mkdir(exist_ok=True)
out.write_text(html, encoding="utf-8")
print(f"{out.relative_to(ROOT)}: {len(html) // 1024} KB, {len(data['scenarios'])} Szenarien")
