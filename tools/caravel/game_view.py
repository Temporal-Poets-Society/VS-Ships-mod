"""Pictures of the caravel as the game draws her (25 October 2026, chalk note 85: "Do you ever check 3d edits?"): her
exported mesh (config/shipmeshes/caravel.json) with the game's own textures, her slots as a new ship has them, her laid
pieces as plain boxes, from any eye in her ship-local frame (x fore, y up from the waterline, z to starboard).

    python tools/caravel/game_view.py [shots.json] [out dir]

shots.json: [{"name": ..., "eye": [x, y, z], "at": [x, y, z], "fov": 60}, ...] (default: SHOTS below). Writes one page
(models/caravel/export/game-view.html, its data inlined) and a PNG a shot (headless Chrome, SwiftShader) in the out dir
(default models/caravel/export/views). Set CARAVEL_THREE_JS to a local three.js bundle for offline rendering;
otherwise it loads three.js from cdnjs. CARAVEL_ROOT is not needed for this plain-Python viewer.
"""
import base64
import json
import os
import subprocess
import sys
import tempfile
import hashlib

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
VS_TEX = os.path.join(os.environ.get("APPDATA", ""), "Vintagestory", "assets", "survival", "textures")
CHROME = r"C:/Program Files/Google/Chrome/Application/chrome.exe"
W, H = 1600, 900
SHOTS = [
    {"name": "port-quarter", "eye": [-22.0, 6.0, -12.0], "at": [-6.0, 2.0, 0.0], "fov": 45},
    {"name": "starboard-bow", "eye": [16.0, 6.0, 12.0], "at": [2.0, 2.0, 0.0], "fov": 45},
    {"name": "waist-from-aft", "eye": [-6.5, 5.5, 0.0], "at": [2.0, 1.5, 0.0], "fov": 70},
]


def tex_path(code):
    dom, path = code.split(":", 1)
    if dom == "game":
        return os.path.join(VS_TEX, *path.split("/")) + ".png"
    return os.path.join(ROOT, "assets", dom, "textures", *path.split("/")) + ".png"


def data_uri(path):
    with open(path, "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode()


def rotation(axis, deg):
    import math
    a = [v / math.sqrt(sum(c * c for c in axis)) for v in axis]
    t = math.radians(deg)
    c, s_, C = math.cos(t), math.sin(t), 1 - math.cos(t)
    x, y, z = a
    return [[c + x * x * C, x * y * C - z * s_, x * z * C + y * s_],
            [y * x * C + z * s_, c + y * y * C, y * z * C - x * s_],
            [z * x * C - y * s_, z * y * C + x * s_, c + z * z * C]]


INNER = json.load(open(os.path.join(ROOT, "models", "caravel", "caravel-lines.json")))["inner"]


def inner_half(x, y):
    """Her inner half-breadth (blocks) at ship-local x, y; None above or below the inner waterlines."""
    z = (y + 2.875) * 1000.0
    zs = [l["z"] for l in INNER]
    if z < zs[0] or z > zs[-1]:
        return None
    k = max(i for i, zz in enumerate(zs) if zz <= z)
    k1 = min(k + 1, len(zs) - 1)

    def at(l):
        pts = l["pts"]
        if not pts[0][0] <= x * 1000.0 <= pts[-1][0]:
            return 0.0
        j = next(i for i in range(1, len(pts)) if pts[i][0] >= x * 1000.0)
        a, b = pts[j - 1], pts[j]
        t = (x * 1000.0 - a[0]) / ((b[0] - a[0]) or 1.0)
        return a[1] + t * (b[1] - a[1])
    t = 0.0 if k1 == k else (z - zs[k]) / (zs[k1] - zs[k])
    return ((1 - t) * at(INNER[k]) + t * at(INNER[k1])) / 1000.0


def clip_to_side(b):
    """A piece's box cut to her side's inside, as the game cuts a place in her side cells."""
    lim = [inner_half(x, y) for x in (b[0], b[3]) for y in (b[1], (b[1] + b[4]) / 2, b[4])]
    lim = [v for v in lim if v is not None]
    if lim:
        m = min(lim)
        b[2], b[5] = max(b[2], -m), min(b[5], m)
    return b


def swivel_quads(M):
    """Read the runtime's own block shape for the preview; the ship definition supplies only its sockets."""
    shape = json.load(open(os.path.join(ROOT, "assets", "hansa", "shapes", "block", "swivelgun.json")))
    result = []
    faces = {"west": (0, 0), "east": (0, 1), "down": (1, 0), "up": (1, 1), "north": (2, 0), "south": (2, 1)}
    for e in shape["elements"]:
        lo, hi = e["from"], e["to"]
        for name, f in e.get("faces", {}).items():
            axis, high = faces[name]
            other = [a for a in range(3) if a != axis]
            tex = shape["textures"][f["texture"].lstrip("#")]
            if tex not in M["textures"]:
                M["textures"].append(tex)
            q = [M["textures"].index(tex)]
            for u, v in ((0, 0), (1, 0), (1, 1), (0, 1)):
                p = list(lo)
                p[axis] = hi[axis] if high else lo[axis]
                p[other[0]] = hi[other[0]] if u else lo[other[0]]
                p[other[1]] = hi[other[1]] if v else lo[other[1]]
                n = [0, 0, 0]
                n[axis] = 1 if high else -1
                q += [p[0] / 16 - 0.5, p[1] / 16, p[2] / 16 - 0.5,
                      (hi[other[0]] - lo[other[0]]) * u, (hi[other[1]] - lo[other[1]]) * v] + n
            result.append((e["name"], q))
    return result


def page(D, M, pose=None):
    """The page's data: per texture the triangles drawn (positions, normals, UVs, shade), the textures (base and
    overlay data URIs, blend), boxes for her laid pieces. pose: {source: value}, the moving parts driven by a source
    turned by their angle times the value (a rope following one of them left out)."""
    pose = dict(pose or {})
    clad = pose.pop("clad", False)
    empty_deck, empty_hull = set(pose.pop("emptyDeck", [])), set(pose.pop("emptyHull", []))
    show_pieces = pose.pop("pieces", True)
    for f in D.get("fixtures", []):
        if f.get("type") == "gun" and f.get("housed"):
            pose.setdefault("fire:" + f["code"], 1.0)
    # (a lateen's tack: "tack" -1 her rig mirrored across each mast's centre plane, as the mod sets the other tack; "sheet"
    # degrees each yard and its sail turned about its mast toward the lee; the windward shrouds only)
    tack, sheet = pose.pop("tack", 1), pose.pop("sheet", 0.0)
    masts = {m["code"]: m["x"] for m in D.get("masts", [])}
    rig_of = {}
    for g in M["groups"]:
        for code in masts:
            if g in (f"sail:{code}", f"part:yard-{code}", f"part:sheet-{code}", f"part:tack-{code}", f"part:halyard-{code}"):
                rig_of[g] = code
    lee = -1 if tack > 0 else 1                      # (as exported her sails hang to port, game z < 0)
    turned, hidden = {}, set()
    for code in masts:
        hidden.add(f"rig:shrouds-{code}-{'port' if lee < 0 else 'starboard'}")
    import numpy as np
    by_code = {p["code"]: p for p in D.get("movingParts", [])}
    def transform(code):
        key = "part:" + code
        if key in turned:
            return turned[key]
        mp = by_code[code]
        R, t = np.eye(3), np.zeros(3)
        v = pose.get(mp.get("source"), 0)
        if mp.get("kind", "rotate") == "rotate" and v:
            R = np.array(rotation(mp["axisDir"], mp.get("angle", 1.0) * v))
            o = np.array(mp["axisPoint"])
            t = o - R @ o
        elif mp.get("kind") == "slide" and v:
            t = np.array(mp["axisDir"]) * mp["distance"] * v
        if mp.get("parent") in by_code:
            P, pt = transform(mp["parent"])
            R, t = P @ R, P @ t + pt
        turned[key] = (R, t)
        return R, t
    for code in by_code:
        transform(code)
    for mp in D.get("movingParts", []):
        if mp.get("kind") == "follow" and "part:" + mp.get("follow", "") in turned:
            R, t = turned["part:" + mp["follow"]]
            if np.linalg.norm(R - np.eye(3)) + np.linalg.norm(t) > 1e-8:
                hidden.add("part:" + mp["code"])
    deck_filled = [s.get("start") != "empty" and s["id"] not in empty_deck for s in D.get("deckSlots", [])]
    hull_filled = [s.get("start") != "empty" and s["id"] not in empty_hull for s in D.get("hullSlots", [])]
    tris = {}

    def add(q, shade, turn=None, rig=None):
        texture_index = q[0]
        c = [list(q[1 + 8 * k: 9 + 8 * k]) for k in range(4)]
        if rig is not None:
            import math
            mx = masts[rig]
            phi = math.radians(sheet) * (1 if lee > 0 else -1)
            for v in c:
                if tack < 0:
                    v[2], v[7] = -v[2], -v[7]
                dx, z = v[0] - mx, v[2]
                v[0], v[2] = mx + math.cos(phi) * dx + math.sin(phi) * z, -math.sin(phi) * dx + math.cos(phi) * z
                nx, nz = v[5], v[7]
                v[5], v[7] = math.cos(phi) * nx + math.sin(phi) * nz, -math.sin(phi) * nx + math.cos(phi) * nz
            if tack < 0:
                c = c[::-1]
        if turn:
            R, t = turn
            for v in c:
                v[0:3] = list(R @ v[:3] + t)
                v[5:8] = [sum(R[a][b] * v[5 + b] for b in range(3)) for a in range(3)]
        e = tris.setdefault(texture_index, {"p": [], "n": [], "uv": [], "s": []})
        for a, b, cc in ((0, 1, 2), (0, 2, 3)):
            if cc == 3 and c[3][:3] == c[2][:3]:
                continue
            for k in (a, b, cc):
                e["p"] += [round(v, 4) for v in c[k][:3]]
                e["n"] += c[k][5:8]
                e["uv"] += [round(c[k][3] / 16.0, 4), round(c[k][4] / 16.0, 4)]
                e["s"].append(shade)

    for g, quads in M["groups"].items():
        if g == "shot" or (g.endswith("bottomclad") if not clad else g.endswith("bottom")) or g in hidden:
            continue
        shade = 0.72 if "interior" in g else 1.0
        pairs = M.get("slotQuads", {}).get(g)
        filled = hull_filled if g.startswith("slot:hull") else deck_filled
        for n, q in enumerate(quads):
            if pairs is not None:
                own, fac = pairs[2 * n], pairs[2 * n + 1]
                if own >= 0 and not filled[own]:
                    continue
                if fac >= 0 and filled[fac]:
                    continue
            add(q, shade, turned.get(g), rig_of.get(g))
    import math
    sw = swivel_quads(M)
    for f in D.get("fixtures", []):
        if f.get("kind") != "swivel":
            continue
        code = f["code"]
        face = math.degrees(math.atan2(f["out"][0], f["out"][2]))
        train = pose.get("train:" + code, 0) * (1 if f["out"][2] >= 0 else -1)
        for name, q in sw:
            q = list(q)
            R = np.array(rotation((0, 1, 0), face + (0 if name.startswith("socket") else train)))
            for k in range(4):
                at = 1 + 8 * k
                p = np.array(q[at:at + 3])
                n = np.array(q[at + 5:at + 8])
                if name.startswith("barrel"):
                    E = np.array(rotation((1, 0, 0), -pose.get("elevate:" + code, 0)))
                    o = np.array([0, 0.2, 0])
                    p, n = E @ (p - o) + o, E @ n
                q[at:at + 3] = list(R @ p + f["pos"])
                q[at + 5:at + 8] = list(R @ n)
            add(q, 1.0)
    textures = []
    for code in M["textures"]:
        base, _, rest = code.partition("++")
        over = blend = None
        if rest:
            blend, _, over = rest.partition("~")
        textures.append({"base": data_uri(tex_path(base)), "over": data_uri(tex_path(over)) if over else None,
                         "blend": int(blend) if blend else 0})
    boxes = []
    deck_y = {d["code"]: d["y"] for d in D.get("decks", [])}
    for m in D.get("modularPieces", []) if show_pieces else []:
        x, y, z = m["at"]
        k = m["kind"]
        if k in ("partition", "window", "door"):
            h = 2.0 if k == "door" else 1.0
            # (a top row under a deck stops at its planking's underside, as the game draws it)
            above = [dy for dy in deck_y.values() if y + 0.01 < dy < y + h + 0.01]
            if above:
                h = min(above) - 0.2 - y
            if m.get("along") == "z":
                b = [x - 0.06, y, z - 0.5, x + 0.06, y + h, z + 0.5]
            else:
                b = [x - 0.5, y, z - 0.06, x + 0.5, y + h, z + 0.06]
            col = {"partition": "#b08a5a", "window": "#9cd0e8", "door": "#6a4a2a"}[k]
            boxes.append({"b": b, "c": col, "o": 0.6 if k == "window" else 1.0})
        elif k == "stair":
            boxes.append({"b": [x - 0.5, y, z - 0.4, x + 0.5, y + m.get("rise", 3.0), z + 0.4], "c": "#8a5a32", "o": 0.85})
        elif k in ("hearth", "oven"):
            boxes.append({"b": [x - 0.45, y, z - 0.45, x + 0.45, y + (0.5 if k == "hearth" else 0.75), z + 0.45],
                          "c": "#b03020" if k == "hearth" else "#c07030", "o": 1.0})
        elif k == "hatch":
            sx, sz = m.get("size", [1, 1])
            boxes.append({"b": [x - sx / 2, y - 0.05, z - sz / 2, x + sx / 2, y + 0.02, z + sz / 2], "c": "#7a5030", "o": 1.0})
    for sp in D.get("startPieces", []) if show_pieces else []:
        mnt = sp.get("mount", "")
        if not mnt.startswith("f:"):
            continue
        parts = mnt.split(":")
        i, kk = int(parts[2]), int(parts[3])
        lvl = int(parts[4]) if len(parts) > 4 else 0
        y0 = deck_y.get(parts[1], 0.0) + lvl
        boxes.append({"b": [i + 0.08, y0, kk + 0.58, i + 0.92, y0 + 0.9, kk + 1.42], "c": "#a07850", "o": 1.0})
    boxes = [bx for bx in boxes if clip_to_side(bx["b"])[5] > bx["b"][2] + 0.02]
    return {"tris": tris, "textures": textures, "boxes": boxes}


HTML = """<!doctype html><html><head><meta charset="utf-8"><title>caravel game view</title>
<style>html,body{margin:0;background:#9fc3e0;overflow:hidden}</style>
<script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script></head><body>
<script>const DATA = __DATA__;</script>
<script>
const P = new URLSearchParams(location.hash.slice(1));
const v3 = s => s.split(",").map(Number);
const renderer = new THREE.WebGLRenderer({antialias: true, preserveDrawingBuffer: true});
renderer.setSize(__W__, __H__);
document.body.appendChild(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x9fc3e0);
scene.add(new THREE.HemisphereLight(0xffffff, 0x706050, 0.85));
const sun = new THREE.DirectionalLight(0xffffff, 0.55); sun.position.set(-10, 30, -14); scene.add(sun);
// (the game's frame x fore, y up, z starboard is left-handed: z flipped for three.js)
const root = new THREE.Group(); root.scale.set(1, 1, -1); scene.add(root);
const cam = new THREE.PerspectiveCamera(+(P.get("fov") || 60), __W__ / __H__, 0.05, 400);
const eye = v3(P.get("eye") || "-22,6,-12"), at = v3(P.get("at") || "-6,2,0");
cam.position.set(eye[0], eye[1], -eye[2]); cam.lookAt(at[0], at[1], -at[2]);
function texture(t) {
  return new Promise(res => {
    const a = new Image(); a.onload = () => {
      const c = document.createElement("canvas"); c.width = a.width; c.height = a.width; const g = c.getContext("2d");
      g.drawImage(a, 0, 0, a.width, a.width);
      const done = () => { const tx = new THREE.CanvasTexture(c); tx.magFilter = THREE.NearestFilter; tx.minFilter = THREE.NearestFilter;
                           tx.wrapS = tx.wrapT = THREE.RepeatWrapping; tx.flipY = false; res(tx); };
      if (!t.over) return done();
      const b = new Image(); b.onload = () => {
        g.globalCompositeOperation = ({3: "multiply", 4: "screen", 1: "darken", 2: "lighten"})[t.blend] || "source-over";
        g.drawImage(b, 0, 0, a.width, a.width); done(); }; b.src = t.over;
    }; a.src = t.base;
  });
}
Promise.all(DATA.textures.map(texture)).then(txs => {
  for (const [t, e] of Object.entries(DATA.tris)) {
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.Float32BufferAttribute(e.p, 3));
    g.setAttribute("normal", new THREE.Float32BufferAttribute(e.n, 3));
    g.setAttribute("uv", new THREE.Float32BufferAttribute(e.uv, 2));
    const col = []; for (const s of e.s) col.push(s, s, s);
    g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
    root.add(new THREE.Mesh(g, new THREE.MeshLambertMaterial({map: txs[+t], vertexColors: true, side: THREE.DoubleSide,
                                                               transparent: false, alphaTest: 0.3})));
  }
  for (const bx of DATA.boxes) {
    const b = bx.b, m = new THREE.Mesh(new THREE.BoxGeometry(b[3] - b[0], b[4] - b[1], b[5] - b[2]),
      new THREE.MeshLambertMaterial({color: bx.c, transparent: bx.o < 1, opacity: bx.o}));
    m.position.set((b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2); root.add(m);
  }
  renderer.render(scene, cam);
  document.title = "ready";
});
</script></body></html>"""


def main():
    shots = json.load(open(sys.argv[1])) if len(sys.argv) > 1 else SHOTS
    outdir = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(ROOT, "models", "caravel", "export", "views")
    os.makedirs(outdir, exist_ok=True)
    profile = os.path.join(tempfile.gettempdir(), "caravel-preview-" + hashlib.sha256(outdir.encode()).hexdigest()[:12])
    D = json.load(open(os.path.join(ROOT, "assets", "hansa", "config", "ships", "caravel.json")))
    M = json.load(open(os.path.join(ROOT, "assets", "hansa", "config", "shipmeshes", "caravel.json")))
    pages = {}
    for s in shots:
        key = json.dumps(s.get("pose") or {}, sort_keys=True)
        if key not in pages:
            html = os.path.join(ROOT, "models", "caravel", "export", f"game-view-{len(pages)}.html")
            os.makedirs(os.path.dirname(html), exist_ok=True)
            with open(html, "w", encoding="utf-8") as f:
                template = HTML
                local_three = os.environ.get("CARAVEL_THREE_JS")
                if local_three:
                    with open(local_three, encoding="utf-8") as js:
                        template = template.replace('<script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script>', '<script>' + js.read() + '</script>')
                f.write(template.replace("__DATA__", json.dumps(page(D, M, s.get("pose")), separators=(",", ":")))
                        .replace("__W__", str(W)).replace("__H__", str(H)))
            pages[key] = "file:///" + html.replace("\\", "/").replace(" ", "%20")
    for s in shots:
        url = pages[json.dumps(s.get("pose") or {}, sort_keys=True)]
        h = "eye=" + ",".join(map(str, s["eye"])) + "&at=" + ",".join(map(str, s["at"])) + "&fov=" + str(s.get("fov", 60))
        out = os.path.join(outdir, s["name"] + ".png")
        if os.path.exists(out):
            os.remove(out)
        result = subprocess.run([CHROME, "--headless=new", "--no-sandbox", "--disable-gpu", "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
                        "--user-data-dir=" + profile,
                        f"--window-size={W},{H}", "--timeout=40000", "--virtual-time-budget=10000", "--hide-scrollbars", f"--screenshot={out}", url + "#" + h],
                       check=False, capture_output=True, timeout=300)
        if not os.path.exists(out):
            raise RuntimeError("Chrome did not render " + out + ": " + result.stderr.decode(errors="replace")[-2000:])
        from PIL import Image, ImageStat
        if max(ImageStat.Stat(Image.open(out)).stddev[:3]) < 1:
            raise RuntimeError("Blank render: " + out + "; set CARAVEL_THREE_JS to a local three.js file when offline")
        print("wrote", out, flush=True)


if __name__ == "__main__":
    main()
