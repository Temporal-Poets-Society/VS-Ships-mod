using System;
using System.Collections.Generic;
using System.Linq;
using Hansa;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.1.0-dev.9, caravel note 61: "gates dont close when trying to close them when aim on open state gate. aim only at
        /// gate closed position opens them. Also when gates are open then collide with hull side") Each gate is picked where
        /// its leaf stands: shut, in its opening; open, where the leaf swung to (the note's own ray, at the empty opening,
        /// no longer takes an open gate). And its leaf, swung open as drawn (its mesh group turned about its moving part's
        /// axis), stays out of her hull: sampled inside each of its faces, no point of it enters a cell of her collision
        /// it wasn't in shut. The leaf's axis and angle are the design's (the caravel's pipeline): a clash is reported with
        /// the axis that would clear it.
        ///   dotnet run --project tests/HansaTests -- gates
        /// </summary>
        static void CaravelGates(string root)
        {
            Console.WriteLine("the caravel's entry port gates (note 61)");
            var (def, file) = LoadHullDesign(root, "caravel");
            var gates = def.Fixtures.Where(f => f.Type == "gate").ToArray();
            Check(gates.Length == 2, $"caravel: two entry port gates ({gates.Length})");
            foreach (var g in gates)
            {
                var shut = g.SelBox;
                var open = g.OpenSel;
                Check(open != null && shut != null, $"{g.Code}: picked where its leaf stands open ({Box(open)}; shut {Box(shut)})");
                if (open == null || shut == null) continue;
                // from her deck inside the port, clear of her mainmast (where note 61's player stood), at a man's eye: a ray to each
                // box's middle (as far as that)
                var eye = new Vec3d(g.Pos[0], shut.Y1 + 1.62, Math.Sign(g.Pos[2]) * 1.25);
                bool Picks(Cuboidd at, bool isOpen)
                {
                    var to = new Vec3d((at.X1 + at.X2) / 2 - eye.X, (at.Y1 + at.Y2) / 2 - eye.Y, (at.Z1 + at.Z2) / 2 - eye.Z);
                    double len = to.Length();
                    var dir = new Vec3d(to.X / len, to.Y / len, to.Z / len);
                    return ShipFittings.Pick(def, def.Grid, eye, dir, len, f => true, f => isOpen && f == g, f => null, out _) == g;
                }
                Check(Picks(shut, false) && !Picks(open, false), $"{g.Code}: shut, it's used where it stands shut, not where it would stand open");
                Check(Picks(open, true) && !Picks(shut, true), $"{g.Code}: open, it's used where its leaf stands open, not at the gap it shuts");
            }
            // the note's eye (eyeLocal): its ray at the port gate's place shut took it shut; open, a ray at the leaf takes it
            var port = gates.FirstOrDefault(f => f.Code == "gate-port");
            if (port?.OpenSel != null)
            {
                var eye = new Vec3d(port.Pos[0] - .271, 2.826, -1.247);
                var look = new Vec3d(0.0784, -0.5206, -0.8502);
                var shutHit = ShipFittings.Pick(def, def.Grid, eye, look, 4.5, f => true, f => false, f => null, out _);
                var o = port.OpenSel;
                var to = new Vec3d((o.X1 + o.X2) / 2 - eye.X, (o.Y1 + o.Y2) / 2 - eye.Y, (o.Z1 + o.Z2) / 2 - eye.Z);
                double len = to.Length();
                var openHit = ShipFittings.Pick(def, def.Grid, eye, new Vec3d(to.X / len, to.Y / len, to.Z / len), 4.5, f => true, f => f == port, f => null, out _);
                Check(shutHit == port && openHit == port, $"note 61's eye: the port gate shut at its place ({shutHit?.Code ?? "nothing"}), open at its leaf ({openHit?.Code ?? "nothing"})");
            }

            // (25 October 2026, note 91: "here opening click on gate does not register") note 91's ray, the starboard gate shut:
            // it meets her collision at the gate's jamb foot, a cell the shut gate fills (her grid rounds its box out to x -1.0,
            // its selection box starts at -0.91); the crosshair named the gate there, and now the click takes it too (as she
            // rolled 1.61° then, either way)
            var stbd = gates.FirstOrDefault(f => f.Code == "gate-starboard");
            if (stbd != null)
                foreach (double roll in new[] { 0.0, 1.61, -1.61 })
                {
                    double r = roll * Math.PI / 180, ly = -0.6357, lz = 0.7718;
                    var look = new Vec3d(-0.0143, ly * Math.Cos(r) - lz * Math.Sin(r), ly * Math.Sin(r) + lz * Math.Cos(r));
                    var grid = def.Grid.Clone();
                    ShipFittings.ApplySolids(def, grid, f => false, f => null);
                    var hit = ShipFittings.Pick(def, grid, new Vec3d(stbd.Pos[0] - .429, 2.824, 1.098), look, 4.5, f => true, f => false, f => null, out double t);
                    Check(hit == stbd, $"note 91's ray (rolled {roll}°), the gate shut: takes {hit?.Code ?? "nothing"} at {t:0.00}");
                }

            // the leaves swung open as drawn, against her own faces round the port
            if (file?.Groups == null) { Console.WriteLine("  (her mesh file not there: the swing not checked)"); return; }
            foreach (var g in gates)
            {
                var part = def.MovingParts.FirstOrDefault(p => p.Source == "open:" + g.Code);
                if (part == null || !file.Groups.TryGetValue(part.PartGroup, out var leaf) || leaf == null) { Check(false, $"{g.Code}: its leaf ({part?.PartGroup}) is in her mesh"); continue; }
                var hull = file.Groups.Where(kv => !kv.Key.StartsWith("part:") && kv.Value != null).SelectMany(kv => kv.Value)
                    .Where(q => q != null && q.Length >= 32 && Enumerable.Range(0, 4).Any(k => Math.Abs(q[1 + 8 * k] - part.AxisPoint[0]) < 1.6 &&
                        Math.Abs(q[2 + 8 * k] - part.AxisPoint[1]) < 1.6 && Math.Abs(q[3 + 8 * k] - part.AxisPoint[2]) < 1.6)).ToList();
                int clash = SwingClash(part, leaf, hull, out string where);
                // the axis on the leaf's inner face at its after edge, as a door swinging inboard is hung
                double inner = g.Pos[2] < 0 ? leaf.Max(q => Enumerable.Range(0, 4).Max(k => q[3 + 8 * k])) : leaf.Min(q => Enumerable.Range(0, 4).Min(k => q[3 + 8 * k]));
                var moved = new MovingPartDef { Code = part.Code, Kind = part.Kind, Source = part.Source, Angle = part.Angle, Limit = part.Limit,
                    AxisDir = part.AxisDir, AxisPoint = new[] { part.AxisPoint[0], part.AxisPoint[1], inner } };
                int movedClash = SwingClash(moved, leaf, hull, out string movedWhere);
                Check(clash == 0, $"{g.Code}: its leaf swinging open ({part.Angle:0}° about x {part.AxisPoint[0]:0.###}, z {part.AxisPoint[2]:0.###}) stays out of her side" +
                    (clash > 0 ? $": {clash} of its {leaf.Length} faces go through hers ({where}); about z {inner:0.###} (its inner face): {movedClash}{(movedWhere != null ? " (" + movedWhere + ")" : "")}" : ""));
            }
        }

        /// <summary>
        /// How many faces of a leaf (its mesh group's quads) go through her faces near it (others' quads) as it swings from
        /// shut to open (twenty steps): an edge of either through the other. Each face is taken a little in from its edges,
        /// so faces that only meet (the shut leaf against its jambs) don't count; nor what already met shut.
        /// </summary>
        static int SwingClash(MovingPartDef part, float[][] leaf, List<float[]> hull, out string where)
        {
            where = null;
            var hullQ = hull.Select(q => Inset(q, null)).ToList();
            var bad = new HashSet<int>();
            HashSet<(int, int)> shut = null;
            var m = new float[16];
            for (int s = 0; s <= 20; s++)
            {
                PartMotion.Place(part, s / 20f, m, null);
                var now = new HashSet<(int, int)>();
                for (int i = 0; i < leaf.Length; i++)
                {
                    if (leaf[i] == null || leaf[i].Length < 32) continue;
                    var lq = Inset(leaf[i], m);
                    for (int j = 0; j < hullQ.Count; j++)
                    {
                        if (!Crosses(lq, hullQ[j]) && !Crosses(hullQ[j], lq)) continue;
                        if (s == 0) { now.Add((i, j)); continue; }
                        if (shut.Contains((i, j)) || !bad.Add(i)) continue;
                        var c = Mid(lq);
                        where ??= $"at {c[0]:0.##} {c[1]:0.##} {c[2]:0.##}, {s * 5}% open";
                    }
                }
                if (s == 0) shut = now;
            }
            return bad.Count;
        }

        /// <summary>A quad's corners (moved by m, if given), each 3 % of the way in toward its middle.</summary>
        static double[][] Inset(float[] q, float[] m)
        {
            var p = new double[4][];
            for (int k = 0; k < 4; k++)
            {
                double x = q[1 + 8 * k], y = q[2 + 8 * k], z = q[3 + 8 * k];
                p[k] = m == null ? new[] { x, y, z } : new double[]
                {
                    m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]
                };
            }
            var c = Mid(p);
            for (int k = 0; k < 4; k++) for (int a = 0; a < 3; a++) p[k][a] += (c[a] - p[k][a]) * 0.03;
            return p;
        }

        static double[] Mid(double[][] p) => new[] { p.Average(v => v[0]), p.Average(v => v[1]), p.Average(v => v[2]) };

        /// <summary>Does an edge of quad a pass through quad b (its two triangles)?</summary>
        static bool Crosses(double[][] a, double[][] b)
        {
            for (int k = 0; k < 4; k++)
            {
                var p = a[k];
                var q = a[(k + 1) % 4];
                if (SegTri(p, q, b[0], b[1], b[2]) || SegTri(p, q, b[0], b[2], b[3])) return true;
            }
            return false;
        }

        /// <summary>Does the segment p..q pass through the triangle (a, b, c), strictly inside both?</summary>
        static bool SegTri(double[] p, double[] q, double[] a, double[] b, double[] c)
        {
            double[] d = { q[0] - p[0], q[1] - p[1], q[2] - p[2] }, e1 = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, e2 = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
            double[] h = { d[1] * e2[2] - d[2] * e2[1], d[2] * e2[0] - d[0] * e2[2], d[0] * e2[1] - d[1] * e2[0] };
            double det = e1[0] * h[0] + e1[1] * h[1] + e1[2] * h[2];
            if (Math.Abs(det) < 1e-12) return false;
            double inv = 1 / det;
            double[] sv = { p[0] - a[0], p[1] - a[1], p[2] - a[2] };
            double u = (sv[0] * h[0] + sv[1] * h[1] + sv[2] * h[2]) * inv;
            if (u <= 0 || u >= 1) return false;
            double[] qv = { sv[1] * e1[2] - sv[2] * e1[1], sv[2] * e1[0] - sv[0] * e1[2], sv[0] * e1[1] - sv[1] * e1[0] };
            double v = (d[0] * qv[0] + d[1] * qv[1] + d[2] * qv[2]) * inv;
            if (v <= 0 || u + v >= 1) return false;
            double t = (e2[0] * qv[0] + e2[1] * qv[1] + e2[2] * qv[2]) * inv;
            return t > 1e-6 && t < 1 - 1e-6;
        }

        /// <summary>
        /// (0.1.0-dev.9, the user on the caravel: "opening gate and getting close to opening and very quickly snaps to rope
        /// ladder. way to far that happens") Walking at an open entry port from her deck, her rope ladder takes you only at the
        /// port: where she stops you there (the timbers either side of it), or at its top exit; till dev.8, anywhere within 1
        /// of its top exit, 0.7 before she'd stop you. And it still takes you there, down to the water. Walking along her
        /// deck past the port, or out with the gate shut, it doesn't.
        /// </summary>
        static void CaravelLadderCatch(string root)
        {
            Console.WriteLine("the caravel's rope ladders taken at the entry ports");
            var (def, _) = LoadHullDesign(root, "caravel");
            var open = def.Grid.Clone();
            ShipFittings.ApplySolids(def, open, f => true, f => null);
            foreach (var l in def.Ladders.Where(x => x.Kind == "rope" && x.Outside))
            {
                double s = Math.Sign(l.Top[2]);
                // where she stops a man walking straight out at the port, with no ladder to take him
                var w = new DeckMover(l.ExitTop[0], l.ExitTop[1], s * 1.25);
                w.Unstick(open);
                for (int i = 0; i < 5 * 60; i++) w.Step(open, 1 / 60f, 0, s, 3.5, false, false, 1, null, 0);
                double stop = w.Z;
                // from 1.25 off her middle line, level with the ladder's top exit, straight out
                var m = new DeckMover(l.ExitTop[0], l.ExitTop[1], s * 1.25);
                m.Unstick(open);
                m.TopClosed = _ => false;
                double caught = double.NaN;
                var res = DeckMover.Result.Ok;
                for (int i = 0; i < 10 * 60 && res == DeckMover.Result.Ok; i++)
                {
                    double before = m.Z;
                    res = m.Step(open, 1 / 60f, 0, s, 3.5, false, false, 1, def.Ladders, 1);
                    if (m.Climbing == l && double.IsNaN(caught)) caught = before;
                }
                // (her collision may stop a man short of the top exit (dev.8's 0.5 gap); a wider gap lets him walk on out)
                bool stopped = Math.Abs(stop) < Math.Abs(l.Top[2]);
                Check(!double.IsNaN(caught) && Math.Abs(caught) >= Math.Min(stopped ? Math.Abs(stop) : double.MaxValue, Math.Abs(l.ExitTop[2])) - 0.1,
                    $"{l.Code}: taken at z {caught:0.00}, {(stopped ? $"where she stops you at the port ({stop:0.00})" : "she doesn't stop you short of it")}, its top exit {l.ExitTop[2]:0.00}; till dev.8 from {s * (Math.Abs(l.ExitTop[2]) - 1):0.00}");
                Check(res == DeckMover.Result.LeftShip && Math.Abs(m.Y - l.ExitBottom[1]) < 0.01, $"{l.Code}: and down it into the water ({res})");
                // a step short of where she stops you, walking out: not yet; at its top exit (as a climber comes off it): yes
                var near = new DeckMover(l.ExitTop[0], l.ExitTop[1], (stopped ? stop : l.ExitTop[2]) - s * 0.3);
                Check(near.LadderAhead(def.Ladders, 0, s, out _, open) == null && DeckMover.AtTop(l, l.ExitTop[0], l.ExitTop[2]),
                    $"{l.Code}: 0.3 short of the port it doesn't take you; at its top exit it does");
                // along her deck past the port (pressed to her side, walking aft and forward), and out with the gate shut: never
                var shut = def.Grid.Clone();
                ShipFittings.ApplySolids(def, shut, f => false, f => null);
                bool along = false, shutTook = false;
                foreach (double dir in new[] { -1.0, 1.0 })
                {
                    var a = new DeckMover(l.ExitTop[0] - dir * 1.5, l.ExitTop[1], stop);
                    a.Unstick(open);
                    a.TopClosed = _ => false;
                    for (int i = 0; i < 90; i++) { a.Step(open, 1 / 60f, dir, 0, 3.5, false, false, 1, def.Ladders, 1); along |= a.Climbing != null; }
                }
                var c = new DeckMover(l.ExitTop[0], l.ExitTop[1], s * 1.25);
                c.Unstick(shut);
                c.TopClosed = _ => true;
                for (int i = 0; i < 3 * 60; i++) { c.Step(shut, 1 / 60f, 0, s, 3.5, false, false, 1, def.Ladders, 1); shutTook |= c.Climbing != null; }
                Check(!along && !shutTook, $"{l.Code}: walking along her deck past the port ({along}), or out with the gate shut ({shutTook}), it doesn't take you");
            }
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel note 78: "we could have option to add vanilla figure heads") Her figurehead's place at her
        /// stem head: a shown hook that takes figureheads, facing the bow, at her stem's fore face (her collision behind it,
        /// the figure's block in front of it free of her, and wider than her stem, so it's seen past it from her deck).
        /// </summary>
        static void CaravelFigurehead(string root)
        {
            Console.WriteLine("the caravel's figurehead place (note 78)");
            var (def, _) = LoadHullDesign(root, "caravel");
            var l = MountLayout.Build(def);
            var m = l.Hooks.FirstOrDefault(h => h.Kinds != null && Array.IndexOf(h.Kinds, PiecePlanner.FigureheadKind) >= 0);
            Check(m != null && m.Shown && m.Hang == "up" && m.Normal?[0] > 0.9, $"a shown figurehead place facing the bow ({m?.Id ?? "none"})");
            if (m == null) return;
            var b = m.Box;
            bool behind = def.Grid.SolidAt(m.X - 0.15, m.Y + 0.5, m.Z);
            bool free = !def.Grid.Collides(b[0] + 0.03, b[1] + 0.03, b[2] + 0.03, b[3] - 0.03, b[4] - 0.03, b[5] - 0.03);
            Check(behind && b[0] >= m.X - 0.01 && b[3] - b[0] > 0.99 && b[5] - b[2] > 0.99,
                $"{m.Id}: against her stem's fore face at x {m.X:0.00}, its block in front ({Box(new Cuboidd(b[0], b[1], b[2], b[3], b[4], b[5]))}){(free ? "" : ", partly in her collision's cells there")}");
            // (25 October 2026, note 100: "cannot place figure head") note 100's crosshair, from her main deck at the stem head:
            // what it meets of her is within reach of the place (a figurehead in hand goes there; till dev.9 only places
            // next to what was met, and this one is on the far side of the stem)
            var eye = new Vec3d(6.624, 2.819, 1.111);
            var look = new Vec3d(0.9128, 0.2436, -0.3277);
            bool met = AimPlace.GridHit(def.Grid, eye, look, 5.5, out double t, out _);
            double away = met ? AimPlace.BoxDistance(b, eye.X + look.X * t, eye.Y + look.Y * t, eye.Z + look.Z * t) : double.NaN;
            Check(met && away < AimPlace.FigureheadReach, $"note 100's crosshair meets her stem {away:0.00} from the place (within {AimPlace.FigureheadReach})");
            // and the crosshair on her mainmast or her deck amidships doesn't take it
            bool mast = AimPlace.GridHit(def.Grid, new Vec3d(2, 2.8, 0), new Vec3d(-1, 0, 0), 5, out double tm, out _);
            Check(!mast || AimPlace.BoxDistance(b, 2 - tm, 2.8, 0) > AimPlace.FigureheadReach, "the crosshair on her mainmast is nowhere near it");
        }

        /// <summary>
        /// (25 October 2026, caravel note 102: "does rudder and handle work geometrically. handle applies torque in different
        /// axis than rudder swivels") Her rudder and tiller turn as one about the rudder's own axis (the same axis in her
        /// moving parts, so one rotation), and that axis is the rudder's hinge line: her rudder's fore edge lies on it from
        /// heel to head. Reported: the tiller's angle to it (90° sweeps a plane at right angles to the axis; less, a cone) and
        /// how far its tip rises or drops at full helm.
        /// </summary>
        static void CaravelRudder(string root)
        {
            Console.WriteLine("the caravel's rudder and tiller (note 102)");
            var (def, file) = LoadHullDesign(root, "caravel");
            var rudder = def.MovingParts.FirstOrDefault(p => p.Code == "rudder");
            var tiller = def.MovingParts.FirstOrDefault(p => p.Code == "tiller");
            Check(rudder != null && tiller != null && rudder.Source == tiller.Source && rudder.Angle == tiller.Angle &&
                  Enumerable.Range(0, 3).All(i => Math.Abs(rudder.AxisPoint[i] - tiller.AxisPoint[i]) < 1e-6 && Math.Abs(rudder.AxisDir[i] - tiller.AxisDir[i]) < 1e-6),
                "her rudder and tiller turn about one axis, by the same helm");
            if (rudder == null || tiller == null || file?.Groups == null) return;
            double[] a = rudder.AxisPoint, u = rudder.AxisDir;
            double ul = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            u = new[] { u[0] / ul, u[1] / ul, u[2] / ul };
            // the rudder about that axis, all along it (sampled inside its faces): how far forward of the axis it reaches (toward
            // her bow, square to the axis) and how far aft. ((27 October 2026) It turns about its stock: the axis on the stock's
            // middle (the upright post's rudder, its stock 0.2 thick, its fore face 0.1 forward of the axis) or on the blade's
            // fore edge (the raked one's): either way its fore face 0 to 0.15 forward of the axis, the blade aft of it.)
            double nx = u[1], ny = -u[0];
            double nl = Math.Sqrt(nx * nx + ny * ny);
            if (nl < 1e-9) { nx = 1; ny = 0; nl = 1; }
            if (nx < 0) { nx = -nx; ny = -ny; }
            nx /= nl;
            ny /= nl;
            var foreAt = new SortedDictionary<int, double>();
            var aftAt = new SortedDictionary<int, double>();
            foreach (var q in file.Groups.TryGetValue(rudder.PartGroup, out var rq) ? rq : Array.Empty<float[]>())
                for (double s = 0; s <= 1; s += 0.2)
                    for (double t = 0; t <= 1; t += 0.2)
                    {
                        double[] p = new double[3];
                        for (int k = 0; k < 3; k++)
                            p[k] = (1 - s) * ((1 - t) * q[1 + k] + t * q[9 + k]) + s * ((1 - t) * q[25 + k] + t * q[17 + k]);
                        double[] r = { p[0] - a[0], p[1] - a[1], p[2] - a[2] };
                        double along = r[0] * u[0] + r[1] * u[1] + r[2] * u[2];
                        double fwd = r[0] * nx + r[1] * ny;
                        int bin = (int)Math.Floor(along / 0.5);
                        foreAt[bin] = foreAt.TryGetValue(bin, out double f0) ? Math.Max(f0, fwd) : fwd;
                        aftAt[bin] = aftAt.TryGetValue(bin, out double a0) ? Math.Min(a0, fwd) : fwd;
                    }
            double foreMin = foreAt.Count > 0 ? foreAt.Values.Min() : double.NaN, foreMax = foreAt.Count > 0 ? foreAt.Values.Max() : double.NaN;
            bool bladeAft = aftAt.Values.Count(v => v < -0.3) >= aftAt.Count / 2;
            Check(foreAt.Count > 4 && foreMin >= -0.05 && foreMax <= 0.15 && bladeAft,
                $"her rudder turns about that axis: its fore face {foreMin:0.000} to {foreMax:0.000} forward of it all along its {foreAt.Count * 0.5:0.0} blocks, its blade aft of it");
            // the tiller's long way and its tip at full helm
            var tq = file.Groups.TryGetValue(tiller.PartGroup, out var tqs) ? tqs : Array.Empty<float[]>();
            var tips = tq.SelectMany(q => Enumerable.Range(0, 4).Select(k => new[] { (double)q[1 + 8 * k], q[2 + 8 * k], q[3 + 8 * k] })).ToList();
            if (tips.Count == 0) return;
            var fore = tips.OrderByDescending(p => p[0]).First();
            var aft = tips.OrderBy(p => p[0]).First();
            double[] dv = { fore[0] - aft[0], fore[1] - aft[1], fore[2] - aft[2] };
            double dl = Math.Sqrt(dv[0] * dv[0] + dv[1] * dv[1] + dv[2] * dv[2]);
            double angle = Math.Acos(Math.Abs((dv[0] * u[0] + dv[1] * u[1] + dv[2] * u[2]) / dl)) * 180 / Math.PI;
            var m = new float[16];
            PartMotion.Place(tiller, 1, m, null);
            double y1 = m[1] * fore[0] + m[5] * fore[1] + m[9] * fore[2] + m[13];
            Console.WriteLine($"  --   her tiller lies at {angle:0}° to that axis (90°: it sweeps a plane square to it); at full helm its tip {(y1 - fore[1] >= 0 ? "rises" : "drops")} {Math.Abs(y1 - fore[1]):0.00}");
        }

        static string Box(Cuboidd b) => b == null ? "none" : $"{b.X1:0.##} {b.Y1:0.##} {b.Z1:0.##} .. {b.X2:0.##} {b.Y2:0.##} {b.Z2:0.##}";

        /// <summary>
        /// (0.1.0-dev.9, caravel note 66: "lanters does not show light, even there is light around") A lantern aboard is drawn
        /// in its own light, as vanilla's lantern block stands in its own: vanilla's lanterns' light (small and large, plain
        /// and lined, as BlockLantern gives it) is a full, warm light; no light, none.
        /// </summary>
        static void LanternOwnLight()
        {
            Console.WriteLine("a lantern aboard in its own light (note 66)");
            foreach (var hsv in new[] { new byte[] { 7, 3, 18 }, new byte[] { 7, 3, 20 }, new byte[] { 7, 3, 22 } })
            {
                var own = SeeThroughFaces.OwnLight(hsv);
                Check(own != null && Math.Max(own.X, Math.Max(own.Y, own.Z)) > 0.99f && own.X >= own.Z && own.Z > 0.5f,
                    $"a lantern's light [{string.Join(", ", hsv)}]: drawn in {own?.X:0.##} {own?.Y:0.##} {own?.Z:0.##} (full, warm)");
            }
            Check(SeeThroughFaces.OwnLight(new byte[] { 7, 3, 0 }) == null && SeeThroughFaces.OwnLight(null) == null, "no light: none of its own");
        }
    }
}
