using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Hansa
{
    /// <summary>
    /// (0.3.27, note 199: "I placed the wrong floor, but cannot change orientation by wrench as vanilla would allow") A piece
    /// put aboard turned with vanilla's wrench, right-click: a wall piece (a partition, a window, a railing, a door) goes round
    /// its cell to the cell's next side (a quarter turn about the cell's middle), a floor piece (a chest, a bed, a stair, a
    /// ladder-stair, anything standing on a floor place) faces the next way round on its place. The next way it fits (a free
    /// place, nothing in its way, as placing it there would be checked); its contents, paint and id stay.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>Is this a wrench (vanilla's)?</summary>
        public static bool IsWrench(ItemStack s) => s?.Collectible?.Tool == EnumTool.Wrench || s?.Collectible?.Code?.Path?.StartsWith("wrench") == true;

        /// <summary>Server: a player turns a piece with a wrench in hand. Returns an error lang code or null.</summary>
        public string TryRotatePiece(IServerPlayer player, int id)
        {
            var p = Pieces.Get(id);
            if (p == null) return null;
            if (GoneDown) return "hansa:gone-down";
            if (!IsWrench(player.InventoryManager.ActiveHotbarSlot?.Itemstack)) return "hansa:rotate-needs-wrench";
            var c = p.Center;
            if (!InReach(player, c[0], c[1], c[2])) return "hansa:piece-too-far";
            string err = RotatePiece(p, out var turned);
            if (err != null) return err;
            PieceSound(turned, turned.Stack?.Block?.Sounds?.Place.Location);
            World.Logger.Audit("{0} turned the {1} on {2} #{3} ({4}).", player.PlayerName, p.Stack?.Collectible?.Code, GetName(), EntityId, turned.Mount);
            return null;
        }

        /// <summary>Turn a piece the next way it fits (the commands and tests: no player). Returns an error lang code or null.</summary>
        public string RotatePiece(ShipPiece p, out ShipPiece turned)
        {
            turned = null;
            if (p == null || Mounts == null) return "hansa:rotate-nothing";
            if (GetSeat("bunk:" + p.StorageCode)?.Passenger != null) return "hansa:bunk-taken";
            if (GetSeat("sit:" + p.StorageCode)?.Passenger != null) return "hansa:piece-seat-taken";
            if (Pieces.StandingOn(Mounts, p) != null) return "hansa:piece-under";
            // (review H1-13) a working block's real block in her dimension isn't turned with it
            if (IsWorkingPiece(p.Id)) return "hansa:rotate-nothing";
            if (p.Role is PieceRole.Hearth or PieceRole.Oven or PieceRole.Flue or PieceRole.Hatch or PieceRole.Shelf or PieceRole.Panel || p.Kind is MountKind.Hook or MountKind.Wall or MountKind.Horizontal or MountKind.Panel)
                return "hansa:rotate-nothing";
            var m = Mounts.Get(p.Mount);
            if (m == null || p.Stack == null) return "hansa:rotate-nothing";
            var block = new VanillaPieceBlock(World, p.Stack, true);
            var tries = new List<PiecePlan>();
            if (p.OnBorder)
                foreach (var b in RotationBorders(m)) tries.Add(PiecePlanner.Plan(Mounts, b, block, b.In));
            else if (m.Kind == MountKind.Floor)
            {
                int toward = p.IsStair ? (p.Dir + 2) % 4 : p.Dir;
                for (int k = 1; k < 4; k++) tries.Add(PiecePlanner.Plan(Mounts, m, block, (toward + k) % 4));
            }
            if (tries.Count == 0) return "hansa:rotate-nothing";
            // (it's out of her while each way is tried: it doesn't stand in its own way)
            Pieces.Remove(p.Id);
            RebuildGrid();
            string first = null;
            foreach (var plan in tries)
            {
                string err = plan.Error ?? PlaceProblem(plan, true);
                if (err != null) { first ??= err; continue; }
                turned = ShipPiece.From(plan, p.Id, p.Stack);
                turned.Paint = p.Paint;
                // (review H1-5: turning it doesn't mend it)
                turned.Hurt = p.Hurt;
                turned.Open = p.Open && turned.Role == PieceRole.Door;
                Pieces.Add(turned);
                Changed();
                return null;
            }
            Pieces.Add(p);
            Changed();
            return first == "hansa:piece-taken" || first == null ? "hansa:rotate-no-room" : first;
        }

        /// <summary>A wall piece's place's cell's other sides (the same row), the next one round first.</summary>
        List<MountPoint> RotationBorders(MountPoint m)
        {
            var (line, alongX) = MountLayout.BorderLine(m);
            double s = m.In < 2 ? 0.5 : -0.5;
            double cx = alongX ? m.X : line + s, cz = alongX ? line + s : m.Z;
            int Side(MountPoint b)
            {
                var (bl, bx) = MountLayout.BorderLine(b);
                if (bx) { if (Math.Abs(b.X - cx) > 0.05) return -1; return Math.Abs(bl - (cz + 0.5)) < 0.05 ? 1 : Math.Abs(bl - (cz - 0.5)) < 0.05 ? 3 : -1; }
                if (Math.Abs(b.Z - cz) > 0.05) return -1;
                return Math.Abs(bl - (cx + 0.5)) < 0.05 ? 0 : Math.Abs(bl - (cx - 0.5)) < 0.05 ? 2 : -1;
            }
            int cur = Side(m);
            var bySide = new Dictionary<int, MountPoint>();
            foreach (var b in Mounts.Borders)
            {
                if (b == m || b.Deck != m.Deck || b.Row != m.Row || Math.Abs(b.Y - m.Y) > 0.01) continue;
                int sd = Side(b);
                if (sd >= 0 && sd != cur && !bySide.ContainsKey(sd)) bySide[sd] = b;
            }
            var list = new List<MountPoint>();
            for (int k = 1; k < 4; k++)
                if (bySide.TryGetValue(((cur < 0 ? 0 : cur) + k) % 4, out var b)) list.Add(b);
            return list;
        }
    }
}
