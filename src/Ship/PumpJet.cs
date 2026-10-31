using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// The water a bilge pump brings up (pure: the client's PumpClient and the offline checks use it): it gushes out of
    /// the spout (the fixture's Spout and SpoutDir, ship-local) and falls onto the deck under it, in gushes with the brake's
    /// strokes, as much as the pumps really take out of her hold (the server's PumpedRate: nothing while it's dry).
    /// </summary>
    public static class PumpJet
    {
        /// <summary>How fast the water leaves the spout (blocks a second).</summary>
        public const double Speed = 1.6;
        /// <summary>The particles' gravity (vanilla's GravityEffect: × 12 blocks/s² in the game's particle physics).</summary>
        public const float GravityEffect = 0.8f;
        public const double Gravity = GravityEffect * 12;
        /// <summary>
        /// Particles a second: the most one spout gives (more than one at the brake), a full stream (one man pumping a
        /// flooded hold: the user's note 60, it looked feeble), and the fewest while water comes at all (her hold nearly dry).
        /// </summary>
        public const double MaxRate = 130, FullRate = 95, MinRate = 18;

        /// <summary>The spout (ship-local) and the way the water leaves it (unit): the fixture's, or 0.65 over it and 0.45 to its right.</summary>
        public static void Spout(FixtureDef pump, out Vec3d at, out Vec3d dir)
        {
            var (fx, fz) = pump.Facing;
            // its right hand (facing her bow: starboard, + z)
            double rx = -fz, rz = fx;
            if (pump.Spout?.Length >= 3) at = new Vec3d(pump.Spout[0], pump.Spout[1], pump.Spout[2]);
            else at = new Vec3d(pump.Pos[0] + rx * 0.45, pump.Pos[1] + 0.65, pump.Pos[2] + rz * 0.45);
            double dx, dy, dz;
            if (pump.SpoutDir?.Length >= 3) { dx = pump.SpoutDir[0]; dy = pump.SpoutDir[1]; dz = pump.SpoutDir[2]; }
            else { dx = rx * 0.966; dy = -0.259; dz = rz * 0.966; }
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dir = l < 1e-9 ? new Vec3d(rx, 0, rz) : new Vec3d(dx / l, dy / l, dz / l);
        }

        /// <summary>
        /// How long the water falls from the spout to the deck the pump stands on (its Pos height), and where it lands
        /// (ship-local).
        /// </summary>
        public static double Landing(FixtureDef pump, out Vec3d landing)
        {
            Spout(pump, out var at, out var dir);
            double h = Math.Max(0, at.Y - pump.Pos[1]);
            double vy = Speed * dir.Y;
            double t = (vy + Math.Sqrt(vy * vy + 2 * Gravity * h)) / Gravity;
            landing = new Vec3d(at.X + Speed * dir.X * t, pump.Pos[1], at.Z + Speed * dir.Z * t);
            return t;
        }

        /// <summary>What comes out of this pump (cubic blocks a second): what she pumps, shared by the pumps being worked.</summary>
        public static double FlowAt(string pumpCode, string worked, double pumpedRate)
        {
            if (pumpedRate <= 0 || string.IsNullOrEmpty(worked) || string.IsNullOrEmpty(pumpCode)) return 0;
            var codes = worked.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return Array.IndexOf(codes, pumpCode) < 0 ? 0 : pumpedRate / codes.Length;
        }

        /// <summary>
        /// Particles a second for a flow (cubic blocks a second): none without one; what one man at the brake brings up
        /// (onePumper: <see cref="ShipFlood.PumpRate"/> for one) a full stream (<see cref="FullRate"/>), less as her hold runs
        /// dry, more with more men, up to <see cref="MaxRate"/>.
        /// </summary>
        public static double Rate(double flow, double onePumper = 0.2)
        {
            if (flow <= 1e-4) return 0;
            double f = flow / Math.Max(1e-4, onePumper);
            return Math.Min(MaxRate, MinRate + (FullRate - MinRate) * f);
        }

        /// <summary>
        /// The stream swells a little with the brake (0.8 .. 1): strongest at the ends of its swing (its rocking is
        /// sin(2π t / stroke), ShipRenderer's "pump:" source); a steady stream otherwise (0.3.10: no gushes).
        /// </summary>
        public static double Pulse(double seconds, double stroke) => 1;

        /// <summary>A connected laminar tube, relative to the spout, with gravity in world Y. No particle gaps or jitter.</summary>
        public static List<float[]> Stream(Vec3d direction, double flight, double flow)
        {
            var faces=new List<float[]>();
            if(flow<=1e-4 || flight<=0) return faces;
            const int steps=20,sides=12;
            double horizontal=Math.Sqrt(direction.X*direction.X+direction.Z*direction.Z);
            double ax=horizontal<1e-6?1:-direction.Z/horizontal,az=horizontal<1e-6?0:direction.X/horizontal;
            var rings=new double[steps+1][][];
            double distance=0;
            double[] previous=null;
            for(int j=0;j<=steps;j++)
            {
                double t=flight*j/steps;
                var centre=new[]{Speed*direction.X*t,Speed*direction.Y*t-Gravity*t*t/2,Speed*direction.Z*t};
                if(previous!=null) distance+=Math.Sqrt(Math.Pow(centre[0]-previous[0],2)+Math.Pow(centre[1]-previous[1],2)+Math.Pow(centre[2]-previous[2],2));
                previous=centre;
                var tangent=new Vec3d(Speed*direction.X,Speed*direction.Y-Gravity*t,Speed*direction.Z);tangent.Normalize();
                double bx=tangent.Y*az,by=tangent.Z*ax-tangent.X*az,bz=-tangent.Y*ax;
                double radius=Math.Clamp(Math.Sqrt(flow/0.2)*0.045,0.015,0.06)*Math.Sqrt(Speed/Math.Max(Speed,Speed-direction.Y*Gravity*t));
                rings[j]=new double[sides][];
                for(int k=0;k<sides;k++)
                {
                    double angle=k*2*Math.PI/sides,c=Math.Cos(angle),s=Math.Sin(angle);
                    double nx=c*ax+s*bx,ny=s*by,nz=c*az+s*bz;
                    rings[j][k]=new[]{centre[0]+radius*nx,centre[1]+radius*ny,centre[2]+radius*nz,
                        k*2*Math.PI*radius*16/sides,distance*16,nx,ny,nz};
                }
            }
            for(int j=0;j<steps;j++) for(int k=0;k<sides;k++)
            {
                var q=new float[33];int at=1;
                int corner=0;
                foreach(var p in new[]{rings[j][k],rings[j][(k+1)%sides],rings[j+1][(k+1)%sides],rings[j+1][k]})
                {
                    for(int v=0;v<8;v++) q[at++]=(float)(v==3 && k==sides-1 && (corner==1 || corner==2) ? p[3]+rings[corner==1?j:j+1][k][3]*sides/(sides-1) : p[v]);
                    corner++;
                }
                faces.Add(q);
            }
            return faces;
        }

        /// <summary>How loud the pour is for a flow (0 .. 0.5).</summary>
        public static float Volume(double flow) => flow <= 1e-4 ? 0 : (float)Math.Min(0.5, 0.15 + flow * 1.5);
    }
}
