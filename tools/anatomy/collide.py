"""Überschneidungen zwischen anatomischen Strukturen auflösen (für body.bin).

Grundsatz: Die nachgiebigere Struktur weicht der festeren aus, so wie es im Körper auch Abdrücke gibt
(Impressio cardiaca der Lunge, Sulcus a. subclaviae, Impressio renalis der Leber, Rippenabdrücke).
Reihenfolge der Festigkeit: Knochen > Arterien > Herz > Venen > Atemwege > Nervensystem > Zwerchfell >
Nieren/Harnwege > Prostata > Leber > Milz > Skelettmuskeln > Pankreas > Nebennieren > Thymus > Gallenblase > Magen-Darm > Lunge.
Muskeln untereinander: der kleinere weicht dem größeren aus; Köpfe und Teile desselben Muskels (und beide Seiten,
z. B. an der Linea alba) dürfen sich berühren, ebenso die drei Schichten der Interkostalmuskeln.

Erlaubt bleiben Verbindungen, die anatomisch zusammengehören: Äste eines Gefäßbaums, Mündungen ins Herz,
Lungenlappen untereinander, Bronchien und Lungengefäße in der Lunge, Darmabschnitte, Niere–Harnleiter–Blase,
Gehirnteile, Rippe–Rippenknorpel–Brustbein.

Die ausweichende Oberfläche wird an den betroffenen Ecken auf die Oberfläche des Hindernisses (+ Abstand)
geschoben; die Verschiebung klingt über einige Kantenringe weich aus (harmonische Fortsetzung), damit keine Zacken
entstehen. Liegt eine Struktur überwiegend im Hindernis (z. B. Gefäßäste im Organ), bleibt sie unverändert.
"""
import re
import numpy as np
import scipy.sparse as sp
from scipy.spatial import cKDTree
import trimesh
from anatomy_io import winding
from labels import muscle_base


def group_of(name, layer):
    n = name.lower()
    if layer == 'skin': return 'skin'
    if layer == 'bone': return 'bone'
    if layer == 'muscle': return 'intercostal' if 'intercostal' in n else 'muscle:' + muscle_base(n)
    if layer == 'brain' or 'optic' in n or n == 'eyeball': return 'cns'
    if n.startswith('rückenmark'): return 'cns'
    if n == 'wall of heart': return 'heart'
    if n == 'pulmonary artery': return 'pa'
    if n == 'pulmonary vein': return 'pv'
    if layer == 'artery': return 'art'
    if layer == 'vein': return 'ven'
    if layer == 'nerve': return 'nerve'
    if re.search(r'esophag|stomach|duoden|jejun|ileum|colon|rectum', n): return 'gi'
    if re.search(r'trachea|bronch|thyroid cartilage', n): return 'air'
    if re.search(r'lobe of .*lung|middle lobe of lung', n): return 'lung'
    if re.search(r'kidney|ureter|urinary bladder', n): return 'uro'
    if 'thymus' in n: return 'thymus'
    if 'adrenal' in n: return 'adrenal'
    return n


PRIORITY = {'bone': 100, 'art': 90, 'pa': 88, 'heart': 85, 'ven': 80, 'pv': 78, 'air': 75, 'cns': 70, 'diaphragm': 65,
            'uro': 60, 'prostate': 58, 'liver': 57, 'spleen': 56, 'pancreas, nsn': 52, 'adrenal': 50, 'thymus': 48,
            'gallbladder': 46, 'gi': 45, 'nerve': 30, 'lung': 20}

# Zusammengehörige Gruppen (dürfen sich berühren/ineinander übergehen)
CONNECT = {frozenset(p) for p in [('heart', 'art'), ('heart', 'pa'), ('heart', 'pv'), ('heart', 'ven'),
                                    ('pa', 'lung'), ('pv', 'lung'), ('air', 'lung'), ('cns', 'nerve')]}


MUSCLE_PRIORITY = 54   # zwischen Milz (56) und Pankreas (52)


def priority(name, layer):
    g = group_of(name, layer)
    if g == 'intercostal' or g.startswith('muscle:'): return MUSCLE_PRIORITY
    return PRIORITY.get(g, 40)


def allowed(a, b):
    """a, b: (name, layer). True = Kontakt/Übergang anatomisch gewollt."""
    ga, gb = group_of(*a), group_of(*b)
    if ga == gb and ga != 'bone': return True
    if frozenset((ga, gb)) in CONNECT: return True
    if ga == gb == 'bone':
        na, nb = a[0], b[0]
        # Rippe ↔ eigener Rippenknorpel, Rippenknorpel ↔ Brustbein
        ra = re.match(r'(left|right) (\w+) (rib|costal cartilage)$', na); rb = re.match(r'(left|right) (\w+) (rib|costal cartilage)$', nb)
        if ra and rb and ra[1] == rb[1] and ra[2] == rb[2]: return True
        stern = ('manubrium', 'body of sternum', 'xiphoid process')
        if (('costal cartilage' in na) and nb in stern) or (('costal cartilage' in nb) and na in stern): return True
    return False


def smooth01(x):
    x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)


def _adjacency(n, faces):
    e = np.vstack([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]])
    e = np.vstack([e, e[:, ::-1]])
    A = sp.coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(n, n)).tocsr()
    A.data[:] = 1.0
    deg = np.asarray(A.sum(1)).ravel(); deg[deg == 0] = 1
    return sp.diags(1.0 / deg) @ A


def _inside(tm, q):
    """Robuste Innen-Prüfung: geschlossene Meshes per Strahltest, offene per verallgemeinerter Windungszahl.
    (Das Vorzeichen über die Normale der nächsten Fläche ist an Kanten unzuverlässig.)"""
    if len(q) == 0: return np.zeros(0, bool)
    if tm.is_watertight:
        return tm.contains(q)
    return winding(q, np.asarray(tm.vertices), np.asarray(tm.faces)) > 0.5


def push_out(pos, faces, obstacles, clearance=0.0005, iters=6, spread=14, max_inside=0.6, max_depth=0.02, log=None, name=''):
    """Ecken von (pos, faces) aus allen Hindernissen herausschieben. Rückgabe: neue Positionen, Liste übersprungener Hindernisse."""
    P = pos.copy()
    W = _adjacency(len(P), faces)
    skipped = []
    active = list(obstacles)
    for it in range(iters):
        D = np.zeros_like(P); mag = np.zeros(len(P)); viol = np.zeros(len(P), bool)
        VN = trimesh.Trimesh(P, faces, process=False).vertex_normals
        for ob in list(active):
            tm = ob['tm']
            lo, hi = tm.bounds
            sel = np.all((P > lo - clearance) & (P < hi + clearance), axis=1)
            if not sel.any(): continue
            q = P[sel]
            c, d, f = trimesh.proximity.closest_point(tm, q)
            n = tm.face_normals[f]
            near = d < max_depth                              # nur oberflächennahe Ecken prüfen
            ins = np.zeros(len(q), bool); ins[near] = _inside(tm, q[near])
            if it == 0 and ins.sum() > max_inside * len(P):   # überwiegend enthalten: gewollte Lage (z. B. Gefäß im Organ)
                skipped.append(ob['name']); active.remove(ob); continue
            signed = np.where(ins, -d, d)
            bad = (signed < clearance) & near
            if not bad.any(): continue
            idx = np.where(sel)[0][bad]
            # Grundsatz „Abdruck“: die nachgiebige Oberfläche weicht entlang ihrer eigenen Innennormalen zurück, bis sie
            # aus dem Hindernis heraus ist (wie eine Impressio). Der kürzeste Weg zur Hindernisoberfläche würde sonst bei
            # dünnen oder tief steckenden Hindernissen auf die andere Seite führen (Stacheln, Umstülpen).
            disp = c[bad] + n[bad] * clearance - q[bad]           # Ersatz: nächster Punkt (für knapp außen liegende Ecken)
            inner = ins[bad]
            if inner.any():
                vn = VN[idx[inner]]
                orig = q[bad][inner]
                loc, ri, _ = tm.ray.intersects_location(orig, -vn, multiple_hits=False)
                if len(ri):
                    t = np.linalg.norm(loc - orig[ri], axis=1)
                    # Abdruck nur, wenn der Weg nach innen nicht viel länger ist als der kürzeste Weg hinaus
                    # (dünne Platten wie das Zwerchfell: dort zur nächsten Oberfläche schieben)
                    ok = (t < max_depth) & (t < 2.5 * d[bad][inner][ri] + 0.001)
                    dd = disp[inner]
                    dd[ri[ok]] = -vn[ri[ok]] * (t[ok] + clearance)[:, None]
                    disp[inner] = dd
            m = np.linalg.norm(disp, axis=1)
            take = m > mag[idx]
            D[idx[take]] = disp[take]; mag[idx[take]] = m[take]; viol[idx] = True
        # Umgekehrt: Teile des Hindernisses, die in dieser Struktur stecken (z. B. A. subclavia in der Lungenspitze,
        # Wirbelkörper in der Lunge): hier eine Rinne eindrücken (weich um den Kontakt, keine Einzelzacken).
        tmB = trimesh.Trimesh(P, faces, process=False)
        bl, bh = P.min(0), P.max(0)
        tree = None
        for ob in active:
            Av = np.asarray(ob['tm'].vertices)
            s_ = np.all((Av > bl) & (Av < bh), axis=1)
            if not s_.any(): continue
            q = Av[s_]
            cB, dB, fB = trimesh.proximity.closest_point(tmB, q)
            near = dB < max_depth
            if not near.any(): continue
            insA = np.zeros(len(q), bool); insA[near] = _inside(tmB, q[near])
            if not insA.any(): continue
            if tree is None: tree = cKDTree(P)
            nB = tmB.face_normals[fB[insA]]
            # Wanddicke von B an der Kontaktstelle: Rinne nur, wenn B dort dicker ist als die Rinne tief (sonst würde
            # z. B. das dünne Zwerchfell durch sich selbst gedrückt)
            cc_ = cB[insA]; o_ = cc_ - nB * 1e-4
            loc, ri, _ = tmB.ray.intersects_location(o_, -nB, multiple_hits=False)
            thick = np.full(len(cc_), np.inf); thick[ri] = np.linalg.norm(loc - o_[ri], axis=1)
            for cc, nn, dep, th in zip(cc_, nB, dB[insA], thick):
                if th < 2 * (dep + clearance): continue
                R = max(0.004, 1.5 * dep)
                ids = tree.query_ball_point(cc, R)
                if not ids: continue
                ids = np.array(ids)
                w = 1 - smooth01(np.linalg.norm(P[ids] - cc, axis=1) / R)
                disp = -nn[None] * ((dep + clearance) * w)[:, None]
                m = np.linalg.norm(disp, axis=1)
                take = m > mag[ids]
                D[ids[take]] = disp[take]; mag[ids[take]] = m[take]; viol[ids[take & (w > 0.6)]] = True
        if not viol.any(): break
        # weich auslaufen lassen: Verletzer fest, Nachbarn harmonisch
        F = D.copy()
        for _ in range(spread):
            F = W @ F
            F[viol] = D[viol]
        P += F
    # Gekippte Dreiecke (Falten) glätten — nur echte Umklappungen an verschobenen Stellen. (Entartete Dreiecke ohne
    # Normale zählen nicht: sie hatten das Herz sonst über die Glättung um ein Viertel schrumpfen lassen.)
    moved = np.linalg.norm(P - pos, axis=1) > 1e-6
    if moved.any():
        def normals(X):
            t = X[faces]; n = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]); l = np.linalg.norm(n, axis=1)
            return n / np.maximum(l, 1e-15)[:, None], l > 1e-12
        N0, ok0 = normals(pos)
        for _ in range(10):
            N1, ok1 = normals(P)
            flip = ok0 & ok1 & (np.einsum('ij,ij->i', N0, N1) < 0) & moved[faces].any(1)
            if not flip.any(): break
            v = np.zeros(len(P), bool); v[faces[flip].ravel()] = True
            P[v] = 0.5 * P[v] + 0.5 * (W @ P)[v]
    return P, skipped


def refine(pos, faces, sel):
    """Ausgewählte Dreiecke vierteln, Nachbarn passend halbieren/dritteln (konform, keine T-Stellen, Umlauf erhalten)."""
    f = faces[sel]
    E = np.sort(np.vstack([f[:, [0, 1]], f[:, [1, 2]], f[:, [2, 0]]]), axis=1)
    E = np.unique(E, axis=0)
    mid = {(int(a), int(b)): len(pos) + i for i, (a, b) in enumerate(E)}
    newpos = np.vstack([pos, (pos[E[:, 0]] + pos[E[:, 1]]) / 2])
    out = []
    g = lambda u, v: mid.get((u, v) if u < v else (v, u))
    for a, b, c in faces.tolist():
        ab, bc, ca = g(a, b), g(b, c), g(c, a)
        k = (ab is not None) + (bc is not None) + (ca is not None)
        if k == 0: out.append([a, b, c])
        elif k == 3: out += [[a, ab, ca], [ab, b, bc], [ca, bc, c], [ab, bc, ca]]
        elif k == 1:
            if ab is not None: out += [[a, ab, c], [ab, b, c]]
            elif bc is not None: out += [[a, b, bc], [a, bc, c]]
            else: out += [[a, b, ca], [ca, b, c]]
        else:
            if ab is None: out += [[ca, bc, c], [a, b, bc], [a, bc, ca]]
            elif bc is None: out += [[a, ab, ca], [ab, b, c], [ab, c, ca]]
            else: out += [[ab, b, bc], [a, ab, bc], [a, bc, c]]
    return newpos, np.array(out, dtype=np.int64)


def refine_near(pos, faces, obstacles, target=0.004, reach=0.001, levels=2):
    """Dreiecke in der Nähe der Hindernisse verfeinern, damit die ausweichende Oberfläche sich anschmiegen kann
    (grobe Dreiecke würden sonst zwischen ihren Ecken durch das Hindernis schneiden)."""
    nf0 = len(faces)
    for _ in range(levels):
        if len(faces) > 2.5 * nf0: break                   # Größe begrenzen (z. B. Zwerchfell berührt vieles)
        tri = pos[faces]
        fc = tri.mean(1)
        emax = np.max(np.linalg.norm(tri - np.roll(tri, 1, axis=1), axis=2), axis=1)
        near = np.zeros(len(faces), bool)
        for ob in obstacles:
            lo, hi = ob['tm'].bounds
            s = np.all((fc > lo - reach - emax[:, None]) & (fc < hi + reach + emax[:, None]), axis=1) & (emax > target) & ~near
            if not s.any(): continue
            _, d, _ = trimesh.proximity.closest_point(ob['tm'], fc[s])
            near[np.where(s)[0][d < reach + 0.6 * emax[s]]] = True   # Hindernis-Oberfläche läuft durch dieses Dreieck
        if not near.any(): break
        pos, faces = refine(pos, faces, near)
    return pos, faces


# Gefäße, die in ein Organ eintreten (Hilus, Leberpforte, retrohepatische V. cava): Übergang gewollt
HILUM = [(r'splenic (artery|vein)', r'^spleen$'), (r'(right|left) renal (artery|vein)', r'kidney$'),
         (r'inferior vena cava', r'^liver$'), (r'common hepatic artery', r'^liver$')]


def hilum(a, b):
    return any((re.search(v, a) and re.search(o, b)) or (re.search(v, b) and re.search(o, a)) for v, o in HILUM)


def resolve(parts, log=print, skip=None, progress=None, every=10):
    """parts: Liste von dicts mit name, layer, pos, faces. Ändert pos an Ort und Stelle (nachgiebigere zuerst geprüft gegen festere).
    skip(A, B) → True: Paar nicht prüfen (z. B. schon in einem früheren Lauf aufgelöst).
    progress: Datei für Zwischenstände — ein abgebrochener Lauf setzt dort fort (gleiche Teileliste vorausgesetzt)."""
    import os, pickle
    resumed = {}
    if progress and os.path.exists(progress):
        try:
            resumed = pickle.load(open(progress, 'rb'))
            if log: log(f'  Zwischenstand: {len(resumed)} Teile bereits bearbeitet')
        except Exception: resumed = {}
    def save():
        if not progress: return
        tmp = progress + '.tmp'
        pickle.dump({parts[j]['name'] + '|' + parts[j]['layer']: (parts[j]['pos'], parts[j]['faces']) for j in done}, open(tmp, 'wb'))
        os.replace(tmp, progress)
    vol = []
    for p in parts:
        try: vol.append(abs(trimesh.Trimesh(p['pos'], p['faces'], process=False).volume))
        except Exception: vol.append(0.0)
    order = sorted(range(len(parts)), key=lambda i: (-priority(parts[i]['name'], parts[i]['layer']), -vol[i]))
    done, tms, bounds = [], {}, {}
    moved = 0
    for i in order:
        B = parts[i]
        key = B['name'] + '|' + B['layer']
        if key in resumed:
            B['pos'], B['faces'] = resumed[key]
            tms[i] = trimesh.Trimesh(B['pos'], B['faces'], process=False)
            bounds[i] = (B['pos'].min(0), B['pos'].max(0))
            done.append(i)
            continue
        bl, bh = B['pos'].min(0), B['pos'].max(0)
        obst = []
        for j in done:
            A = parts[j]
            if allowed((A['name'], A['layer']), (B['name'], B['layer'])): continue
            if hilum(A['name'], B['name']): continue   # Gefäß tritt ins Organ ein: Organ nicht aushöhlen
            if skip is not None and skip(A, B): continue
            al, ah = bounds[j]
            if np.any(al > bh + 0.002) or np.any(ah < bl - 0.002): continue
            obst.append({'name': A['name'], 'tm': tms[j]})
        if obst:
            nf0 = len(B['faces'])
            if B['layer'] != 'bone':   # Knochen berühren nur Knochen (Nähte, Gelenke): dort reicht Verschieben
                # Muskeln: eine Verfeinerungsstufe (große Flächen, viele Kontakte — zwei Stufen vervierfachen die Dreiecke)
                B['pos'], B['faces'] = refine_near(B['pos'], B['faces'], obst, levels=1 if B['layer'] == 'muscle' else 2)
            new, skipped = push_out(B['pos'], B['faces'], obst)
            shift = np.linalg.norm(new - B['pos'], axis=1)
            if shift.max() > 1e-5:
                moved += 1
                if log and shift.max() > 0.002:
                    log(f'  {B["name"]}: bis {shift.max() * 1000:.1f} mm verschoben ({(shift > 1e-4).sum()} Ecken, {nf0}→{len(B["faces"])} Dreiecke)'
                        + (f'; enthält/liegt in: {", ".join(skipped)}' if skipped else ''))
            B['pos'] = new
        tms[i] = trimesh.Trimesh(B['pos'], B['faces'], process=False)
        bounds[i] = (B['pos'].min(0), B['pos'].max(0))
        done.append(i)
        if obst and len(done) % every == 0: save()
    save()
    if log: log(f'  {moved} Strukturen angepasst')
    return parts


def crossings(parts, min_depth=0.0005, max_inside=0.6, skip_hilum=True):
    """Paare, deren Oberflächen sich schneiden (ein Teil der Ecken von B steckt > min_depth in A, aber B liegt nicht
    überwiegend in A). Rückgabe: Liste (tiefe, name_a, name_b, anzahl)."""
    tms = [trimesh.Trimesh(p['pos'], p['faces'], process=False) for p in parts]
    lo = np.array([p['pos'].min(0) for p in parts]); hi = np.array([p['pos'].max(0) for p in parts])
    out = []
    for a in range(len(parts)):
        for b in range(len(parts)):
            if a == b: continue
            A, B = parts[a], parts[b]
            if allowed((A['name'], A['layer']), (B['name'], B['layer'])): continue
            if skip_hilum and hilum(A['name'], B['name']): continue
            if np.any(lo[b] > hi[a]) or np.any(hi[b] < lo[a]): continue
            P = B['pos']; sel = np.all((P > lo[a]) & (P < hi[a]), axis=1)
            if not sel.any(): continue
            q = P[sel]
            _, d, _ = trimesh.proximity.closest_point(tms[a], q)
            cand = (d > min_depth) & (d < 0.03)               # nur echte, oberflächennahe Eindringtiefen prüfen
            if not cand.any(): continue
            ins = _inside(tms[a], q[cand])
            if not ins.any(): continue
            if ins.sum() > max_inside * len(P): continue
            dd = d[cand][ins]
            out.append((float(dd.max()), A['name'], B['name'], int(len(dd))))
    return sorted(out, reverse=True)
