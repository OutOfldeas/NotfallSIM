"""Rechnet AnatomyPatient (Rückenlage auf der Trage) wie Unity nach: Teile starr am Segment, Haut gewichtet."""
import os, sys, numpy as np, trimesh
from anatomy_io import load, winding
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pose_check import euler  # Unity Quaternion.Euler (Ry·Rx·Rz)

def pose_patient(h, M, height=1.80, mattress=0.84, arm=(14, -4), fore=0, breath=None):
    segs = h['segments']; idx = {s['name']: i for i, s in enumerate(segs)}
    scale = height / h['modelHeight']
    R0 = euler(0, 0, 180) @ euler(90, 0, 0)
    local = {i: np.eye(3) for i in range(len(segs))}
    local[idx['Oberarm rechts']] = euler(arm[0], 0, arm[1]); local[idx['Oberarm links']] = euler(arm[0], 0, -arm[1])
    local[idx['Unterarm rechts']] = euler(fore, 0, 0); local[idx['Unterarm links']] = euler(fore, 0, 0)
    R, T = [None] * len(segs), [None] * len(segs)
    for i, s in enumerate(segs):
        j = np.array(s['joint'])
        if not s['parent']: R[i] = local[i]; T[i] = j
        else:
            p = idx[s['parent']]; R[i] = R[p] @ local[i]; T[i] = T[p] + R[p] @ (j - np.array(segs[p]['joint']))
    skin = M[0]; P = skin['pos'].copy()
    if breath:
        for k, wgt in breath.items(): P += skin['shapes'][skin['info']['blendShapes'].index(k)] * wgt
    minZ = skin['pos'][:, 2].min()
    root = np.array([0, mattress - minZ * scale, 0])
    world = lambda q: root + scale * (R0 @ q.T).T
    def part(m):
        i = m['info']['segment']; j = np.array(segs[i]['joint'])
        return world(T[i] + (R[i] @ (m['pos'] - j).T).T)
    out = np.zeros_like(P)
    for i, s in enumerate(segs):
        q = T[i] + (R[i] @ (P - np.array(s['joint'])).T).T
        b, w0 = skin['bones'], skin['w0']
        out += q * (np.where(b[:, 0] == i, w0, 0) + np.where(b[:, 1] == i, 1 - w0, 0))[:, None]
    return world(out), part, (R, T, world, idx)

if __name__ == '__main__':
    h, M = load(sys.argv[1])
    skinW, part, (R, T, world, idx) = pose_patient(h, M)
    print('Haut: tiefster Punkt y = %.3f (Matratze 0.840), Kopf z = %.3f, Füße z = %.3f, Breite x %.3f…%.3f' % (skinW[:, 1].min(), skinW[:, 2].max(), skinW[:, 2].min(), skinW[:, 0].min(), skinW[:, 0].max()))
    low = []
    for m in M[1:]:
        p = part(m)
        if p[:, 1].min() < 0.84 - 0.002: low.append((m['info']['name'], p[:, 1].min()))
    print('Teile unter der Liegefläche:', low[:10] or 'keine')
    # Arme: Teile der Armsegmente und Schultergürtel in der verformten Haut
    segs = h['segments']
    test = [m for m in M[1:] if m['info']['segment'] in range(4, 8) or any(k in m['info']['name'] for k in ('scapula', 'clavicle'))]
    bad = []
    for m in test:
        p = part(m); s = p[np.linspace(0, len(p) - 1, min(len(p), 25)).astype(int)]
        w = winding(s, skinW, M[0]['tri'])
        f = (w < 0.5).mean()
        if f > 0.04: bad.append(f'{m["info"]["name"]} {f:.0%}')
    print('Arm-/Schulterteile außerhalb der gelagerten Haut:', ', '.join(bad) or 'keine')
    # Röhren über Gelenke: Abstand zur Lage, wenn jeder Ring dem Segment folgte, zu dem er gehört
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    J = {j['name']: np.array(j['pos']) for j in h['joints']}
    print('Bahnstücke mit Versatz > 4 mm an Gelenken:')
    for m in M[1:]:
        n = m['info']['name']
        if not n.startswith(('A. ', 'V. ', 'N. ', 'Plexus', 'Rückenmark')): continue
        rings = m['pos'].reshape(-1, 8, 3).mean(1)
        # nächstes Segment je Ring (Abstand zur Segmentachse)
        best = []
        for q in rings:
            d = []
            for i, s in enumerate(segs):
                A, B = np.array(s['joint']), np.array(s['end']); ab = B - A
                t = np.clip((q - A) @ ab / (ab @ ab), 0, 1); d.append(np.linalg.norm(q - (A + t * ab)) / max(s['radius'], 0.02))
            best.append(int(np.argmin(d)))
        i0 = m['info']['segment']
        pos_assigned = world(T[i0] + (R[i0] @ (rings - np.array(segs[i0]['joint'])).T).T)
        pos_true = np.array([world(T[b] + R[b] @ (q - np.array(segs[b]['joint']))) for q, b in zip(rings, best)])
        err = np.linalg.norm(pos_assigned - pos_true, axis=1).max()
        if err > 0.004: print(f'  {n:42s} Segment {segs[i0]["name"]:20s} max. Versatz {err * 1000:5.1f} mm')
