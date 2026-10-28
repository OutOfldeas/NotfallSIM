"""Prüft anatomische Lagebeziehungen in body.bin (Unity-Koordinaten: x = rechte Körperseite, y = oben, z = vorn)."""
import os, sys, numpy as np
from anatomy_io import load, winding

h, M = load(sys.argv[1])
by = {}
for m in M:  # gleichnamige Stücke (Bahnen je Segment) zusammenfassen
    n = m['info']['name']
    if n in by:
        o = by[n]; by[n] = {'info': o['info'], 'pos': np.vstack([o['pos'], m['pos']]), 'tri': np.vstack([o['tri'], m['tri'] + len(o['pos'])])}
    else: by[n] = m
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_anatomy import _slice
J = {j['name']: np.array(j['pos']) for j in h['joints']}
res = []

def S(n): return by[n]
def c(n): return S(n)['pos'].mean(0)
def lo(n): return S(n)['pos'].min(0)
def hi(n): return S(n)['pos'].max(0)
def vol_c(n):  # Volumenschwerpunkt ist robuster als Eckenmittel
    m = S(n); p, t = m['pos'], m['tri']; a, b, cc = p[t[:, 0]], p[t[:, 1]], p[t[:, 2]]
    v = np.einsum('ij,ij->i', a, np.cross(b, cc)) / 6
    return ((a + b + cc) / 4 * v[:, None]).sum(0) / v.sum() if abs(v.sum()) > 1e-9 else p.mean(0)
def ok(cond, text, detail=''):
    res.append((bool(cond), text, detail))
def mm(x): return f'{x * 1000:.0f} mm'
def vert_y(name): return c(name)[1]

V = {f'T{i}': f'{o} thoracic vertebra' for i, o in enumerate(['first', 'second', 'third', 'fourth', 'fifth', 'sixth', 'seventh', 'eighth', 'ninth', 'tenth', 'eleventh', 'twelfth'], 1)}
V.update({f'L{i}': f'{o} lumbar vertebra' for i, o in enumerate(['first', 'second', 'third', 'fourth', 'fifth'], 1)})
V.update({f'C{i}': f'{o} cervical vertebra' for i, o in enumerate(['third', 'fourth', 'fifth', 'sixth', 'seventh'], 3)})
def level(y):
    """Wirbelhöhe zu einer y-Koordinate (nächster Wirbelkörper)."""
    k = min(V, key=lambda k: abs(vert_y(V[k]) - y)); return k

# ---------------- Seiten und Mittellinie
ok(c('liver')[0] > 0.02, 'Leber überwiegend rechts', f'x = {mm(c("liver")[0])}')
ok(c('spleen')[0] < -0.05, 'Milz links', f'x = {mm(c("spleen")[0])}')
ok(c('stomach')[0] < 0, 'Magen links der Mittellinie', f'x = {mm(c("stomach")[0])}')
ok(c('wall of heart')[0] < 0, 'Herz links der Mittellinie (zwei Drittel links)', f'x = {mm(c("wall of heart")[0])}')
ok(c('gallbladder')[0] > 0, 'Gallenblase rechts', f'x = {mm(c("gallbladder")[0])}')
ok(c('duodenum')[0] > -0.01, 'Duodenum rechts bis Mitte', f'x = {mm(c("duodenum")[0])}')
ok(c('inferior vena cava')[0] > c('descending aorta')[0], 'V. cava inferior rechts der Aorta',
   f'IVC x {mm(c("inferior vena cava")[0])}, Aorta descendens x {mm(c("descending aorta")[0])}')
ok(c('superior vena cava')[0] > c('ascending aorta')[0], 'V. cava superior rechts der Aorta ascendens')
ok(c('descending aorta')[0] < 0.01, 'Aorta descendens links paravertebral', f'x = {mm(c("descending aorta")[0])}')
for s, sx in (('right', 1), ('left', -1)):
    ok(c(f'{s} kidney')[0] * sx > 0.03, f'Niere {s} auf ihrer Seite')
    ok(c(f'{s} internal jugular vein')[0] * sx > c(f'{s} common carotid artery')[0] * sx,
       f'V. jugularis interna lateral der A. carotis communis ({s})')
    ok(c(f'{s} common carotid artery')[0] * sx > c('trachea')[0] * sx, f'A. carotis communis lateral der Trachea ({s})')
ok(c('upper lobe of right lung')[0] > 0 and c('middle lobe of lung')[0] > 0 and c('lower lobe of right lung')[0] > 0, 'Rechte Lunge (3 Lappen) rechts')
ok(c('upper lobe of left lung')[0] < 0 and c('lower lobe of left lung')[0] < 0, 'Linke Lunge (2 Lappen) links')

# ---------------- Höhen (kranio-kaudal)
ok(c('right kidney')[1] < c('left kidney')[1], 'Rechte Niere tiefer als linke (Leber)',
   f'Δ = {mm(c("left kidney")[1] - c("right kidney")[1])}')
for s in ('right', 'left'):
    k = c(f'{s} kidney'); ok(level(hi(f'{s} kidney')[1]) in ('T11', 'T12', 'L1') and level(lo(f'{s} kidney')[1]) in ('L2', 'L3', 'L4'),
                            f'Niere {s} etwa Th12–L3', f'oben {level(hi(f"{s} kidney")[1])}, unten {level(lo(f"{s} kidney")[1])}')
    ok(c(f'{s} adrenal gland')[1] > k[1] and abs(c(f'{s} adrenal gland')[0] - k[0]) < 0.05, f'Nebenniere {s} auf dem oberen Nierenpol')
ok(hi('liver')[1] > lo('wall of heart')[1] - 0.03, 'Leberkuppel reicht bis unter das Herz (Zwerchfellhöhe)',
   f'Leber oben {level(hi("liver")[1])}, Herz unten {level(lo("wall of heart")[1])}')
ok(lo('lower lobe of right lung')[1] < hi('liver')[1], 'Rechte Lungenbasis liegt der Leberkuppel auf (überlappt in der Höhe)')
ok(level(c('spleen')[1]) in ('T10', 'T11', 'T12', 'L1'), 'Milz in Höhe der 9.–11. Rippe (Th10–L1)', level(c('spleen')[1]))
for r in ('ninth', 'tenth', 'eleventh'):
    pass
br = S('bronchus')['pos']; carina_y = br[np.argmin(np.abs(br[:, 0]))][1] if len(br) else 0
tr = S('trachea'); carina = lo('trachea')[1]
ok(level(carina) in ('T3', 'T4', 'T5', 'T6'), 'Trachealbifurkation etwa Th4/5', level(carina))
ok(level(c('thyroid cartilage')[1]) in ('C3', 'C4', 'C5', 'C6'), 'Schildknorpel in Höhe C4/C5', level(c('thyroid cartilage')[1]))
bif = max(hi('right common iliac artery')[1], hi('left common iliac artery')[1])
ok(level(bif) in ('L3', 'L4', 'L5'), 'Aortenbifurkation etwa L4', level(bif))
ok(c('celiac artery')[1] > c('superior mesenteric artery')[1] > c('inferior mesenteric artery')[1], 'Abgänge: Truncus coeliacus > A. mes. sup. > A. mes. inf.')
ok(level(c('celiac artery')[1]) in ('T11', 'T12', 'L1'), 'Truncus coeliacus etwa Th12', level(c('celiac artery')[1]))
for s in ('right', 'left'):
    ok(abs(c(f'{s} renal artery')[1] - c(f'{s} kidney')[1]) < 0.04, f'A. renalis {s} in Höhe des Nierenhilus')
ok(c('urinary bladder')[1] < J['Hüftmitte'][1] + 0.05, 'Harnblase im kleinen Becken', f'Blase y {mm(c("urinary bladder")[1])} rel. Hüftmitte')
ok(c('prostate')[1] < c('urinary bladder')[1], 'Prostata unter der Blase')
ok(hi('upper lobe of right lung')[1] > hi('right first rib')[1] - 0.02 and hi('upper lobe of left lung')[1] > hi('left first rib')[1] - 0.02,
   'Lungenspitzen erreichen die 1. Rippe / Pleurakuppel')
ok(hi('upper lobe of right lung')[1] > hi('lower lobe of right lung')[1] and c('upper lobe of right lung')[1] > c('lower lobe of right lung')[1], 'Oberlappen über Unterlappen (rechts)')
ok(c('stomach')[1] < c('wall of heart')[1] and c('stomach')[1] > c('urinary bladder')[1], 'Magen unter dem Herzen, im Oberbauch')
ok(c('left lobe of thymus')[1] > c('wall of heart')[1], 'Thymus oberhalb des Herzens (vorderes Mediastinum)')
ok(hi('white matter structure of cerebral hemisphere')[1] < hi('frontal bone')[1] + 0.005, 'Großhirn unter der Schädelkalotte')

# ---------------- Vorn/hinten (z)
ok(c('esophagus')[2] < c('trachea')[2], 'Ösophagus hinter der Trachea', f'Δz = {mm(c("trachea")[2] - c("esophagus")[2])}')
for n in ('esophagus', 'descending aorta', 'inferior vena cava'):
    yv = c(n)[1]; vv = V[level(yv)]
    ok(c(n)[2] > c(vv)[2] - 0.005, f'{n} vor der Wirbelsäule', f'Δz zum Wirbel {level(yv)}: {mm(c(n)[2] - c(vv)[2])}')
for s in ('right', 'left'):
    ok(c(f'{s} kidney')[2] < c('duodenum')[2] and c(f'{s} kidney')[2] < c('colon, nsn')[2], f'Niere {s} retroperitoneal hinter Darm', f'z Niere {mm(c(f"{s} kidney")[2])}')
ok(c('pancreas, nsn')[2] < c('stomach')[2], 'Pankreas hinter dem Magen (Bursa omentalis)', f'Δz = {mm(c("stomach")[2] - c("pancreas, nsn")[2])}')
ok(c('spleen')[2] < c('stomach')[2], 'Milz posterolateral des Magens')
ok(c('wall of heart')[2] > c('descending aorta')[2] and c('wall of heart')[2] > c('esophagus')[2], 'Herz vor Ösophagus und Aorta descendens')
ok(c('left lobe of thymus')[2] > c('ascending aorta')[2], 'Thymus vor den großen Gefäßen')
ok(c('urinary bladder')[2] > c('rectum')[2], 'Rektum hinter der Blase', f'Δz = {mm(c("urinary bladder")[2] - c("rectum")[2])}')
ok(c('rectum')[2] > lo('sacrum')[2], 'Rektum vor dem Sakrum')
lrv = S('left renal vein')['pos']; ao = S('descending aorta')['pos']
near = lrv[np.abs(lrv[:, 0]) < 0.012]
ao_z = ao[np.abs(ao[:, 1] - near[:, 1].mean()) < 0.015][:, 2].max() if len(near) else np.nan
ok(len(near) and near[:, 2].mean() > ao_z - 0.01, 'V. renalis sinistra kreuzt vor der Aorta', f'Vene z {mm(near[:, 2].mean()) if len(near) else "–"}, Aorta vorn {mm(ao_z)}')
for s in ('right', 'left'):
    ok(c(f'{s} subclavian vein')[2] > c(f'{s} subclavian artery')[2], f'V. subclavia vor der A. subclavia ({s})')
pa, aa, ht = S('pulmonary artery')['pos'], S('ascending aorta')['pos'], hi('wall of heart')[1]
base = lambda q: q[(q[:, 1] > ht - 0.05) & (q[:, 1] < ht + 0.01) & (np.abs(q[:, 0]) < 0.05)]
ok(base(pa)[:, 2].max() > base(aa)[:, 2].max() - 0.005 and base(pa)[base(pa)[:, 2].argmax()][0] < base(aa)[:, 0].mean() + 0.01,
   'Truncus pulmonalis an der Herzbasis vor und links der Aorta ascendens', f'vorderster Punkt Truncus z {mm(base(pa)[:, 2].max())}, Aorta asc z {mm(base(aa)[:, 2].max())}')

# ---------------- Enthaltensein: alle Strukturen innerhalb der Haut
skin = S('Haut')
outside = []
for m in by.values():
    if m['info']['layer'] == 'skin': continue
    p = m['pos']; samp = p[np.linspace(0, len(p) - 1, min(len(p), 60)).astype(int)]
    w = winding(samp, skin['pos'], skin['tri'])
    frac = (w < 0.5).mean()
    if frac > 0.02: outside.append((m['info']['name'], frac))
ok(not outside, 'Alle Strukturen innerhalb der Haut', '; '.join(f'{n}: {f:.0%} außen' for n, f in sorted(outside, key=lambda x: -x[1])[:25]))

# ---------------- Schematische Bahnen: nicht im Knochen, Reihenfolge im Trigonum femorale (NAV)
bones = [m for m in M if m['info']['layer'] == 'bone']
long_bones = {n: S(n) for n in by if S(n)['info']['layer'] == 'bone' and any(k in n for k in ('femur', 'tibia', 'fibula', 'humerus', 'radius', 'ulna', 'hip bone', 'sacrum', 'clavicle', 'scapula', 'first rib'))}
schem = [m for m in by.values() if m['info']['layer'] in ('artery', 'vein', 'nerve') and (m['info']['name'][:2] in ('A.', 'V.', 'N.') or m['info']['name'].startswith(('Plexus', 'Rückenmark')))]
inbone = []
for m in schem:
    ring = 8; p = m['pos'].reshape(-1, ring, 3).mean(1)  # Mittellinie
    hits = []
    for bn, bm in long_bones.items():
        lo_b, hi_b = bm['pos'].min(0) - 0.01, bm['pos'].max(0) + 0.01
        sel = np.all((p > lo_b) & (p < hi_b), axis=1)
        if not sel.any(): continue
        w = winding(p[sel], bm['pos'], bm['tri'])
        if (w > 0.5).any(): hits.append(f'{bn} ({(w > 0.5).sum()}/{len(p)} Punkte)')
    if hits: inbone.append(f'{m["info"]["name"]}: ' + ', '.join(hits))
ok(not inbone, 'Schematische Bahnen verlaufen nicht durch Knochen', ' | '.join(inbone))

def path(n): return S(n)['pos'].reshape(-1, 8, 3).mean(1)
def at_y(pts, y):
    i = np.argmin(np.abs(pts[:, 1] - y)); return pts[i]
for side, sx in (('rechts', 1), ('links', -1)):
    s_en = 'right' if sx == 1 else 'left'
    y = J[f'Hüfte {side}'][1] - 0.06  # Leiste, knapp unter dem Leistenband
    nf, af, vf = at_y(path(f'N. femoralis {side}'), y), at_y(path(f'A. femoralis {side}'), y), at_y(path(f'V. femoralis {side}'), y)
    ok(nf[0] * sx > af[0] * sx > vf[0] * sx, f'Trigonum femorale {side}: N. – A. – V. von lateral nach medial',
       f'lateral-Abstand N {mm(nf[0] * sx)}, A {mm(af[0] * sx)}, V {mm(vf[0] * sx)}')
    fem = S(f'{s_en} femur')['pos']
    def bone_z(pts, y): sl = pts[np.abs(pts[:, 1] - y) < 0.01]; return sl[:, 2].min(), sl[:, 2].max(), sl[:, 0].mean()
    ymid = (J[f'Hüfte {side}'][1] + J[f'Knie {side}'][1]) / 2
    zmin, zmax, xm = bone_z(fem, ymid)
    sci = at_y(path(f'N. ischiadicus {side}'), ymid)
    ok(sci[2] < zmin, f'N. ischiadicus {side} hinter dem Femur (Oberschenkelmitte)', f'Nerv z {mm(sci[2])}, Femur hinten {mm(zmin)}')
    yk = J[f'Knie {side}'][1] + 0.05
    zmin, zmax, xm = bone_z(fem, yk)
    pop = at_y(path(f'A. poplitea / tibialis posterior {side}'), yk)
    ok(pop[2] < zmin + 0.01, f'A. poplitea {side} in der Kniekehle hinter dem Femur', f'Arterie z {mm(pop[2])}, Femur hinten {mm(zmin)}')
    hum = S(f'{s_en} humerus')['pos']
    ya = (J[f'Schulter {side}'][1] + J[f'Ellbogen {side}'][1]) / 2
    sl = hum[np.abs(hum[:, 1] - ya) < 0.01]
    bra = at_y(path(f'A. axillaris / brachialis {side}'), ya)
    ok(bra[0] * sx < sl[:, 0].mean() * sx, f'A. brachialis {side} medial des Humerus', f'Arterie {mm(bra[0] * sx)}, Humerusmitte {mm(sl[:, 0].mean() * sx)} (lateral positiv)')
    rad = at_y(path(f'N. radialis {side}'), ya)
    ok(rad[2] < sl[:, 2].mean(), f'N. radialis {side} hinter dem Humerus (Sulcus n. radialis)', f'Nerv z {mm(rad[2])}, Humerusmitte z {mm(sl[:, 2].mean())}')
    ye = J[f'Ellbogen {side}'][1]
    uln = at_y(path(f'N. ulnaris {side}'), ye)
    ok(uln[2] < J[f'Ellbogen {side}'][2] and uln[0] * sx < J[f'Ellbogen {side}'][0] * sx,
       f'N. ulnaris {side} hinter dem Epicondylus medialis', f'Nerv rel. Ellbogen: medial {mm((J[f"Ellbogen {side}"][0] - uln[0]) * sx)}, hinten {mm(J[f"Ellbogen {side}"][2] - uln[2])}')
    # Unterarmstellung: Radius distal lateral = supiniert (anatomische Normalstellung)
    r_d = S(f'{s_en} radius')['pos']; u_d = S(f'{s_en} ulna')['pos']
    rd = r_d[r_d[:, 1] < r_d[:, 1].min() + 0.02].mean(0); ud = u_d[u_d[:, 1] < u_d[:, 1].min() + 0.02].mean(0)
    sup = rd[0] * sx > ud[0] * sx
    ok(True, f'Unterarm {side}: {"supiniert (Radius distal lateral)" if sup else "proniert (Radius distal medial)"}', f'Radius {mm(rd[0] * sx)} / Ulna {mm(ud[0] * sx)} lateral, Radius z {mm(rd[2])}, Ulna z {mm(ud[2])}')
    yw = J[f'Handgelenk {side}'][1] + 0.03
    ar, au = at_y(path(f'A. radialis {side}'), yw), at_y(path(f'A. ulnaris {side}'), yw)
    sr, su = _slice(S(f'{s_en} radius')['pos'], S(f'{s_en} radius')['tri'], yw), _slice(S(f'{s_en} ulna')['pos'], S(f'{s_en} ulna')['tri'], yw)
    rr, uu = sr.reshape(-1, 2).mean(0), su.reshape(-1, 2).mean(0); rr = np.array([rr[0], yw, rr[1]]); uu = np.array([uu[0], yw, uu[1]])
    ok(np.linalg.norm(ar[[0, 2]] - rr[[0, 2]]) < np.linalg.norm(ar[[0, 2]] - uu[[0, 2]]) and np.linalg.norm(au[[0, 2]] - uu[[0, 2]]) < np.linalg.norm(au[[0, 2]] - rr[[0, 2]]),
       f'A. radialis {side} auf der Radiusseite, A. ulnaris auf der Ulnaseite (Handgelenk)')

# Rückenmark im Wirbelkanal: von Wirbelmaterial vorn und hinten umgeben, selbst nicht im Knochen
cord = path('Rückenmark'); bad = []
for k, vn in V.items():
    if k.startswith('L') and k not in ('L1', 'L2'): continue
    v = S(vn); y = c(vn)[1]
    pt = at_y(cord, y)
    sl = v['pos'][np.abs(v['pos'][:, 1] - y) < 0.012]
    if not len(sl): continue
    inside = winding(pt[None], v['pos'], v['tri'])[0] > 0.5
    front = (sl[:, 2] > pt[2]).any() and (sl[:, 2] < pt[2]).any()
    lat = (sl[:, 0] > pt[0]).any() and (sl[:, 0] < pt[0]).any()
    # Abstand zum Wirbelkörper: vorderster Punkt der Mark-Ebene
    if inside or not (front and lat): bad.append(f'{k}{" im Knochen" if inside else ""}{"" if front and lat else " nicht umschlossen"}')
ok(not bad, 'Rückenmark im Wirbelkanal (C3–L2)', ', '.join(bad))

# ---------------- Muskeln (BodyParts3D) und Leitungsbahnen in den Muskellücken
if any(m['info']['layer'] == 'muscle' for m in M):
    def ms(n, y, h=0.006):
        q = S(n)['pos']
        while h < 0.05:
            sl = q[np.abs(q[:, 1] - y) < h]
            if len(sl) >= 8: break
            h *= 1.6
        cc = sl.mean(0); cc[1] = y; return cc
    def rng(n): q = S(n)['pos']; return q[:, 1].min(), q[:, 1].max()
    passages = h.get('passages', [])
    # Bauchwand an der Flanke (Höhe LWK 3): von außen nach innen M. obliquus externus, internus, transversus
    y3 = vert_y(V['L3'])
    for s, sx in (('right', 1), ('left', -1)):
        lat = lambda n: S(n)['pos'][np.abs(S(n)['pos'][:, 1] - y3) < 0.01][:, 0].max() * sx if sx == 1 else -S(n)['pos'][np.abs(S(n)['pos'][:, 1] - y3) < 0.01][:, 0].min()
        e, i_, t_ = lat(f'{s} external oblique'), lat(f'{s} internal oblique'), lat(f'{s} transversus abdominis')
        ok(e >= i_ >= t_ - 0.002, f'Bauchwand {s}: externus außen, internus, transversus innen (Flanke L3)', f'{mm(e)} / {mm(i_)} / {mm(t_)}')
        ok(ms(f'{s} rectus abdominis', y3)[2] > ms(f'{s} transversus abdominis', y3)[2], f'M. rectus abdominis {s} vorn')
        ok(ms(f'{s} psoas major', y3)[0] * sx > c(V['L3'])[0] * sx and ms(f'{s} psoas major', y3)[2] > ms(f'{s} quadratus lumborum', y3)[2],
           f'M. psoas {s} seitlich des LWK 3, vor dem M. quadratus lumborum')
    for side, s, sx in (('rechts', 'right', 1), ('links', 'left', -1)):
        hip, knee, ankle = J[f'Hüfte {side}'], J[f'Knie {side}'], J[f'Sprunggelenk {side}']
        sh, el, wr = J[f'Schulter {side}'], J[f'Ellbogen {side}'], J[f'Handgelenk {side}']
        ty = lambda t: hip[1] + (knee[1] - hip[1]) * t
        sy = lambda t: knee[1] + (ankle[1] - knee[1]) * t
        ay = lambda t: sh[1] + (el[1] - sh[1]) * t
        fy = lambda t: el[1] + (wr[1] - el[1]) * t
        L = lambda p: p[0] * sx            # lateral positiv
        fem = lambda t: ms(f'{s} femur', ty(t))
        # Muskeln untereinander
        ok(ms(f'{s} gluteus maximus', hip[1] + 0.03)[2] < ms(f'{s} gluteus medius', hip[1] + 0.03)[2], f'M. gluteus maximus {s} oberflächlich (hinter) dem M. gluteus medius')
        ok(L(ms(f'{s} sartorius', ty(0.08))) > L(ms(f'{s} sartorius', ty(0.85))), f'M. sartorius {s} zieht von lateral oben nach medial unten')
        ok(ms(f'{s} rectus femoris', ty(0.4))[2] > fem(0.4)[2] and ms(f'long head of {s} biceps femoris', ty(0.6))[2] < fem(0.6)[2],
           f'Oberschenkel {s}: Rectus femoris vor, Biceps femoris hinter dem Femur')
        ok(L(ms(f'{s} vastus lateralis', ty(0.5))) > L(fem(0.5)) > L(ms(f'{s} vastus medialis', ty(0.75))), f'Vastus lateralis lateral, Vastus medialis medial ({s})')
        ok(L(ms(f'{s} adductor longus', ty(0.3))) < L(fem(0.3)), f'Adduktoren {s} medial')
        ok(ms(f'medial head of {s} gastrocnemius', sy(0.3))[2] < ms(f'{s} soleus', sy(0.3))[2], f'M. gastrocnemius {s} oberflächlich des M. soleus')
        ok(ms(f'{s} tibialis anterior', sy(0.4))[2] > ms(f'{s} tibia', sy(0.4))[2] - 0.01 and L(ms(f'{s} tibialis anterior', sy(0.4))) > L(ms(f'{s} tibia', sy(0.4))),
           f'M. tibialis anterior {s} vorn lateral der Tibia')
        hum = lambda t: ms(f'{s} humerus', ay(t))
        ok(ms(f'short head of {s} biceps brachii', ay(0.5))[2] > hum(0.5)[2] > ms(f'long head of {s} triceps brachii', ay(0.5))[2], f'Oberarm {s}: Bizeps vorn, Trizeps hinten')
        ok(L(ms(f'acromial part of {s} deltoid', sh[1] - 0.02)) > L(sh), f'M. deltoideus {s} lateral über dem Humeruskopf')
        ok(ms(f'{s} scalenus anterior', (rng(f'{s} scalenus anterior')[0] + rng(f'{s} scalenus anterior')[1]) / 2)[2] >
           ms(f'{s} scalenus medius', (rng(f'{s} scalenus anterior')[0] + rng(f'{s} scalenus anterior')[1]) / 2)[2], f'M. scalenus anterior {s} vor dem M. scalenus medius')
        ypm = sum(rng(f'{s} pectoralis minor')) / 2
        ok(ms(f'sternocostal part of {s} pectoralis major', ypm)[2] > ms(f'{s} pectoralis minor', ypm)[2], f'M. pectoralis major {s} vor dem M. pectoralis minor')
        ytr = vert_y(V['T5'])
        ok(ms(f'transverse part of {s} trapezius', ytr)[2] < ms(f'{s} rhomboid major', ytr)[2] + 0.003, f'M. trapezius {s} oberflächlich der Mm. rhomboidei')
        # Bahnen in den Lücken
        fa, fv = path(f'A. femoralis {side}'), path(f'V. femoralis {side}')
        a1 = at_y(fa, ty(0.08))
        ok(L(a1) < L(ms(f'{s} sartorius', ty(0.08))) and a1[2] > ms(f'{s} pectineus', ty(0.08))[2],
           f'A. femoralis {side} im Trigonum femorale: medial des M. sartorius, vor dem M. pectineus')
        a3 = at_y(fa, ty(0.55)); sart = ms(f'{s} sartorius', ty(0.55))
        ok(a3[2] < sart[2] and np.linalg.norm((a3 - sart)[[0, 2]]) < 0.045 and L(a3) < L(ms(f'{s} vastus medialis', ty(0.55))),
           f'A. femoralis {side} im Adduktorenkanal: unter dem M. sartorius, medial des M. vastus medialis', f'Abstand zum Sartorius {mm(np.linalg.norm((a3 - sart)[[0, 2]]))}')
        hz = [q for q in passages if q['pathway'] == f'A. femoralis {side}' and 'adductor magnus' in q['muscle']]
        ok(len(hz) == 1, f'A. femoralis {side}: Durchtritt durch den Hiatus adductorius (M. adductor magnus) hinterlegt')
        a5 = at_y(fa, ty(0.88))
        ok(a5[2] < fem(0.88)[2], f'A. femoralis {side} nach dem Hiatus hinter dem Femur')
        sci = path(f'N. ischiadicus {side}')
        pir = S(f'{s} piriformis')['pos']
        # Im Becken liegt der Plexus sacralis vor dem Muskel; nach dem Austritt liegt der Nerv hinter dem Muskelniveau —
        # dort, wo er hinter dem Piriformis ist, muss er unter ihm sein (sonst Foramen suprapiriforme)
        behind = []
        for q in sci:
            sl = pir[np.abs(pir[:, 0] - q[0]) < 0.008]
            if len(sl) and q[2] < sl[:, 2].min(): behind.append((q, sl[:, 1].min()))
        ok(len(behind) and all(q[1] < ymin + 0.004 for q, ymin in behind), f'N. ischiadicus {side} verlässt das Becken unter dem M. piriformis (Foramen infrapiriforme)',
           f'{len(behind)} Nervenpunkte hinter dem Muskel, höchster {mm(max(q[1] - ymin for q, ymin in behind)) if behind else "–"} relativ zum Unterrand')
        yq = S(f'{s} quadratus femoris')['pos'][:, 1].mean()
        ok(at_y(sci, yq)[2] > ms(f'{s} gluteus maximus', yq)[2], f'N. ischiadicus {side} unter (vor) dem M. gluteus maximus')
        s35 = at_y(sci, ty(0.35))
        ok(ms(f'{s} adductor magnus', ty(0.35))[2] > s35[2] > ms(f'long head of {s} biceps femoris', ty(0.35))[2] - 0.005,
           f'N. ischiadicus {side} zwischen M. adductor magnus und langem Bizepskopf')
        ap = at_y(path(f'A. poplitea / tibialis posterior {side}'), sy(0.3))
        ok(ms(f'{s} soleus', sy(0.3))[2] < ap[2] < ms(f'{s} tibialis posterior', sy(0.3))[2] + 0.01, f'A. tibialis posterior {side} zwischen M. soleus und M. tibialis posterior')
        fc = path(f'N. fibularis communis {side}')
        fibh = ms(f'{s} fibula', sy(0.07))
        ok(np.linalg.norm((at_y(fc, sy(0.07)) - fibh)[[0, 2]]) < 0.03, f'N. fibularis communis {side} am Fibulakopf')
        ok(any(q['pathway'] == f'N. fibularis communis {side}' and 'fibularis longus' in q['muscle'] for q in passages), f'N. fibularis communis {side} durch den M. fibularis longus')
        fn = at_y(path(f'N. femoralis {side}'), vert_y(V['L5']) - 0.035)
        ps, il = ms(f'{s} psoas major', fn[1]), ms(f'{s} iliacus', fn[1])
        ok(L(ps) - 0.01 < L(fn) < L(il) + 0.01, f'N. femoralis {side} in der Rinne zwischen M. psoas und M. iliacus', f'lateral: Psoas {mm(L(ps))}, Nerv {mm(L(fn))}, Iliacus {mm(L(il))}')
        # Arm
        plex = path(f'Plexus brachialis / N. medianus {side}')
        lo_sa, hi_sa = rng(f'{s} scalenus anterior')
        ysc = lo_sa + 0.35 * (hi_sa - lo_sa)
        pz = at_y(plex, ysc)
        ok(ms(f'{s} scalenus medius', ysc)[2] - 0.004 < pz[2] < ms(f'{s} scalenus anterior', ysc)[2] + 0.004, f'Plexus brachialis {side} in der Skalenuslücke (zwischen M. scalenus anterior und medius)',
           f'z: medius {mm(ms(f"{s} scalenus medius", ysc)[2])}, Plexus {mm(pz[2])}, anterior {mm(ms(f"{s} scalenus anterior", ysc)[2])}')
        sub = S(f'{s} subclavian artery')['pos']; ysa = lo_sa + 0.15 * (hi_sa - lo_sa)
        sl = sub[np.abs(sub[:, 1] - ysa) < 0.012]
        ok(len(sl) and sl[:, 2].mean() < ms(f'{s} scalenus anterior', ysa)[2] + 0.006, f'A. subclavia {side} hinter dem M. scalenus anterior (Skalenuslücke)')
        mid_med = at_y(plex, ay(0.4)); bic = ms(f'short head of {s} biceps brachii', ay(0.4))
        ok(L(mid_med) < L(bic), f'N. medianus {side} medial des M. biceps (Sulcus bicipitalis medialis)')
        pt = at_y(plex, fy(0.12))
        ph, pu = ms(f'humeral head of {s} pronator teres', fy(0.12)), ms(f'ulnar head of {s} pronator teres', fy(0.12))
        ok(min(ph[2], pu[2]) - 0.006 < pt[2] < max(ph[2], pu[2]) + 0.006, f'N. medianus {side} zwischen den Köpfen des M. pronator teres')
        rad = path(f'N. radialis {side}'); r45 = at_y(rad, ay(0.45))
        ok(r45[2] < hum(0.45)[2] and np.linalg.norm((r45 - hum(0.45))[[0, 2]]) < 0.03, f'N. radialis {side} im Sulcus n. radialis am Humerus')
        ok(any(q['pathway'] == f'N. radialis {side}' and 'supinator' in q['muscle'] for q in passages), f'N. radialis {side}: R. profundus durch den M. supinator')
        mc = path(f'N. musculocutaneus {side}')
        ok(any(q['pathway'] == f'N. musculocutaneus {side}' and 'coracobrachialis' in q['muscle'] for q in passages), f'N. musculocutaneus {side} durchbohrt den M. coracobrachialis')
        m6 = at_y(mc, ay(0.6))
        ok(ms(f'{s} brachialis', ay(0.6))[2] - 0.006 < m6[2] < ms(f'short head of {s} biceps brachii', ay(0.6))[2] + 0.006, f'N. musculocutaneus {side} zwischen M. biceps und M. brachialis')
        ar = at_y(path(f'A. radialis {side}'), fy(0.97)); fcr = ms(f'{s} flexor carpi radialis', fy(0.97))
        ok(L(ar) > L(fcr) - 0.004, f'A. radialis {side} lateral der Sehne des M. flexor carpi radialis (Pulstaststelle)')
    # Bahnen durchqueren Muskeln nur an den hinterlegten Durchtrittsstellen
    muscles = {n: S(n) for n in by if S(n)['info']['layer'] == 'muscle'}
    viol = []
    for m in schem:
        n = m['info']['name']
        pts = path(n)
        zones = [(np.array(q['point']), q['radius'], q['muscle']) for q in passages if q['pathway'] == n]
        for mn, mm_ in muscles.items():
            lo_b, hi_b = mm_['pos'].min(0), mm_['pos'].max(0)
            sel = np.all((pts > lo_b) & (pts < hi_b), axis=1)
            if not sel.any(): continue
            q = pts[sel]
            w = winding(q, mm_['pos'], mm_['tri']) > 0.5
            if not w.any(): continue
            inz = np.zeros(len(q), bool)
            for zc, zr, zm in zones:
                if zm == mn: inz |= np.linalg.norm(q - zc, axis=1) < zr
            bad = w & ~inz
            if bad.any(): viol.append(f'{n} in {mn} ({bad.sum()} Punkte)')
    ok(not viol, 'Leitungsbahnen durchqueren Muskeln nur an den Durchtrittsstellen', ' | '.join(viol[:20]) + (f' … ({len(viol)})' if len(viol) > 20 else ''))

npass = sum(r[0] for r in res)
for good, text, d in res:
    print(('OK   ' if good else 'FEHL ') + text + (f'  [{d}]' if d else ''))
print(f'\n{npass}/{len(res)} Prüfungen bestanden')
sys.exit(0 if npass == len(res) else 1)
