using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Hansa
{
    /// <summary>
    /// The block atlas doesn't repeat a texture: a face whose uv runs past its texture's 0..size samples the atlas
    /// region next to it (garbage from other textures: notes 70, 74, 83, 84 — the 0.3.9 hull's voxel faces had their v
    /// taken from the height, and with the waterline on G −0.125 a deck's 4-texel edge ran −2..2). Every face of a ship's
    /// shapes and every quad of her mesh is brought inside its texture before it's tesselated: moved by whole textures
    /// where that's enough, else moved as little as fits (the planks' grain repeats, a shift doesn't show), and a face
    /// longer than its texture is squashed onto it.
    /// </summary>
    public static class ShapeUvs
    {
        const float Eps = 0.01f;

        /// <summary>One axis of a face (two of its uv values) brought into 0..size; whether it changed, and whether it was squashed.</summary>
        public static bool FitAxis(ref float a, ref float b, float size, ref bool squashed)
        {
            float lo = Math.Min(a, b), hi = Math.Max(a, b);
            if (lo >= -Eps && hi <= size + Eps) return false;
            float span = hi - lo;
            if (span > size + Eps)
            {
                float s = size / span;
                a = (a - lo) * s;
                b = (b - lo) * s;
                squashed = true;
                return true;
            }
            float k = (float)Math.Floor(lo / size);
            float lo2 = lo - k * size;
            if (lo2 + span > size + Eps) lo2 = size - span;
            float shift = lo2 - lo;
            a += shift;
            b += shift;
            return true;
        }

        /// <summary>A shape face's uv [u1, v1, u2, v2] in texels of a w × h texture.</summary>
        public static bool FitFace(float[] uv, float w, float h, ref bool squashed)
        {
            if (uv == null || uv.Length < 4) return false;
            bool c = FitAxis(ref uv[0], ref uv[2], w, ref squashed);
            c |= FitAxis(ref uv[1], ref uv[3], h, ref squashed);
            return c;
        }

        /// <summary>
        /// A mesh quad [texture, then per corner x y z u v nx ny nz] (u, v in texels of a 16-texel texture): the four
        /// corners moved together.
        /// </summary>
        public static bool FitQuad(float[] q, ref bool squashed)
        {
            if (q == null || q.Length < 33) return false;
            bool changed = false;
            for (int axis = 3; axis <= 4; axis++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 4; k++) { float t = q[1 + k * 8 + axis]; lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
                float a = lo, b = hi;
                if (!FitAxis(ref a, ref b, 16f, ref squashed)) continue;
                float s = hi - lo > 1e-6f ? (b - a) / (hi - lo) : 1f;
                for (int k = 0; k < 4; k++) q[1 + k * 8 + axis] = a + (q[1 + k * 8 + axis] - lo) * s;
                changed = true;
            }
            return changed;
        }

        /// <summary>Every face of a shape (its children too) brought inside its texture; how many faces were moved and squashed.</summary>
        public static (int moved, int squashed) FitShape(Shape shape)
        {
            int moved = 0, sq = 0;
            if (shape?.Elements == null) return (0, 0);
            float w = shape.TextureWidth > 0 ? shape.TextureWidth : 16, h = shape.TextureHeight > 0 ? shape.TextureHeight : 16;
            void Walk(ShapeElement[] els)
            {
                if (els == null) return;
                foreach (var e in els)
                {
                    if (e == null) continue;
                    var seen = new HashSet<ShapeElementFace>();
                    if (e.FacesResolved != null)
                        foreach (var f in e.FacesResolved) Fit(f, seen);
                    if (e.Faces != null)
                        foreach (var f in e.Faces.Values) Fit(f, seen);
                    Walk(e.Children);
                }
            }
            void Fit(ShapeElementFace f, HashSet<ShapeElementFace> seen)
            {
                if (f == null || !seen.Add(f)) return;
                float fw = w, fh = h;
                if (f.Texture != null && shape.TextureSizes != null && shape.TextureSizes.TryGetValue(f.Texture.TrimStart('#'), out var size) && size?.Length >= 2)
                {
                    fw = size[0];
                    fh = size[1];
                }
                bool s = false;
                if (FitFace(f.Uv, fw, fh, ref s)) moved++;
                if (s) sq++;
            }
            Walk(shape.Elements);
            return (moved, sq);
        }

        /// <summary>
        /// The texture codes a shape's faces use that it doesn't define, and the ones it defines whose image isn't in the
        /// game's assets ("base++n~overlay": each part looked for).
        /// </summary>
        public static List<string> MissingTextures(Shape shape, System.Func<AssetLocation, bool> exists)
        {
            var missing = new List<string>();
            if (shape == null) return missing;
            var used = new HashSet<string>();
            void Walk(ShapeElement[] els)
            {
                if (els == null) return;
                foreach (var e in els)
                {
                    if (e == null) continue;
                    if (e.FacesResolved != null) foreach (var f in e.FacesResolved) if (f?.Texture != null) used.Add(f.Texture.TrimStart('#'));
                    if (e.Faces != null) foreach (var f in e.Faces.Values) if (f?.Texture != null) used.Add(f.Texture.TrimStart('#'));
                    Walk(e.Children);
                }
            }
            Walk(shape.Elements);
            foreach (var code in used)
            {
                if (shape.Textures == null || !shape.Textures.TryGetValue(code, out var loc) || loc == null) missing.Add("#" + code + " (not defined)");
                else if (MissingImage(loc.ToString(), exists) is string m) missing.Add("#" + code + " " + m);
            }
            return missing;
        }

        /// <summary>A texture path's image (each part of an atlas composite "base++n~overlay"), or what of it isn't there.</summary>
        public static string MissingImage(string path, System.Func<AssetLocation, bool> exists)
        {
            if (string.IsNullOrEmpty(path) || exists == null) return null;
            foreach (var part0 in path.Split(new[] { "++" }, StringSplitOptions.None))
            {
                string part = part0;
                int t = part.IndexOf('~');
                if (t >= 0) part = part.Substring(t + 1);
                var loc = AssetLocation.Create(part, "game");
                var img = new AssetLocation(loc.Domain, "textures/" + loc.Path + (loc.Path.EndsWith(".png") ? "" : ".png"));
                if (!exists(img)) return path + " (no " + img + ")";
            }
            return null;
        }
    }
}
