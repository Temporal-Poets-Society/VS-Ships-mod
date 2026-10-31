using System;

namespace Hansa
{
    /// <summary>Resolve a creature's native movement against the ship, using its whole collision box. AI and gravity stay vanilla.</summary>
    public sealed class CreatureDeckMotion
    {
        public double X,Y,Z;
        public readonly double HalfX,HalfZ,Height;
        public bool OnGround;
        public CreatureDeckMotion(double x,double y,double z,double halfX,double height,double halfZ)
        { X=x;Y=y;Z=z;HalfX=halfX;Height=height;HalfZ=halfZ; }
        public bool Stuck(VoxelGrid g) => g.Collides(X-HalfX,Y+.001,Z-HalfZ,X+HalfX,Y+Height,Z+HalfZ);
        public bool Over(VoxelGrid g) => X+HalfX>g.OX && X-HalfX<g.MaxX && Z+HalfZ>g.OZ && Z-HalfZ<g.MaxZ && Y>g.OY &&
            g.Collides(X-HalfX,g.OY,Z-HalfZ,X+HalfX,Y+.01,Z+HalfZ);
        public bool Move(VoxelGrid g,double dx,double dy,double dz)
        {
            double Sweep(int axis,double distance) => g.Sweep(axis,distance,X-HalfX,Y,Z-HalfZ,X+HalfX,Y+Height,Z+HalfZ);
            bool Supported() => g.Collides(X-HalfX,Y-.04,Z-HalfZ,X+HalfX,Y-.001,Z+HalfZ);
            double my=Sweep(1,dy);Y+=my;OnGround=dy<0 && Math.Abs(my-dy)>1e-8 || Supported();
            double mx=Sweep(0,dx);X+=mx;
            double mz=Sweep(2,dz);Z+=mz;
            // Ordinary half-block steps, without lifting a creature through a deck or a low beam.
            if(OnGround && (Math.Abs(mx-dx)>1e-8 || Math.Abs(mz-dz)>1e-8) && Math.Abs(Sweep(1,.5)-.5)<1e-8)
            {
                double x=X,z=Z;Y+=.5;
                X+=Sweep(0,dx-mx);Z+=Sweep(2,dz-mz);
                if(Math.Abs(X-x)+Math.Abs(Z-z)<1e-8) Y-=.5;
                else Y+=Sweep(1,-.5);
            }
            OnGround=Supported();return Over(g);
        }
    }
}
