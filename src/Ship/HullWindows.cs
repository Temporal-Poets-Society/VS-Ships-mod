using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa
{
    public static class HullWindows
    {
        public static bool Fits(ShipDefinition def,HullSlotDef panel) => panel?.Outer?.Length==4 && panel.Normal?.Length==3 &&
            panel.Sill>=def.DeckY-.01 && panel.Normal.Sum(v=>v*v)>.8 && Math.Abs(panel.Normal[1])<.5 && panel.Head-panel.Sill>.4 && panel.Area>.35;
        /// <summary>A leaded pane in a removable hull panel, fitted to its real face and inset inside the planking.</summary>
        public static List<ModularPart> Parts(HullSlotDef panel)
        {
            var list=new List<ModularPart>();if(panel?.Outer?.Length!=4) return list;
            var p=panel.Outer;var normal=panel.Normal;
            double length=Math.Sqrt(p[0].Zip(p[1],(a,b)=>(a-b)*(a-b)).Sum());
            double height=Math.Sqrt(p[0].Zip(p[3],(a,b)=>(a-b)*(a-b)).Sum());
            double u=Math.Min(.2,.07/Math.Max(.01,length)),v=Math.Min(.2,.07/Math.Max(.01,height));
            void Strip(PartMat mat,double u0,double v0,double u1,double v1,double thickness)
            {
                var c=new double[8][];
                for(int k=0;k<8;k++)
                {
                    double a=(k&1)==0?u0:u1,b=(k&2)==0?v0:v1,depth=-.075+((k&4)==0?-thickness/2:thickness/2);
                    c[k]=Enumerable.Range(0,3).Select(i=>(1-b)*((1-a)*p[0][i]+a*p[1][i])+b*((1-a)*p[3][i]+a*p[2][i])+normal[i]*depth).ToArray();
                }
                list.Add(new ModularPart {Mat=mat,Corners=c});
            }
            Strip(PartMat.Wood,0,0,u,1,.08);Strip(PartMat.Wood,1-u,0,1,1,.08);
            Strip(PartMat.Wood,u,0,1-u,v,.08);Strip(PartMat.Wood,u,1-v,1-u,1,.08);
            Strip(PartMat.Glass,u,v,1-u,1-v,.016);
            // The cames lie above the pane and stop at the casing. Their crossing has one shared volume.
            Strip(PartMat.Lead,.49,v,.51,1-v,.03);
            Strip(PartMat.Lead,u,.49,.49,.51,.03);Strip(PartMat.Lead,.51,.49,1-u,.51,.03);
            return list;
        }
    }
}
