"""Textured ship-style renders, using the preserved preview depth-buffer renderer.

python tools/gridship/render.py [cutaway|structure|section|lower|hold|stair|compare]
Faces are split into atlas tiles before the legacy renderer samples them; its
clamping is not allowed to stretch or flatten unwrapped texture coordinates.
"""
import copy
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from geometry import box,normal,metric_uv,mean,dot,sub
from atlas import tiles
from compile import compile_blueprint

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools/shipgen"))
import texview

OUT=ROOT/"docs/prototype/renders"
TIMBER="game:block/wood/debarked/oak"
PLANK="game:block/wood/planks/oak1"
DECK=PLANK+"++0~hansa:block/caulk"
TAR=PLANK+"++3~hansa:block/tar"
CAMERAS={
    "cutaway":((18,13,19),(4.5,4,0),52),
    "structure":((18,13,19),(4.5,4,0),52),
    "section":((24,4.3,0),(4.5,4.3,0),42),
    "lower":((9.7,5.7,3.8),(.8,5.2,3.8),78),
    "hold":((9.7,2.4,0),(.5,2.25,0),78),
    "stair":((7.5,10.5,5.5),(2.5,5.5,2),63),
}

def append_face(result,points,uv,texture):
    for polygon,coordinates in tiles(points,uv):
        for i in range(1,len(polygon)-1):
            indices=[0,i,i+1,i+1]
            result.append((np.asarray([polygon[k] for k in indices],float),
                           np.asarray([coordinates[k] for k in indices],float),texture))

def extra_solid(result,faces,texture,grain=(0,0,1)):
    for face in faces: append_face(result,face,metric_uv(face,grain),texture)

def stair_geometry(result):
    for n in range(12):
        top=4+(n+1)*.25;z=.5+n*.25
        extra_solid(result,box(2.125,top-.1,z,2.875,top,z+.25),DECK)
    # Square handrails and stringers, with the grain following their slope.
    a,b=np.array([0.,4.,.5]),np.array([0.,7.,3.5])
    direction=(b-a)/np.linalg.norm(b-a);side=np.array([1.,0.,0.]);up=np.cross(direction,side)
    for x in (2.08,2.92):
        for raise_y,thickness in ((.85,.06),(-.04,.09)):
            start=a+np.array([x,raise_y,0]);end=b+np.array([x,raise_y,0])
            ring=[start+side*s+up*h for s,h in ((-thickness/2,-thickness/2),(thickness/2,-thickness/2),(thickness/2,thickness/2),(-thickness/2,thickness/2))]
            far=[p+(end-start) for p in ring]
            faces=[ring,far]+[[ring[i],ring[(i+1)%4],far[(i+1)%4],far[i]] for i in range(4)]
            from geometry import outward
            extra_solid(result,outward([list(f) for f in faces]),TIMBER,tuple(direction))

def scene(data,name):
    result=[]
    well={f"cell:2:6:{k}" for k in (1,2,3)}
    for cell in data["cells"]:
        if cell["id"] in well: continue
        for face in cell["faces"]:
            facing=face.get("facing")
            if facing and facing not in well: continue
            kind=face["kind"]
            if name=="structure" and kind in ("deck","hull"): continue
            if name in ("cutaway","stair") and ((kind=="hull" and cell["cell"][2]>=0) or (kind=="deck" and cell["cell"][1]==6 and cell["cell"][2]>=0)): continue
            if kind in ("rib","beam","keel"): texture=TIMBER
            elif kind=="deck": texture=DECK
            else:
                centre=mean(face["points"])
                outward=face["normal"][2]*(1 if centre[2]>=0 else -1)>.3 or face["normal"][1]<-.8
                texture=TAR if outward else PLANK
            append_face(result,face["points"],face["uv"],texture)
    if name!="structure": stair_geometry(result)
    return result

def font(size):
    for name in ("C:/Windows/Fonts/segoeui.ttf","C:/Windows/Fonts/arial.ttf"):
        if Path(name).exists(): return ImageFont.truetype(name,size)
    return ImageFont.load_default()

def render_one(data,name,suffix=""):
    eye,target,fov=CAMERAS[name]
    scene_image=texview.render(scene(data,name),eye,target,size=(1280,800),fov=fov)
    image=Image.new("RGB",(1280,900),(206,222,235));image.paste(scene_image,(0,60))
    draw=ImageDraw.Draw(image)
    title=f'Hansa · {name.capitalize()} · visible ribs {data["spec"]["ribVisibleWidth"]:g} block'
    draw.text((24,18),title,font=font(25),fill=(32,53,51))
    draw.text((24,873),"Two-bay construction proposal · ordinary materials, prescribed grid geometry · open slice ends",font=font(17),fill=(32,53,51))
    path=OUT/f'{name}{suffix}.png';image.save(path)
    print(path,flush=True)
    return image

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    data=json.loads((ROOT/"assets/hansa/config/gridships/grid-two-bay.json").read_text())
    names=sys.argv[1:] or ["cutaway","structure","section","lower","hold","stair","compare"]
    for name in names:
        if name=="compare":
            a=render_one(data,"structure")
            spec=copy.deepcopy(data["spec"]);spec["ribVisibleWidth"]=1
            b=render_one(compile_blueprint(spec),"structure","-full")
            combined=Image.new("RGB",(a.width*2,a.height));combined.paste(a,(0,0));combined.paste(b,(a.width,0));combined.save(OUT/"rib-width-comparison.png")
        else: render_one(data,name)

if __name__=="__main__": main()
