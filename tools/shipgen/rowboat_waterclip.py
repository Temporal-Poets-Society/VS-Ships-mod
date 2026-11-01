"""Bake Sea's rowing-boat dry outline from the actual oriented hull boxes, never collision bounds.

The hull's render coordinates are (x,y,z); the native entity model matrix translates (-.5,0,-.5),
then turns yaw+pi/2. Sea's frame is along=.5-x, side=.5-z. Keep the cut under the planking.
"""
import hashlib
import json
from pathlib import Path
import sys
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import vsshape as vs

ROOT = Path(__file__).resolve().parents[2]
SHAPE = ROOT / 'assets/ships/shapes/entity/rowboat.json'
OUTPUT = ROOT / 'assets/ships/config/rowboat-waterclip.json'
CENTRE_X, CENTRE_Z, HALF_LENGTH = -1.55, 0.5625, 3.75
LEVELS = np.linspace(0.02, 0.98, 4)
STATIONS = np.linspace(-HALF_LENGTH, HALF_LENGTH, 32)
MARGIN = 0.025

def hull_boxes():
    boxes = []
    for element, matrix, path in vs.walk(vs.load(SHAPE)):
        if not any(name.startswith(('Gunwhale', 'HullUpper', 'ProwBeam')) for name in path):
            continue
        if not vs._drawn(element):
            continue
        boxes.append((np.linalg.inv(matrix), (np.array(element['to'])-np.array(element['from']))/16))
    return boxes

def widths(x, y, boxes):
    """Outer wood boundary at a render-space X/Y section, measured about the true symmetric centreline."""
    x, y = np.broadcast_arrays(x, y)
    lo, hi = np.full(x.shape, np.inf), np.full(x.shape, -np.inf)
    for inverse, size in boxes:
        lower, upper, valid = np.full(x.shape, -np.inf), np.full(x.shape, np.inf), np.ones(x.shape, bool)
        for axis in range(3):
            base = inverse[axis,0]*x + inverse[axis,1]*y + inverse[axis,3]
            rate = inverse[axis,2]
            if abs(rate) < 1e-10:
                valid &= (base >= -1e-8) & (base <= size[axis]+1e-8)
            else:
                a, b = -base/rate, (size[axis]-base)/rate
                lower = np.maximum(lower, np.minimum(a,b))
                upper = np.minimum(upper, np.maximum(a,b))
        valid &= upper >= lower
        lo = np.minimum(lo, np.where(valid,lower,np.inf))
        hi = np.maximum(hi, np.where(valid,upper,-np.inf))
    return np.maximum(0,np.minimum(CENTRE_Z-lo,hi-CENTRE_Z))

def interpolate(profile, along, y):
    along, y = np.broadcast_arrays(along,y)
    sx = np.clip((along+HALF_LENGTH)/(2*HALF_LENGTH)*31,0,31)
    sy = np.clip((y-LEVELS[0])/(LEVELS[1]-LEVELS[0]),0,3)
    i, j = np.floor(sx).astype(int), np.floor(sy).astype(int)
    i1, j1 = np.minimum(i+1,31),np.minimum(j+1,3)
    fx, fy = sx-i,sy-j
    return ((1-fx)*profile[j,i]+fx*profile[j,i1])*(1-fy)+((1-fx)*profile[j1,i]+fx*profile[j1,i1])*fy

def bake():
    boxes = hull_boxes()
    p = np.maximum(0,widths(CENTRE_X-STATIONS[None,:],LEVELS[:,None],boxes)-MARGIN)
    # Keep the bilinear shader surface inside the actual wood even at a joint between tapered planks.
    along, y = np.meshgrid(np.linspace(-HALF_LENGTH,HALF_LENGTH,497),np.linspace(LEVELS[0],LEVELS[-1],97))
    allowed = np.maximum(0,widths(CENTRE_X-along,y,boxes)-0.005)
    sx=(along+HALF_LENGTH)/(2*HALF_LENGTH)*31
    sy=(y-LEVELS[0])/(LEVELS[1]-LEVELS[0])
    for _ in range(8):
        fit=interpolate(p,along,y)
        bad=np.argwhere(fit>allowed+0.00001)
        if not len(bad): break
        for row,col in bad:
            ix,iy=min(30,int(sx[row,col])),min(2,int(sy[row,col]))
            current=float(interpolate(p,along[row,col],y[row,col]))
            if current>allowed[row,col]+0.00001:
                p[iy:iy+2,ix:ix+2] *= allowed[row,col]/current
    result={
        'sourceSha256':hashlib.sha256(SHAPE.read_bytes()).hexdigest(),
        'waterY':0.55,'centreOffset':0.5-CENTRE_X,'centreRight':CENTRE_Z-0.5,
        'halfLength':HALF_LENGTH,'halfWidth':float(p.max()),'dryDepth':0.55-float(LEVELS[0]),
        'dryHeight':float(LEVELS[-1])-0.55,'dryLevel0':float(LEVELS[0])-0.55,
        'dryLevelStep':float(LEVELS[1]-LEVELS[0]),'dryProfile':[round(float(v),7) for v in p.ravel()],
        # Safe fallback for clips beyond the detailed-profile budget: never cut outside a narrow lower section.
        'profile':[round(float(v),7) for v in np.interp(np.linspace(-HALF_LENGTH,HALF_LENGTH,16),STATIONS,p.min(axis=0))]
    }
    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    OUTPUT.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    return result,boxes

if __name__=='__main__':
    r,boxes=bake()
    p=np.array(r['dryProfile']).reshape(4,32)
    print('Wrote',OUTPUT)
    print('Profile rows:',p.round(3).tolist())
    a,y=np.meshgrid(np.linspace(-HALF_LENGTH,HALF_LENGTH,997),np.linspace(0.02,0.98,193))
    actual=widths(CENTRE_X-a,y,boxes)
    fitted=interpolate(p,a,y)
    print('Maximum external cut:',float(np.max(fitted-actual)))
    print('Maximum gap below outer wood:',float(np.max(actual-fitted)))
