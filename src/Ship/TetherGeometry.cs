using System;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>The frame and collision of loose rope points. No entity, network or renderer dependencies.</summary>
    public readonly struct TetherFrame
    {
        public readonly double X,Y,Z,Yaw,Pitch,Roll;
        public TetherFrame(double x,double y,double z,double yaw,double pitch,double roll)
        {X=x;Y=y;Z=z;Yaw=yaw;Pitch=pitch;Roll=roll;}
        public Vec3d Local(Vec3d p) {
            ShipFrame.ToLocal(Yaw,Pitch,Roll,p.X-X,p.Y-Y,p.Z-Z,out double x,out double y,out double z);
            return new Vec3d(x,y,z);
        }
        public Vec3d World(Vec3d p) {
            ShipFrame.ToWorld(Yaw,Pitch,Roll,p.X,p.Y,p.Z,out double x,out double y,out double z);
            return new Vec3d(x+X,y+Y,z+Z);
        }
        public Vec3d Carry(Vec3d p,TetherFrame from) => World(from.Local(p));
    }

    public static class TetherGeometry
    {
        public const double Radius=.035;
        public static Vec3d RopeTarget(Vec3d start,Vec3d target)
        {
            var delta=target.SubCopy(start);double length=delta.Length();
            // ClothSystem uses floor(distance * 2) segments, divides by that count and needs two ends.
            // Deck walkers can overlap creatures, so enforce vanilla's own 1.5 m minimum rope length.
            if(length>=1.5) return target;
            if(length<1e-9) {delta.Set(0,1,0);length=1;}
            return start.AddCopy(delta.X*1.5/length,delta.Y*1.5/length,delta.Z*1.5/length);
        }
        /// <summary>Sweep a rope point's thickness against decks, walls and pieces; slide on the other axes.</summary>
        public static int Move(VoxelGrid grid,Vec3d from,Vec3d to)
        {
            double x=from.X,y=from.Y,z=from.Z,r=Radius;
            double dy=to.Y-y,sy=grid.Sweep(1,dy,x-r,y-r,z-r,x+r,y+r,z+r);y+=sy;
            double dx=to.X-x,sx=grid.Sweep(0,dx,x-r,y-r,z-r,x+r,y+r,z+r);x+=sx;
            double dz=to.Z-z,sz=grid.Sweep(2,dz,x-r,y-r,z-r,x+r,y+r,z+r);z+=sz;
            to.Set(x,y,z);
            return (Math.Abs(dx-sx)>1e-8?1:0)|(Math.Abs(dy-sy)>1e-8?2:0)|(Math.Abs(dz-sz)>1e-8?4:0);
        }
    }
}
