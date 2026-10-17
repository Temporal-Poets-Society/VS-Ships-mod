using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hansa.Construction;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Exports a ship design's default blocks and fittings, exactly as the construction core
/// builds them, to docs/preview/&lt;code&gt;.glb and a self-contained WebGL viewer.
///
///   dotnet run --project tools/HansaPreview [design.json] [--out dir]
/// </summary>
static class Program
{
    /// <summary>Viewer groups: what a toggle shows or hides.</summary>
    static readonly string[] Categories = { "hull", "frame", "deck", "walls", "stair", "glass" };

    static string Category(string part) => part switch
    {
        Parts.Rib or Parts.Beam or Parts.Keel => "frame",
        Parts.Deck => "deck",
        "wall" or "door" => "walls",
        "stringer" or "tread" => "stair",
        "glass" => "glass",
        _ => "hull",
    };

    /// <summary>Vanilla texture for a part in a material: planking from planks, timber from
    /// debarked wood, panes from plain glass.</summary>
    static string Texture(string part, string material)
    {
        string wood = "oak";
        var code = material?.Split(':').Last() ?? "";
        if (code.StartsWith("planks-")) wood = code.Split('-')[1];
        return part switch
        {
            "glass" => "glass/plain.png",
            Parts.Rib or Parts.Beam or Parts.Keel or "stringer" => $"wood/debarked/{wood}.png",
            "wall" => $"wood/planks/{wood}2.png",
            "door" => $"wood/planks/{wood}3.png",
            _ => $"wood/planks/{wood}1.png",
        };
    }

    sealed class Batch
    {
        public string Texture, Category;
        public readonly List<float> Pos = new(), Nrm = new(), Uv = new();
        public readonly List<uint> Index = new();
    }

    static int Main(string[] args)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Hansa.csproj"))) dir = dir.Parent;
        string root = dir.FullName;
        if (Array.IndexOf(args, "--skin") >= 0) return SkinMain(args, root);
        string designPath = args.FirstOrDefault(a => !a.StartsWith("--") && a.EndsWith(".json"))
            ?? Path.Combine(root, "assets/hansa/config/shipdesigns/two-bay.json");
        int o = Array.IndexOf(args, "--out");
        string outDir = o >= 0 ? args[o + 1] : Path.Combine(root, "docs/preview");
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory");
        string textures = Path.Combine(game, "assets/survival/textures/block");

        var kinds = BlockKinds.Read(File.ReadAllText(Path.Combine(root, "assets/hansa/config/construction/kinds.json")));
        var design = ShipDesign.Read(File.ReadAllText(designPath));
        var lattice = new ShipLattice(design);
        if (lattice.Problems.Count > 0) { Console.Error.WriteLine(string.Join("\n", lattice.Problems)); return 1; }
        var ship = new ShipConstruction(lattice, kinds);

        var faces = new List<(ShipFace face, string category)>();
        foreach (var c in ship.Present.OrderBy(c => c))
            foreach (var f in ship.VisibleFaces(c)) faces.Add((f, Category(f.Part)));
        foreach (var f in ship.WallFaces.OrderBy(f => f))
            foreach (var x in ship.WallShape(f).Faces) faces.Add((x, "walls"));
        foreach (var s in ship.Stairs)
            foreach (var x in ship.StairShape(s).Faces) faces.Add((x, "stair"));

        var batches = new Dictionary<(string, string), Batch>();
        int triangles = 0;
        foreach (var (f, category) in faces)
        {
            var tex = Texture(f.Part, f.Material);
            if (!batches.TryGetValue((tex, category), out var b)) batches[(tex, category)] = b = new Batch { Texture = tex, Category = category };
            uint first = (uint)(b.Pos.Count / 3);
            for (int i = 0; i < f.Points.Length; i++)
            {
                var p = f.Points[i];
                b.Pos.AddRange(new[] { (float)p.X, (float)p.Y, (float)p.Z });
                b.Nrm.AddRange(new[] { (float)f.Normal.X, (float)f.Normal.Y, (float)f.Normal.Z });
                // 16 texels per block of surface; one texture repeat per block.
                b.Uv.AddRange(new[] { (float)(f.Uv[i][0] / 16), (float)(f.Uv[i][1] / 16) });
            }
            for (int i = 1; i + 1 < f.Points.Length; i++) { b.Index.AddRange(new[] { first, first + (uint)i, first + (uint)i + 1 }); triangles++; }
        }

        // Cell edges of every block, for checking alignment against the grid.
        var edges = new List<float>();
        foreach (var c in ship.Present.OrderBy(c => c))
        {
            var (lo, hi) = lattice.Bounds(c);
            for (int a = 0; a < 3; a++)
            {
                int u = (a + 1) % 3, v = (a + 2) % 3;
                foreach (var (du, dv) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
                {
                    var s = lo.With(u, du == 0 ? lo[u] : hi[u]).With(v, dv == 0 ? lo[v] : hi[v]);
                    var e = s.With(a, hi[a]);
                    edges.AddRange(new[] { (float)s.X, (float)s.Y, (float)s.Z, (float)e.X, (float)e.Y, (float)e.Z });
                }
            }
        }

        var texList = batches.Values.Select(b => b.Texture).Distinct().OrderBy(t => t).ToList();
        var png = texList.ToDictionary(t => t, t => File.ReadAllBytes(Path.Combine(textures, t)));
        var ordered = batches.Values.OrderBy(b => Array.IndexOf(Categories, b.Category)).ThenBy(b => b.Texture).ToList();
        Directory.CreateDirectory(outDir);

        File.WriteAllBytes(Path.Combine(outDir, design.Code + ".glb"), Glb(ordered, texList, png));

        var data = new JObject
        {
            ["name"] = design.Name,
            ["code"] = design.Code,
            ["waterline"] = design.Waterline,
            ["textures"] = new JArray(texList.Select(t => new JObject { ["name"] = t, ["png"] = Convert.ToBase64String(png[t]), ["glass"] = t.StartsWith("glass") })),
            ["batches"] = new JArray(ordered.Select(b => new JObject
            {
                ["texture"] = texList.IndexOf(b.Texture), ["category"] = b.Category,
                ["pos"] = B64(b.Pos), ["nrm"] = B64(b.Nrm), ["uv"] = B64(b.Uv),
                ["index"] = Convert.ToBase64String(b.Index.SelectMany(BitConverter.GetBytes).ToArray()),
            })),
            ["edges"] = B64(edges),
            ["stats"] = $"{ship.Present.Count()} blocks, {ship.WallFaces.Count()} wall faces, {ship.Stairs.Count} stair(s), {triangles} triangles",
        };
        var template = File.ReadAllText(Path.Combine(root, "tools/HansaPreview/viewer.html"));
        var html = template.Replace("__SHIP_DATA__", data.ToString(Formatting.None).Replace("</", "<\\/"));
        File.WriteAllText(Path.Combine(outDir, design.Code + ".html"), html, new UTF8Encoding(false));
        Console.WriteLine($"{design.Code}: {data["stats"]}; wrote {outDir}");
        return 0;
    }

    /// <summary>
    /// A ship's mesh as the game draws it (a config/shipmeshes file, or one exported by HansaTests -- export-grid), in the
    /// same viewer: each mesh group a toggle, the game's own textures (an overlay after "++" left out), and with --def, a
    /// cross at every fixture, hook, moving part's axis and ladder end (to see what floats).
    ///   dotnet run --project tools/HansaPreview -- --skin mesh.json [--def ship.json] [--shapes dir] [--code name] [--out dir]
    /// </summary>
    static int SkinMain(string[] args, string root)
    {
        string Opt(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string meshPath = Opt("--skin"), defPath = Opt("--def");
        string code = Opt("--code") ?? Path.GetFileNameWithoutExtension(meshPath).Replace(".mesh", "");
        string outDir = Opt("--out") ?? Path.Combine(root, "docs/preview");
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory");
        var file = JObject.Parse(File.ReadAllText(meshPath));
        var tex = (file["textures"] ?? file["Textures"]).ToObject<string[]>();
        var groups = (JObject)(file["groups"] ?? file["Groups"]);
        string Category(string g) =>
            g.StartsWith("part:gun") ? "guns" : g.StartsWith("part:") ? "parts" : g.StartsWith("slot:hull") ? "hull slots" :
            g.StartsWith("slot:deck") ? "deck slots" : g.StartsWith("glass") ? "glass" : g.StartsWith("sail") ? "sails" : g;
        string FileOf(string t)
        {
            string b = t.Split("++")[0];
            int c = b.IndexOf(':');
            string domain = c >= 0 ? b.Substring(0, c) : "game", path = c >= 0 ? b.Substring(c + 1) : b;
            return domain == "game" ? Path.Combine(game, "assets/survival/textures", path + ".png") : Path.Combine(root, "assets", domain, "textures", path + ".png");
        }
        var batches = new Dictionary<(int, string), Batch>();
        int quads = 0, hidden = 0;
        // (a slot's quads as a new ship draws them, every slot in: a quad facing a slot (its pair's second) shows only once
        // that slot is out: docs/legacy/slots.md §3)
        var slotQuads = (JObject)(file["slotQuads"] ?? file["SlotQuads"]);
        foreach (var g in groups.Properties())
        {
            if (g.Name == "shot" || g.Name.EndsWith("bottomclad")) continue;
            var pairs = slotQuads?[g.Name] as JArray;
            int qi = -1;
            foreach (JArray q in (JArray)g.Value)
            {
                qi++;
                if (pairs != null && 2 * qi + 1 < pairs.Count && (int)pairs[2 * qi + 1] >= 0) { hidden++; continue; }
                int t = (int)q[0];
                var key = (t, Category(g.Name));
                if (!batches.TryGetValue(key, out var bt)) batches[key] = bt = new Batch { Texture = tex[t], Category = key.Item2 };
                uint first = (uint)(bt.Pos.Count / 3);
                for (int k = 0; k < 4; k++)
                {
                    int o = 1 + 8 * k;
                    bt.Pos.AddRange(new[] { (float)q[o], (float)q[o + 1], (float)q[o + 2] });
                    bt.Uv.AddRange(new[] { (float)q[o + 3] / 16f, (float)q[o + 4] / 16f });
                    bt.Nrm.AddRange(new[] { (float)q[o + 5], (float)q[o + 6], (float)q[o + 7] });
                }
                bt.Index.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                quads++;
            }
        }
        // her voxel shapes (her decks' planking: config "shapes" exterior and interior), each element's faces as quads
        var texList0 = tex.ToList();
        foreach (var shapeName in new[] { "exterior", "interior" })
        {
            string sp = Opt("--shapes") is string dirS ? Path.Combine(dirS, shapeName + ".json") : null;
            if (sp == null || !File.Exists(sp)) continue;
            var sh = JObject.Parse(File.ReadAllText(sp));
            var stex = (JObject)sh["textures"];
            double tw = (double?)sh["textureWidth"] ?? 16, th = (double?)sh["textureHeight"] ?? 16;
            foreach (JObject el in (JArray)sh["elements"])
            {
                var f0 = el["from"].ToObject<double[]>().Select(v => v / 16).ToArray();
                var f1 = el["to"].ToObject<double[]>().Select(v => v / 16).ToArray();
                foreach (var face in ((JObject)el["faces"] ?? new JObject()).Properties())
                {
                    string key = ((string)face.Value["texture"] ?? "").TrimStart('#');
                    string path = (string)stex[key] ?? key;
                    int ti = texList0.IndexOf(path);
                    if (ti < 0) { texList0.Add(path); ti = texList0.Count - 1; }
                    var uv = face.Value["uv"]?.ToObject<double[]>() ?? new double[] { 0, 0, 16, 16 };
                    double u0 = uv[0] / tw, v0 = uv[1] / th, u1 = uv[2] / tw, v1 = uv[3] / th;
                    (double[] a, double[] b, double[] c, double[] d, float[] n) = face.Name switch
                    {
                        "north" => (new[] { f1[0], f0[1], f0[2] }, new[] { f0[0], f0[1], f0[2] }, new[] { f0[0], f1[1], f0[2] }, new[] { f1[0], f1[1], f0[2] }, new float[] { 0, 0, -1 }),
                        "south" => (new[] { f0[0], f0[1], f1[2] }, new[] { f1[0], f0[1], f1[2] }, new[] { f1[0], f1[1], f1[2] }, new[] { f0[0], f1[1], f1[2] }, new float[] { 0, 0, 1 }),
                        "east" => (new[] { f1[0], f0[1], f1[2] }, new[] { f1[0], f0[1], f0[2] }, new[] { f1[0], f1[1], f0[2] }, new[] { f1[0], f1[1], f1[2] }, new float[] { 1, 0, 0 }),
                        "west" => (new[] { f0[0], f0[1], f0[2] }, new[] { f0[0], f0[1], f1[2] }, new[] { f0[0], f1[1], f1[2] }, new[] { f0[0], f1[1], f0[2] }, new float[] { -1, 0, 0 }),
                        "up" => (new[] { f0[0], f1[1], f1[2] }, new[] { f1[0], f1[1], f1[2] }, new[] { f1[0], f1[1], f0[2] }, new[] { f0[0], f1[1], f0[2] }, new float[] { 0, 1, 0 }),
                        _ => (new[] { f0[0], f0[1], f0[2] }, new[] { f1[0], f0[1], f0[2] }, new[] { f1[0], f0[1], f1[2] }, new[] { f0[0], f0[1], f1[2] }, new float[] { 0, -1, 0 }),
                    };
                    var bkey = (ti, "decks (shapes)");
                    if (!batches.TryGetValue(bkey, out var bt)) batches[bkey] = bt = new Batch { Texture = path, Category = bkey.Item2 };
                    uint first = (uint)(bt.Pos.Count / 3);
                    foreach (var (pt, uu, vv) in new[] { (a, u0, v1), (b, u1, v1), (c, u1, v0), (d, u0, v0) })
                    {
                        bt.Pos.AddRange(new[] { (float)pt[0], (float)pt[1], (float)pt[2] });
                        bt.Uv.AddRange(new[] { (float)uu, (float)vv });
                        bt.Nrm.AddRange(n);
                    }
                    bt.Index.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                    quads++;
                }
            }
        }

        var marks = new List<float>();
        if (defPath != null)
        {
            var d = JObject.Parse(File.ReadAllText(defPath));
            void Cross(JToken p)
            {
                if (p is not JArray a || a.Count < 3) return;
                float x = (float)a[0], y = (float)a[1], z = (float)a[2], r = 0.3f;
                marks.AddRange(new[] { x - r, y, z, x + r, y, z, x, y - r, z, x, y + r, z, x, y, z - r, x, y, z + r });
            }
            foreach (var f in d["fixtures"] ?? new JArray()) Cross(f["pos"]);
            foreach (var h in d["hooks"] ?? new JArray()) Cross(h["pos"]);
            foreach (var m in d["movingParts"] ?? new JArray()) Cross(m["axisPoint"]);
            foreach (var l in d["ladders"] ?? new JArray()) { Cross(l["bottom"]); Cross(l["top"]); }
        }
        var texList = batches.Values.Select(b => b.Texture).Distinct().OrderBy(t => t).ToList();
        var blank = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var png = texList.ToDictionary(t => t, t => File.Exists(FileOf(t)) ? File.ReadAllBytes(FileOf(t)) : blank);
        var ordered = batches.Values.OrderBy(b => b.Category).ThenBy(b => b.Texture).ToList();
        var data = new JObject
        {
            ["name"] = code, ["code"] = code, ["waterline"] = 0, ["nocull"] = true,
            ["textures"] = new JArray(texList.Select(t => new JObject { ["name"] = t, ["png"] = Convert.ToBase64String(png[t]), ["glass"] = t.Contains("glass") })),
            ["batches"] = new JArray(ordered.Select(b => new JObject
            {
                ["texture"] = texList.IndexOf(b.Texture), ["category"] = b.Category,
                ["pos"] = B64(b.Pos), ["nrm"] = B64(b.Nrm), ["uv"] = B64(b.Uv),
                ["index"] = Convert.ToBase64String(b.Index.SelectMany(BitConverter.GetBytes).ToArray()),
            })),
            ["edges"] = B64(new List<float>()),
            ["marks"] = B64(marks),
            ["stats"] = $"{quads} quads in {groups.Count} groups and her shapes ({hidden} slot faces hidden, as with every slot in); {marks.Count / 18} fittings marked",
        };
        Directory.CreateDirectory(outDir);
        var template = File.ReadAllText(Path.Combine(root, "tools/HansaPreview/viewer.html"));
        File.WriteAllText(Path.Combine(outDir, code + ".html"), template.Replace("__SHIP_DATA__", data.ToString(Formatting.None).Replace("</", "<\\/")), new UTF8Encoding(false));
        Console.WriteLine($"{code}: {data["stats"]}; wrote {outDir}");
        return 0;
    }

    static string B64(List<float> v) => Convert.ToBase64String(v.SelectMany(BitConverter.GetBytes).ToArray());

    /// <summary>glTF 2.0 binary: one primitive per texture and group, textures embedded as PNG,
    /// repeating with nearest filtering as the game draws them.</summary>
    static byte[] Glb(List<Batch> batches, List<string> textures, Dictionary<string, byte[]> png)
    {
        var bin = new MemoryStream();
        var views = new JArray(); var accessors = new JArray();
        int View(byte[] bytes, int? target)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var view = new JObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = bytes.Length };
            if (target != null) view["target"] = target;
            bin.Write(bytes);
            views.Add(view);
            return views.Count - 1;
        }
        int Accessor(List<float> data, int width, string type, bool bounds)
        {
            var acc = new JObject { ["bufferView"] = View(data.SelectMany(BitConverter.GetBytes).ToArray(), 34962),
                ["componentType"] = 5126, ["count"] = data.Count / width, ["type"] = type };
            if (bounds)
            {
                acc["min"] = new JArray(Enumerable.Range(0, width).Select(k => data.Where((_, i) => i % width == k).Min()));
                acc["max"] = new JArray(Enumerable.Range(0, width).Select(k => data.Where((_, i) => i % width == k).Max()));
            }
            accessors.Add(acc);
            return accessors.Count - 1;
        }
        var primitives = new JArray();
        foreach (var b in batches)
        {
            int pos = Accessor(b.Pos, 3, "VEC3", true), nrm = Accessor(b.Nrm, 3, "VEC3", false), uv = Accessor(b.Uv, 2, "VEC2", false);
            accessors.Add(new JObject { ["bufferView"] = View(b.Index.SelectMany(BitConverter.GetBytes).ToArray(), 34963),
                ["componentType"] = 5125, ["count"] = b.Index.Count, ["type"] = "SCALAR" });
            primitives.Add(new JObject { ["attributes"] = new JObject { ["POSITION"] = pos, ["NORMAL"] = nrm, ["TEXCOORD_0"] = uv },
                ["indices"] = accessors.Count - 1, ["material"] = textures.IndexOf(b.Texture) });
        }
        var images = new JArray(textures.Select(t => new JObject { ["bufferView"] = View(png[t], null), ["mimeType"] = "image/png", ["name"] = t }));
        var gltf = new JObject
        {
            ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "Hansa construction preview" },
            ["scene"] = 0, ["scenes"] = new JArray(new JObject { ["nodes"] = new JArray(0) }),
            ["nodes"] = new JArray(new JObject { ["mesh"] = 0, ["name"] = "ship" }),
            ["meshes"] = new JArray(new JObject { ["primitives"] = primitives }),
            ["samplers"] = new JArray(new JObject { ["magFilter"] = 9728, ["minFilter"] = 9728, ["wrapS"] = 10497, ["wrapT"] = 10497 }),
            ["images"] = images,
            ["textures"] = new JArray(textures.Select((_, i) => new JObject { ["sampler"] = 0, ["source"] = i })),
            ["materials"] = new JArray(textures.Select((t, i) => new JObject
            {
                ["name"] = t,
                ["pbrMetallicRoughness"] = new JObject { ["baseColorTexture"] = new JObject { ["index"] = i }, ["metallicFactor"] = 0, ["roughnessFactor"] = 1 },
                ["alphaMode"] = t.StartsWith("glass") ? "BLEND" : "MASK",
            })),
            ["bufferViews"] = views, ["accessors"] = accessors,
        };
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        gltf["buffers"] = new JArray(new JObject { ["byteLength"] = bin.Length });
        var json = Encoding.UTF8.GetBytes(gltf.ToString(Formatting.None));
        int pad = (4 - json.Length % 4) % 4;
        json = json.Concat(Enumerable.Repeat((byte)' ', pad)).ToArray();
        var outStream = new MemoryStream();
        var w = new BinaryWriter(outStream);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + json.Length + 8 + bin.Length));
        w.Write((uint)json.Length); w.Write(0x4E4F534Au); w.Write(json);
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin.ToArray());
        return outStream.ToArray();
    }
}
