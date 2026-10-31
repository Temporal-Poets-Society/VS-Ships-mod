using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sea;

// Float-for-float port of WaterShader.seaDry and seaDryHalfWidth. All input positions
// and uniforms are camera-relative, as WaterRenderer.Prepare uploads them. The source
// fingerprints deliberately fail on a changed algorithm, rather than testing a stale port.
static class SeaDry
{
    public static void VerifySource(string source)
    {
        string s = File.ReadAllText(source);
        Verify(s, "float seaDryHalfWidth(", "bool seaDry(", "a6a2113bb5f9608d173718d4a1c9563b2e4381d0bd2ccacf0f6c2e67e2d10861");
        Verify(s, "bool seaDry(", "\n\";", "9099773849eb38855cb62d70ea054c4af92ae4b0b6c63d66233fe5f26150898e");
        string compiled = (string)typeof(WaterShader).GetField("FragmentDeclarations",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
        Verify(compiled, "float seaDryHalfWidth(", "bool seaDry(", "a6a2113bb5f9608d173718d4a1c9563b2e4381d0bd2ccacf0f6c2e67e2d10861");
        // Unlike the C# source, the compiled GLSL string ends immediately after seaDry.
        Verify(compiled+"\n\";", "bool seaDry(", "\n\";", "9099773849eb38855cb62d70ea054c4af92ae4b0b6c63d66233fe5f26150898e");
    }

    static void Verify(string source, string start, string end, string expected)
    {
        string body = source.Substring(source.IndexOf(start, StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf(end, StringComparison.Ordinal));
        body = Regex.Replace(Regex.Replace(body, @"//[^\n]*", ""), @"\s+", "");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        if (hash != expected) throw new Exception($"Sea shader changed: review port of {start}; SHA256 {hash}");
    }

    public static float Width(in WaterClipBox c, float u, float hl)
    {
        float s = Math.Clamp((u + 1f) * 15.5f, 0f, 31f);
        float t = Math.Clamp((hl - c.DryLevel0) / c.DryLevelStep, 0f, 3f);
        int i = (int)MathF.Floor(s), j = (int)MathF.Floor(t), i2 = Math.Min(i+1,31), j2 = Math.Min(j+1,3);
        float fs = s-i;
        float lo = Mix(c.DryProfile[j*32+i], c.DryProfile[j*32+i2], fs);
        float hi = Mix(c.DryProfile[j2*32+i], c.DryProfile[j2*32+i2], fs);
        return Mix(lo, hi, t-j);
    }

    static float Mix(float a, float b, float f) => a * (1f-f) + b*f;

    public static bool Contains(in WaterClipBox c, double wx, double wy, double wz, double cx, double cy, double cz)
    {
        if (!c.Dry) return false;
        float qx = (float)(wx-cx) - (float)(c.X-cx), qz = (float)(wz-cz) - (float)(c.Z-cz);
        float sn = (float)Math.Sin(c.Yaw), cs = (float)Math.Cos(c.Yaw);
        float fwd = qx*sn+qz*cs, side = qz*sn-qx*cs;
        float dy = (float)(wy-cy) - (float)(c.WaterY-cy);
        if (dy < c.FloodUniform) return false;
        float cp = 1f/MathF.Sqrt(1f+c.SlopeAlong*c.SlopeAlong), sp = c.SlopeAlong*cp;
        float tr = -c.SlopeAcross*cp, cr = 1f/MathF.Sqrt(1f+tr*tr), sr = tr*cr;
        float up = dy*cp-fwd*sp, hl = up*cr+side*sr;
        float along = fwd*cp+dy*sp-c.CentreOffset, across = MathF.Abs(side*cr-up*sr);
        if (hl < -c.DryDepth || hl > c.DryHeight) return false;
        if (c.HalfLength <= 0 || c.HalfLength-c.DryInset <= 0) return false;
        if (c.HasDryProfile) {
            float len = c.HalfLength-c.DryInset;
            return along <= len && along >= c.DryRake*hl-len && across <= Width(c,along/len,hl);
        }
        float s = Math.Clamp((along/c.HalfLength+1f)*7.5f,0,15);
        int i = (int)MathF.Floor(s), j = Math.Min(i+1,15);
        float w = c.Profile == null ? c.HalfWidth : Mix(c.Profile[i],c.Profile[j],s-i);
        return MathF.Abs(along) <= c.HalfLength-c.DryInset && across <= w-c.DryInset;
    }
}
