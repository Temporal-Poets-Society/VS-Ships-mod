using System;
using System.IO;
using Hansa;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HansaTests
{
    /// <summary>
    /// Offline checks: the carrack definition loads, frame math and the renderer's rotation agree, a player can walk
    /// the decks (stairs, doors, walls, the hatch) and go over the side, and the ship rides Sea's waves.
    ///   dotnet run --project tests/HansaTests
    /// </summary>
    static partial class Program
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static string FindRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "modinfo.json"))) d = d.Parent;
            return d?.FullName ?? throw new Exception("mod root not found");
        }

        static int Main(string[] args)
        {
            string root = FindRoot();
            if (Array.IndexOf(args, "motion") >= 0)
            {
                // a table of how she moves in each wind (not checks): dotnet run -c Release -- motion [code]
                string mcode = args.Length > 1 && args[^1] != "motion" ? args[^1] : "carrack";
                var mdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", mcode + ".json")));
                mdef.Init();
                MotionReport(mdef);
                return 0;
            }
            if (Array.IndexOf(args, "damage") >= 0)
            {
                // her damage parts, hydrostatics and how the water in her acts, and the damage checks alone:
                // dotnet run -c Release -- damage
                var ddef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                ddef.Init();
                DamageReport(ddef);
                Damage(ddef);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "sailflood") >= 0)
            {
                // a sound carrack at sea in each wind for ten minutes: how much water she takes over her side (not checks)
                var sdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                sdef.Init();
                SailFloodReport(sdef);
                return 0;
            }
            if (Array.IndexOf(args, "damage-views") >= 0)
            {
                // her hull with panels in each state, as the client draws it, for python tools/shipgen/texview.py damage
                var vdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                vdef.Init();
                DamageViews(vdef, Path.Combine(root, "assets", "hansa"));
                return 0;
            }
            if (Array.IndexOf(args, "dry") >= 0)
            {
                // the dry holds and particles in them alone (22 October 2026): dotnet run -- dry
                foreach (var dcode in new[] { "caravel", "hansa-carrack", "carrack" }) DryHold(root, dcode);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "inspect") >= 0) { Inspect(root, args.Length > Array.IndexOf(args, "inspect") + 1 ? args[Array.IndexOf(args, "inspect") + 1] : "caravel"); return 0; }
            if (Array.IndexOf(args, "steerage") >= 0) { CaravelSteerage(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "grid-pieces") >= 0) { GridPieces(root); return 0; }
            if (Array.IndexOf(args, "holes") >= 0) return GridHoles(root, true) == 0 ? 0 : 1;
            if (Array.IndexOf(args, "startpieces") >= 0)
            {
                // a design's start pieces and whether her layout has each one's place: dotnet run -- startpieces <code>
                string code = args[Array.IndexOf(args, "startpieces") + 1];
                var sd = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json")));
                sd.Init();
                foreach (var sp in sd.StartPieces ?? Array.Empty<StartPieceDef>())
                    Console.WriteLine($"  {sp.Mount}: {(sd.Mounts.Get(sp.Mount) is MountPoint m ? $"place at {m.X:0.##} {m.Y:0.##} {m.Z:0.##}" : "NO PLACE")} ({sp.Block})");
                return 0;
            }
            if (Array.IndexOf(args, "probe") >= 0)
            {
                int i = Array.IndexOf(args, "probe");
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                GridProbe(root, double.Parse(args[i + 1], ci), double.Parse(args[i + 2], ci), double.Parse(args[i + 3], ci));
                return 0;
            }
            if (Array.IndexOf(args, "export-grid") >= 0)
            {
                // the grid carrack's mesh and fittings as the game builds them, for tools/HansaPreview --skin:
                // dotnet run -- export-grid <dir>
                ExportGrid(root, args[Array.IndexOf(args, "export-grid") + 1]);
                return 0;
            }
            if (Array.IndexOf(args, "grid") >= 0)
            {
                // the grid carrack as the game builds her: dotnet run -- grid
                GridShip(root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "sails") >= 0)
            {
                // the sails' wear table and the sail checks alone (looking at them, mending them, the lateen's trim):
                // dotnet run -c Release -- sails
                var sdef2 = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                sdef2.Init();
                Sails(sdef2, Path.Combine(root, "assets", "hansa"));
                SailsPressed(sdef2, Path.Combine(root, "assets", "hansa"));
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "parts") >= 0)
            {
                // what the crosshair says from a few places aboard and round her (not checks): dotnet run -c Release -- parts
                var pdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                pdef.Init();
                PartsReport(pdef);
                return 0;
            }
            if (Array.IndexOf(args, "cannons") >= 0)
            {
                // the guns' checks alone, with the range table: dotnet run -c Release -- cannons
                var cdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                cdef.Init();
                CannonReport(cdef);
                Cannons(cdef, Path.Combine(root, "assets", "hansa"));
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "notes") >= 0)
            {
                // the surveyor's chalk's checks alone: dotnet run -c Release -- notes
                var ndef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                ndef.Init();
                SurveyNotes(ndef, root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "oven") >= 0)
            {
                // the clay oven's checks alone: dotnet run -c Release -- oven
                Oven();
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "helm") >= 0)
            {
                // the helm's sign and the wind UI's frames alone: dotnet run -c Release -- helm
                var hdef2 = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                hdef2.Init();
                var hboat = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "skiff.json")));
                hboat.Init();
                Helm(hdef2, hboat);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "rain") >= 0)
            {
                // the rain under her decks and the water specks swept into her hold alone (0.3.13): dotnet run -c Debug -- rain
                var rdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                rdef.Init();
                Rain(rdef);
                SweptSpecks(rdef);
                StrainNotice();
                PartitionsToHer(rdef);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "design") >= 0)
            {
                // (0.3.14) one design's checks: dotnet run -- design carrack-a
                string dcode = args[^1];
                Console.WriteLine($"======== {dcode}");
                // (0.3.17) a modular design (carrack-m: no door fixtures, her doors are pieces) has checks of its own
                string dpath = Path.Combine(root, "assets", "hansa", "config", "ships", dcode + ".json");
                var dprobe = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(dpath));
                if (dprobe.ModularPieces?.Length > 0) { dprobe.Init(); ModularDesign(dprobe); }
                else CheckCarrack(dpath);
                // (0.3.20, step B) her deck cells against the contract (docs/slots.md), if she has them
                if (dprobe.DeckSlots?.Length > 0)
                {
                    if (dprobe.Grid == null) dprobe.Init();
                    var skinPath = Path.Combine(root, "assets", "hansa", dprobe.Shapes.Skin.Replace("hansa:", "").Replace('/', Path.DirectorySeparatorChar));
                    DesignSlots(dprobe, JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(skinPath)));
                }
                else Console.WriteLine($"  --   {dcode}: no deck slots (docs/slots.md){(dcode == "carrack-m" ? ": waits on the generator" : "")}");
                // (0.3.21, step C) her gun places against the contract (docs/slots.md §7), if she has them
                if (dprobe.GunPlaces?.Length > 0)
                {
                    if (dprobe.Grid == null) dprobe.Init();
                    var gbad = GunPlaces.DesignProblems(dprobe);
                    Check(gbad.Count == 0, $"{dcode}: her {dprobe.GunPlaces.Length} gun places keep the contract{(gbad.Count > 0 ? ": " + string.Join("; ", System.Linq.Enumerable.Take(gbad, 8)) : "")}");
                }
                else Console.WriteLine($"  --   {dcode}: no gun places (docs/slots.md §7){(dcode == "carrack-m" ? ": waits on the generator" : ": fixed guns")}");
                // (0.3.26, step D1) her hull panels against the contract (docs/slots.md §9), if she has them
                if (dprobe.HullSlots?.Length > 0)
                {
                    var (hdef, hfile) = LoadHullDesign(root, dcode);
                    DesignHullSlots(hdef, hfile);
                }
                else Console.WriteLine($"  --   {dcode}: no hull panels (docs/slots.md §9){(dcode == "carrack-m" ? ": waits on the generator" : ": the old damage model")}");
                // (22 October 2026, caravel note 38) her steerage's front closed up to the quarterdeck
                if (dcode == "caravel") CaravelSteerage(root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "hull") >= 0)
            {
                // (0.3.26, step D1) the hull panels on carrack-m: dotnet run -c Debug -- hull
                HullSlots(root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "notes0324") >= 0) { Notes0324(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0325") >= 0) { Notes0325(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0327") >= 0) { Notes0327(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0329") >= 0) { Notes0329(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0330") >= 0) { Notes0330(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0328") >= 0) { Notes0328(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0328damage") >= 0) { Notes0328Damage(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "notes0328hood") >= 0) { Notes0328Hood(root); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (Array.IndexOf(args, "stairs") >= 0)
            {
                // (0.3.23) the ship's stairs (steep and long) and a hatch from below: dotnet run -c Debug -- stairs
                var sdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                sdef.Init();
                ShipStairs(sdef);
                CastleGunPorts(root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "gunplaces") >= 0)
            {
                // (0.3.21, step C) gun places on carrack-m: dotnet run -c Debug -- gunplaces
                GunPlacesTest(root);
                // (0.3.25) the stern chasers
                Notes0325(root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "slots") >= 0)
            {
                // (0.3.20, step B) the deck cells on a carrack-m given deck slots by the test: dotnet run -c Debug -- slots
                Slots(root);
                // (0.3.26, step D1) and her hull panels' contract
                var (sdefH, sfileH) = LoadHullDesign(root);
                if (sdefH.Hull != null) DesignHullSlots(sdefH, sfileH);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "partitions") >= 0)
            {
                var pdef2 = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                pdef2.Init();
                PartitionsToHer(pdef2);
                PartitionsMeetHer(pdef2, Path.Combine(root, "assets", "hansa"));
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "modular") >= 0)
            {
                // (0.3.16) the modular pieces alone: dotnet run -c Debug -- modular
                var mdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                mdef.Init();
                Modular(mdef, root);
                // (0.3.23) the ship's stairs and the hatch from below
                ShipStairs(mdef);
                CastleGunPorts(root);
                // (0.3.24) chalk notes 176-193
                Notes0324(root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "chalktext") >= 0)
            {
                // the chalk note's comment box alone: dotnet run -c Release -- chalktext
                ChalkText();
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "decor") >= 0)
            {
                // decorating's checks alone (mount points, pieces, paint): dotnet run -c Release -- decor
                var ddef2 = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                ddef2.Init();
                Decor(ddef2, root);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "hearth") >= 0)
            {
                // the firehearth's checks alone, with its cooking times: dotnet run -c Release -- hearth
                var hdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                hdef.Init();
                Hearth(hdef);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (Array.IndexOf(args, "wreck") >= 0 || Array.IndexOf(args, "wreck-views") >= 0)
            {
                // the wreck's checks alone (dotnet run -c Release -- wreck), or a wreck as the client draws it for
                // python tools/shipgen/texview.py wreck (-- wreck-views)
                var wdef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "carrack.json")));
                wdef.Init();
                if (Array.IndexOf(args, "wreck-views") >= 0) { WreckViews(wdef); return 0; }
                Wrecks(wdef);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            ShipDefinition def = null;
            // the carrack as built, and the two of the proposal of 2026-09-30 (deeper, the castles moved) the user tries
            // beside it: the same checks, where things are read from each one's own doors and decks
            foreach (var code in new[] { "carrack", "carrack2", "carrack3" })
            {
                string path = Path.Combine(root, "assets", "hansa", "config", "ships", code + ".json");
                if (!File.Exists(path)) { Check(code != "carrack", $"{code}.json is there"); continue; }
                Console.WriteLine($"======== {code}");
                var d = CheckCarrack(path);
                if (code == "carrack") def = d;
            }

            var boatDef = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets", "hansa", "config", "ships", "skiff.json")));
            boatDef.Init();
            Boat(boatDef, def);
            Shadows(boatDef, Path.Combine(root, "assets", "hansa"));
            SurveyNotes(def, root);
            Hearth(def);
            Decor(def, root);
            ChalkText();
            PumpWater(def);
            Oven();
            Helm(def, boatDef);
            Rain(def);
            SweptSpecks(def);
            foreach (var dcode in new[] { "caravel", "hansa-carrack", "carrack" }) DryHold(root, dcode);
            StrainNotice();
            PartitionsToHer(def);
            PartitionsMeetHer(def, Path.Combine(root, "assets", "hansa"));
            SailsPressed(def, Path.Combine(root, "assets", "hansa"));
            WorkingBlocks(def);
            Modular(def, root);
            BuildModeStructureOnly(root);
            ShipStairs(def);
            DeckItems(root);
            CastleGunPorts(root);
            Notes0324(root);
            Notes0325(root);
            LiddedPortSealCarrack(def);
            // (0.3.26, step D1) carrack-m's hull panels
            HullSlots(root);
            // (0.3.27) chalk notes 198-222, the stairs and beams, mending holes, gun laying
            Notes0327(root);
            // (0.3.28) chalk notes 223-231, step D2
            Notes0328(root);
            Notes0329(root);
            // (0.3.30) chalk notes 234-243: run alone (-- notes0330) until carrack-m is generated again with the swept arcs

            // (0.3.14) the second design, companion option A: its general checks (all of them: dotnet run -- design carrack-a)
            string pathA = Path.Combine(root, "assets", "hansa", "config", "ships", "carrack-a.json");
            if (File.Exists(pathA)) SecondDesign(pathA, root);
            else Console.WriteLine("(carrack-a.json not there: the second design isn't checked)");

            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        static ShipDefinition CheckCarrack(string path)
        {
            var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(path));
            def.Init();
            var g = def.Grid;
            // heights from the main deck (3.5 in the first carrack; a deeper one floats lower on her own lines), and the
            // castle fronts from their doors
            double D = def.DeckY - 3.5;
            // the hold's floor (0.3.9: a storey of 3 under the lower deck, read from the design)
            double H = Array.Find(def.Decks, d => d.Code == "hold")?.Y ?? -1.75 + D;
            double qd = def.Fixture("door-greatcabin").Pos[0] + 0.125, pf = def.Fixture("door-poopcabin").Pos[0] + 0.125;
            double fc = def.Fixture("door-forecastle").Pos[0] - 0.125;
            // how far out each stair stands (the middle of its first step): further out in a wider hull
            double zQ = StairZ(g, qd + 1.375, 3.875 + D), zF = StairZ(g, fc - 1.375, 3.875 + D), zP = StairZ(g, pf + 1.375, 6.875 + D);
            Check(zQ > 1.5 && zF > 1.5 && zP > 1.5, $"the stairs stand on the starboard side (middles at z {zQ:0.00}, {zF:0.00}, {zP:0.00})");

            Console.WriteLine("definition");
            Check(g.CountSolid() > 20000, $"collision voxels decoded ({g.CountSolid()})");
            Check(def.Fixtures.Length > 20, $"fixtures ({def.Fixtures.Length})");
            Check(def.Fixture("helm") != null, "has a helm");
            Check(def.RoomAt(0, 1.5 + D, 0)?.Code == "lowerdeck", "lower deck room at midships");
            Check(def.RoomAt(qd - 4, 4.5 + D, 0)?.Code == "greatcabin", "great cabin under the quarterdeck");
            Check(def.RoomAt(0, 5 + D, 0) == null, "the waist is open to the sky");
            {
                // Sea keeps waves out of this footprint: it must hold the hull around the waterline with room to spare
                var wc = def.WaterClip;
                double worst = double.MaxValue;
                for (int i = 0; i < g.NX; i++)
                    for (int j = Math.Max(0, g.CellY(-0.5)); j <= g.CellY(1.4); j++)
                        for (int k = 0; k < g.NZ; k++)
                        {
                            if (!g.Solid(i, j, k)) continue;
                            double x = g.OX + (i + 0.5) * g.V, z = Math.Abs(g.OZ + (k + 0.5) * g.V);
                            worst = Math.Min(worst, wc.HalfWidthAt(x) - z);
                            worst = Math.Min(worst, wc.HalfLength - Math.Abs(x - wc.CenterX));
                        }
                Check(wc.Profile != null && worst >= 0.75, $"water footprint measured from the hull, at least {worst:0.00} blocks clear of it " +
                    $"(length {2 * wc.HalfLength:0.0}, widest {2 * wc.HalfWidth:0.0}, stern {wc.Profile?[0]:0.0} / bow {wc.Profile?[^1]:0.0})");
            }
            foreach (var b in def.BoardPoints)
                Check(!g.Collides(b[0] - 0.3, b[1], b[2] - 0.3, b[0] + 0.3, b[1] + 1.85, b[2] + 0.3) && DeckMover.HasFloor(g, b[0], b[1], b[2]),
                    $"board point {b[0]},{b[1]},{b[2]} is free with floor");
            foreach (var f in def.Fixtures)
            {
                if (f.Type == "helm")
                {
                    bool free = !g.Collides(f.Pos[0] - 0.3, f.Pos[1], f.Pos[2] - 0.3, f.Pos[0] + 0.3, f.Pos[1] + 1.85, f.Pos[2] + 0.3);
                    Check(free, $"{f.Code} position is free");
                }
                else if (f.Opens)
                {
                    // the doorway / entry port is open space in the design; the door or gate fills it while shut
                    var b = f.Box;
                    Check(b != null && !g.Collides(b[0] + 0.01, b[1] + 0.01, b[2] + 0.01, b[3] - 0.01, b[4] - 0.01, b[5] - 0.01), $"{f.Code}: the opening is clear");
                }
                else if (f.Type == "attach" && f.Kind == "boat" && ShipFittings.AttachBox(f) is double[] bb)
                {
                    // a boat on the gallows: over everyone's head, clear of everything
                    bool clear = !g.Collides(bb[0] + 0.01, bb[1] + 0.01, bb[2] + 0.01, bb[3] - 0.01, bb[4] - 0.01, bb[5] - 0.01);
                    Check(clear && bb[1] > def.DeckY + 2.0, $"{f.Code}: room for a boat on the gallows, over everyone's head");
                }
                else if (f.Type == "attach" && ShipFittings.AttachBox(f) is double[] ab)
                {
                    // a chest, bed or cargo put here stands on the deck and isn't inside the hull or a wall
                    bool clear = !g.Collides(ab[0] + 0.01, ab[1] + 0.01, ab[2] + 0.01, ab[3] - 0.01, ab[4] - 0.01, ab[5] - 0.01);
                    bool floor = g.SolidAt((ab[0] + ab[3]) / 2, ab[1] - 0.1, (ab[2] + ab[5]) / 2);
                    Check(clear && floor, $"{f.Code}: room for a {f.Kind} on the deck");
                }
                else if (f.Type == "attach" && f.Kind == "lantern" && f.Hang == "down" && f.Code.EndsWith("mast"))
                {
                    // a hook on a mast: the mast right beside it, room under it to hang
                    bool mast = g.SolidAt(f.Pos[0] + 0.45, f.Pos[1], f.Pos[2]) || g.SolidAt(f.Pos[0] - 0.45, f.Pos[1], f.Pos[2]);
                    Check(mast && !g.Collides(f.Pos[0] - 0.2, f.Pos[1] - 0.75, f.Pos[2] - 0.2, f.Pos[0] + 0.2, f.Pos[1] - 0.05, f.Pos[2] + 0.2),
                        $"{f.Code}: the hook is on the mast with room to hang");
                }
                else if (f.Type == "attach" && f.Kind == "lantern" && f.Hang == "down")
                {
                    Check(g.SolidAt(f.Pos[0], f.Pos[1] + 0.1, f.Pos[2]) && !g.SolidAt(f.Pos[0], f.Pos[1] - 0.3, f.Pos[2]),
                        $"{f.Code}: the hook is under a beam or deck with room to hang");
                }
            }
            if (Array.Exists(def.Fixtures, f => f.Type == "attach" && f.Kind == "bed"))
                Check(Array.FindAll(def.Fixtures, f => f.Type == "attach" && f.Kind == "bed").Length >= 8, "berths for a crew of 8");
            else
            {
                // (0.3.8: her people furnish her; no built-in beds) room for a crew of 8 to put their bunks: a bed's
                // footprint (2 x 1, its height) against her side on the lower deck, free and with the deck under it
                int berths = 0;
                foreach (int sd in new[] { -1, 1 })
                    for (double x = -12.0; x < 12.0; x += 2.25)
                        for (double zo = 4.75; zo > 1.5; zo -= 0.25)
                        {
                            double z0 = sd > 0 ? zo - 1.0 : -zo, z1 = sd > 0 ? zo : -zo + 1.0;
                            if (g.Collides(x + 0.01, 0.5 + D + 0.01, z0 + 0.01, x + 1.99, 0.5 + D + 0.6, z1 - 0.01)) continue;
                            if (DeckMover.HasFloor(g, x + 1.0, 0.5 + D, (z0 + z1) / 2)) berths++;
                            break;
                        }
                Check(berths >= 8, $"room for a crew of 8 to put their bunks on the lower deck against her sides ({berths} places)");
            }
            {
                // (0.3.7, the user's note 12) the stern lantern's place shows even empty, with nothing for it in hand, on its
                // bracket out over the taffrail; the other empty places only show holding what goes there
                // (0.3.8: her lanterns' places are hooks, each with a lantern on it to start with)
                var stern = def.Fixture("lantern-stern");
                var hook = def.Hooks == null ? null : Array.Find(def.Hooks, h => h.Code == "lantern-stern");
                bool alone = def.Hooks != null ? Array.FindAll(def.Hooks, h => h.Shown).Length == 1
                                               : Array.FindAll(def.Fixtures, f => f.Type == "attach" && f.Shown).Length == 1;
                double[] spos = hook?.Pos ?? stern?.Pos;
                bool shown = hook?.Shown ?? stern?.Shown ?? false;
                string hang = hook?.Hang ?? stern?.Hang;
                double aftmost = double.MaxValue;
                foreach (var f in def.Fixtures) aftmost = Math.Min(aftmost, f.Pos[0]);
                Check(spos != null && shown && hang == "up" && alone && spos[0] <= aftmost + 1e-6 &&
                      !g.SolidAt(spos[0], spos[1] + 0.2, spos[2]),
                    "the stern lantern's place: out over the taffrail on its bracket, room for a lantern standing on it, and shown even empty (\"Stern lantern\")");
            }

            Console.WriteLine("frame math");
            var rnd = new Random(1);
            double maxErr = 0, maxRenderErr = 0;
            for (int i = 0; i < 200; i++)
            {
                double yaw = rnd.NextDouble() * Math.PI * 4 - Math.PI * 2;
                double lx = rnd.NextDouble() * 40 - 20, ly = rnd.NextDouble() * 10, lz = rnd.NextDouble() * 10 - 5;
                ShipFrame.ToWorld(yaw, lx, ly, lz, out double wx, out double wy, out double wz);
                ShipFrame.ToLocal(yaw, wx, wy, wz, out double bx, out double by, out double bz);
                maxErr = Math.Max(maxErr, Math.Abs(bx - lx) + Math.Abs(by - ly) + Math.Abs(bz - lz));

                // what the renderer does to a model vertex
                var m = new Matrixf().RotateY(ShipFrame.ModelYaw(yaw));
                var v = m.TransformVector(new Vec4f((float)lx, (float)ly, (float)lz, 1));
                maxRenderErr = Math.Max(maxRenderErr, Math.Abs(v.X - wx) + Math.Abs(v.Y - wy) + Math.Abs(v.Z - wz));
            }
            Check(maxErr < 1e-9, $"local <-> world round trip (err {maxErr:e1})");
            Check(maxRenderErr < 1e-3, $"render rotation matches collision frame (err {maxRenderErr:e1})");
            ShipFrame.ToWorld(0, 1, 0, 0, out double fx, out _, out double fz);
            Check(Math.Abs(fx) < 1e-9 && Math.Abs(fz - 1) < 1e-9, "yaw 0: bow points +Z, like a vanilla entity with yaw 0 walks");
            ShipFrame.ToWorld(0, 0, 0, 1, out double sx, out _, out double sz);
            Check(Math.Abs(sx + 1) < 1e-9, "starboard is to the right of the heading");

            Console.WriteLine("collision");
            double r = g.Raycast(new Vec3d(-2.0, 6 + D, 0.3), new Vec3d(0, -1, 0), 20);
            Check(Math.Abs(r - 2.5) < 0.01, $"ray down from the waist hits the main deck at {def.DeckY} (t={r:0.00})");
            int bad = 0;
            for (int i = 0; i < 3000; i++)
            {
                double x = rnd.NextDouble() * 30 - 15, y = rnd.NextDouble() * 10 - 2, z = rnd.NextDouble() * 8 - 4;
                if (g.Collides(x - 0.3, y, z - 0.3, x + 0.3, y + 1.85, z + 0.3)) continue;
                int axis = rnd.Next(3) switch { 0 => 0, 1 => 1, _ => 2 };
                double d = rnd.NextDouble() * 4 - 2;
                double m = g.Sweep(axis, d, x - 0.3, y, z - 0.3, x + 0.3, y + 1.85, z + 0.3);
                if (axis == 0) x += m; else if (axis == 1) y += m; else z += m;
                if (g.Collides(x - 0.3, y, z - 0.3, x + 0.3, y + 1.85, z + 0.3)) bad++;
            }
            Check(bad == 0, $"sweeps never end inside a voxel ({bad} bad)");

            Console.WriteLine("walking");
            // the design's grid has every door, gate and hatch open and nothing put aboard; "shut" has them all shut
            var shutAll = def.Grid.Clone();
            ShipFittings.ApplySolids(def, shutAll, f => false, f => null);
            // where things are (the waist was lengthened on 2026-09-29, so the forward half is read from the design)
            double fcDoor = def.Fixture("door-forecastle").Pos[0], hatchX0 = def.Fixture("hatch-waist").Box[0];
            double mainX = Array.Find(def.Ladders, l => l.Code == "main-starboard").ExitTop[0] + 0.8;
            Walk(g, "stand still on the main deck", -0.3, 3.5 + D, 2.8, 0, 0, 2, false, false, (m, res) => Math.Abs(m.Y - 3.5 - D) < 0.01 && m.OnGround);
            Walk(g, "up the stairs to the forecastle", fcDoor - 4.0, 3.5 + D, zF, 1, 0, 4, false, false, (m, res) => m.Y > 6.4 + D && m.X > fcDoor + 0.2);
            bool capstanForward = def.Fixture("capstan").Pos[0] > fcDoor - 5.0;   // (0.3.13: a block further aft, note 130)
            if (capstanForward)
            {
                // (0.3.10: on the centreline just aft of the forecastle's bulkhead) walking forward up the middle, the capstan stops you
                var capF = def.Fixture("capstan");
                // (0.3.13: from just forward of the waist hatch's coaming, now 1.5 from it: aft of that is the open hatch)
                Walk(g, "walking forward up the middle of the waist, the capstan stops you", Math.Max(capF.Pos[0] - 2.5, def.Fixture("hatch-waist").Box[3] + 0.4), 3.5 + D, capF.Pos[2], 1, 0, 3, false, false,
                    (m, res) => m.Y < 3.6 + D && m.X < capF.Pos[0] - 0.4);
            }
            Walk(shutAll, "over the coaming onto the shut waist hatch", hatchX0 - 0.9, 3.5 + D, 0.3, 1, 0, 0.7, false, false, (m, res) => m.X > hatchX0 + 0.5 && Math.Abs(m.Y - 4.0 - D) < 0.01);
            Walk(g, "through the open waist hatch down to the lower deck", hatchX0 - 0.9, 3.5 + D, 0.3, 1, 0, 2, false, false, (m, res) => m.Y < 1.0 + D && m.X > hatchX0);
            // (0.3.8: beside the pump's pipe, now at the hatch's after end)
            Walk(shutAll, "over the shut hold hatch on the lower deck", hatchX0 - 0.85, 0.5 + D, 0.6, 1, 0, 0.7, false, false, (m, res) => m.X > hatchX0 + 0.5 && Math.Abs(m.Y - 0.75 - D) < 0.01);
            Walk(g, "down through the open hold hatch onto the cargo deck", hatchX0 + 0.75, 1.0 + D, 0.0, 0, 0, 1.2, false, false, (m, res) => Math.Abs(m.Y - H) < 0.01 && m.OnGround);
            foreach (var (dx, dz, name) in new[] { (1.0, 0.0, "forward"), (-1.0, 0.0, "aft"), (0.0, 1.0, "to starboard"), (0.0, -1.0, "to port") })
            {
                // round the hold: the cargo deck holds you and the hull keeps you in (stay inside the dry hull); from where
                // the hold's ladder sets you down (0.3.6: the pump's pipe comes down to the keelson just forward of it)
                var holdFoot = Array.Find(def.Ladders, l => l.Code == "holdladder").ExitBottom;
                var m = new DeckMover(holdFoot[0], H, 0.0);
                m.Unstick(g);
                var res = DeckMover.Result.Ok;
                bool inside = true;
                for (int i = 0; i < 4 * 60 && res == DeckMover.Result.Ok; i++)
                {
                    res = m.Step(g, 1 / 60f, dx, dz, 3.5 + D, false, false);
                    inside &= def.DryMask.SolidAt(m.X, m.Y + 1.0, m.Z);
                }
                // (0.3.10: the bottom 0.5 thick, the turn of the bilge tighter: you may step up onto it at the side, up to 0.6)
                Check(res == DeckMover.Result.Ok && m.Y - H > -0.05 && m.Y - H < (dz != 0 ? 0.65 : 0.3) && inside,
                    $"walking {name} in the hold stays on the cargo deck, inside the hull (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00}{(inside ? "" : ", outside the dry hull")})");
            }
            Walk(g, "up the stairs to the quarterdeck", qd + 3.5, 3.5 + D, zQ, -1, 0, 4, false, false, (m, res) => m.Y > 6.4 + D && m.X < qd - 0.2);
            Walk(g, "up the steep stairs to the poop deck", pf + 3.0, 6.5 + D, zP, -1, 0, 4, false, false, (m, res) => m.Y > 9.4 + D && m.X < pf - 0.2);
            // the quarterdeck keeps room between the head of the waist's stairs, the wheel and the poop cabin's door
            Walk(g, "from the head of the quarterdeck's stairs across to the helm", qd - 0.6, 6.5 + D, zQ + 0.4, 0, -1, 1.2 + (zQ - 2.5) / 3.5, false, false, (m, res) => m.Z < 0.9 && m.Y < 6.6 + D);
            Walk(g, "from the head of the quarterdeck's stairs aft to the poop cabin's door", qd - 0.6, 6.5 + D, 1.1, -1, 0, 1.2 + (qd + 6 - pf - 4) / 3.5, false, false, (m, res) => m.X < pf + 0.8 && m.Y < 6.6 + D);
            Walk(g, "into the forecastle", fcDoor - 1.5, 3.5 + D, 0, 1, 0, 3, false, false, (m, res) => m.X > fcDoor + 0.6 && m.Y < 3.6 + D);
            Walk(g, "from the helm into the poop cabin", pf + 1.0, 6.5 + D, 0, -1, 0, 3, false, false, (m, res) => m.X < pf - 0.6 && m.Y < 6.6 + D);
            // (0.3.14: carrack-a's companion door is a block inboard, where this walks: the carrack's own)
            if (def.Code == "carrack")
                Walk(g, "the great cabin wall stops you", qd - 4, 3.5 + D, 2.0, 1, 0, 3, false, false, (m, res) => m.X < qd - 0.5 && m.X > qd - 1.0);
            Walk(g, "through the great cabin door (to the capstan)", qd - 2.5, 3.5 + D, 0, 1, 0, 3, false, false, (m, res) => m.X > qd);
            // somewhere along the waist's bulwark clear of the stairs, a gun, a gate, the shrouds' foot (0.3.10: read from the design)
            double side = -0.3;
            for (double x = qd + 1.5; x < fcDoor - 2; x += 0.25)
            {
                bool clearHere = System.Linq.Enumerable.All(def.Fixtures, f => (f.Box ?? f.Sel) is not double[] b || b[3] < x - 0.6 || b[0] > x + 0.6 || b[5] < 2.4) &&
                                 System.Linq.Enumerable.All(def.Ladders, l => l.ExitBottom == null || Math.Abs(l.ExitBottom[0] - x) > 1.0 || l.ExitBottom[2] < 2.4) &&
                                 System.Linq.Enumerable.All(def.Stairs ?? Array.Empty<StairDef>(), st => Math.Max(st.Foot[0], st.Head[0]) < x - 1.0 || Math.Min(st.Foot[0], st.Head[0]) > x + 1.0 || st.Foot[2] < 1.5);
                if (clearHere) { side = x; break; }
            }
            Walk(g, "the bulwark stops you walking", side, 3.5 + D, 2.8, 0, 1, 2, false, false, (m, res) => res == DeckMover.Result.Ok && m.Z < def.HullHalfWidthAtDeck - 0.1);
            Walk(g, "jumping the rail puts you over the side", side, 3.5 + D, 3.2, 0, 1, 3, true, false, (m, res) => res == DeckMover.Result.Overboard);
            Walk(g, "the hull keeps you in on the lower deck", 0, 0.5 + D, 2.5, 0, 1, 2, true, false, (m, res) => res == DeckMover.Result.Ok && m.Z < def.HullHalfWidthAtDeck);
            Walk(g, "past the aft castle break (the sheer sweeps up there) onto the quarterdeck stairs", qd + 3.0, 3.5 + D, zQ + 0.1, -1, 0, 4, false, false, (m, res) => m.Y > 6.4 + D && m.X < qd - 0.2);
            var comp = Array.Find(def.Ladders, l => l.Code == "companionway");
            var compStair = def.Stairs == null ? null : Array.Find(def.Stairs, st => st.Code == "companion");
            if (comp == null && compStair != null)
            {
                // (0.3.8, the agreed redesign) the companionway is a stair in the great cabin, down aft along its port side
                // from just inside its front wall to the lower deck, in through the cabin's door: down it from its head and
                // up it from its foot (headroom under the deck's edge all the way), and the cabin's door is the way below
                double hx = compStair.Head[0], hz = compStair.Head[2], fx2 = compStair.Foot[0];
                Walk(g, "down the companion stair in the great cabin to the lower deck", hx - 0.35, compStair.Head[1], hz, -1, 0, 1.4, false, false,
                    (m, res) => m.Y < compStair.Foot[1] + 0.1 && m.X < fx2);
                Walk(g, "up the companion stair from the lower deck into the great cabin", fx2 - 0.6, compStair.Foot[1], hz, 1, 0, 5, false, false,
                    (m, res) => m.Y > compStair.Head[1] - 0.1 && m.X > hx - 1.0);
                // (0.3.10) the stair on from the lower deck down to the hold, under the companion: down it and up it
                var holdStair = Array.Find(def.Stairs, st => st.Code == "hold");
                if (holdStair != null)
                {
                    double u = Math.Sign(holdStair.Foot[0] - holdStair.Head[0]), hsz = holdStair.Head[2];
                    Walk(g, "down the stair from the lower deck to the hold", holdStair.Head[0] + u * 0.35, holdStair.Head[1], hsz, u, 0, 2.5, false, false,
                        (m, res) => m.Y < holdStair.Foot[1] + 0.1 && (m.X - holdStair.Foot[0]) * u > -0.1);
                    Walk(g, "up the stair from the hold to the lower deck", holdStair.Foot[0] + u * 0.6, holdStair.Foot[1], hsz, -u, 0, 5, false, false,
                        (m, res) => m.Y > holdStair.Head[1] - 0.1);
                }
                // (0.3.12, note 112: the stair walled off from the great cabin, its own room "companion" with a door from the
                // cabin; the waist's companion door is the way below, and the great cabin stays closed with the lower deck open)
                var gcDoor = def.Fixture("door-companion-cabin");
                var stairDoor = def.Fixture("door-companion-stair");
                Check(def.RoomAt(hx - 0.5, compStair.Head[1] + 0.5, hz)?.Code == "companion" && gcDoor?.Unseals == "lowerdeck" &&
                      stairDoor != null && stairDoor.Unseals == null && def.Fixture("door-greatcabin")?.Unseals == null &&
                      hx < gcDoor.Box[0] + 0.01 && compStair.Head[1] - compStair.Foot[1] > 2.9,
                    $"the companion stair goes from the great cabin (head at x {hx:0.00}, z {hz:0.00}) down aft to the lower deck (foot at x {fx2:0.00}); " +
                    "the great cabin's door is the way below (open, the lower deck isn't closed)");
            }
            else if (Math.Abs(comp.Top[0] - qd) < 3)
                Walk(g, "no stairs on the port side any more: the companionway's hood against the quarterdeck", qd + 3.0, 3.5 + D, -2.75, -1, 0, 3, false, false, (m, res) => m.Y < 1.0 + D || m.X > qd + 1.1);
            else
            {
                // the companionway on the centreline forward of the mainmast, down toward the bow (0.3.4)
                double into = Math.Sign(comp.Top[0] - comp.ExitTop[0]);
                Check(comp.Top[0] > mainX + 1 && comp.Bottom[0] > comp.Top[0] && Math.Abs(comp.Top[2]) < 0.1,
                    $"the companionway goes down toward the bow on the centreline, forward of the mainmast (head at x {comp.Top[0]:0.00}, foot {comp.Bottom[0]:0.00})");
                Check(!g.Collides(comp.ExitTop[0] - 0.3, 3.5 + D, comp.ExitTop[2] - 0.3, comp.ExitTop[0] + 0.3, 3.5 + D + 1.85, comp.ExitTop[2] + 0.3),
                    "room to stand between the mainmast and the companionway's head");

                // the hatches right after the companionway (0.3.6, the user: "1-2 blocks only" between the ladders): the
                // hold's ladder comes up just forward of the companionway's foot. Up from the cargo deck by it and straight
                // on aft up the companionway to the waist, and down from the waist the same way into the hold (hatches open)
                var holdL = Array.Find(def.Ladders, l => l.Code == "holdladder");
                double gap = def.Fixture("hatch-hold").Box[0] - comp.Bottom[0];
                (string seen, DeckMover m) Go(double[] from, double dir)
                {
                    var m = new DeckMover(from[0], from[1], from[2]);
                    m.Unstick(g);
                    string seen = "";
                    for (int i = 0; i < 20 * 60; i++)
                    {
                        m.Step(g, 1 / 60f, dir, 0, 3.5, false, false, 1, def.Ladders, 1);
                        if (m.Climbing != null && !seen.EndsWith(m.Climbing.Code)) seen += (seen.Length > 0 ? ", " : "") + m.Climbing.Code;
                        if (m.Climbing == null && m.OnGround && seen.Contains(',')) break;
                    }
                    return (seen, m);
                }
                var up = Go(holdL.ExitBottom, -1);
                var down = Go(comp.ExitTop, 1);
                Check(gap > 1.0 && gap < 2.5 && up.seen == "holdladder, companionway" && Math.Abs(up.m.Y - def.DeckY) < 0.05 &&
                      down.seen == "companionway, holdladder" && Math.Abs(down.m.Y - holdL.ExitBottom[1]) < 0.05,
                    $"the hold hatch {gap:0.00} forward of the companionway's foot: up from the hold by its ladder and on up the companionway to the waist " +
                    $"({up.seen}; ended at {up.m.X:0.00},{up.m.Y:0.00}), and down the same way ({down.seen}; ended at {down.m.X:0.00},{down.m.Y:0.00})");

                // (0.3.7, the user's note 24) a house over the companionway, its door aft on the coaming: room to stand in
                // the doorway upright, its walls round the ladder; shut, the door stops you at it (open, the way down above)
                var compDoor = def.Fixture("door-companion");
                if (compDoor != null)
                {
                    var db = compDoor.Box;
                    bool upright = !g.Collides(db[0] - 0.05, db[1], -0.3, db[0] + 0.55, db[1] + 1.85, 0.3);
                    bool walled = g.SolidAt(comp.Top[0] + 0.5, def.DeckY + 1.5, 0.65) && g.SolidAt(comp.Top[0] + 0.5, def.DeckY + 1.5, -0.65) &&
                                  g.SolidAt(comp.Top[0] + 0.5, def.DeckY + 2.6, 0.0);
                    var shutM = new DeckMover(comp.ExitTop[0], comp.ExitTop[1], comp.ExitTop[2]) { TopClosed = l => l.Gate != null };
                    shutM.Unstick(shutAll);
                    for (int i = 0; i < 3 * 60; i++) shutM.Step(shutAll, 1 / 60f, 1, 0, 3.5, false, false, 1, def.Ladders, 1);
                    Check(upright && walled && shutM.Climbing == null && shutM.X < db[0] && Math.Abs(shutM.Y - def.DeckY) < 0.05 && compDoor.Unseals == "lowerdeck" &&
                          comp.Gate == compDoor.Code,
                        $"the companionway has a house over it: its door aft on the coaming, room to stand in the doorway upright, walls and a roof round the ladder; " +
                        $"shut, the door stops you at it (at {shutM.X:0.00}) and the ladder's top with it; open, it lets the weather into the lower deck");
                }
            }
            {
                // the capstan stands clear, with room all round to walk its bars
                var cap = def.Fixture("capstan");
                int blocked = 0;
                for (int k = 0; k < 32; k++)
                {
                    double a = k * Math.PI / 16, x = cap.Pos[0] + Math.Cos(a) * 1.1, z = cap.Pos[2] + Math.Sin(a) * 1.1;   // (0.3.13: shorter bars, note 130)
                    if (g.Collides(x - 0.3, 3.5 + D, z - 0.3, x + 0.3, 3.5 + D + 1.85, z + 0.3) || !DeckMover.HasFloor(g, x, 3.5 + D, z)) blocked++;
                }
                Check(blocked == 0, $"a clear ring round the capstan for walking the bars ({blocked} of 32 places blocked)");
                if (capstanForward)
                    // (0.3.8: 1.25 further inboard, z -0.75, to clear the fifth gun a side housed)
                    // (0.3.10, the user's decision: on the centreline just aft of the forecastle's bulkhead, the cables led either side of its door)
                    Check(cap.Pos[0] > fcDoor - 5.0 && cap.Pos[0] < fcDoor - 1.4 && Math.Abs(cap.Pos[2]) < 0.1,
                        $"the capstan stands on the centreline in front of the forecastle, clear of its door (x {cap.Pos[0]}, z {cap.Pos[2]})");
                else
                    Check(cap.Pos[0] < mainX && cap.Pos[0] > qd + 1.3 && cap.Pos[2] < -1.5, $"the capstan stands aft of the mainmast on the port side, clear of the quarterdeck's front (x {cap.Pos[0]}, z {cap.Pos[2]})");
            }
            Check(g.SolidAt(-12.5, -1.5 + D, 0) && g.SolidAt(-12.5, 1.5 + D, 0) && g.SolidAt(-12.5, 4.5 + D, 0), "the mizzen goes down through the great cabin and the lower deck to the keel");
            // (0.3.9, hull R: the keelson flush with the hold's floor)
            // (x 5.5: clear of the pump's pipe, which comes down to the keelson at 4.5)
            Check(g.SolidAt(5.5, H - 0.1, 0) && !g.Collides(5.2, H, -0.3, 5.8, H + 1.85, 0.3),
                "the keelson runs down the middle of the hold under its floor, with a full man's height over it");
            Walk(g, "across the keelson in the hold", 5.5, H, -1.2, 0, 1, 0.7, false, false, (m, res) => m.Z > 0.8 && Math.Abs(m.Y - H) < 0.3);
            // (0.3.9: the beams on even x, on the grid, in the band under the deck)
            double poopY = Array.Find(def.Decks, d => d.Code == "poop")?.Y ?? 9.5 + D;
            Check(g.SolidAt(-14.0, poopY - 0.45, 0) && g.SolidAt(-16.0, poopY - 0.45, 0) && !g.SolidAt(-15.0, poopY - 0.45, 0), "beams under the poop deck, in line with the rest");
            {
                int rail = 0;
                for (int k = 0; k < 8; k++) if (g.SolidAt(mainX + Math.Cos(k * Math.PI / 4) * 1.6, 21.5 + D + 1.1, Math.Sin(k * Math.PI / 4) * 1.6)) rail++;
                Check(rail == 8, $"a railing all round the main top ({rail} of 8 places at chest height)");
            }

            Console.WriteLine("doors, gates, things put aboard");
            {
                // a new ship: every door and gate shut
                var shut = def.Grid.Clone();
                ShipFittings.ApplySolids(def, shut, f => false, f => null);
                Walk(shut, "a shut door stops you", qd - 2.5, 3.5 + D, 0, 1, 0, 2, false, false, (m, res) => m.X < qd - 0.5);
                var gateF = def.Fixture("gate-starboard");
                Walk(shut, "a shut gate keeps you aboard", gateF.Pos[0], 3.5 + D, 2.9, 0, 1, 2, false, false, (m, res) => res == DeckMover.Result.Ok && m.Z < gateF.Pos[2] - 0.1);
                var chest = def.Grid.Clone();
                ShipFittings.ApplySolids(def, chest, f => true, f => f.Code == "chest-greatcabin-2" ? new ItemStack() : null);
                var cf = def.Fixture("chest-greatcabin-2");
                // (0.3.8: no built-in chests or beds in the carrack: her people put them; the decorating tests have them)
                if (cf != null)
                    Walk(chest, "a chest put aboard is in the way", cf.Pos[0], 3.5 + D, cf.Pos[2] - 1.2, 0, 1, 2, false, false, (m, res) => m.Z < cf.Pos[2] - 0.55);
                var bed = def.Grid.Clone();
                ShipFittings.ApplySolids(def, bed, f => true, f => f.Code == "bed-lowerdeck-1" ? new ItemStack() : null);
                var bf = def.Fixture("bed-lowerdeck-1");
                if (bf != null)
                    Walk(bed, "you can step up onto a bed", bf.Pos[0], 0.5 + D, bf.Pos[2] - 1.3, 0, 1, 0.5, false, false, (m, res) => m.Y > 0.9 + D && m.Y < 1.2 + D);
                var boats = Array.FindAll(def.Fixtures, f => f.Type == "attach" && f.Kind == "boat");
                Check(boats.Length == 2, $"two launches on the gallows over the waist, room for eight ({boats.Length})");
                var boat = def.Grid.Clone();
                ShipFittings.ApplySolids(def, boat, f => true, f => f.Kind == "boat" ? new ItemStack() : null);
                Walk(boat, "you walk under the boats on the gallows", hatchX0 - 0.9, 3.5 + D, -1.8, 1, 0, 1.2, false, false, (m, res) => m.X > hatchX0 + 2.5 && m.Y < 3.6 + D);
                // (0.3.8: from just forward of the pump, now at the hatch's after end)
                Walk(boat, "and climb onto the open waist hatch's coaming under them and drop through it", hatchX0 - 0.6, 3.5 + D, 0.3, 1, 0, 2, false, false, (m, res) => m.Y < 1.0 + D && m.X > hatchX0);
                {
                    // right-clicking a boat on the gallows from the deck picks it (its cells in the collision stick out a
                    // little past its box; in 0.3.1 they hid it, so it couldn't be taken down)
                    int tries = 0, picked = 0;
                    foreach (var boatF in boats)
                    {
                        var bb = ShipFittings.AttachBox(boatF);
                        double cx = (bb[0] + bb[3]) / 2, cy = (bb[1] + bb[4]) / 2, cz = (bb[2] + bb[5]) / 2;
                        foreach (double ex in new[] { cx - 2.5, cx - 1.0, cx, cx + 1.5 })
                            foreach (double ez in new[] { -2.8, -1.9, -0.9, 0.9, 1.9, 2.8 })     // (the guns stand at the sides)
                            {
                                var eye = new Vec3d(ex, 3.5 + D + 1.62, ez);
                                if (boat.Collides(ex - 0.3, 3.5 + D, ez - 0.3, ex + 0.3, 3.5 + D + 1.85, ez + 0.3)) continue;
                                var dir = new Vec3d(cx - ex, cy - eye.Y, cz - ez);
                                double len = dir.Length();
                                if (len > 5) continue;
                                dir.Mul(1 / len);
                                // only rays that reach this boat before anything else of the ship
                                double tw = boat.Raycast(eye, dir, 6);
                                var cells = boat.CellsOf(bb);
                                double tc = ShipFittings.RayBox(eye, dir, new Cuboidd(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]));
                                if (tc < 0 || tc > tw + 0.01) continue;
                                tries++;
                                var hit = ShipFittings.Pick(def, boat, eye, dir, 6, f => true, f => true, f => f.Kind == "boat" ? new ItemStack() : null, out _);
                                if (hit == boatF) picked++;
                            }
                    }
                    Check(tries >= 12 && picked == tries, $"right-clicking a boat on the gallows from the deck picks it ({picked} of {tries} views)");
                }
                var cargo = Array.FindAll(def.Fixtures, f => f.Type == "attach" && f.Kind == "cargo");
                int paired = Array.FindAll(cargo, f => f.Pair != null && def.Fixture(f.Pair)?.Pair == f.Code).Length;
                if (cargo.Length > 0)                   // (0.3.8: the carrack's hold is furnished by her people)
                Check(cargo.Length >= 20 && paired >= cargo.Length - 4 && Array.TrueForAll(cargo, f => def.RoomAt(f.Pos[0], f.Pos[1] + 0.5, f.Pos[2])?.Code == "hold"),
                    $"places for chests, vessels and crates in the hold, in pairs a trunk takes ({cargo.Length}, {paired} paired)");
                var fh = Array.Find(def.Fixtures, f => f.Type == "figurehead");
                // (0.3.9, note 66: no figurehead, a place for a vanilla one later)
                Check(fh == null || fh.Options?.Length >= 1 && fh.Groups?.Length == fh.Options.Length, fh == null ? "no figurehead (note 66)" : "a figurehead to choose");
                var nb = Array.Find(def.Fixtures, f => f.Type == "nameboard");
                Check(nb?.Right != null && nb.Up != null && nb.Out != null && nb.Width > 2 && nb.Out[0] < -0.9, "a name board across the transom, facing aft");
                var hatch = def.Fixture("hatch-waist");
                Check(hatch?.Opens == true && hatch.Unseals == "lowerdeck", "the waist hatch opens, and while it's open the lower deck isn't a closed room");
                var hold = def.Fixture("hatch-hold");
                Check(hold?.Storage == "hold" && def.Fixture("hold")?.Slots >= 1200, $"the hold opens through its hatch in the lower deck, one big storage ({def.Fixture("hold")?.Slots} slots)");
                var captain = def.Fixture("bed-captain");
                if (captain != null || def.Hooks == null)
                {
                    Check(captain?.Type == "bed" && captain.Kind == "bed" && def.RoomAt(captain.Pos[0], captain.Pos[1] + 0.5, captain.Pos[2])?.Code == "greatcabin",
                        "the captain's bed in the great cabin");
                    Check(Array.FindAll(def.Fixtures, f => f.Type == "prop" && f.Block != null).Length >= 3, "vanilla props: the chart table and stools");
                }
                else
                    Check(def.Hooks.Length >= 10 && def.StartPieces?.Length == def.Hooks.Length,
                        $"(0.3.8) her lanterns' places are hooks, a lantern on each to start with ({def.Hooks.Length}); the rest her people put");
                {
                    // the ship's firehearth (0.3.6): in the forecastle's starboard after corner, a vanilla firepit standing in
                    // its sunken firebed, and what the working fire to come needs (the fire, the pot's place, the chimney)
                    var h = def.Fixture("hearth");
                    var fcRoom = Array.Find(def.Rooms, r => r.Code == "forecastle");
                    bool there = h?.Type == "hearth" && h.Block == "game:firepit-cold" && h.Room == "forecastle" &&
                                 def.RoomAt(h.Pos[0], h.Pos[1] + 0.5, h.Pos[2])?.Code == "forecastle" &&
                                 h.Pos[0] - fcDoor < 2.5 && h.Pos[2] > 1.5;
                    // on the firebed: bricks under it, sunk below the hearth's top with its sides higher round it, its front open
                    bool onBed = h != null && g.SolidAt(h.Pos[0], h.Pos[1] - 0.1, h.Pos[2]) && h.Pos[1] < h.Box[1] + 0.5 * (h.Box[4] - h.Box[1]) &&
                               h.Sel != null && h.Sel[1] >= h.Pos[1] - 0.01 && h.Sel[2] > h.Box[2] + 0.05 && h.Sel[5] < h.Box[5];
                    bool data = h?.Fire?.Length == 3 && h.Cook?.Length == 3 && h.Chimney?.Length == 3 && h.Fire[1] > h.Pos[1] &&
                                Math.Abs(h.Cook[1] - h.Pos[1] - 0.125) < 1e-3 &&
                                // the chimney: the smoke out through the forecastle deck (0.3.10: a grating in it, kept clear), in the open, near the hearth
                                h.Chimney[1] > Array.Find(def.Decks, d2 => d2.Code == "forecastle").Y - 0.3 && def.RoomAt(h.Chimney[0], h.Chimney[1], h.Chimney[2]) == null &&
                                (h.Chimney[1] > Array.Find(def.Decks, d2 => d2.Code == "forecastle").Y + 1.0 ||
                                 System.Linq.Enumerable.Any(def.KeepClear ?? Array.Empty<KeepClearDef>(), k => k.What == "grating" && k.Box != null && h.Chimney[0] >= k.Box[0] && h.Chimney[0] <= k.Box[3] && h.Chimney[2] >= k.Box[2] && h.Chimney[2] <= k.Box[5])) &&
                                Math.Abs(h.Chimney[0] - h.Pos[0]) < 1.0 && Math.Abs(h.Chimney[2] - h.Pos[2]) < 1.0;
                    Check(there && onBed && data && fcRoom != null,
                        $"the firehearth in the forecastle's starboard after corner, a vanilla firepit in its sunken firebed, its fire, pot and chimney given " +
                        $"(at {h?.Pos[0]:0.00},{h?.Pos[1]:0.00},{h?.Pos[2]:0.00}; chimney {h?.Chimney?[0]:0.00},{h?.Chimney?[1]:0.00},{h?.Chimney?[2]:0.00})");
                }
                Check(Array.TrueForAll(def.Fixtures, f => f.Type != "gate" || f.Block == null), "the entry port gates are sections of the bulwark (the design's own), not vanilla fence gates");
            }

            Console.WriteLine("ratlines and rope ladders");
            Check(Array.FindAll(def.Ladders, l => l.KindOrDefault == "ratlines").Length == 2, "the main shrouds, both sides, are the only ratlines to climb");
            // (0.3.8: the companionway is a stair in the great cabin, walked; the hold's is the one ship's ladder)
            // (0.3.13, note 136: the hold's ladder in two, broken where the hold's hatch shuts: cargo deck to the lower deck, and
            // the lower deck up through the glazed waist hatch)
            var hatchLadder = Array.Find(def.Ladders, l => l.Code == "hatchladder");
            int shipLadders = (def.Stairs != null && Array.Exists(def.Stairs, st => st.Code == "companion") ? 1 : 2) + (hatchLadder != null ? 1 : 0);
            Check(Array.FindAll(def.Ladders, l => l.KindOrDefault == "ladder").Length == shipLadders && Array.Find(def.Ladders, l => l.Code == "holdladder") is LadderDef hl &&
                  hl.Gate == "hatch-hold" && (hatchLadder == null || hatchLadder.Gate == "hatch-waist"),
                hatchLadder != null ? "the ship's ladders: the hold's up through its hatch, and on from the lower deck up through the glazed hatch (shut, each stops you)"
                    : shipLadders == 1 ? "a ship's ladder: the hold's up through its hatch (shut, it stops you)" : "two ship's ladders: the companionway's up to the waist, and the hold's up through its hatch (shut, it stops you)");
            Check(Array.FindAll(def.Ladders, l => l.Outside && l.Gate != null && def.Fixture(l.Gate)?.Type == "gate").Length == 2, "a rope ladder at each entry port");
            // (a ladder standing on a hatch, the waist's on the hold's, is climbed with that hatch shut under it)
            foreach (var l in def.Ladders) if (!l.Outside) ClimbRoundTrip(l.ExitBottom[1] > l.Bottom[1] - 0.6 && !DeckMover.HasFloor(g, l.ExitBottom[0], l.ExitBottom[1], l.ExitBottom[2]) ? shutAll : g, def.Ladders, l);
            foreach (var l in def.Ladders) if (l.Outside) SideLadder(def, l);
            WalkTop(g, def.Ladders, def.Ladders[0]);
            {
                // strolling along the rail past the main shrouds must not pull you up into them
                // (on a carrack whose quarterdeck comes to the mainmast, from its stairs' foot)
                var m = new DeckMover(Math.Max(mainX - 3.5, qd + 1.7), 3.5 + D, 2.9);
                m.Unstick(g);
                bool climbed = false;
                for (int i = 0; i < 120; i++) { m.Step(g, 1 / 60f, 1, 0, 3.5 + D, false, false, 1, def.Ladders, 1); climbed |= m.Climbing != null; }
                Check(!climbed && m.X > mainX + 0.5, $"walking along the rail past the shrouds stays on deck (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00})");
            }

            Console.WriteLine("stairs feel (largest height change of the seat in one 1/60 s tick)");
            Smoothness(g, "walking up to the forecastle", fcDoor - 4.0, 3.5 + D, zF, 1, 0, 3.5, 0.08);
            Smoothness(g, "sprinting up to the forecastle", fcDoor - 4.0, 3.5 + D, zF, 1, 0, 7.0, 0.15);
            Smoothness(g, "walking up to the quarterdeck", qd + 3.5, 3.5 + D, zQ, -1, 0, 3.5, 0.08);

            MovingParts(def);
            SailsTurn(def, Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)));
            DryHull(def);
            LookingAt(def);
            Damage(def);
            Drive(def);
            Pointing(def);
            Sails(def, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "..")));
            DamageSkin(def, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "..")));
            FloodWater(def);
            SpawnPoint(def);
            Shadows(def, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "..")));
            Cannons(def, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "..")));
            Wrecks(def);

            RidingTheWaves(def);
            return def;
        }

        /// <summary>
        /// Hansa's own ship's boat (a launch): you stand and walk in her, over her thwarts, not out over her sides unless
        /// you jump; she's dry inside; her fixtures (the tiller, two rowing thwarts, the folding mast, two places for cargo)
        /// are where they should be; the oars go out only while someone rows; rowing and sailing add up; she fits the
        /// carrack's gallows.
        /// </summary>
        static void Boat(ShipDefinition boat, ShipDefinition carrack)
        {
            Console.WriteLine("the ship's boat");
            var g = boat.Grid;
            Check(g.CountSolid() > 100 && boat.MaxAboard == 4 && boat.BoardFromWater && boat.PickUpItem == "hansa:boat-skiff" && boat.RowSpeed > 1,
                $"a launch for four, climbed into from the water, picked up as the ship's boat item, rowed ({g.CountSolid()} voxels)");
            foreach (var b in boat.BoardPoints)
                Check(!g.Collides(b[0] - 0.3, b[1], b[2] - 0.3, b[0] + 0.3, b[1] + 1.85, b[2] + 0.3) && DeckMover.HasFloor(g, b[0], b[1], b[2]),
                    $"board point {b[0]},{b[1]},{b[2]} in her is free with floor");
            double floor = boat.BoardPoints[0][1];
            Walk(g, "stand in her on the bottom boards", -0.25, floor, 0.0, 0, 0, 1, false, false, (m, res) => Math.Abs(m.Y - floor) < 0.01 && m.OnGround);
            Walk(g, "walk aft over the rowing thwarts to the stern sheets", 0.875, floor, 0.0, -1, 0, 2, false, false,
                (m, res) => res == DeckMover.Result.Ok && m.X < -1.8);
            Walk(g, "walk forward over the thwarts to the bow locker (and no further)", -1.5, floor, 0.0, 1, 0, 3, false, false,
                (m, res) => res == DeckMover.Result.Ok && m.X > 1.5 && m.X < 2.0);
            Walk(g, "from the stern sheets you can't step out over the transom", -2.15, floor + 0.5, 0.0, -1, 0, 2, false, false,
                (m, res) => res == DeckMover.Result.Ok && m.X > -2.5 && m.Y < floor + 0.6);
            Walk(g, "her side stops you walking out", -0.2, floor, 0.0, 0, 1, 2, false, false, (m, res) => res == DeckMover.Result.Ok && m.Z < 1.0);
            Walk(g, "and from a thwart you can't step out over it either", -0.75, floor + 0.5, 0.0, 0, 1, 2, false, false,
                (m, res) => res == DeckMover.Result.Ok && m.Z < 1.0);
            Walk(g, "but you can jump out over her side", -0.2, floor, 0.3, 0, 1, 3, true, false, (m, res) => res == DeckMover.Result.Overboard);
            {
                // dry inside: every air cell between her sides below the gunwale, none outside her
                var dry = boat.DryMask;
                int cells = 0, wet = 0, outside = 0, wetOut = 0;
                for (double x = -2.2; x <= 2.2; x += 0.25)
                    for (double y = floor + 0.1; y < boat.DeckY - 0.3; y += 0.25)
                    {
                        if (!g.Collides(x - 0.1, y, -0.1, x + 0.1, y + 0.1, 0.1)) { cells++; if (!dry.SolidAt(x, y, 0)) wet++; }
                        outside++;
                        if (dry.SolidAt(x, y, boat.Beam / 2 + 0.4)) wetOut++;
                    }
                Check(cells > 20 && wet == 0 && wetOut == 0, $"dry inside her ({wet} of {cells} air cells wet), not beside her ({wetOut} of {outside})");
                var clip = boat.WaterClip;
                Check(clip?.DryProfile != null && clip.DryRake < 0, $"a detailed dry outline, ending at her raked transom ({clip?.DryRake:0.00} a block up)");
            }
            {
                var helm = boat.Fixture("helm");
                var oars = Array.FindAll(boat.Fixtures, f => f.Type == "oars");
                var mast = Array.Find(boat.Fixtures, f => f.Type == "mast");
                var cargo = Array.FindAll(boat.Fixtures, f => f.Type == "attach" && f.Kind == "cargo");
                var locker = Array.Find(boat.Fixtures, f => f.Type == "storage");
                Check(helm?.Pose == "sit" && helm.Pos[0] < -1.5 && Math.Abs(helm.Pos[2]) > 0.3, "the helm: the tiller, sitting on the stern sheets beside it");
                Check(locker != null && locker.Slots >= 8, $"a locker in the bow ({locker?.Slots} slots)");
                Check(oars.Length == 2 && Array.TrueForAll(oars, f => f.Pose == "sit"), $"two rowing thwarts ({oars.Length})");
                Check(mast != null && mast.StartOpen && mast.Opens, "a mast that folds, lowered on a new boat");
                Check(cargo.Length == 1, $"a place for a chest, a vessel or a crate ({cargo.Length})");
                foreach (var f in cargo)
                {
                    var ab = ShipFittings.AttachBox(f);
                    bool clear = !g.Collides(ab[0] + 0.01, ab[1] + 0.01, ab[2] + 0.01, ab[3] - 0.01, ab[4] - 0.01, ab[5] - 0.01);
                    bool on = g.SolidAt((ab[0] + ab[3]) / 2, ab[1] - 0.1, (ab[2] + ab[5]) / 2);
                    Check(clear && on, $"{f.Code}: room for it on the bottom boards");
                }
                // the oars show while someone rows there, stowed otherwise; the sail and the yard only with the mast up
                foreach (var o in oars)
                {
                    var outOars = Array.FindAll(boat.MovingParts, p => p.Shown == "manned:" + o.Code);
                    var stowed = Array.FindAll(boat.MovingParts, p => p.Shown == "unmanned:" + o.Code);
                    Check(outOars.Length == 2 && stowed.Length == 1 && Array.TrueForAll(outOars, p => p.Source == "stroke:" + o.Code),
                        $"{o.Code}: two oars out with the stroke while rowed, stowed on the thwarts otherwise");
                }
                var lug = Array.Find(boat.Sails, s => s.Mast == mast?.Code);
                Check(lug != null && Array.Find(boat.MovingParts, p => p.Code == "yard-" + lug.Code)?.Shown == "shut:" + mast.Code,
                    "the lug sail and its yard only with the mast up");
                var known = new[] { "rotate", "slide", "follow", "static" };
                Check(Array.TrueForAll(boat.MovingParts, p => Array.IndexOf(known, p.Kind) >= 0 && (p.Kind == "follow" || p.Source == "rudder" || p.Source == "sheet" ||
                    p.Source.StartsWith("stroke:") || p.Source.StartsWith("open:") && boat.Fixture(p.Source.Substring(5))?.Opens == true)),
                    $"every moving part of hers has a known kind and something that moves it ({boat.MovingParts.Length} parts)");
            }
            {
                // rowing: one rower, two go √2 as fast; backing water; with the sail the drives add up to her top speed
                double one = Oars.Target(1, boat.RowSpeed), two = Oars.Target(2, boat.RowSpeed), back = Oars.Target(-1, boat.RowSpeed);
                Check(Math.Abs(one - boat.RowSpeed) < 1e-6 && Math.Abs(two - boat.RowSpeed * Math.Sqrt(2)) < 1e-5 && back < 0 && Oars.Target(0, boat.RowSpeed) == 0 &&
                      Oars.Target(1, 0) == 0, $"rowing: one rower {one:0.0} b/s, two {two:0.0}, backing water {back:0.0}");
                Check(Math.Abs(Oars.Combine(2.0f, 1.9f, boat.MaxSpeed) - boat.MaxSpeed) < 1e-6 && Math.Abs(Oars.Combine(1.0f, 1.9f, boat.MaxSpeed) - 2.9f) < 1e-5 &&
                      Oars.Combine(1.0f, -1.9f, boat.MaxSpeed) < 0, "rowing under sail: the drives add up, to her top speed at most; backing against the sail slows her");
            }
            {
                // two of her go on the carrack's gallows: the space there takes her, rudder unshipped
                double len = boat.Bounds[1][0] - boat.Bounds[0][0];
                double hullLen = 0, wid = boat.Beam;
                var o = boat.WaterClip.DryOutline;
                double first = double.NaN, last = double.NaN;
                for (int c = 0; c < o.NX; c++)
                    for (int j = 0; j < o.NY; j++)
                        if (o.HalfWidths[j * o.NX + c] > 0) { double x = o.X0 + c * o.DX; if (double.IsNaN(first) || x < first) first = x; if (double.IsNaN(last) || x > last) last = x; }
                hullLen = last - first;
                var places = Array.FindAll(carrack.Fixtures, f => f.Kind == "boat");
                bool fits = places.Length == 2 && Array.TrueForAll(places, f => f.Box[3] - f.Box[0] >= hullLen && f.Box[5] - f.Box[2] >= wid &&
                    f.Box[4] - f.Box[1] >= 1.5);
                Check(fits, $"she fits a place on the carrack's gallows ({hullLen:0.0} x {wid:0.0} in {places[0].Box[3] - places[0].Box[0]:0.0} x {places[0].Box[5] - places[0].Box[2]:0.0})");
            }
        }

        /// <summary>
        /// Riding Sea's waves: the tilted frame, the drawn waterline and passengers agree with it, the hull follows long
        /// swell and ignores chop, heads into beam seas the right way, heels in the wind, and every side gets the same
        /// motion from the same inputs.
        /// </summary>
        static void RidingTheWaves(ShipDefinition def)
        {
            Console.WriteLine("riding the waves: frames");
            var rnd = new Random(5);
            double maxErr = 0, maxRender = 0, maxPlane = 0, maxBody = 0;
            for (int i = 0; i < 300; i++)
            {
                double yaw = rnd.NextDouble() * Math.PI * 4 - Math.PI * 2, pitch = (rnd.NextDouble() - 0.5) * 0.5, roll = (rnd.NextDouble() - 0.5) * 0.8;
                double lx = rnd.NextDouble() * 40 - 20, ly = rnd.NextDouble() * 10 - 3, lz = rnd.NextDouble() * 10 - 5;
                ShipFrame.ToWorld(yaw, pitch, roll, lx, ly, lz, out double wx, out double wy, out double wz);
                ShipFrame.ToLocal(yaw, pitch, roll, wx, wy, wz, out double bx, out double by, out double bz);
                maxErr = Math.Max(maxErr, Math.Abs(bx - lx) + Math.Abs(by - ly) + Math.Abs(bz - lz));

                // what the renderer does to a model vertex
                var m = new Matrixf().RotateY(ShipFrame.ModelYaw(yaw)).RotateZ((float)pitch).RotateX((float)roll);
                var v = m.TransformVector(new Vec4f((float)lx, (float)ly, (float)lz, 1));
                maxRender = Math.Max(maxRender, Math.Abs(v.X - wx) + Math.Abs(v.Y - wy) + Math.Abs(v.Z - wz));

                // a passenger's body turned by the tilt in world axes ends up where the tilted deck puts it
                ShipFrame.ToWorld(yaw, lx, ly, lz, out double fx, out double fy, out double fz);
                var b = ShipFrame.TiltMatrix(new Matrixf(), yaw, pitch, roll).TransformVector(new Vec4f((float)fx, (float)fy, (float)fz, 1));
                maxBody = Math.Max(maxBody, Math.Abs(b.X - wx) + Math.Abs(b.Y - wy) + Math.Abs(b.Z - wz));

                // the water shader's plane (height at the origin plus slopes measured level along the keel and across)
                // holds every point of the tilted waterline
                const double h = 0.1;
                ShipFrame.WaterlinePlane(pitch, roll, h, out double y0, out double along, out double across);
                ShipFrame.ToWorld(yaw, pitch, roll, lx, h, lz, out double px, out double py, out double pz);
                double fwd = px * Math.Sin(yaw) + pz * Math.Cos(yaw), side = pz * Math.Sin(yaw) - px * Math.Cos(yaw);
                maxPlane = Math.Max(maxPlane, Math.Abs(y0 + along * fwd + across * side - py));
            }
            Check(maxErr < 1e-9, $"tilted local <-> world round trip (err {maxErr:e1})");
            Check(maxRender < 1e-3, $"the drawn tilt matches the tilted frame (err {maxRender:e1})");
            Check(maxBody < 1e-3, $"passengers lean with the deck (err {maxBody:e1})");
            Check(maxPlane < 1e-9, $"the water clip's plane is the tilted waterline (err {maxPlane:e1})");
            ShipFrame.ToWorld(0, 0.1, 0, 10, 0, 0, out _, out double bowY, out _);
            ShipFrame.ToWorld(0, 0, 0.1, 0, 0, 4, out _, out double stbY, out _);
            Check(bowY > 0.9 && stbY < -0.3, "pitch + lifts the bow, roll + puts starboard down");

            Console.WriteLine("riding the waves: the hull's answer");
            def.Waterplane(out double hl, out double hb, out double cx);
            Check(hl > 12 && hl < 21 && hb > 3.5 && hb < 6.5, $"waterplane measured from the hull: {2 * hl:0.0} x {2 * hb:0.0} blocks");
            var md = def.Motion;

            // one wave train at a time, a ship at the origin heading +Z (yaw 0: bow +Z, starboard -X)
            ShipAttitude[] Track(double lambda, double dirDeg, double amp, double speed, double seconds, double yaw, out double[] surface, out double[] slope)
            {
                var p = new Sea.WavePlace();
                double k = 2 * Math.PI / lambda, d = dirDeg * Math.PI / 180;
                int n = (int)(seconds * 20);
                var r = new ShipAttitude[n];
                surface = new double[n];
                slope = new double[n];
                double x = 0, z = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = 100 + i / 20.0;
                    p.SetTrains(t, 1, new[] { Math.Cos(d) * k }, new[] { Math.Sin(d) * k }, new[] { amp }, new[] { 0.3 });
                    x = Math.Sin(yaw) * speed * i / 20.0;
                    z = Math.Cos(yaw) * speed * i / 20.0;
                    r[i] = ShipMotion.Waves(p, 1, t, x, z, yaw, speed, hl, hb, 0, md);
                    surface[i] = p.HeightAt(x, z);
                    // the surface's slope along the keel at the ship
                    slope[i] = (p.HeightAt(x + Math.Sin(yaw) * 0.01, z + Math.Cos(yaw) * 0.01) - p.HeightAt(x - Math.Sin(yaw) * 0.01, z - Math.Cos(yaw) * 0.01)) / 0.02;
                }
                return r;
            }
            static double Amp(double[] a) { double m = 0; foreach (var v in a) m = Math.Max(m, Math.Abs(v)); return m; }
            static double Corr(double[] a, double[] b)
            {
                double ab = 0, aa = 0, bb = 0;
                for (int i = 0; i < a.Length; i++) { ab += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
                return ab / Math.Sqrt(aa * bb + 1e-30);
            }
            double[] Heave(ShipAttitude[] r) => Array.ConvertAll(r, a => a.Heave);
            double[] Pitch(ShipAttitude[] r) => Array.ConvertAll(r, a => a.Pitch);
            double[] Roll(ShipAttitude[] r) => Array.ConvertAll(r, a => a.Roll);

            {
                // a long swell from ahead (running toward -Z, yaw 0 means bow +Z): she rides it
                var r = Track(150, 270, 1.0, 0, 30, 0, out var surf, out var slope);
                double ha = Amp(Heave(r)), pa = Amp(Pitch(r)), ra = Amp(Roll(r)), sa = Amp(slope);
                double hc = Corr(Heave(r), surf), pc = Corr(Pitch(r), Array.ConvertAll(slope, Math.Atan));
                Check(ha > 0.85 && ha < 1.35 && hc > 0.9, $"long swell ahead: she rises and falls with it (heave {ha:0.00} for a 1.00 wave, in step {hc:0.00})");
                Check(pa > 0.8 * Math.Atan(sa) && pa < 1.5 * Math.Atan(sa) && pc > 0.85, $"… and pitches with its slope ({pa * 180 / Math.PI:0.0}° for {Math.Atan(sa) * 180 / Math.PI:0.0}°, in step {pc:0.00})");
                Check(ra < 1e-6, $"… without rolling ({ra * 180 / Math.PI:0.000}°)");
            }
            {
                // the same swell on the beam: she rolls, not pitches
                var r = Track(80, 180, 0.8, 0, 30, 0, out _, out _);
                double pa = Amp(Pitch(r)), ra = Amp(Roll(r));
                Check(ra > 0.03 && pa < 1e-6, $"beam sea: she rolls ({ra * 180 / Math.PI:0.0}°), doesn't pitch ({pa * 180 / Math.PI:0.000}°)");
            }
            {
                // chop much shorter than the hull hardly moves her
                var r = Track(6.5, 237, 0.25, 0, 20, 0, out var surf, out _);
                double ha = Amp(Heave(r)), pa = Amp(Pitch(r)), ra = Amp(Roll(r));
                Check(ha < 0.02 && pa < 0.3 * Math.PI / 180 && ra < 0.5 * Math.PI / 180,
                    $"chop 6.5 blocks long, 0.25 high: heave {ha:0.000}, pitch {pa * 180 / Math.PI:0.00}°, roll {ra * 180 / Math.PI:0.00}° (the water itself {Amp(surf):0.00})");
            }
            {
                // running with a wave at its own speed she stays on the same part of it; sailing into it she meets it faster
                double lambda = 40, c = Math.Sqrt(9.81 * lambda / (2 * Math.PI));
                var surfing = Track(lambda, 90, 0.5, c, 6, 0, out _, out _);
                var still = Track(lambda, 90, 0.5, 0, 6, 0, out _, out _);
                var into = Track(lambda, 270, 0.5, 6, 6, 0, out _, out _);
                double Spread(ShipAttitude[] r) { double lo = double.MaxValue, hi = double.MinValue; foreach (var a in r) { lo = Math.Min(lo, a.Heave); hi = Math.Max(hi, a.Heave); } return hi - lo; }
                int Crossings(ShipAttitude[] r) { int n = 0; for (int i = 1; i < r.Length; i++) if (Math.Sign(r[i].Heave) != Math.Sign(r[i - 1].Heave)) n++; return n; }
                Check(Spread(surfing) < 0.01 * Spread(still), $"surfing a wave at its speed ({c:0.0} b/s): she stays put on it ({Spread(surfing):0.000} vs {Spread(still):0.00} at rest)");
                Check(Crossings(into) > Crossings(still), $"sailing into the waves she meets them more often ({Crossings(into)} vs {Crossings(still)} in 6 s)");
            }
            {
                // a storm sea: rolls stay within the design's limit, and the same inputs give the same motion wherever
                // they're worked out (server and clients), whatever the tick or frame rate
                var spec = new Sea.WaveSpectrum(4242, 0);
                var storm = new Sea.SeaState(3.6, 0, 1, 8.6);
                var gale = Sea.Wind.FromCompass(0, 1.5);
                var server = new Sea.WavePlace();
                var client = new Sea.WavePlace();
                double worst = 0, maxRoll = 0, maxPitch = 0, maxHeave = 0;
                double tc = 500;
                for (double t = 500; t < 560; t += 0.05)
                {
                    server.Update(spec, t, storm, gale);
                    var a = ShipMotion.Waves(server, 1, t, 1000, 2000, 1.3, 3, hl, hb, cx, md);
                    while (tc + 1 / 144.0 < t) { tc += 1 / 144.0; client.Update(spec, tc, storm, gale); }
                    client.Update(spec, t, storm, gale);
                    var b = ShipMotion.Waves(client, 1, t, 1000, 2000, 1.3, 3, hl, hb, cx, md);
                    worst = Math.Max(worst, Math.Abs(a.Heave - b.Heave) + Math.Abs(a.Pitch - b.Pitch) + Math.Abs(a.Roll - b.Roll));
                    maxRoll = Math.Max(maxRoll, Math.Abs(a.Roll));
                    maxPitch = Math.Max(maxPitch, Math.Abs(a.Pitch));
                    maxHeave = Math.Max(maxHeave, Math.Abs(a.Heave));
                }
                Check(worst < 1e-12, $"the server and a client at another frame rate get the same motion (worst {worst:e1})");
                Check(maxRoll < md.MaxRoll * Math.PI / 180 && maxPitch < md.MaxPitch * Math.PI / 180,
                    $"a violent storm (4 blocks, beam on): roll up to {maxRoll * 180 / Math.PI:0} of {md.MaxRoll}°, pitch {maxPitch * 180 / Math.PI:0} of {md.MaxPitch}°, heave ±{maxHeave:0.0}");
                // half a block apart (a client's smoothing) changes little
                var c = ShipMotion.Waves(server, 1, 559.95, 1000.5, 2000, 1.3, 3, hl, hb, cx, md);
                var d0 = ShipMotion.Waves(server, 1, 559.95, 1000, 2000, 1.3, 3, hl, hb, cx, md);
                Check(Math.Abs(c.Heave - d0.Heave) < 0.15 && Math.Abs(c.Roll - d0.Roll) < 0.02, $"half a block off changes it a little (heave {Math.Abs(c.Heave - d0.Heave):0.000}, roll {Math.Abs(c.Roll - d0.Roll) * 180 / Math.PI:0.00}°)");
                // sheltered water (a lake) gets a fifth, like the water shader draws it
                var lake = ShipMotion.Waves(server, 0.2, 559.95, 1000, 2000, 1.3, 3, hl, hb, cx, md);
                Check(Math.Abs(lake.Heave - 0.2 * d0.Heave) < 0.01 && Math.Abs(lake.Roll - 0.2 * d0.Roll) < 0.002,
                    $"on sheltered water she moves with the smaller waves drawn there (heave {lake.Heave:0.000} for {d0.Heave:0.000} on the open sea)");
            }

            Console.WriteLine("riding the waves: heel");
            {
                // yaw 0: the bow points +Z, starboard is -X; wind (-0.6, 0) blows from port toward starboard
                var sd = def.Sailing;
                double Heel(double wx, double wz, double speed, double sail) => ShipMotion.Heel(wx, wz, 0, speed, sail, md, sd) * 180 / Math.PI;
                double full = Heel(-0.6, 0, 0, 1), port = Heel(0.6, 0, 0, 1), half = Heel(-0.6, 0, 0, 0.55);
                double ahead = Heel(0, -0.6, 0, 1), furled = Heel(-0.6, 0, 0, 0), storm = Heel(-1.5, 0, 0, 1);
                Check(full > 4 && full < 9, $"a strong breeze on the beam under full sail leans her {full:0.0}° to leeward (starboard down)");
                Check(Math.Abs(port + full) < 1e-9, $"from the other side, the other way ({port:0.0}°)");
                Check(half > 0.4 * full && half < 0.7 * full && Math.Abs(ahead) < 1e-9 && furled == 0,
                    $"less with half sail ({half:0.0}°), none head to wind ({ahead:0.0}°) or with the sails furled");
                Check(storm < md.MaxHeel && storm > 0.8 * md.MaxHeel, $"never more than {md.MaxHeel}° ({storm:0.0}° under full sail in a violent storm)");
                // gusts (Sea's: ±45 % of the wind at most, coming and going over 7–15 s) lean her far less than they would at
                // once: her lean follows over HeelResponse seconds, as clients and the server ease it
                {
                    double raw0 = double.MaxValue, raw1 = double.MinValue, eased0 = double.MaxValue, eased1 = double.MinValue, eased = Heel(-0.5, 0, 0, 1);
                    for (double t = 0; t < 120; t += 0.05)
                    {
                        double gust = 1 + 0.3 * Math.Sin(2 * Math.PI * t / 10) + 0.15 * Math.Sin(2 * Math.PI * t / 4.7);
                        double h = Heel(-0.5 * gust, 0, 0, 1);
                        eased += (h - eased) * (1 - Math.Exp(-0.05 / md.HeelResponse));
                        if (t < 30) continue;
                        raw0 = Math.Min(raw0, h); raw1 = Math.Max(raw1, h); eased0 = Math.Min(eased0, eased); eased1 = Math.Max(eased1, eased);
                    }
                    Check(eased1 - eased0 < 0.45 * (raw1 - raw0),
                        $"in gusts her lean swings {eased0:0.0}–{eased1:0.0}°, not {raw0:0.0}–{raw1:0.0}° (it follows over {md.HeelResponse} s)");
                }
                // the sails feel the apparent wind: close-hauled with way on it's stronger; before the wind nothing presses her over
                double closeW = Math.Cos(65 * Math.PI / 180), closeS = Math.Sin(65 * Math.PI / 180);   // from 65° off the port bow
                double atRest = Heel(-0.6 * closeS, -0.6 * closeW, 0, 1), underWay = Heel(-0.6 * closeS, -0.6 * closeW, 3, 1);
                Check(underWay > atRest && atRest > 0, $"close-hauled she heels more with way on ({atRest:0.0}° → {underWay:0.0}°)");
                Check(Math.Abs(Heel(0, 0.6, 5, 1)) < 1e-9, "running before the wind she stays upright");
            }

            Console.WriteLine("sailing by the wind");
            {
                var sd = def.Sailing;
                // yaw 0: the bow points +Z, starboard -X. Where the air goes, from a compass-free angle off the bow:
                static (double x, double z) From(double angleOffBow, int side, double strength)
                {
                    double a = angleOffBow * Math.PI / 180;
                    // "from" direction in world: ahead +Z, starboard -X; the air goes the other way
                    double fx = -side * Math.Sin(a), fz = Math.Cos(a);
                    return (-fx * strength, -fz * strength);
                }
                var (ax, az) = From(0, 1, 0.5);
                double a0 = Sailing.WindAngle(ax, az, 0, out _);
                (ax, az) = From(180, 1, 0.5);
                double a180 = Sailing.WindAngle(ax, az, 0, out _);
                (ax, az) = From(90, 1, 0.5);
                double a90 = Sailing.WindAngle(ax, az, 0, out int s90);
                (ax, az) = From(90, -1, 0.5);
                Sailing.WindAngle(ax, az, 0, out int s90p);
                Check(Math.Abs(a0) < 1e-9 && Math.Abs(a180 - 180) < 1e-9 && Math.Abs(a90 - 90) < 1e-9 && s90 == 1 && s90p == -1,
                    "the wind's angle off the bow and its side (ahead 0°, astern 180°, starboard beam 90° from starboard)");
                (ax, az) = From(90, 1, 0.5);
                Check(Math.Abs(Sailing.WindAngle(ax, az, 0, out _) - Sailing.WindAngle(Math.Cos(1.1) * ax + Math.Sin(1.1) * az, -Math.Sin(1.1) * ax + Math.Cos(1.1) * az, 1.1, out _)) < 1e-9,
                    "… the same whichever way she heads");

                double V(double angle, double strength, double sail) => def.MaxSpeed * Sailing.SpeedFactor(angle, strength, sail, sd);
                Console.WriteLine($"         medium breeze, full sail: close-hauled {V(70, 0.3, 1):0.0}, beam {V(90, 0.3, 1):0.0}, broad {V(130, 0.3, 1):0.0}, running {V(180, 0.3, 1):0.0} b/s");
                Check(V(40, 0.3, 1) <= 0 && V(0, 0.3, 1) < 0 && V(0, 0.3, 1) > -1, $"head to wind she can't sail: in irons, taken aback right ahead ({V(0, 0.3, 1):0.00} b/s astern)");
                Check(V(70, 0.3, 1) < V(90, 0.3, 1) && V(90, 0.3, 1) < V(130, 0.3, 1) && V(180, 0.3, 1) < V(130, 0.3, 1) && V(180, 0.3, 1) > V(90, 0.3, 1) * 0.8,
                    "fastest on a broad reach, slower close-hauled, still good running before the wind");
                Check(V(130, 0.15, 1) < V(130, 0.3, 1) && V(130, 0.3, 1) < V(130, 0.6, 1) && V(130, 1.0, 1) <= def.MaxSpeed + 1e-9 && V(130, 1.0, 1) - V(130, 0.6, 1) < 0.5,
                    $"more wind, more speed, until the hull limits her (light {V(130, 0.15, 1):0.0}, medium {V(130, 0.3, 1):0.0}, strong {V(130, 0.6, 1):0.0}, storm {V(130, 1.0, 1):0.0} of {def.MaxSpeed})");
                Check(V(130, 1.0, 0.55) > 0.9 * V(130, 1.0, 1) && V(130, 0.15, 0.55) < 0.85 * V(130, 0.15, 1),
                    "in a gale half sail is nearly as fast as full; in light airs she wants all she has");
                Check(V(0, 0.3, 0) == 0 && V(130, 0, 1) == 0, "no sail or no wind, no way");

                Sailing.Trim(180, 1, sd, out double b180, out double s180);
                Sailing.Trim(40, 1, sd, out double b45, out double s45);
                Sailing.Trim(40, -1, sd, out double b45p, out _);
                Check(Math.Abs(b180) < 1e-9 && Math.Abs(b45 - sd.MaxBrace * Math.PI / 180) < 1e-9 && Math.Abs(b45p + b45) < 1e-9,
                    "the yards are square before the wind and braced hard round close-hauled (wind from starboard: the starboard yardarm forward)");
                {
                    // Does the trim actually drive her? In the ship's level frame (x bow, z starboard): a yard turned by
                    // brace b about +Y runs along (sin b, cos b); the sail is pushed along its normal on the lee side.
                    // It must fill from aft and push her forward on every point of sail, and do better than bracing the
                    // other way (which takes the sails aback).
                    int bad = 0;
                    double worst = double.MaxValue;
                    foreach (int side in new[] { 1, -1 })
                        for (double awa = 55; awa <= 175; awa += 5)
                        {
                            double a = awa * Math.PI / 180, wx = -Math.Cos(a), wz = -side * Math.Sin(a);   // where the air goes
                            Sailing.Trim(awa, side, sd, out double b, out _);
                            double Drive(double brace)
                            {
                                double nx = Math.Cos(brace), nz = -Math.Sin(brace);
                                if (nx * wx + nz * wz < 0) { nx = -nx; nz = -nz; }   // the lee side
                                return nx;
                            }
                            worst = Math.Min(worst, Drive(b));
                            if (Drive(b) <= 0 || Drive(b) < Drive(-b) - 1e-9) bad++;
                        }
                    Check(bad == 0, $"the braced square sails fill from aft and drive her forward from close-hauled to running, both tacks (least drive {worst:0.00})");

                    // the lateen: its sail runs along the yard, whose after end the sheet turns to (-cos s, sin s)
                    bad = 0;
                    worst = double.MaxValue;
                    foreach (int side in new[] { 1, -1 })
                        for (double awa = 55; awa <= 175; awa += 5)
                        {
                            double a = awa * Math.PI / 180, wx = -Math.Cos(a), wz = -side * Math.Sin(a);
                            Sailing.Trim(awa, side, sd, out _, out double s);
                            double nx = Math.Sin(s), nz = Math.Cos(s);
                            if (nx * wx + nz * wz < 0) { nx = -nx; nz = -nz; }
                            bool toLee = Math.Sin(s) * side < 0;   // the after end on the side away from the wind
                            worst = Math.Min(worst, nx);
                            if (nx <= 0 || !toLee) bad++;
                        }
                    Check(bad == 0, $"the lateen sits to leeward and drives her forward on every point of sail, both tacks (least drive {worst:0.00})");
                }
                Check(s45 < 0 && s180 < s45 && Math.Abs(s180 + sd.MaxSheet * Math.PI / 180) < 1e-9,
                    $"a fore-and-aft sail swings out to leeward, further the freer the wind, never past its shrouds ({sd.MaxSheet}°)");
                var lateen = Array.Find(def.MovingParts, p => p.Source == "sheet");
                Check(lateen == null || lateen.Limit <= 0 || sd.MaxSheet <= lateen.Limit + 1e-3,
                    $"the lateen is trimmed no further than it's drawn ({sd.MaxSheet}° of {lateen?.Limit}°)");

                Check(Math.Abs(Sailing.Steer(7, 7, false, sd) - 1) < 1e-9 && Math.Abs(Sailing.Steer(0, 7, false, sd) - sd.SlowSteer) < 1e-9 &&
                      Math.Abs(Sailing.Steer(0, 7, true, sd) - sd.TackSteer) < 1e-9 && Sailing.Steer(-2, 7, false, sd) < 0 &&
                      Math.Abs(Sailing.Steer(-0.4, 7, true, sd) - sd.TackSteer) < 1e-9,
                    "the rudder bites with way on, little without, and backing the head sails helps her round when tacking");

                // a voyage, tick by tick as the server sails her: pick up speed on a reach, tack through the wind, and
                // lose way pointing into it (wind from the north, 0.3; yaw is where the bow points)
                var wind = Sea.Wind.FromCompass(0, 0.3);
                float speed = 0, dt = 0.05f;
                double yaw = 0;
                void Tick(float rudder)
                {
                    float target = (float)Sailing.TargetSpeed(wind.X, wind.Z, yaw, 1, def.MaxSpeed, sd);
                    speed += Math.Clamp(target - speed, -def.Deceleration * dt, def.Acceleration * dt);
                    bool irons = Sailing.WindAngle(wind.X, wind.Z, yaw, out _) < sd.NoGo;
                    yaw += -rudder * def.TurnRate * Math.PI / 180 * Sailing.Steer(speed, def.MaxSpeed, irons, sd) * dt;
                }
                // yaw 0 heads +Z (south): wind from the north is right astern; yaw π/2 heads east, a beam reach
                yaw = Math.PI / 2;
                for (int i = 0; i < 60 * 20; i++) Tick(0);
                double reach = speed;
                Check(reach > 3 && reach < def.MaxSpeed, $"from rest, a beam reach in a medium breeze gets her to {reach:0.0} b/s in a minute");
                // come up to close-hauled, then tack: put the helm over to port side of the wind and hold it
                double Off() => Sailing.WindAngle(wind.X, wind.Z, yaw, out _);
                int ticks = 0;
                while (Off() > 70 && ticks < 60 * 20) { Tick(-1); ticks++; }
                for (int i = 0; i < 20 * 20; i++) Tick(0);   // settle close-hauled
                double closeSpeed = speed;
                Sailing.WindAngle(wind.X, wind.Z, yaw, out int sideBefore);
                ticks = 0;
                bool crossed = false;
                while (ticks < 120 * 20)
                {
                    Tick(-1);
                    ticks++;
                    Sailing.WindAngle(wind.X, wind.Z, yaw, out int sideNow);
                    crossed |= sideNow != sideBefore;
                    if (crossed && Off() > sd.NoGo + 3) break;
                }
                Check(crossed && Off() > sd.NoGo && ticks < 60 * 20, $"tacking: close-hauled at {closeSpeed:0.0} b/s she comes through the wind onto the other tack in {ticks / 20.0:0} s");
                // point right into the wind and hold her there: she loses way and is pushed astern
                yaw = Math.PI;   // heading north, into the wind
                speed = 4;
                for (int i = 0; i < 30 * 20; i++)
                {
                    float target = (float)Sailing.TargetSpeed(wind.X, wind.Z, yaw, 1, def.MaxSpeed, sd);
                    speed += Math.Clamp(target - speed, -def.Deceleration * dt, def.Acceleration * dt);
                }
                Check(speed < 0 && speed > -1, $"head to wind she loses her way and gathers sternway ({speed:0.00} b/s)");
            }

            Console.WriteLine("riding the waves: the view on deck");
            {
                ShipCamera.ViewTilt(0.1, 0, 0, out double up, out double hr);
                Check(Math.Abs(up - 0.1) < 1e-12 && Math.Abs(hr) < 1e-12, "looking at the bow as it rises, you look up; the horizon stays level");
                ShipCamera.ViewTilt(0, 0.1, 0, out up, out hr);
                Check(Math.Abs(up) < 1e-12 && Math.Abs(hr - 0.1) < 1e-12, "looking at the bow as she rolls to starboard, your head tips right");
                // looking to starboard: an entity looking along (sin y, cos y) with the ship at yaw 0 (starboard -X) has y = -π/2
                ShipCamera.ViewTilt(0.1, 0.05, -Math.PI / 2, out up, out hr);
                Check(Math.Abs(up + 0.05) < 1e-12 && Math.Abs(hr - 0.1) < 1e-12, "looking to starboard as she rolls that way you look down, and a rising bow (on your left) tips your head right");
                // the roll turns the picture about the view axis: the crosshair stays put, the right side of the horizon rises
                var view = new double[16]; for (int i = 0; i < 4; i++) view[i * 5] = 1;
                ShipCamera.ApplyRoll(view, 0.2);
                double rx = view[0] * 1 + view[4] * 0, ry = view[1] * 1 + view[5] * 0;
                double cx0 = view[8] * -1, cy0 = view[9] * -1;
                Check(ry > 0.19 && Math.Abs(cx0) < 1e-12 && Math.Abs(cy0) < 1e-12, $"a head tipped right sees the world turn anticlockwise, the view axis stays ({rx:0.00}, {ry:0.00})");
            }
        }

        static double[] Apply(float[] m, double[] p) => new[]
        {
            m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
            m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
            m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14],
        };

        static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));

        /// <summary>
        /// The parts the renderer moves: the ropes follow the tiller and the anchor, the tiller clears the mizzen at full
        /// helm, the gates swing inboard, the hatches' leaves swing up, the anchor goes down under the keel.
        /// </summary>
        static void MovingParts(ShipDefinition def)
        {
            Console.WriteLine("moving parts");
            MovingPartDef P(string code) => Array.Find(def.MovingParts, p => p.Code == code);
            var known = new[] { "rotate", "slide", "follow" };
            Check(Array.TrueForAll(def.MovingParts, p => Array.IndexOf(known, p.Kind) >= 0 &&
                    (p.Source == "rudder" || p.Source == "anchor" || p.Source == "brace" || p.Source == "sheet" || p.Source.StartsWith("open:") && def.Fixture(p.Source.Substring(5))?.Opens == true ||
                     // (0.3.8) a gun port's lid, opened with its gun's port
                     p.Source.StartsWith("open:") && def.Fixture(p.Source.Substring(5)) is FixtureDef lg && lg.Type == "gun" && lg.Lid == p.Code ||
                     p.Source.StartsWith("pump:") && def.Fixture(p.Source.Substring(5))?.Type == "pump" ||
                     p.Source.StartsWith("fire:") && def.Fixture(p.Source.Substring(5))?.Type == "gun" ||
                     p.Source.StartsWith("train:") && def.Fixture(p.Source.Substring(6))?.Type == "gun" ||
                     p.Source.StartsWith("elevate:") && def.Fixture(p.Source.Substring(8))?.Type == "gun") &&
                    (p.Parent == null || Array.FindIndex(def.MovingParts, q => q.Code == p.Parent) is int pi && pi >= 0 && pi < Array.IndexOf(def.MovingParts, p))),
                $"every moving part has a known kind and something that moves it, and rides only on a part listed before it ({def.MovingParts.Length} parts)");

            var tiller = P("tiller");
            var full = new float[16];
            PartMotion.PartMatrix(tiller, tiller.Angle * (float)(Math.PI / 180), full);
            foreach (var side in new[] { "starboard", "port" })
            {
                var rope = P("tillerrope-" + side);
                var m = new float[16];
                PartMotion.FollowMatrix(rope, full, m);
                double[] tipNow = Apply(full, rope.Tip), ropeTip = Apply(m, rope.Tip), anchor = Apply(m, rope.Anchor);
                Check(Dist(tipNow, ropeTip) < 1e-3 && Dist(anchor, rope.Anchor) < 1e-4, $"the {side} tiller rope stays on the tiller's end at full helm ({Dist(tipNow, ropeTip):0.0000})");
            }
            const double mizzenX = -12.5, mizzenR = 0.3;
            double[] tipRest = P("tillerrope-port").Tip, tipFull = Apply(full, tipRest);
            Check(Math.Abs(tipRest[0] - mizzenX) > mizzenR + 0.15 && Math.Sqrt((tipFull[0] - mizzenX) * (tipFull[0] - mizzenX) + tipFull[2] * tipFull[2]) > mizzenR + 0.15,
                $"the tiller swings clear of the mizzen (its end at {tipRest[0]:0.00} amidships, {tipFull[0]:0.00},{tipFull[2]:0.00} at full helm)");
            Check(tipFull[1] > def.DeckY - 3.0 + 1.95, $"the tiller swings above head height on the lower deck (y {tipFull[1]:0.00})");

            var anchorPart = P("anchor");
            var down = new float[16];
            PartMotion.SlideMatrix(anchorPart, anchorPart.Distance, down);
            var cable = P("anchorcable");
            var cm = new float[16];
            PartMotion.FollowMatrix(cable, down, cm);
            Check(Dist(Apply(cm, cable.Tip), Apply(down, cable.Tip)) < 1e-3 && Apply(down, cable.Tip)[1] < def.KeelY + 1,
                $"the anchor goes down with its cable on it (ring at y {Apply(down, cable.Tip)[1]:0.0})");

            foreach (var side in new[] { "starboard", "port" })
            {
                var gate = P("gate-" + side);
                var m = new float[16];
                PartMotion.PartMatrix(gate, gate.Angle * (float)(Math.PI / 180), m);
                // its free edge: the end of the opening away from the hinge
                var gf = def.Fixture("gate-" + side);
                double far = (gf.Box[0] + gf.Box[3]) / 2 > gate.AxisPoint[0] ? 1.1 : -1.1;
                double[] edge = { gate.AxisPoint[0] + far, 4.0, gate.AxisPoint[2] }, moved = Apply(m, edge);
                Check(Math.Abs(moved[2]) < Math.Abs(edge[2]) - 0.8, $"the {side} entry port gate swings inboard ({edge[2]:0.00} → {moved[2]:0.00})");
            }
            // furled, a sail's clew goes up to its yard, and its sheet with it (no rope left hanging in the air)
            foreach (var code in new[] { "sheet-main-course-starboard", "sheet-fore-course-port", "sheet-mizzen-lateen-aft", "sheet-main-topsail-port" })
            {
                var sheet = P(code);
                var sail = sheet == null ? null : Array.Find(def.Sails, x => x.Code == sheet.Sail);
                Check(sheet?.Sail != null && sail != null, $"{code}: follows its sail's furl");
                if (sheet == null || sail == null) continue;
                var ident = new float[16];
                Mat4f.Identity(ident);
                var furl = new float[16];
                PartMotion.FurlMatrix(sail, PartMotion.DrawnSail(0), furl);
                var m = new float[16];
                PartMotion.FollowMatrix(sheet, furl, m, sheet.AnchorFollow != null ? ident : null);
                double[] clew = Apply(furl, sheet.Tip), end = Apply(m, sheet.Tip), anchor = Apply(m, sheet.Anchor);
                // the clew, furled, is close to the yard line (the sail's axis)
                double[] a = sail.AxisPoint, u = sail.AxisDir;
                double[] rel = { clew[0] - a[0], clew[1] - a[1], clew[2] - a[2] };
                double along = rel[0] * u[0] + rel[1] * u[1] + rel[2] * u[2];
                double off = Math.Sqrt(Math.Max(0, rel[0] * rel[0] + rel[1] * rel[1] + rel[2] * rel[2] - along * along));
                Check(Dist(end, clew) < 1e-3 && Dist(anchor, sheet.Anchor) < 1e-4 && off < 0.8,
                    $"{code}: furled, the sheet's end goes up with the clew to the yard ({off:0.00} from it) and stays belayed");
            }
            {
                // the topsail's sheets run between the two yards: both ends move with them
                var ts = P("sheet-main-topsail-starboard");
                var course = P("yard-main-course");
                var braced = new float[16];
                PartMotion.PartMatrix(course, 30 * (float)(Math.PI / 180), braced);
                var ident = new float[16];
                Mat4f.Identity(ident);
                var m = new float[16];
                PartMotion.FollowMatrix(ts, braced, m, braced);
                Check(ts?.AnchorFollow == "yard-main-course" && Dist(Apply(m, ts.Anchor), Apply(braced, ts.Anchor)) < 1e-3 &&
                      Dist(Apply(m, ts.Tip), Apply(braced, ts.Tip)) < 1e-3, "the topsail's sheets stay on the course yardarms and the topsail's clews as both yards brace");
            }
            foreach (var code in new[] { "hatch-waist-s", "hatch-waist-p", "hatch-hold-s", "hatch-hold-p" })
            {
                var leaf = P(code);
                var m = new float[16];
                PartMotion.PartMatrix(leaf, leaf.Angle * (float)(Math.PI / 180), m);
                double[] inner = { 2.0, leaf.AxisPoint[1], 0.0 }, moved = Apply(m, inner);
                Check(moved[1] > leaf.AxisPoint[1] + 0.6 && Math.Abs(moved[2]) < Math.Abs(leaf.AxisPoint[2]) + 0.3,
                    $"{code}: the leaf swings up on its hinge (its inner edge to y {moved[1]:0.00}, z {moved[2]:0.00})");
            }
        }

        /// <summary>
        /// The sails turn with the wind (item 17): every sail hangs from a yard that turns about its mast, stays under it
        /// at any brace, the yardarms clear the shrouds, backstays and stays at full brace, and the braces and sheets
        /// stay on their yardarms and clews.
        /// </summary>
        static void SailsTurn(ShipDefinition def, Newtonsoft.Json.Linq.JObject raw)
        {
            Console.WriteLine("the sails turn with the wind");
            MovingPartDef P(string code) => Array.Find(def.MovingParts, p => p.Code == code);
            float rad = (float)(Math.PI / 180);
            foreach (var sail in def.Sails)
            {
                var yard = P("yard-" + sail.Code);
                bool ok = yard != null && (yard.Source == "brace" || yard.Source == "sheet") && yard.AxisDir[1] == 1 &&
                          Math.Abs(PartMotion.TurnDegrees(yard, 0.5f) - (yard.Limit > 0 ? Math.Min(0.5f * yard.Angle, yard.Limit) : 0.5f * yard.Angle)) < 1e-3;
                Check(ok && Math.Abs(0.5f * yard.Angle - 0.5f * 180 / Math.PI) < 1e-2, $"{sail.Code}: hangs from a yard turned about its mast by the crew's trim in radians ({yard?.Source})");
                if (yard == null) continue;
                // the sail's head, turned with its yard, stays on the turned yard
                double worst = 0;
                foreach (float deg in new[] { -45f, -20f, 0f, 20f, 45f })
                {
                    var m = new float[16];
                    PartMotion.PartMatrix(yard, PartMotion.TurnDegrees(yard, deg * rad) * rad, m);
                    foreach (double t in new[] { -3.0, 0.0, 3.0 })
                    {
                        double[] head = { sail.AxisPoint[0] + sail.AxisDir[0] * t, sail.AxisPoint[1] + sail.AxisDir[1] * t, sail.AxisPoint[2] + sail.AxisDir[2] * t };
                        // the yard's own line (its axis point and the sail's line are both on the yard): turned the same, it's the same place
                        double[] turnedHead = Apply(m, head);
                        double dx = turnedHead[0] - yard.AxisPoint[0], dz = turnedHead[2] - yard.AxisPoint[2];
                        double r0 = Math.Sqrt((head[0] - yard.AxisPoint[0]) * (head[0] - yard.AxisPoint[0]) + (head[2] - yard.AxisPoint[2]) * (head[2] - yard.AxisPoint[2]));
                        worst = Math.Max(worst, Math.Abs(Math.Sqrt(dx * dx + dz * dz) - r0) + Math.Abs(turnedHead[1] - head[1]));
                    }
                }
                Check(worst < 1e-3, $"{sail.Code}: stays under its yard whichever way it's braced (off by {worst:0.0000})");
            }

            var rig = raw["rigCheck"];
            Check(rig != null, "the design lists its standing rigging and yardarms for this check");
            if (rig == null) return;
            var standing = rig["standing"].ToObject<double[][]>();
            double maxBrace = def.Sailing.MaxBrace;
            foreach (var y in rig["yards"])
            {
                var part = P((string)y["part"]);
                double reach = part.Source == "sheet" ? (part.Limit > 0 ? part.Limit : 80) : maxBrace;
                double worstGap = double.MaxValue;
                foreach (double sgn in new[] { 1.0, -1.0 })
                {
                    var m = new float[16];
                    PartMotion.PartMatrix(part, PartMotion.TurnDegrees(part, (float)(sgn * reach * rad)) * rad, m);
                    foreach (var arm in y["arms"].ToObject<double[][]>())
                    {
                        double[] a = Apply(m, new[] { arm[0], arm[1], arm[2] }), b = Apply(m, new[] { arm[3], arm[4], arm[5] });
                        foreach (var st in standing)
                            worstGap = Math.Min(worstGap, SegDist(a, b, new[] { st[0], st[1], st[2] }, new[] { st[3], st[4], st[5] }) - arm[6] - st[6]);
                    }
                }
                Check(worstGap > 0, $"{part.Code}: braced round {reach:0}° each way its yardarms clear the shrouds, backstays and stays (by {worstGap:0.00})");
            }

            // braces and sheets follow their yards
            int ropes = 0;
            double off = 0;
            foreach (var rope in def.MovingParts)
            {
                if (rope.Kind != "follow" || rope.Follow == null || !rope.Follow.StartsWith("yard-")) continue;
                ropes++;
                var yard = P(rope.Follow);
                var ym = new float[16];
                PartMotion.PartMatrix(yard, PartMotion.TurnDegrees(yard, (float)(maxBrace * rad)) * rad, ym);
                var m = new float[16];
                PartMotion.FollowMatrix(rope, ym, m);
                off = Math.Max(off, Dist(Apply(m, rope.Tip), Apply(ym, rope.Tip)) + Dist(Apply(m, rope.Anchor), rope.Anchor));
            }
            Check(ropes >= 7 && off < 1e-3, $"the braces and sheets ({ropes}) stay on their yardarms and clews, and made fast where they were (off by {off:0.0000})");
        }

        static double SegDist(double[] p1, double[] q1, double[] p2, double[] q2)
        {
            double[] d1 = { q1[0] - p1[0], q1[1] - p1[1], q1[2] - p1[2] }, d2 = { q2[0] - p2[0], q2[1] - p2[1], q2[2] - p2[2] }, r = { p1[0] - p2[0], p1[1] - p2[1], p1[2] - p2[2] };
            double Dot(double[] u, double[] v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
            double a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r), c = Dot(d1, r), b = Dot(d1, d2), den = a * e - b * b;
            double s = den > 1e-12 ? Math.Clamp((b * f - c * e) / den, 0, 1) : 0;
            double t = e > 1e-12 ? (b * s + f) / e : 0;
            if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
            else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            double[] cp = { p1[0] + d1[0] * s - p2[0] - d2[0] * t, p1[1] + d1[1] * s - p2[1] - d2[1] * t, p1[2] + d1[2] * s - p2[2] - d2[2] * t };
            return Math.Sqrt(Dot(cp, cp));
        }

        /// <summary>
        /// The dry hull (item 18): the mask Sea asks holds every enclosed air cell of the lower deck and the hold, nothing
        /// just outside the hull skin, and the dry outline drawn in the water covers it at the waterline.
        /// </summary>
        static void DryHull(ShipDefinition def)
        {
            Console.WriteLine("the dry hull");
            {
                // the outline Sea cuts the water out by: never inside the hull (from the keel up to the main deck), and
                // close to it at the waterline where you see it
                var clip = def.WaterClip;
                var o = clip?.DryOutline;
                Check(clip?.DryProfile?.Length == WaterClipDef.DryStations * WaterClipDef.DryLevels && o != null, "a detailed dry outline, from the hull's exact widths");
                if (clip?.DryProfile != null && o != null)
                {
                    double stern = clip.CenterX - (clip.HalfLength - clip.Margin), bow = clip.CenterX + (clip.HalfLength - clip.Margin);
                    double worstIn = 0, worstOut = 0, wx = 0, wy = 0, ww = 0, ix = 0, iy = 0;
                    for (double x = stern - 2; x <= bow; x += 0.1)
                        for (double y = def.KeelY; y <= def.DeckY + 0.25; y += 0.1)
                        {
                            double w = o.At(x, y), got = DryAt(clip, x, y);
                            if (w > 0 && w - got > worstIn) { worstIn = w - got; ix = x; iy = y; }
                            if (Math.Abs(y) < 0.3 && x > stern + 1.6 && x < bow - 1 && got - w > worstOut) { worstOut = got - w; wx = x; wy = y; ww = w; }
                        }
                    Check(worstIn < 1e-3, $"the dry outline covers the hull everywhere below the main deck, from the raked transom to the stem (short by {worstIn:0.000} at worst, x {ix:0.0} y {iy:0.0})");
                    {
                        // round the stern (the transom and her quarters, where the water shows) it stays close to her: the
                        // cut ends square at the transom, and beside her quarters it's hardly wider than she is (in 0.3.1
                        // it stood out 2 blocks there, and cut the water away a block behind her transom)
                        double beside = 0, bx = 0, by = 0, behind = 0, hy = 0, below = 0;
                        for (double y = -0.75; y <= 2.51; y += 0.125)
                        {
                            double cutStart = double.NaN, hullStart = double.NaN;
                            for (double x = stern - 2; x <= stern + 4; x += 0.02)
                            {
                                double cut = DryAt(clip, x, y), hull = o.At(x, y);
                                if (double.IsNaN(cutStart) && cut > 0) cutStart = x;
                                if (double.IsNaN(hullStart) && hull > 0.05) hullStart = x;
                                // (right at the transom the widths ramp up over a step of the outline's grid, and between two of its rows the ramps blur: that's its face)
                                if (hull > 0.05 && o.At(x - 0.3, y) > 0.05)
                                {
                                    if (y < -0.25) below = Math.Max(below, cut - hull);   // under the transom, where she closes to the post
                                    else if (cut - hull > beside) { beside = cut - hull; bx = x; by = y; }
                                }
                            }
                            if (y >= -0.5 && !double.IsNaN(hullStart) && !double.IsNaN(cutStart) && hullStart - cutStart > behind) { behind = hullStart - cutStart; hy = y; }
                        }
                        Check(beside < 0.45 && below < 1.0 && behind < 0.4, $"round her stern the water is cut close to her: {beside:0.00} wider than her quarters at most from the waterline up " +
                            $"(x {bx:0.0}, y {by:0.0}; {below:0.00} under the transom), and {behind:0.00} behind the transom (y {hy:0.0}; the cut's end is raked {clip.DryRake:0.00} a block up, like the transom)");
                    }
                    Check(worstOut < 0.6, $"and at the waterline it's close to her sides, but for right at the transom ({worstOut:0.00} out at worst, at x {wx:0.0} y {wy:0.0} where she's {ww:0.00} wide)");
                    double aft = DryAt(clip, stern + 0.3, 0.0), aftHigh = DryAt(clip, stern + 0.3, 2.5);
                    Check(aft < aftHigh - 0.4, $"it follows her transom stern: narrow at the waterline, wide higher up ({aft:0.00} / {aftHigh:0.00})");
                    if (Environment.GetEnvironmentVariable("HANSA_DUMP_DRY") is string dump)
                    {
                        // the cut and the hull, for looking at (x y cut hull per line)
                        using var w = new StreamWriter(dump);
                        for (double y = -1.5; y <= 2.51; y += 0.25)
                            for (double x = stern - 1.0; x <= bow + 1.0; x += 0.05)
                            {
                                w.WriteLine(FormattableString.Invariant($"{x:0.00} {y:0.00} {DryAt(clip, x, y):0.000} {o.At(x, y):0.000}"));
                            }
                    }
                }
            }
            var g = def.Grid;
            var dry = def.DryMask;
            Check(dry != null, "the design has a mask of the inside of its hull");
            if (dry == null) return;
            int band = g.CellY(def.DeckY - 0.25);

            // every air cell you can get to from the lower deck without going over the main deck (the hatches stand open in
            // the design, so the hold is part of it) is dry, and none of them is outside the hull
            var seen = new bool[g.NX, band, g.NZ];
            var queue = new System.Collections.Generic.Queue<(int, int, int)>();
            int ci = g.CellX(0.0), cj = g.CellY(1.5), ck = g.CellZ(0.1);
            queue.Enqueue((ci, cj, ck));
            seen[ci, cj, ck] = true;
            int cells = 0, wet = 0;
            bool leaks = false, holdReached = false;
            while (queue.Count > 0)
            {
                var (i, j, k) = queue.Dequeue();
                cells++;
                double x = g.OX + (i + 0.5) * g.V, y = g.OY + (j + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
                if (!dry.SolidAt(x, y, z)) wet++;
                if (y < -1.0 && Math.Abs(x - 5.0) < 0.3) holdReached = true;
                foreach (var (di, dj, dk) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                {
                    int a = i + di, b = j + dj, c = k + dk;
                    if (a < 0 || b < 0 || c < 0 || a >= g.NX || c >= g.NZ) { leaks = true; continue; }
                    if (b >= band || seen[a, b, c] || g.Solid(a, b, c)) continue;
                    seen[a, b, c] = true;
                    queue.Enqueue((a, b, c));
                }
            }
            Check(!leaks && holdReached, $"the lower deck and the hold are closed below the main deck ({cells} air cells, the hold reached: {holdReached})");
            Check(wet == 0, $"all of it is dry ({wet} of {cells} air cells aren't)");

            // just outside the hull skin it's never dry: 0.3 out from the outermost solid cell, all round and underneath
            int outside = 0, wetOut = 0;
            foreach (double y in new[] { -2.25, -1.5, -0.5, 0.5, 1.5, 2.5, 3.0 })
            {
                for (int i = 0; i < g.NX; i++)
                {
                    double x = g.OX + (i + 0.5) * g.V;
                    int j = g.CellY(y);
                    for (int side = 1; side >= -1; side -= 2)
                    {
                        int k = side > 0 ? g.NZ - 1 : 0;
                        while (k >= 0 && k < g.NZ && !g.Solid(i, j, k)) k -= side;
                        if (k < 0 || k >= g.NZ) continue;
                        double zOut = side > 0 ? g.OZ + (k + 1) * g.V + 0.3 : g.OZ + k * g.V - 0.3;
                        outside++;
                        if (dry.SolidAt(x, y, zOut)) wetOut++;
                    }
                }
                // the bow and the stern
                int jj = g.CellY(y), kk = g.CellZ(0.1);
                int i0 = 0, i1 = g.NX - 1;
                while (i0 < g.NX && !g.Solid(i0, jj, kk)) i0++;
                while (i1 >= 0 && !g.Solid(i1, jj, kk)) i1--;
                if (i0 < i1)
                {
                    outside += 2;
                    if (dry.SolidAt(g.OX + i0 * g.V - 0.3, y, 0.1)) wetOut++;
                    if (dry.SolidAt(g.OX + (i1 + 1) * g.V + 0.3, y, 0.1)) wetOut++;
                }
            }
            for (int i = 0; i < g.NX; i++)
            {
                int k = g.CellZ(0.3), j = 0;
                while (j < band && !g.Solid(i, j, k)) j++;
                if (j >= band) continue;
                outside++;
                if (dry.SolidAt(g.OX + (i + 0.5) * g.V, g.OY + j * g.V - 0.3, 0.3)) wetOut++;
            }
            Check(outside > 400 && wetOut == 0, $"no point 0.3 outside the hull skin is dry ({wetOut} of {outside})");

            // the outline Sea cuts the water to (the footprint less its margin) holds the mask at the waterline
            var wc = def.WaterClip;
            int band0 = g.CellY(-0.5), band1 = g.CellY(1.5), outOfOutline = 0, n = 0;
            double worst = double.MaxValue;
            for (int i = 0; i < g.NX; i++)
                for (int j = band0; j <= band1; j++)
                    for (int k = 0; k < g.NZ; k++)
                    {
                        double x = g.OX + (i + 0.5) * g.V, y = g.OY + (j + 0.5) * g.V, z = g.OZ + (k + 0.5) * g.V;
                        if (!dry.SolidAt(x, y, z)) continue;
                        n++;
                        // (its stern end is the raked plane through the transom, like the detailed outline's)
                        double side = Math.Abs(z) + g.V / 2, len = wc.HalfLength - wc.Margin;
                        double fwd = len - (x - wc.CenterX + g.V / 2), aft = (x - wc.CenterX - g.V / 2) - (wc.DryRake * (y + g.V / 2 - wc.Height) - len);
                        double gap = Math.Min(wc.HalfWidthAt(x) - wc.Margin - side, Math.Min(fwd, aft + 0.13));
                        worst = Math.Min(worst, gap);
                        if (gap < -1e-6) outOfOutline++;
                    }
            Check(n > 0 && outOfOutline == 0, $"the dry outline in the water covers the inside of the hull at the waterline ({outOfOutline} of {n} cells out, closest {worst:0.00})");
        }

        /// <summary>Walk from the deck into the shrouds holding W, up onto the top, let go, walk back over the edge and down.</summary>
        /// <summary>The dry outline's half-width at ship-local (x, y), interpolated as Sea's shader does.</summary>
        /// <summary>
        /// The crosshair on the ship (EntityShip.IntersectsRay → ShipParts.Ray): the part index round trip; the grid
        /// raycast from outside (each side, below the waterline, the topsides, the bulwark, a castle's side), from the
        /// decks and inside the hull (decks, their undersides, the sides, a mast, a stair, the helm, the plate); the same
        /// hits with her pitched and rolled.
        /// </summary>
        static void LookingAt(ShipDefinition def)
        {
            Console.WriteLine("the crosshair: what part of her you look at");
            var map = def.PartMap;
            var g = def.Grid;
            {
                var rnd = new Random(7);
                int bad = 0;
                for (int n = 0; n < 2000; n++)
                {
                    var p = (n % 5) switch
                    {
                        0 => ShipPart.Hull(rnd.Next(2) * 2 - 1, rnd.Next(256), rnd.Next(256)),
                        1 => ShipPart.Deck(rnd.Next(256), rnd.Next(256), rnd.Next(2) == 1),
                        2 => ShipPart.Mast(rnd.Next(1000)),
                        3 => ShipPart.Structure(rnd.Next(2) * 2 - 1, rnd.Next(256), rnd.Next(256)),
                        _ => ShipPart.Fixture(rnd.Next(100000)),
                    };
                    int code = p.Encode();
                    var q = ShipPart.Decode(code);
                    if (code <= 0 || q.Kind != p.Kind || q.Side != p.Side && (p.Kind == ShipPartKind.Hull || p.Kind == ShipPartKind.Structure) ||
                        q.Station != p.Station || q.Band != p.Band || q.Index != p.Index) bad++;
                }
                Check(bad == 0 && ShipPart.Decode(0).Kind == ShipPartKind.None, $"a part's index round-trips, and 0 is no part ({bad} bad of 2000)");
                Check(ShipPart.Hull(-1, 12, 3).Encode() != ShipPart.Hull(1, 12, 3).Encode() && ShipPart.Hull(1, 12, 3).Encode() != ShipPart.Structure(1, 12, 3).Encode(),
                    "port and starboard, hull and woodwork have their own indices");
            }
            Check(def.Frames != null && def.Frames.Space >= 1.5 && def.Frames.Space <= 2 && def.Decks.Length >= 6 && def.Masts.Length == 3,
                $"the design says where her frames ({def.Frames?.Count} every {def.Frames?.Space}), decks ({def.Decks.Length}) and masts ({def.Masts.Length}) are");
            {
                // a hull part is the planking between two frames: every frame space along her is one station, and a
                // frame's own x is the boundary between two
                double f0 = def.Frames.X0, sp = def.Frames.Space;
                Check(map.StationAt(f0 + 0.01) == 0 && map.StationAt(f0 + sp - 0.01) == 0 && map.StationAt(f0 + sp + 0.01) == 1 && map.StationAt(f0 + 3 * sp + 0.7) == 3,
                    $"a station is the space between two frames ({sp})");
                Check(map.BandAt(def.KeelY + 0.1) == 0 && Array.TrueForAll(new[] { def.Waterline - 0.1, def.Waterline + 0.1, def.DeckY + 0.3 }, y => map.BandAt(y) == (int)Math.Floor(y - def.KeelY)),
                    "a band is a block of her height, from the keel");
            }

            bool All(FixtureDef f) => true;
            bool None(FixtureDef f) => f.Type != "attach";
            ShipRayHit Level(double ox, double oy, double oz, double dx, double dy, double dz, System.Func<FixtureDef, bool> visible = null,
                System.Func<FixtureDef, ItemStack> attached = null)
            {
                // (yaw π/2: her frame is the world's, the ray in ship-local terms)
                ShipParts.Ray(def, g, 0, 0, 0, Math.PI / 2, 0, 0, new Vec3d(ox, oy, oz), new Vec3d(dx, dy, dz), 30, visible ?? None, f => false,
                    attached ?? (f => null), out var h);
                return h;
            }
            string Where(ShipRayHit h) => $"{h.Part} at {h.Local?.X:0.00},{h.Local?.Y:0.00},{h.Local?.Z:0.00}";
            double wl = def.Waterline, deck = def.DeckY;
            // from outside
            var h1 = Level(2.0, wl - 1.0, -20, 0, 0, 1);
            Check(h1.Part.Kind == ShipPartKind.Hull && h1.Part.Side == -1 && map.HeightZone(h1.Part.Station, h1.Part.Band) == "below" &&
                  map.LengthZone(h1.Part.Station) == "amidships" && h1.Local.Z < -3 && h1.Face.Normalf.Z < -0.9,
                $"from the water, her port side amidships below the waterline ({Where(h1)}, {map.LengthZone(h1.Part.Station)}, face {h1.Face})");
            var h2 = Level(2.0, wl + 0.6, 20, 0, 0, -1);
            Check(h2.Part.Kind == ShipPartKind.Hull && h2.Part.Side == 1 && map.HeightZone(h2.Part.Station, h2.Part.Band) == "topsides" && h2.Local.Z > 3,
                $"her starboard topsides ({Where(h2)})");
            var h3 = Level(3.6, deck + 0.6, 20, 0, 0, -1);      // (between two gunports)
            Check(h3.Part.Kind == ShipPartKind.Hull && map.HeightZone(h3.Part.Station, h3.Part.Band) == "bulwark", $"the waist's bulwark ({Where(h3)})");
            var h4 = Level(-11.0, deck + 1.5, -20, 0, 0, 1);
            Check(h4.Part.Kind == ShipPartKind.Hull && map.HeightZone(h4.Part.Station, h4.Part.Band) == "castle" && map.LengthZone(h4.Part.Station) is "stern" or "aft",
                $"the sterncastle's side ({Where(h4)}, {map.LengthZone(h4.Part.Station)})");
            var h5 = Level(2.0, def.KeelY - 3, 0.6, 0, 1, 0);
            Check(h5.Part.Kind == ShipPartKind.Hull && map.HeightZone(h5.Part.Station, h5.Part.Band) == "below", $"her bottom, from under the keel ({Where(h5)})");
            var h6 = Level(40, wl + 0.5, 0.3, -1, 0, 0);
            Check(h6.Part.Kind == ShipPartKind.Hull && map.LengthZone(h6.Part.Station) == "bow", $"her stem, from ahead ({Where(h6)})");
            var h7 = Level(-30, wl + 1.5, 0.3, 1, 0, 0);
            Check(h7.Part.Kind == ShipPartKind.Hull && map.LengthZone(h7.Part.Station) == "stern", $"her transom, from astern ({Where(h7)})");
            Check(Level(2.0, wl - 1.0, -20, 0, 0, -1).Part.Kind == ShipPartKind.None, "a ray looking away from her misses her");
            // from her decks and inside her hull
            string DeckOf(ShipRayHit h) => h.Part.Kind == ShipPartKind.Deck ? def.Decks[h.Part.Index].Code + (h.Part.Band != 0 ? " underside" : "") : "-";
            var d1 = Level(5.0, deck + 1.6, 1.5, 0.1, -1, 0.05);
            Check(DeckOf(d1) == "main" && Math.Abs(d1.Local.Y - deck) < 0.01 && d1.Face == BlockFacing.UP, $"standing in the waist, looking down: the main deck ({Where(d1)})");
            var spawn = Array.Find(def.Fixtures, f => f.Kind == "plate")?.Spawn ?? new double[] { -7.75, deck + 3, -1.5 };
            var d2 = Level(spawn[0], spawn[1] + 1.6, spawn[2], 0.2, -1, 0);
            Check(DeckOf(d2) == "quarter", $"on the quarterdeck, looking down: the quarterdeck ({Where(d2)})");
            var d3 = Level(3.0, deck - 3 + 1.2, 1.5, 0, -1, 0);
            var d4 = Level(3.0, deck - 3 + 1.2, 1.5, 0, 1, 0);
            var d5 = Level(3.0, deck - 3 + 1.2, 1.5, 0, 0, 1);
            Check(DeckOf(d3) == "lower" && DeckOf(d4) == "main underside" && d5.Part.Kind == ShipPartKind.Hull && d5.Part.Side == 1,
                $"on the lower deck: its deck ({Where(d3)}), the main deck's underside overhead ({Where(d4)}), the ship's side ({Where(d5)})");
            var d6 = Level(-5.0, def.Decks[0].Y + 1.2, 1.0, 0, 0, 1);
            Check(d6.Part.Kind == ShipPartKind.Hull && map.HeightZone(d6.Part.Station, d6.Part.Band) == "below", $"in the hold, her side below the waterline ({Where(d6)})");
            var d7 = Level(-12.0, deck + 3 + 1.2, 0.8, 0, -1, 0);
            Check(DeckOf(d7) == "quarter" && map.RoomOf(d7.Part)?.Code == "poopcabin", $"in the poop cabin, looking down: its floor, the quarterdeck ({Where(d7)})");
            var top = Array.Find(def.Decks, dd => dd.Code == "top");
            if (top != null)
            {
                var d8 = Level((top.X0 + top.X1) / 2 + 1.0, top.Y + 1.6, 0.5, 0, -1, 0);
                Check(DeckOf(d8) == "top", $"in the main top, looking down: the top ({Where(d8)})");
            }
            var mainMast = Array.Find(def.Masts, m => m.Code == "main");
            var d9 = Level(mainMast.X - 1.5, deck + 1.2, 0.0, 1, 0, 0);
            Check(d9.Part.Kind == ShipPartKind.Mast && def.Masts[d9.Part.Index].Code == "main", $"the mainmast, from the waist ({Where(d9)})");
            // (the quarterdeck's stair, from the design: from inboard of it, onto its middle tread)
            var qs = (def.Stairs == null ? null : Array.Find(def.Stairs, s0 => s0.Code == "quarterdeck"));
            var stair = qs == null ? Level(-2.1, deck + 1.6, 0.5, 0, -0.12, 1)
                : Level((qs.Foot[0] + qs.Head[0]) / 2, deck + 2.6, qs.Foot[2] - 2.5, 0, -((deck + 2.6) - (qs.Foot[1] + qs.Head[1]) / 2 + 0.1), 2.5);
            Check(stair.Part.Kind == ShipPartKind.Structure, $"the stair up to the quarterdeck is woodwork, not the hull ({Where(stair)})");
            var helm = def.Fixture("helm");
            var d10 = Level(helm.Pos[0], helm.Pos[1] + 1.6, helm.Pos[2], 1, -0.25, 0, All);
            Check(d10.Part.Kind == ShipPartKind.Fixture && d10.Fixture == helm, $"at the helm, looking at the wheel: the helm ({Where(d10)})");
            var plate = Array.Find(def.Fixtures, f => f.Kind == "plate");
            if (plate != null)
            {
                var d11 = Level(spawn[0], spawn[1] + 1.6, spawn[2], -1, -0.1, 0, All, f => f == plate ? new ItemStack() : null);
                var d12 = Level(spawn[0], spawn[1] + 1.6, spawn[2], -1, -0.1, 0);
                Check(d11.Part.Kind == ShipPartKind.Fixture && d11.Fixture == plate && d12.Part.Kind == ShipPartKind.Structure && Math.Abs(d12.Local.X - plate.Pos[0]) < 0.3,
                    $"by the plate: the plate on the bulkhead when it's up ({Where(d11)}), the bulkhead when it isn't ({Where(d12)})");
            }

            {
                // every name the look-at box can show is in the language file (the client isn't tested here)
                var lang = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(FindRoot(), "assets", "hansa", "lang", "en.json")));
                var missing = new System.Collections.Generic.List<string>();
                void Need(string key) { if (lang[key] == null && !missing.Contains(key)) missing.Add(key); }
                foreach (var f in def.Fixtures)
                {
                    int dash = f.Code.LastIndexOf('-');
                    string key = dash > 0 && int.TryParse(f.Code.Substring(dash + 1), out _) ? f.Code.Substring(0, dash) : f.Code;
                    if (lang["fixture-" + key] == null) Need("fixturekind-" + (f.Type == "attach" ? "attach-" + f.Kind : f.Type));
                }
                foreach (var d in def.Decks) Need("deck-" + d.Code);
                foreach (var m in def.Masts) Need("mast-" + m.Code);
                foreach (var r in def.Rooms) Need("room-" + r.Code);
                foreach (var z in new[] { "stern", "aft", "amidships", "forward", "bow" }) Need("part-at-" + z);
                foreach (var z in new[] { "below", "topsides", "bulwark", "castle" }) Need("part-height-" + z);
                foreach (var k in new[] { "part-hull", "part-side-port", "part-side-starboard", "part-deck", "part-deck-under", "part-in", "part-structure",
                                          "plate-info", "plate-info-yours", "hint-attach-plate", "hint-attached-plate", "hint-attached-plate-bound", "attach-needs-plate",
                                          "plate-empty-hand", "spawn-off", "spawn-bound", "spawn-unbound", "spawn-ended-gone", "spawn-ended-plate", "spawn-ended-off",
                                          "spawn-ended-missing", "spawn-ended-replaced" }) Need(k);
                Check(missing.Count == 0, $"every name the look-at box and the spawn point can say is in the language file{(missing.Count > 0 ? ": missing " + string.Join(", ", missing) : "")}");
            }

            // the same rays with her somewhere else, turned, pitched and rolled (as clients draw her): the same hits
            {
                var rays = new (double ox, double oy, double oz, double dx, double dy, double dz)[]
                {
                    (2.0, wl - 1.0, -20, 0, 0, 1), (3.0, deck + 0.6, 20, 0, 0, -1), (5.0, deck + 1.6, 1.5, 0.1, -1, 0.05),
                    (3.0, deck - 3 + 1.2, 1.5, 0, 0, 1), (3.0, deck - 3 + 1.2, 1.5, 0, 1, 0), (mainMast.X - 1.5, deck + 1.2, 0.0, 1, 0, 0),
                    (-11.0, deck + 1.5, -20, 0.2, 0.1, 1), (40, wl + 0.5, 0.3, -1, 0.05, 0),
                };
                var poses = new (double yaw, double pitch, double roll)[] { (0.7, 0.0, 0.0), (2.1, 0.14, -0.3), (-1.3, -0.2, 0.35), (4.0, 0.05, 0.12) };
                int bad = 0, n = 0;
                double worst = 0;
                string first = "";
                foreach (var (yaw, pitch, roll) in poses)
                    foreach (var r0 in rays)
                    {
                        // (off the cell boundaries, where rounding may put a straight ray in the next row of cells)
                        var r = (ox: r0.ox + 0.07, oy: r0.oy + 0.06, oz: r0.oz + 0.07, r0.dx, r0.dy, r0.dz);
                        var level = Level(r.ox, r.oy, r.oz, r.dx, r.dy, r.dz);
                        // the ray in the world: her frame turned and tilted, her origin elsewhere
                        double px = 1234.5, py = 110.25, pz = -777.75;
                        ShipFrame.ToWorld(yaw, pitch, roll, r.ox, r.oy, r.oz, out double wx, out double wy, out double wz);
                        ShipFrame.ToWorld(yaw, pitch, roll, r.dx, r.dy, r.dz, out double vx, out double vy, out double vz);
                        bool got = ShipParts.Ray(def, g, px, py, pz, yaw, pitch, roll, new Vec3d(px + wx, py + wy, pz + wz), new Vec3d(vx * 7, vy * 7, vz * 7), 30,
                            None, f => false, f => null, out var tilted);
                        n++;
                        if (!got || tilted.Part.Encode() != level.Part.Encode() || Math.Abs(tilted.Distance - level.Distance) > 1e-6)
                        {
                            if (bad++ == 0) first = $"; first: ray {r} at {yaw:0.0},{pitch:0.00},{roll:0.00}: {Where(tilted)} for {Where(level)}";
                            continue;
                        }
                        ShipFrame.ToLocal(yaw, pitch, roll, tilted.World.X - px, tilted.World.Y - py, tilted.World.Z - pz, out double lx, out double ly, out double lz);
                        worst = Math.Max(worst, Math.Abs(lx - level.Local.X) + Math.Abs(ly - level.Local.Y) + Math.Abs(lz - level.Local.Z));
                        // the face looks back toward the eye
                        var nf = tilted.Face.Normalf;
                        if (nf.X * vx + nf.Y * vy + nf.Z * vz > 0 && bad++ == 0) first = $"; first: ray {r} at {yaw:0.0},{pitch:0.00},{roll:0.00}: face {tilted.Face}";
                    }
                Check(bad == 0 && worst < 1e-6, $"turned, pitched and rolled, the same rays meet her at the same parts and places ({n - bad} of {n}, {worst:e1}{first})");
            }
            {
                // the raycast that tells the face agrees with the plain one
                var rnd = new Random(3);
                int bad = 0;
                for (int n = 0; n < 3000; n++)
                {
                    var o = new Vec3d(rnd.NextDouble() * 60 - 30, rnd.NextDouble() * 30 - 8, rnd.NextDouble() * 20 - 10);
                    var d = new Vec3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
                    double len = d.Length();
                    if (len < 0.1) continue;
                    d.Mul(1 / len);
                    double a = g.Raycast(o, d, 25), b = g.RaycastCell(o, d, 25, out int i, out int j, out int k, out int axis);
                    if (a == double.MaxValue != (b == double.MaxValue)) { bad++; continue; }
                    if (a == double.MaxValue) continue;
                    if (Math.Abs(a - b) > 1e-5 || !g.Solid(i, j, k)) bad++;
                }
                Check(bad == 0, $"the raycast that tells the cell and face agrees with the plain one ({bad} of 3000 differ)");
                // an eye against a wall looks on past it
                var eye = new Vec3d(d5.Local.X, d5.Local.Y, d5.Local.Z + 0.05);
                double t0 = g.RaycastCell(eye, new Vec3d(0, 0, -1), 20, out _, out _, out _, out int ax0), t1 = g.RaycastCell(eye, new Vec3d(0, 0, -1), 20, out _, out _, out _, out _, skipStart: true);
                Check(t0 == 0 && ax0 == -1 && t1 > 0.5, $"an eye inside the planking: met at once ({t0:0.00}), or with skipStart looking on past it ({t1:0.00})");
            }
        }

        /// <summary>
        /// The spawn point on the ship: the plate on the wall by the helm, the spot in front of it free and with a deck
        /// under it, and the world block vanilla's spawn position is set to as she lies anywhere, turned any way.
        /// </summary>
        static void SpawnPoint(ShipDefinition def)
        {
            Console.WriteLine("the spawn point by the helm");
            var plates = Array.FindAll(def.Fixtures, f => f.Type == "attach" && f.Kind == "plate");
            Check(plates.Length == 1 && plates[0].Spawn?.Length == 3 && plates[0].SelBox != null, $"a place for a metal plate, with a spawn spot ({plates.Length})");
            if (plates.Length == 0 || plates[0].Spawn == null) return;
            var p = plates[0];
            var g = def.Grid;
            var s = p.Spawn;
            var (fx, fz) = p.Facing;
            Check(g.SolidAt(p.Pos[0] - fx * 0.12, p.Pos[1], p.Pos[2] - fz * 0.12) && !g.SolidAt(p.Pos[0] + fx * 0.12, p.Pos[1], p.Pos[2] + fz * 0.12),
                "the plate hangs on a wall, facing out from it");
            Check(!g.Collides(s[0] - 0.3, s[1], s[2] - 0.3, s[0] + 0.3, s[1] + 1.85, s[2] + 0.3) && DeckMover.HasFloor(g, s[0], s[1], s[2]),
                $"the spawn spot ({s[0]}, {s[1]}, {s[2]}) is free, with the deck under it");
            double toPlate = Math.Sqrt((s[0] - p.Pos[0]) * (s[0] - p.Pos[0]) + (s[2] - p.Pos[2]) * (s[2] - p.Pos[2]));
            var helm = def.Fixture("helm");
            double hx = helm.Pos[0] - p.Pos[0], hz = helm.Pos[2] - p.Pos[2], toHelm = Math.Sqrt(hx * hx + hz * hz);
            Check(toPlate < 1.2 && toHelm < 4.5 && (hx * fx + hz * fz) / toHelm > 0.8 && Math.Abs(helm.Pos[1] - s[1]) < 0.01,
                $"right in front of the plate ({toPlate:0.00}), which faces the helm {toHelm:0.0} blocks away on the same deck");
            foreach (var f in def.Fixtures)
            {
                if (f == p || f.Box == null) continue;
                var b = f.Box;
                bool inIt = s[0] + 0.3 > b[0] && s[0] - 0.3 < b[3] && s[1] + 1.8 > b[1] && s[1] < b[4] && s[2] + 0.3 > b[2] && s[2] - 0.3 < b[5];
                if (inIt) Check(false, $"the spawn spot is clear of {f.Code}");
            }
            var sel = p.SelBox;
            bool blocksDoor = false;
            foreach (var f in def.Fixtures)
                if (f.Type == "door" && f.SelBox is Cuboidd ds && ds.Intersects(sel)) blocksDoor = true;
            Check(!blocksDoor, "the plate is clear of the door beside it");
            {
                var stack = PlateStack();
                Check(stack == null || ShipFittings.Accepts(p, stack), "a vanilla metal plate goes there");
                Check(ShipFittings.AttachBox(p) == null, "the plate takes no room (you walk right up to the wall)");
            }

            // vanilla's spawn position is a block: the one the spot is in, at her still-water level, feet just above her deck
            var rnd = new Random(11);
            int bad = 0;
            double worst = 0;
            for (int n = 0; n < 500; n++)
            {
                double x = rnd.NextDouble() * 20000 - 10000, y = 100 + rnd.NextDouble() * 20, z = rnd.NextDouble() * 20000 - 10000;
                double yaw = rnd.NextDouble() * Math.PI * 2;
                ShipFittings.SpawnBlock(s, x, y, z, yaw, out int bx, out int by, out int bz);
                ShipFrame.ToWorld(yaw, s[0], s[1], s[2], out double wx, out double wy, out double wz);
                double ex = x + wx, ey = y + wy, ez = z + wz;
                if (bx != (int)Math.Floor(ex) || bz != (int)Math.Floor(ez) || by < ey - 0.02 || by > ey + 1.0) bad++;
                // the spot's offset from her turns with her: yaw 0 puts her bow (+x) toward +z
                worst = Math.Max(worst, Math.Abs(Math.Sqrt(wx * wx + wz * wz) - Math.Sqrt(s[0] * s[0] + s[2] * s[2])));
            }
            ShipFittings.SpawnBlock(s, 0.5, 100, 0.5, 0, out int ax, out _, out int az);
            Check(bad == 0 && worst < 1e-9 && az == (int)Math.Floor(0.5 + s[0]) && ax == (int)Math.Floor(0.5 - s[2]),
                "vanilla's spawn position: the block the spot is in, wherever she lies and however she's turned, her deck under the feet");
            ShipFittings.SpawnBlock(s, 0.5, 100.0, 0.5, 1.0, out _, out int y0, out _);
            ShipFittings.SpawnBlock(s, 0.5, 100.4, 0.5, 1.0, out _, out int y1, out _);
            Check(y1 - y0 <= 1 && y0 >= 100 + s[1] - 0.01, $"at her still-water level, not moving with every wave (feet at {y0} for a deck at {100 + s[1]})");
        }

        class SkinLite
        {
            public string[] Textures;
            public System.Collections.Generic.Dictionary<string, float[][]> Groups;
        }

        /// <summary>
        /// Her shadow (ShipRenderer.RenderShadows, ShipShadows): the sphere that decides whether she's in a shadow map
        /// holds all of her, the moving parts at the ends of their travel; what the far and the near map get; and the
        /// shadow's matrix is the engine's own (the shadow map's projection × the sun's view) with her model matrix, so
        /// her shadow falls where she's drawn.
        /// </summary>
        static void Shadows(ShipDefinition def, string assets)
        {
            Console.WriteLine($"her shadow ({def.Code})");
            ShipShadows.Sphere(def, out double cx, out double cy, out double cz, out double r);
            double spare = double.MaxValue;
            void Hold(double x, double y, double z)
            {
                double dx = x - cx, dy = y - cy, dz = z - cz;
                spare = Math.Min(spare, r - Math.Sqrt(dx * dx + dy * dy + dz * dz));
            }
            var g = def.Grid;
            int voxels = 0;
            for (int i = 0; i < g.NX; i++)
                for (int j = 0; j < g.NY; j++)
                    for (int k = 0; k < g.NZ; k++)
                    {
                        if (!g.Solid(i, j, k)) continue;
                        voxels++;
                        for (int c = 0; c < 8; c++) Hold(g.OX + (i + (c & 1)) * g.V, g.OY + (j + ((c >> 1) & 1)) * g.V, g.OZ + (k + ((c >> 2) & 1)) * g.V);
                    }
            foreach (var f in def.Fixtures)
            {
                if (f.Pos != null) Hold(f.Pos[0], f.Pos[1], f.Pos[2]);
                if (f.Box != null) for (int c = 0; c < 8; c++) Hold(f.Box[(c & 1) * 3], f.Box[1 + ((c >> 1) & 1) * 3], f.Box[2 + ((c >> 2) & 1) * 3]);
            }
            // a moving part's mesh at the ends of its travel (the lateen with its yard); the ropes stretch between those
            System.Collections.Generic.List<float[]> Poses(MovingPartDef part)
            {
                var list = new System.Collections.Generic.List<float[]> { Mat4f.Create() };
                if (part == null || part.Kind == "follow" || part.Kind == "static") return list;
                string src = part.Source ?? "rudder";
                float[] values = src switch
                {
                    "brace" => new[] { def.Sailing.MaxBrace * GameMath.DEG2RAD, -def.Sailing.MaxBrace * GameMath.DEG2RAD },
                    "sheet" => new[] { def.Sailing.MaxSheet * GameMath.DEG2RAD, -def.Sailing.MaxSheet * GameMath.DEG2RAD },
                    "anchor" => new[] { 1f },
                    _ => src.StartsWith("open:") ? new[] { 1f } : new[] { 1f, -1f }    // the helm, an oar's stroke
                };
                foreach (float v in values)
                {
                    var m = new float[16];
                    if (part.Kind == "slide") PartMotion.SlideMatrix(part, v * part.Distance, m);
                    else PartMotion.PartMatrix(part, PartMotion.TurnDegrees(part, v) * GameMath.DEG2RAD, m);
                    list.Add(m);
                }
                return list;
            }
            int verts = 0;
            SkinLite skin = null;
            string skinPath = def.Shapes?.Skin == null ? null : Path.Combine(assets, new AssetLocation(def.Shapes.Skin).Path);
            if (skinPath != null && File.Exists(skinPath)) skin = JsonConvert.DeserializeObject<SkinLite>(File.ReadAllText(skinPath));
            if (skin?.Groups != null)
            {
                foreach (var kv in skin.Groups)
                {
                    MovingPartDef part = null;
                    if (kv.Key.StartsWith("part:")) part = Array.Find(def.MovingParts, p => "part:" + p.Code == kv.Key);
                    else if (Array.Find(def.Sails, s => s.Mesh == kv.Key) is SailDef sd) part = Array.Find(def.MovingParts, p => p.Code == "yard-" + sd.Code);
                    var poses = Poses(part);
                    foreach (var q in kv.Value)
                        for (int c = 0; c < 4; c++)
                        {
                            int o = 1 + c * 8;
                            verts++;
                            foreach (var m in poses)
                                Hold(m[0] * q[o] + m[4] * q[o + 1] + m[8] * q[o + 2] + m[12], m[1] * q[o] + m[5] * q[o + 1] + m[9] * q[o + 2] + m[13],
                                    m[2] * q[o] + m[6] * q[o + 1] + m[10] * q[o + 2] + m[14]);
                        }
                }
            }
            Check(skin?.Groups != null && spare >= 0, $"the sphere she's tested against for the shadow maps holds all of her: {voxels} voxels, the fixtures, " +
                $"{verts} mesh vertices, the moving parts at the ends of their travel (radius {r:0.0}, {spare:0.00} to spare)");

            // what the far map (past the near one's 30-odd blocks) and the near map get
            bool skinOk = true;
            if (skin?.Groups != null)
                foreach (var grp in skin.Groups.Keys)
                {
                    if (grp.StartsWith("part:") || grp.StartsWith("sail:")) continue;      // their own rules
                    bool f = ShipShadows.CastsSkin(grp, true), n = ShipShadows.CastsSkin(grp, false);
                    bool want = grp is "exterior" or "bottom" or "bottomclad";
                    if (grp == "interior" || grp.StartsWith("glass")) skinOk &= !f && !n;
                    else if (want) skinOk &= f && n;
                    else skinOk &= n && !f;       // a figurehead
                }
            foreach (var fh in def.Fixtures)
                if (fh.Type == "figurehead" && fh.Groups != null)
                    foreach (var grp in fh.Groups) if (!string.IsNullOrEmpty(grp)) skinOk &= ShipShadows.CastsSkin(grp, false) && !ShipShadows.CastsSkin(grp, true);
            Check(skinOk, "her planking outside, masts, standing rigging and bottom in both shadow maps, a figurehead in the near one, never the inside planking or the glass");

            int far = 0, near = 0;
            bool partsOk = true;
            foreach (var p in def.MovingParts)
            {
                bool f = ShipShadows.CastsPart(p, true), n = ShipShadows.CastsPart(p, false);
                if (f) far++;
                if (n) near++;
                if (f && !n) partsOk = false;
                if (p.Interior && n) partsOk = false;
                if (p.Kind == "follow" && (f || !n && !p.Interior)) partsOk = false;
                bool big = p.Code == "rudder" || p.Code == "mast" || p.Code == "wheel" || p.Code.StartsWith("yard-") || p.Code.StartsWith("gate-") || p.Code.StartsWith("oar");
                if (big && !p.Interior && !f) partsOk = false;
            }
            Check(partsOk && near > far, $"moving parts: the rudder, yards, gates, oars and the wheel in both maps, the ropes that follow them only near, nothing inside her (far {far}, near {near} of {def.MovingParts.Length})");

            int pieces = 0, fFar = 0, fNear = 0;
            bool fixOk = true;
            foreach (var f in def.Fixtures)
            {
                // the vanilla blocks ShipRenderer.BuildPiece draws, marked as it marks them
                bool piece = f.Type is "door" or "window" or "prop" or "bed" or "attach" || f.Type == "gate" && f.Block != null;
                if (!piece) continue;
                pieces++;
                bool indoors = f.Type is "prop" or "bed" or "attach" && def.Indoors(f), glass = f.Type == "window";
                bool cf = ShipShadows.CastsFixture(f, indoors, glass, true), cn = ShipShadows.CastsFixture(f, indoors, glass, false);
                if (cf) fFar++;
                if (cn) fNear++;
                if (cf && !cn || (indoors || glass) && cn) fixOk = false;
                if ((f.Type == "door" || f.Kind == "boat") && !indoors && !cf) fixOk = false;
            }
            if (def.Fixture("hold") != null)
            {
                // the carrack: the hold's cargo, the bunks and the cabins' chests are indoors (in her own shadow)
                foreach (var f in def.Fixtures)
                    if (f.Kind is "cargo" or "bed" or "chest" && !def.Indoors(f)) fixOk = false;
            }
            Check(fixOk && fNear >= fFar, $"vanilla blocks aboard: doors and the boats on the gallows in both maps, lanterns and the plate on deck only near, nothing indoors (far {fFar}, near {fNear} of {pieces})");

            {
                // the engine's shadow map: an orthographic box round the camera, seen from the sun (SystemRenderShadowMap)
                float w = 180, h = 150, len = 320;
                var proj = Mat4f.Create();
                proj[0] = 2 / w;
                proj[5] = 2 / h;
                proj[10] = -2 / len;
                float[] sun = { 0.42f, 0.78f, -0.46f };
                var view = Mat4f.LookAt(new float[16], sun, new float[3], new float[] { 0, 1, 0 });
                double sl = Math.Sqrt(sun[0] * sun[0] + sun[1] * sun[1] + sun[2] * sun[2]);
                var rnd = new Random(17);
                double worst = 0, deeper = double.MaxValue, deepest = 0;
                var mvp0 = new float[16];
                var mvp = new float[16];
                const float push = 0.3f;
                for (int n = 0; n < 300; n++)
                {
                    double yaw = rnd.NextDouble() * Math.PI * 2, pitch = (rnd.NextDouble() - 0.5) * 0.4, roll = (rnd.NextDouble() - 0.5) * 0.8;
                    // her model matrix as ShipRenderer.SetModel builds it (camera-relative)
                    var model = new Matrixf().Translate(rnd.NextDouble() * 80 - 40, rnd.NextDouble() * 20 - 10, rnd.NextDouble() * 80 - 40)
                        .RotateY(ShipFrame.ModelYaw(yaw)).RotateZ((float)pitch).RotateX((float)roll);
                    ShipShadows.ShadowMvp(mvp0, proj, view, model.Values, 0);
                    ShipShadows.ShadowMvp(mvp, proj, view, model.Values, push);
                    float[] p = { (float)(cx + (rnd.NextDouble() - 0.5) * r), (float)(cy + (rnd.NextDouble() - 0.5) * r), (float)(cz + (rnd.NextDouble() - 0.5) * r), 1 };
                    var wp = model.TransformVector(new Vec4f(p[0], p[1], p[2], 1));
                    var a = Mat4f.MulWithVec4(proj, Mat4f.MulWithVec4(view, new[] { wp.X, wp.Y, wp.Z, 1f }));
                    // the same point `push` further from the sun, drawn plainly
                    var b = Mat4f.MulWithVec4(proj, Mat4f.MulWithVec4(view, new[] { (float)(wp.X - sun[0] / sl * push), (float)(wp.Y - sun[1] / sl * push), (float)(wp.Z - sun[2] / sl * push), 1f }));
                    var s0 = Mat4f.MulWithVec4(mvp0, p);
                    var s1 = Mat4f.MulWithVec4(mvp, p);
                    for (int i = 0; i < 4; i++) worst = Math.Max(worst, Math.Max(Math.Abs(s0[i] - a[i]), Math.Abs(s1[i] - b[i])));
                    worst = Math.Max(worst, Math.Max(Math.Abs(s1[0] - s0[0]), Math.Abs(s1[1] - s0[1])));
                    // depth in the map (0..1) grows by push / len: further from the sun
                    double dd = (s1[2] - s0[2]) / 2 * len;
                    deeper = Math.Min(deeper, dd);
                    deepest = Math.Max(deepest, dd);
                }
                Check(worst < 1e-4 && Math.Abs(deeper - push) < 1e-3 && Math.Abs(deepest - push) < 1e-3,
                    $"her shadow's matrix is the shadow map's projection × the sun's view × her model matrix: the same place in the map as drawn, only stored {push} further from the sun (off by at most {worst:0.0e0}; pushed {deeper:0.000}..{deepest:0.000})");
            }
        }

        /// <summary>What the crosshair meets from a few places aboard and round her, looking all round: the parts, as codes.</summary>
        static void PartsReport(ShipDefinition def)
        {
            var map = def.PartMap;
            double deck = def.DeckY;
            var places = new (string name, double x, double y, double z)[]
            {
                ("waist, starboard", 4.0, deck + 1.6, 2.5), ("quarterdeck by the plate", -7.75, deck + 4.6, -1.5), ("great cabin", -9.0, deck + 1.6, 1.5),
                ("forecastle deck", 15.0, deck + 4.6, 1.0), ("lower deck", 3.0, deck - 1.4, 1.5), ("hold", -5.0, def.Decks[0].Y + 1.6, 1.0),
                ("in the water, port", 2.0, def.Waterline + 0.4, -8.5), ("in the water, astern", -21.0, def.Waterline + 0.4, 0.5),
            };
            foreach (var (name, x, y, z) in places)
            {
                var counts = new System.Collections.Generic.SortedDictionary<string, int>();
                for (int a = 0; a < 24; a++)
                    for (int e = -3; e <= 2; e++)
                    {
                        double yaw = a * Math.PI / 12, pitch = e * 0.35;
                        var d = new Vec3d(Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Sin(yaw) * Math.Cos(pitch));
                        if (!ShipParts.Ray(def, def.Grid, 0, 0, 0, Math.PI / 2, 0, 0, new Vec3d(x, y, z), d, 5, f => f.Type != "attach", f => false, f => null, out var h)) continue;
                        string what = h.Part.Kind switch
                        {
                            ShipPartKind.Hull => $"hull {(h.Part.Side > 0 ? "stbd" : "port")} {map.LengthZone(h.Part.Station)} {map.HeightZone(h.Part.Station, h.Part.Band)}",
                            ShipPartKind.Deck => $"deck {def.Decks[h.Part.Index].Code}{(h.Part.Band != 0 ? " underside" : "")}{(map.RoomOf(h.Part) is RoomDef r ? " in " + r.Code : "")}",
                            ShipPartKind.Mast => $"mast {def.Masts[h.Part.Index].Code}",
                            ShipPartKind.Structure => $"woodwork {(map.RoomOf(h.Part)?.Code ?? map.LengthZone(h.Part.Station))}",
                            ShipPartKind.Fixture => $"fixture {h.Fixture.Code}",
                            _ => "?"
                        };
                        counts[what] = counts.TryGetValue(what, out int c) ? c + 1 : 1;
                    }
                Console.WriteLine($"{name} ({x}, {y}, {z}): " + string.Join(", ", System.Linq.Enumerable.Select(counts, kv => $"{kv.Key} ×{kv.Value}")));
            }
        }

        /// <summary>A vanilla copper plate, as far as a stack can be made without the game (null if it can't).</summary>
        static ItemStack PlateStack()
        {
            try { return new ItemStack(new Item { Code = new AssetLocation("game", "metalplate-copper") }); }
            catch { return null; }
        }

        /// <summary>The dry outline's half-width at ship-local (x, y) as Sea draws it (0 outside its ends).</summary>
        static double DryAt(WaterClipDef wc, double x, double y)
        {
            const int S = WaterClipDef.DryStations, L = WaterClipDef.DryLevels;
            double len = wc.HalfLength - wc.Margin, u = (x - wc.CenterX) / len;
            if (x - wc.CenterX > len || x - wc.CenterX < wc.DryRake * (y - wc.Height) - len) return 0;
            double s = Math.Clamp((u + 1) * (S - 1) / 2.0, 0, S - 1), t = Math.Clamp((y - wc.DryRowY0) / wc.DryRowDY, 0, L - 1);
            int i = (int)Math.Floor(s), j = (int)Math.Floor(t), i2 = Math.Min(i + 1, S - 1), j2 = Math.Min(j + 1, L - 1);
            var p = wc.DryProfile;
            double lo = p[j * S + i] + (p[j * S + i2] - p[j * S + i]) * (s - i), hi = p[j2 * S + i] + (p[j2 * S + i2] - p[j2 * S + i]) * (s - i);
            return lo + (hi - lo) * (t - j);
        }

        static void ClimbRoundTrip(VoxelGrid g, LadderDef[] ladders, LadderDef l)
        {
            double ax = l.Bottom[0] - l.ExitBottom[0], az = l.Bottom[2] - l.ExitBottom[2], al = Math.Sqrt(ax * ax + az * az);
            ax /= al; az /= al;
            var m = new DeckMover(l.ExitBottom[0] - ax * 0.6, l.ExitBottom[1], l.ExitBottom[2] - az * 0.6);
            m.Unstick(g);
            for (int i = 0; i < 20; i++) m.Step(g, 1 / 60f, 0, 0, 0, false, false, 1, ladders, 0);
            bool started = false;
            double worst = 0, prev = m.SmoothY;
            for (int i = 0; i < 14 * 60 && !(started && m.Climbing == null); i++)
            {
                m.Step(g, 1 / 60f, ax, az, 3.5, false, false, 1, ladders, 1);
                started |= m.Climbing != null;
                worst = Math.Max(worst, Math.Abs(m.SmoothY - prev));
                prev = m.SmoothY;
            }
            Check(started && m.Climbing == null && Math.Abs(m.Y - l.ExitTop[1]) < 0.3 && m.OnGround,
                $"{l.Code}: up the shrouds onto the top (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00}, smooth {worst:0.000}/tick)");
            Check(worst < 0.1, $"{l.Code}: the camera glides on the way up ({worst:0.000} blocks/tick)");

            // keep holding W for a moment on the top, on the way the ladder brings you off: you step off, not back into it
            // (this ladder only: since 0.3.6 the hold's comes up just forward of the companionway's foot, and walking on
            // takes you up that one, as it should: see "from the hold up its ladder and on up the companionway")
            double ex = Math.Sign(l.ExitTop[0] - l.Top[0]);    // fore or aft, as the ladder brings you off
            for (int i = 0; i < 30; i++) m.Step(g, 1 / 60f, ex, 0, 3.5, false, false, 1, new[] { l }, 1);
            Check(m.Climbing == null && Math.Abs(m.Y - l.ExitTop[1]) < 0.3, $"{l.Code}: walking on at the top stays on the platform ({m.X:0.00},{m.Y:0.00},{m.Z:0.00})");
            for (int i = 0; i < 20; i++) m.Step(g, 1 / 60f, 0, 0, 0, false, false, 1, ladders, 0);
            // back to where the shrouds come up, then over the edge (still W) and all the way down
            for (int i = 0; i < 90 && m.Climbing == null; i++)
            {
                double tx = l.ExitTop[0] - m.X, tz = l.ExitTop[2] - m.Z, tl = Math.Sqrt(tx * tx + tz * tz);
                double ox = l.Bottom[0] - l.Top[0], oz = l.Bottom[2] - l.Top[2], ol = Math.Sqrt(ox * ox + oz * oz);
                double wx = tl > 0.3 ? tx / tl : ox / ol, wz = tl > 0.3 ? tz / tl : oz / ol;
                m.Step(g, 1 / 60f, wx, wz, 3.5, false, false, 1, ladders, 1);
            }
            bool down = m.Climbing != null;
            for (int i = 0; i < 14 * 60 && m.Climbing != null; i++)
            {
                double ox = l.Bottom[0] - l.Top[0], oz = l.Bottom[2] - l.Top[2], ol = Math.Sqrt(ox * ox + oz * oz);
                m.Step(g, 1 / 60f, ox / ol, oz / ol, 3.5, false, false, 1, ladders, 1);
            }
            for (int i = 0; i < 30; i++) m.Step(g, 1 / 60f, ax, az, 3.5, false, false, 1, ladders, 1);
            Check(down && m.Climbing == null && Math.Abs(m.Y - l.ExitBottom[1]) < 0.3,
                $"{l.Code}: over the edge of the top and back down to the deck (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00})");
        }

        /// <summary>
        /// The rope ladder down the side: from the water up through the entry port onto the deck, and from the deck
        /// out through the gate and down into the water. A shut gate stops both.
        /// </summary>
        static void SideLadder(ShipDefinition def, LadderDef l)
        {
            var open = def.Grid.Clone();
            ShipFittings.ApplySolids(def, open, f => true, f => null);
            var shut = def.Grid.Clone();
            ShipFittings.ApplySolids(def, shut, f => false, f => null);

            // up from the water, gate open
            var m = new DeckMover(l.Bottom[0], l.Bottom[1], l.Bottom[2]);
            m.StartClimbAtBottom(l);
            m.TopClosed = _ => false;
            for (int i = 0; i < 10 * 60 && m.Climbing != null; i++) m.Step(open, 1 / 60f, 0, 0, 0, false, false, 1, def.Ladders, 1);
            for (int i = 0; i < 20; i++) m.Step(open, 1 / 60f, 0, 0, 0, false, false, 1, def.Ladders, 0);
            Check(m.Climbing == null && Math.Abs(m.Y - def.DeckY) < 0.05 && m.OnGround && Math.Abs(m.Z) < Math.Abs(l.Top[2]) - 0.5,
                $"{l.Code}: up from the water through the entry port onto the deck (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00})");

            // up from the water, gate shut: you hang at the top
            m = new DeckMover(l.Bottom[0], l.Bottom[1], l.Bottom[2]);
            m.StartClimbAtBottom(l);
            m.TopClosed = _ => true;
            for (int i = 0; i < 10 * 60; i++) m.Step(shut, 1 / 60f, 0, 0, 0, false, false, 1, def.Ladders, 1);
            Check(m.Climbing == l && m.ClimbT >= 0.999, $"{l.Code}: a shut gate keeps you on the ladder at the top");

            // down: walk out through the open gate, keep going, off into the water
            double side = Math.Sign(l.Top[2]);
            m = new DeckMover(l.ExitTop[0], l.ExitTop[1], l.ExitTop[2]);
            m.Unstick(open);
            m.TopClosed = _ => false;
            var res = DeckMover.Result.Ok;
            bool climbed = false;
            for (int i = 0; i < 10 * 60 && res == DeckMover.Result.Ok; i++)
            {
                res = m.Step(open, 1 / 60f, 0, side, 3.5, false, false, 1, def.Ladders, 1);
                climbed |= m.Climbing != null;
            }
            Check(climbed && res == DeckMover.Result.LeftShip && Math.Abs(m.Y - l.ExitBottom[1]) < 0.01,
                $"{l.Code}: out through the gate, down the ladder and into the water ({res} at {m.X:0.00},{m.Y:0.00},{m.Z:0.00})");

            // the same with the gate shut: you stay aboard
            m = new DeckMover(l.ExitTop[0], l.ExitTop[1], l.ExitTop[2]);
            m.Unstick(shut);
            m.TopClosed = _ => true;
            climbed = false;
            for (int i = 0; i < 3 * 60; i++) { m.Step(shut, 1 / 60f, 0, side, 3.5, false, false, 1, def.Ladders, 1); climbed |= m.Climbing != null; }
            Check(!climbed && Math.Abs(m.Y - def.DeckY) < 0.05, $"{l.Code}: with the gate shut you can't get onto the ladder");
        }

        /// <summary>
        /// For each Beaufort force: the wind, Sea's fully developed sea (its formulas), her speed and lean on a beam reach
        /// under full and half sail, and over two minutes of that sea the biggest roll, pitch and heave from the waves,
        /// at rest and under way with the waves on the beam, ahead and astern, and how near the water comes to her lee
        /// deck edge amidships and to her stern windows (lean included).
        /// </summary>
        static void MotionReport(ShipDefinition def)
        {
            var md = def.Motion;
            var sd = def.Sailing;
            def.Waterplane(out double hl, out double hb, out double cx);
            // Beaufort's forces (the middle of each; 12 is Sea's strongest wind, 1.5 units)
            double[] ms = { 0.3, 1.0, 2.5, 4.4, 6.7, 9.4, 12.3, 15.5, 19.0, 22.6, 26.5, 30.6, 37.5 };
            var spec = new Sea.WaveSpectrum(4242, 0);
            double deckX = 2.0, deckZ = def.HullHalfWidthAtDeck, deckY = def.DeckY;
            double sternX = def.WaterClip.CenterX - (def.WaterClip.HalfLength - def.WaterClip.Margin);
            Console.WriteLine($"{def.Code}: deck {deckY:0.0} above the water at the side ({deckZ:0.0} out), stern lights from 2.5 above it; roll period {md.RollPeriod}s, damping {md.RollDamping}");
            Console.WriteLine(" F   m/s  Hs   T  | speed heel full/half | waves at rest beam: roll pitch heave | under way (full sail) beam / head / astern: roll pitch | water over lee deck edge | over stern lights");
            for (int f = 1; f <= 12; f++)
            {
                double s = ms[f] / Sea.Beaufort.MetersPerSecondPerUnit;
                double hs = 0.1 + 2.4 * s * s, T = 2.5 + 3.2 * Math.Sqrt(hs);
                var sea = new Sea.SeaState(hs, 0, 1, T);          // running toward +z
                var wind = Sea.Wind.FromCompass(0, s);             // from the north, toward +z
                double v = def.MaxSpeed * Sailing.SpeedFactor(90, s, 1, sd);
                double heelFull = ShipMotion.Heel(wind.X, wind.Z, Math.PI / 2, v, 1, md, sd) * 180 / Math.PI;
                double heelHalf = ShipMotion.Heel(wind.X, wind.Z, Math.PI / 2, def.MaxSpeed * Sailing.SpeedFactor(90, s, 0.55, sd), 0.55, md, sd) * 180 / Math.PI;
                var place = new Sea.WavePlace();
                (double roll, double pitch, double heave, double deck, double stern) Run(double yaw, double speed, double heelDeg)
                {
                    double rMax = 0, pMax = 0, hMax = 0, deckMax = double.MinValue, sternMax = double.MinValue;
                    double x0 = 1000, z0 = 2000;
                    double steady = speed > 0 ? ShipMotion.SailSteadiness(1, s) : 0;     // under way, full sail
                    for (double t = 600; t < 720; t += 0.1)
                    {
                        double x = x0 + Math.Sin(yaw) * speed * (t - 600), z = z0 + Math.Cos(yaw) * speed * (t - 600);
                        place.Update(spec, t, sea, wind);
                        var a = ShipMotion.Waves(place, 1, t, x, z, yaw, speed, hl, hb, cx, md, steady);
                        rMax = Math.Max(rMax, Math.Abs(a.Roll)); pMax = Math.Max(pMax, Math.Abs(a.Pitch)); hMax = Math.Max(hMax, Math.Abs(a.Heave));
                        double roll = a.Roll + heelDeg * Math.PI / 180;
                        // the lee side (starboard, down with a positive lean) amidships, and the stern lights' corners
                        foreach (var (lx, ly, lz, isDeck) in new[] { (deckX, deckY, deckZ, true), (deckX, deckY, -deckZ, true), (sternX, 2.5, 2.4, false), (sternX, 2.5, -2.4, false) })
                        {
                            ShipFrame.ToWorld(yaw, a.Pitch, roll, lx, ly, lz, out double wx, out double wy, out double wz);
                            double surf = 0;
                            for (int i = 0; i < place.Count; i++) surf += place.Amp[i] * Math.Sin(place.Kx[i] * (x + wx) + place.Kz[i] * (z + wz) - place.Omega[i] * t + place.Phase[i]);
                            double over = surf - (a.Heave + wy);
                            if (isDeck) deckMax = Math.Max(deckMax, over); else sternMax = Math.Max(sternMax, over);
                        }
                    }
                    return (rMax * 180 / Math.PI, pMax * 180 / Math.PI, hMax, deckMax, sternMax);
                }
                var rest = Run(Math.PI / 2, 0, 0);
                var beam = Run(Math.PI / 2, v, heelFull);
                var head = Run(Math.PI, v, 0);
                var astern = Run(0, v, 0);
                Console.WriteLine($"{f,2} {ms[f],5:0.0} {hs,4:0.0} {T,4:0.0} | {v,4:0.0} {heelFull,5:0.0}/{heelHalf,4:0.0}° | {rest.roll,5:0.0}° {rest.pitch,4:0.0}° {rest.heave,4:0.0} | " +
                    $"{beam.roll,4:0.0}/{head.roll,4:0.0}/{astern.roll,4:0.0}° {beam.pitch,4:0.0}/{head.pitch,4:0.0}/{astern.pitch,4:0.0}° | {beam.deck,5:+0.0;-0.0}  {head.deck,5:+0.0;-0.0} | {beam.stern,5:+0.0;-0.0} {astern.stern,5:+0.0;-0.0}");
            }
        }

        /// <summary>The middle (z) of the first solid run across the ship at (x, y) out from the centreline: a stair's step.</summary>
        static double StairZ(VoxelGrid g, double x, double y)
        {
            double z0 = double.NaN, z1 = double.NaN;
            for (double z = 0.5 + 0.0625; z < 5.5; z += 0.125)
            {
                bool s = g.SolidAt(x, y, z);
                if (s && double.IsNaN(z0)) z0 = z;
                if (!s && !double.IsNaN(z0)) { z1 = z; break; }
            }
            return double.IsNaN(z0) || double.IsNaN(z1) ? double.NaN : (z0 + z1) / 2 - 0.0625;
        }

        /// <summary>Walk around on the top: the platform holds you and its rim keeps you on it.</summary>
        static void WalkTop(VoxelGrid g, LadderDef[] ladders, LadderDef l)
        {
            var m = new DeckMover(l.ExitTop[0], l.ExitTop[1], l.ExitTop[2]);
            m.Unstick(g);
            double y0 = m.Y, cx = l.Top[0] > l.ExitTop[0] ? l.ExitTop[0] + 0.8 : l.ExitTop[0] - 0.8;
            foreach (var (dx, dz) in new[] { (-1.0, 0.0), (0.0, -1.0), (1.0, 0.0), (0.0, 1.0) })
                for (int i = 0; i < 90; i++) m.Step(g, 1 / 60f, dx, dz, 3.5, false, false, 1, null, 0);
            Check(Math.Abs(m.Y - y0) < 0.3 && m.OnGround, $"walking around the main top keeps you on it (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00})");
        }

        static void Smoothness(VoxelGrid g, string what, double x, double y, double z, double dx, double dz, double speed, double maxStep)
        {
            var m = new DeckMover(x, y, z);
            m.Unstick(g);
            for (int i = 0; i < 30; i++) m.Step(g, 1 / 60f, 0, 0, 0, false, false);  // settle
            double prev = m.SmoothY, worst = 0, y0 = m.Y, climbed = 0;
            for (int i = 0; i < 240; i++)
            {
                m.Step(g, 1 / 60f, dx, dz, speed, false, false);
                worst = Math.Max(worst, Math.Abs(m.SmoothY - prev));
                prev = m.SmoothY;
                if (Math.Abs(m.Y - y0) > Math.Abs(climbed)) climbed = m.Y - y0;
            }
            Check(worst <= maxStep && Math.Abs(climbed) > 2.5, $"{what}: {worst:0.000} blocks/tick (limit {maxStep}), climbed {climbed:0.0}");
        }

        static void Walk(VoxelGrid g, string what, double x, double y, double z, double dx, double dz, double seconds, bool jump, bool sneak,
            System.Func<DeckMover, DeckMover.Result, bool> ok)
        {
            var m = new DeckMover(x, y, z);
            m.Unstick(g);
            var res = DeckMover.Result.Ok;
            for (int i = 0; i < seconds * 60 && res == DeckMover.Result.Ok; i++)
            {
                res = m.Step(g, 1 / 60f, dx, dz, 3.5, jump, sneak);
            }
            Check(ok(m, res), $"{what} (ended at {m.X:0.00},{m.Y:0.00},{m.Z:0.00} {res})");
        }
    }
}
