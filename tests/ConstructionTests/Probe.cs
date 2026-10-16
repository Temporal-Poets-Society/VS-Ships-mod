using System;
using System.Linq;
using Hansa.Construction;
static class Probe
{
    public static void Slots(ShipConstruction ship, string[] cells)
    {
        foreach (var s in cells)
        {
            var c = CellId.Parse(s);
            Console.WriteLine($"{s,-12} slot '{ship.Lattice.SlotKey(c)}' interior {ship.Lattice.Interior(c)} kind {ship.At(c)?.Kind ?? "-"}");
        }
    }
}
