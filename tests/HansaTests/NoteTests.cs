using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// The surveyor's chalk (0.3.6): a note's frame maths (a known point on her hull at a tilted pose: where the ray
        /// meets her, ship-local and world, as the renderer draws it, the mark lifted off it, and the view in her frame); the
        /// note's file round trip (JSON array, numbering, delete, a broken file, the hand-made sample tools/shipgen reads);
        /// the readable list.
        /// </summary>
        static void SurveyNotes(ShipDefinition def, string root)
        {
            Console.WriteLine("the surveyor's chalk: a note's frame maths, its file, the readable list");
            const double D = Math.PI / 180;
            var pose = new NotePose(1203.25, 112.5, -540.75, 37 * D, 4.5 * D, -11 * D);

            // ---- a known point at a tilted pose: world and back, and where the renderer's model matrix puts it
            {
                double[] p = { 3.25, -1.5, -4.85 };
                var w = NoteMath.ToWorld(pose, p[0], p[1], p[2]);
                var back = NoteMath.ToLocal(pose, w[0], w[1], w[2]);
                var model = new Matrixf().Identity().RotateY(ShipFrame.ModelYaw(pose.Yaw)).RotateZ((float)pose.Pitch).RotateX((float)pose.Roll);
                var m = model.TransformVector(new Vec4f((float)p[0], (float)p[1], (float)p[2], 1));
                double drawn = Math.Sqrt(Math.Pow(pose.X + m.X - w[0], 2) + Math.Pow(pose.Y + m.Y - w[1], 2) + Math.Pow(pose.Z + m.Z - w[2], 2));
                double rt = Math.Sqrt(Math.Pow(back[0] - p[0], 2) + Math.Pow(back[1] - p[1], 2) + Math.Pow(back[2] - p[2], 2));
                Check(drawn < 1e-4 && rt < 1e-9, $"a ship-local point at a tilted pose (yaw 37°, pitch 4.5°, roll -11°): the note's world point is where the renderer draws it " +
                    $"(off by {drawn:0.0e0}) and comes back to her frame (off by {rt:0.0e0})");
                // a level ship heading north (yaw π): her bow points north, her starboard beam east
                var north = new NotePose(0, 0, 0, Math.PI, 0, 0);
                var bow = NoteMath.DirToWorld(north, 1, 0, 0);
                var star = NoteMath.DirToWorld(north, 0, 0, 1);
                Check(Math.Abs(NoteMath.Heading(bow[0], bow[2])) < 1e-9 && Math.Abs(NoteMath.Heading(star[0], star[2]) - 90) < 1e-9,
                    $"headings: a ship heading north (yaw π) has her bow at 0° and her starboard beam at 90° (east)");
            }

            // ---- a ray at her starboard side, the same in her frame and tilted: the note's point, normal and mark
            {
                var g = def.Grid;
                double y = def.Waterline + 0.6;
                ShipParts.Ray(def, g, 0, 0, 0, Math.PI / 2, 0, 0, new Vec3d(2.0, y, 20), new Vec3d(0, 0, -1), 30, f => f.Type != "attach", f => false, f => null, out var level);
                var eye = NoteMath.ToWorld(pose, 2.0, y, 20);
                var dir = NoteMath.DirToWorld(pose, 0, 0, -1);
                bool got = ShipParts.Ray(def, g, pose.X, pose.Y, pose.Z, pose.Yaw, pose.Pitch, pose.Roll, new Vec3d(eye[0], eye[1], eye[2]),
                    new Vec3d(dir[0] * 5, dir[1] * 5, dir[2] * 5), 30, f => f.Type != "attach", f => false, f => null, out var hit);
                NoteMath.FromHit(hit, out var local, out var normal, out var world);
                bool onHull = got && level.Local != null && hit.Part.Kind == ShipPartKind.Hull && hit.Part.Side == 1;
                double dl = onHull ? Math.Sqrt(Math.Pow(local[0] - level.Local.X, 2) + Math.Pow(local[1] - level.Local.Y, 2) + Math.Pow(local[2] - level.Local.Z, 2)) : 99;
                var expect = onHull ? NoteMath.ToWorld(pose, local[0], local[1], local[2]) : new double[3];
                double dw = onHull ? Math.Sqrt(Math.Pow(world[0] - expect[0], 2) + Math.Pow(world[1] - expect[1], 2) + Math.Pow(world[2] - expect[2], 2)) : 99;
                Check(onHull && dl < 0.002 && dw < 0.003 && normal != null && Math.Abs(normal[2] - 1) < 1e-6,
                    $"her starboard topsides at that pose: the note keeps the same ship-local point as level ({(onHull ? $"{local[0]:0.000}, {local[1]:0.000}, {local[2]:0.000}" : "missed")}, " +
                    $"off by {dl:0.0000}), the world point at the pose (off by {dw:0.0000}), the face's normal to starboard");
                var nw = normal == null ? new double[3] : NoteMath.DirToWorld(pose, normal[0], normal[1], normal[2]);
                Check(onHull && hit.Face == BlockFacing.FromVector(nw[0], nw[1], nw[2]),
                    $"the world face ({hit.Face}) is her starboard normal at the pose ({nw[0]:0.00}, {nw[1]:0.00}, {nw[2]:0.00}) to the nearest axis");
                // the mark: lifted off the hull along its normal, and in the crosshair of the eye that made the note
                var at = onHull ? NoteMath.ShipMarkWorld(pose, local, normal) : new double[3];
                double lift = Math.Sqrt(Math.Pow(at[0] - world[0], 2) + Math.Pow(at[1] - world[1], 2) + Math.Pow(at[2] - world[2], 2));
                double along = (at[0] - world[0]) * nw[0] + (at[1] - world[1]) * nw[1] + (at[2] - world[2]) * nw[2];
                double off = NoteMath.OffRay(eye, dir, at, out double t);
                Check(onHull && Math.Abs(lift - NoteMath.MarkLift) < 0.001 && Math.Abs(along - NoteMath.MarkLift) < 0.001 && off <= NoteMath.PickRadius(t) && Math.Abs(t - hit.Distance) < 0.05,
                    $"its chalk mark is drawn {lift:0.000} off her side along its normal, and lies in the crosshair it was made from ({off:0.000} off the ray at {t:0.00})");
                NoteMath.Across(normal ?? new[] { 0.0, 0, 1 }, out var a1, out var a2);
                double dots = Math.Abs(a1[0] * a2[0] + a1[1] * a2[1] + a1[2] * a2[2]) + Math.Abs(a1[0] * (normal?[0] ?? 0) + a1[1] * (normal?[1] ?? 0) + a1[2] * (normal?[2] ?? 1));
                NoteMath.Across(new[] { 0.0, 1, 0 }, out var f1, out var f2);
                Check(dots < 1e-9 && Math.Abs(a1[1]) < 1e-9 && Math.Abs(f1[1]) < 1e-9 && Math.Abs(f2[1]) < 1e-9,
                    "the mark's strokes lie across its surface: level ones on her side, flat on a deck");
                // a mark beside it is out of the crosshair
                double away = NoteMath.OffRay(eye, dir, NoteMath.ToWorld(pose, local[0] + 0.6, local[1], local[2] + 0.02), out double t2);
                Check(away > NoteMath.PickRadius(t2), $"a mark 0.6 along her side from where you look isn't in the crosshair ({away:0.00} off it)");
            }

            // ---- the view in her frame: toward her bow, her starboard beam, astern, and up
            {
                var b0 = NoteMath.DirToWorld(pose, 1, 0, 0);
                var b1 = NoteMath.DirToWorld(pose, 0, 0, 1);
                var b2 = NoteMath.DirToWorld(pose, -1, 0, -0.0001);
                var b3 = NoteMath.DirToWorld(pose, Math.Cos(20 * D), Math.Sin(20 * D), 0);
                double Bear(double[] w) { var l = NoteMath.DirToLocal(pose, w[0], w[1], w[2]); return NoteMath.Bearing(l[0], l[2]); }
                double Elev(double[] w) { var l = NoteMath.DirToLocal(pose, w[0], w[1], w[2]); return NoteMath.Elevation(l[0], l[1], l[2]); }
                Check(Math.Abs(Bear(b0)) < 1e-6 && Math.Abs(Bear(b1) - 90) < 1e-6 && Math.Abs(Math.Abs(Bear(b2)) - 180) < 0.01 && Math.Abs(Elev(b3) - 20) < 1e-6 &&
                      NoteMath.BearingText(Bear(b1)) == "on the starboard beam" && NoteMath.BearingText(-30) == "30° to port of the bow",
                    $"the view in her frame, tilted: her bow 0°, starboard beam +90° (\"{NoteMath.BearingText(Bear(b1))}\"), astern ±180°, 20° up stays 20° up");
            }

            // ---- the file: the JSON array round trip, numbering, deleting, a broken file
            var a = SampleNote(1, "the gun port's sill looks too high from outside\nsecond line");
            var b = SampleNote(2, "stairs: I fall through here");
            b.Target = new NoteTarget
            {
                Kind = "block", Summary = "Granite (game:rock-granite) at 512,110,-300, north face",
                Block = new NoteBlockHit { Code = "game:rock-granite", Name = "Granite", Pos = new[] { 512, 110, -300 }, Face = "north", Hit = new[] { 512.5, 110.25, -300.0 }, Distance = 3.2 }
            };
            b.Mark = new NoteMark { World = new[] { 512.5, 110.25, -300.0 }, Normal = new[] { 0.0, 0, -1 } };
            b.Screenshot = null;
            var c = SampleNote(3, "");
            {
                var list = new List<SurveyNote> { a, b, c };
                string json = SurveyNoteFile.ToJson(list);
                var again = SurveyNoteFile.FromJson(json);
                string json2 = SurveyNoteFile.ToJson(again);
                var arr = JArray.Parse(json);
                var first = (JObject)arr[0];
                Check(json == json2 && again.Count == 3 && again[0].Target.Ship.Local.SequenceEqual(a.Target.Ship.Local) && again[0].Ships[0].Wind.Force == 6 &&
                      again[0].Mark.ShipId == 6743 && again[1].Target.Block.Pos.SequenceEqual(new[] { 512, 110, -300 }) && again[0].Comment == a.Comment &&
                      again[0].Screenshot.File == "2026-10-02_21-14-12.png" && again[0].Player.Aboard.Deck == "main",
                    "a note's JSON round-trips: written, read and written again it's the same, every field kept");
                Check(arr.Count == 3 && first["number"]?.Value<int>() == 1 && first["target"]?["ship"]?["local"] is JArray && first["mark"]?["shipId"]?.Value<long>() == 6743 &&
                      json.Contains("\"local\": [3.25, -1.5, -4.85]") && !json.Contains("\"entity\""),
                    "the file is a JSON array of objects, readable: camelCase keys, a point on one line ([3.25, -1.5, -4.85]), nothing written for what isn't there");

                string tmp = Path.Combine(Path.GetTempPath(), "hansa-notes-test-" + Guid.NewGuid().ToString("N"));
                try
                {
                    string jp = Path.Combine(tmp, SurveyNoteFile.JsonName);
                    var none = SurveyNoteFile.Load(jp, out string e0);
                    SurveyNoteFile.Save(tmp, list);
                    var loaded = SurveyNoteFile.Load(jp, out string e1);
                    loaded.RemoveAll(n => n.Number == 2);
                    SurveyNoteFile.Save(tmp, loaded);
                    var after = SurveyNoteFile.Load(jp, out _);
                    int next = SurveyNoteFile.NextNumber(after);
                    string md = File.ReadAllText(Path.Combine(tmp, SurveyNoteFile.MarkdownName)).Replace("\r\n", "\n");
                    Check(none != null && none.Count == 0 && e0 == null && e1 == null && loaded.Count == 2 && after.Select(n => n.Number).SequenceEqual(new[] { 1, 3 }) && next == 4 &&
                          !File.Exists(jp + ".tmp"),
                        $"saved and read back from disk; note 2 deleted leaves 1 and 3, and the next is {next} (a missing file is no notes)");
                    Check(md.Contains("## 1 · 2026-10-02 21:14:05 · Carrack \"Santa Maria\" #6743: Hull, starboard side, amidships, topsides (frames 15–16)") &&
                          md.Contains("> the gun port's sill looks too high from outside\n> second line") && !md.Contains("## 2 ·") && md.Contains("## 3 ·") &&
                          md.Contains("(no comment)") && md.Contains("2026-10-02_21-14-12.png") && md.Contains("ship-local (3.250, -1.500, -4.850)") &&
                          md.Contains("water 12.0 t (+3.0 l/s)") && md.Contains("wind force 6 (strong breeze) from 260°") && md.Contains("looking 30° to starboard of the bow, 12° down") &&
                          md.Contains("Cracked (hurt 0.42, wear 0.10, ~1.0 l/s)") && md.StartsWith("# Hansa notes"),
                        "the readable list: each note's number, time and target, the comment quoted, the screenshot, the key numbers (her frame, the water, the wind, the view, the damage)");
                    File.WriteAllText(jp, "[ { \"number\": 1, ");
                    var broken = SurveyNoteFile.Load(jp, out string e2);
                    Check(broken == null && e2 != null && File.Exists(jp), "a file that can't be read is said so and left as it is");
                }
                finally
                {
                    try { Directory.Delete(tmp, true); } catch (Exception) { }
                }
                string line = SurveyNoteFile.ListLine(a), chat = SurveyNoteFile.ChatText(a);
                Check(line.StartsWith("#1 · 21:14 · Carrack \"Santa Maria\" #6743: Hull") && line.EndsWith("\"the gun port's sill looks too high from …\"") &&
                      chat.Contains("Screenshot: 2026-10-02_21-14-12.png") && SurveyNoteFile.ListLine(c).EndsWith("(frames 15–16)"),
                    $"the list line (.hansa notes) and the note in chat: {line}");
            }

            // ---- the hand-made sample that tools/shipgen/view.py notes reads
            {
                string sample = Path.Combine(root, "tools", "shipgen", "notes-sample.json");
                var s = File.Exists(sample) ? SurveyNoteFile.Load(sample, out _) : null;
                Check(s != null && s.Count >= 3 && s.All(n => n.Number > 0 && n.Target != null && n.Mark != null) && s.Any(n => n.Mark.ShipId != 0 && n.Mark.Local != null) &&
                      s.Any(n => n.Target.Kind == "block"),
                    $"the hand-made sample notes (tools/shipgen/notes-sample.json) read as notes ({s?.Count ?? 0}, on the carrack and in the world)");
            }
        }

        /// <summary>A note as the chalk writes one aboard a carrack, every section filled.</summary>
        static SurveyNote SampleNote(int number, string comment) => new SurveyNote
        {
            Number = number,
            Time = "2026-10-02 21:14:05", TimeUtc = "2026-10-02T19:14:05.123Z", GameDate = "12. May, Year 2, 14:00", GameHours = 1234.5,
            Hansa = "0.3.6", Sea = "0.3.3", Game = "1.22.7", World = "abc", Comment = comment,
            Target = new NoteTarget
            {
                Kind = "ship", Summary = "Carrack \"Santa Maria\" #6743: Hull, starboard side, amidships, topsides (frames 15–16)",
                Ship = new NoteShipHit
                {
                    Code = "carrack", Name = "Santa Maria", Design = "Carrack", Id = 6743, Part = "Hull, starboard side, amidships, topsides (frames 15–16)",
                    PartIndex = 16842754, PartCode = "Hull starboard station 14 band 4", Local = new[] { 3.25, -1.5, -4.85 }, Normal = new[] { 0.0, 0, 1 },
                    World = new[] { 1204.5, 111.2, -545.0 }, Face = "east", Distance = 4.2,
                    Damage = new List<NotePartState> { new() { Part = "Hull, starboard side", Index = 300, State = "Cracked", Hurt = 0.42, Wear = 0.1, Leak = 1.0 } },
                    LookAtBox = "Santa Maria\nCarrack\nHull, starboard side, amidships, topsides (frames 15–16)\nCracked",
                }
            },
            Player = new NotePlayer
            {
                Name = "andris", Pos = new[] { 1203.0, 114.5, -541.0 }, Eye = new[] { 1203.0, 116.1, -541.0 }, Look = new[] { 0.5, -0.2, -0.84 }, Heading = 149, Elevation = -12,
                GameMode = "Creative", Camera = "FirstPerson",
                Aboard = new NoteAboard { ShipId = 6743, Seat = "deck", Local = new[] { 2.0, 2.0, 1.0 }, EyeLocal = new[] { 2.0, 3.6, 1.0 }, LookLocal = new[] { 0.85, -0.2, 0.49 }, Bearing = 30, Elevation = -12, Deck = "main" },
            },
            Ships = new List<NoteShip>
            {
                new()
                {
                    Role = "aboard, target", Code = "carrack", Name = "Santa Maria", Id = 6743, Pos = new[] { 1203.25, 112.5, -540.75 }, Heading = 210, Speed = 3.2, Knots = 6.2,
                    Sails = "full", SailFraction = 1, Wind = new NoteWind { Strength = 0.5, Speed = 12.5, Force = 6, Name = "strong breeze", From = 260 },
                    Waves = new NoteWaves { Height = 2.1, Period = 7.5, Factor = 1, Surface = 0.3 }, Heave = 0.31, Pitch = 1.2, Roll = -3.4, Heel = -2.1,
                    Water = 12, WaterRate = 3, List = 1.2, Sinkage = 0.1, Stage = "Afloat", Hurt = new List<string> { "Hull, starboard side: Cracked" },
                }
            },
            Mark = new NoteMark { ShipId = 6743, ShipCode = "carrack", Local = new[] { 3.25, -1.5, -4.85 }, Normal = new[] { 0.0, 0, 1 }, World = new[] { 1204.5, 111.2, -545.0 } },
            Screenshot = new NoteScreenshot { File = "2026-10-02_21-14-12.png", Path = @"C:\Users\x\Pictures\Vintagestory\2026-10-02_21-14-12.png", Time = "2026-10-02 21:14:12", Status = "taken" },
        };
    }
}
