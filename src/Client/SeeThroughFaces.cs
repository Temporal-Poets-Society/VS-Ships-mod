using Vintagestory.API.Client;

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

        /// <summary>A render pass (a shape element's "renderPass") that's see-through.</summary>
        public static bool Pass(int pass) => (EnumChunkRenderPass)pass is EnumChunkRenderPass.Transparent or EnumChunkRenderPass.BlendNoCull;
    }
}
