using System;
using System.IO;
using System.Linq;
using Hansa;
using Hansa.Grid;

static class Program
{
    static int checks;
    static void Check(bool condition,string description)
    {
        checks++;
        if (!condition) throw new Exception(description);
        Console.WriteLine("ok: "+description);
    }
    static void Reject(Action action,string description)
    {
        try { action(); } catch (InvalidOperationException) { Check(true,description); return; }
        Check(false,description);
    }
    static int Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"Hansa.csproj"))) root=root.Parent;
        var blueprint=GridBlueprint.Read(File.ReadAllText(Path.Combine(root.FullName,"assets/hansa/config/gridships/grid-two-bay.json")));
        var state=new GridAssembly(blueprint);
        Check(blueprint.Spec.LengthCells==9 && blueprint.Spec.RibCells.SequenceEqual(new[]{0,4,8}),"R PPP R PPP R ownership");
        Check(!state.MissingRequired().Any(),"complete default slice has no missing required parts");
        Check(state.At(GridBlueprint.Id(6,6,-2)).Variant=="hatch","default slice includes its closed deck hatch");
        Check(state.StairProblem(2)==null,"default stair has headroom and both landings");
        Check(state.StairProblem(6)==null,"second bay accepts the relocated stair");
        Check(state.StairProblem(4)!=null,"rib/beam intersection rejects an unframed stair opening");
        state.Damage(state.WellAt(2).First(),false);
        Check(state.WellAt(2).All(id=>!state.Solid(id)),"damage to an empty stair well does not recreate a floor");
        var oldWell=state.WellAt(2).ToArray();
        state.MoveStair(6);
        Check(oldWell.All(state.Solid) && state.WellAt(6).All(id=>!state.Solid(id)),"moving the stair restores the old deck and opens the new well");
        var rib=blueprint.Cells.First(c=>c.Role=="rib").Id;
        state.Damage(rib,false);
        Check(state.At(rib).State==SlotState.Cracked && state.Solid(rib),"cracked rib retains collision");
        state.Damage(rib,true);
        Check(!state.Solid(rib) && state.MissingRequired().Contains(rib),"destroyed rib loses collision and gains a missing-part guide");
        state.Lay(rib,"game:planks-pine-ud");
        Check(state.Solid(rib) && state.At(rib).Material.Contains("pine"),"ordinary planks repair the prescribed rib shape");
        var keel=blueprint.Cells.First(c=>c.Role=="keel").Id;
        state.Remove(keel);
        Check(state.MissingRequired().Contains(keel),"keel remains removable despite its requirement");
        var side=blueprint.Cells.First(c=>c.Allowed.Contains("window")).Id;
        state.Lay(side,"game:glasspane-leaded-oak-ew","window");
        Check(state.Sealed(side),"window replaces a hull cell and seals it");
        Reject(()=>state.Lay(rib,"game:glasspane-leaded-oak-ew","window"),"window cannot replace a structural rib");
        Reject(()=>state.Lay(side,"game:stone-granite","plank"),"wrong material is rejected");
        state.Lay(side,"game:planks-oak-ud","port"); state.Toggle(side);
        Check(!state.Sealed(side) && !state.Solid(side),"open port unseals and opens collision");
        state.Toggle(side);
        Check(state.Sealed(side),"shut port seals again");
        string hatch=GridBlueprint.Id(6,6,-2);
        state.Lay(hatch,"game:planks-oak-ud","hatch"); state.Toggle(hatch);
        Check(!state.Solid(hatch) && !state.MissingRequired().Contains(hatch),"open deck hatch has no mandatory-layout ghost");
        var restored=new GridAssembly(blueprint); restored.Load(state.Save());
        Check(restored.StairX==6 && !restored.Solid(hatch) && restored.MissingRequired().Contains(keel),"save/load preserves layout, fitting operation and damage by cell identity");
        Console.WriteLine($"{checks} grid construction checks passed.");
        return 0;
    }
}
