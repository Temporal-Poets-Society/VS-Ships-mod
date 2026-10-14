using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// (0.3.18, note 165: "the hearth and oven could be placeable, with a chute for the smoke") The ship's hearth and clay
    /// oven as pieces put aboard (fire bricks or a firepit; vanilla's clay oven), and the hearth's flue (brick chimney
    /// segments, one per deck, up to open air). A placed hearth or oven is her hearth or oven exactly as a design's fixed one
    /// is: it gets a stand-in fixture (type "hearth" or "oven", its packet index a piece's) that becomes her
    /// <see cref="HearthFixture"/> or <see cref="OvenFixture"/>, so its inventory, fire, dialog, lighting, drawing, warmth
    /// and smoke are the fixed ones' own code. One of each per ship (a design's fixed one wins). (0.3.24, note 192) A placed
    /// hearth burns with or without a flue (<see cref="HearthFlued"/> is always true); a flue reaching open air takes its
    /// smoke out at its top.
    /// </summary>
    public partial class EntityShip
    {
        /// <summary>(0.3.18) A placed hearth's smoke has a way out: always true for a design's fixed hearth.</summary>
        public bool HearthFlued { get; private set; } = true;
        /// <summary>(0.3.18) The hearth and the oven her pieces gave her (null: none, or the design's own).</summary>
        public FixtureDef PlacedHearth { get; private set; }
        public FixtureDef PlacedOven { get; private set; }

        /// <summary>(0.3.18) The stand-in fixtures of a placed hearth and oven (picked, drawn and used as the design's are).</summary>
        public IEnumerable<FixtureDef> PlacedFixtures
        {
            get
            {
                if (PlacedHearth != null && PlacedHearth == HearthFixture) yield return PlacedHearth;
                if (PlacedOven != null && PlacedOven == OvenFixture) yield return PlacedOven;
            }
        }

        /// <summary>A stand-in fixture for a placed hearth or oven: where its fire, its pot and its block are, its boxes, its room.</summary>
        FixtureDef FireStandIn(ShipPiece p, FixtureDef f)
        {
            var m = Mounts?.Get(p.Mount);
            if (m == null) return f;
            double cx = m.X + 0.5, y = m.Y, cz = m.Z + 0.5;
            f.Kind = p.Role == PieceRole.Hearth ? "hearth" : "oven";
            f.Type = f.Kind;
            f.Yaw = ShipMounts.YawOfDir(p.Dir);
            f.Box = p.Box;
            f.Room = Def.RoomAt(cx, y + 0.5, cz)?.Code;
            if (p.Role == PieceRole.Hearth)
            {
                double b = PiecePlanner.FirebedHeight;
                f.Pos = new[] { cx, y + b, cz };
                f.Block = "game:firepit-cold";
                f.Fire = new[] { cx, y + b + 0.2, cz };
                f.Cook = new[] { cx, y + b + 0.125, cz };
                f.Sel = new[] { cx - 0.4375, y + b, cz - 0.4375, cx + 0.4375, y + b + 0.6, cz + 0.4375 };
            }
            else
            {
                f.Pos = new[] { cx, y, cz };
                f.Block = p.Parts.Count > 0 ? p.Parts[0].Block : "game:clayoven-north";
                f.Fire = new[] { cx, y + 0.15, cz };
                f.Cook = new[] { cx, y + 0.0625, cz };
                f.Sel = p.Box;
            }
            return f;
        }

        /// <summary>
        /// Whenever her pieces change (both sides): a placed hearth or oven is hers (her design has none), its stand-in
        /// fixture her hearth's or oven's; the hearth's flue worked out (does its smoke get out, where it comes out).
        /// </summary>
        void SyncPieceFixtures()
        {
            if (Def == null) return;
            var hp = Def.Fixtures.Any(f => f.Type == "hearth") ? null : Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Hearth);
            var op = Def.Fixtures.Any(f => f.Type == "oven") ? null : Pieces.All.FirstOrDefault(p => p.Role == PieceRole.Oven);
            PlacedHearth = hp == null ? null : FireStandIn(hp, PieceFixture(hp));
            PlacedOven = op == null ? null : FireStandIn(op, PieceFixture(op));
            // (0.3.24, note 192: "works fine relocated; we can skip the chimney for a while to simplify") a placed hearth
            // burns without a flue; a flue up to open air (optional) takes its smoke up and out at its top, else the smoke
            // rises from the fire
            HearthFlued = true;
            if (PlacedHearth != null)
                PlacedHearth.Chimney = HearthFlue.FlueTop(Mounts, Pieces, Def.Grid, hp, out var top) ? top : null;
            if (PlacedOven != null)
            {
                // (no flue of its own: up the hearth's when it stands by a flued one, else from its mouth)
                var hf = PlacedHearth ?? Def.Fixtures.FirstOrDefault(f => f.Type == "hearth");
                PlacedOven.Chimney = hf?.Chimney != null && HearthFlued && Math.Abs(hf.Pos[0] - PlacedOven.Pos[0]) <= 1.6 && Math.Abs(hf.Pos[2] - PlacedOven.Pos[2]) <= 1.6 ? hf.Chimney : null;
            }
            if (hearthReady) SetHearth(Def.Fixtures.FirstOrDefault(f => f.Type == "hearth") ?? PlacedHearth);
            if (ovenReady) SetOven(Def.Fixtures.FirstOrDefault(f => f.Type == "oven") ?? PlacedOven);
        }

        /// <summary>
        /// (0.3.18) Why a hearth, an oven or a flue can't be taken up now (a lang code), or null: the hearth or oven with
        /// something in it or warm, a flue while the hearth burns.
        /// </summary>
        string PieceFireProblem(ShipPiece p)
        {
            switch (p.Role)
            {
                case PieceRole.Hearth when HearthFixture != null && HearthFixture == PlacedHearth:
                    if (Fire.IsBurning || Fire.CanIgniteFuel) return "hansa:hearth-hot";
                    if (HearthInv != null && !HearthInv.Empty) return "hansa:hearth-not-empty";
                    return null;
                case PieceRole.Oven when OvenFixture != null && OvenFixture == PlacedOven:
                    if (Oven.Burning) return "hansa:oven-hot";
                    if (OvenInv != null && !OvenInv.Empty) return "hansa:oven-not-empty";
                    return null;
                case PieceRole.Flue when HearthFixture != null && HearthFixture == PlacedHearth && (Fire.IsBurning || Fire.CanIgniteFuel):
                    return "hansa:flue-hot";
            }
            return null;
        }
    }
}
