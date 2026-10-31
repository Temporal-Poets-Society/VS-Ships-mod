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
        /// (0.3.18, step A of the slot plan: notes 162–166 and the user's chat) Placing by aim (only next to what the
        /// crosshair hits), the window's lead, railing corners and an open door (no faces in one plane), the hearth, the
        /// oven and the flue as pieces.
        /// </summary>
        static void StepA(ShipDefinition def)
        {
            Console.WriteLine("0.3.18: placing by aim, windows, corners, hearth and flue");
            var l = def.Mounts;
            var g = def.Grid;
            var planks = new FakePiece { Code = "game:planks-oak-ud", Family = "game:planks-oak", Planks = true };
            var fence = new FakePiece { Code = "game:woodenfence-oak-ew-free", Family = "game:woodenfence-oak", Fence = true };
            var chest = new FakePiece { Code = "game:chest-east", Family = "game:chest", Container = true, TurnMesh = true, Slots = 16, Sel = new[] { 0.0625, 0, 0.0625, 0.9375, 0.875, 0.9375 } };

            // ---- placing by aim (the user's point 1): a cell of the lower deck with its four sides free for walls
            MountPoint f = null;
            foreach (var m in l.Floors.Where(m => m.Room == "lowerdeck").OrderBy(m => Math.Abs(m.X - 3) + Math.Abs(m.Z - 1.5)))
            {
                var e = EdgeOf(l, m, 0);
                if (e != null && EdgeOf(l, m, 1) != null && EdgeOf(l, m, 2) != null && EdgeOf(l, m, 3) != null && l.FloorAt(m.Deck, m.I - 2, m.K) != null) { f = m; break; }
            }
            Check(f != null, "a lower deck cell with border places on its four sides");
            if (f != null)
            {
                Func<MountPoint, bool> borderOnly = x => x.Kind == MountKind.Border;
                Func<MountPoint, bool> floorOnly = x => x.Kind == MountKind.Floor && x.Level == 0;
                // from two cells back, aiming at the floor near the cell's +x edge
                var eye = new Vec3d(f.X - 1.5, f.Y + 1.6, f.Z + 0.5);
                var at = new Vec3d(f.X + 0.85, f.Y, f.Z + 0.45);
                var d = new Vec3d(at.X - eye.X, at.Y - eye.Y, at.Z - eye.Z);
                d.Mul(1 / d.Length());
                bool hit = AimPlace.GridHit(g, eye, d, 6, out double t, out var n);
                var p = eye.AddCopy(d.X * t, d.Y * t, d.Z * t);
                var wall = AimPlace.Candidates(l, null, p, n, null, borderOnly);
                var chestAt = AimPlace.Candidates(l, null, p, n, null, floorOnly);
                Check(hit && n[1] > 0.5 && wall.Count > 0 && wall[0] == EdgeOf(l, f, 0) && chestAt.FirstOrDefault() == f,
                    $"aiming at the floor by a cell's edge: a wall piece goes on that edge ({wall.FirstOrDefault()?.Id}), a chest on the cell ({chestAt.FirstOrDefault()?.Id})");
                // nothing but the hit cell's own edges: the borders the ray passed through on its way aren't offered
                var edges = new[] { 0, 1, 2, 3 }.Select(k => EdgeOf(l, f, k)).ToList();
                var old = l.Pick(eye, d, 6, t, borderOnly, out double tOld);
                Check(wall.All(edges.Contains) && (old == null || !edges.Contains(old) || tOld < t - 0.3),
                    $"no place in mid-air: offered only the hit cell's edges ({wall.Count}); the old search took {old?.Id ?? "none"} {t - tOld:0.00} before the floor");
                // under the deck above: the top-row place under it
                double? ceil = null;
                for (double y = f.Y + 0.1; y < f.Y + 4; y += 0.05) if (g.SolidAt(f.X + 0.5, y, f.Z + 0.15)) { ceil = y; break; }
                if (ceil != null)
                {
                    var eye2 = new Vec3d(f.X + 0.5, f.Y + 1.6, f.Z + 0.5);
                    var d2 = new Vec3d(0, ceil.Value - eye2.Y, -0.35);
                    d2.Mul(1 / d2.Length());
                    if (AimPlace.GridHit(g, eye2, d2, 6, out double t2, out var n2))
                    {
                        var p2 = eye2.AddCopy(d2.X * t2, d2.Y * t2, d2.Z * t2);
                        var top = AimPlace.Candidates(l, null, p2, n2, null, borderOnly).FirstOrDefault();
                        Check(n2[1] < -0.5 && top != null && top.Row >= 1 && p2.Y - 0.05 >= top.Y && p2.Y - 0.05 <= top.Y + 1 && Math.Abs(top.Z - f.Z) < 1e-6,
                            $"aiming at the deck's underside: the top-row place under it ({top?.Id}, row {top?.Row})");
                    }
                }
                // on a partition: its upper part takes the row over it, its end the next place along
                var set = new ShipPieceSet();
                var e0 = EdgeOf(l, f, 0);
                var part = ShipPiece.From(PiecePlanner.Plan(l, e0, planks, e0.In), 1, null);
                set.Add(part);
                Func<MountPoint, bool> freeBorder = x => x.Kind == MountKind.Border && !set.Taken(x.Id);
                var face = new Vec3d(e0.X - 0.0625, e0.Y + 0.9, e0.Z);
                var above = AimPlace.Candidates(l, set, face, new double[] { -1, 0, 0 }, part, freeBorder).FirstOrDefault();
                var endHit = new Vec3d(e0.X - 0.0625, e0.Y + 0.5, e0.Z + 0.45);
                var along = AimPlace.Candidates(l, set, endHit, new double[] { -1, 0, 0 }, part, freeBorder).FirstOrDefault();
                Check(above != null && above == l.BorderNext(e0, 0, 1) && along != null && along == l.BorderNext(e0, 1, 0),
                    $"on a partition: its upper part takes the row over it ({above?.Id}), its end the next place along its line ({along?.Id})");
            }

            // ---- the window (note 163): two by two lights, each leaded two by two
            {
                var b = l.Borders.First(x => x.Clip == null && x.Row == 1);
                var parts = ModularGeometry.Window(b);
                int glass = parts.Count(x => x.Mat == PartMat.Glass), lead = parts.Count(x => x.Mat == PartMat.Lead);
                var boxes = parts.Select(x => x.AsBox()).ToList();
                Check(glass == 4 && lead == 12 && ModularGeometry.Coplanar(boxes).Count == 0,
                    $"a window: four lights in a mullion and transom ({glass} glass), each leaded two by two ({lead} cames: 4 × 4 panes in all), no two faces in one plane");
            }

            // ---- railing corners (note 166): one post for the railings meeting at a corner
            {
                (MountPoint A, MountPoint B, double X, double Z)? corner = null;
                foreach (var a in l.Borders.Where(x => x.Row == 0 && x.Clip == null && x.Deck == "main"))
                {
                    var (la, ax) = MountLayout.BorderLine(a);
                    if (!ax) continue;
                    double ex = a.X + 0.5, ez = la;
                    var bb = l.Borders.FirstOrDefault(x => x.Row == 0 && x.Clip == null && x.Deck == a.Deck && !MountLayout.BorderLine(x).AlongX &&
                                                           Math.Abs(MountLayout.BorderLine(x).Line - ex) < 1e-6 && Math.Abs(x.Z - 0.5 - ez) < 1e-6 && Math.Abs(x.Y - a.Y) < 1e-6);
                    if (bb != null) { corner = (a, bb, ex, ez); break; }
                }
                Check(corner != null, "two railing places meeting at a corner on the main deck");
                if (corner != null)
                {
                    var (ma, mb, cx, cz) = corner.Value;
                    var set = new ShipPieceSet();
                    var ra = ShipPiece.From(PiecePlanner.Plan(l, ma, fence, ma.In), 1, null);
                    var rb = ShipPiece.From(PiecePlanner.Plan(l, mb, fence, mb.In), 2, null);
                    set.Add(ra); set.Add(rb);
                    var endA = ModularGeometry.RailingEnd(l, set.All, ra, cx, cz, out double sa);
                    var endB = ModularGeometry.RailingEnd(l, set.All, rb, cx, cz, out double sb);
                    var (lineA, _) = MountLayout.BorderLine(ma);
                    var (lineB, _) = MountLayout.BorderLine(mb);
                    var now = ModularGeometry.Railing(ma, null, ra.Box, ModularGeometry.EndPost.Half, ModularGeometry.PostHalf, endA, sa)
                        .Concat(ModularGeometry.Railing(mb, null, rb.Box, endB, sb, ModularGeometry.EndPost.Half, ModularGeometry.PostHalf)).Select(x => x.AsBox()).ToList();
                    var before = ModularGeometry.Railing(ma, null, ra.Box).Concat(ModularGeometry.Railing(mb, null, rb.Box)).Select(x => x.AsBox()).ToList();
                    int cNow = ModularGeometry.Coplanar(now).Count, cBefore = ModularGeometry.Coplanar(before).Count;
                    Check(endA == ModularGeometry.EndPost.Shared && endB == ModularGeometry.EndPost.None && cNow == 0 && cBefore > 0,
                        $"two railings at a corner share one post ({endA}, {endB}): no faces in one plane ({cNow}; two half posts and their rails had {cBefore})");
                    // a railing ending at a partition across its end: no post, the rails stop at the partition's face
                    var setP = new ShipPieceSet();
                    setP.Add(ra);
                    setP.Add(ShipPiece.From(PiecePlanner.Plan(l, mb, planks, mb.In), 3, null));
                    var endP = ModularGeometry.RailingEnd(l, setP.All, ra, cx, cz, out double sp);
                    var withWall = ModularGeometry.Railing(ma, null, ra.Box, ModularGeometry.EndPost.Half, ModularGeometry.PostHalf, endP, sp).Select(x => x.AsBox()).ToList();
                    withWall.Add(PiecePlanner.BorderBlock(mb));
                    Check(endP == ModularGeometry.EndPost.None && ModularGeometry.Coplanar(withWall).Count == 0, $"a railing ending at a partition across it: no post, no faces in one plane with it ({ModularGeometry.Coplanar(withWall).Count})");
                }
            }

            // ---- a door put aboard opens 86° (note 164): its open leaf never in a wall's plane beside it
            Check(ModularGeometry.DoorOpenFraction < 1 && Math.Sin((1 - ModularGeometry.DoorOpenFraction) * Math.PI / 2) > 0.05,
                $"an open door stops {90 * (1 - ModularGeometry.DoorOpenFraction):0}° short of square: its far edge {Math.Sin((1 - ModularGeometry.DoorOpenFraction) * Math.PI / 2):0.00} off a wall beside it");

            // ---- the hearth, the oven and the flue as pieces (note 165)
            {
                var hearthB = new FakePiece { Code = "game:claybricks-good-fire", Family = "game:claybricks", Hearth = true };
                var ovenB = new FakePiece { Code = "game:clayoven-north", Family = "game:clayoven", Oven = true, Orient = PieceOrient.Orientable, States = Sides };
                var flueB = new FakePiece { Code = "game:claybrickchimney-four-red-ns", Family = "game:claybrickchimney", Flue = true };
                var flues = l.Flues.ToList();
                Console.WriteLine($"         flue places: {flues.Count} ({string.Join(", ", flues.GroupBy(x => x.Deck + "→" + x.Over).Select(gr => gr.Key + " " + gr.Count()))})");
                // a hearth under the forecastle (main deck), a flue up through the forecastle deck to open air
                var fl = flues.Where(x => x.Room == "forecastle" && x.OutPlace != null).OrderBy(x => Math.Abs(x.Z)).FirstOrDefault()
                         ?? flues.FirstOrDefault(x => x.OutPlace != null);
                Check(fl != null, "a flue place over a floor cell, up through the deck above");
                if (fl != null)
                {
                    var hf = l.Get(fl.Under);
                    var hp = PiecePlanner.Plan(l, hf, hearthB, 2);
                    var op = PiecePlanner.Plan(l, l.Floors.First(x => x.Room == hf.Room && x != hf), ovenB, 2);
                    Check(hp.Error == null && hp.Role == PieceRole.Hearth && hp.Dir == 2 && hp.Mounts.Contains(hf.Id) && hp.Mounts.Count > 1 && hp.Room.Count == 1 &&
                          op.Error == null && op.Role == PieceRole.Oven && op.Parts[0].Block == "game:clayoven-east",
                        $"fire bricks on a floor place are the ship's hearth (facing the placer, the places over it taken, {PiecePlanner.HearthHeadroom} clear over it); a clay oven her oven ({op.Parts.FirstOrDefault()?.Block})");
                    var set = new ShipPieceSet();
                    Check(!set.Supports(l, fl), "a flue place without a hearth under it holds nothing");
                    var hearth = ShipPiece.From(hp, 1, null);
                    set.Add(hearth);
                    var flp = PiecePlanner.Plan(l, fl, flueB, 1);
                    Check(set.Supports(l, fl) && flp.Error == null && flp.Role == PieceRole.Flue && flp.Mounts.Contains(fl.OutPlace) && PiecePlanner.Plan(l, hf, flueB, 1).Error != null,
                        $"over the hearth a brick chimney is its flue ({fl.Id}: from {fl.Box[1]:0.##} up through the {fl.Over} deck to {fl.Box[4]:0.##}); a chimney goes nowhere else");
                    bool before = HearthFlue.FlueTop(l, set, g, hearth, out _);
                    var flue = ShipPiece.From(flp, 2, null);
                    set.Add(flue);
                    bool after = HearthFlue.FlueTop(l, set, g, hearth, out var smoke);
                    Check(!before && after && set.StandingOn(l, hearth)?.Id == 2 && !set.Supports(l, l.Above(l.Get(fl.OutPlace))),
                        $"the hearth draws only with its flue up to open air ({before} → {after}, smoke at y {smoke?[1]:0.00}); the hearth doesn't come up from under it; nothing stands on the flue");
                    // aiming at the hearth with a chimney in hand: its flue's place
                    var aim = AimPlace.Candidates(l, set, new Vec3d(hf.X + 0.5, hf.Y + PiecePlanner.KerbHeight, hf.Z + 0.5), new double[] { 0, 1, 0 }, hearth, x => x.Kind == MountKind.Flue && !set.Taken(x.Id) && set.Supports(l, x));
                    Check(aim.Count == 0 && AimPlace.Candidates(l, new ShipPieceSet(), new Vec3d(hf.X + 0.5, hf.Y + PiecePlanner.KerbHeight, hf.Z + 0.5), new double[] { 0, 1, 0 }, hearth, x => x.Kind == MountKind.Flue).FirstOrDefault() == fl,
                        "aiming at the hearth with a brick chimney in hand: its flue's place");
                    var hb = ModularGeometry.Hearth(hf, 2).Select(x => x.AsBox()).Concat(ModularGeometry.Flue(fl.Box, true).Select(x => x.AsBox())).ToList();
                    Check(ModularGeometry.Coplanar(hb).Count == 0, $"the firebed, its kerb, the flue's hood and chute: no two faces in one plane ({hb.Count} boxes)");
                }
            }
        }

        /// <summary>The border place (row 0) on a floor cell's side dir (0 +x, 1 +z, 2 -x, 3 -z), or null.</summary>
        static MountPoint EdgeOf(MountLayout l, MountPoint f, int dir)
        {
            bool alongX = dir == 1 || dir == 3;
            double line = dir switch { 0 => f.X + 1, 2 => f.X, 1 => f.Z + 1, _ => f.Z };
            double mid = alongX ? f.X + 0.5 : f.Z + 0.5;
            return l.BorderAt(alongX, line, mid, f.Y);
        }

        /// <summary>
        /// (0.3.18, notes 164, 166) A modular design's laid pieces as drawn (their parts; a partition its block on its line,
        /// a shut door its box): faces of two parts in one plane, facing the same way and overlapping, listed by the pieces'
        /// kinds.
        /// </summary>
        static void CoplanarReport(ShipDefinition md, ShipPieceSet set)
        {
            var l = md.Mounts;
            var boxes = new List<double[]>();
            var owner = new List<ShipPiece>();
            foreach (var p in set.All)
            {
                var m = l.Get(p.Mount);
                if (m == null) continue;
                IEnumerable<double[]> bs;
                switch (p.Role)
                {
                    case PieceRole.Partition:
                    {
                        var (lo, hi) = ModularGeometry.WallEnds(l, set.All, p);
                        var bb = PiecePlanner.BorderBlock(m);
                        int ax = MountLayout.BorderLine(m).AlongX ? 0 : 2;
                        bb[ax] -= lo; bb[ax + 3] += hi;
                        bs = m.Clip == null ? new[] { bb } : ModularGeometry.AdjustClip(m, m.Clip, lo, hi).Select(c => PiecePlanner.ClipBox(m, c, MountLayout.PartitionThickness / 2));
                        break;
                    }
                    case PieceRole.Window:
                    {
                        var (lo, hi) = ModularGeometry.WallEnds(l, set.All, p);
                        bs = ModularGeometry.Window(m, lo, hi).Select(x => x.AsBox());
                        break;
                    }
                    case PieceRole.Railing:
                    {
                        var (line, alongX) = MountLayout.BorderLine(m);
                        double a0 = (alongX ? m.X : m.Z) - 0.5, a1 = a0 + 1;
                        if (m.Clip != null && p.Box != null && p.Rake == null) { a0 = alongX ? p.Box[0] : p.Box[2]; a1 = alongX ? p.Box[3] : p.Box[5]; }
                        var e0 = ModularGeometry.RailingEnd(l, set.All, p, alongX ? a0 : line, alongX ? line : a0, out double s0);
                        var e1 = ModularGeometry.RailingEnd(l, set.All, p, alongX ? a1 : line, alongX ? line : a1, out double s1);
                        bs = ModularGeometry.Railing(m, p.Rake, p.Box, e0, s0, e1, s1, ModularGeometry.RailingCap(l, set, p)).Select(x => x.AsBox());
                        break;
                    }
                    case PieceRole.Ladder: bs = ModularGeometry.Ladder(m, p.Dir, p.Rise).Concat(ModularGeometry.LadderGuardParts(m, p.Dir, p.Solids.Skip(PiecePlanner.LadderSegments))).Select(x => x.AsBox()); break;
                    // (0.3.24) as the renderer draws it: its own run (the steep one 2 cells) and its handrails
                    case PieceRole.Stair: bs = ModularGeometry.Flight(m, p.Dir, p.Rise, PiecePlanner.RunOf(p), p.Rails).Select(x => x.AsBox()); break;
                    case PieceRole.Hearth: bs = ModularGeometry.Hearth(m, p.Dir).Select(x => x.AsBox()); break;
                    case PieceRole.Flue: bs = ModularGeometry.Flue(m.Box, set.At(m.Under)?.Role == PieceRole.Hearth).Select(x => x.AsBox()); break;
                    case PieceRole.Oven: bs=Array.Empty<double[]>(); break; // A vanilla mesh, not its full selection cube; checked with the game mesh preview.
                    default: bs = p.Box != null ? new[] { p.Box } : Array.Empty<double[]>(); break;
                }
                foreach (var b in bs) { if (b == null) continue; boxes.Add(b); owner.Add(p); }
            }
            var found = ModularGeometry.Coplanar(boxes).Where(x=> {
                var a=owner[x.A];var b=owner[x.B];
                // Fitted joints are boolean-clipped by ModularMesh; IntegrationNotes exports their actual quads.
                return !ModularGeometry.FittedContact(new PiecePlan {Role=a.Role,Mount=l.Get(a.Mount),Box=a.Box,Dir=a.Dir,Cells=a.Cells},b);
            }).ToList();
            var byKinds = found.GroupBy(x => owner[x.A] == owner[x.B] ? owner[x.A].Role + " (itself)" : string.Join(" / ", new[] { owner[x.A].Role.ToString(), owner[x.B].Role.ToString() }.OrderBy(s => s)))
                .Select(gr => $"{gr.Key} {gr.Count()}").ToList();
            foreach (var x in found.OrderBy(x => owner[x.A].Role == PieceRole.Railing || owner[x.B].Role == PieceRole.Railing ? 0 : 1).Take(8))
                Console.WriteLine($"         in one plane: {owner[x.A].Role} {owner[x.A].Mount} and {owner[x.B].Role} {owner[x.B].Mount} ({x.Area:0.0000} m²; clipped {l.Get(owner[x.A].Mount)?.Clip?.Count ?? 0}/{l.Get(owner[x.B].Mount)?.Clip?.Count ?? 0}; ends {ModularGeometry.WallEnds(l, set.All, owner[x.A])} {ModularGeometry.WallEnds(l, set.All, owner[x.B])}; [{string.Join(",", boxes[x.A].Select(v => v.ToString("0.###")))}] [{string.Join(",", boxes[x.B].Select(v => v.ToString("0.###")))}])");
            static bool Drawn(ShipPiece p) => p.Role is PieceRole.Railing or PieceRole.Window or PieceRole.Ladder or PieceRole.Stair or PieceRole.Hearth or PieceRole.Flue;
            bool railsOk = !found.Any(x => Drawn(owner[x.A]) || Drawn(owner[x.B]));
            Check(railsOk && found.Count == 0, $"{md.Code}: her laid pieces (walls, doors, railings, windows, ladders, hearth and flue): no face in another part's plane ({(byKinds.Count == 0 ? "none at all" : string.Join(", ", byKinds))})");
        }
    }
}
