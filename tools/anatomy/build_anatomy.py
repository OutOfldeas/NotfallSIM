#!/usr/bin/env python3
"""Baut die anatomischen Modelle für NotfallSIM aus BodyParts3D.

Ergebnis: Assets/StreamingAssets/NotfallSIM/anatomy/body.bin (eigenes Binärformat, siehe AnatomyFile.cs)
und Vorschaubilder in tools/anatomy/preview/.

Schritte:
 1. STL-Dateien laden (Haut, Knochen, Organe, Gefäße, Gehirn) — Auswahl in parts.py
 2. Haut säubern: Voxel-Füllung + Marching Cubes → geschlossene Oberfläche, vereinfacht
 3. Gelenke aus den Knochen bestimmen (Femurkopf, Knie, Sprunggelenk, Humeruskopf, Ellbogen, Wirbelsäule)
 4. Segmente wie im Ragdoll (Becken, Abdomen, Thorax, Kopf, Arme, Beine) mit Kollisionsformen
 5. Haut-Gewichte je Segment (für die Ragdoll-Haut) und Atmungs-Blendshapes
 6. Nerven und Extremitätengefäße, die BodyParts3D nicht enthält, als Röhren entlang der Knochen (schematisch)
 7. Alles in Unity-Koordinaten: Meter, y oben, z vorn (anterior), x = rechte Körperseite; Ursprung Hüftgelenkmitte

Aufruf:  python3 tools/anatomy/build_anatomy.py --bp3d /pfad/zu/BodyParts3D/assets/BodyParts3D_data
(BodyParts3D: https://github.com/Kevin-Mattheus-Moerman/BodyParts3D — Lizenz CC BY-SA 2.1 Japan)
Abhängigkeiten: numpy, scipy, trimesh, scikit-image, fast-simplification
"""
import argparse, csv, json, os, re, struct, sys, time
import numpy as np
import trimesh
import fast_simplification
from scipy import ndimage
from skimage import measure

sys.path.insert(0, os.path.dirname(__file__))
from parts import select, SKIN  # noqa: E402
from labels import german       # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'StreamingAssets', 'NotfallSIM', 'anatomy')
PREVIEW = os.path.join(os.path.dirname(__file__), 'preview')

LAYER_COLORS = {  # RGBA 0..1
    'skin': (0.86, 0.68, 0.56, 1), 'bone': (0.93, 0.90, 0.80, 1), 'organ': (0.75, 0.35, 0.33, 1), 'brain': (0.88, 0.72, 0.72, 1),
    'artery': (0.82, 0.12, 0.12, 1), 'vein': (0.18, 0.28, 0.75, 1), 'nerve': (0.98, 0.85, 0.25, 1),
}
ORGAN_COLORS = [(r'heart', (0.70, 0.12, 0.14, 1)), (r'lung', (0.93, 0.62, 0.64, 1)), (r'liver', (0.48, 0.20, 0.14, 1)),
                (r'spleen', (0.45, 0.16, 0.30, 1)), (r'kidney', (0.55, 0.22, 0.18, 1)), (r'stomach|duoden|jejun|ileum|colon|rectum', (0.92, 0.70, 0.60, 1)),
                (r'gallbladder', (0.35, 0.55, 0.25, 1)), (r'pancreas', (0.93, 0.80, 0.55, 1)), (r'bladder|ureter|prostate', (0.90, 0.80, 0.50, 1)),
                (r'trachea|bronch|esophag|thyroid', (0.85, 0.75, 0.70, 1)), (r'eyeball', (0.95, 0.95, 0.95, 1)), (r'adrenal|thymus', (0.90, 0.75, 0.40, 1))]


def to_unity(v):
    """BodyParts3D (mm; x = links, y = hinten, z = oben) → Unity (m; x = rechts, y = oben, z = vorn)."""
    v = np.asarray(v, dtype=np.float64)
    return np.c_[-v[:, 0], v[:, 2], -v[:, 1]] / 1000.0


# ------------------------------------------------------------------ Laden

def load_names(bp):
    names = {}
    for line in open(os.path.join(bp, 'parts_list_e.txt'), encoding='utf-8'):
        p = line.rstrip('\n').split('\t')
        if len(p) >= 2:
            names[p[0].strip('"')] = p[1]
    for row in csv.reader(open(os.path.join(bp, 'FMA.csv'), encoding='utf-8')):
        if row and row[0].isdigit():
            names.setdefault('FMA' + row[0], row[1])
    return names


def load(bp, pid):
    return trimesh.load(os.path.join(bp, 'stl', pid + '.stl'), process=True)


# ------------------------------------------------------------------ Haut

def clean_skin(mesh, pitch=3.0, faces=60000):
    v = mesh.vertices
    lo = v.min(0) - 12
    mid = (v[mesh.edges_unique[:, 0]] + v[mesh.edges_unique[:, 1]]) / 2
    idx = np.vstack([np.floor((v - lo) / pitch), np.floor((mid - lo) / pitch)]).astype(int)
    shape = idx.max(0) + 8
    g = np.zeros(shape, bool)
    g[idx[:, 0], idx[:, 1], idx[:, 2]] = True
    g = ndimage.binary_dilation(g, iterations=3)
    f = ndimage.binary_fill_holes(g)
    for k in range(g.shape[2]): f[:, :, k] |= ndimage.binary_fill_holes(g[:, :, k])
    for k in range(g.shape[0]): f[k, :, :] |= ndimage.binary_fill_holes(g[k, :, :])
    for k in range(g.shape[1]): f[:, k, :] |= ndimage.binary_fill_holes(g[:, k, :])
    f = ndimage.binary_closing(f, structure=np.ones((1, 1, 3), bool), iterations=3)
    f = ndimage.binary_fill_holes(f)
    f = ndimage.binary_erosion(f, iterations=3)
    f = ndimage.binary_opening(f, iterations=1)
    s = ndimage.gaussian_filter(f.astype(np.float32), 1.0)
    verts, tri, _, _ = measure.marching_cubes(s, 0.5)
    m = trimesh.Trimesh(verts * pitch + lo, tri)
    m.invert()
    m = max(m.split(only_watertight=False), key=lambda x: len(x.faces))
    p, fc = fast_simplification.simplify(m.vertices, m.faces, target_reduction=max(0.0, 1 - faces / len(m.faces)))
    m = trimesh.Trimesh(p, fc)
    trimesh.smoothing.filter_taubin(m, iterations=12)
    return m


def simplify(mesh, target):
    if len(mesh.faces) > target:
        p, fc = fast_simplification.simplify(mesh.vertices, mesh.faces, target_reduction=1 - target / len(mesh.faces))
        mesh = trimesh.Trimesh(p, fc)
    mesh.remove_unreferenced_vertices()
    return mesh  # keine Glättung: Taubin erzeugt nach der Vereinfachung Ausreißer


# ------------------------------------------------------------------ Gelenke

def joints_from_bones(bones):
    """bones: Name → Trimesh (BodyParts3D-Koordinaten). Rückgabe: Gelenkname → Punkt (mm)."""
    J = {}
    def top(m, h, medial_mm=None, side=0):
        v = m.vertices[m.vertices[:, 2] > m.vertices[:, 2].max() - h]
        if medial_mm is not None:
            ax = np.abs(v[:, 0]); v = v[ax < ax.min() + medial_mm]
        return v.mean(0)
    def bottom(m, h):
        v = m.vertices; return v[v[:, 2] < v[:, 2].min() + h].mean(0)
    for side, s in (('rechts', 'right'), ('links', 'left')):
        femur, tibia, hum, rad, ulna, talus = (bones[f'{s} {n}'] for n in ('femur', 'tibia', 'humerus', 'radius', 'ulna', 'talus'))
        J[f'Hüfte {side}'] = top(femur, 50, medial_mm=35)
        J[f'Knie {side}'] = (bottom(femur, 25) + top(tibia, 25)) / 2
        J[f'Sprunggelenk {side}'] = talus.vertices.mean(0)
        J[f'Schulter {side}'] = top(hum, 35, medial_mm=30)
        J[f'Ellbogen {side}'] = (bottom(hum, 20) + top(ulna, 20)) / 2
        J[f'Handgelenk {side}'] = (bottom(rad, 15) + bottom(ulna, 15)) / 2
        tips = bones[f'distal phalanx of {s} middle finger'].vertices
        J[f'Fingerspitze {side}'] = tips[tips[:, 2].argmin()]
        heel = bones[f'{s} calcaneus'].vertices
        toe = bones[f'distal phalanx of {s} big toe'].vertices
        J[f'Ferse {side}'] = heel[heel[:, 2].argmin()]
        J[f'Zehe {side}'] = toe[toe[:, 1].argmin()]
    c = lambda n: bones[n].vertices.mean(0)
    J['C7/T1'] = (c('seventh cervical vertebra') + c('first thoracic vertebra')) / 2
    J['T12/L1'] = (c('twelfth thoracic vertebra') + c('first lumbar vertebra')) / 2
    J['L4/L5'] = (c('fourth lumbar vertebra') + c('fifth lumbar vertebra')) / 2
    skull = np.vstack([bones[n].vertices for n in ('frontal bone', 'left parietal bone', 'right parietal bone', 'occipital bone')])
    J['Scheitel'] = skull[skull[:, 2].argmax()]
    J['Hüftmitte'] = (J['Hüfte rechts'] + J['Hüfte links']) / 2
    return J


SEGMENTS = [  # Name, Region (Untersuchung/Sensor), Eltern, proximales Gelenk, distales Gelenk, Massenanteil Mann/Frau (de Leva 1996)
    ('Becken', 'Becken', None, 'Hüftmitte', 'L4/L5', 0.1117, 0.1247),
    ('Abdomen', 'Abdomen', 'Becken', 'L4/L5', 'T12/L1', 0.1633, 0.1465),
    ('Thorax', 'Thorax', 'Abdomen', 'T12/L1', 'C7/T1', 0.1596, 0.1545),
    ('Kopf', 'Kopf', 'Thorax', 'C7/T1', 'Scheitel', 0.0694, 0.0668),
    ('Oberarm rechts', 'Arm rechts', 'Thorax', 'Schulter rechts', 'Ellbogen rechts', 0.0271, 0.0255),
    ('Unterarm rechts', 'Arm rechts', 'Oberarm rechts', 'Ellbogen rechts', 'Fingerspitze rechts', 0.0223, 0.0194),
    ('Oberarm links', 'Arm links', 'Thorax', 'Schulter links', 'Ellbogen links', 0.0271, 0.0255),
    ('Unterarm links', 'Arm links', 'Oberarm links', 'Ellbogen links', 'Fingerspitze links', 0.0223, 0.0194),
    ('Oberschenkel rechts', 'Oberschenkel rechts', 'Becken', 'Hüfte rechts', 'Knie rechts', 0.1416, 0.1478),
    ('Unterschenkel rechts', 'Unterschenkel rechts', 'Oberschenkel rechts', 'Knie rechts', 'Sprunggelenk rechts', 0.0570, 0.0610),
    ('Oberschenkel links', 'Oberschenkel links', 'Becken', 'Hüfte links', 'Knie links', 0.1416, 0.1478),
    ('Unterschenkel links', 'Unterschenkel links', 'Oberschenkel links', 'Knie links', 'Sprunggelenk links', 0.0570, 0.0610),
]
LIMB = lambda name: name.startswith(('Ober', 'Unter'))


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def segment_scores(p, J, radii):
    """Zugehörigkeit von Punkten p (N×3, Unity) zu den Segmenten; Rückgabe N×S."""
    S = np.zeros((len(p), len(SEGMENTS)))
    for k, (name, _, parent, a, b, *_m) in enumerate(SEGMENTS):
        A, B = J[a], J[b]
        if name.startswith('Unterschenkel'):  # Fuß gehört zum Unterschenkel
            B = B + np.array([0, -0.06, 0.03])
        ab = B - A; L2 = ab @ ab
        t = ((p - A) @ ab) / L2
        closest = A + np.clip(t, 0, 1)[:, None] * ab
        d = np.linalg.norm(p - closest, axis=1)
        sigma = radii[name] * (1.3 if LIMB(name) else 1.25)
        s = np.exp(-(d / sigma) ** 2)
        if parent is not None:
            s *= smoothstep(-0.20, 0.0, t) if LIMB(name) else smoothstep(-0.10, 0.05, t)  # proximal des Gelenks: Elternsegment
        if name in ('Oberarm rechts', 'Oberarm links', 'Oberschenkel rechts', 'Oberschenkel links', 'Abdomen'):
            s *= 1 - smoothstep(0.95, 1.08, t)        # distal davon das Kindsegment
        if name == 'Thorax':
            s *= 1 - smoothstep(1.0, 1.12, t)
        if name == 'Becken':
            s *= 1 - smoothstep(0.95, 1.1, t)
        S[:, k] = s
    return S


def skin_weights(p, J, radii):
    S = segment_scores(p, J, radii)
    order = np.argsort(-S, axis=1)[:, :2]
    w = np.take_along_axis(S, order, axis=1)
    w = np.maximum(w, 1e-9)
    w = w ** 1.0  # weiche Übergänge an den Gelenken
    w /= w.sum(1, keepdims=True)
    # Fallback: kaum Zugehörigkeit → nächstes Segment allein
    weak = S.max(1) < 1e-4
    if weak.any():
        w[weak] = [1, 0]
    return order.astype(np.uint8), w


# ------------------------------------------------------------------ Röhren (Nerven, Gefäße der Extremitäten)

def catmull(points, n=6):
    P = np.asarray(points); out = []
    for i in range(len(P) - 1):
        p0, p1, p2, p3 = P[max(i - 1, 0)], P[i], P[i + 1], P[min(i + 2, len(P) - 1)]
        for t in np.linspace(0, 1, n, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(P[-1])
    return np.array(out)


def tube(points, r0, r1=None, sides=8):
    path = catmull(points)
    r1 = r0 if r1 is None else r1
    verts, faces = [], []
    n = len(path)
    for i, c in enumerate(path):
        tdir = path[min(i + 1, n - 1)] - path[max(i - 1, 0)]
        tdir /= np.linalg.norm(tdir) + 1e-12
        ref = np.array([0, 0, 1.0]) if abs(tdir[2]) < 0.9 else np.array([1.0, 0, 0])
        u = np.cross(tdir, ref); u /= np.linalg.norm(u); w = np.cross(tdir, u)
        r = r0 + (r1 - r0) * i / (n - 1)
        for k in range(sides):
            a = 2 * np.pi * k / sides
            verts.append(c + r * (np.cos(a) * u + np.sin(a) * w))
    for i in range(n - 1):
        for k in range(sides):
            a, b = i * sides + k, i * sides + (k + 1) % sides
            c_, d = a + sides, b + sides
            faces += [[a, c_, b], [b, c_, d]]
    return trimesh.Trimesh(np.array(verts), np.array(faces), process=False)


def schematic_paths(J, bones, U):
    """Wichtige Leitungsbahnen, die in BodyParts3D fehlen — schematisch entlang der Knochen (Unity-Koordinaten).
    Rückgabe: Liste (Name, Ebene, Punkte, Radius Anfang, Radius Ende)."""
    out = []
    V = lambda n: U(bones[n].vertices)
    cen = lambda n: V(n).mean(0)
    vert_names = [f'{o} cervical vertebra' for o in ('third', 'fourth', 'fifth', 'sixth', 'seventh')] + \
                 [f'{o} thoracic vertebra' for o in ('first', 'second', 'third', 'fourth', 'fifth', 'sixth', 'seventh', 'eighth', 'ninth', 'tenth', 'eleventh', 'twelfth')] + \
                 ['first lumbar vertebra', 'second lumbar vertebra']
    spine = [cen('atlas') + [0, 0.03, -0.005], cen('atlas'), cen('axis')] + [cen(n) for n in vert_names]
    # Wirbelkanal: Wirbelschwerpunkte liegen etwa auf Höhe der Bogenwurzeln; leicht nach dorsal
    out.append(('Rückenmark', 'nerve', [p + np.array([0, 0, -0.012]) for p in spine], 0.0055, 0.004))
    for side, s, sx in (('rechts', 'right', 1), ('links', 'left', -1)):
        hip, knee, ankle = J[f'Hüfte {side}'], J[f'Knie {side}'], J[f'Sprunggelenk {side}']
        sh, el, wr = J[f'Schulter {side}'], J[f'Ellbogen {side}'], J[f'Handgelenk {side}']
        ext_il = V(f'{s} external iliac artery'); ext_end = ext_il[ext_il[:, 1].argmin()]
        sub = V(f'{s} subclavian artery'); sub_end = sub[(sub[:, 0] * sx).argmax()]
        thigh = lambda f, lat, ant: hip + (knee - hip) * f + np.array([sx * lat, 0, ant])
        shank = lambda f, lat, ant: knee + (ankle - knee) * f + np.array([sx * lat, 0, ant])
        arm = lambda f, lat, ant: sh + (el - sh) * f + np.array([sx * lat, 0, ant])
        fore = lambda f, lat, ant: el + (wr - el) * f + np.array([sx * lat, 0, ant])
        # Arterien: A. femoralis → poplitea → tibialis posterior; A. axillaris → brachialis → radialis/ulnaris
        out.append((f'A. femoralis {side}', 'artery', [ext_end, thigh(0.08, -0.03, 0.045), thigh(0.4, -0.04, 0.03), thigh(0.75, -0.035, -0.01), thigh(0.97, 0, -0.035)], 0.0045, 0.0038))
        out.append((f'A. poplitea / tibialis posterior {side}', 'artery', [thigh(0.97, 0, -0.035), shank(0.1, 0, -0.035), shank(0.5, -0.015, -0.02), shank(0.95, -0.03, -0.01), J[f'Ferse {side}'] + [sx * -0.02, 0.03, 0.03]], 0.0035, 0.0022))
        out.append((f'V. femoralis {side}', 'vein', [ext_end + [sx * -0.012, 0, -0.005], thigh(0.08, -0.042, 0.04), thigh(0.4, -0.05, 0.025), thigh(0.75, -0.045, -0.015), thigh(0.97, 0.008, -0.04)], 0.005, 0.0042))
        out.append((f'A. axillaris / brachialis {side}', 'artery', [sub_end, arm(0.05, -0.02, 0.0), arm(0.5, -0.02, 0.0), arm(0.95, -0.005, 0.015)], 0.0035, 0.003))
        out.append((f'A. radialis {side}', 'artery', [arm(0.95, -0.005, 0.015), fore(0.3, 0.015, 0.012), fore(0.98, 0.022, 0.01)], 0.0022, 0.0018))
        out.append((f'A. ulnaris {side}', 'artery', [arm(0.95, -0.005, 0.015), fore(0.3, -0.015, 0.008), fore(0.98, -0.02, 0.008)], 0.0022, 0.0018))
        # Nerven: N. ischiadicus → tibialis; N. femoralis; Plexus brachialis → N. medianus, ulnaris, radialis
        sac = cen('sacrum')
        out.append((f'N. ischiadicus {side}', 'nerve', [sac + [sx * 0.05, 0.0, -0.02], hip + [sx * -0.01, -0.03, -0.07], thigh(0.3, 0, -0.05), thigh(0.7, 0, -0.045), thigh(0.95, 0, -0.04), shank(0.4, 0, -0.04), shank(0.95, -0.025, -0.015)], 0.0055, 0.003))
        out.append((f'N. femoralis {side}', 'nerve', [cen('fourth lumbar vertebra') + [sx * 0.04, 0, 0.02], ext_end + [sx * 0.015, 0.0, 0.0], thigh(0.15, 0.0, 0.05)], 0.003, 0.0025))
        c6 = cen('sixth cervical vertebra')
        out.append((f'Plexus brachialis / N. medianus {side}', 'nerve', [c6 + [sx * 0.03, 0, 0.0], sub_end + [0, 0.01, -0.01], arm(0.1, -0.025, 0.0), arm(0.5, -0.025, 0.005), arm(0.95, -0.01, 0.02), fore(0.5, 0, 0.012), fore(1.0, 0, 0.012), wr + [0, -0.06, 0.01]], 0.0035, 0.002))
        out.append((f'N. ulnaris {side}', 'nerve', [arm(0.1, -0.025, 0.0), arm(0.6, -0.035, -0.012), el + [sx * -0.035, 0, -0.015], fore(0.5, -0.02, 0.0), fore(1.0, -0.022, 0.005)], 0.0025, 0.0018))
        out.append((f'N. radialis {side}', 'nerve', [arm(0.1, -0.02, -0.01), arm(0.45, 0.015, -0.025), arm(0.85, 0.03, 0.0), el + [sx * 0.025, 0, 0.012], fore(0.6, 0.02, 0.0)], 0.0025, 0.0018))
    return out


# ------------------------------------------------------------------ Ausgabe

def write_bin(path, header, meshes):
    """Format (little endian): 'NSAN', int32 Version, int32 Länge, JSON-Kopf (UTF-8), danach je Mesh:
    float32 Positionen[3·n], uint16 oder uint32 Indizes[3·f], bei Gewichten uint8 Knochen[2·n] + uint8 Gewicht0[n],
    je Blendshape float32 Deltas[3·n]."""
    js = json.dumps(header, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    with open(path, 'wb') as f:
        f.write(b'NSAN'); f.write(struct.pack('<ii', 1, len(js))); f.write(js)
        for m in meshes:
            f.write(m['pos'].astype('<f4').tobytes())
            f.write(m['idx'].astype('<u2' if m['short'] else '<u4').tobytes())
            if m.get('bones') is not None:
                f.write(m['bones'].astype(np.uint8).tobytes())
                f.write(np.round(m['w0'] * 255).astype(np.uint8).tobytes())
            for d in m.get('shapes', []):
                f.write(d.astype('<f4').tobytes())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bp3d', required=True)
    ap.add_argument('--skin-faces', type=int, default=60000)
    args = ap.parse_args()
    t0 = time.time()
    names = load_names(args.bp3d)
    avail = {os.path.splitext(f)[0] for f in os.listdir(os.path.join(args.bp3d, 'stl'))}
    sel = select({i: names[i] for i in avail if i in names})
    print(f'{len(sel)} Teile ausgewählt')

    raw = {pid: load(args.bp3d, pid) for pid in sel}
    by_name = {names[pid]: raw[pid] for pid in sel}
    J_bp = joints_from_bones(by_name)
    origin = to_unity(J_bp['Hüftmitte'][None])[0]
    U = lambda v: to_unity(v) - origin
    J = {k: U(v[None])[0] for k, v in J_bp.items()}
    print('Gelenke', {k: np.round(v, 3).tolist() for k, v in J.items()})

    skin_bp = clean_skin(load(args.bp3d, SKIN), faces=args.skin_faces)
    skin = trimesh.Trimesh(U(skin_bp.vertices), skin_bp.faces, process=False)
    print(f'Haut {len(skin.vertices)} Ecken, {time.time() - t0:.0f} s')

    # Segmentradien aus der Haut: mittlerer Abstand zur Achse der zugehörigen Hautpunkte
    radii = {}
    p = skin.vertices
    for name, _, parent, a, b, *_m in SEGMENTS:
        A, B = J[a], J[b]; ab = B - A
        t = ((p - A) @ ab) / (ab @ ab)
        sel_t = (t > 0.25) & (t < 0.75)
        d = np.linalg.norm(p - (A + np.clip(t, 0, 1)[:, None] * ab), axis=1)
        near = d[sel_t]
        near = near[near < (0.25 if not LIMB(name) else 0.12)]
        radii[name] = float(np.percentile(near, 50)) if len(near) else 0.05
    bones_idx, w = skin_weights(p, J, radii)
    dominant = bones_idx[:, 0]

    # Kollisionsformen je Segment (Ruhelage, Achsen = Körperachsen)
    segs = []
    for k, (name, region, parent, a, b, mm, mf) in enumerate(SEGMENTS):
        pts = p[dominant == k]
        lo, hi = pts.min(0), pts.max(0)
        A, B = J[a], J[b]
        q = np.percentile(pts, [3, 97], axis=0)
        seg = {'name': name, 'region': region, 'parent': parent or '', 'joint': A.tolist(), 'end': B.tolist(),
               'massMale': mm, 'massFemale': mf, 'radius': radii[name] * 0.92, 'capsule': LIMB(name),
               'boxCenter': ((q[0] + q[1]) / 2 - A).tolist(), 'boxSize': (q[1] - q[0]).tolist()}
        segs.append(seg)

    # Atmungs-Blendshapes (Vorderseite Thorax rechts/links, Abdomen)
    n = skin.vertex_normals
    thor = segs[2]; abd = segs[1]
    def band(y, a, b): return smoothstep(a - 0.03, a + 0.03, y) * (1 - smoothstep(b - 0.03, b + 0.03, y))
    front = np.clip(n[:, 2], 0, 1) ** 0.7
    chest = band(p[:, 1], J['T12/L1'][1] - 0.02, J['C7/T1'][1] - 0.04) * front * (w[:, 0] * (dominant == 2) + 0.3 * (dominant == 1))
    side = smoothstep(-0.03, 0.03, p[:, 0])  # 1 = rechte Seite
    belly = band(p[:, 1], J['L4/L5'][1] - 0.06, J['T12/L1'][1] + 0.02) * front * ((dominant == 1) | (dominant == 0))
    shapes = {'AtmungThoraxRechts': (n * (chest * side * 0.009)[:, None]), 'AtmungThoraxLinks': (n * (chest * (1 - side) * 0.009)[:, None]),
              'AtmungAbdomen': (n * (belly * 0.012)[:, None])}

    # Haut in zwei Teilmeshes: Haut und Unterhose (Becken-/Oberschenkelband)
    fc = skin.faces; fcen = p[fc].mean(1)
    hy = J['Hüftmitte'][1]
    face_dom = dominant[fc].max(1)
    arm = (face_dom >= 4) & (face_dom <= 7)
    shorts = (fcen[:, 1] < hy + 0.10) & (fcen[:, 1] > hy - 0.17) & (np.abs(fcen[:, 0]) < 0.24) & ~arm
    meshes, entries = [], []
    def add(entry, pos, faces, **extra):
        short = len(pos) < 65535
        meshes.append({'pos': pos, 'idx': faces.reshape(-1), 'short': short, **extra})
        entry.update({'vertexCount': int(len(pos)), 'indexCount': int(faces.size), 'shortIndex': short})
        entry.setdefault('submeshes', [int(faces.size)])
        entries.append(entry)
    add({'name': 'Haut', 'label': 'Haut', 'layer': 'skin', 'segment': -1, 'color': LAYER_COLORS['skin'],
         'submeshes': [int(fc[~shorts].size), int(fc[shorts].size)], 'weights': True, 'blendShapes': list(shapes)},
        p, np.vstack([fc[~shorts], fc[shorts]]), bones=bones_idx, w0=w[:, 0], shapes=list(shapes.values()))

    # Anatomie
    counts = {}
    for pid, (layer, name) in sorted(sel.items(), key=lambda x: (x[1][0], x[1][1])):
        m = raw[pid]
        area = m.area
        if layer == 'bone': target = int(np.clip(area / 60, 120, 5000))
        elif layer in ('organ', 'brain'): target = int(np.clip(area / 80, 400, 8000))
        else: target = int(np.clip(area / 80, 200, 3000))
        m = simplify(m.copy(), target)
        pos = U(m.vertices)
        color = LAYER_COLORS[layer]
        if layer == 'organ':
            for pat, col in ORGAN_COLORS:
                if re.search(pat, name): color = col; break
        segk = int(np.argmax(segment_scores(pos.mean(0)[None], J, radii)[0]))
        add({'name': name, 'label': german(name), 'layer': layer, 'segment': segk, 'color': color, 'weights': False, 'blendShapes': []}, pos, m.faces)
        counts[layer] = counts.get(layer, 0) + len(m.faces)
    for name, layer, pts, r0, r1 in schematic_paths(J, by_name, U):
        m = tube(pts, r0, r1)
        add({'name': name, 'label': name, 'layer': layer, 'segment': int(np.argmax(segment_scores(np.mean(pts, 0)[None], J, radii)[0])),
             'color': LAYER_COLORS[layer], 'weights': False, 'blendShapes': [], 'schematic': True}, np.asarray(m.vertices), np.asarray(m.faces))
        counts[layer] = counts.get(layer, 0) + len(m.faces)
    print('Dreiecke je Ebene', counts)

    header = {
        'format': 'NotfallSIM-Anatomie', 'version': 1, 'units': 'm', 'axes': 'x rechts, y oben, z vorn; Ursprung Hüftgelenkmitte',
        'modelHeight': float(skin.vertices[:, 1].max() - skin.vertices[:, 1].min()), 'floorY': float(skin.vertices[:, 1].min()),
        'joints': [{'name': k, 'pos': v.tolist()} for k, v in J.items()], 'segments': segs, 'meshes': entries,
        'attribution': 'Anatomie: BodyParts3D, © The Database Center for Life Science, lizenziert unter CC BY-SA 2.1 Japan '
                       '(https://lifesciencedb.jp/bp3d/). Bearbeitet für NotfallSIM (vereinfacht, Haut neu vernetzt). '
                       'Nerven und Extremitätengefäße schematisch ergänzt.',
    }
    os.makedirs(OUT, exist_ok=True)
    write_bin(os.path.join(OUT, 'body.bin'), header, meshes)
    print(f'body.bin: {os.path.getsize(os.path.join(OUT, "body.bin")) / 1e6:.1f} MB, {len(entries)} Meshes, {time.time() - t0:.0f} s')

    # Vorschau-GLBs (three.js ist rechtshändig: x spiegeln)
    os.makedirs(PREVIEW, exist_ok=True)
    def glb(filter_fn, path, skin_alpha=None):
        sc = trimesh.Scene()
        for e, m in zip(entries, meshes):
            if not filter_fn(e): continue
            pos = m['pos'] * np.array([-1, 1, 1])
            f = m['idx'].reshape(-1, 3)  # Spiegelung x kehrt die Orientierung bereits um
            tm = trimesh.Trimesh(pos, f, process=False)
            col = np.array(e['color']) * 255
            if e['layer'] == 'skin' and skin_alpha is not None: col[3] = skin_alpha
            tm.visual.vertex_colors = np.tile(col.astype(np.uint8), (len(pos), 1))
            sc.add_geometry(tm)
        sc.export(path)
    glb(lambda e: e['layer'] == 'skin', os.path.join(PREVIEW, 'haut.glb'))
    glb(lambda e: e['layer'] != 'skin', os.path.join(PREVIEW, 'anatomie.glb'))
    # Gewichtsvorschau: Haut nach dominantem Segment eingefärbt
    pal = np.array([[230, 80, 80], [230, 160, 60], [220, 220, 70], [120, 200, 90], [80, 180, 220], [60, 100, 220], [150, 90, 220], [220, 90, 200],
                    [110, 110, 110], [200, 200, 200], [90, 60, 40], [40, 160, 140]], np.uint8)
    tm = trimesh.Trimesh(p * np.array([-1, 1, 1]), fc, process=False)
    tm.visual.vertex_colors = np.c_[pal[dominant], np.full(len(p), 255, np.uint8)]
    tm.export(os.path.join(PREVIEW, 'gewichte.glb'))
    json.dump({'joints': header['joints'], 'segments': segs}, open(os.path.join(PREVIEW, 'skelett.json'), 'w'), ensure_ascii=False, indent=1)
    # Vorschaubilder für die Doku: python3 … && node tools/anatomy/render_preview.js (optional)


if __name__ == '__main__':
    main()
