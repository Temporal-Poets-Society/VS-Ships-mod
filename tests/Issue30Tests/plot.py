"""Plot the harness's measured geometry and intake. No game client or model rebuild.

    C:/AstoCAD/bin/python.exe tests/Issue30Tests/plot.py
"""
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "obj/issue30"
OUT = ROOT / "docs/issue30"
OUT.mkdir(exist_ok=True)
cross = np.genfromtxt(DATA / "cross-section.csv", delimiter=",", names=True)
sections = np.genfromtxt(DATA / "sections.csv", delimiter=",", names=True)
sweep = json.loads((DATA / "sweep.json").read_text())
continuous = json.loads((DATA / "continuous.json").read_text())
flood = json.loads((DATA / "flood-surface.json").read_text())

plt.rcParams.update({"font.size": 11, "axes.spines.top": False, "axes.spines.right": False,
                     "figure.facecolor": "#faf9f6", "axes.facecolor": "#faf9f6"})
wood, cut, water = "#845b2d", "#00896d", "#336c8b"
fig, axs = plt.subplots(1, 2, figsize=(13, 6), gridspec_kw={"width_ratios": [1, 1.15]})
ax = axs[0]
valid = cross["inner"] > .1
c = cross[valid]
for side in (-1, 1):
    ax.plot(side*c["inner"], c["y"], color=wood, lw=3, label="Actual inner planking" if side == 1 else None)
    ax.plot(side*c["outer"], c["y"], color=wood, lw=1, alpha=.55, label="Authored outside" if side == 1 else None)
    ax.plot(side*cross["dry"], cross["y"], color=cut, lw=2, ls="--", label="Sea dry-volume boundary" if side == 1 else None)
ax.plot([-2.4041, 2.4041], [-1.875, -1.875], color="#c59a58", lw=5, label="Hold floor")
ax.axhline(.1, color="#64748b", lw=1, ls=":", label="Clip waterline")
ax.set(xlabel="Across hull (blocks)", ylabel="Ship-local height (blocks)", xlim=(-3.9, 3.9), ylim=(-3, 1),
       title="Between ribs at x = -2.5\nThe cut covers the curved bilge")
ax.set_aspect("equal", adjustable="box")
ax.legend(loc="upper center", fontsize=9, frameon=False)
ax = axs[1]
floor = sections[(np.abs(sections["y"] + 1.875) < 1e-6) & (sections["inner"] > .05)]
ax.fill_between(floor["x"], floor["inner"], floor["dry"], color=cut, alpha=.14, label="Dry-cut allowance")
ax.plot(floor["x"], floor["dry"], color=cut, lw=2, label="Sea cut at hold floor")
ax.plot(floor["x"], floor["inner"], color=wood, lw=2, label="Exact inner planking")
ax.set(xlabel="Along hull (blocks, bow to right)", ylabel="Half-width (blocks)", ylim=(0, 3.6),
       title=f'Hold floor at y = -1.875\nContinuous minimum allowance: {continuous["floor"]["Margin"]:.3f} blocks')
ax.legend(loc="lower center", frameon=False, fontsize=10)
ax.grid(alpha=.15)
fig.suptitle("Issue 30: dry-volume measurements against exported planking", fontsize=17, y=.99)
fig.text(.5, .035,
         f'{sweep["poses"]} poses: pitch -12/0/+12 deg; roll -37/-20/0/+20/+37; heave -6/0/+6; 3 yaws.\n'
         f'{sweep["boundary"]:,} planking tests + {sweep["planes"]:,} interior sea-plane tests: '
         f'{sweep["boundaryMiss"] + sweep["planeMiss"]} misses. Continuous bilge check also passes. Offline evidence; no game client.',
         ha="center", va="bottom", fontsize=10)
fig.tight_layout(rect=(0, .11, 1, .91))
fig.savefig(OUT / "dry-volume.png", dpi=160)
plt.close(fig)

rows = json.loads((DATA / "intake.json").read_text())
fig, axs = plt.subplots(1, 2, figsize=(13, 5.6))
ax = axs[0]
for side in (-1, 1):
    ax.plot(side*c["inner"], c["y"], color=wood, lw=3)
    ax.plot(side*cross["dry"], cross["y"], color=cut, lw=1.5, ls="--")
ax.plot([-2.4041, 2.4041], [-1.875, -1.875], color="#c59a58", lw=5)
level = flood["level"]
extent = float(np.interp(level, c["y"], c["inner"]))
ax.hlines(level, -extent, extent, color=water, lw=2, label=f'Hansa flat sheet: y {level:.3f}')
ax.text(0, -.9, f'{flood["volume"]:.3f} m³ stored water\nwould set this flood level', ha="center", color=water)
ax.text(0, -2.55, "Sea is deliberately uncut\nbelow the flood plane", ha="center", fontsize=10)
ax.set(xlabel="Across hull (blocks)", ylabel="Ship-local height (blocks)", xlim=(-3.6,3.6), ylim=(-2.8,.1),
       title="Two independent water paths\nA stored-water sheet is not tested by seaDry")
ax.legend(loc="upper center", frameon=False, fontsize=10)
ax = axs[1]
colors = {0:"#b75031",90:"#336c8b",180:"#9872aa",270:"#6a8535"}
for heading in (0,90,180,270):
    for speed, ls in ((0, "-"),):
        f = DATA / f"storm-1.5-{heading}-{speed:g}.csv"
        a = np.genfromtxt(f, delimiter=",", names=True)
        ax.plot(a["seconds"],a["water"],color=colors[heading],ls=ls,lw=1.5,
                label=f'{heading}° / {speed:g} b/s')
ax.axhline(.5, color="#555", lw=1, ls=":")
ax.text(238, .5, "draw threshold", ha="right", va="bottom", fontsize=9)
ax.set(xlabel="Time (seconds)", ylabel="Stored water (m³)", ylim=(0,.6),
       title="Stopped, intact hull, hatches closed, wind from E at 1.5\nThis chosen wave phase does not reproduce the live flooding")
ax.legend(fontsize=8, ncol=2, frameon=False, loc="upper left")
ax.grid(alpha=.15)
fig.suptitle("Issue 30: floodwater is separate from the ocean's dry cut",fontsize=16,y=.99)
fig.text(.5,.025,"Chosen seed 4242, launch coordinates 460545/30103. Isolated intake; no pumps, damage, sinkage/list feedback or full entity tick.\n"
         "These are diagnostic inputs, not a reconstruction of caravel 642. A sheet below the floor can be occluded; this is not a visibility render.",
         ha="center",va="bottom",fontsize=9)
fig.tight_layout(rect=(0,.12,1,.90))
fig.savefig(OUT / "water-paths.png",dpi=160)
plt.close(fig)
print(OUT / "dry-volume.png")
print(OUT / "water-paths.png")
