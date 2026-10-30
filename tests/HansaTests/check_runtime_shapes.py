import sys,json,math,os
from pathlib import Path
import numpy as np
sys.path.insert(0,'tools/caravel');import overlap_check as oc
out=Path('tmp/runtime-meshes')
failures=0
if len(list(out.glob('stair-*.json')))!=24:
 raise SystemExit('Run runtime-notes first: expected all 24 stair variants')
for path in sorted(out.glob('stair-*.json')):
 d=json.load(open(path));found=oc.find_clashes(d,{'deck':set(),'hull':set()})
 print(path.name,len(found))
 failures+=len(found)
 if found:print(' FIRST',[(x[0],x[1],x[2],x[3],x[4],x[5].tolist())for x in found[:3]])
# Read each inventory block's actual shape rotations, preserving its emitted faces.
faces={'north':(0,1,3,2),'south':(4,6,7,5),'west':(0,2,6,4),'east':(1,5,7,3),'up':(2,3,7,6),'down':(0,4,5,1)}
def mesh_shape(d):
 qs=[]
 textures=list(d.get('textures',{}).values());keys=list(d.get('textures',{}))
 def element(e,parent):
  o=np.array(e.get('rotationOrigin',[0,0,0]))/16;M=np.eye(4);M[:3,3]=o
  for axis,key in [(2,'rotationZ'),(1,'rotationY'),(0,'rotationX')]:
   a=math.radians(e.get(key,0));R=np.eye(4);b,c=(axis+1)%3,(axis+2)%3;R[b,b]=R[c,c]=math.cos(a);R[b,c]=-math.sin(a);R[c,b]=math.sin(a);M=M@R
  T=np.eye(4);T[:3,3]=-o;M=parent@M@T
  lo=np.array(e['from'])/16;hi=np.array(e['to'])/16
  corners=[(M@np.array([hi[0]if k&1 else lo[0],hi[1]if k&2 else lo[1],hi[2]if k&4 else lo[2],1]))[:3]for k in range(8)]
  for name,face in e.get('faces',{}).items():
   if not face.get('enabled',True):continue
   pts=[corners[k]for k in faces[name]];n=np.cross(pts[1]-pts[0],pts[2]-pts[0]);l=np.linalg.norm(n)
   if l<1e-10:continue
   n/=-l;uv=face.get('uv',[0,0,16,16]);q=[keys.index(face.get('texture','#'+keys[0])[1:])]
   for k,p in enumerate(pts):q+=p.tolist()+[uv[2]if k in [1,2]else uv[0],uv[3]if k in [2,3]else uv[1]]+n.tolist()
   qs.append(q)
  T=np.eye(4);T[:3,3]=lo
  for child in e.get('children',[]):element(child,M@T)
 for e in d['elements']:element(e,np.eye(4))
 return {'textures':[t if ':' in t else 'game:'+t for t in textures],'groups':{'exterior':qs}}
for name in ['shipstair-short','shipstair-steep','shipstair-long','swivelgun']:
 d=mesh_shape(json.load(open('assets/hansa/shapes/block/'+name+'.json')))
 p=out/(name+'-block.json');json.dump(d,open(p,'w'));found=oc.find_clashes(d,{'deck':set(),'hull':set()});print(name,len(d['groups']['exterior']),len(found))
 failures+=len(found)
 if found: print(' FIRST',[(x[0],x[1],x[2],x[3],x[4],x[5].tolist())for x in found[:5]])
rig=json.load(open(out/'rig-poses.json'))
for group,quads in rig['groups'].items():
 mat=np.array(rig['poses'].get(group,np.eye(4).reshape(-1,order='F'))).reshape((4,4),order='F')
 normal=np.linalg.inv(mat[:3,:3]).T
 for q in quads:
  for k in range(4):
   q[1+8*k:4+8*k]=(mat@np.append(q[1+8*k:4+8*k],1))[:3].tolist()
   n=normal@q[6+8*k:9+8*k];q[6+8*k:9+8*k]=(n/max(1e-10,np.linalg.norm(n))).tolist()
found=oc.find_clashes(rig,{'deck':set(),'hull':set()})
print('posed-shrouds',len(found));failures+=len(found)
raise SystemExit(1 if failures else 0)
