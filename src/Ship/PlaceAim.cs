using System;
using System.Linq;
using Func1 = System.Func<Hansa.MountPoint, bool>;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (25 October 2026, dev.9: the shelf "cannot be placed" in the game though the offline checks passed) What a block in
    /// hand may go on aboard, and the place the crosshair offers for it, as the client's crosshair works them out
    /// (DecoClient): here so the server's self-test drives the same code with the game's own blocks.
    /// </summary>
    public sealed class HeldForPlacing
    {
        /// <summary>
        /// Building mode's structure: planks (a partition, a panel), a door, a pane (a window), a fence (a railing), a ship's
        /// stair or a ladder, a plank slab (a shelf); a chimney (a flue), the hearth or the oven; the surveyor's chalk.
        /// </summary>
        public bool Planks, Door, Pane, Fence, Stairs, Slab, Flue, Fire, Oven, Chalk;
        /// <summary>A wall piece: what goes on a cell's border.</summary>
        public bool OnBorder => Planks || Door || Pane || Fence;

        public static HeldForPlacing Of(Block b, bool building, bool chalk = false)
        {
            bool block = b != null && b.Id != 0;
            return new HeldForPlacing
            {
                Planks = block && building && VanillaPieceBlock.IsPlanks(b),
                Door = block && VanillaPieceBlock.IsDoor(b),
                Pane = block && building && VanillaPieceBlock.IsPane(b),
                Fence = block && building && VanillaPieceBlock.IsFence(b),
                Stairs = block && building && (VanillaPieceBlock.IsShipStair(b) || VanillaPieceBlock.IsLadder(b)),
                Slab = block && building && VanillaPieceBlock.IsPlankSlab(b),
                Flue = block && VanillaPieceBlock.IsFlueBlock(b),
                Fire = block && (VanillaPieceBlock.IsHearthBlock(b) || VanillaPieceBlock.IsOvenBlock(b)),
                // ((24 October 2026, caravel note 74) the oven on top of a block put aboard too)
                Oven = block && VanillaPieceBlock.IsOvenBlock(b),
                Chalk = chalk
            };
        }

        /// <summary>
        /// (0.3.16, notes 154-159) The kind of place it goes on: a panel place only with planks; a border with planks, a pane,
        /// a fence or (its lowest row) a door, and those nowhere else; a horizontal place with a slab and a slab only there;
        /// stairs, the hearth on a floor place on the deck (the oven on a stacked one too); a flue place with a chimney and a
        /// chimney only there; with a wall piece in hand, not a partition's own wall spots (the border past it is).
        /// </summary>
        public bool Takes(MountPoint x) =>
            (x.Kind != MountKind.Panel || Chalk || Planks) &&
            (x.Kind != MountKind.Border || Chalk || Planks || Pane || Fence || Door && x.Row == 0) && (!(Door || Pane || Fence) || Chalk || x.Kind == MountKind.Border) &&
            (x.Kind != MountKind.Horizontal || Chalk || Slab) && (!Slab || Chalk || x.Kind == MountKind.Horizontal) &&
            (!Stairs || Chalk || x.Kind == MountKind.Floor && x.Level == 0) &&
            (!Fire || Chalk || x.Kind == MountKind.Floor && (x.Level == 0 || Oven)) &&
            (x.Kind != MountKind.Flue || Chalk || Flue) && (!Flue || Chalk || x.Kind == MountKind.Flue) &&
            (!OnBorder || Chalk || x.Kind != MountKind.Wall || x.Under == null);
    }

    public static class PlaceAim
    {
        /// <summary>
        /// A free place aboard for what's in hand: not taken, of its kind, (0.3.20) not over a deck cell taken up, (a place on
        /// top of a piece) only while a piece with a top stands under it.
        /// </summary>
        public static Func1 Free(EntityShip ship, HeldForPlacing h) => x =>
            !ship.Pieces.Taken(x.Id) && h.Takes(x) && ship.MountOpen(x) && ship.Pieces.Supports(ship.Mounts, x);

        /// <summary>
        /// (0.3.18, the user's point 1) The place a ray (ship-local, d normalised) offers: next to what it hits first, a piece
        /// put aboard (piece, at tp) or her own woodwork; the first of <see cref="AimPlace.Candidates"/> the block in hand fits
        /// (planned by plan, given the place and the hit), else the first, to say why not. Null if it hits nothing or nothing's
        /// there. t: how far the hit is; hit: where.
        /// </summary>
        public static MountPoint Choose(EntityShip ship, HeldForPlacing h, Vec3d o, Vec3d d, double range, ShipPiece piece, double tp,
            Func1 free, System.Func<MountPoint, Vec3d, PiecePlan> plan, out PiecePlan chosen, out double t, out Vec3d hit)
        {
            chosen = null;
            t = double.MaxValue;
            hit = null;
            double[] normal = null;
            // (0.3.24, note 177: a piece still here is in front of her, or behind only its own solid layer)
            if (piece != null)
            {
                t = tp;
                hit = o.AddCopy(d.X * tp, d.Y * tp, d.Z * tp);
                // A piece's walking voxels can be met before its drawn face. Snap from the actual block face,
                // never from that quarter-block halo or from the direction of the player's gaze.
                if(AimPlace.PieceFace(piece,o,d,range,out double faceT,out normal)) { t=faceT;hit=o.AddCopy(d.X*t,d.Y*t,d.Z*t); }
            }
            else if (AimPlace.GridHit(ship.Grid, o, d, range, out double tg, out var ng))
            {
                t = tg;
                normal = ng;
                hit = o.AddCopy(d.X * tg, d.Y * tg, d.Z * tg);
            }
            if (hit == null || normal == null) return null;
            MountPoint m = null;
            var candidates = h.Stairs && !h.Chalk
                ? AimPlace.StairFloors(ship.Mounts, hit, normal).Where(c => free == null || free(c))
                : AimPlace.Candidates(ship.Mounts, ship.Pieces, hit, normal, piece, free, h.OnBorder && !h.Chalk);
            foreach (var c in candidates)
            {
                if (h.Chalk) { m = c; break; }
                var pl = plan(c, hit);
                if (m == null) { m = c; chosen = pl; }
                if (pl.Error == null) { m = c; chosen = pl; break; }
            }
            return m;
        }
    }
}
