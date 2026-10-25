using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (0.1.0-dev.7, note 43: "lanterns does not have light") A block mesh's see-through faces: the elements of its shape
    /// vanilla draws in its transparent or blended pass, a lantern's glowing glass. A ship draws a piece's solid faces with
    /// her solid things and these with her glass, blended over what's behind them (the lit candle in a lantern); drawn
    /// solid, they were black panes.
    /// </summary>
    public static class SeeThroughFaces
    {
        /// <summary>
        /// The mesh without its see-through faces, and those faces as a mesh of their own (null: none, the mesh itself is
        /// given back). The faces of a mesh without render passes (made here, not tesselated) stay solid.
        /// </summary>
        public static MeshData Split(MeshData md, out MeshData seeThrough)
        {
            seeThrough = null;
            if (md?.RenderPassesAndExtraBits == null || md.RenderPassCount == 0 || md.VerticesPerFace != 4 || md.IndicesPerFace != 6) return md;
            int faces = md.VerticesCount / 4;
            bool any = false;
            for (int i = 0; i < faces && !any; i++) any = Is(md, i);
            if (!any) return md;
            var solid = md.EmptyClone();
            solid.AddMeshData(md, i => !Is(md, i));
            seeThrough = md.EmptyClone();
            seeThrough.AddMeshData(md, i => Is(md, i));
            return solid;
        }

        /// <summary>A face drawn in vanilla's transparent or blended pass (its low 10 bits; -1, none given, is the solid pass).</summary>
        public static bool Is(MeshData md, int face)
        {
            if (face >= md.RenderPassCount) return false;
            short bits = md.RenderPassesAndExtraBits[face];
            if (bits < 0) return false;
            return Pass(bits & 0x3FF);
        }

        /// <summary>
        /// (0.1.0-dev.9, caravel note 66) The block light a light-giving block stands in, as a vanilla lantern's own block
        /// is lit: its colour (vanilla's hue and saturation, as the ships' point lights take it) at its level's brightness,
        /// full from level 16 (a lantern gives 18 to 22), r g b 0..1; null for none (no light, or level 0).
        /// </summary>
        public static Vec3f OwnLight(byte[] hsv)
        {
            if (hsv == null || hsv.Length < 3 || hsv[2] == 0) return null;
            int rgb = ColorUtil.HsvToRgb(hsv[0] * 4, hsv[1] * 32, 255);
            float k = GameMath.Clamp(hsv[2] / 16f, 0, 1);
            return new Vec3f(((rgb >> 16) & 255) / 255f * k, ((rgb >> 8) & 255) / 255f * k, (rgb & 255) / 255f * k);
        }

        /// <summary>A render pass (a shape element's "renderPass") that's see-through.</summary>
        public static bool Pass(int pass) => (EnumChunkRenderPass)pass is EnumChunkRenderPass.Transparent or EnumChunkRenderPass.BlendNoCull;
    }
}
