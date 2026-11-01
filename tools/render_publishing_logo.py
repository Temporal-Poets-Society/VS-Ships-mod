"""Render the exact caravel part:wheel mesh for the Ships listing. Does not alter game models.
Run from the Hansa repository: python tools/render_publishing_logo.py
"""
from pathlib import Path
import json,hashlib,os,math
import numpy as np
from PIL import Image,ImageDraw,ImageFont,ImageFilter
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/publishing-assets'
MESH=ROOT/'assets/ships/config/shipmeshes/caravel.json'
DEF=ROOT/'assets/ships/config/ships/caravel.json'
model=json.loads(MESH.read_text(encoding='utf-8'));definition=json.loads(DEF.read_text(encoding='utf-8'))
quads=model['groups']['part:wheel'];centre=np.array(next(p['axisPoint'] for p in definition['movingParts'] if p['code']=='wheel'))
game=Path(os.environ['APPDATA'])/'Vintagestory/assets/survival/textures'
textures={}
for idx in sorted(set(q[0] for q in quads)):
 path=game/(model['textures'][idx].split(':',1)[1]+'.png')
 textures[idx]=np.asarray(Image.open(path).convert('RGB'),dtype=np.float32)/255

def unit(v):v=np.array(v,dtype=float);return v/np.linalg.norm(v)
view=unit([-4,.7,-.6]);right=unit(np.cross([0,1,0],view));up=unit(np.cross(view,right))
light=unit(view*.55+up*.9-right*.6);half=unit(light+view)
size=1920;at=np.array([size*.5,size*.435]);scale=1120
rgba=np.zeros((size,size,4),dtype=np.uint8);depth=np.full((size,size),-np.inf,dtype=np.float32)
faces=0
for q in quads:
 v=np.array(q[1:],dtype=float).reshape(4,8);p=v[:,:3]-centre
 screen=np.stack((p@right*scale+at[0],-p@up*scale+at[1],p@view),axis=1)
 for tri in [(0,1,2),(0,2,3)]:
  t=screen[list(tri)];uv=v[list(tri),3:5];normal=v[list(tri),5:8]
  if np.mean(normal,axis=0)@view<=0:continue
  xmin=max(0,int(np.floor(t[:,0].min())));xmax=min(size-1,int(np.ceil(t[:,0].max())))
  ymin=max(0,int(np.floor(t[:,1].min())));ymax=min(size-1,int(np.ceil(t[:,1].max())))
  den=(t[1,1]-t[2,1])*(t[0,0]-t[2,0])+(t[2,0]-t[1,0])*(t[0,1]-t[2,1])
  if abs(den)<1e-7 or xmin>xmax or ymin>ymax:continue
  yy,xx=np.mgrid[ymin:ymax+1,xmin:xmax+1];xx=xx+.5;yy=yy+.5
  a=((t[1,1]-t[2,1])*(xx-t[2,0])+(t[2,0]-t[1,0])*(yy-t[2,1]))/den
  b=((t[2,1]-t[0,1])*(xx-t[2,0])+(t[0,0]-t[2,0])*(yy-t[2,1]))/den;c=1-a-b
  z=a*t[0,2]+b*t[1,2]+c*t[2,2]
  region=depth[ymin:ymax+1,xmin:xmax+1];mask=(a>=-1e-6)&(b>=-1e-6)&(c>=-1e-6)&(z>region)
  if not mask.any():continue
  aa=a[mask,None];bb=b[mask,None];cc=c[mask,None]
  coords=aa*uv[0]+bb*uv[1]+cc*uv[2]
  tex=textures[q[0]];h,w=tex.shape[:2]
  tx=np.floor(coords[:,0]*w/16).astype(int)%w;ty=np.floor(coords[:,1]*h/16).astype(int)%h
  base=tex[ty,tx];n=aa*normal[0]+bb*normal[1]+cc*normal[2];n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-9)
  diffuse=np.maximum(0,n@light);shine=np.maximum(0,n@half)**28
  # Original game textures, with diffuse/key light and a small bronze highlight.
  linear=base**2.2*(.38+.85*diffuse[:,None])+(0.1 if 'metal' in model['textures'][q[0]] else .015)*shine[:,None]
  colour=np.clip(linear**(1/2.2)*255,0,255).astype(np.uint8)
  canvas=rgba[ymin:ymax+1,xmin:xmax+1];canvas[mask,:3]=colour;canvas[mask,3]=255;region[mask]=z[mask];faces+=1
wheel=Image.fromarray(rgba,'RGBA')
wheel.save(OUT/'caravel-wheel-render-1920.png')
logo=Image.new('RGBA',(size,size),'#f2ecdf');draw=ImageDraw.Draw(logo)
cx,cy=size*.5,size*.435;radius=size*.375
bounds=(cx-radius,cy-radius,cx+radius,cy+radius)
draw.ellipse(bounds,fill='#142f3e')
draw.ellipse((bounds[0]+16,bounds[1]+16,bounds[2]-16,bounds[3]-16),outline='#b79355',width=5)
shadow=Image.new('RGBA',(size,size));shadow.putalpha(wheel.getchannel('A').filter(ImageFilter.GaussianBlur(14)).point(lambda x:int(x*.38)))
logo.alpha_composite(shadow,(13,22));logo.alpha_composite(wheel)
draw=ImageDraw.Draw(logo);font=ImageFont.truetype(r'C:\Windows\Fonts\georgiab.ttf',151)
word='SHIPS';tracking=24;length=sum(draw.textlength(ch,font=font) for ch in word)+tracking*(len(word)-1)
x=(size-length)/2
for ch in word:
 draw.text((x,size*.865),ch,fill='#142f3e',font=font,stroke_width=0);x+=draw.textlength(ch,font=font)+tracking
logo.convert('RGB').save(OUT/'ships-logo-master-1920.png')
logo.convert('RGB').resize((480,480),Image.Resampling.LANCZOS).save(OUT/'ships-logo-moddb-480.png')
# A small-size QA image proves that the silhouette still reads as a wheel.
logo.convert('RGB').resize((96,96),Image.Resampling.LANCZOS).save(OUT/'ships-logo-preview-96.png')
record={'model':str(MESH.relative_to(ROOT)),'modelSha256':hashlib.sha256(MESH.read_bytes()).hexdigest(),'group':'part:wheel','quads':len(quads),'renderedTriangles':faces,'centre':centre.tolist(),'materials':{str(i):model['textures'][i] for i in textures},'geometry':'Exact existing mesh, without alterations','render':'Orthographic view; native textures; original code-rendered logo composition','size':[480,480],'masterSize':[1920,1920],'script':'tools/render_publishing_logo.py'}
(OUT/'ships-logo-source.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
print(json.dumps(record))
