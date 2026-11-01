using System;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (1 November 2026, the user: "cannot get elk on rope past gate (at least in storm)") An adult elk (vanilla box 1.3 ×
        /// 1.5) led from a quay through her starboard entry port (0.82 wide) onto her main deck and back, on her own grid.
        /// </summary>
        static void Creatures(ShipDefinition def)
        {
            var states=new SlotStates(def.Slots);var grid=def.Grid.Clone();states.Apply(grid);
            var gate=def.Fixtures.Single(f=>f.Code=="gate-starboard");
            double gx=(gate.Box[0]+gate.Box[3])/2,deck=def.DeckY;
            // her side at the gate: the quay ends there (the outermost voxel at her deck's height)
            double side=grid.MaxZ;while(side>0 && !grid.Collides(gx-.01,deck-.2,side-.01,gx+.01,deck-.1,side)) side-=.01;
            const double length=1.3,height=1.5;
            // walk in from the quay toward her centreline: caught as AfterPhysics catches it, then moved on her deck
            (bool aboard,double x,double y,double z,string why) Lead(double x,double feet,double yaw,double width,double sill,double headX=0)
            {
                double z=side+1.3;CreatureDeckMotion m=null;
                for(int t=0;t<400;t++)
                {
                    double dx=headX*.05,dz=-.05;
                    if(m==null)
                    {
                        if(z+length/2<side) return (false,x,feet,z,"fell off the quay at her side");
                        var c=new CreatureDeckMotion(x,feet,z,length,height,width,yaw);
                        if(c.Catch(grid,yaw,CreatureDeckMotion.Hop,sill)) {c.Move(grid,dx,-.02,dz);if(c.OnGround) {m=c;continue;}}
                        x+=dx;z+=dz;continue;
                    }
                    m.Turn(grid,yaw);
                    if(!m.Move(grid,dx,-.02,dz)) return (false,m.X,m.Y,m.Z,"left her");
                    if(m.Z<side-2.2) return (true,m.X,m.Y,m.Z,"on her deck");
                }
                return m==null ? (false,x,feet,z,"never caught") : (false,m.X,m.Y,m.Z,"stopped at "+m.Z.ToString("0.##"));
            }
            double south=Math.PI;   // heading -z, in through the starboard port
            void In(string what,(bool aboard,double x,double y,double z,string why) r,bool want=true) =>
                Check(r.aboard==want && (!want || Math.Abs(r.y-deck)<.05),$"creatures: {what}: {r.why} ({r.x:0.##}, {r.y:0.###}, {r.z:0.##})");
            In("an elk led straight through her starboard gate walks onto her deck",Lead(gx,deck,south,length/2,deck));
            In("one 0.2 to one side eases into the gap",Lead(gx+.2,deck,south,length/2,deck));
            In("one coming in at 25° squares itself to the gate",Lead(gx,deck,south+.44,length/2,deck,-.02));
            In("one off a quay 0.6 below her sill (she's lifted on a swell) hops up onto it",Lead(gx,deck-.6,south,length/2,deck));
            In("(before) vanilla's square 1.3 box can't pass the 0.82 gate",Lead(gx,deck,south,length,deck),false);
            In("one off a quay 1.3 below her sill doesn't",Lead(gx,deck-1.3,south,length/2,deck),false);
            {
                // beside the gate her bulwark stands 1.07 high: from a quay 0.2 above her deck the elk never ends up on her rail
                var r=Lead(gx-2,deck+.2,south,length/2,deck);
                Check(!r.aboard && r.y<deck+.3,$"creatures: along her side, off a quay above her deck, it's never caught on top of her rail: {r.why} ({r.x:0.##}, {r.y:0.###}, {r.z:0.##})");
            }
            {
                // and out again: from her deck through the gate, leaving her frame for the quay
                var m=new CreatureDeckMotion(gx,deck,side-2,length,height,length/2,0);
                bool left=false;
                for(int t=0;t<200 && !left;t++) {m.Turn(grid,0);left=!m.Move(grid,0,-.02,.05);}
                Check(left && Math.Abs(m.Y-deck)<.05,$"creatures: led out through the gate it leaves her deck level with it ({m.X:0.##}, {m.Y:0.###}, {m.Z:0.##})");
            }
            {
                // the body turns with the creature where it fits, and not into her timbers
                var m=new CreatureDeckMotion(gx,deck,side-.15-length/4,length,height,length/2,south);   // in the gateway
                m.Turn(grid,south+Math.PI/2);
                Check(Math.Abs(m.HalfX-length/4)<1e-9 && !m.Stuck(grid),$"creatures: in the gateway it can't turn broadside into the gate's posts (half {m.HalfX:0.###} × {m.HalfZ:0.###})");
                var open=new CreatureDeckMotion(gx,deck,side-1.7,length,height,length/2,south);
                open.Turn(grid,south+Math.PI/2);
                Check(Math.Abs(open.HalfX-length/2)<1e-9 && Math.Abs(open.HalfZ-length/4)<1e-9,"creatures: on open deck it turns broadside");
                // (rejoined, "elk had disappeared from post i tied it") saved squared across its heading in the gateway, it's found again
                var saved=new CreatureDeckMotion(gx,deck,side-.15-length/4,length,height,length/2,south);
                Check(saved.Catch(grid,south+Math.PI/2+.1) && Math.Abs(saved.HalfX-length/4)<1e-9 && Math.Abs(saved.Y-deck)<1e-9,
                    $"creatures: loaded in the gateway heading across it, its body is squared along the gateway and it stays aboard (half {saved.HalfX:0.###} × {saved.HalfZ:0.###})");
            }
            CreaturesHold(def);
        }

        /// <summary>(1 November 2026, the user: "elk cannot get down by stairs", "got stuck in stairs") Her hold hatch and ladder.</summary>
        static void CreaturesHold(ShipDefinition def)
        {
            var set=new ShipPieceSet();var plans=new System.Collections.Generic.Dictionary<ShipPiece,PiecePlan>();
            foreach(var e in def.ModularPieces.OrderBy(e=>e.Kind=="stair"?0:e.Kind=="hearth"?1:2)) {
                if(e.Kind is "hatch" or "oven") continue;
                var m=def.Mounts.ForModular(e);string kind=def.Mounts.LaidKind(e);
                var plan=m==null?null:PiecePlanner.Plan(def.Mounts,m,ModularFake(e,kind),ModularToward(e,kind,m));
                if(plan?.Error==null && plan!=null && set.Blocking(plan)==null) {var sp=ShipPiece.From(plan,set.NextId,null);set.Add(sp);plans[sp]=plan;}
            }
            var entry=def.ModularPieces.Single(e=>e.Code=="hatch-main");
            int nx=entry.Size[0],nz=entry.Size[1];
            var cells=def.Slots.Rect(entry.At[1],entry.At[0]-nx/2.0,entry.At[2]-nz/2.0,entry.At[0]+nx/2.0,entry.At[2]+nz/2.0);
            var hatch=ShipPiece.From(HatchPlanner.Plan(def.Slots,cells,false,entry.Hinge,entry.Coaming,entry.DoubleLeaf),set.NextId,null);
            hatch.Open=true;
            var ladder=set.All.First(p=>p.Role==PieceRole.Stair && p.Box[1]<0);
            VoxelGrid Grid(bool withLadder)
            {
                var g=def.Grid.Clone();new SlotStates(def.Slots).Apply(g);
                foreach(var c in HatchPlanner.CoamingParts(hatch.Box,hatch.Coaming)) g.Fill(c.Box);
                foreach(var p in set.All) if(withLadder || p!=ladder) foreach(var s in p.Solids) g.Fill(s);
                return g;
            }
            var grid=Grid(true);var bare=Grid(false);            // (the ladder taken up to clear the hatchway for the sling) it can be put back: its top landing steps over the coaming
            Check(!StairLanding.At(plans[ladder],bare) && PiecePlanner.GridClash(def.Mounts,plans[ladder],bare)==null,"creatures: the hold ladder, taken up, can be put back with her hatch and its coaming aboard (its landing steps over the coaming)");
            double deck=hatch.Box[1]-hatch.Coaming,clear=hatch.Coaming+.75;
            const double length=1.3,height=1.5;
            {
                // led across her deck toward the open hatchway, over its coaming: it stops at the edge, on her deck
                var m=new CreatureDeckMotion(-7.5,deck,0,length,height,length/2,Math.PI/2);
                for(int t=0;t<200;t++) {m.Turn(grid,Math.PI/2);m.Move(grid,.05,-.02,0);}
                Check(m.Y>deck-.01 && m.OnGround,$"creatures: an elk led along her deck into the open hatchway (2 long) stops at its edge, doesn't walk into it and get stuck on the ladder ({m.X:0.##}, {m.Y:0.###}, {m.Z:0.##})");
                // and led back the way it came it walks off the ladder's head and over the coaming onto her deck
                for(int t=0;t<200;t++) {m.Turn(grid,-Math.PI/2);m.Move(grid,-.05,-.02,0);}
                Check(m.X<-6 && Math.Abs(m.Y-deck)<.01 && m.OnGround,$"creatures: led back from the hatchway's edge it's on her deck again ({m.X:0.##}, {m.Y:0.###}, {m.Z:0.##})");
            }
            var beside=new CreatureDeckMotion(-4,deck,1.25,length,height,length/2,Math.PI/2);
            Check(!beside.Stuck(grid) && beside.Standing(grid),"creatures: there's room for the elk on her deck beside the hatch");
            var path=CreatureDeckMotion.Sling(grid,beside,hatch.Box[0],hatch.Box[2],hatch.Box[3],hatch.Box[5],deck,clear,out string why);
            Check(path==null && why=="ships:sling-blocked",$"creatures: with the hold ladder standing in the hatch the elk can't be slung through ({why})");
            path=CreatureDeckMotion.Sling(bare,beside,hatch.Box[0],hatch.Box[2],hatch.Box[3],hatch.Box[5],deck,clear,out why);
            var end=path?[^1];
            var below=end==null?null:new CreatureDeckMotion(end[0],end[1],end[2],length,height,length/2,end[3]);
            Check(below!=null && end[1]<deck-2 && !below.Stuck(bare) && below.Standing(bare),
                $"creatures: with the ladder taken up it's slung down through the hatch onto the hold floor ({why}; ends {(end==null?"-":string.Join(", ",end.Select(v=>v.ToString("0.###"))))})");
            if(below==null) return;
            var up=CreatureDeckMotion.Sling(bare,below,hatch.Box[0],hatch.Box[2],hatch.Box[3],hatch.Box[5],deck,clear,out why);
            var top=up?[^1];
            var back=top==null?null:new CreatureDeckMotion(top[0],top[1],top[2],length,height,length/2,top[3]);
            Check(back!=null && Math.Abs(top[1]-deck)<1e-9 && !back.Stuck(bare) && back.Standing(bare),
                $"creatures: and hoisted up again onto her deck beside the hatch ({why}; ends {(top==null?"-":string.Join(", ",top.Select(v=>v.ToString("0.###"))))})");
            var far=new CreatureDeckMotion(4,deck,0,length,height,length/2,0);
            Check(CreatureDeckMotion.Sling(bare,far,hatch.Box[0],hatch.Box[2],hatch.Box[3],hatch.Box[5],deck,clear,out why)==null && why=="ships:sling-too-far",
                "creatures: an animal across her deck must be led to the hatch first");
        }
    }
}
