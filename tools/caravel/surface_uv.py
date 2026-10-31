"""Metric wood UVs: a texel is a physical distance along the face, including oblique end and corner faces."""
import math


def wood_uv(point, normal, grain, origin=(0, 0, 0), units_per_block=1000):
    length = math.sqrt(sum(v*v for v in normal))
    n = [v / length for v in normal]
    u = [float(a == grain) - n[a] * n[grain] for a in range(3)]
    ul = math.sqrt(sum(v*v for v in u))
    if ul < 1e-8:
        axis = min(range(3), key=lambda a: abs(n[a]))
        u = [float(a == axis) - n[a]*n[axis] for a in range(3)]
        ul = math.sqrt(sum(v*v for v in u))
    u = [v/ul for v in u]
    v = [n[1]*u[2]-n[2]*u[1], n[2]*u[0]-n[0]*u[2], n[0]*u[1]-n[1]*u[0]]
    axis = max(range(3), key=lambda a: abs(n[a]))
    cross_axis = next((a for a in range(3) if a != axis and a != grain), 2)
    sign = -1 if cross_axis == 2 else 1
    if v[cross_axis]*sign < 0:
        v = [-q for q in v]
    p = [(point[a]-origin[a])*16/units_per_block for a in range(3)]
    return sum(p[a]*u[a] for a in range(3)), sum(p[a]*v[a] for a in range(3))
