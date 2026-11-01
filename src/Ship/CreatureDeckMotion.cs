using System;
using System.Collections.Generic;

namespace Hansa
{
    /// <summary>
    /// Resolve a creature's native movement against the ship. AI and gravity stay vanilla.
    /// (1 November 2026, the user: "cannot get elk on rope past gate") Vanilla gives a creature one square box as long as
    /// the animal (an elk's 1.3); a 1.3 square can't pass her 0.82 entry ports or turn in her hold. Aboard, its body is that
    /// length along its heading and half of it across (an elk 1.3 × 0.65, about a real one's), boxed in her frame as it
    /// turns. It turns where the turned body fits; where it doesn't it squares itself to her timbers (lines up with a gate
    /// or a hatchway); and walking into an opening a little to one side it eases over into the gap.
    /// </summary>
    public sealed class CreatureDeckMotion
    {
        public double X,Y,Z;
        /// <summary>The body's half extents in her frame now.</summary>
        public double HalfX,HalfZ;
        public readonly double Length,Height,Width;
        public bool OnGround;
        /// <summary>A step it walks up on deck (half a block), how far it's eased sideways into a gap in a tick, and how far it looks for the gap.</summary>
        public const double Step=.5,Ease=.05,EaseReach=.3;
        /// <summary>How high it hops from the quay onto her sill when she's lifted on a swell.</summary>
        public const double Hop=1.0;
        public CreatureDeckMotion(double x,double y,double z,double length,double height,double width,double yaw=0)
        {
            X=x;Y=y;Z=z;Length=length;Height=height;Width=Math.Min(width,length);
            Body(yaw,out HalfX,out HalfZ);
        }
        /// <summary>A creature by its vanilla box: its side the length, half that the width.</summary>
        public static CreatureDeckMotion Of(double x,double y,double z,double boxSide,double boxHeight,double yaw) =>
            new(x,y,z,Math.Max(.1,boxSide),Math.Max(.1,boxHeight),Math.Max(.1,boxSide)/2,yaw);
        /// <summary>The body's half extents in her frame headed along yaw (vanilla: walking toward (sin yaw, cos yaw)).</summary>
        public void Body(double yaw,out double hx,out double hz)
        {
            double s=Math.Abs(Math.Sin(yaw)),c=Math.Abs(Math.Cos(yaw));
            hx=(s*Length+c*Width)/2;hz=(c*Length+s*Width)/2;
        }
        bool Fits(VoxelGrid g,double x,double y,double z,double hx,double hz) => !g.Collides(x-hx,y+.001,z-hz,x+hx,y+Height,z+hz);
        public bool Stuck(VoxelGrid g) => !Fits(g,X,Y,Z,HalfX,HalfZ);
        public bool Over(VoxelGrid g) => X+HalfX>g.OX && X-HalfX<g.MaxX && Z+HalfZ>g.OZ && Z-HalfZ<g.MaxZ && Y>g.OY &&
            g.Collides(X-HalfX,g.OY,Z-HalfZ,X+HalfX,Y+.01,Z+HalfZ);
        /// <summary>Turn the body to its heading where it fits, else square it to her timbers; else it stays as it was.</summary>
        public void Turn(VoxelGrid g,double yaw)
        {
            double square=Math.Round(yaw/(Math.PI/2))*(Math.PI/2);
            foreach(double a in new[]{yaw,square})
            {
                Body(a,out double hx,out double hz);
                if(Fits(g,X,Y,Z,hx,hz)) {HalfX=hx;HalfZ=hz;return;}
            }
        }
        /// <summary>
        /// Catch a creature stepping aboard at the start of its move: its body turned to its heading or squared to her, on
        /// its feet or hopped up onto her sill (sill: her deck's level at her entry ports; hop: how far below it the creature's
        /// feet may be, <see cref="Hop"/> for one standing on the quay, 0 for one swimming), and set over by up to
        /// <see cref="EaseReach"/> into an opening it's walking into. Only onto the sill: never up onto her rail or a wale.
        /// False: it isn't over her, or there's no room for it there. ((1 November 2026, the user: "i rejoined and elk had
        /// disappeared from post i tied it") squared the other way too: a body squared across its heading where it stood, as
        /// it may be saved, is found again on loading.)
        /// </summary>
        public bool Catch(VoxelGrid g,double yaw,double hop=0,double sill=double.NaN)
        {
            double x0=X,y0=Y,z0=Z,square=Math.Round(yaw/(Math.PI/2))*(Math.PI/2);
            var lifts=sill-y0>1e-6 && sill-y0<=hop+1e-9 ? new[]{0,sill-y0} : new[]{0.0};
            foreach(double a in new[]{yaw,square,square+Math.PI/2})
            {
                Body(a,out double hx,out double hz);
                // sideways is across its squared heading
                bool alongX=Math.Abs(Math.Sin(Math.Round(a/(Math.PI/2))*(Math.PI/2)))>.5;
                foreach(double lift in lifts)
                    for(double s=0;s<=EaseReach+1e-9;s+=.025)
                        foreach(int sign in s==0?new[]{1}:new[]{1,-1})
                        {
                            double x=x0+(alongX?0:sign*s),z=z0+(alongX?sign*s:0);
                            if(!Fits(g,x,y0+lift,z,hx,hz)) continue;
                            X=x;Y=y0+lift;Z=z;HalfX=hx;HalfZ=hz;
                            if(Over(g)) {OnGround=Supported(g);return true;}
                        }
            }
            X=x0;Y=y0;Z=z0;return false;
        }
        bool Supported(VoxelGrid g) => g.Collides(X-HalfX,Y-.04,Z-HalfZ,X+HalfX,Y-.001,Z+HalfZ);
        double Sweep(VoxelGrid g,int axis,double distance) => g.Sweep(axis,distance,X-HalfX,Y,Z-HalfZ,X+HalfX,Y+Height,Z+HalfZ);
        /// <summary>Is it inside her: her timbers on both sides of it, across her, below its feet (not out over her side).</summary>
        public bool Inside(VoxelGrid g) =>
            g.Collides(X-.01,Y-1,Z,X+.01,Y-.05,g.MaxZ) && g.Collides(X-.01,Y-1,g.OZ,X+.01,Y-.05,Z);
        /// <summary>Is it standing on a floor: under each corner of its body.</summary>
        public bool Standing(VoxelGrid g)
        {
            foreach(double sx in new[]{-1,1}) foreach(double sz in new[]{-1,1})
            {
                double x=X+sx*(HalfX-.05),z=Z+sz*(HalfZ-.05);
                if(!g.Collides(x-.04,Y-.04,z-.04,x+.04,Y-.001,z+.04)) return false;
            }
            return true;
        }
        public bool Move(VoxelGrid g,double dx,double dy,double dz)
        {
            double my=Sweep(g,1,dy);Y+=my;OnGround=dy<0 && Math.Abs(my-dy)>1e-8 || Supported(g);
            bool footed=OnGround;double x0=X,z0=Z;
            double mx=Sweep(g,0,dx);X+=mx;
            double mz=Sweep(g,2,dz);Z+=mz;
            // Ordinary half-block steps, without lifting a creature through a deck or a low beam.
            if(OnGround && (Math.Abs(mx-dx)>1e-8 || Math.Abs(mz-dz)>1e-8) && Math.Abs(Sweep(g,1,Step)-Step)<1e-8)
            {
                double x=X,z=Z;Y+=Step;
                X+=Sweep(g,0,dx-mx);Z+=Sweep(g,2,dz-mz);
                if(Math.Abs(X-x)+Math.Abs(Z-z)<1e-8) Y-=Step;
                else {Y+=Sweep(g,1,-Step);mx=dx;mz=dz;}
            }
            // Into an opening a little to one side (a gate, a hatchway): it eases over toward the gap.
            if(Math.Abs(mx-dx)>1e-8 || Math.Abs(mz-dz)>1e-8) EaseInto(g,dx-mx,dz-mz);
            // ((1 November 2026, the user: "got stuck in stairs") it doesn't walk into a hatchway or off a deck's edge
            // inside her, a drop deeper than a step; out through her entry port it goes, onto the quay). A step down it takes
            // at once, so it's footed again on the lower floor or tread and the next step is judged from there.
            if(footed && !Supported(g))
            {
                double drop=Sweep(g,1,-(Step+.01));
                if(Math.Abs(drop+Step+.01)>1e-8) Y+=drop;
                else if(Inside(g)) {X=x0;Z=z0;}
            }
            OnGround=Supported(g);return Over(g);
        }
        /// <summary>
        /// (1 November 2026, the user's choice for getting an elk into her hold: "Sling through hatch") The way an animal
        /// is slung through a hatch's opening (x0..x1 × z0..z1, its deck's top at deck), as points (ship-local feet; the last
        /// carries its body's heading): from the deck beside the opening up over the coaming, across and down through it to the
        /// floor below; or from under it up and onto the deck beside it. Its body goes through lengthwise and must reach a
        /// floor under all four corners (the hold ladder standing in the opening is in the way). Null, and why (a lang key).
        /// </summary>
        public static List<double[]> Sling(VoxelGrid g,CreatureDeckMotion m,double x0,double z0,double x1,double z1,double deck,double clear,out string why)
        {
            why=null;
            double ox=(x0+x1)/2,oz=(z0+z1)/2,top=deck+clear;
            double Gap(double x,double z) => Math.Sqrt(Math.Pow(Math.Max(0,Math.Max(x0-x,x-x1)),2)+Math.Pow(Math.Max(0,Math.Max(z0-z,z-z1)),2));
            bool down=m.Y>deck-.6;
            if(down && Math.Abs(m.Y-deck)>.6 || down && Gap(m.X,m.Z)>1.5) {why="ships:sling-too-far";return null;}
            // its column through the opening: lengthwise along the opening's longer side first, nearest the animal
            var body=new CreatureDeckMotion(ox,top,oz,m.Length,m.Height,m.Width);
            bool fits=false;double[] column=null;double best=double.MaxValue;
            foreach(double a in x1-x0>=z1-z0 ? new[]{Math.PI/2,0.0} : new[]{0.0,Math.PI/2})
            {
                body.Body(a,out double hx,out double hz);
                if(x1-x0<2*hx-1e-9 || z1-z0<2*hz-1e-9) continue;
                fits=true;body.HalfX=hx;body.HalfZ=hz;
                for(double x=x0+hx;x<=x1-hx+1e-9;x+=.125)
                    for(double z=z0+hz;z<=z1-hz+1e-9;z+=.125)
                    {
                        body.X=x;body.Y=top;body.Z=z;
                        if(body.Stuck(g)) continue;
                        body.Y+=body.Sweep(g,1,g.OY-top);
                        if(body.Y>deck-1.5 || !body.Standing(g)) continue;
                        double d=(x-m.X)*(x-m.X)+(z-m.Z)*(z-m.Z);
                        if(d<best) {best=d;column=new[]{x,body.Y,z,a};}
                    }
            }
            if(!fits) {why="ships:sling-too-small";return null;}
            if(column==null) {why="ships:sling-blocked";return null;}
            var path=new List<double[]>{new[]{m.X,m.Y,m.Z}};
            if(down)
            {
                path.Add(new[]{m.X,top,m.Z});path.Add(new[]{column[0],top,column[2]});path.Add(column);
                return path;
            }
            // up: it stands under the opening; set on the deck beside it, as near as there's room
            if(Math.Abs(m.Y-column[1])>.6 || Gap(m.X,m.Z)>.5) {why="ships:sling-not-under";return null;}
            double[] spot=null;
            for(double r=1;r<=3.5+1e-9 && spot==null;r+=.25)
                for(int k=0;k<16 && spot==null;k++)
                    foreach(double a in new[]{0.0,Math.PI/2})
                    {
                        var s=new CreatureDeckMotion(ox+r*Math.Sin(k*Math.PI/8),deck,oz+r*Math.Cos(k*Math.PI/8),m.Length,m.Height,m.Width,a);
                        if(s.Stuck(g) || !s.Standing(g)) continue;
                        spot=new[]{s.X,deck,s.Z,a};break;
                    }
            if(spot==null) {why="ships:sling-no-room";return null;}
            path.Add(new[]{column[0],m.Y,column[2]});path.Add(new[]{column[0],top,column[2]});path.Add(new[]{spot[0],top,spot[2]});path.Add(spot);
            return path;
        }
        void EaseInto(VoxelGrid g,double rx,double rz)
        {
            int main=Math.Abs(rx)>=Math.Abs(rz)?0:2,side=2-main;
            double r=main==0?rx:rz;
            if(Math.Abs(r)<1e-4) return;
            for(double s=.025;s<=EaseReach+1e-9;s+=.025)
                foreach(int sign in new[]{1,-1})
                {
                    double x=X,z=Z;
                    double moved=Sweep(g,side,sign*s);
                    if(Math.Abs(moved-sign*s)>1e-8) continue;
                    if(side==0) X+=moved;else Z+=moved;
                    bool gap=Math.Abs(Sweep(g,main,r))>1e-4;
                    X=x;Z=z;
                    if(!gap) continue;
                    // a little way toward it each tick, and on through once it's lined up
                    double step=Math.Min(s,Ease)*sign;
                    if(side==0) X+=Sweep(g,0,step);else Z+=Sweep(g,2,step);
                    if(s<=Ease+1e-9) {double m=Sweep(g,main,r);if(main==0) X+=m;else Z+=m;}
                    return;
                }
        }
    }
}
