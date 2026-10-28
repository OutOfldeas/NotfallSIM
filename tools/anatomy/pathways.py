"""Verlauf der ergänzten Leitungsbahnen (Nerven, Extremitätengefäße) entlang der echten Muskellücken.

BodyParts3D enthält die Muskeln, aber nicht die peripheren Nerven und die Gefäße der Extremitäten. Die Bahnen werden
deshalb ergänzt — nicht mehr über feste Versätze zu den Gelenken, sondern über Wegpunkte, die aus der Geometrie der
umgebenden Muskeln bestimmt werden:

  P(punkt)                    fester Punkt (z. B. Ende der A. iliaca externa aus BodyParts3D)
  Gap(schätzung, radius)      freier Raum nahe der Schätzung mit größtmöglichem Abstand zu allen Strukturen
                              (Bindegewebs-/Fettraum zwischen Muskeln, z. B. Adduktorenkanal, Skalenuslücke)
  Through(muskel, schätzung)  Durchtritt durch einen Muskel (z. B. N. musculocutaneus durch den M. coracobrachialis);
                              nur dort darf die Bahn den Muskel durchqueren

Die Schätzungen kommen aus Querschnitten der Nachbarmuskeln (Mitte, Rand) auf der jeweiligen Höhe. Lagebeziehungen
werden danach in check_anatomy.py geprüft (z. B. A. femoralis unter dem M. sartorius im Adduktorenkanal).
Koordinaten: Unity, Meter, x = rechte Körperseite, y = oben, z = vorn.
"""
import numpy as np
import trimesh


class P:
    def __init__(self, p): self.p = np.asarray(p, float)


class Gap:
    def __init__(self, guess, radius=0.015, note=''):
        self.guess, self.radius, self.note = np.asarray(guess, float), radius, note


class Through:
    def __init__(self, muscle, guess, radius=0.012, zone=0.03, note=''):
        self.muscle, self.guess, self.radius, self.zone, self.note = muscle, np.asarray(guess, float), radius, zone, note


class Geo:
    """Querschnitte und Hilfspunkte auf den (bereits bereinigten) Teilen."""

    def __init__(self, parts, J):
        self.P = {q['name']: q for q in parts}
        self.J = J
        self._tm = {}

    def has(self, n): return n in self.P

    def pos(self, n): return self.P[n]['pos']

    def tm(self, n):
        if n not in self._tm:
            q = self.P[n]; self._tm[n] = trimesh.Trimesh(q['pos'], q['faces'], process=False)
        return self._tm[n]

    def slab(self, n, y, h=0.005):
        p = self.pos(n)
        while h < 0.04:
            s = p[np.abs(p[:, 1] - y) < h]
            if len(s) >= 8: return s
            h *= 1.6
        # Höhe außerhalb des Muskels: nächstgelegenes Ende
        k = np.argsort(np.abs(p[:, 1] - y))[:12]
        return p[k]

    def mid(self, n, y):
        c = self.slab(n, y).mean(0); c[1] = y; return c

    def extreme(self, n, y, d):
        s = self.slab(n, y); return s[np.argmax(s @ np.asarray(d, float))]

    def top(self, n): return self.pos(n)[:, 1].max()

    def bottom(self, n): return self.pos(n)[:, 1].min()


def _grid(center, radius, step):
    r = np.arange(-radius, radius + 1e-9, step)
    g = np.stack(np.meshgrid(r, r, r, indexing='ij'), -1).reshape(-1, 3)
    return center + g[np.linalg.norm(g, axis=1) <= radius]


def _inside_any(q, tms):
    from collide import _inside
    ins = np.zeros(len(q), bool)
    for tm in tms:
        lo, hi = tm.bounds
        sel = np.all((q > lo) & (q < hi), axis=1) & ~ins
        if sel.any(): ins[np.where(sel)[0]] = _inside(tm, q[sel])
    return ins


def _clearance(q, tms):
    d = np.full(len(q), np.inf)
    for tm in tms:
        lo, hi = tm.bounds
        sel = np.all((q > lo - 0.03) & (q < hi + 0.03), axis=1)
        if sel.any(): d[sel] = np.minimum(d[sel], trimesh.proximity.closest_point(tm, q[sel])[1])
    return d


def find_gap(spec, obstacles, need, step=0.0025, cap=0.006, weight=0.12):
    """Freier Punkt nahe der Schätzung: höchste Bewertung min(Abstand, cap) − weight · Entfernung zur Schätzung.
    obstacles: Trimesh-Liste (nur Teile in der Nähe nötig)."""
    q = _grid(spec.guess, spec.radius, step)
    near = [tm for tm in obstacles if np.all(tm.bounds[0] < spec.guess + spec.radius + 0.03)
            and np.all(tm.bounds[1] > spec.guess - spec.radius - 0.03)]
    ins = _inside_any(q, near)
    q = q[~ins]
    if not len(q): return spec.guess, 0.0
    cl = _clearance(q, near)
    score = np.minimum(cl, cap) - weight * np.linalg.norm(q - spec.guess, axis=1)
    ok = cl >= need
    if ok.any():
        i = np.where(ok)[0][np.argmax(score[ok])]
    else:
        i = int(np.argmax(cl))
    return q[i], float(cl[i])


def find_through(spec, geo, step=0.002, weight=0.15):
    """Punkt im Muskel nahe der Schätzung, möglichst mittig im Muskel (größter Abstand zu seiner Oberfläche)."""
    tm = geo.tm(spec.muscle)
    q = _grid(spec.guess, spec.radius, step)
    ins = _inside_any(q, [tm])
    if not ins.any():
        q = _grid(spec.guess, 2.5 * spec.radius, step * 2); ins = _inside_any(q, [tm])
    if not ins.any(): return spec.guess
    q = q[ins]
    d = trimesh.proximity.closest_point(tm, q)[1]
    return q[np.argmax(d - weight * np.linalg.norm(q - spec.guess, axis=1))]


# ------------------------------------------------------------------ Verläufe

def routes(geo, cen):
    """Leitungsbahnen je Seite: Liste (Name, Ebene, Wegpunkte, Radius Anfang, Radius Ende).
    cen(name): Mittelpunkt eines Knochens (Unity)."""
    J = geo.J
    out = []
    for side, s, sx in (('rechts', 'right', 1), ('links', 'left', -1)):
        hip, knee, ankle = J[f'Hüfte {side}'], J[f'Knie {side}'], J[f'Sprunggelenk {side}']
        sh, el, wr = J[f'Schulter {side}'], J[f'Ellbogen {side}'], J[f'Handgelenk {side}']
        X = np.array([sx, 0, 0])                   # nach lateral
        ty = lambda t: hip[1] + (knee[1] - hip[1]) * t          # Oberschenkel 0 = Hüfte … 1 = Knie
        sy = lambda t: knee[1] + (ankle[1] - knee[1]) * t       # Unterschenkel
        ay = lambda t: sh[1] + (el[1] - sh[1]) * t              # Oberarm
        fy = lambda t: el[1] + (wr[1] - el[1]) * t              # Unterarm
        m = geo.mid
        mean = lambda *pts: np.mean(np.array(pts), axis=0)
        femur = f'{s} femur'
        ext = geo.pos(f'{s} external iliac artery'); ext_end = ext[ext[:, 1].argmin()]
        sub = geo.pos(f'{s} subclavian artery'); sub_end = sub[(sub[:, 0] * sx).argmax()]

        # ---- A. femoralis: Lacuna vasorum → Trigonum femorale (zwischen M. sartorius und M. adductor longus, auf
        # M. iliopsoas/pectineus) → Adduktorenkanal (unter dem M. sartorius, zwischen M. vastus medialis und den
        # Adduktoren) → Hiatus adductorius (durch den M. adductor magnus) → Fossa poplitea (dem Femur anliegend)
        y1, y2, y3, y4, y5 = ty(0.08), ty(0.30), ty(0.55), ty(0.74), ty(0.93)
        fa = [P(ext_end),
              Gap(mean(m(f'{s} pectineus', y1), m(f'{s} psoas major', y1), m(f'{s} sartorius', y1)) + [0, 0, 0.012], 0.02, 'Trigonum femorale'),
              Gap(mean(m(f'{s} sartorius', y2), m(f'{s} adductor longus', y2), m(f'{s} vastus medialis', y2)), 0.02, 'Spitze des Trigonum femorale'),
              Gap(mean(m(f'{s} sartorius', y3), m(f'{s} vastus medialis', y3), m(f'{s} adductor magnus', y3)), 0.02, 'Adduktorenkanal'),
              Through(f'{s} adductor magnus', mean(m(f'{s} adductor magnus', y4), m(femur, y4)), 0.015, 0.035, 'Hiatus adductorius'),
              Gap(m(femur, y5) + [0, 0, -0.03], 0.018, 'Fossa poplitea, dem Femur anliegend')]
        out.append((f'A. femoralis {side}', 'artery', fa, 0.0045, 0.0038))
        # ---- A. poplitea → A. tibialis posterior: zwischen den Gastroknemiusköpfen in die Tiefe, unter dem
        # Arcus tendineus m. solei in die tiefe Flexorenloge (zwischen M. soleus und M. tibialis posterior), hinter dem
        # Innenknöchel
        z1, z2, z3 = sy(0.06), sy(0.3), sy(0.65)
        pop = [Gap(m(femur, y5) + [0, 0, -0.03], 0.018, 'Fossa poplitea'),
               Gap(mean(m(f'medial head of {s} gastrocnemius', z1), m(f'lateral head of {s} gastrocnemius', z1), m(f'{s} tibia', z1)), 0.02, 'zwischen den Gastroknemiusköpfen'),
               Gap(mean(m(f'{s} soleus', z2), m(f'{s} tibialis posterior', z2)), 0.018, 'tiefe Flexorenloge'),
               Gap(mean(m(f'{s} soleus', z3), m(f'{s} flexor digitorum longus', z3), m(f'{s} tibialis posterior', z3)), 0.018, 'tiefe Flexorenloge distal'),
               Gap(ankle + X * -0.025 + [0, 0.01, -0.012], 0.012, 'hinter dem Malleolus medialis'),
               P(J[f'Ferse {side}'] + [sx * -0.02, 0.03, 0.03])]
        out.append((f'A. poplitea / tibialis posterior {side}', 'artery', pop, 0.0035, 0.0022))
        # ---- V. femoralis: begleitet die Arterie (im Trigonum medial, in der Kniekehle oberflächlicher)
        fv = [P(ext_end + [sx * -0.012, 0, -0.005]),
              Gap(mean(m(f'{s} pectineus', y1), m(f'{s} adductor longus', y1)) + [0, 0, 0.012] - X * 0.004, 0.02, 'medial der A. femoralis'),
              Gap(mean(m(f'{s} sartorius', y2), m(f'{s} adductor longus', y2), m(f'{s} vastus medialis', y2)) - [0, 0, 0.008], 0.02),
              Gap(mean(m(f'{s} sartorius', y3), m(f'{s} vastus medialis', y3), m(f'{s} adductor magnus', y3)) - [0, 0, 0.008], 0.02),
              Through(f'{s} adductor magnus', mean(m(f'{s} adductor magnus', y4), m(femur, y4)) - [0, 0, 0.006], 0.015, 0.035, 'Hiatus adductorius'),
              Gap(m(femur, y5) + [0, 0, -0.042], 0.018, 'Fossa poplitea, oberflächlich der Arterie')]
        out.append((f'V. femoralis {side}', 'vein', fv, 0.005, 0.0042))

        # ---- N. ischiadicus → N. tibialis: Plexus sacralis vor dem M. piriformis → Foramen infrapiriforme → zwischen
        # Trochanter major und Tuber ischiadicum unter dem M. gluteus maximus, hinter den Mm. gemelli/obturator internus/
        # quadratus femoris → Oberschenkel hinter dem M. adductor magnus, unter dem langen Bizepskopf → Kniekehle
        # (oberflächlich der Gefäße) → zwischen den Gastroknemiusköpfen → unter dem M. soleus → hinter dem Innenknöchel
        pir = f'{s} piriformis'
        # Unterrand des Muskels dort, wo er durch das Foramen ischiadicum majus zieht (mediale Hälfte); der tiefste
        # Punkt insgesamt wäre der Ansatz am Trochanter major
        pp = geo.pos(pir); medial = pp[pp[:, 0] * sx < np.median(pp[:, 0] * sx)]
        pir_low = medial[medial[:, 1].argmin()]
        sac = cen('sacrum')
        isch = [P(sac + [sx * 0.05, 0.0, -0.02]),
                Gap(pir_low + [0, -0.012, -0.004], 0.014, 'Foramen infrapiriforme'),
                Gap(m(f'{s} quadratus femoris', geo.P[f'{s} quadratus femoris']['pos'][:, 1].mean()) + [0, 0, -0.016], 0.018, 'hinter dem M. quadratus femoris'),
                Gap(mean(m(f'{s} adductor magnus', ty(0.35)), m(f'long head of {s} biceps femoris', ty(0.35))), 0.018, 'hinter dem M. adductor magnus'),
                Gap(mean(m(f'long head of {s} biceps femoris', ty(0.7)), m(f'{s} semimembranosus', ty(0.7))), 0.018, 'zwischen Bizeps und Semimembranosus'),
                Gap(m(femur, y5) + [0, 0, -0.052], 0.018, 'Kniekehle, oberflächlich der Gefäße'),
                Gap(mean(m(f'medial head of {s} gastrocnemius', sy(0.12)), m(f'lateral head of {s} gastrocnemius', sy(0.12))), 0.018, 'zwischen den Gastroknemiusköpfen'),
                Gap(mean(m(f'{s} soleus', sy(0.45)), m(f'{s} tibialis posterior', sy(0.45))) + [0, 0, -0.004], 0.018, 'unter dem M. soleus'),
                Gap(ankle + X * -0.028 + [0, 0.012, -0.02], 0.012, 'hinter dem Malleolus medialis')]
        out.append((f'N. ischiadicus {side}', 'nerve', isch, 0.0055, 0.003))
        # ---- N. fibularis communis: am medialen Rand des M. biceps femoris zum Fibulakopf, um den Fibulahals, durch
        # den Ursprung des M. fibularis longus in die Extensorenloge (typische Verletzung bei Fibulakopffraktur)
        fib = f'{s} fibula'
        yfh = sy(0.07)
        fc = [Gap(mean(m(f'long head of {s} biceps femoris', ty(0.85)), m(f'{s} semimembranosus', ty(0.85))), 0.016),
              Gap(m(f'short head of {s} biceps femoris', ty(0.97)) - X * 0.008, 0.016, 'medialer Rand der Bizepssehne'),
              Gap(geo.extreme(fib, yfh, [0, 0, -1]) + X * 0.006 + [0, 0, -0.004], 0.012, 'hinter dem Fibulakopf'),
              Through(f'{s} fibularis longus', geo.extreme(fib, sy(0.12), X) + X * 0.006, 0.012, 0.03, 'Durchtritt durch den M. fibularis longus'),
              Gap(mean(m(f'{s} tibialis anterior', sy(0.3)), m(f'{s} extensor digitorum longus', sy(0.3))) + [0, 0, -0.006], 0.016, 'Extensorenloge')]
        out.append((f'N. fibularis communis {side}', 'nerve', fc, 0.003, 0.0022))
        # ---- N. femoralis: im M. psoas (Plexus lumbalis) → Rinne zwischen M. psoas und M. iliacus → Lacuna musculorum
        # lateral der A. femoralis → Trigonum femorale
        l4, l5 = cen('fourth lumbar vertebra'), cen('fifth lumbar vertebra')
        yg = l5[1] - 0.035
        fn = [Through(f'{s} psoas major', l4 + [sx * 0.035, 0, -0.005], 0.012, 0.03, 'Plexus lumbalis im M. psoas'),
              Gap(mean(m(f'{s} psoas major', yg), m(f'{s} iliacus', yg)) + [0, 0, 0.008], 0.018, 'Rinne zwischen M. psoas und M. iliacus'),
              Gap(ext_end + X * 0.02 + [0, 0.004, -0.004], 0.014, 'Lacuna musculorum, lateral der Arterie'),
              Gap(mean(m(f'{s} sartorius', ty(0.12)), m(f'{s} iliacus', ty(0.05))) + [0, 0, 0.01], 0.016, 'Trigonum femorale lateral')]
        out.append((f'N. femoralis {side}', 'nerve', fn, 0.003, 0.0025))

        # ---- Arm: A. axillaris/brachialis: hinter dem M. pectoralis minor durch die Achsel → Sulcus bicipitalis medialis
        # (medial des M. biceps, vor dem Caput mediale des M. triceps) → Fossa cubitalis medial der Bizepssehne
        pm = f'{s} pectoralis minor'
        ypm = 0.5 * (geo.top(pm) + geo.bottom(pm))
        bic = f'short head of {s} biceps brachii'
        tri_m = f'medial head of {s} triceps brachii'
        groove = lambda t: mean(m(bic, ay(t)), m(tri_m, ay(t)), m(f'{s} brachialis', ay(t))) - X * 0.012
        ax = [P(sub_end),
              Gap(m(pm, ypm) + [0, 0, -0.022], 0.02, 'Achsel, hinter dem M. pectoralis minor'),
              Gap(groove(0.3), 0.018, 'Sulcus bicipitalis medialis'),
              Gap(groove(0.65), 0.018, 'Sulcus bicipitalis medialis'),
              Gap(el + [-sx * 0.008, 0, 0.022], 0.012, 'Fossa cubitalis')]
        out.append((f'A. axillaris / brachialis {side}', 'artery', ax, 0.0035, 0.003))
        br, fcr = f'{s} brachioradialis', f'{s} flexor carpi radialis'
        out.append((f'A. radialis {side}', 'artery',
                    [Gap(el + [-sx * 0.008, 0, 0.022], 0.012),
                     Gap(m(br, fy(0.3)) - X * 0.012 + [0, 0, -0.006], 0.016, 'unter dem M. brachioradialis'),
                     Gap(mean(m(br, fy(0.75)), m(fcr, fy(0.75))), 0.014, 'zwischen M. brachioradialis und M. flexor carpi radialis'),
                     Gap(m(fcr, fy(0.97)) + X * 0.01, 0.012, 'lateral der Sehne des M. flexor carpi radialis')], 0.0022, 0.0018))
        pt_u, fdp = f'ulnar head of {s} pronator teres', f'{s} flexor digitorum profundus'
        fcu_u, fcu_h = f'ulnar head of {s} flexor carpi ulnaris', f'humeral head of {s} flexor carpi ulnaris'
        out.append((f'A. ulnaris {side}', 'artery',
                    [Gap(el + [-sx * 0.008, 0, 0.022], 0.012),
                     Gap(m(pt_u, fy(0.12)) + [0, 0, -0.008], 0.014, 'unter dem Caput ulnare des M. pronator teres'),
                     Gap(mean(m(fcu_u, fy(0.55)), m(fdp, fy(0.55))), 0.016, 'zwischen M. flexor carpi ulnaris und M. flexor digitorum profundus'),
                     Gap(wr + [-sx * 0.02, 0, 0.008], 0.012, 'radial des Os pisiforme')], 0.0022, 0.0018))
        # ---- Plexus brachialis → N. medianus: Skalenuslücke (zwischen M. scalenus anterior und medius) → unter der
        # Klavikula → Achsel → Sulcus bicipitalis medialis → zwischen den Köpfen des M. pronator teres → zwischen
        # M. flexor digitorum superficialis und profundus → Karpaltunnel
        sa, sm_ = f'{s} scalenus anterior', f'{s} scalenus medius'
        ysc = geo.bottom(sa) + 0.35 * (geo.top(sa) - geo.bottom(sa))
        c6 = cen('sixth cervical vertebra')
        pt_h = f'humeral head of {s} pronator teres'
        fds = f'humeroulnar head of {s} flexor digitorum superficialis'
        med = [P(c6 + [sx * 0.03, 0, 0.0]),
               Gap(mean(m(sa, ysc), m(sm_, ysc)), 0.012, 'Skalenuslücke'),
               Gap(sub_end + [0, 0.01, -0.01], 0.015, 'unter der Klavikula'),
               Gap(m(pm, ypm) + [0, 0, -0.03], 0.02, 'Achsel'),
               Gap(groove(0.4) - X * 0.004, 0.018, 'Sulcus bicipitalis medialis'),
               Gap(el + [-sx * 0.015, 0, 0.02], 0.012, 'Fossa cubitalis medial der Arterie'),
               Gap(mean(m(pt_h, fy(0.12)), m(pt_u, fy(0.12))), 0.012, 'zwischen den Köpfen des M. pronator teres'),
               Gap(mean(m(fds, fy(0.5)), m(fdp, fy(0.5))), 0.016, 'zwischen oberflächlichem und tiefem Fingerbeuger'),
               Gap(wr + [0, 0.01, 0.012], 0.012, 'Karpaltunnel'),
               P(wr + [0, -0.06, 0.01])]
        out.append((f'Plexus brachialis / N. medianus {side}', 'nerve', med, 0.0035, 0.002))
        # ---- N. ulnaris: medial am Oberarm → hinter dem Epicondylus medialis (Sulcus n. ulnaris) → zwischen den Köpfen
        # des M. flexor carpi ulnaris → unter dem M. flexor carpi ulnaris auf dem M. flexor digitorum profundus
        hum = f'{s} humerus'
        epi = geo.extreme(hum, el[1] + 0.012, -X)
        out.append((f'N. ulnaris {side}', 'nerve',
                    [Gap(m(pm, ypm) + [0, 0, -0.035] - X * 0.006, 0.02, 'Achsel'),
                     Gap(m(tri_m, ay(0.6)) - X * 0.01 + [0, 0, 0.008], 0.018, 'medial am Caput mediale des M. triceps'),
                     Gap(epi + [0, 0, -0.012], 0.01, 'Sulcus n. ulnaris'),
                     Gap(mean(m(fcu_h, fy(0.1)), m(fcu_u, fy(0.1))), 0.012, 'zwischen den Köpfen des M. flexor carpi ulnaris'),
                     Gap(mean(m(fcu_u, fy(0.5)), m(fdp, fy(0.5))), 0.016, 'unter dem M. flexor carpi ulnaris'),
                     Gap(wr + [-sx * 0.022, 0.005, 0.005], 0.012)], 0.0025, 0.0018))
        # ---- N. radialis: hinter der A. axillaris → Sulcus n. radialis (am Humerus zwischen Caput laterale und mediale
        # des M. triceps) → zwischen M. brachialis und M. brachioradialis → vor dem Epicondylus lateralis → R. profundus
        # durch den M. supinator (Frohse-Arkade) in die Extensorenloge
        tri_l = f'lateral head of {s} triceps brachii'
        sup = f'{s} supinator'
        out.append((f'N. radialis {side}', 'nerve',
                    [Gap(m(pm, ypm) + [0, 0, -0.04], 0.02, 'hinter der A. axillaris'),
                     Gap(mean(m(tri_l, ay(0.45)), m(tri_m, ay(0.45)), m(hum, ay(0.45))), 0.014, 'Sulcus n. radialis'),
                     Gap(mean(m(f'{s} brachialis', ay(0.82)), m(br, ay(0.82))), 0.014, 'zwischen M. brachialis und M. brachioradialis'),
                     Gap(el + [sx * 0.022, 0, 0.012], 0.012, 'vor dem Epicondylus lateralis'),
                     Through(sup, m(sup, fy(0.12)), 0.012, 0.03, 'R. profundus durch den M. supinator'),
                     Gap(m(f'{s} extensor digitorum', fy(0.4)) + [0, 0, 0.008], 0.016, 'Extensorenloge')], 0.0025, 0.0018))
        # ---- N. musculocutaneus: aus dem Fasciculus lateralis → durch den M. coracobrachialis → zwischen M. biceps und
        # M. brachialis → lateral der Bizepssehne als N. cutaneus antebrachii lateralis
        cb = f'{s} coracobrachialis'
        ycb = geo.bottom(cb) + 0.6 * (geo.top(cb) - geo.bottom(cb))
        out.append((f'N. musculocutaneus {side}', 'nerve',
                    [Gap(m(pm, ypm) + [0, 0, -0.025] + X * 0.008, 0.02, 'Fasciculus lateralis'),
                     Through(cb, m(cb, ycb), 0.012, 0.03, 'Durchtritt durch den M. coracobrachialis'),
                     Gap(mean(m(bic, ay(0.6)), m(f'{s} brachialis', ay(0.6))), 0.016, 'zwischen M. biceps und M. brachialis'),
                     Gap(el + [sx * 0.026, 0.01, 0.02], 0.014, 'lateral der Bizepssehne'),
                     Gap(el + (wr - el) * 0.3 + X * 0.024 + [0, 0, 0.01], 0.014, 'N. cutaneus antebrachii lateralis')], 0.0022, 0.0016))
    return out


def resolve_waypoints(specs, geo, obstacles, need):
    """Wegpunkte bestimmen. Rückgabe: Punkte, Durchtritte [(Muskel, Punkt, Zone)] und Protokoll."""
    pts, passages, log = [], [], []
    for sp in specs:
        if isinstance(sp, P):
            pts.append(sp.p); log.append(('P', None, 0.0))
        elif isinstance(sp, Gap):
            p, cl = find_gap(sp, obstacles, need)
            pts.append(p); log.append(('Gap', sp.note, cl))
        elif isinstance(sp, Through):
            p = find_through(sp, geo)
            pts.append(p); passages.append((sp.muscle, p, sp.zone, sp.note)); log.append(('Through', sp.note, 0.0))
    return np.array(pts), passages, log
