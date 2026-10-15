"""Split a planar face into atlas-safe texture tiles without stretching its UVs."""
from math import ceil, floor
from geometry import area

def clip(vertices, axis, bound, sign):
    if not vertices: return []
    result=[];a=vertices[-1];da=sign*(a[axis]-bound)
    for b in vertices:
        db=sign*(b[axis]-bound)
        if (da<=1e-9)!=(db<=1e-9):
            t=da/(da-db)
            result.append([a[i]+t*(b[i]-a[i]) for i in range(5)])
        if db<=1e-9: result.append(b)
        a,da=b,db
    return result

def tiles(points,uv):
    vertices=[list(p)+list(t) for p,t in zip(points,uv)]
    u0=floor(min(t[0] for t in uv)/16+1e-9);u1=ceil(max(t[0] for t in uv)/16-1e-9)
    v0=floor(min(t[1] for t in uv)/16+1e-9);v1=ceil(max(t[1] for t in uv)/16-1e-9)
    for u in range(u0,u1):
        for v in range(v0,v1):
            polygon=vertices
            for axis,n in ((3,u),(4,v)):
                polygon=clip(polygon,axis,n*16,-1)
                polygon=clip(polygon,axis,(n+1)*16,1)
            if len(polygon)>=3 and area([p[:3] for p in polygon])>1e-9:
                yield [p[:3] for p in polygon],[[p[3]-u*16,p[4]-v*16] for p in polygon]
