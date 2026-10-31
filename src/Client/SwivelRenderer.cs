using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// (25 October 2026) Her swivel guns (<see cref="SwivelGun"/>), drawn from their own small shape (ships:shapes/block/swivelgun):
    /// the socket in her rail cap, the yoke turned with the gun's train, the barrel with that and its elevation, as it's laid
    /// now (<see cref="SwivelGun.PartMatrices"/>, the laying's own geometry: the drawn muzzle is where its shot leaves).
    /// </summary>
    public partial class ShipRenderer
    {
        MultiTextureMeshRef swivelSocket, swivelYoke, swivelBarrel;
        bool swivelTried;
        readonly float[] swivelS = new float[16], swivelY = new float[16], swivelB = new float[16];

        void LoadSwivel()
        {
            if (swivelTried) return;
            swivelTried = true;
            var loc = new AssetLocation("ships", "shapes/block/swivelgun.json");
            var shape = Shape.TryGet(capi, loc);
            if (shape == null)
            {
                capi.Logger.Error("[ships] The swivel gun's shape {0} isn't there: swivel guns aren't drawn", loc);
                return;
            }
            var tex = new ShapeTextureSource(capi, shape, loc.ToString());
            MultiTextureMeshRef Part(string prefix)
            {
                capi.Tesselator.TesselateShape("ships swivel gun", shape, out MeshData md, tex, null, 0, 0, 0, null, new[] { prefix + "*" });
                if (md == null || md.VerticesCount == 0) return null;
                // (the shape's socket top at (0.5, 0, 0.5) of its block: to the origin)
                md.Translate(-0.5f, 0, -0.5f);
                return capi.Render.UploadMultiTextureMesh(md);
            }
            swivelSocket = Part("socket");
            swivelYoke = Part("yoke");
            swivelBarrel = Part("barrel");
        }

        void DrawSwivels(IRenderAPI rapi, IStandardShaderProgram prog, EntityShip ship)
        {
            bool any = false;
            foreach (var f in ship.Def.Fixtures) if (f.Swivel) { any = true; break; }
            if (!any) return;
            LoadSwivel();
            foreach (var f in ship.Def.Fixtures)
            {
                if (!f.Swivel || ship.GunAbsent(f)) continue;
                SwivelGun.PartMatrices(f, ship.GunLayNow(f), swivelS, swivelY, swivelB);
                DrawSwivelPart(rapi, prog, swivelSocket, swivelS);
                DrawSwivelPart(rapi, prog, swivelYoke, swivelY);
                DrawSwivelPart(rapi, prog, swivelBarrel, swivelB);
            }
            prog.ModelMatrix = model.Values;
        }

        void DrawSwivelPart(IRenderAPI rapi, IStandardShaderProgram prog, MultiTextureMeshRef mesh, float[] at)
        {
            if (mesh == null) return;
            Mat4f.Mul(tmp, model.Values, at);
            prog.ModelMatrix = tmp;
            rapi.RenderMultiTextureMesh(mesh, "tex");
        }

        void DisposeSwivels()
        {
            swivelSocket?.Dispose();
            swivelYoke?.Dispose();
            swivelBarrel?.Dispose();
            swivelSocket = swivelYoke = swivelBarrel = null;
            swivelTried = false;
        }
    }
}
