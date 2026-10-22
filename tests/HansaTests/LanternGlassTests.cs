using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;

namespace HansaTests
{
    static partial class Program
    {
        /// <summary>
        /// (0.1.0-dev.7, note 43: "lanterns does not have light") A lantern aboard is drawn with its glass as vanilla has it:
        /// the glass elements of vanilla's lantern shapes are in the see-through pass (glowing), the candle and the metal
        /// solid; a mesh made face by face as the game's tesselator makes it (each face with its element's pass) splits into
        /// the solid faces, drawn with her solid things, and the glass, drawn with her glass (it was all drawn solid: black
        /// panes over the lit candle), each face whole with its texture and glow.
        /// </summary>
        static void LanternGlass()
        {
            Console.WriteLine("a lantern's glass aboard");
            string game = Environment.GetEnvironmentVariable("VINTAGE_STORY");
            if (string.IsNullOrEmpty(game)) game = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory");
            foreach (var size in new[] { "small", "large" })
                foreach (var hang in new[] { "ground", "ceiling" })
                {
                    string path = Path.Combine(game, "assets", "survival", "shapes", "block", "metal", "lantern", size, hang + ".json");
                    if (!File.Exists(path)) { Console.WriteLine($"  ({path} not there)"); continue; }
                    var shape = JObject.Parse(File.ReadAllText(path));
                    var faces = new List<(int pass, string tex, int glow)>();
                    void Walk(JToken els)
                    {
                        foreach (var e in els ?? new JArray())
                        {
                            int pass = e["renderPass"]?.Value<int>() ?? -1;
                            foreach (var f in (e["faces"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                                if (f.Value["enabled"]?.Value<bool>() != false && f.Value["texture"]?.Value<string>() is string t && t != "#null")
                                    faces.Add((pass, t, f.Value["glow"]?.Value<int>() ?? 0));
                            Walk(e["children"]);
                        }
                    }
                    Walk(shape["elements"]);

                    var texIds = faces.Select(f => f.tex).Distinct().Select((t, i) => (t, i)).ToDictionary(x => x.t, x => 100 + x.i);
                    var md = new MeshData(faces.Count * 4, faces.Count * 6, false, true, true, true).WithRenderpasses();
                    for (int q = 0; q < faces.Count; q++)
                    {
                        for (int v = 0; v < 4; v++) md.AddWithFlagsVertex(q, v is 1 or 2 ? 1 : 0, v >= 2 ? 1 : 0, 0, 0, -1, faces[q].glow);
                        md.AddIndices(q * 4, q * 4 + 1, q * 4 + 2, q * 4, q * 4 + 2, q * 4 + 3);
                        md.AddTextureId(texIds[faces[q].tex]);
                        md.AddRenderPass((short)faces[q].pass);
                    }
                    var solid = SeeThroughFaces.Split(md, out var glass);
                    var wantGlass = Enumerable.Range(0, faces.Count).Where(q => SeeThroughFaces.Pass(faces[q].pass)).ToList();
                    var wantSolid = Enumerable.Range(0, faces.Count).Where(q => !SeeThroughFaces.Pass(faces[q].pass)).ToList();
                    bool glassIsGlass = wantGlass.Count > 0 && wantGlass.All(q => faces[q].tex == "#glass" && faces[q].glow > 0) &&
                        faces.Where(f => f.tex == "#glass").All(f => SeeThroughFaces.Pass(f.pass)) && faces.Where(f => f.tex == "#candle").All(f => !SeeThroughFaces.Pass(f.pass));
                    Check(glassIsGlass, $"lantern {size} {hang}: its {wantGlass.Count} glass faces are see-through (and glow), its candle solid");
                    // each face whole where it was, by its first corner's x (its face number), its texture and glow with it
                    bool Same(MeshData m, List<int> want) => m != null && m.VerticesCount == want.Count * 4 && m.IndicesCount == want.Count * 6 &&
                        want.Select((q, k) => m.xyz[k * 12] == q && m.Flags[k * 4] == faces[q].glow && m.TextureIds[m.TextureIndices[k]] == texIds[faces[q].tex] &&
                            Enumerable.Range(0, 6).All(j => m.Indices[k * 6 + j] == k * 4 + new[] { 0, 1, 2, 0, 2, 3 }[j])).All(ok => ok);
                    Check(Same(solid, wantSolid) && Same(glass, wantGlass),
                        $"lantern {size} {hang}: split into {solid.VerticesCount / 4} solid faces and {glass?.VerticesCount / 4 ?? 0} of glass (of {faces.Count}), each whole with its texture and glow");
                }
            // a mesh without render passes (made here, not tesselated), or with none see-through, is given back as it was
            var plain = new MeshData(4, 6, false, true, true, true);
            for (int v = 0; v < 4; v++) plain.AddWithFlagsVertex(v, 0, 0, 0, 0, -1, 0);
            plain.AddIndices(0, 1, 2, 0, 2, 3);
            plain.AddTextureId(1);
            Check(SeeThroughFaces.Split(plain, out var none) == plain && none == null, "a mesh without render passes stays solid as it is");
        }
    }
}
