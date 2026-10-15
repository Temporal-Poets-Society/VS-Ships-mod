using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Hansa.Grid
{
    /// <summary>Compiled grid ownership. Geometry is prescribed by the blueprint;
    /// the player's inventory contains ordinary materials, not shape variants.</summary>
    public sealed class GridBlueprint
    {
        public int Schema;
        public string Code;
        public GridSpec Spec;
        public GridModule[] Cells = Array.Empty<GridModule>();
        [JsonIgnore] public Dictionary<string, GridModule> ById;

        public static GridBlueprint Read(string json)
        {
            var b = JsonConvert.DeserializeObject<GridBlueprint>(json);
            if (b == null || b.Schema != 1 || b.Spec?.Grid?.CellSize != 1)
                throw new ArgumentException("Unsupported grid blueprint");
            b.ById = new Dictionary<string, GridModule>();
            foreach (var cell in b.Cells)
            {
                if (cell.Cell?.Length != 3 || cell.Bounds?.Length != 6 || !b.ById.TryAdd(cell.Id, cell))
                    throw new ArgumentException("Invalid or duplicate grid cell");
                if (cell.Id != Id(cell.Cell[0], cell.Cell[1], cell.Cell[2]))
                    throw new ArgumentException("Cell identity is not its grid address");
                for (int a = 0; a < 3; a++)
                    if (Math.Abs(cell.Bounds[a] - b.Spec.Grid.Origin[a] - cell.Cell[a]) > 1e-6 ||
                        Math.Abs(cell.Bounds[a + 3] - cell.Bounds[a] - 1) > 1e-6)
                        throw new ArgumentException("Cell bounds are off grid");
                foreach (var face in cell.Faces)
                    foreach (var point in face.Points)
                        for (int a = 0; a < 3; a++)
                            if (point[a] < cell.Bounds[a] - 1e-6 || point[a] > cell.Bounds[a + 3] + 1e-6)
                                throw new ArgumentException("Face leaves its owning cell");
            }
            return b;
        }

        public static string Id(int x, int y, int z) => $"cell:{x}:{y}:{z}";
        public double DeckTop(string code) => Spec.Decks.First(d => d.Code == code).Top;
    }

    public sealed class GridSpec
    {
        public GridCoordinates Grid;
        public int LengthCells, ClearBayCells;
        public int[] RibCells;
        public double RibVisibleWidth, Waterline;
        public GridDeck[] Decks;
        public GridStair Stair;
        public GridHatch Hatch;
    }
    public sealed class GridCoordinates { public double[] Origin; public double CellSize, CollisionVoxel; }
    public sealed class GridDeck { public string Code; public double Top; }
    public sealed class GridStair { public int[] Foot; public string Lower, Upper, Direction; public int Run, Rise; }
    public sealed class GridHatch { public int[] Cell; }
    public sealed class GridModule
    {
        public string Id, Role, Material;
        public int[] Cell;
        public double[] Bounds;
        public bool Required, Seals;
        public string[] Parts, Allowed;
        public GridFace[] Faces;
        public double[][] Solids;
    }
    public sealed class GridFace
    {
        public double[][] Points, Uv;
        public double[] Normal;
        public string Kind, Facing;
    }
}
