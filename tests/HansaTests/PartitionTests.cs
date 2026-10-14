using System;
using System.Linq;
using Hansa;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.3.13, note 137: "cannot still fill till ceiling timber partition walls, also cannot do it to curved hull") The
        /// border places go up under her beams and deck (a top row cut to what's free) and out along a partition line to her
        /// curved side (the wedge cells, cut likewise); a partition planned there is solid only where she isn't, and the
        /// planks reach her: the top row's to within a voxel of her deck, the wedge's to her side.
        /// </summary>
        static void PartitionsToHer(ShipDefinition def)
        {
            Console.WriteLine("partitions up to her beams and out to her side");
            var l = def.Mounts;
            var g = def.Grid;
            var borders = l.Borders.ToList();
            var cut = borders.Where(b => b.Clip != null).ToList();
            var lowerTop = cut.Where(b => b.Room == "lowerdeck" && b.Row == 2).ToList();
            Check(lowerTop.Count > 20, $"the lower deck's borders have a top row under her beams and deck ({lowerTop.Count} places, {cut.Count} cut ones in all, {borders.Count} borders)");
            // near where the user tried (10.33, 1.0, -0.5): a top-row place reaching up to her deck or a beam
            var near = lowerTop.OrderBy(b => Math.Abs(b.X - 10.33) + Math.Abs(b.Z + 0.5)).FirstOrDefault();
            if (near != null)
            {
                double topY = near.Clip.Max(c => c[3]);
                var (line, alongX) = MountLayout.BorderLine(near);
                // (just over the cut's top, in its layer: her)
                var c0 = near.Clip.OrderByDescending(c => c[3]).First();
                double al = (c0[0] + c0[2]) / 2, layer = line + (near.In < 2 ? 1 : -1) * g.V / 2;
                bool herAbove = alongX ? g.SolidAt(al, topY + 0.1, layer) : g.SolidAt(layer, topY + 0.1, al);
                Check(herAbove && topY > near.Y + 0.2, $"by the user's spot ({near.Id}): the top row's partition goes up to {topY:0.00}, and her timbers are right over it");
            }
            // out to her side: cut places in cells that aren't floor places, against her
            var wedges = cut.Where(b => l.FloorAt(b.Deck, b.I, b.K) == null).ToList();
            Check(wedges.Count > 10, $"along partition lines past the last floor cell to her curved side ({wedges.Count} places)");
            // every cut place's partition: solid only where she isn't, drawn within the place's block
            int clash = 0, outside = 0;
            var planks = new FakePiece { Code = "game:planks-oak-hor", Family = "game:planks-oak", Planks = true };
            foreach (var b in cut)
            {
                var plan = PiecePlanner.Plan(l, b, planks, b.In);
                if (plan.Error != null) { clash++; continue; }
                foreach (var s in plan.Solids)
                    if (g.Collides(s[0] + 0.03, s[1] + 0.03, s[2] + 0.03, s[3] - 0.03, s[4] - 0.03, s[5] - 0.03)) { clash++; break; }
                var whole = PiecePlanner.BorderBlock(b);
                if (plan.Box[1] < whole[1] - 1e-6 || plan.Box[4] > whole[4] + 1e-6) outside++;
            }
            Check(clash == 0 && outside == 0, $"partitions on them are solid only where she isn't ({clash} clash) and within their blocks ({outside} not)");
            // the runs: a column free at the foot and the top, shut between, is two runs; columns alike are joined
            var free = new bool[4, 4];
            for (int a = 0; a < 4; a++) { free[a, 0] = true; free[a, 3] = a < 2; free[a, 1] = true; }
            var runs = MountLayout.ClipRuns(free, 4, 10, 0, 0.25);
            Check(runs.Count == 3 && runs.Any(r => r[0] == 10 && r[2] == 10.5 && r[1] == 0 && r[3] == 0.5) && runs.Any(r => r[0] == 10 && r[2] == 10.5 && r[1] == 0.75 && r[3] == 1.0) &&
                  runs.Any(r => r[0] == 10.5 && r[2] == 11 && r[1] == 0 && r[3] == 0.5),
                $"the free parts as rectangles: {string.Join(" ", runs.Select(r => $"[{r[0]}..{r[2]} × {r[1]}..{r[3]}]"))}");
        }
    }
}
