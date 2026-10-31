using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    public sealed class BeamFrameDef
    {
        public string Deck;
        public double[] Stations;
        public double[] Interrupted;
        public double Top, Width=.2, Depth=.4;
        public double[][] Existing;
    }

    /// <summary>One boundary round the union of removed beam lengths, supported by the nearest whole beams.</summary>
    public static class OpeningFrames
    {
        public static List<float[]> Mesh(ShipDefinition def,IEnumerable<ModularPart> parts) => ModularMesh.Quads(parts,
            (def.BeamFrames??Array.Empty<BeamFrameDef>()).SelectMany(f=>f.Existing??Array.Empty<double[]>()).Select(b=>ModularPart.B(PartMat.Wood,b)),false);

        public static List<ModularPart> Build(ShipDefinition def,SlotStates states)
        {
            var result=new List<ModularPart>();
            if(states==null || def.BeamFrames==null) return result;
            foreach(var frame in def.BeamFrames)
            {
                var beams=def.DeckSlots.Select((s,i)=>(Slot:s,Index:i)).Where(s=>s.Slot.IsBeam && s.Slot.Deck==frame.Deck).ToArray();
                var missing=beams.Where(s=>!states.Filled(s.Index)).ToArray();
                bool Whole(double x)=>!(frame.Interrupted??Array.Empty<double>()).Any(s=>Math.Abs(s-x)<.01) && !missing.Any(s=>Math.Abs((s.Slot.Box[0]+s.Slot.Box[3])/2-x)<.01);
                var openings=new List<double[]>();
                foreach(var beam in missing) {
                    var b=beam.Slot.Box;double x=(b[0]+b[3])/2;
                    var aft=frame.Stations.Where(s=>s<x-.01 && Whole(s)).ToArray();
                    var fore=frame.Stations.Where(s=>s>x+.01 && Whole(s)).ToArray();
                    if(aft.Length==0 || fore.Length==0) continue;
                    openings.Add(new[]{aft.Max()+frame.Width/2,b[2],fore.Min()-frame.Width/2,b[5]});
                }
                var xs=openings.SelectMany(b=>new[]{b[0],b[2]}).Distinct().OrderBy(x=>x).ToArray();
                var zs=openings.SelectMany(b=>new[]{b[1],b[3]}).Distinct().OrderBy(z=>z).ToArray();
                bool Open(double x,double z)=>openings.Any(b=>x>b[0] && x<b[2] && z>b[1] && z<b[3]);
                foreach(double z in zs)
                {
                    for(int i=0;i+1<xs.Length;i++) {
                        double mid=(xs[i]+xs[i+1])/2;bool low=Open(mid,z-.001),high=Open(mid,z+.001);
                        if(low==high) continue;
                        var parts=new List<double[]> {new[]{xs[i],frame.Top-frame.Depth,high?z-frame.Width:z,xs[i+1],frame.Top,high?z:z+frame.Width}};
                        // A carling seats against each surviving transverse timber; never draws over its top.
                        foreach(double station in frame.Stations) {
                            double zmid=(parts[0][2]+parts[0][5])/2;
                            bool removed=missing.Any(s=>station>s.Slot.Box[0] && station<s.Slot.Box[3] && zmid>s.Slot.Box[2] && zmid<s.Slot.Box[5]);
                            if(!removed) parts=Subtract(parts,new[]{station-frame.Width/2,frame.Top-frame.Depth,zmid-1,station+frame.Width/2,frame.Top,zmid+1});
                            if(parts.Count==0) break;
                        }
                        foreach(var existing in frame.Existing??Array.Empty<double[]>()) parts=Subtract(parts,existing);
                        result.AddRange(parts.Select(b=>ModularPart.B(PartMat.Wood,b)));
                    }
                }
            }
            return result;
        }

        static List<double[]> Subtract(List<double[]> boxes,double[] cut)
        {
            var result=new List<double[]>();
            foreach(var b in boxes) {
                if(!PiecePlanner.Overlap(b,cut,1e-8)) {result.Add(b);continue;}
                var inner=PiecePlanner.ClipTo(b,cut);var core=(double[])b.Clone();
                for(int a=0;a<3;a++) {
                    if(core[a]<inner[a]-1e-8) {var part=(double[])core.Clone();part[a+3]=inner[a];result.Add(part);core[a]=inner[a];}
                    if(core[a+3]>inner[a+3]+1e-8) {var part=(double[])core.Clone();part[a]=inner[a+3];result.Add(part);core[a+3]=inner[a+3];}
                }
            }
            return result;
        }
    }
}
