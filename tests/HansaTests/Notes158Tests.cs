using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        static void Notes158(string root)
        {
            int before=failures;
            var (def,_)=LoadHullDesign(root,"caravel");
            // the default caravel as spawned: her modular pieces laid, the hatch over its design opening (PlaceModularHatch)
            var set=new ShipPieceSet();
            foreach(var e in def.ModularPieces.OrderBy(e=>e.Kind=="stair"?0:e.Kind=="hearth"?1:2)) {
                if(e.Kind is "hatch" or "oven") continue;
                var m=def.Mounts.ForModular(e);string kind=def.Mounts.LaidKind(e);
                var plan=m==null?null:PiecePlanner.Plan(def.Mounts,m,ModularFake(e,kind),ModularToward(e,kind,m));
                if(plan?.Error==null && plan!=null && set.Blocking(plan)==null) set.Add(ShipPiece.From(plan,set.NextId,null));
            }
            var entry=def.ModularPieces.Single(e=>e.Code=="hatch-main");
            int nx=entry.Size[0],nz=entry.Size[1];
            var cells=def.Slots.Rect(entry.At[1],entry.At[0]-nx/2.0,entry.At[2]-nz/2.0,entry.At[0]+nx/2.0,entry.At[2]+nz/2.0);
            var hatch=ShipPiece.From(HatchPlanner.Plan(def.Slots,cells,false,entry.Hinge,entry.Coaming,entry.DoubleLeaf),set.NextId,null);
            set.Add(hatch);
            // her live collision (EntityShip.RebuildGrid) and her design's (DesignGrid: cells as they are, shut hatches)
            var states=new SlotStates(def.Slots);
            var grid=def.Grid.Clone();states.Apply(grid);
            foreach(var p in set.All) {
                if(p.Role==PieceRole.Hatch) foreach(var c in HatchPlanner.CoamingParts(p.Box,p.Coaming)) grid.Fill(c.Box);
                if(p.Role is PieceRole.Door or PieceRole.Hatch && p.Open) continue;
                foreach(var s in p.Solids) grid.Fill(s);
            }
            var design=def.Grid.Clone();states.Apply(design);foreach(var s in hatch.Solids) design.Fill(s);
            const double range=4.5,eyeH=1.7;
            (ShipPiece P,double T,double Wall) Client(Vec3d o,Vec3d d) {
                var p=ShipPieceSet.Aimed(set,grid,o,d,range,out double tp,out double wall);
                return (p,tp,wall);
            }
            bool Server(Vec3d eye,ShipPiece p)=>p!=null && ShipPieceSet.SeenPiece(design,eye,p);
            foreach(var (feet,aim) in new[]{(new Vec3d(-5.70,1.13,.22),new Vec3d(-4.430,1.500,.140)),(new Vec3d(-5.70,1.13,-.42),new Vec3d(-3.673,1.500,-.120))}) {
                var eye=feet.AddCopy(0,eyeH,0);var d=aim.SubCopy(eye).Normalize();
                var (p,t,wall)=Client(eye,d);
                Console.WriteLine($"  note ray from {eye} to {aim}: client {(p==null?"nothing":p.Role.ToString())} at {t:0.###}, her collision at {wall:0.###}");
                Check(p==hatch && Server(eye,p),$"158: the note's ray at ({aim.X:0.###}, {aim.Z:0.###}) aims at the hatch and the server lets it be used");
                // (before: the leaf at t met behind its own rounded voxel more than 0.15 nearer, not its Solids: dropped, nothing sent)
                Check(t>wall+.15 && !ShipPieceSet.OwnHit(hatch,eye,d,wall) && ShipPieceSet.HatchVoxelHit(hatch,grid,eye,d,wall),
                    $"158: the note's ray meets the hatch's walking voxel at {wall:0.###}, {t-wall:0.###} before its leaf (the old client rule dropped it)");
            }
            {
                var eye=new Vec3d(-5.70,2.83,.22);var d=new Vec3d(-4.430,1.500,.140).SubCopy(eye).Normalize();
                var blocked=grid.Clone();blocked.Fill(new[]{-5.25,1.9,-.25,-5.0,2.5,.5});
                Check(ShipPieceSet.Aimed(set,blocked,eye,d,range,out _,out _)==null,"158: other timber across that ray still hides the hatch from the client");
            }
            // a grid of rays over the hatch's top from where the players stood and stand beside it
            int n=0,missed=0,refused=0;var misses=new List<string>();
            // (1 November 2026, the hatch one block toward the main mast: its forward end now on the mast's cell, so the player
            // ahead of it stands at its forward corners either side of the mast, not in it)
            foreach(var feet in new[]{new Vec3d(-5.70,1.13,.22),new Vec3d(-5.70,1.13,-.42),new Vec3d(-2.7,1.13,.95),new Vec3d(-2.7,1.13,-.95),new Vec3d(-4,1.13,1.2),new Vec3d(-4,1.13,-1.2),new Vec3d(-5.7,1.125,.965)})
                for(double x=hatch.Box[0]+.05;x<hatch.Box[3];x+=.1) for(double z=hatch.Box[2]+.05;z<hatch.Box[5];z+=.1) {
                    var eye=feet.AddCopy(0,eyeH,0);var aim=new Vec3d(x,hatch.Box[4],z);var d=aim.SubCopy(eye).Normalize();
                    if(eye.DistanceTo(aim)>range-.05) continue;
                    n++;var (p,t,wall)=Client(eye,d);
                    if(p!=hatch) {missed++;if(misses.Count<6) misses.Add($"({x:0.00},{z:0.00}) from ({feet.X},{feet.Z}): {p?.Role.ToString()??"nothing"} t {t:0.###} wall {wall:0.###}");}
                    else if(!Server(eye,p)) refused++;
                }
            foreach(var s in misses) Console.WriteLine("  miss "+s);
            Check(n>500 && missed==0 && refused==0,$"158: {n} rays over the shut hatch's top, from both ends and sides: {missed} not aimed at it, {refused} refused by the server");
            // (the leaves rise to 100°, past upright: a body beside them clears once its middle is 0.39 past the hinge edge, 0.32 at
            // 90°; one beside the coaming (0.125 wide) stands with its middle 0.425 out or more: the swing never refused the clicker)
            bool natural=true;
            foreach(double y in new[]{1.125,1.375}) foreach(double s in new[]{.425,.5,.65,.8}) foreach(double x in new[]{-4.7,-4,-3.3}) foreach(int side in new[]{-1,1})
                natural &= !HatchPlanner.BodyInSwing(hatch.Box,hatch.Dir,true,false,new[]{x-.3,y,side*(.5+s)-.3,x+.3,y+1.85,side*(.5+s)+.3});
            Check(natural && HatchPlanner.BodyInSwing(hatch.Box,hatch.Dir,true,false,new[]{-4.3,1.375,.5+.35-.3,-3.7,3.225,.5+.35+.3}),
                "158: a player beside the coaming (middle 0.425-0.8 past either hinge edge, on the deck or the coaming) is clear of the swing; one leaning over the leaves is not");
            // (1 November 2026, chalk note 158) layout revision 1: a caravel launched before keeps her old opening's cells (her
            // own hatch and stair stay where they were), one changed by her player stays hers, a new one has the new opening
            bool Out(SlotStates s,string id)=>!s.Filled(def.Slots.IndexOf(id));
            var oldShip=new SlotStates(def.Slots);
            var moved=LayoutMigration.Apply(def,0,new Dictionary<string,int>(),oldShip,null,new HashSet<string>(def.ModularPieces.Select(e=>e.Key)));
            var fresh=new SlotStates(def.Slots);
            var mineState=new SlotStates(def.Slots);mineState.Set(def.Slots.IndexOf("main:-6:-1"),SlotState.Filled);
            LayoutMigration.Apply(def,0,new Dictionary<string,int>{["main:-6:-1"]=(int)SlotState.Filled},mineState,null,new HashSet<string>());
            Check(def.LayoutRev==1 && new[]{"main:-6:-1","main:-4:-1","beam:main:-6:-1"}.All(id=>def.Slots.IndexOf(id)>=0) &&
                  Out(oldShip,"main:-6:-1") && Out(oldShip,"main:-5:-1") && !Out(oldShip,"main:-4:-1") && Out(oldShip,"beam:main:-6:-1") && moved.NewlyLaid.Count==0 &&
                  !Out(fresh,"main:-6:-1") && Out(fresh,"main:-5:-1") && Out(fresh,"main:-4:-1") && !Out(fresh,"beam:main:-6:-1") && !Out(mineState,"main:-6:-1"),
                "158: an older caravel keeps her opening at -6..-4 ("+string.Join(", ",moved.Notes)+"), a player's own cell stays hers, a new one opens -5..-3");
            Console.WriteLine(failures==before?"notes158 ALL OK":$"notes158 {failures-before} FAILED");
        }
    }
}
