using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// The surveyor's chalk, for reporting what you find (the user's wish of 2026-10-01; backlog, the sixth client test,
    /// item 9). With it in hand:
    /// - right-click: what the crosshair is on (a part of a ship and where on her, a block, a creature) and the state of
    ///   the ship there are captured at once, and a window asks for a comment; Save writes the note to
    ///   ModData/ships-notes.json (every number) and ships-notes.md (a readable list), and a screenshot is taken
    ///   with the game's own F12 code once the window has gone (its file name goes into the note);
    /// - each note's spot shows a small chalk X with its number (on a ship at her ship-local point, moving and tilting with
    ///   her; else where it was in the world);
    /// - left-click a mark: its note in chat; Shift + left-click: the note is rubbed out.
    /// .ships notes lists them (delete, clear). The notes are this player's, on their own disk; nothing goes to the
    /// server. Nothing here has been seen in the game yet: drawing the marks stops (logged once) if it ever fails.
    /// </summary>
    public class SurveyorsChalk : IRenderer
    {
        readonly ICoreClientAPI capi;
        readonly ShipClientSystem sys;
        readonly string dir;
        /// <summary>The notes as last read or written: what the marks are drawn from.</summary>
        List<SurveyNote> notes = new();
        GuiDialogChalkNote dialog;
        // a click is acted on once: the game repeats it every quarter second or so while the button is held
        bool leftHeld, rightHeld;
        long leftAt, rightAt;
        bool drawFailed;
        LoadedTexture markTex;
        MeshRef quad;
        readonly Dictionary<int, LoadedTexture> labels = new();
        readonly float[] mat = new float[16];
        static readonly Vec4f FullLight = new Vec4f(1, 1, 1, 1);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>How wide a chalk mark is drawn, and how tall its number (blocks); how far off marks are drawn.</summary>
        const double MarkSize = 0.3, LabelHeight = 0.2, DrawRange = 64;

        public double RenderOrder => 0.6;
        public int RenderRange => 9999;

        public SurveyorsChalk(ICoreClientAPI capi, ShipClientSystem sys)
        {
            this.capi = capi;
            this.sys = sys;
            try { dir = capi.GetOrCreateDataPath("ModData"); }
            catch (Exception) { dir = Path.Combine(GamePaths.DataPath, "ModData"); }
            var list = SurveyNoteFile.Load(JsonPath, out string err);
            if (list != null) notes = list;
            else capi.Logger.Warning("[ships] {0} can't be read ({1}); it's set aside when the next note is written", JsonPath, err);
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "ships-chalk");
        }

        public string JsonPath => Path.Combine(dir, SurveyNoteFile.JsonName);
        public string MarkdownPath => Path.Combine(dir, SurveyNoteFile.MarkdownName);

        /// <summary>Is this player holding the chalk?</summary>
        public bool Holding => capi.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemSurveyorsChalk;

        // ------------------------------------------------------------------ the files

        /// <summary>The notes on disk now; a file that can't be read is set aside (renamed, kept) and the notes start again.</summary>
        List<SurveyNote> ReadForWrite()
        {
            var list = SurveyNoteFile.Load(JsonPath, out string err);
            if (list != null) return list;
            string aside = JsonPath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv);
            try
            {
                File.Move(JsonPath, aside);
                capi.ShowChatMessage(Lang.Get("ships:chalk-file-aside", aside, err));
            }
            catch (Exception e)
            {
                capi.Logger.Error("[ships] Couldn't set {0} aside: {1}", JsonPath, e.Message);
            }
            return new List<SurveyNote>();
        }

        bool Write(List<SurveyNote> list)
        {
            try
            {
                SurveyNoteFile.Save(dir, list);
                notes = list;
                return true;
            }
            catch (Exception e)
            {
                capi.Logger.Error("[ships] Writing the notes to {0} failed: {1}", dir, e);
                capi.ShowChatMessage(Lang.Get("ships:chalk-write-failed", JsonPath, e.Message));
                return false;
            }
        }

        // ------------------------------------------------------------------ clicks

        /// <summary>
        /// A click in the world (ShipClientSystem's InWorldAction, before anything else of Hansa's or vanilla's). With
        /// the chalk in hand it's always the chalk's (true: nothing else gets it): right-click writes a note, left-click
        /// reads the mark in the crosshair, Shift + left-click rubs it out. Once a press.
        /// </summary>
        public bool OnClick(EntityPlayer ep, EnumEntityAction action)
        {
            if (!Holding || ep == null) return false;
            long now = capi.World.ElapsedMilliseconds;
            try
            {
                if (action == EnumEntityAction.InWorldRightMouseDown)
                {
                    bool fresh = !rightHeld || now - rightAt > 600;
                    rightHeld = true;
                    rightAt = now;
                    if (fresh) OpenNote(ep);
                }
                else if (action == EnumEntityAction.InWorldLeftMouseDown)
                {
                    bool fresh = !leftHeld || now - leftAt > 600;
                    leftHeld = true;
                    leftAt = now;
                    if (fresh) ClickMark(ep, ShiftDown());
                }
            }
            catch (Exception e)
            {
                capi.Logger.Error("[ships] The surveyor's chalk failed: {0}", e);
                capi.ShowChatMessage(Lang.Get("ships:chalk-failed", e.Message));
            }
            return true;
        }

        bool ShiftDown()
        {
            var keys = capi.Input.KeyboardKeyStateRaw;
            return keys != null && ((int)GlKeys.ShiftRight < keys.Length) && (keys[(int)GlKeys.ShiftLeft] || keys[(int)GlKeys.ShiftRight]);
        }

        void OpenNote(EntityPlayer ep)
        {
            if (dialog?.IsOpened() == true) return;
            var note = Capture(ep);
            var list = SurveyNoteFile.Load(JsonPath, out _) ?? notes;
            dialog?.Dispose();
            dialog = new GuiDialogChalkNote(capi, SurveyNoteFile.NextNumber(list), SurveyNoteFile.Lines(note) + "\n" + Lang.Get("ships:chalk-shot-after"),
                comment => Save(note, comment));
            dialog.OnClosed += () => rightHeld = false;
            dialog.TryOpen();
        }

        void Save(SurveyNote note, string comment)
        {
            try
            {
                var list = ReadForWrite();
                note.Number = SurveyNoteFile.NextNumber(list);
                note.Comment = (comment ?? "").Trim();
                list.Add(note);
                if (!Write(list)) return;
                capi.ShowChatMessage(Lang.Get("ships:chalk-saved", note.Number, JsonPath));
                TakeScreenshot(note.Number, note.TimeUtc);
            }
            catch (Exception e)
            {
                capi.Logger.Error("[ships] Saving a note failed: {0}", e);
                capi.ShowChatMessage(Lang.Get("ships:chalk-failed", e.Message));
            }
        }

        void ClickMark(EntityPlayer ep, bool shift)
        {
            var n = MarkInSight(ep);
            if (n == null)
            {
                capi.TriggerIngameError(this, "ships-chalk", Lang.Get("ships:chalk-no-mark"));
                return;
            }
            if (shift) capi.ShowChatMessage(Delete(n.Number));
            else capi.ShowChatMessage(SurveyNoteFile.ChatText(n));
        }

        string Delete(int number)
        {
            var list = ReadForWrite();
            int i = list.FindIndex(x => x.Number == number);
            if (i < 0) return Lang.Get("ships:chalk-no-such", number);
            list.RemoveAt(i);
            return Write(list) ? Lang.Get("ships:chalk-deleted", number) : Lang.Get("ships:chalk-write-failed", JsonPath, "");
        }

        // ------------------------------------------------------------------ the screenshot

        /// <summary>
        /// A screenshot of what the note is about, without the window: a moment after it has closed, the game's own F12
        /// code is asked for one (its hotkey's handler: it saves the next frame to the Screenshots folder as
        /// yyyy-MM-dd_HH-mm-ss.png and says so in chat). The file is then looked for there and named in the note.
        /// </summary>
        void TakeScreenshot(int number, string key)
        {
            var hk = capi.Input.GetHotKeyByCode("screenshot");
            if (hk?.Handler == null)
            {
                SetShot(number, key, new NoteScreenshot { Status = "no screenshot key in this game: match one taken with F12 by the note's time" });
                return;
            }
            capi.Event.RegisterCallback(_ =>
            {
                var asked = DateTime.Now;
                try { hk.Handler(null); }
                catch (Exception e)
                {
                    SetShot(number, key, new NoteScreenshot { Status = "the screenshot failed (" + e.Message + "): match one taken with F12 by the note's time" });
                    return;
                }
                capi.Event.RegisterCallback(__ => FindShot(number, key, asked, 0), 700);
            }, 250);
        }

        void FindShot(int number, string key, DateTime asked, int attempt)
        {
            FileInfo found = null;
            try
            {
                var d = new DirectoryInfo(GamePaths.Screenshots);
                if (d.Exists)
                    found = d.GetFiles("*.png").Where(f => f.LastWriteTime >= asked.AddSeconds(-1.5))
                        .OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            }
            catch (Exception) { }
            if (found != null)
            {
                SetShot(number, key, new NoteScreenshot
                {
                    File = found.Name, Path = found.FullName, Status = "taken",
                    Time = found.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", Inv)
                });
            }
            else if (attempt < 6) capi.Event.RegisterCallback(_ => FindShot(number, key, asked, attempt + 1), 1000);
            else SetShot(number, key, new NoteScreenshot
            {
                Status = $"none found in {GamePaths.Screenshots} after it was asked for at {asked.ToString("HH:mm:ss", Inv)}: match one by the note's time"
            });
        }

        void SetShot(int number, string key, NoteScreenshot shot)
        {
            var list = SurveyNoteFile.Load(JsonPath, out _);
            var n = list?.Find(x => x.Number == number && x.TimeUtc == key);
            if (n == null) return;
            n.Screenshot = shot;
            Write(list);
        }

        // ------------------------------------------------------------------ .ships notes

        /// <summary>.ships notes [list | show n | delete n | clear [yes]].</summary>
        public TextCommandResult Command(string what, string arg)
        {
            what = (what ?? "list").ToLowerInvariant();
            int.TryParse(arg?.TrimStart('#') ?? "", NumberStyles.Integer, Inv, out int number);
            switch (what)
            {
                case "list":
                {
                    var list = SurveyNoteFile.Load(JsonPath, out string err);
                    if (list == null) return TextCommandResult.Error(Lang.Get("ships:chalk-unreadable", JsonPath, err));
                    notes = list;
                    if (list.Count == 0) return TextCommandResult.Success(Lang.Get("ships:chalk-none", JsonPath));
                    var shown = list.OrderBy(n => n.Number).ToList();
                    int skip = Math.Max(0, shown.Count - 25);
                    string head = Lang.Get("ships:chalk-list", list.Count, JsonPath) + (skip > 0 ? " " + Lang.Get("ships:chalk-list-more", skip) : "");
                    return TextCommandResult.Success(head + "\n" + string.Join("\n", shown.Skip(skip).Select(SurveyNoteFile.ListLine)));
                }
                case "show":
                {
                    var n = (SurveyNoteFile.Load(JsonPath, out _) ?? notes).Find(x => x.Number == number);
                    return n == null ? TextCommandResult.Error(Lang.Get("ships:chalk-no-such", arg ?? "?")) : TextCommandResult.Success(SurveyNoteFile.ChatText(n));
                }
                case "delete":
                    if (number <= 0) return TextCommandResult.Error(Lang.Get("ships:cmd-notes"));
                    return TextCommandResult.Success(Delete(number));
                case "clear":
                {
                    var list = SurveyNoteFile.Load(JsonPath, out _) ?? notes;
                    if (arg?.ToLowerInvariant() != "yes") return TextCommandResult.Success(Lang.Get("ships:chalk-clear-ask", list.Count));
                    ReadForWrite();
                    return Write(new List<SurveyNote>()) ? TextCommandResult.Success(Lang.Get("ships:chalk-cleared", list.Count))
                        : TextCommandResult.Error(Lang.Get("ships:chalk-write-failed", JsonPath, ""));
                }
                default:
                    return TextCommandResult.Error(Lang.Get("ships:cmd-notes"));
            }
        }

        // ------------------------------------------------------------------ what's captured

        static double R(double v, int digits) => double.IsFinite(v) ? Math.Round(v, digits) : 0;
        static double[] V(Vec3d v, int digits = 3) => v == null ? null : NoteMath.V(v.X, v.Y, v.Z, digits);
        static double Deg(double radians) => R(radians * GameMath.RAD2DEG, 2);

        static T Try<T>(Func<T> f) where T : class
        {
            try { return f(); }
            catch (Exception) { return null; }
        }

        static NotePose Pose(EntityShip ship) => new NotePose(ship.Pos.X, ship.Pos.Y, ship.Pos.Z, ship.Pos.Yaw, ship.Pitch, ship.Roll);

        static Vec3d Eye(EntityPlayer ep) => ep.CameraPos.AddCopy(ep.LocalEyePos);

        static double[] Look(EntityPlayer ep)
        {
            var v = EntityPos.GetViewVector(ep.Pos.Pitch, ep.Pos.Yaw);
            return NoteMath.Unit(v.X, v.Y, v.Z);
        }

        /// <summary>"Carrack "Santa Maria" #6743", or "Carrack #6743" before she's named.</summary>
        static string ShipLabel(EntityShip ship)
        {
            string key = ship.Code != null ? ship.Code.Domain + ":entity-" + ship.Code.Path : null;
            string kind = key != null && Lang.HasTranslation(key, false, false) ? Lang.Get(key) : ship.Def?.Name ?? "Ship";
            return ship.ShipName != null ? $"{kind} \"{ship.ShipName}\" #{ship.EntityId}" : $"{kind} #{ship.EntityId}";
        }

        /// <summary>Everything about the moment of the click but the comment.</summary>
        SurveyNote Capture(EntityPlayer ep)
        {
            var plr = capi.World.Player;
            var now = DateTime.Now;
            var n = new SurveyNote
            {
                Time = now.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                TimeUtc = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv),
                GameDate = Try(() => capi.World.Calendar?.PrettyDate()),
                GameHours = R(capi.World.Calendar?.TotalHours ?? 0, 2),
                ShipsVersion = sys.Mod?.Info?.Version,
                Sea = capi.ModLoader.GetMod("sea")?.Info?.Version,
                Game = GameVersion.LongGameVersion,
                World = Try(() => capi.World.SavegameIdentifier),
            };
            var eye = Eye(ep);
            var dir = Look(ep);
            double range = plr.WorldData.PickingRange;
            n.Player = new NotePlayer
            {
                Name = plr.PlayerName,
                Pos = NoteMath.V(ep.Pos.X, ep.Pos.Y, ep.Pos.Z),
                Eye = V(eye),
                Look = NoteMath.V(dir[0], dir[1], dir[2], 4),
                Dimension = ep.Pos.Dimension,
                Heading = R(NoteMath.Heading(dir[0], dir[2]), 1),
                Elevation = R(NoteMath.Elevation(dir[0], dir[1], dir[2]), 1),
                GameMode = plr.WorldData.CurrentGameMode.ToString(),
                Camera = plr.CameraMode.ToString(),
            };
            var seat = ep.MountedOn as ShipSeat;
            var aboard = seat?.Ship?.Def != null ? seat.Ship : null;
            if (aboard != null) n.Player.Aboard = Aboard(seat, eye, dir);

            var es = plr.CurrentEntitySelection;
            var bs = plr.CurrentBlockSelection;
            EntityShip target = null;
            NoteMark mark;
            if (es?.Entity is EntityShip ship && ship.Def != null)
            {
                target = ship;
                n.Target = ShipTarget(ship, es, eye, dir, range, out mark);
            }
            else if (es?.Entity != null) n.Target = EntityTarget(es, eye, dir, out mark);
            else if (bs?.Position != null) n.Target = BlockTarget(bs, eye, out mark);
            else
            {
                n.Target = new NoteTarget
                {
                    Kind = "nothing",
                    Summary = string.Format(Inv, "nothing in reach; looking {0:0}° ({1}), {2:0}° {3}", n.Player.Heading, NoteMath.CompassPoint(n.Player.Heading),
                        Math.Abs(n.Player.Elevation), n.Player.Elevation >= 0 ? "up" : "down")
                };
                // its mark where they stand
                mark = aboard != null
                    ? new NoteMark { ShipId = aboard.EntityId, ShipCode = aboard.Def.Code, Local = V(seat.LocalPos), Normal = new[] { 0.0, 1, 0 }, World = n.Player.Pos, Dimension = ep.Pos.Dimension }
                    : new NoteMark { World = n.Player.Pos, Normal = new[] { 0.0, 1, 0 }, Dimension = ep.Pos.Dimension };
            }
            if (aboard != null) n.Ships.Add(ShipState(aboard, aboard == target ? "aboard, target" : "aboard"));
            if (target != null && target != aboard) n.Ships.Add(ShipState(target, "target"));
            n.Mark = mark;
            return n;
        }

        /// <summary>Aboard: which seat (deck: walking), where in her, on which deck and in which room, looking which way in her frame.</summary>
        static NoteAboard Aboard(ShipSeat seat, Vec3d eye, double[] dir)
        {
            var ship = seat.Ship;
            var l = seat.LocalPos;
            var el = ship.WorldToLocal(eye.X, eye.Y, eye.Z);
            var ld = ship.WorldDirToLocal(dir[0], dir[1], dir[2]);
            DeckDef deck = null;
            foreach (var d in ship.Def.Decks)
                if (l.X >= d.X0 && l.X <= d.X1 && d.Y <= l.Y + 0.3 && (deck == null || d.Y > deck.Y)) deck = d;
            return new NoteAboard
            {
                ShipId = ship.EntityId,
                Seat = seat is DeckSeat ? "deck" : seat.SeatId,
                Climbing = (seat as DeckSeat)?.Mover?.Climbing?.Code,
                Local = V(l),
                EyeLocal = V(el),
                LookLocal = V(ld, 4),
                Bearing = R(NoteMath.Bearing(ld.X, ld.Z), 1),
                Elevation = R(NoteMath.Elevation(ld.X, ld.Y, ld.Z), 1),
                Deck = deck?.Code,
                Room = ship.Def.RoomAt(l.X, l.Y + 0.5, l.Z)?.Code,
            };
        }

        /// <summary>A ship in the crosshair: the part (as the look-at box says it), where on her (her frame and the world), the fixture, its damage.</summary>
        NoteTarget ShipTarget(EntityShip ship, EntitySelection es, Vec3d eye, double[] dir, double range, out NoteMark mark)
        {
            var part = ShipPart.Decode(es.SelectionBoxIndex);
            var held = capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            bool got = ship.RayHit(eye, new Vec3d(dir[0], dir[1], dir[2]), range + 0.5, held, out var hit);
            double[] local, world, normal;
            double dist;
            if (got)
            {
                NoteMath.FromHit(hit, out local, out normal, out world);
                dist = hit.Distance;
            }
            else
            {
                // (the game's selection has her, this frame's ray just missed: its hit point)
                var hp = es.HitPosition ?? new Vec3d();
                var w = new Vec3d(ship.Pos.X + hp.X, ship.Pos.Y + hp.Y, ship.Pos.Z + hp.Z);
                world = V(w);
                local = V(ship.WorldToLocal(w.X, w.Y, w.Z));
                normal = null;
                dist = eye.DistanceTo(w);
            }
            if (normal == null)
            {
                var back = ship.WorldDirToLocal(-dir[0], -dir[1], -dir[2]);
                normal = NoteMath.V(back.X, back.Y, back.Z, 4);
            }
            FixtureDef fx = part.Kind == ShipPartKind.Fixture && part.Index < ship.Def.Fixtures.Length ? ship.Def.Fixtures[part.Index] : got ? hit.Fixture : null;
            // the fixture the crosshair picks for clicking (ShipClientSystem.FindLookedAtFixture), if it's where the hull is met
            var look = sys.LookedAt;
            if (fx == null && look.Ship == ship && Math.Abs(look.Dist - dist) < 0.3) fx = look.Fixture;
            string partName = Try(() => ship.PartText(part)) ?? part.ToString();
            var h = new NoteShipHit
            {
                Code = ship.Def.Code, Name = ship.ShipName, Design = ship.Def.Name, Id = ship.EntityId,
                Part = partName, PartIndex = es.SelectionBoxIndex, PartCode = part.ToString(),
                Fixture = fx?.Code, FixtureType = fx?.Type, FixtureName = fx != null ? Try(() => ShipHud.FixtureName(ship, fx)) : null,
                Local = local, Normal = normal, World = world,
                Face = (got ? hit.Face : es.Face)?.Code,
                Distance = R(dist, 2),
                Damage = Try(() => Damage(ship, part)),
                LookAtBox = Try(() => (ship.GetName() + "\n" + ship.GetInfoText()).Trim()),
            };
            mark = new NoteMark { ShipId = ship.EntityId, ShipCode = ship.Def.Code, Local = local, Normal = normal, World = world, Dimension = ship.Pos.Dimension };
            string summary = ShipLabel(ship) + ": " + partName;
            if (fx != null && part.Kind != ShipPartKind.Fixture) summary += $" (by {h.FixtureName ?? fx.Code})";
            return new NoteTarget { Kind = "ship", Summary = summary, Ship = h };
        }

        /// <summary>The part's state in the damage model; at a mast its rigging and sails too, at the helm the rudder.</summary>
        static List<NotePartState> Damage(EntityShip ship, ShipPart part)
        {
            var l = ship.DamageParts;
            if (l == null || ship.PartDamage == null) return null;
            var idx = new List<int>();
            int i = ship.DamagePartFor(part);
            if (i >= 0) idx.Add(i);
            if (part.Kind == ShipPartKind.Mast)
                foreach (var p in l.Parts)
                    if (p.Mast == part.Index && (p.Kind == DamageKind.Rigging || p.Kind == DamageKind.Sail) && !idx.Contains(p.Index)) idx.Add(p.Index);
            if (idx.Count == 0) return null;
            return idx.Select(k => new NotePartState
            {
                Part = ship.PartName(k), Index = k, State = ship.StateOfPart(k).ToString(),
                Hurt = R(ship.PartDamage.Hurt[k], 3), Wear = R(ship.PartDamage.Wear[k], 3), Leak = R(ship.LeakEstimate(k), 2)
            }).ToList();
        }

        NoteTarget BlockTarget(BlockSelection bs, Vec3d eye, out NoteMark mark)
        {
            var pos = bs.Position;
            var block = capi.World.BlockAccessor.GetBlock(pos);
            var hp = bs.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
            double hx = pos.X + hp.X, hy = pos.Y + hp.Y, hz = pos.Z + hp.Z;
            string code = block?.Code?.ToString() ?? "?";
            string name = Try(() => block.GetPlacedBlockName(capi.World, pos)) ?? code;
            var face = bs.Face;
            var b = new NoteBlockHit
            {
                Code = code, Name = name, Pos = new[] { pos.X, pos.Y, pos.Z }, Dimension = pos.dimension, Face = face?.Code,
                Hit = NoteMath.V(hx, hy, hz), Distance = R(eye.DistanceTo(new Vec3d(hx, hy, hz)), 2),
                Info = Try(() => block.GetPlacedBlockInfo(capi.World, pos, capi.World.Player))?.Trim(),
            };
            var nrm = face != null ? new double[] { face.Normali.X, face.Normali.Y, face.Normali.Z } : new[] { 0.0, 1, 0 };
            mark = new NoteMark { World = b.Hit, Normal = nrm, Dimension = pos.dimension };
            return new NoteTarget { Kind = "block", Summary = $"{name} ({code}) at {pos.X},{pos.Y},{pos.Z}, {face?.Code ?? "?"} face", Block = b };
        }

        static NoteTarget EntityTarget(EntitySelection es, Vec3d eye, double[] dir, out NoteMark mark)
        {
            var e = es.Entity;
            var hp = es.HitPosition ?? new Vec3d();
            double hx = e.Pos.X + hp.X, hy = e.Pos.Y + hp.Y, hz = e.Pos.Z + hp.Z;
            var h = new NoteEntityHit
            {
                Code = e.Code?.ToString(), Name = Try(() => e.GetName()), Id = e.EntityId,
                Pos = NoteMath.V(e.Pos.X, e.Pos.Y, e.Pos.Z), Hit = NoteMath.V(hx, hy, hz), Distance = R(eye.DistanceTo(new Vec3d(hx, hy, hz)), 2),
            };
            // (it stays where the creature was hit)
            mark = new NoteMark { World = h.Hit, Normal = NoteMath.V(-dir[0], -dir[1], -dir[2], 4), Dimension = e.Pos.Dimension };
            return new NoteTarget { Kind = "entity", Summary = $"{h.Name ?? h.Code} ({h.Code}) #{e.EntityId}", Entity = h };
        }

        /// <summary>A ship's state: under way, the wind and the sea there, how she's drawn moving, the water in her, what's hurt.</summary>
        NoteShip ShipState(EntityShip ship, string role)
        {
            double x = ship.Pos.X, z = ship.Pos.Z;
            var s = new NoteShip
            {
                Role = role, Code = ship.Def.Code, Name = ship.ShipName, Design = ship.Def.Name, Id = ship.EntityId,
                Pos = NoteMath.V(ship.Pos.X, ship.Pos.Y, ship.Pos.Z),
                Heading = R(HansaMod.HeadingDeg(ship.Pos.Yaw), 1),
                Speed = R(ship.Speed, 2), Knots = R(ship.Speed * 1.944, 1),
                Sails = ship.Crawl ? "sweeps" : ship.SailSet switch { <= 0 => "furled", 1 => "half", _ => "full" },
                SailFraction = R(ship.SailFraction, 2), Rudder = R(ship.Rudder, 2), Anchored = ship.Anchored, Aground = ship.Aground,
                Heave = R(ship.Heave, 3), Pitch = Deg(ship.Pitch), Roll = Deg(ship.Roll), Heel = Deg(ship.Heel),
                TruePitch = Deg(ship.TruePitch), TrueRoll = Deg(ship.TrueRoll), SeaMotion = R(sys.SeaMotion, 2),
                Water = R(ship.Water, 2), WaterRate = R(ship.WaterRate * 1000, 1), List = Deg(ship.ListAngle), Trim = Deg(ship.Trim),
                Sinkage = R(ship.Sinkage, 3), Stage = ship.Stage.ToString(), Pumpers = ship.Pumpers, Fouling = R(ship.Fouling, 2),
                OnBottom = ship.OnBottom, Clad = ship.Clad,
            };
            try
            {
                var w = ship.WindHere(x, z);
                s.Wind = new NoteWind
                {
                    Strength = R(w.Strength, 3), Speed = R(w.Strength * Sea.Beaufort.MetersPerSecondPerUnit, 1),
                    Force = Sea.Beaufort.Force(w.Strength), Name = Sea.Beaufort.Name(w.Strength), From = R(w.FromDegrees, 0)
                };
            }
            catch (Exception) { }
            try
            {
                var sea = ship.SeaSys;
                if (sea != null)
                {
                    var st = sea.SeaAt(x, z);
                    double f = sea.WaveFactorAt(x, z);
                    s.Waves = new NoteWaves { Height = R(st.Height * f, 2), Period = R(st.Period, 1), Factor = R(f, 2), Surface = R(sea.WaveHeightAt(x, z), 2) };
                }
            }
            catch (Exception) { }
            try
            {
                var l = ship.DamageParts;
                if (l != null && ship.PartDamage != null)
                {
                    var hurt = new List<string>();
                    for (int i = 0; i < l.Count && hurt.Count < 40; i++)
                    {
                        var st = ship.StateOfPart(i);
                        if (st != PartState.Sound) hurt.Add($"{ship.PartName(i)}: {st}");
                    }
                    if (hurt.Count > 0) s.Hurt = hurt;
                }
            }
            catch (Exception) { }
            return s;
        }

        // ------------------------------------------------------------------ the marks

        /// <summary>
        /// Where a note's mark is drawn now (world), its surface's normal and the two directions across it; false if it
        /// isn't shown here (another world or dimension, its ship not loaded, too far).
        /// </summary>
        bool MarkAt(SurveyNote n, EntityPlayer ep, out double[] at, out double[] normal, out double[] t1, out double[] t2)
        {
            at = normal = t1 = t2 = null;
            var m = n?.Mark;
            if (m == null || m.Dimension != ep.Pos.Dimension) return false;
            if (n.World != null && capi.World.SavegameIdentifier != null && n.World != capi.World.SavegameIdentifier) return false;
            var up = new[] { 0.0, 1, 0 };
            if (m.ShipId != 0)
            {
                EntityShip ship = null;
                foreach (var s in sys.Ships) if (s.EntityId == m.ShipId) { ship = s; break; }
                if (ship?.Def == null || m.Local == null) return false;
                var o = sys.Renderer?.RenderOrigin(ship, ep) ?? new Vec3d(ship.Pos.X, ship.Pos.Y, ship.Pos.Z);
                var pose = new NotePose(o.X, o.Y, o.Z, ship.Pos.Yaw, ship.Pitch, ship.Roll);
                var nl = m.Normal ?? up;
                at = NoteMath.ShipMarkWorld(pose, m.Local, nl);
                NoteMath.Across(nl, out var a1, out var a2);
                normal = NoteMath.DirToWorld(pose, nl[0], nl[1], nl[2]);
                t1 = NoteMath.DirToWorld(pose, a1[0], a1[1], a1[2]);
                t2 = NoteMath.DirToWorld(pose, a2[0], a2[1], a2[2]);
            }
            else
            {
                if (m.World == null) return false;
                normal = NoteMath.Unit((m.Normal ?? up)[0], (m.Normal ?? up)[1], (m.Normal ?? up)[2]);
                at = new[] { m.World[0] + normal[0] * NoteMath.MarkLift, m.World[1] + normal[1] * NoteMath.MarkLift, m.World[2] + normal[2] * NoteMath.MarkLift };
                NoteMath.Across(normal, out t1, out t2);
            }
            var cam = ep.CameraPos;
            double dx = at[0] - cam.X, dy = at[1] - cam.Y, dz = at[2] - cam.Z;
            return dx * dx + dy * dy + dz * dz <= DrawRange * DrawRange;
        }

        /// <summary>The mark the crosshair is on (nearest the ray, not behind what the crosshair is on), or null.</summary>
        SurveyNote MarkInSight(EntityPlayer ep)
        {
            if (notes.Count == 0) return null;
            var e = Eye(ep);
            double[] eye = { e.X, e.Y, e.Z }, dir = Look(ep);
            double limit = capi.World.Player.WorldData.PickingRange + 1;
            // not through what the crosshair is on (a little behind it: a mark lies on it)
            var plr = capi.World.Player;
            if (plr.CurrentEntitySelection?.Entity is Entity se && plr.CurrentEntitySelection.HitPosition is Vec3d sh)
                limit = Math.Min(limit, e.DistanceTo(new Vec3d(se.Pos.X + sh.X, se.Pos.Y + sh.Y, se.Pos.Z + sh.Z)) + 0.5);
            else if (plr.CurrentBlockSelection?.Position is BlockPos bp && plr.CurrentBlockSelection.HitPosition is Vec3d bh)
                limit = Math.Min(limit, e.DistanceTo(new Vec3d(bp.X + bh.X, bp.Y + bh.Y, bp.Z + bh.Z)) + 0.5);
            SurveyNote best = null;
            double bestOff = double.MaxValue;
            foreach (var n in notes)
            {
                if (!MarkAt(n, ep, out var at, out _, out _, out _)) continue;
                double off = NoteMath.OffRay(eye, dir, at, out double along);
                if (along < 0 || along > limit || off > NoteMath.PickRadius(along) || off >= bestOff) continue;
                best = n;
                bestOff = off;
            }
            return best;
        }

        /// <summary>
        /// The line above the hotbar while the chalk is in hand (not at the helm, the oars, a gun or in a bunk): the mark in
        /// the crosshair and what clicking it does, else what a right-click writes about. Null otherwise.
        /// </summary>
        public string HudText(EntityPlayer ep)
        {
            if (!Holding || ep == null || ep.MountedOn is HelmSeat or RowSeat or GunSeat or BunkSeat) return null;
            try
            {
                var m = MarkInSight(ep);
                if (m != null)
                {
                    string words = SurveyNoteFile.FirstWords(m.Comment, 6);
                    return Lang.Get("ships:chalk-hud-mark", m.Number, words.Length > 0 ? words : m.Target?.Summary ?? "");
                }
                var plr = capi.World.Player;
                string what;
                if (plr.CurrentEntitySelection?.Entity is EntityShip ship && ship.Def != null)
                    what = ShipLabel(ship) + ": " + ship.PartText(ShipPart.Decode(plr.CurrentEntitySelection.SelectionBoxIndex));
                else if (plr.CurrentEntitySelection?.Entity is Entity e) what = e.GetName();
                else if (plr.CurrentBlockSelection?.Position is BlockPos bp) what = capi.World.BlockAccessor.GetBlock(bp)?.GetPlacedBlockName(capi.World, bp);
                else what = Lang.Get("ships:chalk-hud-here");
                return Lang.Get("ships:chalk-hud", what);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            // a press ends when the button is let go
            var mouse = capi.Input.InWorldMouseButton;
            if (!mouse.Left) leftHeld = false;
            if (!mouse.Right && dialog?.IsOpened() != true) rightHeld = false;
            if (drawFailed || notes.Count == 0 || !Holding) return;
            var ep = capi.World.Player?.Entity;
            if (ep == null) return;
            var rapi = capi.Render;
            IStandardShaderProgram prog = null;
            try
            {
                if (quad == null) quad = rapi.UploadMesh(UnitQuad());
                if (markTex == null) markTex = MarkTexture();
                var cam = ep.CameraPos;
                var e = Eye(ep);
                rapi.GlDisableCullFace();
                rapi.GlToggleBlend(true);
                foreach (var n in notes)
                {
                    if (!MarkAt(n, ep, out var at, out var nrm, out var t1, out var t2)) continue;
                    prog = rapi.PreparedStandardShader((int)Math.Floor(at[0]), (int)Math.Floor(at[1]), (int)Math.Floor(at[2]));
                    prog.ViewMatrix = rapi.CameraMatrixOriginf;
                    prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                    // chalk shows up in the dark hold too
                    prog.RgbaLightIn = FullLight;
                    prog.AlphaTest = 0.05f;
                    // the X: a square across the surface, its strokes along the diagonals
                    double[] rel = { at[0] - cam.X, at[1] - cam.Y, at[2] - cam.Z };
                    Place(rel, t1, t2, nrm, MarkSize, MarkSize);
                    prog.Tex2D = markTex.TextureId;
                    prog.ModelMatrix = mat;
                    rapi.RenderMesh(quad);
                    // its number, a little above it, turned to the eye
                    var label = Label(n.Number);
                    if (label != null && label.Width > 0 && label.Height > 0)
                    {
                        double[] c = { rel[0] + nrm[0] * 0.06, rel[1] + nrm[1] * 0.06 + MarkSize * 0.7, rel[2] + nrm[2] * 0.06 };
                        double[] toEye = NoteMath.Unit(e.X - cam.X - c[0], e.Y - cam.Y - c[1], e.Z - cam.Z - c[2]);
                        // right = up × toEye, up = toEye × right
                        double[] right = NoteMath.Unit(toEye[2], 0, -toEye[0]);
                        double[] upv = { toEye[1] * right[2] - toEye[2] * right[1], toEye[2] * right[0] - toEye[0] * right[2], toEye[0] * right[1] - toEye[1] * right[0] };
                        double w = LabelHeight * label.Width / label.Height;
                        Place(c, right, upv, toEye, w, LabelHeight);
                        prog.Tex2D = label.TextureId;
                        prog.ModelMatrix = mat;
                        rapi.RenderMesh(quad);
                    }
                    prog.Stop();
                    prog = null;
                }
                rapi.GlToggleBlend(false);
                rapi.GlEnableCullFace();
            }
            catch (Exception ex)
            {
                drawFailed = true;
                capi.Logger.Error("[ships] Drawing the chalk marks failed; they're not drawn until the game is restarted: {0}", ex);
                try { prog?.Stop(); } catch (Exception) { }
                try { rapi.GlToggleBlend(false); rapi.GlEnableCullFace(); } catch (Exception) { }
            }
        }

        /// <summary>The unit quad's model matrix: centred on c (camera-relative), its x along a (width w), its y along b (height h), facing n.</summary>
        void Place(double[] c, double[] a, double[] b, double[] n, double w, double h)
        {
            for (int i = 0; i < 3; i++)
            {
                mat[i] = (float)(a[i] * w);
                mat[4 + i] = (float)(b[i] * h);
                mat[8 + i] = (float)n[i];
                mat[12 + i] = (float)(c[i] - a[i] * w / 2 - b[i] * h / 2);
            }
            mat[3] = mat[7] = mat[11] = 0;
            mat[15] = 1;
        }

        /// <summary>A quad from (0,0) to (1,1) in its x-y plane facing +z, the whole texture on it (upright along +y).</summary>
        static MeshData UnitQuad()
        {
            var md = new MeshData(4, 6, false, true, true, true);
            int flags = VertexFlags.PackNormal(0, 0, 1);
            md.AddWithFlagsVertex(0, 0, 0, 0, 1, -1, flags);
            md.AddWithFlagsVertex(1, 0, 0, 1, 1, -1, flags);
            md.AddWithFlagsVertex(1, 1, 0, 1, 0, -1, flags);
            md.AddWithFlagsVertex(0, 1, 0, 0, 0, -1, flags);
            md.AddIndices(0, 1, 2, 0, 2, 3);
            return md;
        }

        /// <summary>A chalk X: two rough white strokes corner to corner, with a faint dark edge so it shows on pale wood too.</summary>
        LoadedTexture MarkTexture()
        {
            const int S = 32;
            var px = new int[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    double u = (x + 0.5) / S * 2 - 1, v = (y + 0.5) / S * 2 - 1;
                    double d = Math.Min(Math.Abs(u - v), Math.Abs(u + v)) / Math.Sqrt(2);
                    double r = Math.Max(Math.Abs(u), Math.Abs(v));
                    // a hand-drawn stroke: a little thicker and thinner along its length
                    double w = 0.11 + 0.025 * Math.Sin(7 * (u + 2 * v)) + 0.015 * Math.Sin(13 * (v - u));
                    int a = 0, c = 0;
                    if (d < w && r < 0.9) { a = 255; c = 238 - (int)(18 * Math.Abs(Math.Sin(17 * u + 11 * v))); }
                    else if (d < w + 0.07 && r < 0.95) { a = 120; c = 40; }
                    px[y * S + x] = ColorUtil.ToRgba(a, c, c, c);
                }
            var tex = new LoadedTexture(capi, 0, S, S);
            capi.Render.LoadOrUpdateTextureFromRgba(px, false, 1, ref tex);
            return tex;
        }

        LoadedTexture Label(int number)
        {
            if (labels.TryGetValue(number, out var t)) return t;
            var font = new CairoFont(48, GuiStyle.StandardFontName, new double[] { 1, 1, 1, 1 }, new double[] { 0, 0, 0, 1 }) { StrokeWidth = 4 };
            t = capi.Gui.TextTexture.GenUnscaledTextTexture(number.ToString(Inv), font);
            labels[number] = t;
            return t;
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            dialog?.Dispose();
            markTex?.Dispose();
            quad?.Dispose();
            foreach (var t in labels.Values) t?.Dispose();
            labels.Clear();
        }
    }
}
