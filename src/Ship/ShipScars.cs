using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, note 207: "shooting the other ship but looks like no damage") Where a ball struck her: a splintered scar on
    /// her planking where it went in, and where it came out of her far side, outside and in. Every hit leaves one, on a hull
    /// panel or not (her fixed planking, a strip, a wale, her castles' sides, which take a shot as her structure and show no
    /// panel's overlay). Each is a ray (where to look from, which way, how far) the clients follow to her drawn surfaces
    /// (<see cref="ShipRenderer"/>), so the scar lies on what's drawn, not on her collision. Saved and sent with her
    /// ("hansaScars"), the latest <see cref="MaxScars"/>; mending or laying a part takes off the scars on it.
    /// </summary>
    public struct ShotScar
    {
        public float X, Y, Z, DX, DY, DZ, Reach;
        /// <summary>0: on her outside (lit as outside), 1: inside her; and which of the two looks.</summary>
        public byte Inside, Variant;

        /// <summary>About where it is (half way along its ray).</summary>
        public Vec3d Point => new Vec3d(X + DX * Reach * 0.5, Y + DY * Reach * 0.5, Z + DZ * Reach * 0.5);
    }

    public partial class EntityShip
    {
        /// <summary>How many scars she keeps (the oldest go first).</summary>
        public const int MaxScars = 64;
        /// <summary>(0.3.30, note 234) Shots leave no scars (the self-test may turn it off to check the old drawing).</summary>
        public static bool NoScars = true;
        public readonly List<ShotScar> Scars = new();
        /// <summary>Bumped whenever her scars change (the renderer builds them again).</summary>
        public int ScarVersion;
        int scarSeed;

        void InitScars()
        {
            LoadScars();
            if (Api.Side == EnumAppSide.Client) WatchedAttributes.RegisterModifiedListener("hansaScars", LoadScars);
        }

        /// <summary>
        /// (0.3.30, note 234: "nothing about holes from cannon") Server, once her damage is loaded: the scars a save brought
        /// (0.3.27–0.3.29) become what a shot leaves now: the nearest slot to each (a hull panel, a deck cell; within
        /// RouteReach) cracked if it was sound. Then none are kept. How many slots were cracked.
        /// </summary>
        public int ScarsToCracks()
        {
            if (Api?.Side != EnumAppSide.Server || Scars.Count == 0) return 0;
            int n = 0;
            var l = DamageParts;
            if (l != null && PartDamage != null && (l.HullSlotted || l.CellSlotted))
            {
                var d = l.D;
                foreach (var s in Scars)
                {
                    var pt = s.Point;
                    int best = -1;
                    double bd = d.RouteReach;
                    foreach (var p in l.Parts)
                    {
                        if (p.Slot < 0 && p.Cell < 0) continue;
                        if (p.Inner != (s.Inside == 1) && p.Partner >= 0) continue;     // (inside: the ceiling's; outside: the planking's)
                        double dist = p.DistanceTo(pt);
                        if (dist < bd) { bd = dist; best = p.Index; }
                    }
                    if (best < 0 || PartDamage.Hurt[best] >= d.Cracked) continue;
                    PartDamage.Hurt[best] = d.Cracked;
                    n++;
                }
                if (n > 0)
                {
                    SyncSlotsFromHurt();
                    SaveSlots();
                    SaveDamage(true);
                    RebuildGrid();
                }
            }
            World.Logger.Audit("[ships] {0} #{1}: her {2} shot scars from an older save made {3} cracked slots (0.3.30: no scars)", GetName(), EntityId, Scars.Count, n);
            Scars.Clear();
            WatchedAttributes.RemoveAttribute("hansaScars");
            ScarVersion++;
            return n;
        }

        void LoadScars()
        {
            Scars.Clear();
            var a = (WatchedAttributes["hansaScars"] as FloatArrayAttribute)?.value;
            if (a != null)
                for (int i = 0; i + 8 < a.Length; i += 9)
                    Scars.Add(new ShotScar { X = a[i], Y = a[i + 1], Z = a[i + 2], DX = a[i + 3], DY = a[i + 4], DZ = a[i + 5], Reach = a[i + 6], Inside = (byte)a[i + 7], Variant = (byte)a[i + 8] });
            ScarVersion++;
        }

        void SaveScars()
        {
            var a = new float[Scars.Count * 9];
            for (int n = 0; n < Scars.Count; n++)
            {
                var s = Scars[n];
                int i = n * 9;
                a[i] = s.X; a[i + 1] = s.Y; a[i + 2] = s.Z; a[i + 3] = s.DX; a[i + 4] = s.DY; a[i + 5] = s.DZ; a[i + 6] = s.Reach; a[i + 7] = s.Inside; a[i + 8] = s.Variant;
            }
            WatchedAttributes["hansaScars"] = new FloatArrayAttribute(a);
            WatchedAttributes.MarkPathDirty("hansaScars");
            ScarVersion++;
        }

        /// <summary>
        /// Server: a ball struck her at hit (ship-local) going along dir (ship-local, unit), and came out at exit (null: it
        /// stayed in her). Scars outside and inside where it went in, and where it came out.
        /// </summary>
        public void AddShotScars(Vec3d hit, Vec3d dir, Vec3d exit)
        {
            // ((0.3.30, note 234) no scars any more: a shot cracks or knocks out slots (ShipDamage); kept for old callers)
            if (NoScars) return;
            if (Api?.Side != EnumAppSide.Server || hit == null || dir == null) return;
            double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
            if (len < 1e-9) return;
            double ux = dir.X / len, uy = dir.Y / len, uz = dir.Z / len;
            void Add(double ox, double oy, double oz, double sx, byte inside, float reach)
            {
                Scars.Add(new ShotScar
                {
                    X = (float)ox, Y = (float)oy, Z = (float)oz, DX = (float)(ux * sx), DY = (float)(uy * sx), DZ = (float)(uz * sx), Reach = reach,
                    Inside = inside, Variant = (byte)(scarSeed++ & 7)
                });
            }
            // where it went in: from outside along its way; from inside back toward it
            Add(hit.X - ux * 0.6, hit.Y - uy * 0.6, hit.Z - uz * 0.6, 1, 0, 1.3f);
            Add(hit.X + ux * 1.0, hit.Y + uy * 1.0, hit.Z + uz * 1.0, -1, 1, 1.1f);
            if (exit != null)
            {
                // where it came out: from inside along its way; from outside back toward it
                Add(exit.X - ux * 1.0, exit.Y - uy * 1.0, exit.Z - uz * 1.0, 1, 1, 1.1f);
                Add(exit.X + ux * 0.6, exit.Y + uy * 0.6, exit.Z + uz * 0.6, -1, 0, 1.3f);
            }
            if (Scars.Count > MaxScars) Scars.RemoveRange(0, Scars.Count - MaxScars);
            SaveScars();
        }

        /// <summary>Server: the scars on a box (ship-local, grown by margin) taken off (it's mended, laid again). How many.</summary>
        public int ClearScars(double[] box, double margin = 0.6)
        {
            if (Api?.Side != EnumAppSide.Server || box == null || Scars.Count == 0) return 0;
            int n = Scars.RemoveAll(s =>
            {
                var p = s.Point;
                return p.X > box[0] - margin && p.X < box[3] + margin && p.Y > box[1] - margin && p.Y < box[4] + margin && p.Z > box[2] - margin && p.Z < box[5] + margin;
            });
            if (n > 0) SaveScars();
            return n;
        }

        /// <summary>Server: every scar off (a command).</summary>
        public int ClearAllScars()
        {
            int n = Scars.Count;
            Scars.Clear();
            if (Api?.Side == EnumAppSide.Server) SaveScars();
            return n;
        }

        /// <summary>A scar that's on a hull panel that's out (its opening is drawn instead): not drawn.</summary>
        public bool ScarOnOpening(ShotScar s)
        {
            var l = HullLayout;
            if (l == null || HullPanels == null) return false;
            var p = s.Point;
            for (int n = 0; n < l.Count; n++)
            {
                if (HullPanels.Filled(n)) continue;
                var b = l.Slots[n].Box;
                if (b != null && p.X > b[0] - 0.05 && p.X < b[3] + 0.05 && p.Y > b[1] - 0.05 && p.Y < b[4] + 0.05 && p.Z > b[2] - 0.1 && p.Z < b[5] + 0.1) return true;
            }
            return false;
        }
    }
}
