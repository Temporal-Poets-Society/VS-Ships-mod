"""
Ship geometry library for Hansa.

A ship design is built on a voxel grid in ship-local coordinates (blocks):
  +X = forward (bow), +Y = up, +Z = starboard (right when facing the bow). Origin = waterline, midships.

From one design this produces:
  - render shapes (Vintage Story shape JSON): exterior faces, interior faces (lit darker in game), one file per sail
  - the ship definition JSON read by the mod: collision voxels, rooms, fixtures, sails, lights
  - preview PNGs (flat-shaded, painter's algorithm) for checking proportions without starting the game

Coordinates in shape files are ship-local * 16, so the model origin is the ship's origin.
"""
import base64
import contextlib
import json
import math
import os
import sys
import zlib

import numpy as np

VOX = 0.25  # voxel edge in blocks (4 shape units)

FACES = ("east", "west", "up", "down", "south", "north")  # +x -x +y -y +z -z
FACE_DIRS = ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))


SRC_TRACE = os.environ.get("SHIPGEN_SRC", "")   # (a folder, or "1": tools/shipgen/__pycache__)


def _src_of_call():
    """The innermost caller outside shipgen.py (function and line): what made a quad (SHIPGEN_SRC=1)."""
    f = sys._getframe(2)
    out = []
    while f is not None and len(out) < 2:
        name = os.path.basename(f.f_code.co_filename)
        if name not in ("shipgen.py", "planking.py"):
            out.append(f"{f.f_code.co_name}:{f.f_lineno}")
        f = f.f_back
    return " < ".join(out)


class Material:
    def __init__(self, code, textures, color, solid=True, opaque=True, render=True, glow=0, grain=False):
        self.code = code
        self.textures = textures      # list of asset paths (variants)
        self.color = color            # preview colour (r, g, b)
        self.solid = solid            # collides with players
        self.opaque = opaque          # hides neighbouring faces
        self.render = render
        self.glow = glow
        # wood: vanilla's wood textures have their grain along u, so a round pole (a mast, a stanchion) is textured
        # with u along its length instead of round it
        self.grain = grain


class Design:
    def __init__(self, code, x0, x1, y0, y1, z0, z1):
        self.code = code
        self.o = np.array([x0, y0, z0], dtype=float)
        # how far translate_y has moved her: the voxels' textures stay on the design's whole-block tiles (_uv_aabb)
        self.ty = 0.0
        self.n = (int(round((x1 - x0) / VOX)), int(round((y1 - y0) / VOX)), int(round((z1 - z0) / VOX)))
        self.grid = np.zeros(self.n, dtype=np.uint8)
        self.materials = [None]  # index 0 = air
        self.mat_index = {}
        nx, ny, nz = self.n
        self.xs = x0 + (np.arange(nx) + 0.5) * VOX
        self.ys = y0 + (np.arange(ny) + 0.5) * VOX
        self.zs = z0 + (np.arange(nz) + 0.5) * VOX
        self.X, self.Y, self.Z = np.meshgrid(self.xs, self.ys, self.zs, indexing="ij")
        self.elements = []   # free (non-voxel) elements: dicts with group, mat, center, R, size
        self.quads = []      # skin mesh: dicts with group, mat, pts (4 corners, counter-clockwise seen from outside), uv
        # voxel faces that look into `hide_toward` cells (from cells in `hide_applies`) aren't drawn: the smooth skin
        # mesh covers the outside of the hull, the voxels still draw its inside and give collision
        self.hide_toward = None
        self.hide_applies = None
        self.ladders = []
        self.rooms = []
        self.fixtures = []
        self.lights = []
        self.sails = []
        self.board_points = []
        self.moving_parts = []   # mesh groups "part:<code>" turned by the mod (wheel, rudder)
        self.props = {}
        # the planking's thickness (outside to the ceiling), for the mod's holes through it (None: the mod's default)
        self.hull_thickness = None
        # (0.3.8) paint zones: what the quads added now are (Design.paint), and once sorted (the carrack's paint_zones):
        # each quad's zone ("pz", an index into paint_zone_list) and each voxel face's ({(box index, face): zone})
        self.paint_tag = None
        self.paint_zone_list = None
        self.paint_voxel = None
        self.paint_voxel_cells = {}

    def translate_y(self, dy):
        """
        Moves everything up by dy (down, dy < 0) before writing: a design drawn around another waterline (a deeper
        ship drawn in the first one's heights) is lowered so its own waterline is at 0, where the mod floats every ship.
        Voxels, meshes, and every point in what's written (fixtures, ladders, rooms, moving parts, sails, board points,
        the hull samples, bounds, the dry outline, the rig check); not directions.
        """
        if not dy:
            return
        self.o[1] += dy
        self.ty += dy
        self.ys = self.ys + dy
        self.Y = self.Y + dy
        for q in self.quads:
            q["pts"] = [(p[0], p[1] + dy, p[2]) for p in q["pts"]]
        for e in self.elements:
            e["center"] = np.asarray(e["center"], float) + np.array([0.0, dy, 0.0])

        def pt(v):
            return [v[0], round(float(v[1]) + dy, 4), v[2]] + list(v[3:])

        def pair(v):        # x0 y0 z0 x1 y1 z1 (and a radius after, for the rig check)
            v = list(v)
            v[1] = round(float(v[1]) + dy, 4)
            v[4] = round(float(v[4]) + dy, 4)
            return v

        for f in self.fixtures:
            for k in ("pos", "spawn", "muzzle", "trunnion", "pivot", "seat", "fire", "cook", "chimney", "spout", "feet"):
                if k in f:
                    f[k] = pt(f[k])
            for k in ("sel", "box"):
                if k in f:
                    f[k] = pair(f[k])
            if "port" in f:         # a gun's port: x0 y0 x1 y1
                f["port"] = [f["port"][0], round(float(f["port"][1]) + dy, 4), f["port"][2], round(float(f["port"][3]) + dy, 4)]
        for lad in self.ladders:
            for k in ("bottom", "top", "exitBottom", "exitTop"):
                if k in lad:
                    lad[k] = pt(lad[k])
            if "throughY" in lad:       # (0.3.12) where it passes a hatch on the way (the carrack's hold ladder)
                lad["throughY"] = round(float(lad["throughY"]) + dy, 4)
        for r in self.rooms:
            r["min"], r["max"] = pt(r["min"]), pt(r["max"])
        for s in self.sails:
            s["axisPoint"] = pt(s["axisPoint"])
        for m in self.moving_parts:
            for k in ("axisPoint", "anchor", "tip"):
                if k in m:
                    m[k] = pt(m[k])
        for light in self.lights:
            if "pos" in light:
                light["pos"] = pt(light["pos"])
        self.board_points = [pt(b) for b in self.board_points]
        p = self.props
        for k in ("keelY", "deckY", "waterline"):
            if k in p:
                p[k] = round(p[k] + dy, 4)
        if "bounds" in p:
            p["bounds"] = [pt(b) for b in p["bounds"]]
        if "hullSamples" in p:
            p["hullSamples"] = [pt(s) for s in p["hullSamples"]]
        for deck in p.get("decks", []):
            deck["y"] = round(deck["y"] + dy, 4)
        for mast in p.get("masts", []):
            mast["y0"] = round(mast["y0"] + dy, 4)
            mast["y1"] = round(mast["y1"] + dy, 4)
        dry = p.get("waterClip", {}).get("dryOutline")
        if dry:
            dry["y0"] = round(dry["y0"] + dy, 4)
        # (0.3.8) the decorating data: hooks, stairs, keep-clear boxes; the openings through her side
        for h in p.get("hooks", []):
            h["pos"] = pt(h["pos"])
        for st in p.get("stairs", []):
            st["foot"], st["head"] = pt(st["foot"]), pt(st["head"])
        for k in p.get("keepClear", []):
            k["box"] = pair(k["box"])
        # (0.3.16) the modular pieces' places and the decks' wells (docs/modular-pieces.md)
        for e in p.get("modularPieces", []):
            e["at"] = pt(e["at"])
        for w in p.get("wells", []):
            w["box"] = pair(w["box"])
        # (0.3.20, step B, docs/slots.md §2) the deck cells' slots
        for s in p.get("deckSlots", []):
            s["box"] = pair(s["box"])
            if "solids" in s:
                s["solids"] = [pair(b) for b in s["solids"]]
        # (0.3.26, step D1, docs/slots.md §9) the hull's panels
        for s in p.get("hullSlots", []):
            s["box"] = pair(s["box"])
            s["solids"] = [pair(b) for b in s.get("solids", [])]
            for k in ("outer", "inner"):
                if s.get(k):
                    s[k] = [pt(v) for v in s[k]]
            for k in ("sill", "head"):
                s[k] = round(float(s[k]) + dy, 4)
        for b in p.get("hullBands", []):
            b["y0"], b["y1"] = round(b["y0"] + dy, 4), round(b["y1"] + dy, 4)
        for c in p.get("sideCuts", []):
            c["y0"], c["y1"] = round(c["y0"] + dy, 4), round(c["y1"] + dy, 4)
        dmg = p.get("damage")
        if dmg and "rudder" in dmg:
            dmg["rudder"] = pair(dmg["rudder"])
        rc = p.get("rigCheck")
        if rc:
            rc["standing"] = [pair(s) for s in rc["standing"]]
            for y in rc["yards"]:
                y["pivot"] = pt(y["pivot"])
                y["arms"] = [pair(a) for a in y["arms"]]

    # ------------------------------------------------------------------ materials / voxels

    def material(self, m):
        self.materials.append(m)
        self.mat_index[m.code] = len(self.materials) - 1
        return len(self.materials) - 1

    def mid(self, code):
        return self.mat_index[code]

    def _rng(self, a, b, axis):
        o = self.o[axis]
        i0 = int(round((a - o) / VOX))
        i1 = int(round((b - o) / VOX))
        n = self.n[axis]
        return max(0, min(n, i0)), max(0, min(n, i1))

    def box_slices(self, x0, x1, y0, y1, z0, z1):
        i0, i1 = self._rng(x0, x1, 0)
        j0, j1 = self._rng(y0, y1, 1)
        k0, k1 = self._rng(z0, z1, 2)
        return slice(i0, i1), slice(j0, j1), slice(k0, k1)

    def box(self, mat, x0, x1, y0, y1, z0, z1, where=None, mirror=False):
        """Fill [x0,x1)x[y0,y1)x[z0,z1) with mat (0 = carve). where: optional mask (full grid) limiting the fill."""
        m = self.mid(mat) if isinstance(mat, str) else mat
        spans = [(z0, z1)]
        if mirror:
            spans.append((-z1, -z0))
        for a, b in spans:
            s = self.box_slices(x0, x1, y0, y1, a, b)
            if where is None:
                self.grid[s] = m
            else:
                sub = self.grid[s]
                sub[where[s]] = m

    def cylinder_y(self, mat, cx, cz, r, y0, y1, where=None):
        m = self.mid(mat) if isinstance(mat, str) else mat
        mask = ((self.X - cx) ** 2 + (self.Z - cz) ** 2 <= r * r) & (self.Y >= y0) & (self.Y < y1)
        if where is not None:
            mask &= where
        self.grid[mask] = m

    # ------------------------------------------------------------------ free elements

    def add_box(self, group, mat, center, R, size, faces=None, name=None):
        """Oriented box. R: 3x3 rotation (columns = the box's local x, y, z axes in ship space). size in blocks."""
        self.elements.append(dict(group=group, mat=mat, center=np.array(center, float), R=np.array(R, float),
                                  size=np.array(size, float), faces=faces, name=name))

    def add_aabb(self, group, mat, x0, x1, y0, y1, z0, z1, name=None):
        self.add_box(group, mat, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), np.eye(3),
                     (x1 - x0, y1 - y0, z1 - z0), name=name)

    def add_rod(self, group, mat, a, b, thick, thick2=None, up=(0, 1, 0), name=None):
        a = np.array(a, float)
        b = np.array(b, float)
        d = b - a
        L = np.linalg.norm(d)
        u = d / L
        upv = np.array(up, float)
        if abs(np.dot(upv, u)) > 0.95:
            upv = np.array([1.0, 0, 0]) if abs(u[0]) < 0.9 else np.array([0, 0, 1.0])
        w = np.cross(u, upv)
        w /= np.linalg.norm(w)
        v = np.cross(w, u)
        R = np.column_stack([u, v, w])
        self.add_box(group, mat, (a + b) / 2, R, (L, thick, thick2 if thick2 else thick), name=name)

    def add_octagon_y(self, group, mat, cx, cz, r, y0, y1, name=None):
        """Mast-like octagonal prism: a square plus the same square turned 45 degrees."""
        s = r * 2 * math.cos(math.pi / 8)  # square edge so both squares fit the octagon
        c = ((cx, (y0 + y1) / 2, cz))
        self.add_box(group, mat, c, np.eye(3), (s, y1 - y0, s), name=name)
        a = math.radians(45)
        R = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
        self.add_box(group, mat, c, R, (s, y1 - y0, s), name=name)

    def add_sheet(self, group, mat_of_panel, P, nu, nv, thick=1 / 16, overlap=1.04):
        """A curved sheet (sail) from a parametric surface P(u, v) -> point, built from nu x nv flat panels.
        mat_of_panel(iu, iv, u, v) -> material code."""
        for iu in range(nu):
            for iv in range(nv):
                u0, u1 = iu / nu, (iu + 1) / nu
                v0, v1 = iv / nv, (iv + 1) / nv
                p00, p10 = np.array(P(u0, v0)), np.array(P(u1, v0))
                p01, p11 = np.array(P(u0, v1)), np.array(P(u1, v1))
                c = (p00 + p10 + p01 + p11) / 4
                du = ((p10 - p00) + (p11 - p01)) / 2
                dv = ((p01 - p00) + (p11 - p10)) / 2
                w = np.linalg.norm(du)
                h = np.linalg.norm(dv)
                if w < 1e-4 or h < 1e-4:
                    continue
                ux = du / w
                n = np.cross(ux, dv)
                if np.linalg.norm(n) < 1e-6:
                    continue
                n /= np.linalg.norm(n)
                vy = np.cross(n, ux)
                h_eff = abs(np.dot(dv, vy))
                R = np.column_stack([ux, vy, n])
                self.add_box(group, mat_of_panel(iu, iv, (u0 + u1) / 2, (v0 + v1) / 2), c, R,
                             (w * overlap, h_eff * overlap, thick))

    @contextlib.contextmanager
    def paint(self, tag):
        """What the quads added inside this are, for their paint zone (paint_zones.quad_zone): a zone's code, a kind of
        thing whose zone is where it is ("outside", "inside", "frame", "ceiling", "wall"...), a dict of either by
        material, or None (not paintable)."""
        old = self.paint_tag
        self.paint_tag = tag
        try:
            yield
        finally:
            self.paint_tag = old

    def add_quad(self, group, mat, pts, uv=(0, 0, 16, 16), smooth=None, row=0, hull=None, uvs=None, variant=None):
        """One mesh quad; pts counter-clockwise seen from the side it faces. uv = (u0, v0, u1, v1) in texels, v0 at
        the top edge (pts[3]-pts[2]). Quads sharing a `smooth` id get averaged normals where they meet. hull: "out" for
        the hull's planking outside, "in" for its inside planking (the ceiling): the mod draws damage on them.
        uvs: each corner's own (u, v) instead (a face textured by its position in its plane: add_planar); variant:
        which of the material's textures (else picked from the quad's place in the list)."""
        q = {"group": group, "mat": mat, "pts": [tuple(map(float, p)) for p in pts], "uv": list(uv), "smooth": smooth,
             "row": row}
        if hull:
            q["hull"] = hull
        if uvs is not None:
            q["uvs"] = [(float(u), float(v)) for u, v in uvs]
        if variant is not None:
            q["variant"] = int(variant)
        if self.paint_tag is not None:
            q["paint"] = self.paint_tag
        if SRC_TRACE:
            q["src"] = _src_of_call()
        self.quads.append(q)

    @staticmethod
    def corner_uvs(q):
        """A quad's four corners' (u, v), as written."""
        if q.get("uvs"):
            return q["uvs"]
        u0, v0, u1, v1 = q["uv"]
        return [(u0, v1), (u1, v1), (u1, v0), (u0, v0)]

    def quad_texture(self, i, q):
        m = self.materials[self.mat_index[q["mat"]]]
        k = q["variant"] if q.get("variant") is not None else i * 7 + q.get("row", 0)
        return m.textures[k % len(m.textures)]

    def add_planar(self, group, mat, poly, normal, u_dir=None, origin=None, seed=0, hull=None, centre=None):
        """
        A flat convex polygon (its corners in order round it) facing `normal`, textured by where each point is in its
        plane: u along u_dir (the wood's grain, the length of its planks), v across, 16 texels a block like a deck's
        cells, so its planks run straight across it whatever its shape (a round face's fan of triangles each showing
        the texture turned its own way looked like a wheel of wedges, the user's notes 40, 45 and 54). Cut along the
        texture's edges (every block from `origin`, default the ship's origin, as the decks' cells are) into pieces whose
        uvs stay within one texture; each piece's texture picked by its block, so pieces in one block match. centre: a
        point to have in the middle of a texture instead (a round cap up to a block across is then one piece).
        """
        P = [np.asarray(p, float) for p in poly]
        if len(P) < 3:
            return
        n = np.asarray(normal, float)
        n = n / np.linalg.norm(n)
        if u_dir is None:
            u_dir = (1.0, 0.0, 0.0) if abs(n[0]) < 0.9 else (0.0, 0.0, 1.0)
        eu = np.asarray(u_dir, float) - np.dot(u_dir, n) * n
        eu /= np.linalg.norm(eu)
        ev = np.cross(n, eu)
        if centre is not None:
            o = np.asarray(centre, float) - 0.5 * eu - 0.5 * ev
        else:
            o = np.zeros(3) if origin is None else np.asarray(origin, float)
        verts = [(float((p - o) @ eu), float((p - o) @ ev), p) for p in P]
        area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(verts, verts[1:] + verts[:1]))
        if abs(area) < 1e-12:
            return
        if area < 0:                    # counter-clockwise seen from the side it faces
            verts = verts[::-1]

        def clip(vs, axis, lim, keep_above):
            out = []
            for a, b in zip(vs, vs[1:] + vs[:1]):
                ia = (a[axis] >= lim - 1e-9) if keep_above else (a[axis] <= lim + 1e-9)
                ib = (b[axis] >= lim - 1e-9) if keep_above else (b[axis] <= lim + 1e-9)
                if ia:
                    out.append(a)
                if ia != ib:
                    t = (lim - a[axis]) / (b[axis] - a[axis])
                    out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t))
            return out

        ss, ts = [v[0] for v in verts], [v[1] for v in verts]
        for i in range(int(math.floor(min(ss) + 1e-9)), int(math.ceil(max(ss) - 1e-9))):
            for j in range(int(math.floor(min(ts) + 1e-9)), int(math.ceil(max(ts) - 1e-9))):
                piece = verts
                for axis, lim, above in ((0, i, True), (0, i + 1, False), (1, j, True), (1, j + 1, False)):
                    piece = clip(piece, axis, lim, above)
                    if len(piece) < 3:
                        break
                # drop repeated corners (a polygon's corner on a block's edge)
                piece = [v for k, v in enumerate(piece) if np.linalg.norm(v[2] - piece[k - 1][2]) > 1e-7]
                if len(piece) < 3:
                    continue
                a2 = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(piece, piece[1:] + piece[:1]))
                if a2 < 1e-9:
                    continue
                # (a round cap, centred: one texture all over it, so a cap a little over a block across doesn't show
                # another plank pattern at its rim; the plank textures tile)
                var = seed if centre is not None else ((i * 73856093) ^ (j * 19349663) ^ (seed * 83492791)) & 0x7FFFFFFF
                uv = [((v[0] - i) * 16, (v[1] - j) * 16) for v in piece]
                k = 1
                while k + 1 < len(piece):
                    idx = [0, k, k + 1, min(k + 2, len(piece) - 1)]
                    self.add_quad(group, mat, [piece[m][2] for m in idx], uvs=[uv[m] for m in idx], variant=var,
                                  hull=hull)
                    k += 2

    def add_cylinder(self, group, mat, a, b, r0, r1=None, sides=12, seg=1.0, caps=True, texture_turns=1.0,
                     smooth=True, phase=0.0, inward=False, cap_grain=None):
        """Round (tapered) pole from a to b as a mesh, split into pieces of at most `seg` blocks so the texture
        isn't stretched. smooth=False: flat facets (a 16-sided mast looks hewn, not turned). cap_grain: which way the
        planks run across its ends (default: along x, or z for a pole along x)."""
        a, b = np.asarray(a, float), np.asarray(b, float)
        r1 = r0 if r1 is None else r1
        axis = b - a
        L = float(np.linalg.norm(axis))
        if L < 1e-6:
            return
        w = axis / L
        ref = np.array([0.0, 1.0, 0.0]) if abs(w[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
        e1 = np.cross(w, ref)
        e1 /= np.linalg.norm(e1)
        e2 = np.cross(w, e1)
        n_len = max(1, int(math.ceil(L / seg)))
        sid = f"cyl{len(self.quads)}" if smooth else None
        grain = mat in self.mat_index and self.materials[self.mat_index[mat]].grain
        dirs = [math.cos(2 * math.pi * (k + phase) / sides) * e1 + math.sin(2 * math.pi * (k + phase) / sides) * e2
                for k in range(sides + 1)]
        for i in range(n_len):
            t0, t1 = i / n_len, (i + 1) / n_len
            c0, c1 = a + axis * t0, a + axis * t1
            ra, rb = r0 + (r1 - r0) * t0, r0 + (r1 - r0) * t1
            vlen = min(16.0, L / n_len * 16)
            for k in range(sides):
                u0 = 16 * texture_turns * k / sides % 16
                u1 = u0 + 16 * texture_turns / sides
                if grain:       # u along the pole, v round it
                    pts = [c0 + dirs[k] * ra, c1 + dirs[k] * rb, c1 + dirs[k + 1] * rb, c0 + dirs[k + 1] * ra]
                    uv = (0, u0, vlen, min(16, u1))
                else:
                    pts = [c0 + dirs[k] * ra, c0 + dirs[k + 1] * ra, c1 + dirs[k + 1] * rb, c1 + dirs[k] * rb]
                    uv = (u0, 0, min(16, u1), vlen)
                # counter-clockwise seen from outside: (p1-p0) x (p3-p0) must point away from the axis
                if (np.dot(np.cross(pts[1] - pts[0], pts[3] - pts[0]), dirs[k] + dirs[k + 1]) < 0) != inward:
                    pts = [pts[1], pts[0], pts[3], pts[2]]
                self.add_quad(group, mat, pts, uv, smooth=sid)
        caps_ = (caps, caps) if isinstance(caps, bool) else tuple(caps)     # (0.3.10: or (at a, at b))
        if any(caps_):
            # (0.3.8) each end one flat face, its planks straight across it (add_planar; a fan of triangles before, each
            # with the texture its own way round: a wheel of wedges, the user's notes 40, 45, 54)
            for (c, r, sgn), on in zip(((a, r0, -1), (b, r1, 1)), caps_):
                if on and r > 1e-6:
                    self.add_planar(group, mat, [c + dirs[k] * r for k in range(sides)], w * sgn, centre=c,
                                    u_dir=cap_grain)

    def add_box_mesh(self, group, mat, center, size, R=None, uv_scale=16.0, max_len=2.0):
        """
        An (oriented) box as six mesh quads, flat-shaded. size = (sx, sy, sz) along R's columns. On every face the
        texture's u (the grain of vanilla's wood textures, the length of its planks) runs along the face's longer side,
        so a timber shows its grain along its length; a box longer than max_len is drawn in pieces so the texture
        isn't stretched along it.
        """
        c = np.asarray(center, float)
        R = np.eye(3) if R is None else np.asarray(R, float)
        size = np.abs(np.asarray(size, float))      # (a box given corner to corner the other way round: the same box)
        long_ax = int(np.argmax(size))
        n_pieces = max(1, int(math.ceil(size[long_ax] / max_len - 1e-9))) if max_len else 1
        h = size / 2
        ph = h.copy()
        ph[long_ax] = h[long_ax] / n_pieces
        for piece in range(n_pieces):
            off = np.zeros(3)
            off[long_ax] = -h[long_ax] + ph[long_ax] * (2 * piece + 1)
            for axis in range(3):
                for sgn in (1, -1):
                    if axis == long_ax and ((sgn > 0 and piece < n_pieces - 1) or (sgn < 0 and piece > 0)):
                        continue                  # where two pieces meet, inside the box
                    n = np.zeros(3)
                    n[axis] = sgn
                    a1, a2 = [i for i in range(3) if i != axis]
                    if ph[a2] > ph[a1] + 1e-9:
                        a1, a2 = a2, a1           # u along the longer side
                    e1 = np.zeros(3)
                    e1[a1] = ph[a1]
                    e2 = np.zeros(3)
                    e2[a2] = ph[a2]
                    fc = off + n * ph
                    pts = [fc - e1 - e2, fc + e1 - e2, fc + e1 + e2, fc - e1 + e2]
                    world = [c + R @ p for p in pts]
                    # wound counter-clockwise seen from outside in the ship's space (0.3.8: it was in the box's own, so a
                    # box turned by a left-handed R, a mirror, came out inside out, every face's normal pointing in and
                    # lit dark: the stern gallery's, the nameboard's, the wheel's rim and spokes)
                    if np.dot(np.cross(world[1] - world[0], world[3] - world[0]), R @ n) < 0:
                        world = [world[1], world[0], world[3], world[2]]
                    du = min(16.0, 2 * ph[a1] * uv_scale)
                    dv = min(16.0, 2 * ph[a2] * uv_scale)
                    self.add_quad(group, mat, world, (0, 0, du, dv))

    def add_prism(self, group, mat, front, back):
        """A convex prism between two matching quads (4 corners each, in order around the face)."""
        front, back = [np.asarray(p, float) for p in front], [np.asarray(p, float) for p in back]
        centre = np.mean(front + back, axis=0)
        faces = [front, back[::-1]] + [[back[i], back[(i + 1) % 4], front[(i + 1) % 4], front[i]] for i in range(4)]
        for q in faces:
            n = np.cross(q[1] - q[0], q[3] - q[0])
            if np.dot(n, np.mean(q, axis=0) - centre) < 0:
                q = [q[1], q[0], q[3], q[2]]
            if np.linalg.norm(q[3] - q[0]) > np.linalg.norm(q[1] - q[0]) + 1e-9:
                q = [q[3], q[0], q[1], q[2]]         # u (the wood's grain) along the face's longer side, same winding
            w = float(np.linalg.norm(q[1] - q[0]))
            h = float(np.linalg.norm(q[3] - q[0]))
            self.add_quad(group, mat, q, (0, 0, min(16, w * 16), min(16, h * 16)))

    def add_beam_mesh(self, group, mat, a, b, w, h=None, up=(0, 1, 0)):
        """A squared timber from a to b (w wide, h high), as a mesh."""
        a, b = np.asarray(a, float), np.asarray(b, float)
        dvec = b - a
        L = float(np.linalg.norm(dvec))
        u = dvec / L
        upv = np.asarray(up, float)
        if abs(np.dot(upv, u)) > 0.95:
            upv = np.array([1.0, 0, 0]) if abs(u[0]) < 0.9 else np.array([0, 0, 1.0])
        wv = np.cross(u, upv)
        wv /= np.linalg.norm(wv)
        v = np.cross(wv, u)
        self.add_box_mesh(group, mat, (a + b) / 2, (L, h if h else w, w), np.column_stack([u, v, wv]))

    def add_rope(self, a, b, r=0.035, group="exterior", mat="rope"):
        self.add_cylinder(group, mat, a, b, r, r, sides=5, seg=2.0, caps=False)

    def add_sheet_grid(self, group, mat, origin, eu, ev, su, sv, cell, inside, belly, thick=1 / 16, overlap=1.06):
        """A sheet on a regular grid of square panels in the plane (origin, eu, ev), keeping the cells whose centre
        passes inside(s, t); belly(s, t) pushes each panel along the plane normal. No gaps, stepped edges."""
        origin, eu, ev = np.array(origin, float), np.array(eu, float), np.array(ev, float)
        eu /= np.linalg.norm(eu)
        ev = ev - np.dot(ev, eu) * eu
        ev /= np.linalg.norm(ev)
        en = np.cross(eu, ev)

        def P(s, t):
            return origin + s * eu + t * ev + belly(s, t) * en

        h = cell * 0.5
        s = 0.0
        while s < su:
            t = 0.0
            while t < sv:
                cs, ct = s + h, t + h
                if inside(cs, ct):
                    c = P(cs, ct)
                    du = P(cs + h, ct) - P(cs - h, ct)
                    dv = P(cs, ct + h) - P(cs, ct - h)
                    ux = du / np.linalg.norm(du)
                    n = np.cross(ux, dv)
                    n /= np.linalg.norm(n)
                    vy = np.cross(n, ux)
                    R = np.column_stack([ux, vy, n])
                    self.add_box(group, mat, c, R, (np.linalg.norm(du) * overlap, abs(np.dot(dv, vy)) * overlap, thick))
                t += cell
            s += cell

    # ------------------------------------------------------------------ meshing

    def room_grid(self):
        rg = np.zeros(self.n, dtype=np.int16)
        for idx, r in enumerate(self.rooms, start=1):
            (x0, y0, z0), (x1, y1, z1) = r["min"], r["max"]
            mask = (self.X >= x0) & (self.X < x1) & (self.Y >= y0) & (self.Y < y1) & (self.Z >= z0) & (self.Z < z1)
            if r.get("mask") is not None:
                mask &= r["mask"]
            rg[mask & (rg == 0)] = idx
        return rg

    def point_zone(self, p, rg):
        """1 = interior (inside a room), 0 = exterior."""
        i = int(math.floor((p[0] - self.o[0]) / VOX))
        j = int(math.floor((p[1] - self.o[1]) / VOX))
        k = int(math.floor((p[2] - self.o[2]) / VOX))
        if 0 <= i < self.n[0] and 0 <= j < self.n[1] and 0 <= k < self.n[2]:
            return 1 if rg[i, j, k] > 0 else 0
        return 0

    def mesh_voxels(self):
        """Greedy-merge voxels into boxes that never cross a block boundary and whose cells share
        material, texture variant and the same per-face exposure (none / exterior / interior).
        (0.3.20, step B) A design with deck slots (slot_grid: per voxel its slot's index or -1) is meshed with every slot
        filled (its empty-start cells' planking put in: the full grid), no box crossing a slot's edge (each box's "slot":
        its index, or -1); write() draws the slots' boxes as the mesh's slot quads instead of shape elements."""
        sg = getattr(self, "slot_grid", None)
        g = self.grid if sg is None else self.full_grid()
        nx, ny, nz = self.n
        rg = self.room_grid()
        opaque = np.array([False] + [m.opaque for m in self.materials[1:]])
        render = np.array([False] + [m.render for m in self.materials[1:]])
        op = opaque[g]

        # exposure code per cell & face: 0 hidden, 1 exterior, 2 interior
        expo = np.zeros(self.n + (6,), dtype=np.uint8)
        for f, (dx, dy, dz) in enumerate(FACE_DIRS):
            nb_op = np.zeros(self.n, dtype=bool)
            nb_room = np.zeros(self.n, dtype=np.int16)
            src = [slice(None)] * 3
            dst = [slice(None)] * 3
            for ax, d in enumerate((dx, dy, dz)):
                if d == 1:
                    dst[ax], src[ax] = slice(0, -1), slice(1, None)
                elif d == -1:
                    dst[ax], src[ax] = slice(1, None), slice(0, -1)
            nb_op[tuple(dst)] = op[tuple(src)]
            nb_room[tuple(dst)] = rg[tuple(src)]
            e = np.where(nb_op, 0, np.where(nb_room > 0, 2, 1)).astype(np.uint8)
            if self.hide_toward is not None:
                nb_hide = np.ones(self.n, dtype=bool)
                nb_hide[tuple(dst)] = self.hide_toward[tuple(src)]
                e[self.hide_applies & nb_hide] = 0
            expo[..., f] = e

        # block coordinates -> texture variant
        bx = np.floor(self.X).astype(int)
        # (0.3.10: the design's blocks, before translate_y moved her by a fraction of one: the faces' uv are by them,
        # _uv_aabb; by the moved ones a merged box could run across a design block's border, its uv past 0..16)
        by = np.floor(self.Y - getattr(self, "ty", 0.0) + 1e-6).astype(int)
        bz = np.floor(self.Z).astype(int)
        variant = ((bx * 73856093) ^ (by * 19349663) ^ (bz * 83492791)) & 0x7FFFFFFF

        key = np.zeros(self.n, dtype=np.int64)
        for f in range(6):
            key = key * 3 + expo[..., f]
        key = key * 256 + g
        key = key * 8 + (variant % 4)
        if sg is not None:
            key = key * (int(sg.max()) + 2) + (sg.astype(np.int64) + 1)
        blk = np.stack([bx, by, bz], axis=-1)

        done = np.zeros(self.n, dtype=bool)
        boxes = []
        solid_cells = np.argwhere((g > 0) & render[g])
        for i, j, k in solid_cells:
            if done[i, j, k]:
                continue
            kk = key[i, j, k]
            b = blk[i, j, k]

            def ok(ii, jj, kk2):
                return (ii < nx and jj < ny and kk2 < nz and not done[ii, jj, kk2] and key[ii, jj, kk2] == kk
                        and (blk[ii, jj, kk2] == b).all())

            i1 = i + 1
            while ok(i1, j, k):
                i1 += 1
            k1 = k + 1
            while all(ok(ii, j, k1) for ii in range(i, i1)):
                k1 += 1
            j1 = j + 1
            while all(ok(ii, j1, kz) for ii in range(i, i1) for kz in range(k, k1)):
                j1 += 1
            done[i:i1, j:j1, k:k1] = True
            m = g[i, j, k]
            ex = expo[i, j, k].copy()
            # end faces along merged axes: take the exposure of the far cells
            ex[0] = expo[i1 - 1, j, k, 0]
            ex[2] = expo[i, j1 - 1, k, 2]
            ex[4] = expo[i, j, k1 - 1, 4]
            # a merged face is shown if any of its cells is exposed
            faces = {}
            for f in range(6):
                sl = {0: (slice(i1 - 1, i1), slice(j, j1), slice(k, k1)),
                      1: (slice(i, i + 1), slice(j, j1), slice(k, k1)),
                      2: (slice(i, i1), slice(j1 - 1, j1), slice(k, k1)),
                      3: (slice(i, i1), slice(j, j + 1), slice(k, k1)),
                      4: (slice(i, i1), slice(j, j1), slice(k1 - 1, k1)),
                      5: (slice(i, i1), slice(j, j1), slice(k, k + 1))}[f]
                vals = expo[sl + (f,)]
                faces[FACES[f]] = int(vals.max())
            lo = self.o + np.array([i, j, k]) * VOX
            hi = self.o + np.array([i1, j1, k1]) * VOX
            boxes.append(dict(mat=int(m), lo=lo, hi=hi, faces=faces, variant=int(variant[i, j, k] % 4)))
            if sg is not None:
                boxes[-1]["slot"] = int(sg[i, j, k])
        if sg is not None:
            self._mesh_cache = dict(g=g, expo=expo, variant=variant, rg=rg)
        return boxes, rg

    # ------------------------------------------------------------------ (0.3.20, step B) deck slots

    def full_grid(self):
        """Her grid with every slot filled (the empty-start cells' planking put in)."""
        g = self.grid.copy()
        mask = self.slot_grid >= 0
        # ((0.3.27) a beam segment's voxels are the beam's own (hidden): only the deck cells are planking put in)
        bm = getattr(self, "slot_beam_mask", None)
        if bm is not None:
            mask &= ~bm
        g[mask] = self.slot_fill
        return g

    # a box face's corners (as the game lays a shape element's face) and its normal, for (lo, hi)
    _FACE_CORNERS = {
        "north": [(1, 0, 0), (0, 0, 0), (0, 1, 0), (1, 1, 0)],
        "south": [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)],
        "east": [(1, 0, 1), (1, 0, 0), (1, 1, 0), (1, 1, 1)],
        "west": [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)],
        "up": [(0, 1, 1), (1, 1, 1), (1, 1, 0), (0, 1, 0)],
        "down": [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)],
    }

    def _face_quad(self, face, lo, hi, uv_lo=None, uv_hi=None):
        """A box face as a mesh quad: its 4 corners (wound to face out) and their (u, v) as the shape element's face
        would have them (_uv_aabb; uv_lo/hi: the box whose uv it takes, if other than lo/hi)."""
        lo, hi = np.asarray(lo, float), np.asarray(hi, float)
        u0, v0, u1, v1 = self._uv_aabb(face, np.asarray(lo if uv_lo is None else uv_lo, float),
                                       np.asarray(hi if uv_hi is None else uv_hi, float))
        pts = [tuple(float(lo[a] + (hi[a] - lo[a]) * c[a]) for a in range(3)) for c in self._FACE_CORNERS[face]]
        return pts, [(u0, v1), (u1, v1), (u1, v0), (u0, v0)]

    def slot_state_quads(self, quads, states=None):
        """The slot quads drawn with the slots in `states` (filled: True; default self.slot_states, else the design's
        start): drawn while (owner -1 or filled) and (facing -1 or empty)."""
        st = states if states is not None else getattr(self, "slot_states", None)
        if st is None:
            st = [s.get("start", "filled") != "empty" for s in self.props.get("deckSlots", [])]
        return [q for q in quads if (q["slot"][0] < 0 or st[q["slot"][0]]) and (q["slot"][1] < 0 or not st[q["slot"][1]])]

    def slot_quads(self, boxes, rg):
        """
        (0.3.20, docs/slots.md §3) The deck cells' quads, as dicts like self.quads' (group "slot:deck" or
        "slot:deck:interior", mat, pts, uvs, variant, "slot": (owner, facing), "pz": paint zone or None, "voxels": the
        voxel faces it covers with their zones, for the paint check). Per cell: its top and underside (each half of it in
        a block of its own, the textures as the voxel faces had them), its sides toward a neighbouring slot (owner the
        cell, facing the neighbour) and toward the open (facing -1); the fixed planking's (or a coaming's, a clipped
        cell's) faces toward it (owner -1, facing the cell). Nothing toward fixed stuff from the cell itself.
        """
        mc = self._mesh_cache
        g, expo, variant = mc["g"], mc["expo"], mc["variant"]
        sg = self.slot_grid
        nx, ny, nz = self.n
        opaque = np.array([False] + [m.opaque for m in self.materials[1:]])
        render = np.array([False] + [m.render for m in self.materials[1:]])
        hull_in = self.hide_applies if self.hide_applies is not None else np.ones(self.n, bool)
        out = []

        def zone_of(i, j, k, face):
            return self.paint_voxel_cells.get((i, j, k, face)) if self.paint_voxel_cells else None

        def grp(code):
            return "slot:deck:interior" if code == 2 else "slot:deck"

        def emit(face, cells, mat, var, owner, facing, room_code, ext=0.0, zone_face=None):
            """One quad over voxel cells (i, j, k) in a row (one face's plane, one block)."""
            c = np.array(cells)
            lo = self.o + c.min(axis=0) * VOX
            hi = self.o + (c.max(axis=0) + 1) * VOX
            if ext:
                lo[1] -= ext
                hi[1] += ext
            ax = {"east": 0, "west": 0, "up": 1, "down": 1, "south": 2, "north": 2}[face]
            far = face in ("east", "up", "south")
            if far:
                lo[ax] = hi[ax]
            else:
                hi[ax] = lo[ax]
            # (a fixed clipped cell's edge a hair past it up and down: onto the edge strips 0.002 off the deck's faces)
            pts, uvs = self._face_quad(face, lo, hi)
            q = {"group": grp(room_code), "mat": self.materials[mat].code, "pts": pts, "uvs": uvs, "variant": var,
                 "slot": (owner, facing), "pz": None, "voxels": []}
            if zone_face:
                zs = [zone_of(*cell, zone_face) for cell in cells]
                q["voxels"] = [z for z in zs if z is not None]
                if q["voxels"]:
                    q["pz"] = max(set(q["voxels"]), key=q["voxels"].count)
            out.append(q)

        dirs = {"east": (1, 0, 0), "west": (-1, 0, 0), "south": (0, 0, 1), "north": (0, 0, -1)}
        opp = {"east": "west", "west": "east", "south": "north", "north": "south"}
        n_slots = int(sg.max()) + 1
        cells_of = [[] for _ in range(n_slots)]
        for i, j, k in np.argwhere(sg >= 0):
            cells_of[sg[i, j, k]].append((int(i), int(j), int(k)))
        bx = np.floor(self.X).astype(int)
        bz = np.floor(self.Z).astype(int)
        beam_slots = getattr(self, "slot_beam_ids", set())
        for s, cells in enumerate(cells_of):
            if s in beam_slots:
                continue                    # ((0.3.27) a beam segment: its quads are the beam's timber's, tagged by beam_segments)
            cs = set(cells)
            # top and underside: per block (and exposure), in runs along x then z (one quad a block half)
            for face, dj in (("up", 1), ("down", -1)):
                f_i = 2 if face == "up" else 3
                groups = {}
                for (i, j, k) in cells:
                    e = int(expo[i, j, k, f_i])
                    if e == 0:
                        continue
                    groups.setdefault((bx[i, j, k], bz[i, j, k], e), []).append((i, j, k))
                for (_, _, e), cl in sorted(groups.items()):
                    c = np.array(cl)
                    if len(cl) != (np.ptp(c[:, 0]) + 1) * (np.ptp(c[:, 2]) + 1):
                        for cell in cl:        # (not a rectangle: a voxel at a time)
                            emit(face, [cell], g[cell], int(variant[cell] % 4), s, -1, e, zone_face=face)
                        continue
                    i0, j0, k0 = c.min(axis=0)
                    emit(face, cl, g[i0, j0, k0], int(variant[i0, j0, k0] % 4), s, -1, e, zone_face=face)
            # the sides: each border voxel's neighbour across the cell's edge
            for face, (di, _, dk) in dirs.items():
                runs = {}
                for (i, j, k) in cells:
                    a, c_ = i + di, k + dk
                    if (a, j, c_) in cs:
                        continue
                    inside = 0 <= a < nx and 0 <= c_ < nz
                    nb = (a, j, c_)
                    if inside and not hull_in[nb]:
                        continue                                    # (outside her: nothing there)
                    t = int(sg[nb]) if inside else -1
                    m = int(g[nb]) if inside else 0
                    room = 2 if inside and rg[nb] > 0 else 1
                    blk = (bx[i, j, k], bz[i, j, k])
                    if t >= 0:
                        kind = ("slot", t)
                    elif m == 0 or (not opaque[m] and render[m]):
                        kind = ("open", -1)
                    else:
                        kind = ("fixed", -1)
                    runs.setdefault((kind, room, blk), []).append((i, j, k))
                split = []
                along = 2 if face in ("east", "west") else 0
                for key_, cl in sorted(runs.items()):
                    cl = sorted(cl, key=lambda c: c[along])
                    part = [cl[0]]
                    for c in cl[1:]:
                        if c[along] != part[-1][along] + 1:
                            split.append((key_, part))
                            part = []
                        part.append(c)
                    split.append((key_, part))
                for ((kind, t), room, _), cl in split:
                    i0, j0, k0 = cl[0]
                    if kind == "slot":
                        emit(face, cl, g[i0, j0, k0], int(variant[i0, j0, k0] % 4), s, t, room)
                    elif kind == "open":
                        emit(face, cl, g[i0, j0, k0], int(variant[i0, j0, k0] % 4), s, -1, room)
                    else:
                        # the fixed thing's face toward the cell, seen while the cell is out (lit as the cell's space)
                        nbs = [(i + di, j, k + dk) for i, j, k in cl]
                        a0, b0, c0 = nbs[0]
                        m = int(g[a0, b0, c0])
                        hid = not render[m]
                        mat = self.slot_fill if hid else m
                        room_c = 2 if rg[i0, j0, k0] > 0 else 1
                        emit(opp[face], nbs, mat, int(variant[a0, b0, c0] % 4), -1, s, room_c, ext=0.002 if hid else 0.0)
        return out

    # ------------------------------------------------------------------ output

    def _tex_code(self, mat_idx, variant):
        m = self.materials[mat_idx]
        v = variant % len(m.textures)
        return f"{m.code}{v}" if len(m.textures) > 1 else m.code

    def _textures(self):
        t = {}
        for m in self.materials[1:]:
            if not m.render:
                continue
            if len(m.textures) > 1:
                for v, p in enumerate(m.textures):
                    t[f"{m.code}{v}"] = p
            else:
                t[m.code] = m.textures[0]
        return t

    def _uv_aabb(self, face, lo, hi):
        # (0.3.10) each face's uv is its place in the whole block it's in, those blocks the design's (before translate_y):
        # a design moved by a fraction of a block (the carrack on the grid: 1.375) had cells across the game's block
        # borders, their uv running past 0..16, and the game showed the atlas's neighbouring textures there (the user's
        # notes 70, 74, 83, 84: "weird textures")
        off = np.array([0.0, getattr(self, "ty", 0.0), 0.0])
        b = np.floor(lo - off + 1e-6) + off
        fl = (lo - b) * 16
        fh = (hi - b) * 16
        if face in ("north", "south"):
            return [fl[0], 16 - fh[1], fh[0], 16 - fl[1]]
        if face in ("east", "west"):
            return [fl[2], 16 - fh[1], fh[2], 16 - fl[1]]
        return [fl[0], fl[2], fh[0], fh[2]]

    @staticmethod
    def _r(v):
        return round(float(v), 4)

    def _voxel_element(self, name, b, zone):
        m = self.materials[b["mat"]]
        faces = {}
        for f in FACES:
            if b["faces"][f] != zone:
                continue
            face = {"texture": "#" + self._tex_code(b["mat"], b["variant"]),
                    "uv": [self._r(x) for x in self._uv_aabb(f, b["lo"], b["hi"])]}
            if m.glow:
                face["glow"] = m.glow
            faces[f] = face
        if not faces:
            return None
        return {"name": name, "from": [self._r(x * 16) for x in b["lo"]], "to": [self._r(x * 16) for x in b["hi"]],
                "faces": faces}

    @staticmethod
    def _euler_xyz(R):
        """Angles (deg) for VS's element rotation, which applies Rx * Ry * Rz (Z first) to vertices."""
        sy = max(-1.0, min(1.0, R[0, 2]))
        b = math.asin(sy)
        if abs(math.cos(b)) > 1e-6:
            a = math.atan2(-R[1, 2], R[2, 2])
            c = math.atan2(-R[0, 1], R[0, 0])
        else:
            a = math.atan2(R[2, 1], R[1, 1])
            c = 0.0
        return math.degrees(a), math.degrees(b), math.degrees(c)

    def _free_element(self, name, e):
        m = self.materials[self.mat_index[e["mat"]]]
        c, R, s = e["center"], e["R"], e["size"]
        lo = c - s / 2
        hi = c + s / 2
        tex = "#" + self._tex_code(self.mat_index[e["mat"]], zlib.crc32(name.encode()) & 3)
        faces = {}
        for f in FACES:
            if e["faces"] is not None and f not in e["faces"]:
                continue
            if f in ("north", "south"):
                du, dv = s[0], s[1]
            elif f in ("east", "west"):
                du, dv = s[2], s[1]
            else:
                du, dv = s[0], s[2]
            uv = (e.get("uv") or {}).get(f)
            if uv is None:
                uv = [0, 0, min(16, max(0.5, du * 16)), min(16, max(0.5, dv * 16))]
            face = {"texture": tex, "uv": [self._r(v) for v in uv]}
            if m.glow:
                face["glow"] = m.glow
            faces[f] = face
        el = {"name": name, "from": [self._r(x * 16) for x in lo], "to": [self._r(x * 16) for x in hi], "faces": faces}
        if not np.allclose(R, np.eye(3), atol=1e-6):
            ax, ay, az = self._euler_xyz(R)
            el["rotationOrigin"] = [self._r(x * 16) for x in c]
            if abs(ax) > 1e-4:
                el["rotationX"] = self._r(ax)
            if abs(ay) > 1e-4:
                el["rotationY"] = self._r(ay)
            if abs(az) > 1e-4:
                el["rotationZ"] = self._r(az)
        return el

    def _write_skin(self, asset_dir, rg, paint_faces=None, slot_q=None):
        """Mesh quads -> skin.json: per group a flat list of quads, each
        [texture index, then per corner x y z u v nx ny nz] (33 numbers). u, v in texels (0..16).
        hullOut / hullIn: per group, which of its quads are the hull's planking outside and inside, as half-open index
        ranges [start, end, start, end, ...]; hullThickness: the planking's thickness."""
        if not self.quads:
            return None
        # smooth normals: average face normals of quads in the same smoothing group that share a corner
        def face_normal(p):
            p = np.asarray(p)
            n = np.cross(p[1] - p[0], p[3] - p[0]) + np.cross(p[3] - p[2], p[1] - p[2])
            if np.linalg.norm(n) < 1e-12:
                n = np.cross(p[1] - p[0], p[2] - p[0])
            ln = np.linalg.norm(n)
            return n / ln if ln > 1e-12 else np.array([0.0, 1.0, 0.0])

        acc = {}
        normals = []
        all_q = self.quads + (slot_q or [])
        slot_pairs = {}
        for q in all_q:
            n = face_normal(q["pts"])
            normals.append(n)
            if q.get("smooth"):
                for p in q["pts"]:
                    key = (q["smooth"], round(p[0], 4), round(p[1], 4), round(p[2], 4))
                    acc[key] = acc.get(key, 0) + n
        textures, tex_index = [], {}
        groups = {}
        hull_ranges = {"out": {}, "in": {}}
        written_groups = []
        paint_ranges = {}
        for i, q in enumerate(all_q):
            path = self.quad_texture(i, q)
            if path not in tex_index:
                tex_index[path] = len(textures)
                textures.append(path)
            grp = q["group"]
            if grp == "auto":
                # lit as the space it faces is: a little in front of it (since 0.3.7; its middle was taken before, and a
                # quad on a room's edge, like the inside planking of a cabin's corner over a bulkhead, was lit as outside)
                grp = "interior" if self.point_zone(np.mean(q["pts"], axis=0) + normals[i] * 0.06, rg) else "exterior"
            if "slot" in q and not grp.startswith("slot:") and q.get("slotfam") == "deck":
                # ((0.3.27, docs/slots.md §10) a beam segment's timber, a carling or a cut end: the deck cells' groups)
                grp = {"exterior": "slot:deck", "interior": "slot:deck:interior"}[grp]
            elif "slot" in q and not grp.startswith("slot:"):
                # (0.3.26, step D1, docs/slots.md §9) a hull panel's quad (or a reveal round one): its slot group
                grp = {"exterior": "slot:hull", "interior": "slot:hull:interior", "bottom": "slot:hull:bottom",
                       "bottomclad": "slot:hull:bottomclad"}[grp]
            corner_uv = self.corner_uvs(q)
            row = [tex_index[path]]
            for k, p in enumerate(q["pts"]):
                if q.get("smooth"):
                    n = acc[(q["smooth"], round(p[0], 4), round(p[1], 4), round(p[2], 4))]
                    n = n / (np.linalg.norm(n) + 1e-12)
                else:
                    n = normals[i]
                row += [round(float(p[0]), 4), round(float(p[1]), 4), round(float(p[2]), 4),
                        round(float(corner_uv[k][0]), 3), round(float(corner_uv[k][1]), 3),
                        round(float(n[0]), 3), round(float(n[1]), 3), round(float(n[2]), 3)]
            groups.setdefault(grp, []).append(row)
            written_groups.append(grp)
            if "slot" in q:
                slot_pairs.setdefault(grp, []).extend(int(v) for v in q["slot"])
            if q.get("pz") is not None:
                # (0.3.8) its paint zone: [start, end, zone, ...] per group, half-open, merged where they run on
                pr = paint_ranges.setdefault(grp, [])
                at = len(groups[grp]) - 1
                if pr and pr[-2] == at and pr[-1] == q["pz"]:
                    pr[-2] = at + 1
                else:
                    pr += [at, at + 1, int(q["pz"])]
            if q.get("hull") in ("out", "in"):
                ranges = hull_ranges[q["hull"]].setdefault(grp, [])
                at = len(groups[grp]) - 1
                if ranges and ranges[-1] == at:
                    ranges[-1] = at + 1
                else:
                    ranges += [at, at + 1]
        mesh_dir = os.path.join(asset_dir, "config", "shipmeshes")
        os.makedirs(mesh_dir, exist_ok=True)
        out = {"textures": textures, "groups": groups}
        if hull_ranges["out"] or hull_ranges["in"]:
            out["hullOut"], out["hullIn"] = hull_ranges["out"], hull_ranges["in"]
            if self.hull_thickness:
                out["hullThickness"] = self.hull_thickness
        if paint_ranges or paint_faces:
            out["paintQuads"] = paint_ranges
            out["paintFaces"] = paint_faces or {}
        if slot_pairs:
            # (0.3.20, docs/slots.md §3) each slot group's quads' owner and facing slots, 2 ints a quad
            out["slotQuads"] = slot_pairs
        with open(os.path.join(mesh_dir, self.code + ".json"), "w", encoding="utf-8") as fh:
            json.dump(out, fh, separators=(",", ":"))
        if SRC_TRACE:
            # (debugging: which generator function made each written quad, by group and index, for tools/shipgen's mesh
            # check's findings: SHIPGEN_SRC=1)
            srcs = {}
            for g_, row_src in zip(written_groups, [q.get("src", "?") for q in self.quads]):
                srcs.setdefault(g_, []).append(row_src)
            src_dir = SRC_TRACE if os.path.isdir(SRC_TRACE) else os.path.join(os.path.dirname(__file__), "__pycache__")
            os.makedirs(src_dir, exist_ok=True)
            with open(os.path.join(src_dir, self.code + ".src.json"), "w", encoding="utf-8") as fh:
                json.dump(srcs, fh)
        return f"ships:config/shipmeshes/{self.code}.json"

    def _face_zone(self, b, face):
        """A voxel box's face's paint zone: the one most of its cells' faces were given (paint_voxel_cells), or None."""
        lo = np.round((np.asarray(b["lo"], float) - self.o) / VOX).astype(int)
        hi = np.round((np.asarray(b["hi"], float) - self.o) / VOX).astype(int)
        ax = {"east": 0, "west": 0, "up": 1, "down": 1, "south": 2, "north": 2}[face]
        far = face in ("east", "up", "south")
        rng = [range(lo[k], hi[k]) for k in range(3)]
        rng[ax] = [hi[ax] - 1] if far else [lo[ax]]
        votes = {}
        for i in rng[0]:
            for j in rng[1]:
                for k in rng[2]:
                    z = self.paint_voxel_cells.get((i, j, k, face))
                    if z is not None:
                        votes[z] = votes.get(z, 0) + 1
        return max(votes, key=votes.get) if votes else None

    def _shape(self, elements):
        return {"textureWidth": 16, "textureHeight": 16, "textures": self._textures(), "elements": elements}

    def write(self, asset_dir, docs_dir=None):
        boxes, rg = self.mesh_voxels()
        ext, inte = [], []
        paint_faces = {"exterior": {}, "interior": {}} if self.paint_voxel is not None else None
        self.paint_face_area = {}
        slot_q = None
        if getattr(self, "slot_grid", None) is not None:
            # (0.3.20, step B) the deck cells: out of the shapes, into the mesh's slot groups
            slot_q = self.slot_quads(boxes, rg)
            for q in slot_q:
                for z in q["voxels"]:
                    self.paint_face_area[z] = self.paint_face_area.get(z, 0.0) + VOX * VOX
        for n, b in enumerate(boxes):
            if b.get("slot", -1) >= 0:
                continue
            for zone, lst in ((1, ext), (2, inte)):
                el = self._voxel_element(f"v{n}{'e' if zone == 1 else 'i'}", b, zone)
                if el and paint_faces is not None:
                    # (0.3.8) the paint zones of its faces, by the element's name and the face's (found by its cells:
                    # the boxes merge differently once her heights are moved, translate_y, so not by the box's index)
                    pf = {f: z for f in el["faces"] for z in (self._face_zone(b, f),) if z is not None}
                    for f, z in pf.items():
                        ext_ = np.asarray(b["hi"], float) - np.asarray(b["lo"], float)
                        ax_ = {"east": 0, "west": 0, "up": 1, "down": 1, "south": 2, "north": 2}[f]
                        self.paint_face_area[z] = self.paint_face_area.get(z, 0.0) + float(np.prod(np.delete(ext_, ax_)))
                    if pf:
                        paint_faces["exterior" if zone == 1 else "interior"][el["name"]] = pf
                if el:
                    lst.append(el)

        groups = {}
        for n, e in enumerate(self.elements):
            grp = e["group"]
            if grp == "auto":
                grp = "interior" if self.point_zone(e["center"], rg) else "exterior"
            groups.setdefault(grp, []).append(self._free_element(e["name"] or f"{grp}{n}", e))
        ext += groups.pop("exterior", [])
        inte += groups.pop("interior", [])

        shape_dir = os.path.join(asset_dir, "shapes", "entity", "ship", self.code)
        os.makedirs(shape_dir, exist_ok=True)
        for f in os.listdir(shape_dir):
            if f.endswith(".json"):
                os.remove(os.path.join(shape_dir, f))

        def dump(name, els):
            with open(os.path.join(shape_dir, name + ".json"), "w", encoding="utf-8") as fh:
                json.dump(self._shape(els), fh, separators=(",", ":"))

        dump("exterior", ext)
        dump("interior", inte)
        skin_path = self._write_skin(asset_dir, rg, paint_faces, slot_q)
        self.slot_quad_list = slot_q
        sail_files = {}
        for grp, els in groups.items():
            dump(grp, els)
            sail_files[grp] = f"ships:entity/ship/{self.code}/{grp}"

        # collision voxels
        solid_lut = np.array([False] + [m.solid for m in self.materials[1:]])
        solid = solid_lut[self.grid]
        bits = np.packbits(solid.flatten(order="C"), bitorder="little").tobytes()
        solid_b64 = base64.b64encode(zlib.compress(bits, 9)).decode("ascii")

        sails = []
        for s in self.sails:
            s2 = dict(s)
            if s["code"] in sail_files:      # a sail drawn as a mesh (skin group "mesh") has no shape file
                s2["shape"] = sail_files[s["code"]]
            sails.append(s2)

        rooms = [{k: v for k, v in r.items() if k != "mask"} for r in self.rooms]
        definition = {
            "code": self.code,
            "voxelSize": VOX,
            "gridOrigin": [float(x) for x in self.o],
            "gridSize": list(self.n),
            "solid": solid_b64,
            "shapes": {"exterior": f"ships:entity/ship/{self.code}/exterior",
                       "interior": f"ships:entity/ship/{self.code}/interior",
                       "skin": skin_path},
            "ladders": self.ladders,
            "rooms": rooms,
            "fixtures": self.fixtures,
            "lights": self.lights,
            "sails": sails,
            "boardPoints": self.board_points,
            "movingParts": self.moving_parts,
        }
        definition.update(self.props)
        cfg_dir = os.path.join(asset_dir, "config", "ships")
        os.makedirs(cfg_dir, exist_ok=True)
        with open(os.path.join(cfg_dir, self.code + ".json"), "w", encoding="utf-8") as fh:
            json.dump(definition, fh, indent=1)

        stats = dict(voxel_boxes=len(boxes), exterior=len(ext), interior=len(inte),
                     sails={k: len(v) for k, v in groups.items()}, solid_cells=int(solid.sum()))
        if docs_dir:
            self.render_previews(boxes, groups, docs_dir)
        return stats

    # ------------------------------------------------------------------ preview

    def _all_quads(self, boxes, sail_groups, cut=None, skip_markers=()):
        """Returns list of (corners[4x3], normal, colour). skip_markers: kinds of fixture whose marker box isn't drawn
        (view.py draws the boats from their own model)."""
        quads = []

        def box_quads(c, R, s, col, faces=None):
            hx, hy, hz = s / 2
            for fname, (dx, dy, dz) in zip(FACES, FACE_DIRS):
                if faces is not None and fname not in faces:
                    continue
                n = np.array([dx, dy, dz], float)
                # two in-plane axes
                if dx:
                    a, b = np.array([0, hy, 0]), np.array([0, 0, hz])
                elif dy:
                    a, b = np.array([hx, 0, 0]), np.array([0, 0, hz])
                else:
                    a, b = np.array([hx, 0, 0]), np.array([0, hy, 0])
                fc = n * np.array([hx, hy, hz])
                pts = [fc - a - b, fc + a - b, fc + a + b, fc - a + b]
                pts = [c + R @ p for p in pts]
                quads.append((np.array(pts), R @ n, col))

        slot_view = None
        if getattr(self, "slot_grid", None) is not None:
            # (0.3.20) the deck cells drawn as they are in self.slot_states (default: as designed), not as boxes
            slot_view = self.slot_state_quads(self.slot_quads(boxes, self._mesh_cache["rg"]))
        for b in boxes:
            if b.get("slot", -1) >= 0:
                continue
            c = (b["lo"] + b["hi"]) / 2
            if cut is not None and not cut(c):
                continue
            col = np.array(self.materials[b["mat"]].color, float)
            col = col * (0.92 + 0.08 * ((b["variant"] % 4) / 3))
            shown = [f for f in FACES if b["faces"][f] > 0]
            box_quads(c, np.eye(3), b["hi"] - b["lo"], col, shown)
        for e in self.elements:
            if cut is not None and (e["group"] not in ("exterior", "interior", "auto") or not cut(e["center"])):
                continue
            col = np.array(self.materials[self.mat_index[e["mat"]]].color, float)
            box_quads(e["center"], e["R"], e["size"], col)
        # where the mod puts vanilla blocks: marker boxes in the previews
        marker = {"door": (120, 60, 40), "window": (170, 205, 225), "lantern": (250, 200, 80),
                  "chest": (150, 95, 45), "bed": (200, 60, 55), "prop": (175, 130, 85), "boat": (140, 105, 70),
                  "plate": (200, 120, 70)}
        for f in self.fixtures:
            kind = f.get("kind") or f["type"]
            if kind not in marker or kind in skip_markers:
                continue
            b = f.get("box") or f.get("sel")
            if kind == "plate":
                # a vanilla metal plate (9/16 square, 1/16 thick) on the wall, facing its yaw; and where you come back
                # standing, a flat mark on the deck
                a = math.radians(f.get("yaw", 0.0))
                fx, fz = math.cos(a), -math.sin(a)
                p = np.array(f["pos"], float)
                c = p + np.array([fx, 0, fz]) * 0.032
                size = np.array([0.0625 if abs(fx) > 0.5 else 0.5625, 0.5625, 0.0625 if abs(fz) > 0.5 else 0.5625])
                if cut is None or cut(c):
                    box_quads(c, np.eye(3), size, np.array(marker[kind], float))
                if f.get("spawn") is not None:
                    s = np.array(f["spawn"], float) + np.array([0, 0.01, 0])
                    if cut is None or cut(s):
                        box_quads(s, np.eye(3), np.array([0.6, 0.02, 0.6]), np.array((90, 170, 230), float))
                continue
            if b is None:
                continue
            lo, hi = np.array(b[:3], float), np.array(b[3:], float)
            if kind == "bed":
                hi[1] = lo[1] + 0.55
            if kind == "boat":
                lo[1] += 0.35
            c = (lo + hi) / 2
            if cut is not None and not cut(c):
                continue
            box_quads(c, np.eye(3), np.maximum(hi - lo, 0.05), np.array(marker[kind], float))
        hs = getattr(self, "hull_states", None)
        if hs is None and self.props.get("hullSlots"):
            # ((0.3.29) as they start: a castle port's halves out where the port starts in)
            hs = [x.get("start", "filled") != "empty" for x in self.props["hullSlots"]]
        for q in self.quads + (slot_view or []):
            if q["group"].startswith("bottomclad"):
                continue
            if "slot" in q and not q["group"].startswith("slot:") and q.get("slotfam") == "deck":
                # ((0.3.27) a beam segment's timber or a carling: as the deck slots' states have it)
                ds = getattr(self, "slot_states", None)
                if ds is None:
                    ds = [x.get("start", "filled") != "empty" for x in self.props.get("deckSlots", [])]
                o, f = q["slot"]
                if not ((o < 0 or ds[o]) and (f < 0 or not ds[f])):
                    continue
            elif "slot" in q and not q["group"].startswith("slot:"):
                # (0.3.26) a hull panel's quads as its state has it (hull_states: filled per panel; default all in)
                o, f = q["slot"]
                if not ((o < 0 or hs is None or hs[o]) and (f < 0 or (hs is not None and not hs[f]))):
                    continue
            pts = np.array(q["pts"], float)
            if cut is not None and not cut(pts.mean(axis=0)):
                continue
            n = np.cross(pts[1] - pts[0], pts[3] - pts[0]) + np.cross(pts[3] - pts[2], pts[1] - pts[2])
            if np.linalg.norm(n) < 1e-9:
                continue
            col = np.array(self.materials[self.mat_index[q["mat"]]].color, float) * (0.96 + 0.04 * (q.get("row", 0) % 2))
            quads.append((pts, n / np.linalg.norm(n), col))
        return quads

    def _render(self, quads, view_R, scale, size, bg=(206, 222, 235), light=(0.4, 0.8, 0.45)):
        from PIL import Image, ImageDraw
        W, H = size
        img = Image.new("RGB", (W, H), bg)
        dr = ImageDraw.Draw(img)
        L = np.array(light, float)
        L /= np.linalg.norm(L)
        proj = []
        allp = np.concatenate([q[0] for q in quads]) @ view_R.T
        mn, mx = allp.min(axis=0), allp.max(axis=0)
        cx = (mn[0] + mx[0]) / 2
        cy = (mn[1] + mx[1]) / 2
        for pts, n, col in quads:
            vp = pts @ view_R.T
            nv = view_R @ n
            if nv[2] < -1e-3:  # facing away (camera looks down -z of view space)
                continue
            depth = vp[:, 2].mean()
            shade = 0.45 + 0.55 * max(0.0, float(np.dot(n, L)))
            c = tuple(int(min(255, x * shade)) for x in col)
            xy = [(W / 2 + (p[0] - cx) * scale, H / 2 - (p[1] - cy) * scale) for p in vp]
            proj.append((depth, xy, c))
        proj.sort(key=lambda t: t[0])
        for _, xy, c in proj:
            dr.polygon(xy, fill=c)
        return img

    def render_previews(self, boxes, sail_groups, docs_dir):
        os.makedirs(docs_dir, exist_ok=True)

        def rot(yaw, pitch):
            cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
            cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
            Ry = np.array([[cy, 0, -sy], [0, 1, 0], [sy, 0, cy]])
            Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
            return Rx @ Ry

        full = self._all_quads(boxes, sail_groups)
        views = [
            ("side", rot(0, 0), 16, (900, 640), full),
            ("oblique", rot(-35, 20), 15, (1000, 800), full),
            ("bow", rot(90, 0), 16, (520, 640), full),
        ]
        # cutaway: starboard half removed (hull and fittings only, no rigging)
        cut_q = self._all_quads(boxes, sail_groups, cut=lambda c: c[2] < 0.05 and c[1] < 12)
        views.append(("cutaway", rot(0, 0), 22, (1000, 400), cut_q))
        # deck plans (top-down) sliced at each level
        for name, ylo, yhi in (("plan-lowerdeck", -3, 2.6), ("plan-maindeck", -3, 5.6), ("plan-castles", -3, 8.6)):
            q = self._all_quads(boxes, sail_groups, cut=lambda c, a=ylo, b=yhi: a <= c[1] < b)
            views.append((name, rot(0, 90), 22, (1000, 300), q))
        # the stern (gallery windows), the waist's fittings close up, the lower deck from the side (hull cut away)
        views.append(("stern", rot(-90, 6), 34, (700, 620), self._all_quads(boxes, sail_groups, cut=lambda c: c[1] < 12.5)))
        views.append(("waist", rot(-38, 38), 40, (1300, 900),
                      self._all_quads(boxes, sail_groups, cut=lambda c: -8.0 < c[0] < 10.5 and c[1] < 8.0)))
        views.append(("lowerdeck", rot(0, 18), 34, (1300, 520),
                      self._all_quads(boxes, sail_groups, cut=lambda c: c[2] < 1.2 and -0.3 < c[1] < 3.3 and -15.5 < c[0] < 15)))
        for name, R, sc, sz, q in views:
            img = self._render(q, R, sc, sz)
            img.save(os.path.join(docs_dir, f"{self.code}-{name}.png"))


def interp(y, table):
    ys = [t[0] for t in table]
    vs = [t[1] for t in table]
    return np.interp(y, ys, vs)
