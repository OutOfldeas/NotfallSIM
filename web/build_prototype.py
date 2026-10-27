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
    "imaging": load(DATA_DIR / "imaging.json"),
    "scenarios": [load(p) for p in sorted((DATA_DIR / "scenarios").glob("*.json"))],
    "built": datetime.date.today().strftime("%d.%m.%Y"),
}
# Reihenfolge: zuerst die Grundszenarien, dann Varianten
order = ["motorrad_beckenfraktur", "pkw_spannungspneu", "escooter_sht"]
data["scenarios"].sort(key=lambda s: (order.index(s["id"]) if s["id"] in order else len(order), s["id"]))

# Bilder aus web/images als data:-URI einbetten (Artifacts dürfen keine externen Bilder laden).
import base64
import mimetypes
missing = []
for inj in data["injuries"]["injuries"]:
    for f in inj.get("imaging", []):
        if not f.get("image"):
            continue
        path = WEB / "images" / f["image"]
        if path.exists():
            mime = mimetypes.guess_type(path.name)[0] or "image/jpeg"
            f["image"] = f"data:{mime};base64," + base64.b64encode(path.read_bytes()).decode()
        else:
            missing.append(f["image"])
            f["image"] = ""
if missing:
    print("Fehlende Bilder (übersprungen):", ", ".join(missing))

template = (WEB / "prototype.template.html").read_text(encoding="utf-8")
engine = (WEB / "engine.js").read_text(encoding="utf-8")
payload = json.dumps(data, ensure_ascii=False).replace("</", "<\\/")
html = template.replace("/*ENGINE*/", engine).replace("/*DATA*/null", payload)

out = WEB / "dist" / "notfallsim.html"
out.parent.mkdir(exist_ok=True)
out.write_text(html, encoding="utf-8")
print(f"{out.relative_to(ROOT)}: {len(html) // 1024} KB, {len(data['scenarios'])} Szenarien")
