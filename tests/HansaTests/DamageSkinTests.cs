using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hansa;
using Newtonsoft.Json;

namespace HansaTests
{
    static partial class Program
    {
        static ShipSkinFile LoadSkinFile(ShipDefinition def, string assets) =>
            JsonConvert.DeserializeObject<ShipSkinFile>(File.ReadAllText(Path.Combine(assets, "config", "shipmeshes", def.Code + ".json")));

        /// <summary>The hull panel in the middle of her port side, amidships, at this height (ship-local).</summary>
        static DamagePart PanelNear(ShipDefinition def, double x, double y, int side = -1) =>
            def.DamageParts.Parts.Where(p => p.Kind == DamageKind.Hull && p.Part.Side == side)
                .OrderBy(p => Math.Abs(p.Center.X - x) + Math.Abs(p.Center.Y - y)).First();

        static double QuadArea(float[] q)
        {
            double Tri(int a, int b, int c)
            {
                double ux = q[1 + 8 * b] - q[1 + 8 * a], uy = q[2 + 8 * b] - q[2 + 8 * a], uz = q[3 + 8 * b] - q[3 + 8 * a];
                double vx = q[1 + 8 * c] - q[1 + 8 * a], vy = q[2 + 8 * c] - q[2 + 8 * a], vz = q[3 + 8 * c] - q[3 + 8 * a];
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                return 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
            }
            return Tri(0, 1, 2) + Tri(0, 2, 3);
        }

        /// <summary>Does a quad's box reach into what a hole can cut?</summary>
        static bool NearHole(float[] q, HullHole h)
        {
            for (int a = 0; a < 3; a++)
            {
                double lo = Math.Min(Math.Min(q[1 + a], q[9 + a]), Math.Min(q[17 + a], q[25 + a]));
                double hi = Math.Max(Math.Max(q[1 + a], q[9 + a]), Math.Max(q[17 + a], q[25 + a]));
                if (hi < h.Min[a] || lo > h.Max[a]) return false;
            }
            return true;
        }

        /// <summary>Where a point is for a hole (below 0 between a broken strake's ends, as the builder cuts; 1 off them).</summary>
        static double InHole(HullHole h, double x, double y, double z) => HullDamageSkin.HoleValue(h, x, y, z);

        /// <summary>Is this one of a hole's reveal quads (the same corners)?</summary>
        static bool IsReveal(float[] q, List<float[]> reveal) =>
            reveal.Any(r => Enumerable.Range(1, 32).All(e => Math.Abs(r[e] - q[e]) < 1e-6));

        /// <summary>
        /// The damage drawn on her (Hansa 0.3.5, 3b): the generator's planking marks, each planking quad's hull panel, the
        /// chunks built again for a change, a panel's overlay by its state, and a real hole (since the eighth client test
        /// broken planking: whole strakes from seam to seam, each off at its own length with splintered ends): cut out of the
        /// planking outside and in, its reveal the planking's thickness, nothing left inside it; the same at every size step
        /// and every time, a bigger one holding the smaller.
        /// </summary>
        static void DamageSkin(ShipDefinition def, string assets)
        {
            Console.WriteLine("damage drawn on her: the planking by panel, overlays, holes");
            var file = LoadSkinFile(def, assets);
            var l = def.DamageParts;
            var sw = Stopwatch.StartNew();
            var hs = new HullDamageSkin(file, l);
            long buildMs = sw.ElapsedMilliseconds;
            int marked = 0;
            foreach (var ranges in new[] { file.HullOut, file.HullIn })
                foreach (var kv in ranges ?? new Dictionary<string, int[]>())
                    if (HullDamageSkin.HullGroups.Contains(kv.Key))
                        for (int i = 0; i + 1 < kv.Value.Length; i += 2) marked += kv.Value[i + 1] - kv.Value[i];
            int assigned = hs.Chunks.Sum(c => c.Quads.Length);
            var hull = l.Parts.Where(p => p.Kind == DamageKind.Hull).ToArray();
            var outerPanels = new HashSet<int>(hs.Chunks.SelectMany(c => c.OuterPanels));
            int covered = hull.Count(p => outerPanels.Contains(p.Index));
            Check(file.HullThickness > 0 && hs.Usable && marked > 5000 && assigned >= marked * 0.93 && covered >= hull.Length * 0.9 && hs.Chunks.Count >= 20,
                $"the mesh file marks her planking ({marked} quads, {file.HullThickness} thick); {assigned} belong to a hull panel, {covered} of {hull.Length} panels have planking outside; {hs.Chunks.Count} chunks ({buildMs} ms)");
            // each planking quad is its panel's: its middle (the ceiling's: outside it) within the panel's face
            var rnd = new Random(5);
            int near = 0, tried = 0;
            foreach (var c in hs.Chunks)
                for (int n = 0; n < c.Quads.Length; n += 17)
                {
                    tried++;
                    if (hs.PanelOf(file.Groups[c.Group][c.Quads[n]], c.Inside[n]) == c.Panel[n] && l.Parts[c.Panel[n]].Kind == DamageKind.Hull) near++;
                }
            Check(near == tried, $"a planking quad is the panel's its middle is in (the ceiling's: the panel outside it): {near} of {tried}");

            // ---- inside (0.3.10, the user: "inside, damage textures are not visible, at least not as well as outside"):
            // which panels have ceiling quads to draw on, and is that ceiling hidden behind the voxel shapes' faces
            {
                var innerPanels = new HashSet<int>(hs.Chunks.SelectMany(c => c.Panel.Where((p, n) => c.Inside[n])));
                var withRoom = hull.Where(p => def.Rooms.Any(r => r.Enclosed && p.Center.X > r.Min[0] && p.Center.X < r.Max[0] && p.Center.Y > r.Min[1] - 0.5 && p.Center.Y < r.Max[1] + 0.5)).ToList();
                var noCeiling = withRoom.Where(p => !innerPanels.Contains(p.Index)).Select(p => p.Part.ToString()).ToList();
                // a ceiling quad hidden: an element of her interior voxel shape right in front of its middle, inboard
                var interiorBoxes = new List<double[]>();
                string shapeFile = Path.Combine(assets, "shapes", "entity", "ship", def.Code, "interior.json");
                if (File.Exists(shapeFile))
                {
                    void Walk(Newtonsoft.Json.Linq.JToken els)
                    {
                        if (els == null) return;
                        foreach (var e in els)
                        {
                            var f = e["from"]?.ToObject<double[]>(); var t = e["to"]?.ToObject<double[]>();
                            if (f != null && t != null)
                                interiorBoxes.Add(new[] { Math.Min(f[0], t[0]) / 16, Math.Min(f[1], t[1]) / 16, Math.Min(f[2], t[2]) / 16, Math.Max(f[0], t[0]) / 16, Math.Max(f[1], t[1]) / 16, Math.Max(f[2], t[2]) / 16 });
                            Walk(e["children"]);
                        }
                    }
                    Walk(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(shapeFile))["elements"]);
                }
                int ceil = 0, hidden = 0;
                foreach (var c in hs.Chunks)
                    for (int n = 0; n < c.Quads.Length; n++)
                    {
                        if (!c.Inside[n]) continue;
                        var q = file.Groups[c.Group][c.Quads[n]];
                        double x = 0, y = 0, z = 0, nx = 0, ny = 0, nz = 0;
                        for (int k = 0; k < 4; k++) { int o = 1 + 8 * k; x += q[o] / 4; y += q[o + 1] / 4; z += q[o + 2] / 4; nx += q[o + 5]; ny += q[o + 6]; nz += q[o + 7]; }
                        double ln = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (ln < 1e-6) continue;
                        ceil++;
                        // (the interior voxel shape's elements, ship-local in 1/16: one in front of the ceiling hides it)
                        double px = x + nx / ln * 0.03, py = y + ny / ln * 0.03, pz = z + nz / ln * 0.03;
                        if (interiorBoxes.Any(e => px > e[0] && px < e[3] && py > e[1] && py < e[4] && pz > e[2] && pz < e[5])) hidden++;
                    }
                Console.WriteLine($"         inside: {innerPanels.Count} hull panels have ceiling to draw damage on ({withRoom.Count} by a room; none: {noCeiling.Count}" +
                    (noCeiling.Count > 0 ? ": " + string.Join(", ", noCeiling.Take(8)) : "") + $"); {hidden} of {ceil} ceiling quads behind her interior voxel shape's boxes");
                Check(noCeiling.Count <= withRoom.Count / 10 && hidden <= ceil / 20 && interiorBoxes.Count > 0,
                    $"inside her the damage is drawn on her ceiling: {withRoom.Count - noCeiling.Count} of {withRoom.Count} panels by a room have ceiling quads, {ceil - hidden} of {ceil} of them in the open");
            }

            // ---- looks: sound is nothing; each step of the ladder its overlay, outside and in
            var st = new DamageState(l.Count);
            var sound = hs.Looks(st);
            Check(sound.All(b => b == 0) && hs.Affected(null, sound).Count == 0 && hs.Chunks.All(c => !hs.Damaged(c, sound)),
                "a sound ship: every panel looks sound, no chunk needs building");
            var d = l.D;
            var steps = new (string Name, float Hurt, float Wear, int Look, string Out, string In)[]
            {
                // (inside: its own, stronger overlays, 0.3.10; worn seams weep inside, a scrape is only outside)
                ("worn", 0, d.Worn, HullLook.Worn, "dmg-worn", "dmg-damp-in"), ("scraped", d.Scraped, 0, HullLook.Scraped, "dmg-scraped", "dmg-scraped-in"),
                ("cracked", d.Cracked, 0, HullLook.Cracked, "dmg-cracked-a", "dmg-cracked-in"), ("leaking", 0, d.Leaking, HullLook.Leaking, "dmg-leaking", "dmg-wet-in"),
                ("holed", d.Holed, 0, HullLook.Holed, "dmg-holed-a", "dmg-holed-in"),
            };
            var panel = PanelNear(def, 0.5, def.Waterline - 1.5);
            bool ladder = true;
            string ladderText = "";
            foreach (var s in steps)
            {
                byte b = HullLook.Of(l, panel.Index, s.Hurt, s.Wear);
                ladder &= HullLook.Look(b) == s.Look;
                string o = HullLook.Overlay(s.Look, false, 0), i = HullLook.Overlay(s.Look, true, 1);
                ladder &= (o?.StartsWith(s.Out) ?? s.Out == null) && (i?.StartsWith(s.In ?? "?") ?? s.In == null);
                ladderText += $" {s.Name}: {o ?? "-"} / {i ?? "-"};";
            }
            Check(ladder && HullLook.HoleStep(HullLook.Of(l, panel.Index, d.Holed, 0)) == 1 && HullLook.HoleStep(HullLook.Of(l, panel.Index, 1, 0)) == HullLook.HoleSteps,
                $"each state its overlay outside / inside:{ladderText} a hole grows from step 1 to {HullLook.HoleSteps} as it's stove in");

            // ---- one panel cracked: only its own chunks are built again, and only its quads change
            st.Hurt[panel.Index] = d.Cracked + 0.01f;
            var cracked = hs.Looks(st);
            var affected = hs.Affected(sound, cracked);
            var own = hs.Chunks.Where(c => c.Panels.Contains(panel.Index)).Select(c => c.Index).ToHashSet();
            bool onlyItsQuads = true;
            int overlaid = 0;
            foreach (int ci in affected)
            {
                var c = hs.Chunks[ci];
                var built = hs.Build(c, cracked);
                onlyItsQuads &= built.Count == c.Quads.Length;
                for (int n = 0; n < c.Quads.Length && n < built.Count; n++)
                {
                    bool isIt = c.Panel[n] == panel.Index;
                    bool changed = (int)built[n][0] != (int)file.Groups[c.Group][c.Quads[n]][0];
                    if (changed) overlaid++;
                    onlyItsQuads &= changed == isIt && hs.Textures[(int)built[n][0]].Contains("dmg-cracked") == isIt;
                    for (int e = 1; e < 33; e++) onlyItsQuads &= built[n][e] == file.Groups[c.Group][c.Quads[n]][e];
                }
            }
            Check(affected.SetEquals(own) && own.Count >= 2 && onlyItsQuads && overlaid > 10,
                $"one panel cracked: only its own chunks ({own.Count}: outside and the ceiling) are built again, its {overlaid} quads get the cracks, the rest stay as they were");

            // ---- a hole: broken planking, cut out of the planking outside and in, closed by its reveal
            st.Clear();
            st.Hurt[panel.Index] = 0.85f;
            var holed = hs.Looks(st);
            var hole = hs.HoleOf(panel.Index, HullLook.HoleStep(holed[panel.Index]));
            var touched = hs.Affected(sound, holed);
            var revealQuads = hs.Reveal(hole);
            double removedOut = 0, removedIn = 0, leftInside = 0;
            int edgePoints = 0, reveals = 0, revealDepthOk = 0, changedChunks = 0, missed = 0;
            foreach (var c in hs.Chunks)
            {
                var built = hs.Damaged(c, holed) ? hs.Build(c, holed) : hs.SoundQuads(c);
                var soundQ = hs.SoundQuads(c);
                bool differs = built.Count != soundQ.Count || built.Zip(soundQ).Any(p => !p.First.Skip(1).SequenceEqual(p.Second.Skip(1)));
                if (differs) { changedChunks++; if (!touched.Contains(c.Index)) missed++; }
                if (c.Group == "bottomclad") continue;
                // (the panel is below the waterline: its planking outside is the bottom's, the ceiling is the interior's)
                double before = hs.SoundQuads(c).Sum(QuadArea), after = 0;
                foreach (var q in built)
                {
                    if (c.RevealPanels.Contains(panel.Index) && IsReveal(q, revealQuads))
                    {
                        reveals++;
                        double depth = Math.Max(Math.Sqrt(Math.Pow(q[1] - q[25], 2) + Math.Pow(q[2] - q[26], 2) + Math.Pow(q[3] - q[27], 2)),
                                                Math.Sqrt(Math.Pow(q[9] - q[17], 2) + Math.Pow(q[10] - q[18], 2) + Math.Pow(q[11] - q[19], 2)));
                        if (Math.Abs(depth - hole.Depth) < 0.15) revealDepthOk++;      // (0.3.9: her shell as thick as it is there)
                        continue;
                    }
                    after += QuadArea(q);
                    for (int k = 0; k < 4; k++)
                    {
                        double f = InHole(hole, q[1 + 8 * k], q[2 + 8 * k], q[3 + 8 * k]);
                        if (f < -0.02) leftInside++;
                        if (Math.Abs(f) < 0.005) edgePoints++;
                    }
                }
                if (c.Group == "bottom") removedOut += before - after;
                if (c.Group == "interior") removedIn += before - after;
            }
            var outStrakes = hole.Strakes.Where(s => !s.Inside && !s.Over).ToArray();
            var inStrakes = hole.Strakes.Where(s => s.Inside).ToArray();
            Check(missed == 0 && changedChunks >= 2 && touched.Count >= changedChunks,
                $"a hole: every chunk it changes is among those built again ({changedChunks} changed, {touched.Count} built)");
            Check(Math.Abs(removedOut - hole.AreaOut) < 0.2 * hole.AreaOut && Math.Abs(removedIn - hole.AreaIn) < 0.25 * hole.AreaIn && leftInside == 0,
                $"it's cut out of the planking outside ({removedOut:0.000} m² gone, {hole.AreaOut:0.000} between its strakes' ends) and the ceiling " +
                $"({removedIn:0.000}, {hole.AreaIn:0.000}); nothing left inside it ({leftInside} corners in it)");
            Check(edgePoints >= 30 && reveals == revealQuads.Count && reveals >= 4 * outStrakes.Length && revealDepthOk >= reveals * 0.8,
                $"its edges are cut exactly where its strakes are broken off ({edgePoints} points on them) and closed by its reveal ({reveals} quads, {revealDepthOk} the planking's thickness deep)");

            // its shape: whole strakes from seam to seam, each off at its own length, the ends jagged; the ceiling its own way
            {
                bool seams = true, jagged = true, ownLengths = false, ownInside = false, paired = inStrakes.Length >= outStrakes.Length - 1;
                var lengths = new List<double>();
                foreach (var o in outStrakes)
                {
                    // just inside its seams it's open, from end to end of the break; just over the top one and under the
                    // bottom one (a strake not broken) it isn't
                    foreach (double t in new[] { 0.03, 0.5, 0.97 })
                    {
                        double u = (o.LeftAt(t) + o.RightAt(t)) / 2;
                        seams &= o.LeftAt(t) < -0.05 && o.RightAt(t) > 0.05;
                    }
                    double spreadL = o.Left.Max() - o.Left.Min(), spreadR = o.Right.Max() - o.Right.Min();
                    jagged &= o.T.Length >= 4 && spreadL > 0.08 * -o.Left.Average() && spreadR > 0.08 * o.Right.Average();
                    lengths.Add(o.Right.Average() - o.Left.Average());
                    if (o.Pair != null && (o.Pair.Left.Zip(o.Left).Any(p => Math.Abs(p.First - p.Second) > 0.01) || o.Pair.Right.Zip(o.Right).Any(p => Math.Abs(p.First - p.Second) > 0.01)))
                        ownInside = true;
                }
                ownLengths = lengths.Count >= 2 && lengths.Max() - lengths.Min() > 0.02;
                // a point a hair over its top seam and under its bottom one, on the strakes there, at its middle: not in it
                var ordered = outStrakes.OrderBy(o => o.WMid * Math.Sign(o.WHi - o.WLo)).ToArray();
                var top = ordered[^1];
                var bottom = ordered[0];
                double Probe(HoleStrake st, double t)
                {
                    // the strake's planking at t across it, at the hole's middle (along it), nudged across its seam
                    var q = st.Quads.OrderBy(qq => Math.Abs(((qq[1] + qq[9]) / 2 - hole.C[0]) * hole.U[0] + ((qq[2] + qq[10]) / 2 - hole.C[1]) * hole.U[1] + ((qq[3] + qq[11]) / 2 - hole.C[2]) * hole.U[2])).First();
                    double x = 0, y = 0, z = 0;
                    double w0 = (1 - 0.5) * (1 - t), w1 = 0.5 * (1 - t), w2 = 0.5 * t, w3 = (1 - 0.5) * t;
                    x = w0 * q[1] + w1 * q[9] + w2 * q[17] + w3 * q[25];
                    y = w0 * q[2] + w1 * q[10] + w2 * q[18] + w3 * q[26];
                    z = w0 * q[3] + w1 * q[11] + w2 * q[19] + w3 * q[27];
                    return InHole(hole, x, y, z);
                }
                double inTop = Probe(top, 0.95), inBottom = Probe(bottom, 0.05), overTop = Probe(top, 1.06), underBottom = Probe(bottom, -0.06);
                seams &= inTop < 0 && inBottom < 0 && overTop > 0 && underBottom > 0;
                Check(seams && jagged && ownLengths && ownInside && paired,
                    $"it's broken planking: {outStrakes.Length} strakes broken from seam to seam (open just inside its top and bottom seams, not over or under them), " +
                    $"each off at its own length ({string.Join(", ", lengths.Select(v => v.ToString("0.00")))}), its ends jagged; the ceiling broken its own way through {inStrakes.Length} of them");
            }

            // the same every time, and a bigger step holds the smaller
            {
                var again = new HullDamageSkin(file, l).HoleOf(panel.Index, HullLook.HoleStep(holed[panel.Index]));
                bool same = again.Strakes.Length == hole.Strakes.Length && again.Strakes.Zip(hole.Strakes).All(p =>
                    p.First.T.SequenceEqual(p.Second.T) && p.First.Left.SequenceEqual(p.Second.Left) && p.First.Right.SequenceEqual(p.Second.Right));
                bool grows = true;
                string sizes = "";
                for (int step = 1; step <= HullLook.HoleSteps; step++)
                {
                    var a = hs.HoleOf(panel.Index, step);
                    sizes += $" {a.Strakes.Count(x => !x.Inside && !x.Over)} strakes {a.AreaOut:0.00} m²;";
                    if (step == HullLook.HoleSteps) break;
                    var b = hs.HoleOf(panel.Index, step + 1);
                    grows &= b.AreaOut > a.AreaOut && b.AreaIn >= a.AreaIn;
                    foreach (var sa in a.Strakes)
                    {
                        var sb = b.Strakes.FirstOrDefault(x => x.Inside == sa.Inside && x.Quads == sa.Quads);
                        grows &= sb != null && sb.T == sa.T && Enumerable.Range(0, sa.T.Length).All(i => sb.Left[i] <= sa.Left[i] + 1e-9 && sb.Right[i] >= sa.Right[i] - 1e-9);
                    }
                }
                Check(same && grows, $"a panel's hole is the same every time it's built, and grows with its step, each holding the one before ({sizes.Trim()})");
            }

            // ---- (0.3.7) the user's note 32: a panel of the great cabin's side over the main deck (starboard, frame space 9,
            // band 6), holed (hurt 1.0), was drawn as plain planking in the game (the client never worked out her looks:
            // ShipRenderer's DamageLook). Its planking outside (tarred, the exterior group) is its own; holed, the hole is cut
            // out of it and out of the ceiling inside, the tar under the holed overlay round it, its reveal with it.
            {
                int p32 = l.Find(ShipPart.Hull(1, 9, 6).Encode());
                st.Clear();
                if (p32 >= 0) st.Hurt[p32] = 1.0f;
                var looks32 = hs.Looks(st);
                int outerQuads = 0, overlaidTar = 0, reveal32 = 0;
                double cutOut = 0, cutIn = 0;
                var hole32 = p32 >= 0 ? hs.HoleOf(p32, HullLook.HoleStep(looks32[p32])) : default;
                var reveal32Quads = p32 >= 0 ? hs.Reveal(hole32) : new List<float[]>();
                foreach (var c in hs.Chunks)
                {
                    if (p32 < 0 || !c.Panels.Contains(p32) && !c.HolePanels.Contains(p32)) continue;
                    for (int n = 0; n < c.Quads.Length; n++) if (c.Panel[n] == p32 && !c.Inside[n] && c.Group == "exterior") outerQuads++;
                    var built = hs.Build(c, looks32);
                    // (what's cut out of the tarred planking outside, and out of the ceiling inside; the reveal is raw oak)
                    double before = hs.SoundQuads(c).Where(q => c.Group == "interior" || hs.Textures[(int)q[0]].Contains("ships:block/tar")).Sum(QuadArea), after = 0;
                    foreach (var q in built)
                    {
                        string t = hs.Textures[(int)q[0]];
                        if (c.RevealPanels.Contains(p32) && IsReveal(q, reveal32Quads)) { reveal32++; continue; }
                        if (c.Group == "exterior" && t.Contains("ships:block/tar") && t.Contains("dmg-holed")) overlaidTar++;
                        if (c.Group == "interior" || t.Contains("ships:block/tar")) after += QuadArea(q);
                    }
                    if (c.Group == "exterior") cutOut += before - after;
                    if (c.Group == "interior") cutIn += before - after;
                }
                double area32 = hole32.AreaOut, area32In = hole32.AreaIn;
                Check(p32 >= 0 && HullLook.Look(looks32[p32]) == HullLook.Holed && outerQuads >= 4 && overlaidTar >= 4 && reveal32 == reveal32Quads.Count && reveal32 >= 8 &&
                      Math.Abs(cutOut - area32) < 0.25 * area32 && Math.Abs(cutIn - area32In) < 0.3 * area32In,
                    $"note 32's panel (the great cabin's side, starboard, frame space 9, band 6), holed: {outerQuads} quads of tarred planking outside are its own, " +
                    $"{overlaidTar} get the holed overlay on the tar, the hole ({area32:0.000} m²) is cut out of them ({cutOut:0.000}) and out of the ceiling inside " +
                    $"({cutIn:0.000} of {area32In:0.000}), closed by its reveal ({reveal32} quads)");
            }

            // ---- many holes and cracks at once: what differs from the design is always among the chunks built, and it's quick
            st.Clear();
            foreach (var p in hull) if (rnd.NextDouble() < 0.25) st.Hurt[p.Index] = (float)rnd.NextDouble();
            foreach (var p in hull) if (rnd.NextDouble() < 0.2) st.Wear[p.Index] = (float)rnd.NextDouble();
            var many = hs.Looks(st);
            var all = hs.Affected(null, many);
            sw.Restart();
            int wrong = 0, holes = many.Count(b => HullLook.Look(b) == HullLook.Holed), quads = 0;
            foreach (var c in hs.Chunks)
            {
                bool dmg = hs.Damaged(c, many);
                if (dmg != all.Contains(c.Index)) wrong++;
                if (dmg) quads += hs.Build(c, many).Count;
            }
            long manyMs = sw.ElapsedMilliseconds;
            Check(wrong == 0 && holes > 10 && manyMs < 4000,
                $"a quarter of her planking hurt ({holes} holes): the chunks built are exactly those that differ ({all.Count} of {hs.Chunks.Count}, {quads} quads in {manyMs} ms)");
            Check(hs.Textures.Skip(file.Textures.Length).All(t => t.Contains("++0~ships:block/dmg-") && File.Exists(Path.Combine(assets, "textures", "block", t.Substring(t.LastIndexOf('/') + 1) + ".png")) ||
                                                                   t == "game:block/wood/planks/oak1"),
                $"the textures it asks for are the file's with an overlay that's there ({hs.Textures.Count - file.Textures.Length} made)");
        }

        /// <summary>
        /// The water in her hold (3b): its surface level in the world however she's pitched and rolled, inside her hull and
        /// out to her planking, nothing under her bottom; and what Sea is told: under that water the sea isn't cut away.
        /// </summary>
        static void FloodWater(ShipDefinition def)
        {
            Console.WriteLine("the water in her hold: level in the world, inside her hull, and what Sea is told");
            var l = def.DamageParts;
            var o = def.WaterClip.DryOutline;
            double inset = ShipWater.DefaultInset, fx = ShipWater.FloodX(def);
            int surfaces = 0, points = 0, notLevel = 0, outside = 0, edges = 0, offPlanking = 0;
            double shortEnds = 0;
            foreach (double water in new[] { 20.0, 150.0, 500.0, 1000.0 })
            {
                double level = l.FloodLevel(water);
                foreach (var (pitch, roll) in new[] { (0.0, 0.0), (0.05, 0.2), (-0.04, -0.35), (0.1, 0.44) })
                {
                    ShipWater.Plane(pitch, roll, out double along, out double across);
                    var runs = ShipWater.Surface(def, level, along, across, inset);
                    if (runs.Count == 0) continue;
                    surfaces++;
                    ShipFrame.ToWorld(0.7, pitch, roll, fx, level, 0, out _, out double wy0, out _);
                    foreach (var run in runs)
                        foreach (var st in run)
                            for (int k = 0; k <= 8; k++)
                            {
                                double x = st[3 * k], y = st[3 * k + 1], z = st[3 * k + 2];
                                points++;
                                ShipFrame.ToWorld(0.7, pitch, roll, x, y, z, out _, out double wy, out _);
                                if (Math.Abs(wy - wy0) > 1e-6) notLevel++;
                                // within her hull (inside her outside's outline at that height), the edges on her planking
                                if (Math.Abs(z) > o.At(x, y) - 0.05) outside++;
                                if (k == 0 || k == 8)
                                {
                                    edges++;
                                    double w = o.At(x, y);
                                    if (w > 0.5 && Math.Abs(Math.Abs(z) - (w - inset)) > 0.05) offPlanking++;
                                }
                            }
                    // along her: from where her hull's inside starts at that height to where it ends
                    var all = runs.SelectMany(r => r).ToList();
                    double xa = all.Min(s => s[0]), xb = all.Max(s => s[0]);
                    double ha = def.PartMap.HullX0, hb = def.PartMap.HullX1;
                    for (double x = ha; x < hb; x += 0.1)
                        if (ShipWater.InnerHalfWidth(def, x, level + along * (x - fx), inset) > 0.3 && (x < xa - 0.6 || x > xb + 0.6)) shortEnds = Math.Max(shortEnds, 1);
                }
            }
            Check(surfaces == 16 && notLevel == 0 && outside == 0 && offPlanking <= edges / 20 && shortEnds == 0,
                $"the water in her hold (20 to 1000 t, level and pitched and rolled up to 25°): {surfaces} surfaces of {points} points, all level in the world, " +
                $"inside her hull ({outside} out), its edges on her planking ({edges - offPlanking} of {edges}), from end to end of her hold ({(shortEnds == 0 ? "yes" : "no")}; {notLevel} not level)");
            ShipWater.Plane(0, 0, out double a0, out double c0);
            Check(ShipWater.Surface(def, l.HoldBottom - 1.2, a0, c0, inset).Count == 0 && ShipWater.Surface(def, l.FloodLevel(5), a0, c0, inset).Count > 0,
                "no water drawn under her bottom; a little in her hold is drawn");

            // what Sea's told: under the water in her the sea isn't cut away, over it she's dry as before
            double lvl = l.FloodLevel(300);
            ShipFrame.ToWorld(1.1, 0.03, 0.1, fx, lvl, 0, out _, out double fy, out _);
            var clip = ShipWater.Clip(def, 100, 50, 200, 1.1, 0.03, 0.1, 0.02, 1.5f, 50 + fy);
            var dry = ShipWater.Clip(def, 100, 50, 200, 1.1, 0.03, 0.1, 0.02, 1.5f, double.NaN);
            int under = 0, over = 0, dryUnder = 0, samples = 0;
            foreach (double x in new[] { fx - 6, fx - 2, fx, fx + 3, fx + 7 })
                foreach (double z in new[] { -1.5, 0.0, 1.5 })
                    foreach (double dy in new[] { -0.4, 0.4 })
                    {
                        // a point in her hold that far under or over the water in her (world)
                        ShipFrame.ToWorld(1.1, 0.03, 0.1, x, lvl, z, out double wx, out double wy, out double wz);
                        double py = 50 + fy + dy;
                        wx += 100; wz += 200;
                        samples++;
                        if (dy < 0 && !clip.ToSea().InDryHull(wx, py, wz)) under++;
                        if (dy > 0 && clip.ToSea().InDryHull(wx, py, wz)) over++;
                        if (dy < 0 && dry.ToSea().InDryHull(wx, py, wz)) dryUnder++;
                    }
            Check(clip.Flooded && Math.Abs(clip.FloodY - (50 + fy - ShipWater.FloodGap)) < 1e-9 && !dry.Flooded && under == samples / 2 && over == samples / 2 && dryUnder == samples / 2,
                $"Sea is told the water's level in her ({ShipWater.FloodGap} under it as drawn): under it the sea isn't cut away ({under} of {samples / 2}), over it her hold is dry ({over}); dry, all of it's cut ({dryUnder})");
        }

        /// <summary>
        /// Her hull as it's drawn with some panels in each state, for the textured close-ups (python tools/shipgen/texview.py
        /// damage): JSON files in the temp folder, the hull's groups as the client draws them (dotnet run -c Release -- damage-views).
        /// </summary>
        static void DamageViews(ShipDefinition def, string assets)
        {
            var file = LoadSkinFile(def, assets);
            var l = def.DamageParts;
            var d = l.D;
            string dir = Path.Combine(Path.GetTempPath(), "ships-damage-views");
            Directory.CreateDirectory(dir);
            // a row of panels amidships in each state: sound, worn, scraped, cracked, leaking, holed (fresh), holed (stove in)
            var states = new (float Hurt, float Wear)[] { (0, 0), (0, d.Worn + 0.05f), (d.Scraped + 0.1f, 0), (d.Cracked + 0.1f, 0), (0, d.Leaking + 0.05f), (d.Holed, 0), (1, 0) };
            foreach (var (name, y, clad) in new[] { ("topsides", 1.4, false), ("bottom", -0.9, false), ("clad", -0.9, true), ("hold", -2.4, false) })
            {
                var hs = new HullDamageSkin(file, l);
                var st = new DamageState(l.Count);
                for (int k = 0; k < states.Length; k++)
                {
                    var p = PanelNear(def, -4.5 + 1.5 * k, y);
                    st.Hurt[p.Index] = states[k].Hurt;
                    st.Wear[p.Index] = states[k].Wear;
                }
                var looks = hs.Looks(st);
                var groups = new Dictionary<string, List<float[]>>();
                foreach (string g in new[] { "exterior", clad ? "bottomclad" : "bottom", "interior" })
                {
                    var list = hs.StaticQuads(g);
                    foreach (var c in hs.Chunks.Where(c => c.Group == g)) list.AddRange(hs.Damaged(c, looks) ? hs.Build(c, looks) : hs.SoundQuads(c));
                    groups[g] = list;
                }
                string path = Path.Combine(dir, $"{def.Code}-damage-{name}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(new { textures = hs.Textures, groups }));
                Console.WriteLine(path);
                // the holes: where they are, and what the planking lost to them
                Console.WriteLine("  holes: " + string.Join(", ", Enumerable.Range(0, l.Count).Where(i => HullLook.Look(looks[i]) == HullLook.Holed)
                    .Select(i => $"{l.Parts[i].Part} at ({l.Parts[i].Center.X:0.0}, {l.Parts[i].Center.Y:0.0}, {l.Parts[i].Center.Z:0.0})")));
                foreach (int i in Enumerable.Range(0, l.Count).Where(i => HullLook.Look(looks[i]) == HullLook.Holed))
                {
                    var h = hs.HoleOf(i, HullLook.HoleStep(looks[i]));
                    double U(double[] p) => (p[0] - h.C[0]) * h.U[0] + (p[1] - h.C[1]) * h.U[1] + (p[2] - h.C[2]) * h.U[2];
                    double Wd(double[] p) => (p[0] - h.C[0]) * h.W[0] + (p[1] - h.C[1]) * h.W[1] + (p[2] - h.C[2]) * h.W[2];
                    var rv = hs.Reveal(h);
                    var corners = rv.SelectMany(q => Enumerable.Range(0, 4).Select(k => new double[] { q[1 + 8 * k], q[2 + 8 * k], q[3 + 8 * k] })).ToList();
                    Console.WriteLine($"    {l.Parts[i].Part} step {HullLook.HoleStep(looks[i])}: {h.AreaOut:0.00} m² out, {h.AreaIn:0.00} in; reveal {rv.Count} quads, " +
                        $"u {corners.Min(U):0.00}..{corners.Max(U):0.00}, w {corners.Min(Wd):0.00}..{corners.Max(Wd):0.00}");
                    foreach (var s in h.Strakes)
                        Console.WriteLine($"      {(s.Inside ? "in " : "out")} y {s.YLo:0.00}..{s.YHi:0.00} w {s.WLo:0.00}..{s.WHi:0.00} ends {s.Left.Min():0.00}..{s.Left.Max():0.00} / " +
                            $"{s.Right.Min():0.00}..{s.Right.Max():0.00} ({s.T.Length} points, {s.Quads.Count} quads){(s.Over ? ", over the planking" : "")}{(s.Pair != null ? $", paired y {s.Pair.YLo:0.00}..{s.Pair.YHi:0.00}" : "")}");
                }
            }

            // the water in her hold: 250 t, and she's rolled 10° to starboard and down 2° by the head, the water level in the world
            {
                var hs = new HullDamageSkin(file, l);
                double pitch = -0.035, roll = 0.175, level = l.FloodLevel(250);
                ShipWater.Plane(pitch, roll, out double along, out double across);
                var runs = ShipWater.Surface(def, level, along, across, hs.Thickness / 2);
                var textures = new List<string>(hs.Textures) { "game:block/liquid/saltwater" };
                var flood = new List<float[]>();
                foreach (var run in runs)
                    for (int s = 0; s + 1 < run.Count; s++)
                        for (int k = 0; k < 8; k++)
                        {
                            var q = new float[33];
                            q[0] = textures.Count - 1;
                            int n = 0;
                            foreach (var (st, kk) in new[] { (s, k), (s + 1, k), (s + 1, k + 1), (s, k + 1) })
                            {
                                var p = run[st];
                                q[1 + 8 * n] = (float)p[3 * kk]; q[2 + 8 * n] = (float)p[3 * kk + 1]; q[3 + 8 * n] = (float)p[3 * kk + 2];
                                q[4 + 8 * n] = (float)(p[3 * kk] * 8 % 16); q[5 + 8 * n] = (float)(Math.Abs(p[3 * kk + 2]) * 8 % 16);
                                q[7 + 8 * n] = 1;
                                n++;
                            }
                            flood.Add(q);
                        }
                var groups = new Dictionary<string, List<float[]>>();
                foreach (string g in new[] { "exterior", "bottom", "interior" }) groups[g] = hs.StaticQuads(g).Concat(hs.Chunks.Where(c => c.Group == g).SelectMany(hs.SoundQuads)).ToList();
                groups["flood"] = flood;
                string path = Path.Combine(dir, $"{def.Code}-damage-flooded.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(new { textures, groups, tilt = new[] { pitch, roll } }));
                Console.WriteLine(path);
                Console.WriteLine($"  250 t in her: the water {level - l.HoldBottom:0.00} deep in her hold, {flood.Count} quads");
            }
        }
    }
}
