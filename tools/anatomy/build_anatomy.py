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
from labels import german, german_muscle  # noqa: E402
import collide                  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'StreamingAssets', 'NotfallSIM', 'anatomy')
PREVIEW = os.path.join(os.path.dirname(__file__), 'preview')

LAYER_COLORS = {  # RGBA 0..1
    'skin': (0.86, 0.68, 0.56, 1), 'bone': (0.93, 0.90, 0.80, 1), 'organ': (0.75, 0.35, 0.33, 1), 'brain': (0.88, 0.72, 0.72, 1),
    'artery': (0.82, 0.12, 0.12, 1), 'vein': (0.18, 0.28, 0.75, 1), 'nerve': (0.98, 0.85, 0.25, 1),
    'muscle': (0.70, 0.26, 0.24, 1),
}
ORGAN_COLORS = [(r'heart', (0.70, 0.12, 0.14, 1)), (r'lung', (0.93, 0.62, 0.64, 1)), (r'liver', (0.48, 0.20, 0.14, 1)),
                (r'spleen', (0.45, 0.16, 0.30, 1)), (r'kidney', (0.55, 0.22, 0.18, 1)), (r'stomach|duoden|jejun|ileum|colon|rectum', (0.92, 0.70, 0.60, 1)),
                (r'gallbladder', (0.35, 0.55, 0.25, 1)), (r'pancreas', (0.93, 0.80, 0.55, 1)), (r'bladder|ureter|prostate', (0.90, 0.80, 0.50, 1)),
                (r'trachea|bronch|esophag|thyroid', (0.85, 0.75, 0.70, 1)), (r'^diaphragm$', (0.66, 0.26, 0.24, 1)), (r'eyeball', (0.95, 0.95, 0.95, 1)), (r'adrenal|thymus', (0.90, 0.75, 0.40, 1))]


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

def sphere_center(v, iters=4):
    """Kugel durch Punkte (algebraischer Fit, Ausreißer schrittweise verworfen) → Mittelpunkt, Radius."""
    def fit(P):
        A = np.c_[2 * P, np.ones(len(P))]; b = (P ** 2).sum(1)
        x = np.linalg.lstsq(A, b, rcond=None)[0]; c = x[:3]; return c, float(np.sqrt(x[3] + c @ c))
    c, r = fit(v)
    for _ in range(iters):
        d = np.abs(np.linalg.norm(v - c, axis=1) - r)
        c, r = fit(v[d < max(2.0, np.percentile(d, 60))])
    return c, r


def head_center(m, h, medial_mm):
    """Gelenkkopf (Femur, Humerus) als Kugel: Drehpunkt für Hüfte und Schulter."""
    v = m.vertices; v = v[v[:, 2] > v[:, 2].max() - h]
    ax = np.abs(v[:, 0]); v = v[ax < ax.min() + medial_mm]
    c, r = sphere_center(v)
    assert 18 < r < 30, f'Gelenkkopf-Radius unplausibel: {r:.1f} mm'
    return c


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
        J[f'Hüfte {side}'] = head_center(femur, 60, 45)       # Mitte des Femurkopfs (Kugelfit, r ≈ 23 mm)
        J[f'Knie {side}'] = (bottom(femur, 25) + top(tibia, 25)) / 2
        J[f'Sprunggelenk {side}'] = talus.vertices.mean(0)
        J[f'Schulter {side}'] = head_center(hum, 45, 40)      # Mitte des Humeruskopfs (Kugelfit, r ≈ 22 mm)
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
LIMB_PRIORITY = 0.6


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def segment_scores(p, J, radii):
    """Zugehörigkeit von Punkten p (N×3, Unity) zu den Segmenten; Rückgabe N×S."""
    S = np.zeros((len(p), len(SEGMENTS)))
    limb = np.zeros(len(p))   # Nähe zur Achse von Oberarm/Oberschenkel unterhalb von Achsel bzw. Leiste (0…1)
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
            s *= smoothstep(-0.20, 0.0, t) if LIMB(name) else smoothstep(-0.30, 0.15, t)  # proximal des Gelenks: Elternsegment (Rumpf: breiter Übergang)
        if name in ('Oberarm rechts', 'Oberarm links', 'Oberschenkel rechts', 'Oberschenkel links'):
            s *= 1 - smoothstep(0.95, 1.08, t)        # distal davon das Kindsegment
        if name == 'Abdomen':
            s *= 1 - smoothstep(0.85, 1.3, t)
        if name == 'Thorax':
            s *= 1 - smoothstep(1.0, 1.12, t)
        if name == 'Becken':
            s *= 1 - smoothstep(0.85, 1.25, t)
        if name.startswith(('Oberarm', 'Oberschenkel')):
            limb = np.maximum(limb, smoothstep(0.15, 0.35, t) * (1 - smoothstep(1.25, 1.6, d / radii[name])))
        S[:, k] = s
    # Haut am inneren Oberarm/Oberschenkel liegt in BodyParts3D am Rumpf an (Haut verbunden). Rumpfanteil dort
    # abschwächen, aber nicht auf null: harte Grenzen reißen beim Abspreizen als Hautsegel auf.
    for k, (name, *_r) in enumerate(SEGMENTS):
        if name in ('Becken', 'Abdomen', 'Thorax'):
            S[:, k] *= 1 - LIMB_PRIORITY * limb
    return S


def segment_radii(p, J):
    """Mittlerer Abstand der Hautpunkte zur Segmentachse (mittleres Drittel des Segments)."""
    radii = {}
    for name, _, parent, a_, b_, *_m in SEGMENTS:
        A, B = J[a_], J[b_]; ab = B - A
        t = ((p - A) @ ab) / (ab @ ab)
        sel_t = (t > 0.25) & (t < 0.75)
        d = np.linalg.norm(p - (A + np.clip(t, 0, 1)[:, None] * ab), axis=1)
        near = d[sel_t]
        near = near[near < (0.25 if not LIMB(name) else 0.12)]
        radii[name] = float(np.percentile(near, 50)) if len(near) else 0.05
    return radii


def adjacency():
    """Nachbarschaft im Skelett (Eltern–Kind). Gewichte dürfen nur zwischen Nachbarn mischen — sonst hängt z. B.
    die Hand mit 20 % am Becken, neben dem sie in Ruhelage liegt, und bleibt beim Bewegen des Arms zurück."""
    names = [s_[0] for s_ in SEGMENTS]
    A = np.zeros((len(SEGMENTS), len(SEGMENTS)))
    for k, (name, _, parent, *_r) in enumerate(SEGMENTS):
        if parent:
            pk = names.index(parent); A[k, pk] = A[pk, k] = 1
    return A


def skin_weights(p, J, radii):
    S = segment_scores(p, J, radii)
    first = S.argmax(1)
    S2 = S * adjacency()[first]                       # nur Nachbarsegmente des dominanten Segments
    second = S2.argmax(1)
    w = np.c_[S[np.arange(len(p)), first], S2[np.arange(len(p)), second]]
    w = np.maximum(w, 1e-9)
    w /= w.sum(1, keepdims=True)                      # weiche Übergänge an den Gelenken
    weak = S.max(1) < 1e-4                            # kaum Zugehörigkeit → dominantes Segment allein
    w[weak] = [1, 0]
    return np.c_[first, second].astype(np.uint8), w


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


def tube(path, radii, sides=8, _retry=0):
    """Röhre um eine Mittellinie (bereits abgetastet). Dreiecke so orientiert, dass die Normalen nach außen zeigen
    (Unity: Kreuzprodukt (b−a)×(c−a) zeigt zur Vorderseite)."""
    path = np.asarray(path, float); n = len(path)
    verts, faces = [], []
    for i, c in enumerate(path):
        tdir = path[min(i + 1, n - 1)] - path[max(i - 1, 0)]
        tdir /= np.linalg.norm(tdir) + 1e-12
        ref = np.array([0, 0, 1.0]) if abs(tdir[2]) < 0.9 else np.array([1.0, 0, 0])
        u = np.cross(tdir, ref); u /= np.linalg.norm(u); w = np.cross(tdir, u)
        for k in range(sides):
            a = 2 * np.pi * k / sides
            verts.append(c + radii[i] * (np.cos(a) * u + np.sin(a) * w))
    for i in range(n - 1):
        for k in range(sides):
            a, b = i * sides + k, i * sides + (k + 1) % sides
            c_, d = a + sides, b + sides
            faces += [[a, b, c_], [b, d, c_]]
    verts, faces = np.array(verts), np.array(faces)
    # Kontrolle: Flächennormalen zeigen von der Mittellinie weg
    fc = verts[faces].mean(1); fn = np.cross(verts[faces[:, 1]] - verts[faces[:, 0]], verts[faces[:, 2]] - verts[faces[:, 0]])
    ring = np.repeat(np.arange(n - 1), 2 * sides)
    good = (np.einsum('ij,ij->i', fn, fc - path[ring]) > 0).mean()
    if good <= 0.8 and _retry < 4:   # Faltung in einem kurzen Stück: glätten und neu
        q = path.copy(); q[1:-1] = 0.25 * q[:-2] + 0.5 * q[1:-1] + 0.25 * q[2:]
        return tube(q, radii, sides, _retry + 1)
    assert good > 0.8, f'Röhre falsch orientiert ({good:.0%} der Flächen außen, {n} Ringe)'
    if good < 0.95: print(f'  Warnung: scharfer Knick in einem Bahnstück ({good:.0%} der Flächen außen, {n} Ringe)')
    return verts, faces


def orient_outward(pos, faces):
    """Dreiecke so drehen, dass das umschlossene Volumen positiv ist (Normalen außen). Die Abbildung
    BodyParts3D → Unity spiegelt (Determinante −1) und kehrt damit die Umlaufrichtung um."""
    c = pos.mean(0); a, b, d = pos[faces[:, 0]] - c, pos[faces[:, 1]] - c, pos[faces[:, 2]] - c
    vol = np.einsum('ij,ij->i', a, np.cross(b, d)).sum()
    return faces[:, ::-1].copy() if vol < 0 else faces


# ------------------------------------------------------------------ Wirbelkanal

def _slice(pos, tri, y):
    """Schnitt eines Meshes mit der Ebene y = const → Strecken in (x, z)."""
    d = pos[tri, 1] - y
    s = np.sign(d); cut = (s.min(1) < 0) & (s.max(1) > 0)
    segs = []
    for t, dd in zip(tri[cut], d[cut]):
        pts = []
        for i, j in ((0, 1), (1, 2), (2, 0)):
            if (dd[i] < 0) != (dd[j] < 0):
                f = dd[i] / (dd[i] - dd[j]); q = pos[t[i]] + f * (pos[t[j]] - pos[t[i]]); pts.append(q[[0, 2]])
        if len(pts) == 2: segs.append(pts)
    return np.array(segs)


def canal_center(pos, tri):
    """Mitte des Wirbelkanals: größter freier, rundum von Knochen umschlossener Kreis in einem waagerechten Schnitt."""
    y0, y1 = pos[:, 1].min(), pos[:, 1].max()
    best = None
    dirs = np.array([[np.cos(a), np.sin(a)] for a in np.linspace(0, 2 * np.pi, 12, endpoint=False)])
    for y in np.linspace(y0 + 0.25 * (y1 - y0), y0 + 0.75 * (y1 - y0), 7):
        S = _slice(pos, tri, y)
        if len(S) < 10: continue
        xm = (pos[:, 0].min() + pos[:, 0].max()) / 2
        gx, gz = np.meshgrid(np.arange(xm - 0.014, xm + 0.014, 0.001), np.arange(pos[:, 2].min(), pos[:, 2].max(), 0.001))
        q = np.c_[gx.ravel(), gz.ravel()]
        a, b = S[:, 0], S[:, 1]; e = b - a
        hits = np.zeros((len(q), len(dirs)), int)
        for k, dvec in enumerate(dirs):
            den = dvec[0] * e[:, 1] - dvec[1] * e[:, 0]
            w = a[None] - q[:, None]
            t = (w[..., 0] * e[:, 1] - w[..., 1] * e[:, 0]) / np.where(np.abs(den) < 1e-12, np.nan, den)
            u = (w[..., 0] * dvec[1] - w[..., 1] * dvec[0]) / np.where(np.abs(den) < 1e-12, np.nan, den)
            hits[:, k] = ((t > 0) & (t < 0.04) & (u >= 0) & (u <= 1)).sum(1)
        enclosed = (hits > 0).all(1)
        outside = ((hits % 2) == 0).mean(1) > 0.6       # gerade Schnittzahl → nicht im Knochen
        cand = enclosed & outside
        if not cand.any(): continue
        # Abstand zur nächsten Strecke
        qq = q[cand][:, None]; t = np.clip(((qq - a[None]) * e[None]).sum(2) / ((e * e).sum(1)[None] + 1e-12), 0, 1)
        dist = np.linalg.norm(qq - (a[None] + t[..., None] * e[None]), axis=2).min(1)
        i = dist.argmax()
        if best is None or dist[i] > best[1]:
            best = (np.array([q[cand][i][0], y, q[cand][i][1]]), dist[i])
    return best


# ------------------------------------------------------------------ Leitungsbahnen an Haut und Knochen anpassen

def _nearest_on_polyline(p, line):
    """Nächster Punkt auf einer Polylinie (je Punkt in p) und Parameter-Index für den Radius."""
    a, b = line[:-1], line[1:]; ab = b - a
    t = np.clip(((p[:, None] - a[None]) * ab[None]).sum(2) / ((ab * ab).sum(1)[None] + 1e-12), 0, 1)
    q = a[None] + t[..., None] * ab[None]
    d = np.linalg.norm(p[:, None] - q, axis=2)
    k = d.argmin(1)
    return q[np.arange(len(p)), k], d[np.arange(len(p)), k], k


def separate_lines(p, radii, others, gap=0.0006):
    """Mittellinie von anderen Bahnen fernhalten: Abstand ≥ Summe der Radien + Lücke."""
    for line, lrad in others:
        q, d, k = _nearest_on_polyline(p, line)
        need = radii + lrad[np.minimum(k, len(lrad) - 1)] + gap
        bad = d < need
        if not bad.any(): continue
        dirv = p[bad] - q[bad]
        nrm = np.linalg.norm(dirv, axis=1)
        dirv = np.where(nrm[:, None] > 1e-9, dirv / np.maximum(nrm, 1e-9)[:, None], np.array([1.0, 0, 0]))
        p[bad] = q[bad] + dirv * need[bad, None]
    return p


def constrain_path(path, radii, skin_tm, bone_tm, margin_skin=0.004, margin_bone=0.0015, iters=8, others=(), zones=()):
    """Schematische Bahn innerhalb der Haut und außerhalb der Hindernisse (bone_tm: Knochen, Organe, Muskeln, andere
    Bahnen) halten (iterativ projizieren, leicht glätten). zones: [(Mitte, Radius, Ersatz-Hindernis)] — dort gilt das
    Ersatz-Hindernis ohne den durchquerten Muskel (Durchtritt, z. B. Hiatus adductorius)."""
    p = np.array(path, float)
    for it in range(iters + 1):
        c, dist, tid = trimesh.proximity.closest_point(skin_tm, p)
        n = skin_tm.face_normals[tid]; depth = -np.sign(np.einsum('ij,ij->i', p - c, n)) * dist
        need = radii + margin_skin; bad = depth < need
        p[bad] = c[bad] - n[bad] * need[bad, None]
        zone_of = np.full(len(p), -1)
        for k, (zc, zr, _) in enumerate(zones):
            zone_of[np.linalg.norm(p - zc, axis=1) < zr] = k
        for k, tm in [(-1, bone_tm)] + [(k, z[2]) for k, z in enumerate(zones)]:
            sel = zone_of == k
            if not sel.any(): continue
            q = p[sel]
            c, dist, tid = trimesh.proximity.closest_point(tm, q)
            n = tm.face_normals[tid]; clear = np.sign(np.einsum('ij,ij->i', q - c, n)) * dist
            need = radii[sel] + margin_bone; bad = clear < need
            q[bad] = c[bad] + n[bad] * need[bad, None]
            p[sel] = q
        if others:
            p = separate_lines(p, radii, others)
        if it < iters:
            p[1:-1] = 0.25 * p[:-2] + 0.5 * p[1:-1] + 0.25 * p[2:]
    return p


def inflate_skin(skin_tm, points, margin=0.0015, radius=0.015, rounds=3):
    """Haut dort leicht nach außen schieben, wo Knochen (Finger-, Zehenspitzen) nach dem Vereinfachen herausragen."""
    from scipy.spatial import cKDTree
    v = skin_tm.vertices.copy()
    near = cKDTree(v).query(points)[0] < 0.02
    pts = points[near]
    for _ in range(rounds):
        tm = trimesh.Trimesh(v, skin_tm.faces, process=False)
        sd = trimesh.proximity.signed_distance(tm, pts)           # positiv = innen
        out = sd < margin
        if not out.any(): break
        c, _, _ = trimesh.proximity.closest_point(tm, pts[out])
        push = margin - sd[out]
        tree = cKDTree(v); vn = tm.vertex_normals
        disp = np.zeros(len(v))
        for ci, pu in zip(c, push):
            idx = tree.query_ball_point(ci, radius)
            if not idx: continue
            dd = np.linalg.norm(v[idx] - ci, axis=1) / radius
            disp[idx] = np.maximum(disp[idx], pu * (1 - smoothstep(0, 1, dd)) * 1.2)
        v = v + vn * disp[:, None]
    return trimesh.Trimesh(v, skin_tm.faces, process=False), int(near.sum())


def kink_deg(path, span=0.01):
    """Stärkster Richtungswechsel der Mittellinie über etwa span (Grad) — Maß für Zacken."""
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))]
    worst = 0.0
    for i in range(len(path)):
        a = np.searchsorted(d, d[i] - span / 2); b = min(np.searchsorted(d, d[i] + span / 2), len(path) - 1)
        if a >= i or b <= i: continue
        u, v = path[i] - path[a], path[b] - path[i]
        c = u @ v / (np.linalg.norm(u) * np.linalg.norm(v) + 1e-12)
        worst = max(worst, np.degrees(np.arccos(np.clip(c, -1, 1))))
    return worst


def resample(path, radii, step=0.008):
    """Gleichmäßig nach Bogenlänge neu abtasten (entfernt Doppelpunkte und Knicke nach dem Projizieren)."""
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))]
    keep = np.r_[True, np.diff(d) > 1e-6]
    path, radii, d = path[keep], radii[keep], d[keep]
    n = max(2, int(np.ceil(d[-1] / step)) + 1)
    t = np.linspace(0, d[-1], n)
    return np.c_[[np.interp(t, d, path[:, k]) for k in range(3)]].T, np.interp(t, d, radii)


def split_by_segment(path, radii, J, seg_radii):
    """Bahn in Stücke je Segment teilen (gleiche Zuordnung wie die Hautgewichte), damit sie Gelenkbewegungen folgt."""
    seg = segment_scores(path, J, seg_radii).argmax(1)
    for i in range(1, len(seg) - 1):                  # Ausreißer glätten
        if seg[i - 1] == seg[i + 1] != seg[i]: seg[i] = seg[i - 1]
    pieces, start = [], 0
    for i in range(1, len(seg) + 1):
        if i == len(seg) or seg[i] != seg[start]:
            a, b = max(start - 1, 0), min(i + 1, len(seg))   # ein Ring Überlappung zum Nachbarn
            if b - a >= 2: pieces.append((int(seg[start]), path[a:b], radii[a:b]))
            start = i
    return pieces


def schematic_paths(J, bones, U):
    """Wichtige Leitungsbahnen, die in BodyParts3D fehlen — schematisch entlang der Knochen (Unity-Koordinaten).
    Rückgabe: Liste (Name, Ebene, Punkte, Radius Anfang, Radius Ende)."""
    out = []
    V = lambda n: U(bones[n].vertices)
    cen = lambda n: V(n).mean(0)
    vert_names = [f'{o} cervical vertebra' for o in ('third', 'fourth', 'fifth', 'sixth', 'seventh')] + \
                 [f'{o} thoracic vertebra' for o in ('first', 'second', 'third', 'fourth', 'fifth', 'sixth', 'seventh', 'eighth', 'ninth', 'tenth', 'eleventh', 'twelfth')] + \
                 ['first lumbar vertebra', 'second lumbar vertebra']
    # Rückenmark in der Mitte des Wirbelkanals (aus den Wirbeln bestimmt), von der Medulla oblongata bis L1 (Conus)
    canal = []
    for n in ['atlas', 'axis'] + vert_names[:-1]:
        b = bones[n]; cc = canal_center(U(b.vertices), b.faces)
        if cc is not None and 0.003 < cc[1] < 0.016:
            # Atlas: vorn im Kanal liegt der Dens axis, das Mark im hinteren Teil
            canal.append(cc[0] + ([0, 0, -0.005] if n == 'atlas' else 0))
        else: print('  Wirbelkanal nicht gefunden:', n, None if cc is None else round(cc[1] * 1000, 1))
    med = V('medulla oblongata'); top = med[med[:, 1].argmin()]
    l1 = U(bones['first lumbar vertebra'].vertices); l1c = canal_center(l1, bones['first lumbar vertebra'].faces)[0]
    cord = [top] + canal + [l1c + [0, -0.012, 0]]
    out.append(('Rückenmark', 'nerve', cord, 0.0055, 0.0035))
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
        # Kniekehle von tief nach oberflächlich: A. poplitea (am Femur), V. poplitea, N. tibialis (ischiadicus)
        out.append((f'A. femoralis {side}', 'artery', [ext_end, thigh(0.08, -0.03, 0.045), thigh(0.4, -0.04, 0.03), thigh(0.75, -0.035, -0.01), thigh(0.97, -0.004, -0.028)], 0.0045, 0.0038))
        out.append((f'A. poplitea / tibialis posterior {side}', 'artery', [thigh(0.97, -0.004, -0.028), shank(0.1, -0.004, -0.028), shank(0.5, -0.015, -0.02), shank(0.95, -0.03, -0.01), J[f'Ferse {side}'] + [sx * -0.02, 0.03, 0.03]], 0.0035, 0.0022))
        out.append((f'V. femoralis {side}', 'vein', [ext_end + [sx * -0.012, 0, -0.005], thigh(0.08, -0.042, 0.04), thigh(0.4, -0.05, 0.025), thigh(0.75, -0.045, -0.015), thigh(0.97, 0.004, -0.040)], 0.005, 0.0042))
        out.append((f'A. axillaris / brachialis {side}', 'artery', [sub_end, arm(0.05, -0.02, 0.0), arm(0.5, -0.02, 0.0), arm(0.95, -0.005, 0.015)], 0.0035, 0.003))
        out.append((f'A. radialis {side}', 'artery', [arm(0.95, -0.005, 0.015), fore(0.3, 0.015, 0.012), fore(0.98, 0.022, 0.01)], 0.0022, 0.0018))
        out.append((f'A. ulnaris {side}', 'artery', [arm(0.95, -0.005, 0.015), fore(0.3, -0.015, 0.008), fore(0.98, -0.02, 0.008)], 0.0022, 0.0018))
        # Nerven: N. ischiadicus → tibialis; N. femoralis; Plexus brachialis → N. medianus, ulnaris, radialis
        sac = cen('sacrum')
        out.append((f'N. ischiadicus {side}', 'nerve', [sac + [sx * 0.05, 0.0, -0.02], hip + [sx * -0.01, -0.03, -0.07], thigh(0.3, 0, -0.05), thigh(0.7, 0.004, -0.05), thigh(0.95, 0.012, -0.054), shank(0.15, 0.010, -0.050), shank(0.45, 0.010, -0.040), shank(0.95, -0.02, -0.018)], 0.0055, 0.003))
        # N. femoralis: seitlich am Wirbelkörper L4 (im M. psoas), in der Rinne zwischen Psoas und Iliacus nach unten,
        # unter dem Leistenband lateral der A. femoralis (hinter dem Darm, nicht durch ihn)
        l4, l5 = cen('fourth lumbar vertebra'), cen('fifth lumbar vertebra')
        out.append((f'N. femoralis {side}', 'nerve', [l4 + [sx * 0.035, 0, -0.005], l5 + [sx * 0.06, -0.03, 0.0],
                    np.array([sx * 0.085, hip[1] + 0.07, ext_end[2] - 0.025]), ext_end + [sx * 0.018, 0.0, -0.004],
                    thigh(0.15, 0.0, 0.05)], 0.003, 0.0025))
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
    ap.add_argument('--seed-cache', help='aufgelöste Teile aus einer vorhandenen body.bin übernehmen (spart die Kollisionsauflösung)')
    args = ap.parse_args()
    t0 = time.time()
    names = load_names(args.bp3d)
    avail = {os.path.splitext(f)[0] for f in os.listdir(os.path.join(args.bp3d, 'stl'))}
    sel = select({i: names[i] for i in avail if i in names})
    print(f'{len(sel)} Teile ausgewählt')

    # Rohdaten im Speicher nur für Nicht-Muskeln (Gelenke und Bahnen brauchen sie); die ca. 14 Mio. Muskel-Dreiecke
    # werden einzeln geladen und sofort vereinfacht (sonst reicht der Speicher nicht)
    raw = {pid: load(args.bp3d, pid) for pid, (layer, _) in sel.items() if layer != 'muscle'}
    by_name = {names[pid]: raw[pid] for pid in raw}
    J_bp = joints_from_bones(by_name)
    origin = to_unity(J_bp['Hüftmitte'][None])[0]
    U = lambda v: to_unity(v) - origin
    J = {k: U(v[None])[0] for k, v in J_bp.items()}
    print('Gelenke', {k: np.round(v, 3).tolist() for k, v in J.items()})

    # Anatomie zuerst (vereinfacht, Unity-Koordinaten, Normalen außen) — die Haut wird danach an die Knochen angepasst
    parts = []
    for pid, (layer, name) in sorted(sel.items(), key=lambda x: (x[1][0], x[1][1])):
        m = raw[pid] if pid in raw else load(args.bp3d, pid)
        if layer == 'muscle':
            # Segmentierungsreste (kleine abgetrennte Inseln) entfernen; Interkostalmuskeln bestehen aus vielen Feldern
            comps = m.split(only_watertight=False)
            big = max(c.area for c in comps)
            keep = [c for c in comps if c.area >= max(0.03 * big, 150) or ('intercostal' in name and c.area > 400)]
            m = trimesh.util.concatenate(keep)
        area = m.area
        if layer == 'muscle': target = int(np.clip(area / 40, 400, 6000))
        elif layer == 'bone': target = int(np.clip(area / 20, 400, 6000))      # Röntgenblick aus der Nähe: Facetten vermeiden
        elif re.search(r'gyr|insula|lobule', name): target = int(np.clip(area / 15, 900, 5000))   # Rinde: Windungen erhalten
        elif name == 'diaphragm': target = 10000                                                 # dünne Platte, Hiatus erhalten
        elif layer in ('organ', 'brain'): target = int(np.clip(area / 40, 600, 8000))
        else: target = int(np.clip(area / 50, 300, 4000))
        m = simplify(m.copy(), target)
        pos = U(m.vertices)
        faces = orient_outward(pos, np.asarray(m.faces))
        color = LAYER_COLORS[layer]
        if layer == 'organ':
            for pat, col in ORGAN_COLORS:
                if re.search(pat, name): color = col; break
        parts.append({'name': name, 'layer': layer, 'pos': pos, 'faces': faces, 'color': color})
    print(f'{len(parts)} Teile vereinfacht, {time.time() - t0:.0f} s')
    # Überschneidungen auflösen: nachgiebigere Strukturen weichen festeren aus (collide.py). Dauert lange; das Ergebnis
    # wird zwischengespeichert (Schlüssel: Quelltext von collide.py/parts.py und die vereinfachten Teile).
    import hashlib, pickle
    key = hashlib.sha1(open(collide.__file__, 'rb').read() + open(os.path.join(os.path.dirname(__file__), 'parts.py'), 'rb').read()
                       + b''.join(q['pos'].astype(np.float32).tobytes() for q in parts)).hexdigest()
    cache = os.path.join(PREVIEW, 'kollision_cache.pkl')
    hit = False
    if os.path.exists(cache):
        try:
            ck, cparts = pickle.load(open(cache, 'rb'))
            if ck == key: parts, hit = cparts, True
        except Exception: pass
    seeded = 0
    if not hit and args.seed_cache:
        # Teile, die es in einer früheren body.bin schon gab, übernehmen (dort bereits gegeneinander aufgelöst); danach
        # werden nur noch Paare mit neuen Teilen (z. B. Muskeln) geprüft
        from anatomy_io import load as load_bin
        _, old = load_bin(args.seed_cache)
        prev = {(m['info']['name'], m['info']['layer']): m for m in old[1:] if not m['info'].get('schematic')}
        for q in parts:
            m = prev.get((q['name'], q['layer']))
            if m is not None:
                q['pos'], q['faces'], q['seeded'] = m['pos'].astype(np.float64), m['tri'].astype(np.int64), True
                seeded += 1
        print(f'{seeded} Teile aus {args.seed_cache} übernommen')
        if seeded == len(parts):
            os.makedirs(PREVIEW, exist_ok=True); pickle.dump((key, parts), open(cache, 'wb'))
            hit = True
    if hit:
        print('Kollisionsauflösung aus dem Zwischenspeicher')
    else:
        collide.resolve(parts, skip=(lambda A, B: A.get('seeded') and B.get('seeded')) if seeded else None,
                        progress=os.path.join(PREVIEW, 'kollision_zwischenstand.pkl'))
        for q in parts: q.pop('seeded', None)
        os.makedirs(PREVIEW, exist_ok=True); pickle.dump((key, parts), open(cache, 'wb'))
        rest = collide.crossings(parts)
        print(f'Überschneidungen nach dem Auflösen: {len(rest)}' + (' — ' + '; '.join(f'{b} in {a} {d * 1000:.1f} mm' for d, a, b, c in rest[:6]) if rest else ''))
    bone_tm = trimesh.util.concatenate([trimesh.Trimesh(q['pos'], q['faces'], process=False) for q in parts if q['layer'] == 'bone'])
    print(f'Kollisionen aufgelöst, {time.time() - t0:.0f} s')

    raw_skin = load(args.bp3d, SKIN)
    # Hinweis: In BodyParts3D liegen Oberarme und Oberschenkel innen an; die Haut ist dort verbunden. Weiche
    # Gewichte (Rumpf/Glied gemischt) verteilen die Dehnung beim Bewegen — eine harte Trennung reißt sichtbar auf.
    skin_bp = clean_skin(raw_skin, faces=args.skin_faces)
    sv = U(skin_bp.vertices)
    skin = trimesh.Trimesh(sv, orient_outward(sv, np.asarray(skin_bp.faces)), process=False)
    assert skin.volume > 0, 'Haut falsch orientiert'
    skin, n_near = inflate_skin(skin, np.vstack([q['pos'] for q in parts]))
    print(f'Haut {len(skin.vertices)} Ecken (an {n_near} hautnahen Punkten geprüft), {time.time() - t0:.0f} s')

    # Segmentradien aus der Haut: mittlerer Abstand zur Achse der zugehörigen Hautpunkte
    p = skin.vertices
    radii = segment_radii(p, J)
    bones_idx, w = skin_weights(p, J, radii)
    dominant = bones_idx[:, 0]

    # Kollisionsformen je Segment (Ruhelage, Achsen = Körperachsen)
    segs = []
    for k, (name, region, parent, a_, b_, mm, mf) in enumerate(SEGMENTS):
        pts = p[dominant == k]
        A, B = J[a_], J[b_]
        q = np.percentile(pts, [3, 97], axis=0)
        seg = {'name': name, 'region': region, 'parent': parent or '', 'joint': A.tolist(), 'end': B.tolist(),
               'massMale': mm, 'massFemale': mf, 'radius': radii[name] * 0.92, 'capsule': LIMB(name),
               'boxCenter': ((q[0] + q[1]) / 2 - A).tolist(), 'boxSize': (q[1] - q[0]).tolist()}
        segs.append(seg)

    # Atmungs-Blendshapes (Vorderseite Thorax rechts/links, Abdomen) — entlang der äußeren Normalen
    n = skin.vertex_normals
    def band(y, a, b): return smoothstep(a - 0.03, a + 0.03, y) * (1 - smoothstep(b - 0.03, b + 0.03, y))
    front = np.clip(n[:, 2], 0, 1) ** 0.7
    chest = band(p[:, 1], J['T12/L1'][1] - 0.02, J['C7/T1'][1] - 0.04) * front * (w[:, 0] * (dominant == 2) + 0.3 * (dominant == 1))
    side = smoothstep(-0.03, 0.03, p[:, 0])  # 1 = rechte Seite
    belly = band(p[:, 1], J['L4/L5'][1] - 0.06, J['T12/L1'][1] + 0.02) * front * ((dominant == 1) | (dominant == 0))
    shapes = {'AtmungThoraxRechts': (n * (chest * side * 0.009)[:, None]), 'AtmungThoraxLinks': (n * (chest * (1 - side) * 0.009)[:, None]),
              'AtmungAbdomen': (n * (belly * 0.012)[:, None])}
    for k_, d_ in shapes.items():  # Einatmung hebt die Vorderseite (z nimmt zu)
        assert d_[:, 2].sum() > 0, k_

    # Haut in zwei Teilmeshes: Haut und Unterhose (Becken-/Oberschenkelband)
    fc = skin.faces; fcen = p[fc].mean(1)
    hy = J['Hüftmitte'][1]
    face_dom = dominant[fc].max(1)
    arm = (face_dom >= 4) & (face_dom <= 7)
    shorts = (fcen[:, 1] < hy + 0.10) & (fcen[:, 1] > hy - 0.17) & (np.abs(fcen[:, 0]) < 0.24) & ~arm
    meshes, entries = [], []
    def add(entry, pos, faces, **extra):
        short = len(pos) < 65535
        meshes.append({'pos': np.asarray(pos), 'idx': np.asarray(faces).reshape(-1), 'short': short, **extra})
        entry.update({'vertexCount': int(len(pos)), 'indexCount': int(np.asarray(faces).size), 'shortIndex': short})
        entry.setdefault('submeshes', [int(np.asarray(faces).size)])
        entries.append(entry)
    add({'name': 'Haut', 'label': 'Haut', 'layer': 'skin', 'segment': -1, 'color': LAYER_COLORS['skin'],
         'submeshes': [int(fc[~shorts].size), int(fc[shorts].size)], 'weights': True, 'blendShapes': list(shapes)},
        p, np.vstack([fc[~shorts], fc[shorts]]), bones=bones_idx, w0=w[:, 0], shapes=list(shapes.values()))

    # Anatomie
    counts = {}
    for q in parts:
        if q['layer'] == 'muscle': continue        # Muskeln nach den Bahnen (sie bekommen Rinnen um die Bahnen)
        segk = int(np.argmax(segment_scores(q['pos'].mean(0)[None], J, radii)[0]))
        add({'name': q['name'], 'label': german(q['name']), 'layer': q['layer'], 'segment': segk, 'color': q['color'], 'weights': False,
             'blendShapes': []}, q['pos'], q['faces'])
        counts[q['layer']] = counts.get(q['layer'], 0) + len(q['faces'])
    # Schematische Bahnen: innerhalb der Haut, außerhalb aller anderen Strukturen (Knochen, Organe, Gefäße, schon
    # gelegte Bahnen), je Segment ein Stück (folgt den Gelenken). Reihenfolge: Arterien, Venen, Nerven.
    # Rückenmark aus dem Wirbelkanal; periphere Bahnen entlang der Muskellücken (pathways.py)
    import pathways
    geo = pathways.Geo(parts, J)
    cen_u = lambda n: geo.pos(n).mean(0)
    paths = [x for x in schematic_paths(J, by_name, U) if x[0] == 'Rückenmark'] + pathways.routes(geo, cen_u)
    paths.sort(key=lambda x: {'artery': 0, 'vein': 1, 'nerve': 2}[x[1]])
    passage_log = []
    tube_meshes = []   # (Name, Trimesh) aller Bahnstücke — danach bekommen die Muskeln Rinnen um sie
    routed = []        # (Name, Ebene, Mittellinie, Radien, Hindernis ohne Muskeln, Hautabstand, Durchtritte)
    built = []   # (layer, Trimesh) bereits gelegter Bahnen
    lines = []   # (layer, Mittellinie, Radien) bereits gelegter Bahnen
    for name, layer, pts, r0, r1 in paths:
        own = collide.group_of(name, layer)
        obst_named = [(q['name'], geo.tm(q['name'])) for q in parts if not collide.allowed((name, layer), (q['name'], q['layer']))]
        obst_named += [(None, tm) for l_, tm in built if l_ != layer]
        obst = [tm for _, tm in obst_named]
        # Führung: Muskeln sind kein Hindernis — Gefäß-Nerven-Straßen liegen in den Muskelsepten, die Muskeln weichen
        # ihnen aus (Rinne, z. B. Sulcus bicipitalis). Hindernisse: Knochen, Organe, große Gefäße, andere Bahnen.
        obst_tm = trimesh.util.concatenate([tm for n_, tm in obst_named if n_ is None or geo.P[n_]['layer'] != 'muscle'])
        zones = []
        if not isinstance(pts[0], np.ndarray) or len(np.shape(pts)) != 2:   # Wegpunkt-Beschreibung (pathways.py)
            # Wegpunkte in den echten Lücken (hier zählen die Muskeln mit)
            pts, passages, wlog = pathways.resolve_waypoints(pts, geo, obst, need=r0 + 0.0008)
            for mu, pc, zr, note in passages:
                passage_log.append((name, mu, pc.tolist(), zr, note))
            narrow = [(note, round(cl * 1000, 1)) for kind, note, cl in wlog if kind == 'Gap' and cl < r0 + 0.0008]
            if narrow: print(f'  {name}: enge Wegpunkte {narrow}')
        path = catmull(pts)
        rad = np.linspace(r0, r1, len(path))
        path, rad = resample(path, rad)
        # 8 mm unter der Haut: die Haut am inneren Oberarm ist mit dem Rumpf verbunden und folgt Armbewegungen nur
        # teilweise — so bleiben die Bahnen auch auf der Trage (Arme leicht bewegt) innerhalb der Haut
        others = [(ln, lr) for l_, ln, lr in lines if l_ != layer]
        # Armbahnen 8 mm unter der Haut (die Armhaut folgt der Lagerung nur teilweise), sonst 4 mm (Kniekehle ist eng)
        msk = 0.008 if re.search(r'axillaris|brachialis|radialis|ulnaris|Plexus|musculocutaneus', name) else 0.004
        path = constrain_path(path, rad, skin, obst_tm, margin_skin=msk, margin_bone=0.001, others=others, zones=zones)
        path, rad = resample(path, rad, step=0.005)
        # Armbahnen in der Achsel: 4 mm Abstand zu Skapula und Klavikula, weil sich der Arm auf der Trage um die Schulter
        # dreht (Teile des Oberarms bewegen sich dabei bis ~1 cm nach dorsal gegen das Schulterblatt)
        shoulder = None
        if msk > 0.004:
            shoulder = trimesh.util.concatenate([trimesh.Trimesh(q['pos'], q['faces'], process=False) for q in parts
                                                 if re.search(r'scapula|clavicle', q['name'])])
        for _ in range(12):  # letzte Runden: leicht glätten, dann wieder an Haut, Hindernisse und andere Bahnen anpassen —
            # mehrere Runden, weil sich in engen Logen Knochen, Gefäße und Haut gegenseitig im Weg sind; ohne das Glätten
            # entstehen Zacken, wo zwei Bedingungen gegeneinander schieben (Achsel, Ellbogen)
            path[1:-1] = 0.25 * path[:-2] + 0.5 * path[1:-1] + 0.25 * path[2:]
            if shoulder is not None:
                path = constrain_path(path, rad, skin, shoulder, margin_skin=msk, margin_bone=0.004, iters=0)
            path = constrain_path(path, rad, skin, obst_tm, margin_skin=msk, margin_bone=0.001, iters=0, others=others, zones=zones)
        print(f'  {name}: stärkster Knick {kink_deg(path):.0f}° (je 1 cm)')
        lines.append((layer, path.copy(), rad.copy()))
        routed.append((name, layer, path, rad, obst_tm, msk, [(np.array(pc), zr, mu) for a, mu, pc, zr, _ in passage_log if a == name]))
        for segk, piece, prad in split_by_segment(path, rad, J, radii):
            verts, faces = tube(piece, prad)
            built.append((layer, trimesh.Trimesh(verts, faces, process=False)))
            tube_meshes.append((name, built[-1][1]))

    # Muskeln: Rinne um die Bahnen (außer an den Durchtrittsstellen, dort zieht die Bahn durch den Muskel)
    def carve(tubes_):
        n_ = 0
        for q in parts:
            if q['layer'] != 'muscle': continue
            lo_q, hi_q = q['pos'].min(0) - 0.01, q['pos'].max(0) + 0.01
            obst = []
            for tname, tm in tubes_:
                if np.any(tm.bounds[0] > hi_q) or np.any(tm.bounds[1] < lo_q): continue
                zs = [(np.array(pc), zr) for a_, mu, pc, zr, _ in passage_log if a_ == tname and mu == q['name']]
                if zs:   # Durchtritt: nur die Röhrenteile außerhalb der Zone formen den Muskel
                    v = np.asarray(tm.vertices); keepv = np.ones(len(v), bool)
                    for zc, zr in zs: keepv &= np.linalg.norm(v - zc, axis=1) > zr
                    f = np.asarray(tm.faces); f = f[keepv[f].all(1)]
                    if not len(f): continue
                    tm = trimesh.Trimesh(v, f, process=False)
                obst.append({'name': tname, 'tm': tm})
            if obst:
                q['pos'], q['faces'] = collide.refine_near(q['pos'], q['faces'], obst, levels=1)
                new_, _ = collide.push_out(q['pos'], q['faces'], obst)
                if np.linalg.norm(new_ - q['pos'], axis=1).max() > 1e-5: n_ += 1
                q['pos'] = new_
        return n_
    print(f'{carve(tube_meshes)} Muskeln mit Rinnen um Leitungsbahnen')

    # Nachlauf: wo eine Rinne nicht tief genug wurde (dünne Muskeln, enge Logen), die Bahn verschieben …
    from anatomy_io import winding as _winding
    mus_tm = {q['name']: trimesh.Trimesh(q['pos'], q['faces'], process=False) for q in parts if q['layer'] == 'muscle'}
    nudged = 0
    final_tubes, final_pieces, kinks = [], [], {}
    for name, layer, path, rad, obst_tm, msk, zs in routed:
        hit = []
        for mn, tm in mus_tm.items():
            lo_m, hi_m = tm.bounds
            sel = np.all((path > lo_m - 0.006) & (path < hi_m + 0.006), axis=1)
            if not sel.any(): continue
            d = trimesh.proximity.closest_point(tm, path[sel])[1]
            ins = _winding(path[sel], np.asarray(tm.vertices), np.asarray(tm.faces)) > 0.5
            close = ins | (d < rad[sel] * 0.5)
            for zc, zr, zm in zs:
                if zm == mn: close &= np.linalg.norm(path[sel] - zc, axis=1) > zr
            if close.any(): hit.append((mn, tm))
        before = path.copy()
        if hit:
            nudged += 1
            ob = trimesh.util.concatenate([obst_tm] + [tm for _, tm in hit])
            # Durchtritt: in der Zone gilt das Hindernis ohne den durchquerten Muskel
            zones_n = [(zc, zr, trimesh.util.concatenate([obst_tm] + [tm for mn, tm in hit if mn != zm])) for zc, zr, zm in zs]
            others = [(ln, lr) for l_, ln, lr in lines if l_ != layer]
            for _ in range(6):
                path[1:-1] = 0.25 * path[:-2] + 0.5 * path[1:-1] + 0.25 * path[2:]
                path[:] = constrain_path(path, rad, skin, ob, margin_skin=msk, margin_bone=0.001, iters=0, others=others, zones=zones_n)
            # Knochen und Haut haben das letzte Wort
            for _ in range(2):
                path[:] = constrain_path(path, rad, skin, obst_tm, margin_skin=msk, margin_bone=0.001, iters=0)
        k_ = kink_deg(path)
        if hit and k_ > max(60.0, kink_deg(before) + 20):   # Nachlauf hat die Bahn gefaltet: alte Lage behalten
            print(f'  {name}: Nachlauf verworfen (Knick {k_:.0f}°)')
            path[:] = before; k_ = kink_deg(path)
        if k_ > 45: print(f'  {name}: nach dem Nachlauf Knick {k_:.0f}°')
        kinks[name] = k_
        for segk, piece, prad in split_by_segment(path, rad, J, radii):
            verts, faces = tube(piece, prad)
            final_pieces.append((name, layer, segk, verts, faces))
            final_tubes.append((name, trimesh.Trimesh(verts, faces, process=False)))
    print(f'{nudged} Bahnen gegen Restberührungen mit Muskeln nachgeführt')
    # … und die Rinnen gegen die endgültige Lage der Bahnen noch einmal formen
    print(f'{carve(final_tubes)} Muskeln im zweiten Durchgang nachgeformt')

    # Muskeln gewichtet wie die Haut — sie ziehen über Gelenke und reißen sonst beim Beugen auseinander
    for q in parts:
        if q['layer'] != 'muscle': continue
        segk = int(np.argmax(segment_scores(q['pos'].mean(0)[None], J, radii)[0]))
        mb, mw = skin_weights(q['pos'], J, radii)
        add({'name': q['name'], 'label': german_muscle(q['name']), 'layer': 'muscle', 'segment': segk, 'color': q['color'],
             'weights': True, 'blendShapes': []}, q['pos'], q['faces'], bones=mb, w0=mw[:, 0])
        counts['muscle'] = counts.get('muscle', 0) + len(q['faces'])
    for name, layer, segk, verts, faces in final_pieces:
        add({'name': name, 'label': name, 'layer': layer, 'segment': segk, 'color': LAYER_COLORS[layer],
             'weights': False, 'blendShapes': [], 'schematic': True}, verts, faces)
        counts[layer] = counts.get(layer, 0) + len(faces)
    print('Dreiecke je Ebene', counts)

    header = {
        'format': 'NotfallSIM-Anatomie', 'version': 1, 'units': 'm', 'axes': 'x rechts, y oben, z vorn; Ursprung Hüftgelenkmitte',
        'modelHeight': float(skin.vertices[:, 1].max() - skin.vertices[:, 1].min()), 'floorY': float(skin.vertices[:, 1].min()),
        'joints': [{'name': k, 'pos': v.tolist()} for k, v in J.items()], 'segments': segs, 'meshes': entries,
        'passages': [{'pathway': a, 'muscle': b, 'point': c, 'radius': r, 'note': n} for a, b, c, r, n in passage_log],
        'kinks': [{'pathway': k, 'degrees': round(float(v), 1)} for k, v in kinks.items()],
        'attribution': 'Anatomie: BodyParts3D, © The Database Center for Life Science, lizenziert unter CC BY-SA 2.1 Japan '
                       '(https://lifesciencedb.jp/bp3d/). Bearbeitet für NotfallSIM (vereinfacht, Haut neu vernetzt). '
                       'Skelettmuskeln aus BodyParts3D. Nerven und Extremitätengefäße schematisch ergänzt.',
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
            f = m['idx'].reshape(-1, 3)[:, ::-1]  # Spiegelung x (three.js rechtshändig) → Umlaufrichtung umkehren
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
    tm = trimesh.Trimesh(p * np.array([-1, 1, 1]), fc[:, ::-1], process=False)
    tm.visual.vertex_colors = np.c_[pal[dominant], np.full(len(p), 255, np.uint8)]
    tm.export(os.path.join(PREVIEW, 'gewichte.glb'))
    json.dump({'joints': header['joints'], 'segments': segs}, open(os.path.join(PREVIEW, 'skelett.json'), 'w'), ensure_ascii=False, indent=1)
    # Vorschaubilder für die Doku: python3 … && node tools/anatomy/render_preview.js (optional)


if __name__ == '__main__':
    main()
