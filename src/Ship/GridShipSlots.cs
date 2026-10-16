using System;
using System.Collections.Generic;
using System.Linq;
using Hansa.Construction;

namespace Hansa
{
    /// <summary>
    /// A grid ship's blocks as Magelan's slots (docs/legacy/slots.md): her plain hull planking as hull panels, her plain
    /// deck planking as deck cells (her wells and hatchways cells that start out), each with its own collision and its
    /// faces owned and faced as the slot meshes' rule says. Everything else (her bottom, keel, ribs, beams, deck edges,
    /// castle fronts, transom, stem, openings) is her fixed structure, as Magelan's skeleton is. With them she takes shot
    /// damage, holes, leaks and floods, is mended with planks, and in building mode her cells are taken up and laid, by
    /// the code that does it for carrack-m.
    /// </summary>
    public static class GridShipSlots
    {
        public sealed class Result
        {
            public HullSlotDef[] Hull;
            public DeckSlotDef[] Deck;
            /// <summary>Cell → (family 0 hull / 1 deck, index).</summary>
            public Dictionary<CellId, (int Family, int Index)> SlotOf = new();
            /// <summary>Deck cells of hers that start out (wells, hatchways): no block, but a slot.</summary>
            public HashSet<CellId> StartOut = new();
        }

        public static Result Build(ShipConstruction ship, ShipLattice lattice, BlockKinds kinds, double wl,
            FramesDef frames, Func<double, double, double, string> roomAt)
        {
            var r = new Result();
            var design = lattice.Design;
            double keelY = design.Form.Stations.Min(s => s.Section[0][0]) - wl;
            // ---- hull panels: plain planking above her bottom, on her sides, grouped by side, cell along the keel and row
            var hullCells = ship.Present.Where(c => ship.At(c).Kind == "hull" && lattice.SlotKey(c) == Parts.Skin && c.Y >= 1 && !lattice.Across.Contains(c))
                .Where(c => Math.Abs(lattice.Bounds(c).lo.Z + 0.5) >= 1.5)
                .GroupBy(c => (side: lattice.Bounds(c).lo.Z + 0.5 > 0 ? 1 : -1, x: c.X, row: c.Y))
                .OrderBy(g => g.Key.side).ThenBy(g => g.Key.x).ThenBy(g => g.Key.row).ToList();
            var hull = new List<HullSlotDef>();
            foreach (var g in hullCells)
            {
                int side = g.Key.side;
                var faces = g.SelectMany(c => ship.Shape(c).Faces).ToList();
                bool Out(ShipFace f) => f.Normal.Z * side > 0.2 || f.Normal.Y < -0.7;
                bool In(ShipFace f) => f.Normal.Z * side < -0.2 || f.Normal.Y > 0.7;
                var outer = faces.Where(f => Out(f) && !OnBoundary(lattice, g, f)).ToList();
                var inner = faces.Where(f => In(f) && !OnBoundary(lattice, g, f)).ToList();
                if (outer.Count == 0) continue;
                var solids = g.SelectMany(c => ship.Shape(c).Voxels).Select(v => VoxelBox(lattice, v, wl)).ToArray();
                if (solids.Length == 0) continue;
                double area = outer.Sum(f => Convex.Area(f.Points));
                // (only whole panels are slots: a sliver of planking where her stern or a rail ends stays fixed)
                if (area < 0.25) continue;
                var n = outer.Aggregate(new V3(0, 0, 0), (a, f) => a + f.Normal * Convex.Area(f.Points));
                n = n.Length > 1e-9 ? n.Unit : new V3(0, 0, side);
                var outPts = outer.SelectMany(f => f.Points).Select(p => new V3(p.X, p.Y - wl, p.Z)).ToList();
                double fx = frames.X0, sp = frames.Space;
                int station = (int)Math.Floor((g.Key.x + lattice.Origin.X + 0.5 - fx) / sp);
                int half = (int)Math.Floor((g.Key.x + lattice.Origin.X - fx) - station * sp + 1e-9) >= 1 ? 1 : 0;
                var centre = Convex.Mean(outPts);
                var slot = new HullSlotDef
                {
                    Side = side, Station = station, Band = g.Key.row, Half = half == 1 ? "f" : "a",
                    Box = Bounds(solids),
                    Outer = Quad(outPts), Inner = inner.Count > 0 ? Quad(inner.SelectMany(f => f.Points).Select(p => new V3(p.X, p.Y - wl, p.Z)).ToList()) : null,
                    Normal = new[] { n.X, n.Y, n.Z }, Area = area,
                    Sill = outPts.Min(p => p.Y), Head = outPts.Max(p => p.Y),
                    Room = roomAt(centre.X - n.X * 0.8, centre.Y - n.Y * 0.8, centre.Z - n.Z * 0.8),
                    Solids = solids,
                    Material = new SlotMaterialDef { Block = design.Material, Count = g.Count() },
                };
                foreach (var c in g) r.SlotOf[c] = (0, hull.Count);
                hull.Add(slot);
            }
            r.Hull = hull.ToArray();

            // ---- deck cells: whole cells of plain planking on her decks (not her hold's floor: it's her bottom)
            var deckKind = kinds.Get("deck");
            var deck = new List<DeckSlotDef>();
            foreach (var cell in lattice.Cells.Values.Where(c => c.SlotKey == Parts.Deck).OrderBy(c => c.Id))
            {
                var c = cell.Id;
                var d = design.Decks.FirstOrDefault(x => (int)Math.Round(x.Top - lattice.Origin.Y) - 1 == c.Y);
                if (d == null || !d.Beams) continue;
                var state = ship.At(c);
                bool present = state != null && state.Kind is "deck";
                bool opening = state == null || state.Kind == "hatch";
                if (!present && !opening) continue;
                var shape = present ? ship.Shape(c) : BlockShapes.Build(lattice, c, deckKind, new BlockState { Kind = "deck" });
                var solids = shape.Voxels.Select(v => VoxelBox(lattice, v, wl)).ToArray();
                if (solids.Length == 0) continue;
                var (lo, hi) = lattice.Bounds(c);
                double top = d.Top - wl;
                r.SlotOf[c] = (1, deck.Count);
                if (opening) r.StartOut.Add(c);
                deck.Add(new DeckSlotDef
                {
                    // ("deck:i:k" as DeckSlotLayout names a cell from its centre on the block grid: k is a cell's z less one)
                    Id = d.Code + ":" + c.X + ":" + (c.Z - 1),
                    Deck = d.Code,
                    Box = new[] { lo.X, top - d.Thickness, lo.Z, hi.X, top, hi.Z },
                    Solids = solids,
                    Material = new SlotMaterialDef { Block = design.Material, Count = 1 },
                    Start = opening ? "empty" : null,
                });
            }
            r.Deck = deck.ToArray();
            return r;
        }

        static bool OnBoundary(ShipLattice l, IEnumerable<CellId> cells, ShipFace f)
        {
            foreach (var c in cells)
            {
                var (lo, hi) = l.Bounds(c);
                for (int a = 0; a < 3; a++)
                    if (f.Points.All(p => Math.Abs(p[a] - lo[a]) < 1e-7) || f.Points.All(p => Math.Abs(p[a] - hi[a]) < 1e-7)) return true;
            }
            return false;
        }

        static double[] VoxelBox(ShipLattice l, (int, int, int) v, double wl)
        {
            var lo = l.VoxelLo(v);
            return new[] { lo.X, lo.Y - wl, lo.Z, lo.X + l.Voxel, lo.Y + l.Voxel - wl, lo.Z + l.Voxel };
        }

        static double[] Bounds(double[][] boxes) => new[]
        {
            boxes.Min(b => b[0]), boxes.Min(b => b[1]), boxes.Min(b => b[2]), boxes.Max(b => b[3]), boxes.Max(b => b[4]), boxes.Max(b => b[5]),
        };

        /// <summary>A surface's four corners: aft-low, fore-low, fore-high, aft-high.</summary>
        static double[][] Quad(List<V3> pts)
        {
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X);
            var aft = pts.Where(p => p.X < x0 + 1e-6).ToList();
            var fore = pts.Where(p => p.X > x1 - 1e-6).ToList();
            V3 Lo(List<V3> s) => s.OrderBy(p => p.Y).First();
            V3 Hi(List<V3> s) => s.OrderByDescending(p => p.Y).First();
            return new[] { Lo(aft), Lo(fore), Hi(fore), Hi(aft) }.Select(p => new[] { p.X, p.Y, p.Z }).ToArray();
        }
    }
}
