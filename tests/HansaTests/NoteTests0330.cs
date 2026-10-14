using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.30) Chalk notes 234-243 and the user's plugging from inside: shots crack or knock out slots only (234), the
        /// poop's side ports gone (236), every gun's arc swept against her as written (237), the ports fixed (239), the
        /// lower deck's panel (238), the binnacle's lamp (240), the planking's textures at the bow (242), a door shut from its
        /// open leaf (243).
        ///   dotnet run -c Debug -- notes0330
        /// </summary>
        static void Notes0330(string root)
        {
            Console.WriteLine("0.3.30: chalk notes 234-243");
            var (md, file) = LoadHullDesign(root);
            var raw = JObject.Parse(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-m.json")));
            Ports0330(md);
            GunSweep0330(md, file, raw);
            Shots0330(md);
            Panels0330(md);
            Binnacle0330(md, file);
            Textures0330(md, file);
            Door0330(md);
        }

        // ------------------------------------------------------------------ 236, 239: fixed ports, none on the poop

        static void Ports0330(ShipDefinition md)
        {
            var guns = md.Fixtures.Where(f => f.Type == "gun").ToList();
            bool poopNone = !guns.Any(f => f.Code.Contains("-poop-")) && !md.GunPlaces.Any(p => p.Gun.Contains("-poop-")) &&
                            !(md.MovingParts?.Any(p => p.Code != null && p.Code.Contains("-poop-")) ?? false);
            Check(poopNone, $"0.3.30 (note 236): no gun, gun place or lid on the poop's sides ({guns.Count} guns: the waist's, the quarterdeck's, the forecastle's, the stern chasers)");
            bool noSlots = md.Slots == null || !md.Slots.Slots.Any(s => s.IsPort || s.IsGunPort);
            bool placesFixed = md.GunPlaces.All(p => p.Port == null && p.GunPort == null);
            bool lidded = guns.All(f => f.Lid != null);
            Check(noSlots && placesFixed && lidded, $"0.3.30 (note 239): no port boards or trapdoor ports (deck slots of kind port/gunport: {(noSlots ? "none" : "some")}), every gun place's port fixed ({placesFixed}) and lidded ({lidded})");
            // a 0.3.29 save's port slot states: dropped (their ids gone), the rest kept
            var st = new SlotStates(md.Slots);
            var t = new Vintagestory.API.Datastructures.TreeAttribute();
            t.SetInt("port:gun-port-quarter-1", 0);
            t.SetInt("gunport:gun-port-quarter-1", 0);
            t.SetInt("port:gun-port-poop-1", 1);
            string cell = md.Slots.Slots.First(s => s.IsCell).Id;
            t.SetInt(cell, 0);
            st.FromTree(t);
            Check(st.Differences == 1 && !st.Filled(md.Slots.IndexOf(cell)),
                $"0.3.30: a 0.3.29 save's port boards and ports (port:, gunport:) are dropped as she loads (her quarterdeck's port is the fixed one, the poop's plain bulwark), her deck cells kept ({st.Differences} difference)");
        }

        // ------------------------------------------------------------------ 237: every gun's arc, swept

        sealed class SweepModel
        {
            public double AxisY, Step, Over, Reach;
            public double[] BX, BR, Backs;
            public double[][] Boxes;
        }

        /// <summary>Points over a quad (corners p0..p3, bilinear, every `step` or closer: as the generator's sweep_points).</summary>
        static void SweepPoints(float[] q, double step, List<double[]> into)
        {
            var P = new double[4][];
            for (int k = 0; k < 4; k++) P[k] = new double[] { q[1 + 8 * k], q[2 + 8 * k], q[3 + 8 * k] };
            double D(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
            double lu = Math.Max(D(P[1], P[0]), D(P[2], P[3])), lv = Math.Max(D(P[3], P[0]), D(P[2], P[1]));
            int nu = Math.Max(1, (int)Math.Ceiling(lu / step - 1e-9)), nv = Math.Max(1, (int)Math.Ceiling(lv / step - 1e-9));
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    double u = (double)i / nu, v = (double)j / nv;
                    var p = new double[3];
                    for (int a = 0; a < 3; a++) p[a] = (1 - u) * (1 - v) * P[0][a] + u * (1 - v) * P[1][a] + u * v * P[2][a] + (1 - u) * v * P[3][a];
                    into.Add(p);
                }
        }

        /// <summary>
        /// Does anything of hers (points) come inside the gun (its barrel's outer radius, its carriage's boxes) trained so,
        /// laid at each elevation, run out and back? As the generator's GunSweep.hits, margin 0.
        /// </summary>
        static string SweepHit(FixtureDef f, SweepModel m, List<double[]> pts, double train, IEnumerable<double> elevs)
        {
            double ox = f.Out[0], oz = f.Out[2];
            double ol = Math.Sqrt(ox * ox + oz * oz);
            double ux = ox / ol, uz = oz / ol;
            // w = u x up = (u.y*0 - u.z*1, u.z*0 - u.x*0, u.x*1 - u.y*0) = (-uz, 0, ux)
            double wx = -uz, wz = ux;
            double sign = oz >= 0 ? 1 : -1;
            double phi = -train * Math.PI / 180 * sign, c = Math.Cos(phi), s = Math.Sin(phi);
            double px = f.Pivot[0], pz = f.Pivot[2];
            var o = f.Pos;
            foreach (var p in pts)
            {
                double dx = p[0] - px, dz = p[2] - pz;
                double qx = px + dx * c + dz * s, qy = p[1], qz = pz - dx * s + dz * c;
                double rx = qx - o[0], ry = qy - o[1], rz = qz - o[2];
                double lx = rx * ux + rz * uz, ly = ry, lz = rx * wx + rz * wz;
                if (lx < m.BX[0] - 0.3 - m.Backs.Max() - 0.1 || lx > m.BX[^1] + 0.3 || Math.Abs(lz) >= 0.6 || ly >= 1.2) continue;
                foreach (double b in m.Backs)
                {
                    double x = lx + b;
                    foreach (var bx in m.Boxes)
                        if (x > bx[0] && x < bx[3] && ly > bx[1] && ly < bx[4] && lz > bx[2] && lz < bx[5]) return $"carriage (run back {b}) at {p[0]:0.00},{p[1]:0.00},{p[2]:0.00}";
                    foreach (double e in elevs)
                    {
                        double er = -e * Math.PI / 180, yy = ly - m.AxisY;
                        double xb = x * Math.Cos(er) - yy * Math.Sin(er), yb = x * Math.Sin(er) + yy * Math.Cos(er);
                        if (xb < m.BX[0] || xb > m.BX[^1]) continue;
                        // (the radius table: linear between its points, as numpy's interp)
                        int k = Array.BinarySearch(m.BX, xb);
                        double rr;
                        if (k >= 0) rr = m.BR[k];
                        else
                        {
                            k = ~k;
                            if (k <= 0) rr = m.BR[0];
                            else if (k >= m.BX.Length) rr = m.BR[^1];
                            else rr = m.BR[k - 1] + (m.BR[k] - m.BR[k - 1]) * (xb - m.BX[k - 1]) / (m.BX[k] - m.BX[k - 1]);
                        }
                        if (Math.Sqrt(yb * yb + lz * lz) < rr) return $"barrel (laid {e}, run back {b}) at {p[0]:0.00},{p[1]:0.00},{p[2]:0.00}";
                    }
                }
            }
            return null;
        }

        static void GunSweep0330(ShipDefinition md, ShipSkinFile file, JObject raw)
        {
            var gs = raw["gunSweep"];
            if (gs == null || file?.Groups == null) { Check(false, "0.3.30 (note 237): her design has the guns' sweep model (gunSweep) and her mesh"); return; }
            var m = new SweepModel
            {
                AxisY = gs["axisY"].Value<double>(), Step = gs["step"].Value<double>(), Over = gs["over"].Value<double>(), Reach = gs["reach"].Value<double>(),
                BX = gs["barrelX"].Values<double>().ToArray(), BR = gs["barrelR"].Values<double>().ToArray(), Backs = gs["backs"].Values<double>().ToArray(),
                Boxes = gs["boxes"].Select(b => b.Values<double>().ToArray()).ToArray()
            };
            var rawFix = raw["fixtures"].Where(f => (string)f["type"] == "gun").ToDictionary(f => (string)f["code"], f => f);
            // her surfaces (every group but the moving parts', the shot's and the sails'), as quads
            var quads = file.Groups.Where(kv => !kv.Key.StartsWith("part:") && kv.Key != "shot" && !kv.Key.StartsWith("sail")).SelectMany(kv => kv.Value).Where(q => q.Length >= 33).ToList();
            int limits = 0, clear = 0, bound = 0, boundOk = 0;
            var bad = new List<string>();
            Console.WriteLine("  --   gun                          side°  trains (- aft/port) .. (+ bow/starboard)   what stops it            laid");
            foreach (var f in md.Fixtures.Where(f => f.Type == "gun"))
            {
                var rf = rawFix[f.Code];
                var why = rf["trainBound"]?.Values<string>().ToArray() ?? new[] { "", "" };
                var elevs = rf["sweepElevations"]?.Values<double>().ToArray() ?? new double[] { 0 };
                double deck = f.Pos[1];
                var pts = new List<double[]>();
                foreach (var q in quads)
                {
                    double ymin = double.MaxValue, ymax = double.MinValue, cx = 0, cz = 0, cy = 0;
                    for (int k = 0; k < 4; k++) { double y = q[2 + 8 * k]; ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y); cx += q[1 + 8 * k] / 4; cy += y / 4; cz += q[3 + 8 * k] / 4; }
                    if (ymax < deck + m.Over || ymin > deck + m.Reach) continue;
                    double rad = 0;
                    for (int k = 0; k < 4; k++) rad = Math.Max(rad, Math.Sqrt(Math.Pow(q[1 + 8 * k] - cx, 2) + Math.Pow(q[2 + 8 * k] - cy, 2) + Math.Pow(q[3 + 8 * k] - cz, 2)));
                    if (Math.Sqrt(Math.Pow(cx - f.Pivot[0], 2) + Math.Pow(cz - f.Pivot[2], 2)) > 2.6 + rad) continue;
                    SweepPoints(q, m.Step, pts);
                }
                pts = pts.Where(p => p[1] > deck + m.Over && p[1] < deck + m.Reach).ToList();
                double side = Math.Atan2(f.Out[0], Math.Abs(f.Out[2])) * 180 / Math.PI;
                Console.WriteLine($"  --   {f.Code,-28} {side,5:0.0}  {f.TrainLimits[0],7:0.00} .. {f.TrainLimits[1],6:0.00}   {why[0]} / {why[1]}   {f.ElevationLimits[0]:0.00} .. {f.ElevationLimits[1]:0.00}");
                for (int k = 0; k < 2; k++)
                {
                    double t = f.TrainLimits[k];
                    limits++;
                    string at = SweepHit(f, m, pts, t, elevs);
                    if (at == null) clear++;
                    else bad.Add($"{f.Code} at {t}: {at}");
                    if (why[k] == "hull")
                    {
                        bound++;
                        string past = SweepHit(f, m, pts, t + (k == 0 ? -1.0 : 1.0), elevs);
                        if (past != null) boundOk++;
                        else bad.Add($"{f.Code}: nothing of hers a degree past {t}");
                    }
                }
                // its elevation: clear at both ends laid square, and (where her port stops it) not a degree past
                if (SweepHit(f, m, pts, 0, new double[] { f.ElevationLimits[0], f.ElevationLimits[1] }) != null) bad.Add($"{f.Code}: not clear laid {f.ElevationLimits[0]}..{f.ElevationLimits[1]}");
            }
            foreach (var b in bad.Take(12)) Console.WriteLine("         " + b);
            Check(limits > 0 && clear == limits && boundOk == bound,
                $"0.3.30 (note 237): every gun's arc swept against her mesh: at each of its {limits} train limits its barrel and carriage clear of her (laid anywhere between its elevation limits, run out or back: {clear}); where her own surfaces stop it ({bound} limits), they're inside it a degree further ({boundOk})");
            // the bow guns: square (train 0) is their side board's perpendicular, their arcs about it
            var bow = md.Fixtures.Where(f => f.Type == "gun" && f.Code.Contains("forecastle")).ToList();
            bool normal = bow.All(f => Math.Abs(f.Out[0]) > 0.15);
            Check(bow.Count == 4 && normal, $"0.3.30 (note 237): the forecastle's guns stand along their side board's perpendicular (out {string.Join(", ", bow.Select(f => $"{Math.Atan2(f.Out[0], Math.Abs(f.Out[2])) * 180 / Math.PI:0.0}°"))} forward of square)");
        }

        // ------------------------------------------------------------------ 234: shots crack or knock out slots only

        static void Shots0330(ShipDefinition md)
        {
            var dl = md.DamageParts;
            var D = dl.D;
            var l = md.Hull;
            float radius = new CannonDef().HitRadius;
            var rnd = new Random(30);
            int shots = 0, any = 0, bad = 0, routed = 0, structureHits = 0, splashed = 0;
            var notes = new List<string>();
            // slot parts' states after: sound, cracked or out, nothing between, no seams worn
            bool Good(DamageState st, out string why)
            {
                why = null;
                for (int i = 0; i < dl.Count; i++)
                {
                    var p = dl.Parts[i];
                    if (p.Slot < 0 && p.Cell < 0) continue;
                    float h = st.Hurt[i];
                    if (h > 0 && h < D.Cracked) { why = $"{dl.Parts[i].Part} hurt {h:0.000} (between sound and cracked)"; return false; }
                    if (st.Wear[i] > 0) { why = $"{dl.Parts[i].Part} seams worn {st.Wear[i]:0.000}"; return false; }
                }
                return true;
            }
            void Shoot(Vec3d at, Vec3d dir, double e, string what, bool structure = false)
            {
                var st = new DamageState(dl.Count);
                var g = md.Grid.Clone();
                var r = ShipDamage.Apply(dl, st, g, at, dir, e, radius, DamageKinds.All);
                shots++;
                int slotHits = r.Hits.Count(h => dl.Parts[h.Part].Slot >= 0 || dl.Parts[h.Part].Cell >= 0);
                if (slotHits > 0) any++;
                if (structure) { structureHits++; if (slotHits > 0) routed++; }
                if (r.Hits.Select(h => h.Part).Distinct().Count() > (r.Pierced ? 3 : 1)) splashed++;
                if (!Good(st, out string why)) { bad++; if (notes.Count < 6) notes.Add($"{what}: {why}"); }
            }
            // her side: each planking panel's middle, from outside square and aslant, 40, 80, 145 kJ
            var outer = Enumerable.Range(0, l.Count).Where(n => !l.Slots[n].IsInner).ToList();
            foreach (int n in outer.OrderBy(_ => rnd.Next()).Take(40))
            {
                var s = l.Slots[n];
                var c = s.Centre;
                var nrm = new Vec3d(s.Normal[0], s.Normal[1], s.Normal[2]);
                foreach (var (ax, ay) in new[] { (0.0, 0.0), (0.4, 0.1), (-0.4, -0.2) })
                {
                    var dir = new Vec3d(-nrm.X + ax, -nrm.Y + ay, -nrm.Z);
                    var at = new Vec3d(c.X + nrm.X * 0.02, c.Y + nrm.Y * 0.02, c.Z + nrm.Z * 0.02);
                    foreach (double e in new[] { 40.0, 80.0, 145.0 }) Shoot(at, dir, e, $"{s.Id} {e} kJ");
                }
            }
            // her fixed planking between the panels: on a frame's line (the strips), from outside
            foreach (int n in outer.Where(n => l.Slots[n].Half == "a").OrderBy(_ => rnd.Next()).Take(20))
            {
                var s = l.Slots[n];
                double x = s.Box[0] + 0.02;
                double y = (s.Sill + s.Head) / 2;
                double z = s.OuterZ(y);
                var at = new Vec3d(x, y, z + s.Side * 0.02);
                Shoot(at, new Vec3d(0, 0, -s.Side), 145, $"the strip by {s.Id}", true);
            }
            // her decks: cells from above, 80 and 145 kJ
            foreach (var c in md.Slots.Slots.Where(s => s.IsCell).OrderBy(_ => rnd.Next()).Take(30))
                foreach (double e in new[] { 80.0, 145.0 })
                    Shoot(new Vec3d(c.CX, c.Top + 0.02, c.CZ), new Vec3d(0.3, -1, 0.2), e, $"{c.Id} {e} kJ");
            foreach (var n in notes) Console.WriteLine("         " + n);
            Check(shots > 200 && bad == 0 && any > shots * 0.8,
                $"0.3.30 (note 234: \"nothing about holes from cannon\"): {shots} shots on her side (square and aslant, 40-145 kJ), her decks and her fixed strips: every slot they reach is left sound, cracked (its damaged texture: mend it) or out (lay it again), nothing between, no seams worn ({bad} otherwise); {any} did something to a slot");
            Check(structureHits > 0 && routed == structureHits,
                $"0.3.30 (note 234): a shot on her fixed planking (a frame's strip) cracks or knocks out the nearest panel (within {D.RouteReach}): {routed} of {structureHits} (no scar, no hole of its own)");
            Check(splashed > 0, $"0.3.30 (the user: \"a ball can go through a panel and damage the panels next to it\"): the slots round a strike take {D.SplashShare:0.0} of it between them (within {D.SplashReach}): {splashed} shots hurt more than the slots in their way");
            // a 9-pounder square into a panel's middle in her hold: its neighbours cracked at least, never between
            int P = Enumerable.Range(0, l.Count).Where(n => l.Slots[n].Layer == "outer" && l.Slots[n].Side > 0 && l.Slots[n].Room == "hold").OrderBy(n => Math.Abs(l.Slots[n].Centre.X - 2)).First();
            var sp = l.Slots[P];
            var st9 = new DamageState(dl.Count);
            var r9 = ShipDamage.Apply(dl, st9, md.Grid.Clone(), new Vec3d(sp.Centre.X, sp.Centre.Y, sp.Centre.Z + 0.02), new Vec3d(0, 0, -1), 145, radius, DamageKinds.All);
            var states = r9.Hits.Select(h => h.Part).Distinct().Where(i => dl.Parts[i].Slot >= 0 || dl.Parts[i].Cell >= 0)
                .Select(i => $"{dl.Parts[i].Part}: {HullPanelRules.StateFor(D, st9.Hurt[i])}").ToList();
            Console.WriteLine($"  --   a 9-pounder into {sp.Id}: {string.Join("; ", states.Take(8))}");
            Check(states.Count >= 3 && states.All(x => !x.EndsWith("Filled")), $"0.3.30: a 9-pounder into a panel in her hold: it, its ceiling and the slots round it cracked or out ({states.Count})");
        }

        // ------------------------------------------------------------------ 238: every panel-looking spot a panel

        static void Panels0330(ShipDefinition md)
        {
            var l = md.Hull;
            // note 238: the lower deck, port side, x 6.96, y 0.43 (her planking there and its ceiling)
            var at = l.Slots.Where(s => s.Side < 0 && s.Box[0] <= 6.96 && 6.96 <= s.Box[3] && s.Sill <= 0.43 && 0.43 <= s.Head).ToList();
            Check(at.Count == 2 && at.Any(s => s.IsInner) && at.Any(s => !s.IsInner),
                $"0.3.30 (note 238: \"cannot remove here panel but can left and right\"): the lower deck's side at x 6.96 port is a panel, its planking and its ceiling ({string.Join(", ", at.Select(s => s.Id))})");
            // every frame space's half under her weather decks from the bilge up a panel, the same both sides
            var keys = l.Slots.Where(s => !s.IsInner).Select(s => (s.Station, s.Band, s.Half)).ToList();
            var port = keys.Where((k, i) => l.Slots.Where(s => !s.IsInner).ElementAt(i).Side < 0).ToHashSet();
            var stbd = keys.Where((k, i) => l.Slots.Where(s => !s.IsInner).ElementAt(i).Side > 0).ToHashSet();
            var onlyOne = port.Except(stbd).Concat(stbd.Except(port)).ToList();
            Check(onlyOne.Count == 0, $"0.3.30: her panels the same on both sides ({onlyOne.Count} on one side only{(onlyOne.Count > 0 ? ": " + string.Join(", ", onlyOne.Take(6)) : "")})");
        }

        // ------------------------------------------------------------------ 240: the binnacle

        static void Binnacle0330(ShipDefinition md, ShipSkinFile file)
        {
            var b = md.Fixtures.FirstOrDefault(f => f.Code == "binnacle");
            if (b == null || file?.Groups == null) { Check(false, "0.3.30 (note 240): her binnacle and her mesh"); return; }
            int yellow = Array.FindIndex(file.Textures, t => t.Contains("glass") && t.Contains("yellow"));
            int near = file.Groups.Values.SelectMany(v => v).Count(q => q.Length >= 33 && (int)q[0] == yellow &&
                Math.Abs(q[1] - b.Pos[0]) < 0.5 && Math.Abs(q[3] - b.Pos[2]) < 0.6 && Math.Abs(q[2] - b.Pos[1]) < 1.5);
            Check(near == 0, $"0.3.30 (note 240: \"what is this button?\": the binnacle's little lamp box, unlit): gone from its port side ({near} yellow glass quads by it)");
        }

        // ------------------------------------------------------------------ 242: the planking's textures

        static void Textures0330(ShipDefinition md, ShipSkinFile file)
        {
            if (file?.Groups == null) return;
            int n = 0, off = 0, sheared = 0;
            foreach (var g in new[] { "exterior", "interior", "slot:hull", "slot:hull:interior" })
            {
                if (!file.Groups.TryGetValue(g, out var qs)) continue;
                foreach (var q in qs)
                {
                    if (q.Length < 33 || !file.Textures[(int)q[0]].Contains("planks")) continue;
                    // (its corners: x y z u v ...; along the plank p0-p1 and p3-p2)
                    double L(int a, int b) => Math.Sqrt(Math.Pow(q[1 + 8 * a] - q[1 + 8 * b], 2) + Math.Pow(q[2 + 8 * a] - q[2 + 8 * b], 2) + Math.Pow(q[3 + 8 * a] - q[3 + 8 * b], 2));
                    double U(int a) => q[4 + 8 * a];
                    double V(int a) => q[5 + 8 * a];
                    double l01 = L(0, 1), l32 = L(3, 2);
                    // (only quads whose u runs along p0-p1, as the planking's: v the same at p0 and p1)
                    if (Math.Abs(V(0) - V(1)) > 1e-3 || l01 < 0.1 || l32 < 0.1 || L(0, 3) > 1.2) continue;
                    n++;
                    double r1 = Math.Abs(U(1) - U(0)) / (16 * l01), r2 = Math.Abs(U(2) - U(3)) / (16 * l32);
                    if (Math.Abs(r1 - 1) > 0.2 || Math.Abs(r2 - 1) > 0.2) off++;
                }
            }
            Console.WriteLine($"  --   her planking's quads: {n}, {off} with the texture's density along them off by more than a fifth");
            Check(n > 1000 && off <= n / 50, $"0.3.30 (note 242: \"textures ... distorted and stretched\"): her planking's texture a block a texture along every strake (each corner by where it is along her), the bow's and the stern's too: {off} of {n} quads stretched or squeezed by more than a fifth");
        }

        // ------------------------------------------------------------------ 243: a door shut from its open leaf

        static void Door0330(ShipDefinition md)
        {
            var st = new SlotStates(md.Slots);
            var refused = new List<string>();
            var pieces = LaunchSet0329(md, st, refused);
            var pc = pieces.All.FirstOrDefault(p => p.Role == PieceRole.Door && p.Pivot != null);
            if (pc == null) { Check(false, "0.3.30 (note 243): a door among her pieces"); return; }
            bool was = pc.Open;
            pc.Open = true;
            var leaf = pc.OpenLeafBox;
            bool picked = false;
            if (leaf != null)
            {
                // from a man's eye a block and a half off the leaf's face, at its middle (either side of it)
                var mid = new Vec3d((leaf[0] + leaf[3]) / 2, (leaf[1] + leaf[4]) / 2, (leaf[2] + leaf[5]) / 2);
                double ix = MountLayout.DX[pc.Dir], iz = MountLayout.DZ[pc.Dir];
                foreach (double sg in new[] { 1.0, -1.0 })
                {
                    var eye = new Vec3d(mid.X - sg * iz * 1.5, mid.Y + 0.2, mid.Z + sg * ix * 1.5);
                    var d = new Vec3d(mid.X - eye.X, mid.Y - eye.Y, mid.Z - eye.Z);
                    if (pieces.Pick(eye, d, 4, out _) == pc) picked = true;
                }
            }
            pc.Open = false;
            bool shutNoLeaf = pc.PickBoxes.Count() == pc.Sel.Count;
            pc.Open = was;
            Check(leaf != null && picked && shutNoLeaf, $"0.3.30 (note 243: \"check vanilla door closing mechanics\"): an open door's leaf, swung into the cell, is picked as the door (right-click it: it shuts, as vanilla's); shut, only the doorway (piece {pc.Id}, {pc.Mounts[0]})");
        }
    }
}
