using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa.Construction;
using Newtonsoft.Json.Linq;

static class Program
{
    static int checks, failures;
    static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) failures++;
        Console.WriteLine((condition ? "ok:   " : "FAIL: ") + description);
    }
    static void Rejects(Action action, string description)
    {
        try { action(); } catch (InvalidOperationException e) { Check(true, $"{description} ({e.Message})"); return; }
        Check(false, description);
    }

    static string root;
    static string Asset(string path) => File.ReadAllText(Path.Combine(root, "assets/hansa/config", path));
    static BlockKinds kinds;
    static ShipConstruction Fresh(string designJson = null) =>
        new(new ShipLattice(ShipDesign.Read(designJson ?? Asset("shipdesigns/two-bay.json"))), kinds);

    static int Main(string[] args)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Hansa.csproj"))) dir = dir.Parent;
        root = dir.FullName;
        kinds = BlockKinds.Read(Asset("construction/kinds.json"));
        var json = Asset("shipdesigns/two-bay.json");
        var ship = Fresh();
        var lattice = ship.Lattice;
        if (args.Contains("--dump")) { Dump.Slots(lattice, 2); Dump.Slots(lattice, 4); }
        int export = Array.IndexOf(args, "--export");
        if (export >= 0) { Dump.Faces(ship, args[export + 1]); return 0; }

        DefaultShip(ship);
        FormRules(json);
        Geometry(ship);
        Watertight(ship);
        Locality(json);
        Structure();
        Openings();
        Stairs();
        Walls();
        Saves(json);

        int faces = ship.Present.Sum(c => ship.VisibleFaces(c).Count());
        int triangles = ship.Present.Sum(c => ship.VisibleFaces(c).Sum(f => f.Points.Length - 2));
        Console.WriteLine($"slice: {lattice.Cells.Count} structural cells, {ship.Present.Count()} blocks, {faces} visible faces, {triangles} triangles, {ship.Collision().Count()} collision voxels");
        Console.WriteLine($"{checks - failures}/{checks} checks passed");
        return failures == 0 ? 0 : 1;
    }

    static void DefaultShip(ShipConstruction ship)
    {
        var l = ship.Lattice;
        Check(l.Problems.Count == 0, "the two-bay design compiles: " + string.Join("; ", l.Problems));
        var problems = ship.Problems();
        Check(problems.Count == 0, "the default ship obeys its own rules" + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(5)) : ""));
        Check(!ship.MissingRequired().Any(), "the complete default ship shows no grey guides");
        Check(ship.At(new CellId(6, 7, 0))?.Kind == "hatch", "the layout's hatch is a block in its cell");
        Check(ship.At(new CellId(4, 7, 0))?.Kind == "beam", "a deck cell in a rib column is a beam block");
        Check(ship.At(new CellId(4, 9, 6))?.Kind == "rib" && ship.At(new CellId(2, 9, 6))?.Kind == "hull", "rib cells hold the rib, the cells between hold planking");
        Check(new[] { 0, 4, 8 }.All(x => l.Cells.Values.Any(c => c.Id.X == x && c.Lead == Parts.Rib)) &&
              !l.Cells.Values.Any(c => c.Lead == Parts.Rib && c.Id.X is not (0 or 4 or 8)), "rib timber stays in its own one-cell column");
    }

    static void FormRules(string json)
    {
        ShipLattice With(Action<JObject> edit)
        {
            var o = JObject.Parse(json); edit(o);
            return new ShipLattice(ShipDesign.Read(o.ToString()));
        }
        var off = With(o => o["form"]["stations"][0]["section"][2] = new JArray(1.5, 4.2));
        Check(off.Problems.Any(p => p.Contains("corner grid")), "a corner off the half-block grid is rejected");
        var slope = With(o => { o["form"]["stations"][0]["section"][2] = new JArray(1.5, 4.5); o["form"]["stations"][1]["section"][2] = new JArray(1.5, 4.5); });
        Check(slope.Problems.Any(p => p.Contains("slope")), "a slope outside the slope set is rejected");
        var ribs = With(o => o["frames"]["cells"] = new JArray(0, 3, 8));
        Check(ribs.Problems.Any(p => p.Contains("clear cells")), "ribs with two clear cells between them are rejected");
        var dense = With(o => { o["frames"]["cells"] = new JArray(0, 2, 4, 8); });
        Check(dense.Problems.Count == 0, "1 and 3 clear cells may be mixed");
        var spacing = With(o => { o["frames"]["cells"] = new JArray(0, 3, 8); o["frames"]["clearGaps"] = new JArray(2, 4); });
        Check(spacing.Problems.Count == 0, "rib spacing is design data, not code");
    }

    static void Geometry(ShipConstruction ship)
    {
        var l = ship.Lattice;
        double planar = 0, stretch = 0, overhang = 0;
        var owners = new Dictionary<(int, int, int), CellId>();
        bool exclusive = true;
        foreach (var c in ship.Present)
        {
            var shape = ship.Shape(c);
            var (lo, hi) = l.Bounds(c);
            foreach (var f in shape.Faces)
            {
                for (int i = 0; i < f.Points.Length; i++)
                {
                    planar = Math.Max(planar, Math.Abs(f.Normal.Dot(f.Points[i] - f.Points[0])));
                    int k = (i + 1) % f.Points.Length;
                    double physical = (f.Points[k] - f.Points[i]).Length;
                    double du = f.Uv[k][0] - f.Uv[i][0], dv = f.Uv[k][1] - f.Uv[i][1];
                    stretch = Math.Max(stretch, Math.Abs(physical - Math.Sqrt(du * du + dv * dv) / 16));
                    for (int a = 0; a < 3; a++)
                        overhang = Math.Max(overhang, Math.Max(lo[a] - f.Points[i][a], f.Points[i][a] - hi[a]));
                }
            }
            foreach (var v in shape.Voxels)
            {
                if (owners.TryGetValue(v, out var other) && other != c) exclusive = false;
                owners[v] = c;
            }
        }
        Check(planar < 1e-6, $"every face is planar (max error {planar:E1})");
        Check(stretch < 1e-5, $"UV distance equals surface distance at 16 texels per block (max error {stretch:E1})");
        Check(exclusive, "every collision voxel has exactly one owning block");
        Check(overhang <= ship.Lattice.Design.Form.Skin + ship.Lattice.Design.Frames.Depth + 1e-6,
            $"geometry stays in its cell, except merged corner pieces (max overhang {overhang:0.###} block)");
        Check(ship.Present.All(c => ship.Shape(c).Voxels.Count > 0), "every block has collision");
        var again = new ShipLattice(ship.Lattice.Design);
        Check(again.Cells.Count == l.Cells.Count && again.Cells.Values.All(c =>
            l.Cells[c.Id].SlotKey == c.SlotKey &&
            string.Join("/", c.Pieces.SelectMany(p => p.Solid.Faces).Select(Convex.FaceKey)) ==
            string.Join("/", l.Cells[c.Id].Pieces.SelectMany(p => p.Solid.Faces).Select(Convex.FaceKey))), "compilation is deterministic");
        Check(l.Cells.Values.All(c => c.Volume >= l.Design.MinBlockVolume), "no structural block is a sliver");
    }

    /// <summary>Flood the space outside the hull in cross-sections through a rib and a bay, below
    /// the waterline. It must not reach the hold.</summary>
    static void Watertight(ShipConstruction ship)
    {
        var l = ship.Lattice;
        var solid = ship.Collision().ToHashSet();
        foreach (double x in new[] { 2.5, 4.5, 0.6 })
        {
            int vx = l.VoxelAt(new V3(x, 0, 0)).Item1;
            int yMax = (int)Math.Floor(l.Design.Waterline / l.Voxel);
            var start = l.VoxelAt(new V3(x, 0.1, 7.5));
            var seen = new HashSet<(int, int)> { (start.Item2, start.Item3) };
            var queue = new Queue<(int, int)>(seen);
            int zLimit = l.VoxelAt(new V3(0, 0, 8)).Item3, zLow = l.VoxelAt(new V3(0, 0, -8)).Item3;
            while (queue.Count > 0)
            {
                var (y, z) = queue.Dequeue();
                foreach (var (dy, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (y + dy, z + dz);
                    if (n.Item1 < -2 || n.Item1 > yMax || n.Item2 < zLow || n.Item2 > zLimit || seen.Contains(n) || solid.Contains((vx, n.Item1, n.Item2))) continue;
                    seen.Add(n); queue.Enqueue(n);
                }
            }
            var hold = l.VoxelAt(new V3(x, 2.6, 0));
            Check(!seen.Contains((hold.Item2, hold.Item3)), $"collision keeps the sea out of the hold in the section at x={x}");
        }
    }

    static Dictionary<CellId, HashSet<string>> Visible(ShipConstruction s) =>
        s.Present.ToDictionary(c => c, c => s.VisibleFaces(c).Select(f => Convex.FaceKey(f.Points)).ToHashSet());

    static void Locality(string json)
    {
        var ship = Fresh();
        var cell = new CellId(2, 9, 6);
        var neighbours = CellId.Sides.Select(d => cell + d).ToHashSet();
        var shapesBefore = ship.Present.Where(c => c != cell).ToDictionary(c => c, c => ship.Shape(c));
        var before = Visible(ship);
        ship.Remove(cell);
        ship.Place(cell, "window", "game:glasspane-plain-ew");
        var after = Visible(ship);
        Check(ship.Present.Where(c => c != cell).All(c => ReferenceEquals(ship.Shape(c), shapesBefore[c])),
            "replacing planks with a window recomputes no other block");
        var changed = after.Keys.Where(c => !before.TryGetValue(c, out var b) || !b.SetEquals(after[c])).ToList();
        Check(changed.All(c => c == cell || neighbours.Contains(c)), $"only the cell and its direct neighbours' shared faces change ({changed.Count} cells)");
        var (lo, hi) = ship.Lattice.Bounds(cell);
        bool onShared = changed.Where(c => c != cell).All(c => before[c].Except(after[c]).Concat(after[c].Except(before[c])).All(key =>
            key.Split(';').Select(p => p.Split('|').Select(double.Parse).ToArray()).All(p =>
                Enumerable.Range(0, 3).All(a => p[a] >= lo[a] - 1e-6 && p[a] <= hi[a] + 1e-6))));
        Check(onShared, "neighbours only gain or lose faces lying on the changed cell's boundary");

        // The same move as a design-data edit: one layout line, one changed default block.
        var o = JObject.Parse(json);
        ((JArray)o["layout"]).Add(JObject.Parse("{ \"set\": \"window\", \"at\": [[2, 9, 6]] }"));
        var edited = Fresh(o.ToString());
        var original = Fresh();
        var diff = original.Lattice.Cells.Keys.Where(c => original.DesignAt(c)?.Kind != edited.DesignAt(c)?.Kind).ToList();
        Check(diff.Count == 1 && diff[0] == cell && edited.Problems().Count == 0, "a window added in the design data changes only that cell's default");
        Check(edited.Lattice.Cells.Values.All(c => original.Lattice.Cells[c.Id].SlotKey == c.SlotKey), "layout edits never change the compiled structure");
    }

    static void Structure()
    {
        var ship = Fresh();
        var rib = new CellId(4, 9, 6);
        Rejects(() => ship.Remove(new CellId(4, 0, 0)), "removing the keel under a rib column is refused while that rib carries planking");
        Check(ship.RemoveProblem(new CellId(2, 0, 0)) == null, "keel between ribs strands nothing, so only its grey guide would report it");
        ship.Damage(rib, 200);
        Check(ship.At(rib).Cracked && ship.Solid(rib) && ship.Shape(rib).Voxels.Count > 0, "a cracked rib keeps its shape and collision");
        ship.Damage(rib, 1000);
        Check(!ship.Solid(rib), "a destroyed rib leaves its cell empty");
        Check(ship.Solid(rib - new CellId(1, 0, 0)) && ship.Solid(rib + new CellId(1, 0, 0)), "destroying a rib leaves the neighbouring planking in place");
        var guides = ship.MissingRequired().ToList();
        Check(guides.Count == 1 && guides[0].cell == rib && guides[0].guide.Kind == "rib", "the missing rib shows one grey guide, in its own shape");
        Rejects(() => ship.Repair(rib, "game:glasspane-plain-ew"), "a rib is not repaired with glass");
        ship.Repair(rib, "game:planks-pine-ud");
        Check(ship.At(rib)?.Kind == "rib" && ship.At(rib).Material == "game:planks-pine-ud" && !ship.MissingRequired().Any(),
            "ordinary planks rebuild the rib and clear the guide");

        var deck = new CellId(2, 4, 3);
        ship.Remove(deck);
        Check(!ship.MissingRequired().Any(), "removing an optional deck block produces no guide");
        var hull = new CellId(2, 2, 5);
        ship.Damage(hull, 10000);
        Check(ship.MissingRequired().Any(g => g.cell == hull && g.guide.Kind == "hull"), "lost planking below the waterline is required again");
        Check(!ship.Seals(hull), "a holed hull cell no longer seals");
    }

    static void Openings()
    {
        var ship = Fresh();
        var port = new CellId(6, 6, 6);
        ship.Remove(port);
        Rejects(() => ship.Place(port, "window", "game:planks-oak-ud"), "a window needs glass");
        Rejects(() => ship.Place(new CellId(4, 6, 6), "gunport", "game:planks-oak-ud"), "an opening does not fit a rib cell");
        ship.Place(port, "gunport", "game:planks-oak-ud");
        int closed = ship.Shape(port).Voxels.Count;
        Check(ship.Seals(port), "a shut gun port seals");
        ship.Toggle(port);
        Check(!ship.Seals(port) && ship.Shape(port).Voxels.Count < closed, "an open gun port lets water and players through its hole");
        Check(ship.Shape(port).Faces.Any(f => f.Part == Parts.Skin), "the port's frame keeps the hull planking round the hole");
        var hatch = new CellId(6, 7, 0);
        int shut = ship.Shape(hatch).Voxels.Count;
        ship.Toggle(hatch);
        Check(ship.Shape(hatch).Voxels.Count < shut, "an open hatch is passable");
        Rejects(() => ship.Toggle(new CellId(2, 9, 6)), "plain planking has nothing to open");
    }

    static void Stairs()
    {
        var ship = Fresh();
        CellId S(int x, int y, int z) => new(x, y, z);
        var stair = ship.Stairs.Single();
        var cells = FittingShapes.Occupied(ship.Lattice, stair).ToList();
        Check(cells.Select(c => (c.X, c.Z)).Distinct().Count() == 2, "the ladder-stair takes two cells in plan");
        Check(!ship.Solid(S(1, 7, -2)) && !ship.Solid(S(2, 7, -2)) && ship.Solid(S(0, 7, -2)) && ship.Solid(S(3, 7, -2)),
            "its well is the 2 x 1 opening over those cells, nothing more");
        Check(!cells.Contains(S(2, 5, -2)) && ship.StairAt(S(2, 5, -2)) == null, "the floor under the stair's high end stays free");
        var ladder = ship.Ladder(stair);
        Check(Math.Abs(ladder.Top.Y - ladder.Bottom.Y - 3) < 1e-9 && Math.Abs((ladder.Top - ladder.Bottom).X - 1.5) < 1e-9,
            "it climbs 3 blocks over a run of 1.5, feet mid-cell, hooked at the head cell's far edge");
        var shape = ship.StairShape(stair);
        var under = shape.Faces.Where(f => f.Part == "stringer" && f.Normal.Y < -0.1).ToList();
        Check(under.Count == 2 && under.All(f => Math.Abs(f.Normal.Dot(under[0].Normal) - 1) < 1e-9),
            "each stringer's underside is one flat face along one straight line");
        Check(shape.Faces.Count(f => f.Part == "tread" && f.Normal.Y > 0.9) >= 8, "flat treads about 0.3 apart");
        Check(ship.PlaceProblem(S(1, 6, -2), "deck", "game:planks-oak-ud") != null, "nothing is built into a cell the stair stands in");

        Check(ship.StairProblem(new StairFitting { Foot = new[] { 5, 5, -2 }, Dir = "+x" })?.Contains("Open the deck") == true,
            "a stair needs its well opened first");
        Check(ship.StairProblem(new StairFitting { Foot = new[] { 3, 5, -2 }, Dir = "+x" }) != null, "a stair cannot rise through a beam");

        // Move it to the second bay, as a player would: take it up, deck over, cut a new well, set it.
        ship.RemoveStair(S(2, 6, -2));
        ship.Place(S(1, 7, -2), "deck", "game:planks-oak-ud");
        ship.Place(S(2, 7, -2), "deck", "game:planks-oak-ud");
        Check(ship.Problems().Count == 0, "the old well is decked over again");
        ship.Remove(S(5, 7, -3)); ship.Remove(S(6, 7, -3));
        ship.PlaceStair(S(5, 5, -3), "+x", "game:planks-oak-ud");
        var problems = ship.Problems();
        Check(problems.Count == 0, "the moved stair has its landings and well" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
        Rejects(() => ship.PlaceWall(FaceId.Of(S(5, 5, -3), "+x"), "wall", "game:planks-oak-ud"), "a wall across the stair is refused");
        Rejects(() => ship.PlaceWall(FaceId.Of(S(4, 5, -3), "+x"), "wall", "game:planks-oak-ud"), "a wall across the stair's foot is refused");
        ship.PlaceWall(FaceId.Of(S(5, 5, -3), "+z"), "wall", "game:planks-oak-ud");
        Check(ship.Problems().Count == 0, "a wall beside the stair is fine");
    }

    static void Walls()
    {
        var ship = Fresh();
        CellId S(int x, int y, int z) => new(x, y, z);
        var door = FaceId.Of(S(6, 5, 2), "+z");
        Check(ship.WallAt(door)?.Kind == "door" && ship.WallAt(door.Up)?.Kind == "door" && ship.WallAt(door.Up.Up)?.Kind == "wall",
            "the cabin door covers two rows, with wall above it up to the deck");
        Check(ship.WallSeals(FaceId.Of(S(5, 6, 2), "+z")) && ship.WallSeals(door), "walls and a shut door close the cabin");
        int shut = ship.WallShape(door).Voxels.Count;
        ship.ToggleDoor(door.Up);
        Check(!ship.WallSeals(door) && ship.WallShape(door).Faces.Any(f => f.Part == "door"), "an open door swings aside, still drawn");
        Check(ship.WallShape(door).Voxels.Count != shut, "an open door leaves its doorway passable");
        var top = ship.WallShape(FaceId.Of(S(5, 7, 4), "-x")).Faces.Max(f => f.Points.Max(q => q.Y));
        Check(Math.Abs(top - 7.75) < 1e-9, $"a wall under a deck stops at the deck planking (top {top})");
        var nearBeam = ship.WallShape(FaceId.Of(S(5, 7, 3), "-x"));
        Check(nearBeam.Faces.Max(f => f.Points.Max(q => q.Y)) > 7.7, "a beam in the next rib cell does not cut the wall short");
        var hullEnd = FaceId.Of(S(5, 5, 6), "-x");
        var inner = HullForm.Offset(ship.Lattice.Form.SectionAt(5), ship.Lattice.Design.Form.Skin);
        double reach = ship.WallShape(hullEnd).Faces.Max(f => f.Points.Max(q => q.Z));
        Check(Math.Abs(reach - Enumerable.Range(0, 101).Max(i => HullForm.HalfWidth(inner, 5 + i / 100.0))) < 1e-6, $"a wall's end meets the hull's inner surface (reach {reach:0.###})");

        var before = ship.Present.ToDictionary(c => c, c => ship.Shape(c));
        var face = FaceId.Of(S(2, 5, 1), "+x");
        ship.PlaceWall(face, "wall", "game:planks-oak-ud");
        Check(ship.Present.All(c => ReferenceEquals(ship.Shape(c), before[c])), "placing a wall recomputes no block");
        Check(!ship.Solid(S(2, 5, 1)) && !ship.Solid(S(3, 5, 1)), "a wall on a face leaves both cells free");
        Rejects(() => ship.PlaceWall(face, "wall", "game:planks-oak-ud"), "one wall per face");
        Rejects(() => ship.PlaceWall(FaceId.Of(S(2, 1, 0), "+x"), "wall", "game:planks-oak-ud"), "no wall inside the bottom planking");
        Rejects(() => ship.PlaceWall(FaceId.Of(S(2, 3, 0), "+x"), "wall", "game:glasspane-plain-ew"), "walls are planks");
        ship.RemoveWall(face);
        Check(ship.WallAt(face) == null, "a wall comes down on its own");
        var hold = FaceId.Of(S(2, 2, 0), "+x");
        ship.PlaceWall(hold, "wall", "game:planks-oak-ud");
        Check(ship.Problems().Count == 0, "a wall stands in the hold on its floor");
    }

    static void Saves(string json)
    {
        var ship = Fresh();
        var rib = new CellId(8, 6, -6);
        ship.Damage(rib, 1000);
        ship.Damage(new CellId(2, 2, 5), 50);
        ship.Toggle(new CellId(6, 7, 0));
        ship.Remove(new CellId(2, 9, 6));
        ship.Place(new CellId(2, 9, 6), "window", "game:glasspane-plain-ew");
        ship.ToggleDoor(FaceId.Of(new CellId(6, 5, 2), "+z"));
        ship.RemoveWall(FaceId.Of(new CellId(7, 6, 2), "+z"));
        ship.PlaceWall(FaceId.Of(new CellId(2, 2, 1), "+x"), "wall", "game:planks-pine-ud");
        var save = ship.Serialize();
        var loaded = Fresh();
        loaded.Load(save);
        Check(ship.Present.OrderBy(c => c).SequenceEqual(loaded.Present.OrderBy(c => c)) &&
              ship.Present.All(c => ship.At(c).Same(loaded.At(c))), "a save restores every block");
        Check(ship.Collision().ToHashSet().SetEquals(loaded.Collision()), "a loaded ship collides identically");
        Check(ship.WallFaces.OrderBy(f => f).SequenceEqual(loaded.WallFaces.OrderBy(f => f)) &&
              ship.WallFaces.All(f => ship.WallAt(f).Same(loaded.WallAt(f))), "a save restores walls and doors");
        Check(JObject.Parse(save)["Walls"].Count() == 3 && JObject.Parse(save)["Stairs"].Type == JTokenType.Null, "only changed walls are saved; an unmoved stair is not");
        Check(JObject.Parse(save)["Changes"].Count() == 4, "only changed cells are saved");
        Check(loaded.MissingRequired().Single().guide.Kind == "rib", "a destroyed block survives the save as a guide");

        var o = JObject.Parse(json);
        o["frames"]["visibleWidth"] = 1;
        var wide = Fresh(o.ToString());
        wide.Load(save);
        Check(wide.At(new CellId(2, 9, 6))?.Kind == "window" && !wide.Solid(rib), "the save still loads after the visible rib width changes");
    }
}
