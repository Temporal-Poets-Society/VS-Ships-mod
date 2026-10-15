"""Grid ownership and metric planar geometry. No ship-specific curve functions.

A material fills the blueprint's prescribed volume. Every convex solid is clipped
to the actual three-dimensional cell grid; a cell can contain several planes.
UVs are physical tangent-plane distances, in 16 texels per block. They are
unwrapped coordinates: atlas consumers must tile/split them, never clamp them.
"""
from itertools import product
from math import atan2, ceil, floor, sqrt

EPS = 1e-8

def add(a, b): return tuple(x + y for x, y in zip(a, b))
def sub(a, b): return tuple(x - y for x, y in zip(a, b))
def mul(a, t): return tuple(x * t for x in a)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def length(a): return sqrt(dot(a, a))
def unit(a): return mul(a, 1 / length(a))
def mean(points): return tuple(sum(p[i] for p in points) / len(points) for i in range(3))

def normal(points):
    for i in range(1, len(points) - 1):
        n = cross(sub(points[i], points[0]), sub(points[i+1], points[0]))
        if length(n) > EPS: return unit(n)
    return None

def area(points):
    return sum(length(cross(sub(points[i], points[0]), sub(points[i+1], points[0]))) / 2
               for i in range(1, len(points) - 1))

def clean(points):
    result = []
    for p in points:
        if not result or length(sub(p, result[-1])) > EPS: result.append(p)
    if len(result) > 1 and length(sub(result[0], result[-1])) < EPS: result.pop()
    return result if len(result) >= 3 and area(result) > EPS else []

def outward(faces):
    centre = mean([p for face in faces for p in face])
    result = []
    for face in faces:
        n = normal(face)
        if n is not None:
            result.append(list(reversed(face)) if dot(n, sub(mean(face), centre)) < 0 else face)
    return result

def prism(x0, x1, section):
    """Convex y,z polygon extruded along x."""
    a = [(x0, y, z) for y, z in section]
    b = [(x1, y, z) for y, z in section]
    return outward([a, b] + [[a[i], a[(i+1)%len(a)], b[(i+1)%len(a)], b[i]] for i in range(len(a))])

def box(x0, y0, z0, x1, y1, z1):
    return prism(x0, x1, [(y0,z0), (y1,z0), (y1,z1), (y0,z1)])

def clip_plane(faces, axis, bound, sign):
    """Keep sign*(coordinate-bound) <= 0 and close the new cut surface."""
    result, cuts = [], []
    for face in faces:
        polygon = []
        previous = face[-1]
        dp = sign * (previous[axis] - bound)
        for current in face:
            dc = sign * (current[axis] - bound)
            if (dp <= EPS) != (dc <= EPS):
                t = dp / (dp - dc)
                hit = add(previous, mul(sub(current, previous), t))
                hit = tuple(bound if i == axis else hit[i] for i in range(3))
                polygon.append(hit)
                cuts.append(hit)
            if dc <= EPS: polygon.append(current)
            previous, dp = current, dc
        polygon = clean(polygon)
        if polygon: result.append(polygon)
    unique = {tuple(round(c, 9) for c in p): p for p in cuts}
    if len(unique) >= 3:
        points = list(unique.values())
        centre = mean(points)
        n = tuple(sign if a == axis else 0 for a in range(3))
        u = tuple(1 if a == (axis+1)%3 else 0 for a in range(3))
        v = cross(n, u)
        points.sort(key=lambda p: atan2(dot(sub(p,centre),v), dot(sub(p,centre),u)))
        points = clean(points)
        if points: result.append(points)
    return result

def clip_cell(faces, bounds):
    for axis in range(3):
        faces = clip_plane(faces, axis, bounds[axis], -1)
        faces = clip_plane(faces, axis, bounds[axis+3], 1)
        if not faces: return []
    points = [p for face in faces for p in face]
    if any(max(p[a] for p in points)-min(p[a] for p in points) < EPS for a in range(3)):
        return []
    return faces

def metric_uv(points, grain=(1,0,0)):
    n = normal(points)
    tangent = sub(grain, mul(n, dot(grain,n)))
    if length(tangent) < EPS:
        for fallback in ((1,0,0),(0,0,1),(0,1,0)):
            tangent = sub(fallback, mul(n,dot(fallback,n)))
            if length(tangent) > EPS: break
    u = unit(tangent)
    v = cross(n,u)
    return [[round(16*dot(p,u), 7), round(16*dot(p,v), 7)] for p in points]

def cell_bounds(cell, origin):
    lo = [origin[a] + cell[a] for a in range(3)]
    return lo + [c + 1 for c in lo]

def cell_id(cell): return "cell:" + ":".join(str(c) for c in cell)

def split_solid(faces, origin):
    points = [p for face in faces for p in face]
    low = [floor(min(p[a] for p in points)-origin[a]+EPS) for a in range(3)]
    high = [ceil(max(p[a] for p in points)-origin[a]-EPS) for a in range(3)]
    for cell in product(*(range(low[a], high[a]) for a in range(3))):
        bounded = clip_cell(faces, cell_bounds(cell, origin))
        if bounded: yield cell, bounded

def contains(faces, point):
    return all(dot(normal(f), sub(point, f[0])) <= EPS for f in faces)

def voxel_boxes(faces, bounds, voxel):
    count = round(1/voxel)
    for i,j,k in product(range(count), repeat=3):
        point = tuple(bounds[a] + (c+0.5)*voxel for a,c in enumerate((i,j,k)))
        if contains(faces, point):
            lo = [round(bounds[a]+c*voxel, 6) for a,c in enumerate((i,j,k))]
            yield lo + [round(c+voxel, 6) for c in lo]

def inside_box(point, b): return all(b[a] + EPS < point[a] < b[a+3] - EPS for a in range(3))
