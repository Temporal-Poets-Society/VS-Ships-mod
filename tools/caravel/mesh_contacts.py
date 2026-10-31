"""Trim coincident slot contacts in their real surface plane, retaining the reveal when its neighbour is removed."""
import numpy as np


def corners(row):
    result = []
    for k in range(4):
        p = np.asarray(row[1+8*k:9+8*k], float)
        if not result or np.linalg.norm(p[:3]-result[-1][:3]) > 1e-9:
            result.append(p)
    if len(result)>2 and np.linalg.norm(result[-1][:3]-result[0][:3]) < 1e-9:
        result.pop()
    return result


def clip(poly, distance, inside=True):
    result=[]
    for i,a in enumerate(poly):
        b=poly[(i+1)%len(poly)]
        da,db=distance(a),distance(b)
        if not inside: da,db=-da,-db
        if da >= -1e-12: result.append(a)
        if (da>1e-12 and db< -1e-12) or (da< -1e-12 and db>1e-12):
            result.append(a+(b-a)*(da/(da-db)))
    return result


def subtract(poly, tool, normal):
    # At each edge, retain the outside part; the inside part continues to the next edge. All pieces stay convex.
    area=sum((np.cross(tool[(k+1)%len(tool)][:3]-tool[k][:3],tool[(k+2)%len(tool)][:3]-tool[(k+1)%len(tool)][:3]) @ normal)
             for k in range(len(tool)))
    if area<0: tool=tool[::-1]
    outside=[]; remaining=poly
    for i,a in enumerate(tool):
        b=tool[(i+1)%len(tool)];edge=b[:3]-a[:3]
        if np.linalg.norm(edge)<1e-12: continue
        def distance(p): return float(np.cross(edge,p[:3]-a[:3]) @ normal)
        part=clip(remaining,distance,False)
        if len(part)>=3: outside.append(part)
        remaining=clip(remaining,distance)
        if len(remaining)<3: break
    return outside,remaining


def rows(poly,texture):
    if len(poly)<3: return []
    result=[]
    for k in range(1,len(poly)-1):
        tri=[poly[0],poly[k],poly[k+1]]
        if np.linalg.norm(np.cross(tri[1][:3]-tri[0][:3],tri[2][:3]-tri[0][:3]))<1e-12: continue
        result.append([texture]+[round(float(v),7) for p in tri+[tri[-1]] for v in p])
    return result


def trim_contacts(mesh, clashes):
    """Only contacts within a slot family can be represented by owner/facing references. Returns the contacts repaired."""
    changed=0
    for ga,ia,gb,ib,area,centre,normal,*_ in clashes:
        if not ga.startswith('slot:') or not gb.startswith('slot:') or ga.startswith('slot:hull')!=gb.startswith('slot:hull'):
            continue
        ar=mesh['slotQuads'][ga][2*ia:2*ia+2];br=mesh['slotQuads'][gb][2*ib:2*ib+2]
        if ar[1]>=0 or br[1]>=0: continue
        a,b=mesh['groups'][ga][ia],mesh['groups'][gb][ib]
        pa,pb=corners(a),corners(b)
        if len(pa)<3 or len(pb)<3: continue
        outside,overlap=subtract(pb,pa,normal)
        fragments=[row for p in outside for row in rows(p,b[0])]
        hidden=rows(overlap,b[0]) if ar[0]>=0 and ar[0]!=br[0] else []
        # The clipped portion belongs to B but is visible only through the opening left by A.
        replacement=fragments[:1]
        if replacement: mesh['groups'][gb][ib]=replacement[0]
        else:
            blank=list(b)
            for k in range(1,4): blank[1+8*k:4+8*k]=blank[1:4]
            mesh['groups'][gb][ib]=blank
        paint=mesh.get('paintQuads',{}).get(gb)
        for row,refs in [(q,br) for q in fragments[1:]]+[(q,[br[0],ar[0]]) for q in hidden]:
            mesh['groups'][gb].append(row);mesh['slotQuads'][gb].extend(refs)
            if paint is not None: paint.append(paint[ib] if ib<len(paint) else -1)
        changed+=1
    return changed
