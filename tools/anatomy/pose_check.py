#!/usr/bin/env python3
"""Prüft die Ragdoll-Haltungen (wie RagdollBuilder.PoseRotation) außerhalb von Unity: Vorwärtskinematik,
Lage von Händen/Knien/Füßen und eine gerenderte Vorschau der gehäuteten Figur (GLB)."""
import json, struct, sys, numpy as np, trimesh

def euler(x, y, z):  # Unity: Quaternion.Euler = Ry · Rx · Rz
    x, y, z = np.radians([x, y, z])
    Rx = np.array([[1, 0, 0], [0, np.cos(x), -np.sin(x)], [0, np.sin(x), np.cos(x)]])
    Ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    Rz = np.array([[np.cos(z), -np.sin(z), 0], [np.sin(z), np.cos(z), 0], [0, 0, 1]])
    return Ry @ Rx @ Rz

def pose_rot(pose, seg):
    side = 1 if seg.endswith('rechts') else -1
    if pose == 'Seated':
        if seg.startswith('Oberschenkel'): return euler(-90, 0, 0)
        if seg.startswith('Unterschenkel'): return euler(90, 0, 0)
        if seg.startswith('Oberarm'): return euler(-40, 0, side * 6)
        if seg.startswith('Unterarm'): return euler(-55, 0, 0) @ euler(0, -side * 80, 0)
    if pose == 'Straddling':
        if seg.startswith('Oberschenkel'): return euler(-75, 0, side * 14)
        if seg.startswith('Unterschenkel'): return euler(70, 0, -side * 6)
        if seg.startswith('Oberarm'): return euler(-62, 0, side * 7)
        if seg.startswith('Unterarm'): return euler(-18, 0, 0) @ euler(0, -side * 80, 0)
        if seg == 'Abdomen': return euler(14, 0, 0)
        if seg == 'Thorax': return euler(10, 0, 0)
    if pose == 'Standing' and seg.startswith('Oberarm'): return euler(0, 0, side * 4)
    return np.eye(3)

def load(path):
    b = open(path, 'rb').read()
    n = struct.unpack('<i', b[8:12])[0]; h = json.loads(b[12:12 + n]); off = 12 + n
    sk = h['meshes'][0]; vc, ic = sk['vertexCount'], sk['indexCount']
    P = np.frombuffer(b, '<f4', vc * 3, off).reshape(-1, 3); off += vc * 12
    I = np.frombuffer(b, '<u2' if sk['shortIndex'] else '<u4', ic, off).reshape(-1, 3); off += ic * (2 if sk['shortIndex'] else 4)
    bones = np.frombuffer(b, np.uint8, vc * 2, off).reshape(-1, 2); off += vc * 2
    w0 = np.frombuffer(b, np.uint8, vc, off) / 255.0
    return h, P, I, bones, w0

def fk(h, pose, H, hip):
    k = H / h['modelHeight']; segs = h['segments']; idx = {s['name']: i for i, s in enumerate(segs)}
    R, T = [None] * len(segs), [None] * len(segs)
    for i, s in enumerate(segs):
        j = np.array(s['joint']) * k
        if not s['parent']: R[i] = pose_rot(pose, s['name']); T[i] = np.array(hip)
        else:
            p = idx[s['parent']]; pj = np.array(segs[p]['joint']) * k
            R[i] = R[p] @ pose_rot(pose, s['name']); T[i] = T[p] + R[p] @ (j - pj)
    return k, R, T, idx

def skin(h, P, bones, w0, k, R, T):
    out = np.zeros_like(P)
    for i, s in enumerate(h['segments']):
        q = (R[i] @ (P * k - np.array(s['joint']) * k).T).T + T[i]
        m0, m1 = bones[:, 0] == i, bones[:, 1] == i
        out[m0] += q[m0] * w0[m0, None]; out[m1] += q[m1] * (1 - w0[m1, None])
    return out

if __name__ == '__main__':
    # Aufruf: pose_check.py <body.bin> [Ausgabeordner für pose_*.glb]
    h, P, I, bones, w0 = load(sys.argv[1])
    out_dir = sys.argv[2] if len(sys.argv) > 2 else '.'
    H = 1.80
    k0 = H / h['modelHeight']; J = {j['name']: np.array(j['pos']) * k0 for j in h['joints']}
    knee_to_sole = J['Knie rechts'][1] - h['floorY'] * k0; hip_to_knee = np.linalg.norm(J['Knie rechts'] - J['Hüfte rechts'])
    for pose, hipy in (('Seated', knee_to_sole + 0.05), ('Straddling', 0.85), ('Standing', knee_to_sole + hip_to_knee + 0.01)):
        k, R, T, idx = fk(h, pose, H, [0, hipy, 0])
        tip = lambda n: T[idx[n]] + R[idx[n]] @ ((np.array(h['segments'][idx[n]]['end']) - np.array(h['segments'][idx[n]]['joint'])) * k)
        world = skin(h, P, bones, w0, k, R, T)
        print(pose, 'Hand rechts', tip('Unterarm rechts').round(3), 'Knie', T[idx['Unterschenkel rechts']].round(3), 'Sprunggelenk', tip('Unterschenkel rechts').round(3),
              'Haut min y', world[:, 1].min().round(3), 'Kopf oben', world[:, 1].max().round(3))
        m = trimesh.Trimesh(world * np.array([-1, 1, 1]), I, process=False); m.visual.vertex_colors = [220, 175, 150, 255]
        m.export(f'{out_dir}/pose_{pose}.glb')
    print('Knie–Sohle', round(knee_to_sole, 3), 'Hüfte–Knie', round(hip_to_knee, 3))
