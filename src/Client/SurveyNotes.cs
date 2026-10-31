using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Hansa
{
    /// <summary>
    /// A note written with the surveyor's chalk (<see cref="SurveyorsChalk"/>): what the player looked at and where, the
    /// state of the ship there, their comment and a screenshot. All of them are kept on the player's own disk, in
    /// ModData/ships-notes.json (a JSON array, every number) and ModData/ships-notes.md (a readable list), for Claude
    /// to read when diagnosing a report. Plain data, no game API: the offline checks build, save, read and print them.
    /// Units: blocks (1 block = 1 m) and degrees. A ship-local point is in her design's frame (assets/ships/config/ships):
    /// x toward the bow, y up from her waterline, z to starboard.
    /// </summary>
    public class SurveyNote
    {
        /// <summary>1, 2, 3...: one more than the highest kept (a deleted number comes back only if it was the last).</summary>
        public int Number;
        /// <summary>The real date and time it was written: local ("2026-10-02 21:14:05"), and UTC (ISO 8601).</summary>
        public string Time, TimeUtc;
        /// <summary>The game's calendar then: its date as the game writes it, and the total hours since the world began.</summary>
        public string GameDate;
        public double GameHours;
        /// <summary>The versions: Ships, Sea and the game.</summary>
        public string ShipsVersion, Sea, Game;
        /// <summary>The world (its savegame identifier): a note's mark shows only in its own world.</summary>
        public string World;
        public string Comment = "";
        public NoteTarget Target = new();
        public NotePlayer Player;
        /// <summary>The ship the player was aboard and the one they looked at (one entry if it's the same).</summary>
        public List<NoteShip> Ships = new();
        /// <summary>Where its chalk mark is.</summary>
        public NoteMark Mark;
        public NoteScreenshot Screenshot;
    }

    /// <summary>What the crosshair was on.</summary>
    public class NoteTarget
    {
        /// <summary>ship, block, entity or nothing.</summary>
        public string Kind = "nothing";
        /// <summary>One line: "Carrack "Santa Maria" #6743: Hull, port side, amidships, below the waterline (frames 12–13)".</summary>
        public string Summary;
        public NoteShipHit Ship;
        public NoteBlockHit Block;
        public NoteEntityHit Entity;
    }

    /// <summary>A ship under the crosshair: which, the part of her (as the look-at box says it), where it was hit.</summary>
    public class NoteShipHit
    {
        public string Code, Name, Design;
        public long Id;
        /// <summary>The part as the look-at box names it, its selection box index (<see cref="ShipPart"/>) and what that decodes to.</summary>
        public string Part;
        public int PartIndex;
        public string PartCode;
        /// <summary>The fixture there (its code in the design, its type, its name), if it's on one.</summary>
        public string Fixture, FixtureType, FixtureName;
        /// <summary>Where it was hit: ship-local, the face's outward normal ship-local, and world.</summary>
        public double[] Local, Normal, World;
        /// <summary>The face (world: north, east...), and how far from the eye.</summary>
        public string Face;
        public double Distance;
        /// <summary>That part's state in the damage model (at a mast: its rigging and sails too; at the helm: the rudder).</summary>
        public List<NotePartState> Damage;
        /// <summary>The look-at box as it read (her name, then its lines).</summary>
        public string LookAtBox;
    }

    public class NotePartState
    {
        public string Part;
        public int Index;
        public string State;
        /// <summary>How hurt (0..1) and how worn its seams (0..1); roughly what it lets in (litres a second).</summary>
        public double Hurt, Wear, Leak;
    }

    public class NoteBlockHit
    {
        public string Code, Name;
        public int[] Pos;
        public int Dimension;
        public string Face;
        public double[] Hit;
        public double Distance;
        /// <summary>What the look-at box says of it.</summary>
        public string Info;
    }

    public class NoteEntityHit
    {
        public string Code, Name;
        public long Id;
        public double[] Pos, Hit;
        public double Distance;
    }

    /// <summary>The player: where they stood and looked (world), and where aboard.</summary>
    public class NotePlayer
    {
        public string Name;
        /// <summary>Feet and eye (world); the view's unit direction (world).</summary>
        public double[] Pos, Eye, Look;
        public int Dimension;
        /// <summary>The view: compass heading (0 north, 90 east) and how far up from level (degrees).</summary>
        public double Heading, Elevation;
        public string GameMode, Camera;
        public NoteAboard Aboard;
    }

    /// <summary>Aboard a ship: on which seat, where in her (ship-local), on which deck, looking which way in her frame.</summary>
    public class NoteAboard
    {
        public long ShipId;
        /// <summary>deck (walking), helm, gun:gun-port-1, bunk:..., row:...; and climbing a ladder or the shrouds (its code).</summary>
        public string Seat, Climbing;
        public double[] Local, EyeLocal, LookLocal;
        /// <summary>The view in her frame: degrees off the bow (+ to starboard, - to port, ±180 astern) and up from her deck's level.</summary>
        public double Bearing, Elevation;
        public string Deck, Room;
    }

    /// <summary>A ship's state then (the one aboard, the one looked at).</summary>
    public class NoteShip
    {
        /// <summary>aboard, target, or "aboard, target".</summary>
        public string Role;
        public string Code, Name, Design;
        public long Id;
        public double[] Pos;
        /// <summary>Compass heading (degrees); speed (blocks a second, knots); the sails (sweeps, furled, half, full) and how far set (0..1).</summary>
        public double Heading, Speed, Knots;
        public string Sails;
        public double SailFraction, Rudder;
        public bool Anchored, Aground;
        public NoteWind Wind;
        public NoteWaves Waves;
        /// <summary>
        /// As drawn on this screen (scaled by the player's Sea motion setting, SeaMotion): heave (blocks), pitch (+ bow up),
        /// roll (+ starboard down; with the heel and the list in it), heel (the lean in the wind), degrees; and as she
        /// really lies (TruePitch, TrueRoll).
        /// </summary>
        public double Heave, Pitch, Roll, Heel, TruePitch, TrueRoll, SeaMotion;
        /// <summary>Water in her hold (tonnes) and its rate (litres a second, + rising); list and trim from it (degrees); sinkage (blocks).</summary>
        public double Water, WaterRate, List, Trim, Sinkage;
        /// <summary>Afloat, Foundering, Sinking, Sunk.</summary>
        public string Stage;
        public int Pumpers;
        public double Fouling;
        public bool OnBottom, Clad;
        /// <summary>Her parts that aren't sound ("Main course: torn"), the first 40.</summary>
        public List<string> Hurt;
    }

    public class NoteWind
    {
        /// <summary>Sea's strength (1 = 25 m/s), in metres a second, its Beaufort force and name, where it blows from (compass degrees).</summary>
        public double Strength, Speed;
        public int Force;
        public string Name;
        public double From;
    }

    public class NoteWaves
    {
        /// <summary>Significant height (blocks) and period (seconds) of the sea there, the share of the open sea that reaches it, and the drawn surface's height there now.</summary>
        public double Height, Period, Factor, Surface;
    }

    /// <summary>
    /// Where a note's chalk mark is: on a ship (ShipId not 0) at a ship-local point, moving and tilting with her; else in
    /// the world, where it stays. Normal: the surface's outward normal (ship-local on a ship, world otherwise).
    /// </summary>
    public class NoteMark
    {
        public long ShipId;
        public string ShipCode;
        public double[] Local, World, Normal;
        public int Dimension;
    }

    public class NoteScreenshot
    {
        /// <summary>The file's name and full path, when it was saved, and: taken, not found, or no screenshot key.</summary>
        public string File, Path, Time, Status;
    }

    /// <summary>A ship's pose as she's drawn: her origin (world), her heading (vanilla yaw) and her drawn pitch and roll (radians).</summary>
    public readonly struct NotePose
    {
        public readonly double X, Y, Z, Yaw, Pitch, Roll;

        public NotePose(double x, double y, double z, double yaw, double pitch, double roll)
        {
            X = x; Y = y; Z = z; Yaw = yaw; Pitch = pitch; Roll = roll;
        }
    }

    /// <summary>The frame maths of a note: ship-local and world points and directions, headings, and where a mark is drawn.</summary>
    public static class NoteMath
    {
        public static double[] ToWorld(NotePose p, double lx, double ly, double lz)
        {
            ShipFrame.ToWorld(p.Yaw, p.Pitch, p.Roll, lx, ly, lz, out double x, out double y, out double z);
            return new[] { p.X + x, p.Y + y, p.Z + z };
        }

        public static double[] ToLocal(NotePose p, double wx, double wy, double wz)
        {
            ShipFrame.ToLocal(p.Yaw, p.Pitch, p.Roll, wx - p.X, wy - p.Y, wz - p.Z, out double x, out double y, out double z);
            return new[] { x, y, z };
        }

        public static double[] DirToWorld(NotePose p, double lx, double ly, double lz)
        {
            ShipFrame.ToWorld(p.Yaw, p.Pitch, p.Roll, lx, ly, lz, out double x, out double y, out double z);
            return new[] { x, y, z };
        }

        public static double[] DirToLocal(NotePose p, double dx, double dy, double dz)
        {
            ShipFrame.ToLocal(p.Yaw, p.Pitch, p.Roll, dx, dy, dz, out double x, out double y, out double z);
            return new[] { x, y, z };
        }

        /// <summary>Compass heading of a world direction: 0 north (-z), 90 east (+x).</summary>
        public static double Heading(double dx, double dz)
        {
            if (Math.Abs(dx) < 1e-12 && Math.Abs(dz) < 1e-12) return 0;
            double h = Math.Atan2(dx, -dz) * 180 / Math.PI;
            return (h % 360 + 360) % 360;
        }

        /// <summary>How far a direction points up from level (degrees).</summary>
        public static double Elevation(double dx, double dy, double dz) => Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI;

        /// <summary>A ship-local direction's angle off her bow (degrees, + to starboard, - to port, ±180 astern).</summary>
        public static double Bearing(double lx, double lz) => Math.Abs(lx) < 1e-12 && Math.Abs(lz) < 1e-12 ? 0 : Math.Atan2(lz, lx) * 180 / Math.PI;

        static readonly string[] Points = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        public static string CompassPoint(double heading) => Points[(int)Math.Round(((heading % 360 + 360) % 360) / 22.5) % 16];

        /// <summary>"dead ahead", "30° to starboard of the bow", "on the port beam", "20° to port of the stern", "dead astern".</summary>
        public static string BearingText(double bearing)
        {
            double a = Math.Abs(bearing);
            string side = bearing > 0 ? "starboard" : "port";
            if (a < 2) return "dead ahead";
            if (a > 178) return "dead astern";
            if (Math.Abs(a - 90) < 2) return $"on the {side} beam";
            return a < 90 ? F($"{a:0}° to {side} of the bow") : F($"{180 - a:0}° to {side} of the stern");
        }

        /// <summary>A vector rounded to this many decimals (for the file: readable, and finer than anything drawn).</summary>
        public static double[] V(double x, double y, double z, int digits = 3) => new[] { Math.Round(x, digits), Math.Round(y, digits), Math.Round(z, digits) };

        public static double[] Unit(double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z);
            return l < 1e-12 ? new[] { 0.0, 1, 0 } : new[] { x / l, y / l, z / l };
        }

        /// <summary>
        /// Two unit directions across a surface with this normal (the chalk mark's strokes lie along their diagonals):
        /// level ones on a wall, along the frame's x and z on a floor or a ceiling.
        /// </summary>
        public static void Across(double[] n, out double[] t1, out double[] t2)
        {
            var u = Unit(n[0], n[1], n[2]);
            // a reference not along the normal: up for a wall, x for a floor
            double[] r = Math.Abs(u[1]) < 0.9 ? new[] { 0.0, 1, 0 } : new[] { 1.0, 0, 0 };
            t1 = Unit(r[1] * u[2] - r[2] * u[1], r[2] * u[0] - r[0] * u[2], r[0] * u[1] - r[1] * u[0]);
            t2 = new[] { u[1] * t1[2] - u[2] * t1[1], u[2] * t1[0] - u[0] * t1[2], u[0] * t1[1] - u[1] * t1[0] };
        }

        /// <summary>
        /// Where a ray met a ship (<see cref="ShipParts.Ray"/>), as a note keeps it: the point ship-local and in the world, and
        /// the outward normal of the face it met, ship-local.
        /// </summary>
        public static void FromHit(ShipRayHit hit, out double[] local, out double[] normal, out double[] world)
        {
            local = hit.Local == null ? null : V(hit.Local.X, hit.Local.Y, hit.Local.Z);
            world = hit.World == null ? null : V(hit.World.X, hit.World.Y, hit.World.Z);
            normal = hit.LocalNormal == null ? null : V(hit.LocalNormal.X, hit.LocalNormal.Y, hit.LocalNormal.Z, 4);
        }

        /// <summary>How far a mark is drawn off its surface, so it isn't lost in it.</summary>
        public const double MarkLift = 0.02;

        /// <summary>
        /// Where a mark on a ship is drawn now (world): her ship-local point lifted off the surface along its normal, and her
        /// pose as she's drawn. The same sums as the renderer's model matrix (ShipRenderer.SetModel).
        /// </summary>
        public static double[] ShipMarkWorld(NotePose p, double[] local, double[] normal)
        {
            var n = Unit(normal[0], normal[1], normal[2]);
            return ToWorld(p, local[0] + n[0] * MarkLift, local[1] + n[1] * MarkLift, local[2] + n[2] * MarkLift);
        }

        /// <summary>How far a point is from a ray (eye, unit dir), and how far along it (negative: behind the eye).</summary>
        public static double OffRay(double[] eye, double[] dir, double[] p, out double along)
        {
            double vx = p[0] - eye[0], vy = p[1] - eye[1], vz = p[2] - eye[2];
            along = vx * dir[0] + vy * dir[1] + vz * dir[2];
            double ox = vx - dir[0] * along, oy = vy - dir[1] * along, oz = vz - dir[2] * along;
            return Math.Sqrt(ox * ox + oy * oy + oz * oz);
        }

        /// <summary>How near the crosshair passes a mark for it to count as looked at: a little more further off.</summary>
        public static double PickRadius(double along) => 0.22 + 0.012 * Math.Max(0, along);

        static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The notes' files: the JSON array (read and written whole) and the readable list next to it.</summary>
    public static class SurveyNoteFile
    {
        public const string JsonName = "ships-notes.json", MarkdownName = "ships-notes.md";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            Converters = { new InlineArrayConverter() },
            Culture = CultureInfo.InvariantCulture,
        };

        public static string ToJson(List<SurveyNote> notes) => JsonConvert.SerializeObject(notes ?? new List<SurveyNote>(), Settings);

        public static List<SurveyNote> FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<SurveyNote>();
            return JsonConvert.DeserializeObject<List<SurveyNote>>(json, Settings) ?? new List<SurveyNote>();
        }

        /// <summary>The notes in a file: none if it isn't there; null and why if it can't be read (it's left as it is).</summary>
        public static List<SurveyNote> Load(string jsonPath, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(jsonPath)) return new List<SurveyNote>();
                var list = FromJson(File.ReadAllText(jsonPath, Encoding.UTF8));
                list.RemoveAll(n => n == null);
                return list;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>Both files: the JSON array (through a temporary file, so a crash never leaves half of it) and the readable list.</summary>
        public static void Save(string dir, List<SurveyNote> notes)
        {
            Directory.CreateDirectory(dir);
            string json = Path.Combine(dir, JsonName), tmp = json + ".tmp";
            File.WriteAllText(tmp, ToJson(notes), new UTF8Encoding(false));
            if (File.Exists(json)) File.Replace(tmp, json, null);
            else File.Move(tmp, json);
            File.WriteAllText(Path.Combine(dir, MarkdownName), Markdown(notes), new UTF8Encoding(false));
        }

        /// <summary>The next note's number: one more than the highest kept.</summary>
        public static int NextNumber(List<SurveyNote> notes) => (notes == null || notes.Count == 0 ? 0 : notes.Max(n => n.Number)) + 1;

        /// <summary>The comment's first few words ("" if none), with "…" if there's more.</summary>
        public static string FirstWords(string comment, int words = 8)
        {
            if (string.IsNullOrWhiteSpace(comment)) return "";
            var w = comment.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", w.Take(words)) + (w.Length > words ? " …" : "");
        }

        /// <summary>A line of the list (.ships notes): "#3 · 21:14 · Carrack #6743: Hull, port side... · "the first words…"".</summary>
        public static string ListLine(SurveyNote n)
        {
            string time = n.Time != null && n.Time.Length >= 16 ? n.Time.Substring(11, 5) : n.Time ?? "";
            string words = FirstWords(n.Comment);
            return $"#{n.Number} · {time} · {n.Target?.Summary ?? "?"}" + (words.Length > 0 ? $" · \"{words}\"" : "");
        }

        /// <summary>A note in chat (left-click its mark): its number, time, what it's about, the comment, the screenshot.</summary>
        public static string ChatText(SurveyNote n)
        {
            var sb = new StringBuilder();
            sb.Append($"Note {n.Number} ({n.Time}): {n.Target?.Summary ?? "?"}");
            if (!string.IsNullOrWhiteSpace(n.Comment)) sb.Append("\n\"" + n.Comment.Trim() + "\"");
            if (n.Screenshot?.File != null) sb.Append("\nScreenshot: " + n.Screenshot.File);
            return sb.ToString();
        }

        static string P(double[] v, string fmt = "0.000") =>
            v == null || v.Length < 3 ? "?" : "(" + string.Join(", ", v.Take(3).Select(x => x.ToString(fmt, Inv))) + ")";

        static string N(double v, string fmt = "0.00") => v.ToString(fmt, Inv);

        /// <summary>The readable list: each note's number, time, target, comment, screenshot and the key numbers.</summary>
        public static string Markdown(List<SurveyNote> notes)
        {
            var sb = new StringBuilder();
            notes ??= new List<SurveyNote>();
            sb.AppendLine("# Ships notes");
            sb.AppendLine();
            sb.AppendLine($"{notes.Count} note{(notes.Count == 1 ? "" : "s")}, written with the surveyor's chalk. Every number of each is in " +
                          $"{JsonName} next to this file.");
            sb.AppendLine("Units: blocks (a block is a metre) and degrees. Ship-local points are in her design's frame: x toward the bow, " +
                          "y up from her waterline, z to starboard.");
            foreach (var n in notes.OrderBy(x => x.Number))
            {
                sb.AppendLine();
                sb.AppendLine($"## {n.Number} · {n.Time} · {n.Target?.Summary ?? "?"}");
                sb.AppendLine();
                if (string.IsNullOrWhiteSpace(n.Comment)) sb.AppendLine("(no comment)");
                else foreach (var line in n.Comment.Trim().Replace("\r", "").Split('\n')) sb.AppendLine("> " + line);
                sb.AppendLine();
                var s = n.Screenshot;
                sb.AppendLine("- **Screenshot:** " + (s?.File != null ? $"{s.File} ({s.Path})" : s?.Status != null ? s.Status : "none (match one by the time)"));
                foreach (var (label, text) in Details(n, false)) sb.AppendLine($"- **{label}:** {text}");
            }
            return sb.ToString();
        }

        /// <summary>The key numbers of a note, a line each (labelled): the target, its damage, the player, the ships, when.</summary>
        public static List<(string Label, string Text)> Details(SurveyNote n, bool withSummary = true)
        {
            var lines = new List<(string, string)>();
            lines.Add(("Target", (!withSummary || n.Target?.Kind == "nothing" ? "" : (n.Target?.Summary ?? "?") + "; ") + TargetLine(n.Target)));
            var dmg = n.Target?.Ship?.Damage;
            if (dmg != null && dmg.Count > 0)
                lines.Add(("Damage there", string.Join("; ", dmg.Select(d =>
                    $"{d.Part}: {d.State} (hurt {N(d.Hurt)}, wear {N(d.Wear)}{(d.Leak >= 0.05 ? $", ~{N(d.Leak, "0.0")} l/s" : "")})"))));
            if (n.Player != null) lines.Add(("Player", PlayerLine(n.Player)));
            foreach (var ship in n.Ships ?? new List<NoteShip>()) lines.Add(($"Ship #{ship.Id} ({ship.Role})", ShipLine(ship)));
            lines.Add(("When", $"{n.Time}; game time {n.GameDate} ({N(n.GameHours, "0.0")} h); Ships {n.ShipsVersion}, Sea {n.Sea}, game {n.Game}"));
            return lines;
        }

        /// <summary>A note's key numbers as plain lines ("Target: ..."): what the chalk's window shows of it.</summary>
        public static string Lines(SurveyNote n) => string.Join("\n", Details(n).Select(l => $"{l.Label}: {l.Text}"));

        static string TargetLine(NoteTarget t)
        {
            if (t == null) return "?";
            switch (t.Kind)
            {
                case "ship" when t.Ship != null:
                {
                    var h = t.Ship;
                    string s = $"ship-local {P(h.Local)}, world {P(h.World, "0.00")}, face {h.Face}, {N(h.Distance, "0.0")} away; part index {h.PartIndex} ({h.PartCode})";
                    if (h.Fixture != null) s += $"; fixture {h.Fixture} ({h.FixtureType})";
                    return s;
                }
                case "block" when t.Block != null:
                {
                    var b = t.Block;
                    return $"block {b.Code} at {(b.Pos != null ? string.Join(",", b.Pos) : "?")}, face {b.Face}, hit {P(b.Hit, "0.00")}, {N(b.Distance, "0.0")} away";
                }
                case "entity" when t.Entity != null:
                {
                    var e = t.Entity;
                    return $"entity {e.Code} #{e.Id} at {P(e.Pos, "0.00")}, hit {P(e.Hit, "0.00")}, {N(e.Distance, "0.0")} away";
                }
                default:
                    return t.Summary ?? "nothing";
            }
        }

        static string PlayerLine(NotePlayer p)
        {
            string s = "";
            if (p.Aboard != null)
            {
                var a = p.Aboard;
                s = $"aboard #{a.ShipId} ({a.Seat}{(a.Climbing != null ? ", climbing " + a.Climbing : "")}) at ship-local {P(a.Local, "0.00")}" +
                    (a.Deck != null ? $", {a.Deck} deck" : "") + (a.Room != null ? $", in {a.Room}" : "") +
                    $"; looking {NoteMath.BearingText(a.Bearing)}, {N(Math.Abs(a.Elevation), "0")}° {(a.Elevation >= 0 ? "up" : "down")}; ";
            }
            return s + $"world {P(p.Pos, "0.00")}, heading {N(p.Heading, "0")}° ({NoteMath.CompassPoint(p.Heading)}), " +
                   $"{N(Math.Abs(p.Elevation), "0")}° {(p.Elevation >= 0 ? "up" : "down")}";
        }

        static string ShipLine(NoteShip s)
        {
            var sb = new StringBuilder();
            sb.Append($"{N(s.Speed)} b/s ({N(s.Knots, "0.0")} kn), heading {N(s.Heading, "0")}° ({NoteMath.CompassPoint(s.Heading)}), sails {s.Sails}, " +
                      (s.Anchored ? "anchored" : "anchor up") + (s.Aground ? ", aground" : ""));
            if (s.Wind != null) sb.Append($"; wind force {s.Wind.Force} ({s.Wind.Name}) from {N(s.Wind.From, "0")}°");
            if (s.Waves != null) sb.Append($"; waves {N(s.Waves.Height)} significant ({N(s.Waves.Period, "0.0")} s)");
            sb.Append($"; drawn heave {N(s.Heave)}, pitch {N(s.Pitch, "0.0")}°, roll {N(s.Roll, "0.0")}°, heel {N(s.Heel, "0.0")}°");
            sb.Append($"; water {N(s.Water, "0.0")} t ({(s.WaterRate >= 0 ? "+" : "")}{N(s.WaterRate, "0.0")} l/s), list {N(s.List, "0.0")}°, sinkage {N(s.Sinkage)}, {s.Stage}");
            if (s.Hurt != null && s.Hurt.Count > 0) sb.Append($"; {s.Hurt.Count} part{(s.Hurt.Count == 1 ? "" : "s")} hurt");
            return sb.ToString();
        }
    }

    /// <summary>Writes number arrays on one line ("[1.25, -3.5, 0.0]"), so the file stays readable.</summary>
    public class InlineArrayConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(double[]) || t == typeof(int[]);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var inv = CultureInfo.InvariantCulture;
            string body = value switch
            {
                double[] d => string.Join(", ", d.Select(x => double.IsFinite(x) ? x.ToString("R", inv) : "0")),
                int[] i => string.Join(", ", i.Select(x => x.ToString(inv))),
                _ => ""
            };
            writer.WriteRawValue("[" + body + "]");
        }

        public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.Null) return null;
            return t == typeof(int[]) ? token.Values<int>().ToArray() : token.Values<double>().ToArray();
        }
    }
}
