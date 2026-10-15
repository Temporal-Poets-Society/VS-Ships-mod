using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Hansa.Grid
{
    /// <summary>Pure construction state for the first slice, independent of the
    /// legacy carrack's station/band slots. Engine interaction is a later adapter.
    /// Required cells remain damageable; requirements drive missing-part guides.</summary>
    public sealed class GridAssembly
    {
        public readonly GridBlueprint Blueprint;
        readonly Dictionary<string, GridPlacement> changes = new();
        public int StairX { get; private set; }
        readonly HashSet<string> stairWell = new();

        public GridAssembly(GridBlueprint blueprint)
        {
            Blueprint = blueprint;
            StairX = blueprint.Spec.Stair.Foot[0];
            foreach (string id in WellAt(StairX)) stairWell.Add(id);
            var hatch = blueprint.Spec.Hatch.Cell;
            changes[GridBlueprint.Id(hatch[0],hatch[1],hatch[2])] = new GridPlacement { Variant="hatch",Material=blueprint.ById[GridBlueprint.Id(hatch[0],hatch[1],hatch[2])].Material };
        }

        public GridPlacement At(string id)
        {
            if (!Blueprint.ById.ContainsKey(id)) throw new ArgumentException("Unknown blueprint cell");
            if (changes.TryGetValue(id, out var saved)) return saved;
            return new GridPlacement { State = stairWell.Contains(id) ? SlotState.Empty : SlotState.Filled };
        }

        public bool Solid(string id)
        {
            var p = At(id);
            return p.State != SlotState.Empty && !(p.Open && p.Variant is "port" or "gate" or "hatch");
        }
        public bool Sealed(string id) => Blueprint.ById[id].Seals && Solid(id);
        public IEnumerable<string> MissingRequired() => Blueprint.Cells.Where(c => c.Required && !Solid(c.Id)).Select(c => c.Id);
        public IEnumerable<double[]> Collision() => Blueprint.Cells.Where(c => Solid(c.Id)).SelectMany(c => c.Solids);

        /// <summary>A material class, not the exact oak variant, chooses the contextual geometry.</summary>
        public void Lay(string id, string material, string variant = "plank")
        {
            var c = Blueprint.ById[id];
            if (stairWell.Contains(id)) throw new InvalidOperationException("Cell is occupied by the stair opening");
            bool allowed = c.Allowed.Contains(variant) || variant == "hatch" && c.Role == "deck";
            if (!allowed) throw new InvalidOperationException("This blueprint cell cannot take that fitting");
            string code = material?.Split(':').Last() ?? "";
            bool matches = variant == "window" ? code.StartsWith("glasspane-") : code.StartsWith("planks-");
            if (!matches) throw new InvalidOperationException("Wrong building material");
            changes[id] = new GridPlacement { State = SlotState.Filled, Variant = variant, Material = material };
        }

        public void Remove(string id)
        {
            var p = At(id).Copy(); p.State = SlotState.Empty; changes[id] = p;
        }
        public void Damage(string id, bool destroyed)
        {
            if (At(id).State == SlotState.Empty) return;
            var p = At(id).Copy(); p.State = destroyed ? SlotState.Empty : SlotState.Cracked; changes[id] = p;
        }
        public void Toggle(string id)
        {
            var p = At(id).Copy();
            if (p.Variant is not ("port" or "gate" or "hatch") || p.State == SlotState.Empty)
                throw new InvalidOperationException("No operable fitting in this cell");
            p.Open = !p.Open; changes[id] = p;
        }

        public IEnumerable<string> WellAt(int x)
        {
            var stair = Blueprint.Spec.Stair;
            if (stair.Direction != "+z") throw new NotSupportedException("The first slice uses transverse stairs");
            int y = (int)Math.Round(Blueprint.DeckTop(stair.Upper)) - 1;
            for (int k = 0; k < stair.Run; k++) yield return GridBlueprint.Id(x, y, stair.Foot[1] + k);
        }

        public string StairProblem(int x)
        {
            var opening = WellAt(x).ToHashSet();
            foreach (string id in opening)
                if (!Blueprint.ById.TryGetValue(id, out var c) || c.Role != "deck")
                    return "Opening crosses framing or leaves the deck";
            var spec = Blueprint.Spec;
            double lower = Blueprint.DeckTop(spec.Stair.Lower), upper = Blueprint.DeckTop(spec.Stair.Upper);
            if (Math.Abs(upper-lower-spec.Stair.Rise)>1e-6) return "Stair rise does not match its decks";
            double z0 = spec.Grid.Origin[2] + spec.Stair.Foot[1];
            // Walk the 12 quarter-block treads with a 0.4 x 1.85 player envelope.
            // Test against the proposed live grid, including the old well being restored.
            var collision = Blueprint.Cells.Where(c => !opening.Contains(c.Id) &&
                (stairWell.Contains(c.Id) ? true : Solid(c.Id))).SelectMany(c => c.Solids).ToArray();
            for (int n = -1; n <= 12; n++)
            {
                double z = n < 0 ? z0-0.3 : n == 12 ? z0+3.3 : z0+(n+0.5)*0.25;
                double top = n < 0 ? lower : n == 12 ? upper : lower+(n+1)*0.25;
                var body = new[] { x+0.3, top+0.001, z-0.2, x+0.7, top+1.85, z+0.2 };
                if (collision.Any(b => Overlap(body,b))) return "Stair headroom or landing is obstructed";
                if ((n is -1 or 12) && !collision.Any(b => body[0]<b[3] && body[3]>b[0] && z>b[2] && z<b[5] && Math.Abs(b[4]-top)<0.01))
                    return "Stair landing has no floor";
            }
            return null;
        }

        public void MoveStair(int x)
        {
            string problem = StairProblem(x);
            if (problem != null) throw new InvalidOperationException(problem);
            foreach (string id in stairWell) changes[id] = new GridPlacement();
            stairWell.Clear(); StairX = x;
            foreach (string id in WellAt(x)) { changes.Remove(id); stairWell.Add(id); }
        }

        public string Save() => JsonConvert.SerializeObject(new GridAssemblySave { Schema=1, StairX=StairX, Changes=changes });
        public void Load(string json)
        {
            var saved = JsonConvert.DeserializeObject<GridAssemblySave>(json);
            if (saved?.Schema != 1 || WellAt(saved.StairX).Any(id => !Blueprint.ById.TryGetValue(id,out var c) || c.Role != "deck"))
                throw new ArgumentException("Invalid prototype save");
            if (saved.Changes == null || saved.Changes.Keys.Any(id => !Blueprint.ById.ContainsKey(id)))
                throw new ArgumentException("Save references a missing grid cell");
            foreach (var kv in saved.Changes)
            {
                var p = kv.Value;
                if (p == null || !Enum.IsDefined(p.State) ||
                    !(Blueprint.ById[kv.Key].Allowed.Contains(p.Variant) || p.Variant == "hatch" && Blueprint.ById[kv.Key].Role == "deck"))
                    throw new ArgumentException("Invalid saved placement");
            }
            changes.Clear(); foreach (var kv in saved.Changes) changes[kv.Key] = kv.Value;
            StairX=saved.StairX; stairWell.Clear(); foreach(string id in WellAt(StairX)) stairWell.Add(id);
        }

        static bool Overlap(double[] a,double[] b) => a[0]<b[3]-1e-6 && a[3]>b[0]+1e-6 &&
            a[1]<b[4]-1e-6 && a[4]>b[1]+1e-6 && a[2]<b[5]-1e-6 && a[5]>b[2]+1e-6;
    }
    public sealed class GridPlacement
    {
        public SlotState State = SlotState.Filled;
        public string Variant = "plank", Material;
        public bool Open;
        public GridPlacement Copy() => new() { State=State,Variant=Variant,Material=Material,Open=Open };
    }
    public sealed class GridAssemblySave
    {
        public int Schema,StairX;
        public Dictionary<string,GridPlacement> Changes;
    }
}
