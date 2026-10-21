"""Caravel rig, stability and sailing study (the user, 18 October 2026: items 7-8). Plain Python + numpy.

    python tools/caravel/rig_study.py

Reads models/caravel/caravel-lines.json (the hull's lines) and caravel-report.json (the built model: volumes,
centroids, sails, yards, masts). Writes models/caravel/rig-study.md and views/rig-study.png.

Hydrostatics come from the lines (the outer hull, cut at the transom plane), integrated on a fine grid. Weights come
from the model's own volumes with assumed densities; the ballast makes up the rest of the displacement. Everything
here is an estimate for game design, not a naval architect's calculation.
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import caravel_lines as cl  # noqa: E402

OUT = cl.OUT
MM = 1000.0
RHO_SEA, RHO_AIR, G = 1.025, 1.225, 9.81      # t/m3, kg/m3, m/s2
OAK, PINE, BRONZE = 0.75, 0.50, 8.8            # t/m3
TWO_BLOCKS = 2.0


def load():
    with open(os.path.join(OUT, "caravel-lines.json")) as f:
        lines = json.load(f)
    with open(os.path.join(OUT, "caravel-report.json")) as f:
        rep = json.load(f)
    return lines, rep


# ---------------------------------------------------------------- hydrostatics
def half_breadths(z, xs):
    wx, wy = cl.waterline(z)
    y = np.interp(xs, wx, wy, left=0.0, right=0.0)
    y[xs < cl.x_post(cl.shape_z(z))] = 0.0             # aft of the transom plane
    return y


def hydrostatics(z_wl=cl.Z_WL, dz=0.025, dx=0.02):
    xs = np.arange(-15.0, 13.0, dx)
    zs = np.arange(cl.LIFT, z_wl + 1e-9, dz)
    A, Ax, Az = [], [], []
    for z in zs:
        y = half_breadths(z, xs)
        a = np.sum(2 * y) * dx
        A.append(a)
        Ax.append(np.sum(2 * y * xs) * dx)
        Az.append(a * z)
    A, Ax, Az = map(np.array, (A, Ax, Az))
    vol = np.trapz(A, zs)
    lcb = np.trapz(Ax, zs) / vol
    kb = np.trapz(Az, zs) / vol
    y_wl = half_breadths(z_wl, xs)
    awp = np.sum(2 * y_wl) * dx
    i_t = np.sum((2.0 / 3.0) * y_wl ** 3) * dx
    x_wl = xs[y_wl > 0]
    lwl = x_wl.max() - x_wl.min()
    bwl = 2 * y_wl.max()
    lcf = np.sum(2 * y_wl * xs) * dx / awp
    i_l = np.sum(2 * y_wl * (xs - lcf) ** 2) * dx
    return {"volume": vol, "disp": vol * RHO_SEA, "lcb": lcb, "kb": kb, "bm": i_t / vol, "bml": i_l / vol,
            "awp": awp, "lwl": lwl, "bwl": bwl, "x_aft": x_wl.min(), "x_fwd": x_wl.max(), "tpc": awp * RHO_SEA / 100,
            "cb": vol / (lwl * bwl * (z_wl - cl.LIFT)), "cwp": awp / (lwl * bwl)}


def lateral_plane(z_wl=cl.Z_WL, dx=0.02):
    """The underwater profile on the centreline: area and centroid (x, z) of the hull below the waterline."""
    xs = np.arange(-15.0, 13.0, dx)
    zs = np.arange(cl.LIFT, z_wl + 1e-9, 0.02)
    inside = np.zeros((len(zs), len(xs)), bool)
    for i, z in enumerate(zs):
        inside[i] = half_breadths(z, xs) > 0.0
    cell = dx * 0.02
    area = inside.sum() * cell
    X, Z = np.meshgrid(xs, zs)
    return {"area": area, "x": float((X * inside).sum() * cell / area), "z": float((Z * inside).sum() * cell / area)}


# ---------------------------------------------------------------- weights
def weights(rep, hs, crew=20, days=20):
    mp = rep["massProps"]

    def item(name, density, note):
        v = mp[name]["volume_m3"]
        c = np.array(mp[name]["centroid_mm"]) / MM
        return {"item": name, "mass": v * density, "x": c[0], "z": c[2], "note": note}

    items = [item(n, OAK, "oak 0.75 t/m3") for n in ("Shell", "Backbone", "Ribs", "Beams", "Decks", "Railings",
                                                     "Stairs", "Gates", "Fittings")]
    rig = item("Rig_Latina", PINE, "pine 0.50 t/m3 (masts, yards; sails ~0.6 kg/m2)")
    items.append(rig)
    guns = mp["Guns"]
    n_guns = len(rep["guns"]) * 2
    items.append({"item": "Guns", "mass": n_guns * 0.9, "x": guns["centroid_mm"][0] / MM, "z": guns["centroid_mm"][2] / MM,
                  "note": f"{n_guns} light guns, ~0.65 t bronze + 0.25 t carriage each"})
    items.append({"item": "Crew", "mass": crew * 0.08, "x": -4.0, "z": cl.Z_DECK + 1.0, "note": f"{crew} at 80 kg"})
    items.append({"item": "Stores", "mass": crew * days * 0.006, "x": -2.0, "z": 2.0,
                  "note": f"{days} days: water and food, 6 kg a person a day"})
    fixed = sum(i["mass"] for i in items)
    ballast = hs["disp"] - fixed
    items.append({"item": "Ballast and cargo", "mass": ballast, "x": hs["lcb"], "z": 1.1,
                  "note": "whatever makes up the displacement; stone on the hold floor"})
    m = sum(i["mass"] for i in items)
    kg = sum(i["mass"] * i["z"] for i in items) / m
    lcg = sum(i["mass"] * i["x"] for i in items) / m
    return items, m, kg, lcg


# ---------------------------------------------------------------- sails
def poly_area_centroid(pts):
    p = np.array(pts, float) / MM
    o = p.mean(axis=0)
    area, cen = 0.0, np.zeros(3)
    for k in range(len(p)):
        a, b = p[k] - o, p[(k + 1) % len(p)] - o
        t = 0.5 * np.linalg.norm(np.cross(a, b))
        area += t
        cen += t * (o + (a + b) / 3)
    return area, cen / area


def rig_summary(rig):
    sails = []
    for s in rig["sails"]:
        a, c = poly_area_centroid(s["pts"])
        sails.append({"name": s["name"], "area": a, "x": c[0], "z": c[2], "pts": np.array(s["pts"], float) / MM})
    total = sum(s["area"] for s in sails)
    ce_x = sum(s["area"] * s["x"] for s in sails) / total
    ce_z = sum(s["area"] * s["z"] for s in sails) / total
    return sails, total, ce_x, ce_z


def deck_under(x, y, hs):
    """The deck under (x, y), from that deck's own footprint: the quarterdeck aft of its front, else the main deck."""
    z = cl.Z_QD if x <= cl.QD_FRONT else cl.Z_DECK
    if x < cl.x_post(cl.shape_z(z)) or x > cl.x_stem(cl.shape_z(z)):
        return None
    wx, wy = cl.waterline(z)
    if abs(y) > np.interp(x, wx, wy):
        return None
    return z


def swing(rig, sails, hs, arc=180, step=5):
    """Each lateen yard turns about its mast (the sail with it). For every bearing, the lowest head room over a deck
    under the foot, and whether the sail passes through another mast. Up to abeam either side: going about, a
    lateen yard is dipped round its mast, not swung further."""
    masts = {m["name"]: m for m in rig["masts"]}
    out = []
    for yd in rig["yards"]:
        sail = next(s for s in sails if s["name"] == yd["name"])
        mx = yd["mastX"] / MM
        p = sail["pts"]
        # sample the sail's surface
        samples = []
        for u in np.linspace(0, 1, 25):
            for v in np.linspace(0, 1 - u, max(2, int(25 * (1 - u)))):
                q = p[0] + u * (p[1] - p[0]) + v * (p[2] - p[0])
                samples.append(q)
        foot = [p[0] + t * (p[2] - p[0]) for t in np.linspace(0, 1, 40)]
        r_s = np.array([q[0] - mx for q in samples])
        z_s = np.array([q[2] for q in samples])
        worst = (None, None)
        for ang in range(-arc // 2, arc // 2 + 1, step):
            a = math.radians(ang)
            for q in foot:
                r = q[0] - mx
                x, y = mx + r * math.cos(a), r * math.sin(a)
                d = deck_under(x, y, hs)
                if d is not None and (worst[0] is None or q[2] - d < worst[0]):
                    worst = (q[2] - d, ang)
        clash = []
        for name, m in masts.items():
            ox = m["x"] / MM
            if abs(ox - mx) < 0.5:
                continue
            hit_z = None
            for ang in range(-arc // 2, arc // 2 + 1, step):
                a = math.radians(ang)
                px, py = mx + r_s * math.cos(a), r_s * math.sin(a)
                near = np.hypot(px - ox, py) < (m["r0"] / MM + 0.1)
                below = near & (z_s < m["top"] / MM) & (z_s > m["heel"] / MM)
                if below.any():
                    zz = float(z_s[below].min())
                    hit_z = zz if hit_z is None else min(hit_z, zz)
            clash.append({"mast": name, "distance": abs(ox - mx), "mastTop": m["top"] / MM, "clears": hit_z is None,
                          "hitZ": hit_z})
        out.append({"yard": yd["name"], "minHeadRoom": worst[0], "atBearing": worst[1], "masts": clash})
    return out


# ---------------------------------------------------------------- rig options (same rules as build_caravel.py)
HEAD = 2.2
OPTIONS = [
    ("A (photo slings)", {"main_clew": (-2.0, 6.4), "main_tack": (8.5, 6.2), "main_sling": 1 / 3, "miz_yard": 0.65, "miz_angle": 55}),
    ("B main clew aft", {"main_clew": (-6.5, 9.8), "main_tack": (8.5, 6.2), "main_sling": 1 / 3, "miz_yard": 0.65, "miz_angle": 55}),
    ("C B + smaller mizzen", {"main_clew": (-6.5, 9.8), "main_tack": (8.5, 6.2), "main_sling": 1 / 3, "miz_yard": 0.50, "miz_angle": 55}),
    ("D C + tack at stem head", {"main_clew": (-6.5, 9.8), "main_tack": (9.6, 6.2), "main_sling": 1 / 3, "miz_yard": 0.50, "miz_angle": 55}),
    ("E D + yard slung a quarter down", {"main_clew": (-6.5, 9.8), "main_tack": (9.6, 6.2), "main_sling": 1 / 4, "miz_yard": 0.50, "miz_angle": 55}),
]


def option_rig(L, o):
    """The latina rig of one option: masts, yards and sails as build_caravel.py makes them (metres)."""
    def lateen(mx, cross, tack, yard_len, clew):
        dx, dz = tack[0] - mx, cross - tack[1] - 0.1
        fore = math.hypot(dx, dz)
        a = math.atan2(dz, dx)
        aft = yard_len - fore
        t = (mx + fore * math.cos(a), cross - fore * math.sin(a))
        pk = (mx - aft * math.cos(a), cross + aft * math.sin(a))
        pts = [(t[0], 0.52, t[1] - 0.1), (pk[0] + 0.3 * math.cos(a), 0.52, pk[1] - 0.3 * math.sin(a)), (clew[0], 0.52, clew[1])]
        return pts, t, pk
    main_x, miz_x = -0.5, -8.5
    m_len = 0.80 * L
    m_top = 1.0 + m_len
    m_cross = m_top - o["main_sling"] * m_len
    z_len = 0.50 * L
    z_top = 3.4 + z_len
    z_cross = z_top - o.get("miz_sling", 0.40) * z_len
    z_tack = (miz_x + (z_cross - cl.Z_QD - HEAD - 0.1) / math.tan(math.radians(o["miz_angle"])), cl.Z_QD + HEAD)
    z_clew = (cl.stern_line(cl.Z_QD) - 2.0, cl.Z_QD + HEAD + 0.2)
    mp, mt, mpk = lateen(main_x, m_cross, o["main_tack"], 1.05 * L, o["main_clew"])
    zp, zt, zpk = lateen(miz_x, z_cross, z_tack, o["miz_yard"] * L, z_clew)
    sc = lambda q: [round(c * MM) for c in q]
    return {"masts": [{"name": "main", "x": main_x * MM, "heel": 1000, "top": m_top * MM, "r0": 280},
                      {"name": "mizzen", "x": miz_x * MM, "heel": 3400, "top": z_top * MM, "r0": 180}],
            "sails": [{"name": "main", "pts": [sc(q) for q in mp]}, {"name": "mizzen", "pts": [sc(q) for q in zp]}],
            "yards": [{"name": "main", "mastX": main_x * MM, "tack": sc(mt), "peak": sc(mpk), "cross": [0, m_cross * MM],
                       "length": 1.05 * L * MM},
                      {"name": "mizzen", "mastX": miz_x * MM, "tack": sc(zt), "peak": sc(zpk), "cross": [0, z_cross * MM],
                       "length": o["miz_yard"] * L * MM}]}


def stair_clearance(sails):
    """Lowest margin of any sail over a player's head on the quarterdeck stair or at its foot (2 m over the treads)."""
    worst = None
    for s in sails:
        p = s["pts"]
        for x in np.linspace(-5.0, -3.0, 41):
            head = cl.Z_DECK + TWO_BLOCKS + max(0.0, min(1.0, -4.0 - x)) * (cl.Z_QD - cl.Z_DECK)
            zs = []
            for k in range(len(p)):
                a, b = p[k], p[(k + 1) % len(p)]
                if (a[0] - x) * (b[0] - x) <= 0 and a[0] != b[0]:
                    zs.append(a[2] + (b[2] - a[2]) * (x - a[0]) / (b[0] - a[0]))
            if zs:
                d = min(zs) - head
                worst = d if worst is None else min(worst, d)
    return worst


def options_table(rep, hs, lp, gm):
    L = rep["hullLength_mm"] / MM
    rows = []
    for name, o in OPTIONS:
        rig = option_rig(L, o)
        sails, sa, ce_x, ce_z = rig_summary(rig)
        sw = swing(rig, sails, hs)
        lead = (ce_x - lp["x"]) / hs["lwl"]
        f = 0.5 * RHO_AIR * 12 * 12 * 1.2 * sa / 1000.0
        heel12 = math.degrees(math.atan(f * (ce_z - lp["z"]) / (hs["disp"] * G * gm)))
        rows.append({"name": name, "main": sails[0]["area"], "miz": sails[1]["area"], "sa": sa,
                     "sad": sa / hs["volume"] ** (2 / 3), "lead": lead * 100, "heel12": heel12,
                     "head": min(s["minHeadRoom"] for s in sw), "masts": all(c["clears"] for s in sw for c in s["masts"]),
                     "stair": stair_clearance(sails), "o": o})
    return rows


def main():
    lines, rep = load()
    hs = hydrostatics()
    lp = lateral_plane()
    items, m, kg, lcg = weights(rep, hs)
    gm = hs["kb"] + hs["bm"] - kg
    res = {}
    for rig_name in ("latina", "redonda"):
        rig = rep["rigGeometry"][rig_name]
        sails, sa, ce_x, ce_z = rig_summary(rig)
        lead = (ce_x - lp["x"]) / hs["lwl"]
        arm = ce_z - lp["z"]
        heel = {}
        for v in (5.0, 8.0, 12.0, 16.0):
            f = 0.5 * RHO_AIR * v * v * 1.2 * sa / 1000.0       # kN, beam wind, Cf 1.2
            mom = f * arm                                    # kN m
            heel[v] = math.degrees(math.atan(mom / (hs["disp"] * G * gm)))
        sw = swing(rig, sails, hs) if rig["yards"] else []
        res[rig_name] = {"sails": sails, "sa": sa, "ce_x": ce_x, "ce_z": ce_z, "lead": lead, "arm": arm, "heel": heel,
                         "swing": sw, "sad": sa / hs["volume"] ** (2 / 3), "rig": rig}
    hull_speed_kn = 2.43 * math.sqrt(hs["lwl"])
    opts = options_table(rep, hs, lp, gm)
    write_md(lines, rep, hs, lp, items, m, kg, lcg, gm, res, hull_speed_kn, opts)
    plot(rep, hs, lp, res)


SWEEP_MD = """## Sweep: higher, steeper yards and a sloped foot (the user, 18 October)

Asked: a steeper yard, slung higher for more area, the main sail's foot sloping up aft, the main yard reaching
further to the bow. Every combination was checked against the same rules (2.2 m head room over the decks, normal
and swung up to abeam; no sail through the other mast; the quarterdeck stair clear). Main sail, 64 combinations,
24 pass; mizzen with the chosen main, 32, all pass.

- **Higher and steeper pays:** the main slung 1/8 of its mast down instead of 1/3 grows its sail 48 % (79 → 117 m²);
  its peak rises ~4.8 m over the masthead, as on the replica.
- **A sloped foot** (tack low at the stem head, clew higher aft) helps up to a clew at (−4.0, 7.0), over the waist
  above the stair's approach. Further aft over the quarterdeck the clew must be 9.4 high and loses area again.
- **Past the bow** (tack 1–2 m ahead of the stem, lower over the water) fails: swung toward abeam, the low tack
  comes in over the waist under 2.2 m. The stem head is as far as it goes.
- **A steep, high mizzen yard** (slung 20 % down, 72°, 60 % of the hull) keeps its sail over the quarterdeck
  instead of 2.5 m past the transom, for a little more area.

Chosen by the user next (18 October): main slung 1/8 down, tack at the stem head, clew (−4.0, 7.0); mizzen slung
20 % down at 72°, yard 60 %: 127 → 171 m², lead −20.9 → −11.7 % of LWL. **Superseded**: its mizzen yard crossed
the mast 1.4 m from its tack, a yard that does not work unbalanced on its mast (the user).

## The user's sketch (18 October), now built

Each yard balanced on its mast, crossing it near halfway; the mizzen's sail with a level foot over the
quarterdeck and its after edge over the stern; the main's foot sloping up aft. Taken from the sketch at the
model's scale (mm): main tack (8100, 6200), peak (−8700, 29200), clew (−3800, 9000), mast to 24.8 m; mizzen tack
(−4600, 9200), peak (−14000, 25700), clew (−14000, 9200), mast to 21.8 m. The masts are taller than the photos'
80 % and 50 % (about 101 % and 78 %): the mizzen's yard now crosses its mast at 16.1 m, above its old masthead.
The main's clew and the mizzen's tack lie 0.8 m apart; set on the same side they do not touch, but swung
differently they could.
"""


def write_md(lines, rep, hs, lp, items, m, kg, lcg, gm, res, vh, opts):
    L = rep["hullLength_mm"] / MM
    o = []
    w = o.append
    w("# Caravel rig, stability and sailing study")
    w("")
    w("Status: **study**, 18 October 2026, from `tools/caravel/rig_study.py` on the built model. Estimates for game "
      "design; densities and loads are assumptions, stated where used.")
    w("")
    lat, red = res["latina"], res["redonda"]
    w("## Findings")
    w("")
    w(f"1. **Stability is sound.** GM {gm:.2f} m with the ballast low; under all sail, beam wind, she heels "
      f"{lat['heel'][12.0]:.1f}° at 12 m/s (latina) or {red['heel'][12.0]:.1f}° (redonda).")
    w(f"2. **The lateen sails: {lat['sa']:.0f} m² (SA/D^⅔ {lat['sad']:.1f}).** The 2-block head room keeps each "
      "sail's foot 2.2 m over the deck (9.2 m up over the quarterdeck), so the lowest, widest part of a lateen sail "
      "is lost: about two thirds of a historical caravel's sail remains. The redonda's square sails keep theirs "
      f"({red['sa']:.0f} m²). In game, sail force can be tuned apart from the drawn area.")
    w(f"3. **Balance.** The latina's sail centre is {lat['lead'] * 100:+.1f} % of LWL from the hull's "
      f"({lat['ce_x'] - lp['x']:+.2f} m): weather helm, milder than the photo slings' (−20.9 %).")
    w("4. **Swinging is clear.** Up to abeam either side no sail comes within 2.2 m of a deck and none passes "
      "through the other mast; the main's clew stays above a player on the quarterdeck's stair.")
    w("")
    w("## Hull")
    w("")
    w("| | |")
    w("|---|---|")
    w(f"| Hull length (transom top to stem head) | {L:.2f} m |")
    w(f"| Waterline length × beam | {hs['lwl']:.2f} × {hs['bwl']:.2f} m |")
    w(f"| Draft | {cl.Z_WL - cl.LIFT:.3f} m |")
    w(f"| Displacement | {hs['volume']:.1f} m³, **{hs['disp']:.0f} t** in seawater |")
    w(f"| Block / waterplane coefficient | {hs['cb']:.2f} / {hs['cwp']:.2f} |")
    w(f"| Centre of buoyancy | X {hs['lcb']:.2f}, KB {hs['kb']:.2f} m over the bottom border |")
    w(f"| BM (transverse) | {hs['bm']:.2f} m |")
    w(f"| Tonnes per cm of immersion | {hs['tpc']:.2f} t |")
    w(f"| Lateral plane (underwater profile) | {lp['area']:.1f} m², centre X {lp['x']:.2f}, Z {lp['z']:.2f} |")
    w(f"| Hull speed (1.34 √LWL ft) | {vh:.1f} kn ({vh * 0.5144:.1f} m/s) |")
    w("")
    w("## Weights and stability")
    w("")
    w("| Item | Mass t | X m | Z m | Basis |")
    w("|---|---|---|---|---|")
    for i in items:
        w(f"| {i['item']} | {i['mass']:.1f} | {i['x']:.2f} | {i['z']:.2f} | {i['note']} |")
    w(f"| **Total** | **{m:.0f}** | {lcg:.2f} | **KG {kg:.2f}** | |")
    w("")
    w(f"**GM = KB + BM − KG = {hs['kb']:.2f} + {hs['bm']:.2f} − {kg:.2f} = {gm:.2f} m.** "
      f"Trim: LCG {lcg:.2f} against LCB {hs['lcb']:.2f}; the ballast's X is set to the LCB, so any trim comes from the "
      "rest.")
    w("")
    struct = sum(i["mass"] for i in items if i["note"].startswith("oak"))
    w(f"The hull's timber weighs {struct:.0f} t: the 0.2-block planking and timbers are two to three times a real "
      "caravel's (planks 6–8 cm), so she needs less ballast than a real one, and her weight sits higher. A lighter "
      "density for the planking would only change the ballast line.")
    w("")
    for name, r in res.items():
        w(f"## Rig: {name}")
        w("")
        w("| Sail | Area m² | Centre X | Centre Z |")
        w("|---|---|---|---|")
        for s in r["sails"]:
            w(f"| {s['name']} | {s['area']:.1f} | {s['x']:.2f} | {s['z']:.2f} |")
        w(f"| **Total** | **{r['sa']:.0f}** | **CE {r['ce_x']:.2f}** | **{r['ce_z']:.2f}** |")
        w("")
        w(f"- Sail area / displacement^(2/3): **{r['sad']:.1f}** (traditional cargo ships ~10–15, fast "
          "sailing ships 15–20).")
        w(f"- Lead of the sail centre over the lateral plane's: {r['lead'] * 100:+.1f} % of LWL "
          f"({r['ce_x'] - lp['x']:+.2f} m). Positive: forward, lee helm tendency; a small negative to small positive "
          "value balances a traditional hull.")
        w(f"- Heeling arm (sail centre over lateral plane centre): {r['arm']:.2f} m.")
        w("- Steady heel, wind on the beam, all sail set: " +
          ", ".join(f"{v:.0f} m/s → {h:.1f}°" for v, h in r["heel"].items()) + ".")
        if r["swing"]:
            w("")
            w("| Yard | Lowest head room over a deck, swung up to abeam | Other masts, swung up to abeam |")
            w("|---|---|---|")
            for s in r["swing"]:
                masts = "; ".join(f"{c['mast']} ({c['distance']:.1f} m away, top {c['mastTop']:.1f} m): " +
                                  ("clear" if c["clears"] else f"HIT from Z {c['hitZ']:.1f}")
                                  for c in s["masts"]) or "none in reach"
                hr = f"{s['minHeadRoom']:.2f} m (bearing {s['atBearing']}°)" if s["minHeadRoom"] is not None else "—"
                w(f"| {s['yard']} | {hr} | {masts} |")
        w("")
        rig = r["rig"]
        w("| Mast | X | Heel Z | Top Z | Length | % of hull |")
        w("|---|---|---|---|---|---|")
        for mm_ in rig["masts"]:
            ln = (mm_["top"] - mm_["heel"]) / MM
            w(f"| {mm_['name']} | {mm_['x'] / MM:.1f} | {mm_['heel'] / MM:.2f} | {mm_['top'] / MM:.2f} | {ln:.2f} m | "
              f"{ln / L * 100:.0f} % |")
        if rig["yards"]:
            w("")
            w("| Yard | Length | % of hull | Slung at Z | Tack (X, Z) | Peak (X, Z) |")
            w("|---|---|---|---|---|---|")
            for y in rig["yards"]:
                w(f"| {y['name']} | {y['length'] / MM:.1f} m | {y['length'] / MM / L * 100:.0f} % | {y['cross'][1] / MM:.2f} | "
                  f"{y['tack'][0] / MM:.1f}, {y['tack'][1] / MM:.1f} | {y['peak'][0] / MM:.1f}, {y['peak'][1] / MM:.1f} |")
        w("")
    w("## Options for the latina rig")
    w("")
    w("Each keeps the user's proportions (main mast 80 % of the hull, yard 105 %; mizzen mast 50 %), 2.2 m of head "
      "room over every deck in its normal position and swung up to abeam, and no sail through the other mast. "
      "Stair: the lowest margin of any sail over a player's head on the quarterdeck stair or its approach.")
    w("")
    w("| Option | Main m² | Mizzen m² | Total m² | SA/D^⅔ | CE lead % LWL | Heel at 12 m/s | Head room | Masts | Stair margin |")
    w("|---|---|---|---|---|---|---|---|---|---|")
    for r in opts:
        st = f"{r['stair']:+.2f} m" if r["stair"] is not None else "—"
        w(f"| {r['name']} | {r['main']:.0f} | {r['miz']:.0f} | {r['sa']:.0f} | {r['sad']:.1f} | {r['lead']:+.1f} | "
          f"{r['heel12']:.1f}° | {r['head']:.2f} m | {'clear' if r['masts'] else 'HIT'} | {st} |")
    w("")
    w("- **B**: the main's clew goes aft over the quarterdeck's front at 9.8 m (2.8 over its deck, clear of the stair's "
      "top), still forward of the mizzen.")
    w("- **C**: the mizzen's yard 50 % of the hull instead of 65 %: less sail aft.")
    w("- **D**: the main's tack at the stem head instead of 1.1 m inside it.")
    w("- **E**: the main yard slung a quarter of the mast down instead of a third: the yard sits higher and steeper.")
    w("")
    w(SWEEP_MD)
    w("## Reading it")
    w("")
    w("- **Swing.** A lateen yard turns about its mast with the sail; the check turns each one up to abeam either side in "
      "5° steps and samples its foot over the decks. Rigging ropes are left out: players pass through them.")
    w("- **Head room** is measured from the deck under each point of the sail's foot; the target is 2.2 m (2 blocks "
      "and a margin).")
    w("- **Heel** uses a flat-plate force (Cf 1.2) on all sail with the wind on the beam, and small-angle stability "
      "(GM). Real ships reefed or struck sail long before large angles.")
    w("- **Game speed.** The hull speed is the natural top speed in displacement mode; a game maximum near it, scaled "
      "by the carrack's, keeps the two consistent.")
    with open(os.path.join(OUT, "rig-study.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(o) + "\n")
    print("wrote", os.path.join(OUT, "rig-study.md"))


def plot(rep, hs, lp, res):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return
    fig, axes = plt.subplots(2, 2, figsize=(15, 11), gridspec_kw={"height_ratios": [1.3, 1]})
    for col, name in enumerate(("latina", "redonda")):
        ax = axes[0][col]
        r = res[name]
        sx = np.array([q[0] for q in json.load(open(os.path.join(OUT, "caravel-lines.json")))["sheer"]]) / MM
        sz = np.array([q[1] for q in json.load(open(os.path.join(OUT, "caravel-lines.json")))["sheer"]]) / MM
        keep = (sx > cl.x_post(cl.shape_z(7.0))) & (sx < cl.x_stem(cl.shape_z(6.0)))
        ax.plot(sx[keep], sz[keep], "k", lw=1)
        zz = np.linspace(0, 6.0, 40)
        ax.plot([cl.x_stem(cl.shape_z(z)) for z in zz], zz, "k", lw=1)
        zz = np.linspace(cl.LIFT, cl.Z_QD, 40)
        ax.plot([cl.x_post(cl.shape_z(z)) for z in zz], zz, "k", lw=1)
        ax.axhline(cl.Z_WL, color="b", lw=0.6)
        for s in r["sails"]:
            p = s["pts"]
            ax.fill(p[:, 0], p[:, 2], alpha=0.25)
            ax.plot(s["x"], s["z"], "k+")
        for m in r["rig"]["masts"]:
            ax.plot([m["x"] / MM] * 2, [m["heel"] / MM, m["top"] / MM], color="saddlebrown", lw=2)
        ax.plot(r["ce_x"], r["ce_z"], "ro", label="sail centre (CE)")
        ax.plot(lp["x"], lp["z"], "bs", label="lateral plane centre (CLR)")
        for z0, x0, x1 in ((cl.Z_DECK + TWO_BLOCKS, cl.QD_FRONT, 10), (cl.Z_QD + TWO_BLOCKS, -14, cl.QD_FRONT)):
            ax.plot([x0, x1], [z0, z0], "g--", lw=0.8)
        ax.set_title(f"{name}: sails, CE and CLR; green: 2 blocks over the decks")
        ax.set_aspect("equal")
        ax.grid(True, color="0.9")
        ax.legend(loc="upper left", fontsize=8)
        # plan: swing circles of the lateen yards' tacks and peaks
        ax = axes[1][col]
        wx, wy = cl.waterline(cl.Z_DECK)
        ax.plot(wx, wy, "k", lw=1)
        ax.plot(wx, -wy, "k", lw=1)
        for y in r["rig"]["yards"]:
            mx = y["mastX"] / MM
            for key, c in (("tack", "tab:green"), ("peak", "tab:red")):
                rr = abs(y[key][0] / MM - mx)
                t = np.linspace(0, 2 * np.pi, 120)
                ax.plot(mx + rr * np.cos(t), rr * np.sin(t), color=c, lw=0.8)
        for m in r["rig"]["masts"]:
            ax.plot(m["x"] / MM, 0, "o", color="saddlebrown")
        ax.set_title(f"{name}: swing circles of the yards' tacks (green) and peaks (red)")
        ax.set_aspect("equal")
        ax.grid(True, color="0.9")
    fig.tight_layout()
    fig.savefig(os.path.join(OUT, "views", "rig-study.png"), dpi=100)
    print("wrote views/rig-study.png")


if __name__ == "__main__":
    main()
