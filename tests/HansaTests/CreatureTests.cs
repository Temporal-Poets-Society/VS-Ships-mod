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
            }
        }
    }
}
