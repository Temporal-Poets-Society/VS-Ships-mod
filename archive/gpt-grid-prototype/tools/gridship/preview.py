"""Review artifacts built from the same compiled faces as the construction data."""
import copy
import json
from pathlib import Path

def write_preview(data, destination):
    from compile import compile_blueprint
    broad=copy.deepcopy(data["spec"]); broad["ribVisibleWidth"]=1
    broad_data=compile_blueprint(broad)
    template=Path(__file__).with_name("viewer.template.html.in").read_text(encoding="utf-8")
    payload=json.dumps({"slim":data,"full":broad_data},separators=(",",":"))
    destination.parent.mkdir(parents=True,exist_ok=True)
    destination.write_text(template.replace("__BLUEPRINT_DATA__",payload.replace("</","<\\/")),encoding="utf-8")

def write_images(data, destination):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Polygon, Rectangle
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    from compile import section_width
    spec=data["spec"]
    colors={"hull":"#ad7953","rib":"#ddaf6d","beam":"#b8894f","deck":"#cfac7a","keel":"#79563d"}
    destination.mkdir(parents=True,exist_ok=True)
    fig,ax=plt.subplots(figsize=(11,7),layout="constrained")
    fig.patch.set_facecolor("#edf1ef");ax.set_facecolor("#edf1ef")
    profile=spec["halfSection"]
    for side in (-1,1):
        for (ya,wa),(yb,wb) in zip(profile,profile[1:]):
            t=spec["shellThicknessAcross"]
            ax.add_patch(Polygon([(side*wa,ya),(side*wb,yb),(side*(wb-t),yb),(side*(wa-t),ya)],color=colors["hull"]))
            ax.add_patch(Polygon([(side*(wa-t),ya),(side*(wb-t),yb),(side*max(0,wb-t-spec["ribDepth"]),yb),(side*max(0,wa-t-spec["ribDepth"]),ya)],color=colors["rib"]))
    for deck in spec["decks"]:
        y=deck["top"]; low=y-spec["deckThickness"]
        w=section_width(spec,low)-spec["shellThicknessAcross"]; wt=section_width(spec,y)-spec["shellThicknessAcross"]
        ax.add_patch(Polygon([(-w,low),(-wt,y),(wt,y),(w,low)],color=colors["deck"]))
        if y>1:
            beam_low=low-spec["beamDepth"]; wb=section_width(spec,beam_low)-spec["shellThicknessAcross"]
            ax.add_patch(Polygon([(-wb,beam_low),(-w,low),(w,low),(wb,beam_low)],color=colors["beam"]))
        ax.text(0,y+0.12,f'{deck["code"].capitalize()} · floor {y}',ha="center",va="bottom",fontsize=11)
    ax.add_patch(Rectangle((-.5,0),1,1,color=colors["keel"]))
    by,bw=profile[0]
    for side in (-1,1):
        ax.add_patch(Rectangle((min(side*.5,side*bw),by),bw-.5,spec["shellThicknessAcross"],color=colors["hull"]))
    for x in [i-.5 for i in range(-7,9)]: ax.axvline(x,color="#849a94",alpha=.4,lw=.6,zorder=10)
    for y in range(9): ax.axhline(y,color="#849a94",alpha=.4,lw=.6,zorder=10)
    ax.axhline(spec["waterline"],color="#497b91",ls="--",lw=1.4)
    ax.text(-6.9,spec["waterline"]+.08,"Design waterline",color="#355e70",fontsize=10)
    ax.set(xlim=(-7,7),ylim=(-.15,8.5),xlabel="Across the ship (blocks)",ylabel="Height on the blueprint grid (blocks)")
    ax.set_aspect("equal");ax.set_title("Hansa · proposed grid cross-section",fontsize=18,pad=17)
    fig.savefig(destination/"cross-section.png",dpi=150);plt.close(fig)

    fig=plt.figure(figsize=(12,8),layout="constrained");fig.patch.set_facecolor("#edf1ef")
    ax=fig.add_subplot(111,projection="3d");ax.set_facecolor("#edf1ef")
    polygons=[];face_colors=[]
    for cell in data["cells"]:
        for face in cell["faces"]:
            if face.get("facing"): continue
            if face["kind"]=="hull" and cell["cell"][2]>=0: continue
            if face["kind"]=="deck" and cell["cell"][1]==6 and cell["cell"][2]>=0: continue
            polygons.append([(p[0],p[2],p[1]) for p in face["points"]])
            face_colors.append(colors[face["kind"]])
    ax.add_collection3d(Poly3DCollection(polygons,facecolors=face_colors,edgecolors="#493c2d",linewidths=.12))
    ax.set(xlim=(0,9),ylim=(-7,7),zlim=(0,8),xlabel="Length (blocks)",ylabel="Across (blocks)",zlabel="Height (blocks)")
    ax.set_box_aspect((9,14,8));ax.view_init(elev=25,azim=-42)
    ax.set_title("Hansa · two bays, three rib cells\nR PPP R PPP R",fontsize=17,pad=15)
    fig.text(.5,.005,"Cutaway: skin and half the upper floor omitted for inspection. Open ends; no mast, bow, stern or sailing validation yet.",ha="center",fontsize=10)
    fig.savefig(destination/"two-bay.png",dpi=150);plt.close(fig)
