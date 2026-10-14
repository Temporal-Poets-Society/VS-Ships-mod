using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Hansa
{
    /// <summary>
    /// (0.3.14) A ship's mini-dimension on a client: the engine's movable block accessor, drawn in her drawn pose. The
    /// engine draws a dimension's chunk meshes with GetRenderOffset (added to each chunk's origin, less the camera) and
    /// GetRenderTransformMatrix (the model-view matrix); its own put the dimension at an integer block position turned
    /// about its centre of mass. Here: the offset makes each chunk's origin its place in the dimension relative to the
    /// dimension's origin (her cell 0, 0, 0), the matrix moves and turns that as her hull is drawn (ShipRenderer.SetModel:
    /// her render origin, her yaw, pitch and roll), the grid's fraction added. Registered in the client's dimension list
    /// before her chunks come (or taking over the chunks of the plain one the engine made when they came first).
    /// </summary>
    public class ShipDimClient : BlockAccessorMovable
    {
        public EntityShip Ship;
        readonly ICoreClientAPI capi;
        readonly int sub;
        double camX, camY, camZ;
        readonly float[] result = new float[16];
        /// <summary>(0.3.16) The chunks this dimension has been given (for the diagnostics and the log).</summary>
        public readonly HashSet<long> Received = new();

        public ShipDimClient(ICoreClientAPI capi, EntityShip ship, int sub) : base((BlockAccessorBase)capi.World.BlockAccessor, new Vec3d(ship.Pos.X, ship.Pos.Y, ship.Pos.Z))
        {
            this.capi = capi;
            this.sub = sub;
            Ship = ship;
            SetSubDimensionId(sub);
            // (the engine skips a dimension with no tracking position when it draws)
            selectionTrackingOriginalPos = new BlockPos(0, 0, 0, 0);
            CenterOfMass = new Vec3d();
        }

        static FieldInfo chunksField;

        /// <summary>Her dimension in the client's list (taking over the chunks of one the engine already made for them).</summary>
        public static ShipDimClient Register(ICoreClientAPI capi, EntityShip ship, int sub)
        {
            var dims = capi.World.MiniDimensions;
            dims.TryGetValue(sub, out var had);
            if (had is ShipDimClient mine) { mine.Ship = ship; return mine; }
            var d = new ShipDimClient(capi, ship, sub);
            int taken = 0;
            if (had is BlockAccessorMovable plain)
            {
                chunksField ??= AccessTools.Field(typeof(BlockAccessorMovable), "chunks");
                if (chunksField?.GetValue(plain) is Dictionary<long, IWorldChunk> chunks)
                    foreach (var kv in chunks.ToArray()) { d.ReceiveChunkQuiet(kv.Key, kv.Value, capi.World); taken++; }
            }
            dims[sub] = d;
            capi.Logger.Notification("[hansa] working blocks: dimension {0} of {1} #{2} registered on this client ({3}; {4} chunk(s) taken over from the engine's own)",
                sub, ship.GetName(), ship.EntityId, had == null ? "before any of its chunks came" : "the engine had made a plain one: " + had.GetType().Name, taken);
            return d;
        }

        void ReceiveChunkQuiet(long cindex, IWorldChunk chunk, IWorldAccessor world)
        {
            Received.Add(cindex);
            base.ReceiveClientChunk(cindex, chunk, world);
        }

        public override void ReceiveClientChunk(long cindex, IWorldChunk chunk, IWorldAccessor world)
        {
            bool fresh = Received.Add(cindex);
            base.ReceiveClientChunk(cindex, chunk, world);
            capi?.Logger.Notification("[hansa] working blocks: a chunk of dimension {0} arrived ({1}; {2} chunk(s) now){3}", sub,
                fresh ? "new" : "again", Received.Count, Ship != null ? $", {ShipBlocksClient.Resolved(capi, Ship)}/{Ship.BlockRecord.Cells.Count} working block(s) of {Ship.GetName()} #{Ship.EntityId} seen" : ", no ship for it yet");
        }

        /// <summary>Where her origin is drawn this frame (as her hull: ShipRenderer.RenderOrigin, steady for whoever's aboard).</summary>
        public static Vec3d Origin(EntityShip ship, EntityPlayer ep)
        {
            var r = ship.Mod?.Client?.Renderer;
            if (r != null && ep != null) return r.RenderOrigin(ship, ep);
            return new Vec3d(ship.Pos.X, ship.Pos.Y, ship.Pos.Z);
        }

        /// <summary>Her turn this frame (column-major, as Mat4f): heading, pitch, roll, as her hull is drawn.</summary>
        public static float[] Rotation(EntityShip ship)
        {
            var m = Mat4f.Create();
            Mat4f.RotateY(m, m, ShipFrame.ModelYaw(ship.Pos.Yaw));
            Mat4f.RotateZ(m, m, (float)ship.Pitch);
            Mat4f.RotateX(m, m, (float)ship.Roll);
            return m;
        }

        public override FastVec3d GetRenderOffset(float dt)
        {
            // the chunk's origin made dimension-relative, the camera added back (the engine takes it off again)
            var cam = capi.World.Player?.Entity?.CameraPos;
            camX = cam?.X ?? 0; camY = cam?.Y ?? 0; camZ = cam?.Z ?? 0;
            return new FastVec3d(camX - ShipBlockMath.BaseX(sub), camY - ShipBlockMath.OriginOffset, camZ - ShipBlockMath.BaseZ(sub));
        }

        public override void SetRenderOffsetY(int offset) { }

        public override float[] GetRenderTransformMatrix(float[] currentModelViewMatrix, Vec3d playerPos)
        {
            var ship = Ship;
            var frac = ship?.GridFrac;
            if (ship == null || !ship.Alive || frac == null)
            {
                // (no ship to carry it: out of sight)
                Mat4f.Translate(result, currentModelViewMatrix, 0, -100000, 0);
                return result;
            }
            var ep = capi.World.Player?.Entity;
            var o = Origin(ship, ep);
            // vertex = chunk-relative point + (its dimension place + camera at offset − playerPos): her local point less the
            // fraction, plus (camera at offset − playerPos) (nothing, as the engine passes the same camera)
            Mat4f.Translate(result, currentModelViewMatrix, (float)(o.X - playerPos.X), (float)(o.Y - playerPos.Y), (float)(o.Z - playerPos.Z));
            Mat4f.RotateY(result, result, ShipFrame.ModelYaw(ship.Pos.Yaw));
            Mat4f.RotateZ(result, result, (float)ship.Pitch);
            Mat4f.RotateX(result, result, (float)ship.Roll);
            Mat4f.Translate(result, result, (float)(frac[0] - (camX - playerPos.X)), (float)(frac[1] - (camY - playerPos.Y)), (float)(frac[2] - (camZ - playerPos.Z)));
            return result;
        }
    }

    /// <summary>(0.3.14) Picking a ship's working blocks: the crosshair on one hands vanilla the block (its selection box, face and hit point).</summary>
    public static class ShipBlocksClient
    {
        /// <summary>(0.3.16) Why the last pick on a ship with working blocks didn't give one of them (null: it did), for the log and .hansa blocks.</summary>
        public static string LastWhy;
        public static long LastShip;

        /// <summary>
        /// After the ships are picked (Patches' postfix on the game's selection ray): if the crosshair is on a ship and one of
        /// her working blocks is as near as what it met of her, the selection is that block (in her dimension; vanilla routes
        /// clicks, the look-at box, the anvil's voxels by its selection boxes).
        /// </summary>
        public static void Pick(ICoreClientAPI capi, Ray ray, ref BlockSelection blockSel, ref EntitySelection entitySel)
        {
            if (capi == null || ray == null || entitySel?.Entity is not EntityShip ship) return;
            var rec = ship.BlockRecord;
            if (rec.Cells.Count == 0) return;
            LastShip = ship.EntityId;
            var frac = ship.GridFrac;
            if (rec.Sub <= 0 || frac == null) { LastWhy = rec.Sub <= 0 ? "her record has no sub-dimension" : "her design has no block grid"; return; }
            double shipDist = ray.origin.DistanceTo(entitySel.Position.AddCopy(entitySel.HitPosition));
            double range = ray.Length;
            ship.RayToLocal(ray.origin, ray.dir, out var o, out var d);
            var ba = capi.World.BlockAccessor;
            double best = double.MaxValue;
            BlockSelection sel = null;
            int air = 0;
            foreach (var c in rec.Cells)
            {
                ShipBlockMath.DimPos(rec.Sub, c.I, c.J, c.K, out int x, out int y, out int z);
                var pos = new BlockPos(x, y, z, Dimensions.MiniDimensions);
                var block = ba.GetBlock(pos);
                if (block == null || block.Id == 0) { air++; continue; }
                Cuboidf[] boxes;
                try { boxes = block.GetSelectionBoxes(ba, pos); } catch { continue; }
                if (boxes == null) continue;
                double ox = o.X - (c.I + frac[0]), oy = o.Y - (c.J + frac[1]), oz = o.Z - (c.K + frac[2]);
                for (int n = 0; n < boxes.Length; n++)
                {
                    var b = boxes[n];
                    if (b == null) continue;
                    double t = ShipBlockMath.RayBox(ox, oy, oz, d.X, d.Y, d.Z, b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2, out int face);
                    if (t < 0 || t > range || t >= best) continue;
                    best = t;
                    sel = new BlockSelection
                    {
                        Position = pos, Face = BlockFacing.ALLFACES[Math.Clamp(face, 0, 5)],
                        HitPosition = new Vec3d(ox + d.X * t, oy + d.Y * t, oz + d.Z * t), SelectionBoxIndex = n, DidOffset = false,
                        // (0.3.16) vanilla calls BlockSelection.Block.OnBeingLookedAt every frame: never left null
                        Block = block
                    };
                }
            }
            if (sel == null)
            {
                LastWhy = air == rec.Cells.Count ? $"this client has none of her {rec.Cells.Count} working block(s) (air in dimension {rec.Sub}: its chunks haven't come)"
                    : air > 0 ? $"the crosshair isn't on one of her working blocks ({air} of {rec.Cells.Count} not loaded here)" : "the crosshair isn't on one of her working blocks";
                return;
            }
            // (as near as her own hit: the piece's boxes are the block's, so they meet at the same point. 0.3.16: but her
            // collision voxels hold the working piece's solids, up to half a voxel outside its box, and they're what her ray
            // meets first: a hit of hers on or right by a working block's cell is that block's own voxels, not in front of it)
            if (best > shipDist + 0.05)
            {
                var part = ShipPart.Decode(entitySel.SelectionBoxIndex);
                bool own = part.Kind == ShipPartKind.Piece && ship.IsWorkingPiece(part.Index);
                double hx = o.X + d.X * shipDist, hy = o.Y + d.Y * shipDist, hz = o.Z + d.Z * shipDist;
                double margin = (ship.Grid?.V ?? 0.25) + 0.05;
                if (!own) own = rec.Cells.Any(c => ShipBlockMath.NearCell(frac[0], frac[1], frac[2], c.I, c.J, c.K, hx, hy, hz, margin));
                if (!own || best > shipDist + margin * 2 + 0.05)
                {
                    LastWhy = $"something of hers is in front of the working block ({part.Kind} at {shipDist:0.00}, the block at {best:0.00})";
                    return;
                }
            }
            LastWhy = null;
            blockSel = sel;
            entitySel = null;
        }

        static BlockPos CellPos(ShipBlockRecord rec, ShipBlockCell c)
        {
            ShipBlockMath.DimPos(rec.Sub, c.I, c.J, c.K, out int x, out int y, out int z);
            return new BlockPos(x, y, z, Dimensions.MiniDimensions);
        }

        /// <summary>The block this client has at a working block's cell (air if its chunk hasn't come).</summary>
        public static Block BlockAt(ICoreClientAPI capi, ShipBlockRecord rec, ShipBlockCell c) =>
            rec.Sub > 0 ? capi.World.BlockAccessor.GetBlock(CellPos(rec, c)) : null;

        /// <summary>How many of her working blocks this client has (not air).</summary>
        public static int Resolved(ICoreClientAPI capi, EntityShip ship)
        {
            var rec = ship.BlockRecord;
            int n = 0;
            foreach (var c in rec.Cells) if (BlockAt(capi, rec, c) is Block b && b.Id != 0) n++;
            return n;
        }

        /// <summary>Is a working piece's real block here on this client?</summary>
        public static bool Loaded(ICoreClientAPI capi, EntityShip ship, int pieceId)
        {
            var rec = ship.BlockRecord;
            var c = rec.ByPiece(pieceId);
            return c != null && capi != null && BlockAt(capi, rec, c) is Block b && b.Id != 0;
        }

        /// <summary>Why a working piece can't be used here yet (for the log), or null if it can.</summary>
        public static string WhyNot(ICoreClientAPI capi, EntityShip ship, int pieceId)
        {
            var rec = ship.BlockRecord;
            var c = rec.ByPiece(pieceId);
            if (c == null) return "it isn't in her record of working blocks on this client";
            if (rec.Sub <= 0) return "her record has no sub-dimension";
            if (!Loaded(capi, ship, pieceId)) return $"this client has air at its cell in dimension {rec.Sub} (its chunk hasn't come; {ChunksHere(capi, rec)} of {ChunkKeys(rec).Count} chunk(s) here)";
            return LastShip == ship.EntityId ? LastWhy : null;
        }

        /// <summary>The chunks (x, y with the dimension's, z) her working blocks and supports are in.</summary>
        public static List<(int, int, int)> ChunkKeys(ShipBlockRecord rec)
        {
            var set = new HashSet<(int, int, int)>();
            foreach (var c in rec.Cells.Select(c => new[] { c.I, c.J, c.K }).Concat(rec.Supports))
            {
                ShipBlockMath.DimPos(rec.Sub, c[0], c[1], c[2], out int x, out int y, out int z);
                set.Add((x / 32, (y + ShipBlockMath.DimSizeY) / 32, z / 32));
            }
            return set.ToList();
        }

        public static int ChunksHere(ICoreClientAPI capi, ShipBlockRecord rec) =>
            rec.Sub <= 0 ? 0 : ChunkKeys(rec).Count(k => capi.World.BlockAccessor.GetChunk(k.Item1, k.Item2, k.Item3) != null);

        /// <summary>.hansa blocks: what this client knows of a ship's working blocks.</summary>
        public static string Report(ICoreClientAPI capi, EntityShip ship)
        {
            var sb = new StringBuilder();
            var rec = ship.BlockRecord;
            var attr = ship.WatchedAttributes.GetTreeAttribute(EntityShip.BlocksKey);
            sb.AppendLine($"{ship.GetName()} #{ship.EntityId}: record {(attr == null ? "NOT synced (no " + EntityShip.BlocksKey + ")" : "synced")}, sub {rec.Sub}, {rec.Cells.Count} working block(s), {rec.Supports.Count} support(s); grid fraction {(ship.GridFrac == null ? "none" : string.Join(", ", ship.GridFrac.Select(f => f.ToString("0.###"))))}");
            if (rec.Sub > 0)
            {
                capi.World.MiniDimensions.TryGetValue(rec.Sub, out var dim);
                string owner = ShipBlocksSystem.ShipForSub(rec.Sub, true) is EntityShip s ? $"#{s.EntityId}" : "none";
                sb.AppendLine($"dimension {rec.Sub} here: {(dim == null ? "NOT registered" : dim.GetType().Name)}{(dim is ShipDimClient sdc ? $" (carried by {(sdc.Ship == null ? "nobody" : "#" + sdc.Ship.EntityId)}, {sdc.Received.Count} chunk(s) given)" : "")}; ship for the sub: {owner}");
                var keys = ChunkKeys(rec);
                sb.AppendLine($"her chunks on this client: {ChunksHere(capi, rec)} of {keys.Count} ({string.Join(" ", keys.Select(k => $"{k.Item1},{k.Item2},{k.Item3}:{(capi.World.BlockAccessor.GetChunk(k.Item1, k.Item2, k.Item3) != null ? "yes" : "NO")}"))})");
            }
            foreach (var c in rec.Cells)
            {
                var p = ship.Pieces.Get(c.Piece);
                var b = BlockAt(capi, rec, c);
                sb.AppendLine($"  piece {c.Piece} cell {c.I},{c.J},{c.K} {c.Code}: here {(b == null || b.Id == 0 ? "AIR" : b.Code.ToString())}; piece {(p == null ? "gone" : $"{p.Role}, {(p.Parts.Count > 0 ? "DRAWN as a piece" : "not drawn")}, {(p.Box != null ? "targetable" : "no box")}")}");
            }
            // pieces that look like working blocks but aren't in the record
            foreach (var p in ship.Pieces.All)
                if (rec.ByPiece(p.Id) == null && p.Stack?.Block is Block wb && ShipBlocksSystem.ClientInstance?.IsWorking(wb) == true)
                    sb.AppendLine($"  piece {p.Id} {wb.Code}: NOT a working block here ({p.Kind}, {p.Parts.Count} part(s), {p.Role})");
            sb.Append($"last pick: {(LastShip == ship.EntityId ? LastWhy ?? "a working block" : "none on her yet")}; in the crosshair: {capi.World.Player?.CurrentBlockSelection?.Position?.ToString() ?? "-"} / {capi.World.Player?.CurrentEntitySelection?.Entity?.GetName() ?? "-"}");
            return sb.ToString();
        }
    }
}
