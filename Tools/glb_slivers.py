"""Sliver test for a GLB: fraction of triangles that are long AND thin.

sliver := longest edge > 10 * the mesh's MEDIAN longest-edge
          AND height (2*area/longest) < 0.05 * longest edge
Scale-free on purpose. A bbox-relative rule ("> half the mesh size") missed v4's shards
entirely (8/4689 flagged): they are short next to the mesh's full extent but enormous
next to its normal triangles. Long-only would flag every box face and big floor quad;
the thinness term separates a real large face from a cross-bank index sliver.
Prints one line: SLIVERS <fraction> <count>/<tris> worst_mesh=<i>. Exit 0 always.
"""
import json, struct, sys, math
d = open(sys.argv[1], "rb").read()
off, js, bn = 12, None, b""
while off + 8 <= len(d):
    l, t = struct.unpack_from("<II", d, off)
    if t == 0x4E4F534A: js = json.loads(d[off+8:off+8+l])
    elif t == 0x004E4942: bn = d[off+8:off+8+l]
    off += 8 + l
if d[:4] != b"glTF" or js is None:
    sys.exit("GLB STRUCTURE: magic=%r (expect b'glTF'), JSON chunk %s - fix the writer framing" % (d[:4], "missing" if js is None else "ok"))
CT = {5126: ("f", 4), 5123: ("H", 2), 5125: ("I", 4), 5121: ("B", 1)}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
def acc(i):
    a = js["accessors"][i]; v = js["bufferViews"][a["bufferView"]]
    f, s = CT[a["componentType"]]; n = NC[a["type"]]
    o = v.get("byteOffset", 0) + a.get("byteOffset", 0); st = v.get("byteStride", s * n)
    return [struct.unpack_from("<" + f * n, bn, o + k * st) for k in range(a["count"])]
tot = sl = 0; worst = (0.0, -1)
for mi, m in enumerate(js.get("meshes", [])):
    for p in m["primitives"]:
        if p.get("mode", 4) != 4 or "indices" not in p: continue
        P = acc(p["attributes"]["POSITION"]); I = [i[0] for i in acc(p["indices"])]
        # Median over PROPER triangles only (three distinct positions). Including
        # degenerate/zero-length triangles pulled the median down, so merely removing
        # degenerates moved the 10x cutoff: identical v6/v7 geometry scored 285 vs 36.
        Ls = []
        for k in range(0, len(I) - 2, 3):
            a, b, c = P[I[k]], P[I[k+1]], P[I[k+2]]
            if a == b or b == c or a == c: continue
            Ls.append(max(math.dist(a, b), math.dist(b, c), math.dist(c, a)))
        med = sorted(Ls)[len(Ls) // 2] if Ls else 1.0
        ms = mt = 0
        for k in range(0, len(I) - 2, 3):
            a, b, c = P[I[k]], P[I[k+1]], P[I[k+2]]
            e = [math.dist(a, b), math.dist(b, c), math.dist(c, a)]
            L = max(e)
            if L <= 0 or a == b or b == c or a == c: continue
            u = [b[j]-a[j] for j in range(3)]; w = [c[j]-a[j] for j in range(3)]
            cr = (u[1]*w[2]-u[2]*w[1], u[2]*w[0]-u[0]*w[2], u[0]*w[1]-u[1]*w[0])
            h = math.sqrt(sum(x*x for x in cr)) / L
            mt += 1
            if L > 10 * med and h < 0.05 * L: ms += 1
        tot += mt; sl += ms
        if mt and ms / mt > worst[0]: worst = (ms / mt, mi)
print("SLIVERS %.4f %d/%d worst_mesh=%d(%.4f)" % (sl / tot if tot else 0, sl, tot, worst[1], worst[0]))

# ---- vertex usage ------------------------------------------------------------
# Fraction of POSITION vertices referenced by at least one triangle, unioned per
# accessor so primitives sharing one vertex bank are counted together. v5 drew only
# 784/11,319 (6.9%): a small corner of the bank rendered, the rest silently unused.
used_by_acc = {}
count_by_acc = {}
for m in js.get("meshes", []):
    for p in m["primitives"]:
        pa = p["attributes"]["POSITION"]
        count_by_acc[pa] = js["accessors"][pa]["count"]
        if p.get("mode", 4) != 4 or "indices" not in p: continue
        used_by_acc.setdefault(pa, set()).update(i[0] for i in acc(p["indices"]))
nv = sum(count_by_acc.values())
nu = sum(len(used_by_acc.get(k, ())) for k in count_by_acc)
print("VERTUSE %.4f %d/%d" % (nu / nv if nv else 0, nu, nv))

# ---- exact duplicate triangles -----------------------------------------------
# Share of emitted triangles whose three corner POSITIONS (order-insensitive) repeat an
# earlier triangle. v7 drew 54% duplicates; that inflated every shared-edge metric and
# passed both other checks. Proper triangles only, per mesh primitive.
from collections import Counter as _C
dup = emitted = dup_same = 0
wind_seen = set()
for m in js.get("meshes", []):
    for p in m["primitives"]:
        if p.get("mode", 4) != 4 or "indices" not in p: continue
        P = acc(p["attributes"]["POSITION"]); I = [i[0] for i in acc(p["indices"])]
        seen = _C()
        for k in range(0, len(I) - 2, 3):
            a, b, c = P[I[k]], P[I[k+1]], P[I[k+2]]
            if a == b or b == c or a == c: continue
            emitted += 1
            key = tuple(sorted((a, b, c)))
            ps = [a, b, c]; r = ps.index(min(ps)); wkey = tuple(ps[r:] + ps[:r])
            if seen[key]:
                dup += 1
                if wkey in wind_seen: dup_same += 1
            seen[key] += 1
            wind_seen.add(wkey)
print("DUPTRIS %.4f %d/%d" % (dup / emitted if emitted else 0, dup, emitted))
# Informational split: a same-winding copy is always redundant; an opposite-winding copy
# may be a genuine back face (double-sided surface). Gate [0d] still uses DUPTRIS above.
print("DUPSPLIT same_winding=%d opposite_winding=%d" % (dup_same, dup - dup_same))
