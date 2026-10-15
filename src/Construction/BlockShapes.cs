using System;
using System.Collections.Generic;
using System.Linq;

namespace Hansa.Construction
{
    /// <summary>A block's drawable faces and collision, computed from its own cell's location and
    /// its own state only. Neighbours never change it; they only hide faces they cover.</summary>
    public sealed class BlockShape
    {
        public readonly List<ShipFace> Faces = new();
        public readonly List<(int, int, int)> Voxels = new();
        public readonly HashSet<string> FaceKeys = new();
    }

    public static class BlockShapes
    {
        static V3 Grain(string part, string facing) => part switch
        {
            Parts.Rib => V3.Axis(1),
            Parts.Beam => V3.Axis(2),
            _ => V3.Axis(0),
        };

        public static BlockShape Build(ShipLattice lattice, CellId id, BlockKind kind, BlockState state)
        {
            var solids = new List<(string part, Convex solid, string material, bool collides)>();
            lattice.Cells.TryGetValue(id, out var cell);
            var (lo, hi) = lattice.Bounds(id);
            string wood = state.Material ?? lattice.Design.Material;
            switch (kind.Generator)
            {
                case "slot":
                    foreach (var p in cell.Pieces) solids.Add((p.Part, p.Solid, wood, true));
                    break;
                case "opening":
                    Opening(lattice, cell, kind, state, solids);
                    break;
                default: throw new InvalidOperationException($"Unknown block generator {kind.Generator}");
            }

            var shape = new BlockShape();
            // Faces shared by two of this block's own solids are internal; both copies go.
            var seen = new Dictionary<string, int>();
            var faces = new List<(V3[] pts, V3 n, string part, string material, string key)>();
            foreach (var (part, solid, material, _) in solids)
                foreach (var f in solid.Faces)
                {
                    var key = Convex.FaceKey(f);
                    seen[key] = seen.TryGetValue(key, out int c) ? c + 1 : 1;
                    faces.Add((f, Convex.Normal(f).Value, part, material, key));
                }
            foreach (var f in faces)
            {
                if (seen[f.key] > 1) continue;
                shape.Faces.Add(new ShipFace { Points = f.pts, Normal = f.n, Part = f.part, Material = f.material,
                    Uv = Uvs.Metric(f.pts, f.n, Grain(f.part, state.Facing)) });
                shape.FaceKeys.Add(f.key);
            }
            var voxels = new HashSet<(int, int, int)>();
            foreach (var (_, solid, _, collides) in solids)
                if (collides)
                    foreach (var v in lattice.SampleVoxels(solid))
                        if (lattice.MayStamp(id, v)) voxels.Add(v);
            shape.Voxels.AddRange(voxels.OrderBy(v => v));
            return shape;
        }

        /// <summary>A window, port, gate or hatch: the cell's skin or deck split into a frame and a
        /// panel. The hole spans the two axes along the surface, inset by the margin.</summary>
        static void Opening(ShipLattice lattice, LatticeCell cell, BlockKind kind, BlockState state,
            List<(string, Convex, string, bool)> solids)
        {
            var (lo, hi) = lattice.Bounds(cell.Id);
            var weight = new double[3];
            foreach (var p in cell.Pieces)
                foreach (var f in p.Solid.Faces)
                {
                    if (Enumerable.Range(0, 3).Any(a => f.All(q => Math.Abs(q[a] - lo[a]) < 1e-7 || Math.Abs(q[a] - hi[a]) < 1e-7) &&
                        (f.All(q => Math.Abs(q[a] - lo[a]) < 1e-7) || f.All(q => Math.Abs(q[a] - hi[a]) < 1e-7)))) continue;
                    var n = Convex.Normal(f).Value; double area = Convex.Area(f);
                    for (int a = 0; a < 3; a++) weight[a] += Math.Abs(n[a]) * area;
                }
            int normalAxis = Array.IndexOf(weight, weight.Max());
            double m = kind.Opening.Margin;
            var hole = (lo: lo, hi: hi);
            for (int a = 0; a < 3; a++)
                if (a != normalAxis) { hole.lo = hole.lo.With(a, lo[a] + m); hole.hi = hole.hi.With(a, hi[a] - m); }
            int u = (normalAxis + 1) % 3, v = (normalAxis + 2) % 3;
            string wood = state.Material ?? lattice.Design.Material;
            bool glass = kind.Opening.Panel == "glass";
            string panelMaterial = glass ? lattice.Design.Glass : wood;
            bool panelPresent = glass || !state.Open;
            foreach (var p in cell.Pieces)
            {
                // Frame: everything outside the hole, as four convex strips.
                Add(p.Solid.Clip(u, hole.lo[u], 1), p.Part, wood, solids);
                Add(p.Solid.Clip(u, hole.hi[u], -1), p.Part, wood, solids);
                var middle = p.Solid.Clip(u, hole.lo[u], -1)?.Clip(u, hole.hi[u], 1);
                Add(middle?.Clip(v, hole.lo[v], 1), p.Part, wood, solids);
                Add(middle?.Clip(v, hole.hi[v], -1), p.Part, wood, solids);
                if (panelPresent)
                {
                    var panel = middle?.Clip(v, hole.lo[v], -1)?.Clip(v, hole.hi[v], 1);
                    if (panel != null) solids.Add((glass ? "glass" : p.Part, panel, panelMaterial, true));
                }
            }
        }

        static void Add(Convex c, string part, string material, List<(string, Convex, string, bool)> solids)
        {
            if (c != null) solids.Add((part, c, material, true));
        }
    }
}
