"""body.bin lesen (wie AnatomyFile.cs), Volumen und Windungszahl — für die Prüfskripte."""
import json, struct, numpy as np

def load(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'NSAN'
    ver, ln = struct.unpack('<ii', b[4:12])
    h = json.loads(b[12:12 + ln].decode('utf-8'))
    o = 12 + ln
    meshes = []
    for e in h['meshes']:
        n, ic = e['vertexCount'], e['indexCount']
        pos = np.frombuffer(b, '<f4', 3 * n, o).reshape(-1, 3).astype(np.float64); o += 12 * n
        if e['shortIndex']:
            idx = np.frombuffer(b, '<u2', ic, o).astype(np.int64); o += 2 * ic
        else:
            idx = np.frombuffer(b, '<u4', ic, o).astype(np.int64); o += 4 * ic
        m = {'info': e, 'pos': pos, 'tri': idx.reshape(-1, 3)}
        if e['weights']:
            m['bones'] = np.frombuffer(b, np.uint8, 2 * n, o).reshape(-1, 2).astype(int); o += 2 * n
            m['w0'] = np.frombuffer(b, np.uint8, n, o) / 255.0; o += n
        m['shapes'] = []
        for _ in e['blendShapes']:
            m['shapes'].append(np.frombuffer(b, '<f4', 3 * n, o).reshape(-1, 3)); o += 12 * n
        meshes.append(m)
    assert o == len(b), (o, len(b))
    return h, meshes

def signed_volume(pos, tri):
    a, b, c = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
    return np.einsum('ij,ij->i', a, np.cross(b, c)).sum() / 6.0


def winding(points, pos, tri, chunk=None):
    """Generalisierte Windungszahl (Jacobson 2013): ~±1 innen, ~0 außen; robust bei kleinen Löchern."""
    A, B, C = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
    out = np.zeros(len(points))
    chunk = chunk or max(1, int(3e5 // len(tri)))
    for s in range(0, len(points), chunk):
        p = points[s:s + chunk][:, None, :]
        a, b, c = A[None] - p, B[None] - p, C[None] - p
        la, lb, lc = (np.linalg.norm(x, axis=2) for x in (a, b, c))
        det = np.einsum('ijk,ijk->ij', a, np.cross(b, c))
        div = la * lb * lc + np.einsum('ijk,ijk->ij', a, b) * lc + np.einsum('ijk,ijk->ij', b, c) * la + np.einsum('ijk,ijk->ij', c, a) * lb
        out[s:s + chunk] = 2 * np.arctan2(det, div).sum(1) / (4 * np.pi)
    return np.abs(out)
