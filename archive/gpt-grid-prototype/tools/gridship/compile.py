"""Compile an editable ship blueprint to owned cells and preview geometry.

python tools/gridship/compile.py [designs/two-bay.json] [--rib-width=1]
The historic carrack generator is not imported or run.
"""
import argparse
import json
from pathlib import Path
from geometry import *

ROOT = Path(__file__).resolve().parents[2]
PRIORITY = {"deck":0, "hull":1, "beam":2, "rib":3, "keel":4}

def section_width(spec, y):
    profile = spec["halfSection"]
    for (a,wa),(b,wb) in zip(profile,profile[1:]):
        if a <= y <= b: return wa+(wb-wa)*(y-a)/(b-a)
    raise ValueError(f"Height {y} outside the section")

def validate_spec(spec):
    assert spec["schema"] == 1
    assert spec["grid"]["cellSize"] == 1
    assert spec["grid"]["collisionVoxel"] == 0.25
    assert spec["lengthCells"] >= 3 and isinstance(spec["lengthCells"],int)
    ribs = spec["ribCells"]
    assert ribs[0] == 0 and ribs[-1] == spec["lengthCells"]-1
    assert all(isinstance(x,int) for x in ribs)
    assert all(b-a-1 in (1,3) for a,b in zip(ribs,ribs[1:])), "Clear rib gaps must be 1 or 3"
    assert all(b-a-1 == spec["clearBayCells"] for a,b in zip(ribs,ribs[1:]))
    assert 0 < spec["ribVisibleWidth"] <= 1
    assert all(a[0]<b[0] for a,b in zip(spec["halfSection"],spec["halfSection"][1:]))
    assert all(isinstance(d["top"],int) for d in spec["decks"])

def compile_blueprint(spec):
    validate_spec(spec)
    cells, collision = {}, {}
    origin = spec["grid"]["origin"]
    ribs = set(spec["ribCells"])
    thickness = spec["shellThicknessAcross"]
    visible = spec["ribVisibleWidth"]

    def solid(faces, kind, grain=(1,0,0)):
        for cell, bounded in split_solid(faces, origin):
            key = cell_id(cell)
            module = cells.setdefault(key, {"id":key,"cell":list(cell),"bounds":cell_bounds(cell,origin),
                                          "role":kind,"material":spec["material"],"faces":[],"parts":[]})
            if PRIORITY[kind]>PRIORITY[module["role"]]: module["role"] = kind
            if kind not in module["parts"]: module["parts"].append(kind)
            for face in bounded:
                module["faces"].append({"points":[[round(c,7) for c in p] for p in face],
                    "normal":[round(c,9) for c in normal(face)], "uv":metric_uv(face,grain),"kind":kind})
            voxels = collision.setdefault(key, {})
            for b in voxel_boxes(bounded,module["bounds"],spec["grid"]["collisionVoxel"]): voxels[tuple(b)] = b

    for x in range(spec["lengthCells"]):
        split_x = [x, x+1]
        if x in ribs and visible < 1:
            split_x = [x,x+0.5-visible/2,x+0.5+visible/2,x+1]
        for side in (-1,1):
            for (ya,wa),(yb,wb) in zip(spec["halfSection"],spec["halfSection"][1:]):
                for xa,xb in zip(split_x,split_x[1:]):
                    solid(prism(xa,xb,[(ya,side*wa),(yb,side*wb),
                                      (yb,side*max(0,wb-thickness)),(ya,side*max(0,wa-thickness))]),"hull")
                if x in ribs:
                    a,b = max(0,wa-thickness),max(0,wb-thickness)
                    solid(prism(x+0.5-visible/2,x+0.5+visible/2,
                        [(ya,side*a),(yb,side*b),(yb,side*max(0,b-spec["ribDepth"])),
                         (ya,side*max(0,a-spec["ribDepth"]))]),"rib",(0,yb-ya,side*(wb-wa)))
        # A broad flat bottom, as in the reference carrack, with the keel below it.
        bottom_y,bottom_w=spec["halfSection"][0]
        for side in (-1,1):
            for xa,xb in zip(split_x,split_x[1:]):
                z0,z1=sorted((side*.5,side*bottom_w))
                solid(box(xa,bottom_y,z0,xb,bottom_y+thickness,z1),"hull")
            if x in ribs:
                z0,z1=sorted((side*.5,side*bottom_w))
                solid(box(x+.5-visible/2,bottom_y+thickness,z0,x+.5+visible/2,1,z1),"rib",(0,0,1))
        solid(box(x,0,-0.5,x+1,1,0.5),"keel")
        for deck in spec["decks"]:
            top = deck["top"]
            low=top-spec["deckThickness"]
            width_low=section_width(spec,low)-thickness
            width_top=section_width(spec,top)-thickness
            for xa,xb in zip(split_x,split_x[1:]):
                solid(prism(xa,xb,[(low,-width_low),(top,-width_top),(top,width_top),(low,width_low)]),"deck")
            if x in ribs and top > 1:
                beam_low=low-spec["beamDepth"]
                end_low=section_width(spec,beam_low)-thickness
                solid(prism(x+.5-visible/2,x+.5+visible/2,
                      [(beam_low,-end_low),(low,-width_low),(low,width_low),(beam_low,end_low)]),"beam",(0,0,1))

    # Exact shared faces disappear inside one cell, or follow the neighbouring
    # cell's state. Removed cells expose their neighbours' actual cut surfaces.
    seen = {}
    for module in cells.values():
        module["faces"] = [f for f in module["faces"] if area(f["points"]) > EPS]
        for face in module["faces"]:
            key = tuple(sorted(tuple(round(c,6) for c in p) for p in face["points"]))
            other = seen.get(key)
            if other is None: seen[key] = (module,face)
            elif dot(face["normal"],other[1]["normal"]) < -0.99:
                if module is other[0]:
                    face["hidden"] = True; other[1]["hidden"] = True
                else:
                    face["facing"] = other[0]["id"]; other[1]["facing"] = module["id"]
            else: face["hidden"] = True
    for module in cells.values():
        module["faces"] = [f for f in module["faces"] if not f.pop("hidden",False)]
        module["solids"] = list(collision[module["id"]].values())
        module["required"] = module["role"] in ("keel","rib","beam") or ("hull" in module["parts"] and module["bounds"][1] < spec["waterline"])
        module["seals"] = "hull" in module["parts"]
        module["allowed"] = ["plank"] + (["window","port","gate"] if module["role"] == "hull" and module["bounds"][1] >= 7 else [])
    data = {"schema":1,"code":spec["code"],"spec":spec,"cells":sorted(cells.values(),key=lambda c:tuple(c["cell"]))}
    data["stats"] = validate_compiled(data)
    return data

def validate_compiled(data):
    ids, voxels = set(), set()
    max_error, max_density_error, multipane = 0,0,0
    for module in data["cells"]:
        assert module["id"] not in ids
        ids.add(module["id"])
        bounds = module["bounds"]
        planes = set()
        for face in module["faces"]:
            points = face["points"]
            n = normal(points)
            assert n is not None
            planes.add(tuple(round(c,4) for c in n)+(round(dot(n,points[0]),4),))
            for p in points:
                assert all(bounds[a]-EPS <= p[a] <= bounds[a+3]+EPS for a in range(3)), (module["id"],p)
                error = abs(dot(n,sub(p,points[0])))
                max_error = max(max_error,error)
                assert error < 1e-6, "Non-planar face"
            for i,p in enumerate(points):
                k = (i+1)%len(points)
                physical = length(sub(points[k],p))
                uv_distance = sqrt(sum((face["uv"][k][a]-face["uv"][i][a])**2 for a in (0,1)))/16
                error = abs(physical-uv_distance)
                max_density_error = max(max_density_error,error)
                assert error < 1e-6, "Texture stretch"
        multipane += len(planes)>6
        for b in module["solids"]:
            key = tuple(b)
            assert key not in voxels, "Collision voxel has multiple owners"
            voxels.add(key)
    for module in data["cells"]:
        for face in module["faces"]:
            if "facing" in face: assert face["facing"] in ids
    return {"cells":len(ids),"faces":sum(len(c["faces"]) for c in data["cells"]),
            "collisionVoxels":len(voxels),"cellsWithMoreThanSixPlanes":multipane,
            "maxPlanarityError":max_error,"maxTextureDistanceError":max_density_error}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("spec",nargs="?",default=str(ROOT/"designs/two-bay.json"))
    parser.add_argument("--rib-width",type=float)
    args = parser.parse_args()
    spec = json.loads(Path(args.spec).read_text())
    if args.rib_width is not None: spec["ribVisibleWidth"] = args.rib_width
    data = compile_blueprint(spec)
    destination = ROOT/"assets/hansa/config/gridships"/f'{spec["code"]}.json'
    destination.parent.mkdir(parents=True,exist_ok=True)
    destination.write_text(json.dumps(data,separators=(",",":")),encoding="utf-8")
    from preview import write_preview, write_images
    write_preview(data,ROOT/"docs/prototype/two-bay.html")
    write_images(data,ROOT/"docs/prototype")
    print(json.dumps(data["stats"],indent=2))

if __name__ == "__main__": main()
