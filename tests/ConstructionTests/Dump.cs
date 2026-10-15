using System;
using System.Linq;
using Hansa.Construction;

static class Dump
{
    public static void Slots(ShipLattice l, int x)
    {
        Console.WriteLine($"slots at x={x} (y down from top):");
        var ys = l.Cells.Keys.Select(c => c.Y).Distinct().OrderByDescending(y => y);
        int zmin = l.Cells.Keys.Min(c => c.Z), zmax = l.Cells.Keys.Max(c => c.Z);
        foreach (int y in ys)
        {
            Console.Write($"{y,3} ");
            for (int z = zmin; z <= zmax; z++)
            {
                var c = new CellId(x, y, z);
                string s = l.Cells.TryGetValue(c, out var cell) ? cell.SlotKey.Replace("skin", "S").Replace("rib", "R").Replace("deck", "D").Replace("beam", "B").Replace("keel", "K").Replace("+", "") : (l.Interior(c) ? "." : " ");
                Console.Write(s.PadRight(5));
            }
            Console.WriteLine();
        }
    }

    /// <summary>Visible faces as JSON for the depth-buffered preview renderer.</summary>
    public static void Faces(ShipConstruction ship, string path)
    {
        var faces = ship.Present.OrderBy(c => c).SelectMany(c => ship.VisibleFaces(c).Select(f => new
        {
            cell = c.ToString(), kind = ship.At(c).Kind, part = f.Part,
            p = f.Points.Select(q => new[] { q.X, q.Y, q.Z }), n = new[] { f.Normal.X, f.Normal.Y, f.Normal.Z },
        }));
        var fittings = ship.WallFaces.OrderBy(f => f).SelectMany(f => ship.WallShape(f).Faces.Select(x => (cell: f.ToString(), kind: ship.WallAt(f).Kind, x)))
            .Concat(ship.Stairs.SelectMany(s => ship.StairShape(s).Faces.Select(x => (cell: s.FootCell.ToString(), kind: "stair", x))))
            .Select(t => new { t.cell, t.kind, part = t.x.Part, p = t.x.Points.Select(q => new[] { q.X, q.Y, q.Z }), n = new[] { t.x.Normal.X, t.x.Normal.Y, t.x.Normal.Z } });
        System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(faces.Concat(fittings)));
    }
}
