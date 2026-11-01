using System;

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
        /// False: it isn't over her, or there's no room for it there.
        /// </summary>
        public bool Catch(VoxelGrid g,double yaw,double hop=0,double sill=double.NaN)
        {
            double x0=X,y0=Y,z0=Z,square=Math.Round(yaw/(Math.PI/2))*(Math.PI/2);
            var lifts=sill-y0>1e-6 && sill-y0<=hop+1e-9 ? new[]{0,sill-y0} : new[]{0.0};
            foreach(double a in new[]{yaw,square})
            {
                Body(a,out double hx,out double hz);
                // sideways is across its squared heading
                bool alongX=Math.Abs(Math.Sin(square))>.5;
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
        public bool Move(VoxelGrid g,double dx,double dy,double dz)
        {
            double my=Sweep(g,1,dy);Y+=my;OnGround=dy<0 && Math.Abs(my-dy)>1e-8 || Supported(g);
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
            OnGround=Supported(g);return Over(g);
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
