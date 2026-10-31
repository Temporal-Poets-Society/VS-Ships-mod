using System;
using System.Reflection;
using Hansa;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace HansaTests
{
    // ClothSystem's constructor only needs the world's seed. No game or graphics context is started.
    public class TetherApiStub : DispatchProxy
    {
        public IWorldAccessor World;
        protected override object Invoke(MethodInfo method,object[] args) => method.Name=="get_World"?World:
            method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
    }
    static partial class Program
    {
        static void Tethers()
        {
            var api=DispatchProxy.Create<ICoreAPI,TetherApiStub>();
            ((TetherApiStub)(object)api).World=DispatchProxy.Create<IWorldAccessor,TetherApiStub>();
            foreach(double distance in new[]{0,.1,.49,1.1,1.5,4}) {
                var start=new Vec3d(20,-1.875,30);var target=start.AddCopy(distance,0,0);
                var safe=TetherGeometry.RopeTarget(start,target);
                var native=ClothSystem.CreateRope(api,new ClothManager(),start,safe,null);
                int points=0;bool finite=true;native.WalkPoints(p=> {points++;finite &= double.IsFinite(p.Pos.X+p.Pos.Y+p.Pos.Z);});
                Check(finite && points>=4 && native.FirstPoint!=native.LastPoint && Math.Abs(start.DistanceTo(safe)-Math.Max(1.5,distance))<1e-9,
                    $"155 rope: {distance} m creature distance creates finite cloth with distinct endpoints and minimum length");
            }
            var grid=new VoxelGrid(-2,-1,-2,32,24,32,.125,new byte[32*24*32/8]);
            grid.Fill(new[]{-2.0,-1,-2,2,0,2});
            var from=new Vec3d(0,.8,0);var to=new Vec3d(.4,-2,.3);
            int hit=TetherGeometry.Move(grid,from,to);
            Check(hit==2 && Math.Abs(to.Y-TetherGeometry.Radius)<1e-7 && to.X==.4 && to.Z==.3,
                "155 rope: a fast-falling loose point stops above the deck with visible thickness and slides along it");
            grid.Fill(new[]{.5,0,-2, .75,2,2});
            to.Set(1,1,.2);hit=TetherGeometry.Move(grid,from,to);
            Check(hit==1 && Math.Abs(to.X-(.5-TetherGeometry.Radius))<1e-7 && to.Y==1,
                "155 rope: a loose point cannot tunnel through a wall or post");
            grid.Fill(new[]{-.5,-1,-.5,.5,0,.5},false);
            to.Set(0,-.5,0);hit=TetherGeometry.Move(grid,new Vec3d(0,.8,0),to);
            Check(hit==0 && to.Y==-.5,"155 rope: an actual deck opening lets the rope fall through");
            var before=new TetherFrame(100,30,-60,.4,.05,-.1);
            var after=new TetherFrame(105,32,-62,1.2,-.08,.14);
            var local=new Vec3d(2,-1.3,-.6);var p=before.World(local);
            Check(after.Local(after.Carry(p,before)).DistanceTo(local)<1e-10,
                "155 rope: loose points follow ship translation, yaw, pitch and roll in the same local frame");
            var cameraFrame=new TetherFrame(after.X+.3,after.Y+.15,after.Z-.4,after.Yaw,after.Pitch,after.Roll);
            Check(cameraFrame.World(local).SubCopy(after.World(local)).DistanceTo(new Vec3d(.3,.15,-.4))<1e-10,
                "155 rope: post pins and loose points can use the same camera-relative ship origin as deck creatures");
        }
    }
}
