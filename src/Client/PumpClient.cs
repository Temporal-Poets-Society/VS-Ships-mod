using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    /// <summary>
    /// The water a ship's bilge pump brings up (the user's note 39): while someone works it and it really takes water out
    /// of her hold (the server's PumpedRate, shared by the pumps being worked: none while her hold is dry), water gushes
    /// out of its spout as a connected laminar stream, with a few splashes only where it lands; and vanilla's pouring sound once a
    /// stroke. A dry pump only clanks (the server's pump sound, quieter). Worked out each frame on the main thread
    /// (<see cref="Update"/>, as the renderer draws her) and spawned on vanilla's particle thread.
    /// </summary>
    public class PumpClient : IDisposable, IRenderer
    {
        /// <summary>Pumps further off than this aren't seen gushing.</summary>
        public const double Range = 40;

        readonly ICoreClientAPI capi;
        readonly ShipClientSystem sys;
        bool disposed;

        /// <summary>One spout for the particle thread: where it is and which way the water leaves it, where it lands, how fast she moves.</summary>
        class Jet
        {
            public string Key;
            public double X, Y, Z, LX, LY, LZ;
            public float DX, DY, DZ, VX, VY, VZ, Life;
            public double Rate;
            public double Flow;
            public int Color;
        }

        class Track
        {
            public double LastX = double.NaN, LastY, LastZ;
            public float VX, VY, VZ;
            public double LastPhase = -1;
        }

        volatile Jet[] jets = Array.Empty<Jet>();
        readonly Dictionary<string, Track> tracks = new();
        // used only on the particle thread
        readonly SimpleParticleProperties splash;
        readonly Random rand = new();
        readonly Dictionary<string,(MeshRef Mesh,MeshData Data)> streams=new();
        readonly float[] model=new float[16];
        int streamTexture;
        public double RenderOrder => 0.46;
        public int RenderRange => 9999;

        public PumpClient(ICoreClientAPI capi, ShipClientSystem sys)
        {
            this.capi = capi;
            this.sys = sys;
            splash = new SimpleParticleProperties
            {
                MinPos = new Vec3d(), AddPos = new Vec3d(0.24, 0.02, 0.24),
                MinVelocity = new Vec3f(), AddVelocity = new Vec3f(),
                MinSize = 0.12f, MaxSize = 0.28f, GravityEffect = 1f, LifeLength = 0.22f,
                ParticleModel = EnumParticleModel.Cube, WithTerrainCollision = false,
                SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.6f)
            };
            capi.Event.RegisterAsyncParticleSpawner(OnParticles);
            capi.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"hansa-pump-stream");
        }

        /// <summary>
        /// Every frame (main thread): each pump that's worked and brings water up, near enough: its spout, the water's way
        /// and where it lands, in the world as drawn; how fast it moves (frame to frame: her way, her turning, her
        /// rolling); how much comes out now (the stroke's gush); the pour's sound once a stroke.
        /// </summary>
        public void Update(EntityPlayer ep, ShipRenderer renderer, float dt)
        {
            if (disposed || ep == null) return;
            var list = new List<Jet>();
            var seen = new HashSet<string>();
            var cam = ep.CameraPos;
            foreach (var ship in sys.Ships)
            {
                if (ship.Def == null || ship.GoneDown || ship.PumpedRate <= 0 || string.IsNullOrEmpty(ship.PumpsWorked)) continue;
                Vec3d o = null;
                foreach (var f in ship.Def.Fixtures)
                {
                    if (f.Type != "pump" || f.Pos == null) continue;
                    double flow = PumpJet.FlowAt(f.Code, ship.PumpsWorked, ship.PumpedRate);
                    if (flow <= 0) continue;
                    o ??= renderer.RenderOrigin(ship, ep);
                    PumpJet.Spout(f, out var at, out var dir);
                    float life = (float)PumpJet.Landing(f, out var land);
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, at.X, at.Y, at.Z, out double sx, out double sy, out double sz);
                    sx += o.X; sy += o.Y; sz += o.Z;
                    double cx = sx - cam.X, cy = sy - cam.Y, cz = sz - cam.Z;
                    if (cx * cx + cy * cy + cz * cz > Range * Range) continue;
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, dir.X, dir.Y, dir.Z, out double dx, out double dy, out double dz);
                    ShipFrame.ToWorld(ship.Pos.Yaw, ship.Pitch, ship.Roll, land.X, land.Y, land.Z, out double lx, out double ly, out double lz);
                    string key = ship.EntityId + ":" + f.Code;
                    seen.Add(key);
                    if (!tracks.TryGetValue(key, out var tr)) tracks[key] = tr = new Track();
                    // how the spout moves, as drawn (eased over a few frames)
                    if (!double.IsNaN(tr.LastX) && dt > 1e-4)
                    {
                        float k = (float)(1 - Math.Exp(-dt * 8));
                        tr.VX += ((float)((sx - tr.LastX) / dt) - tr.VX) * k;
                        tr.VY += ((float)((sy - tr.LastY) / dt) - tr.VY) * k;
                        tr.VZ += ((float)((sz - tr.LastZ) / dt) - tr.VZ) * k;
                        // (a jump: a teleport, the seam)
                        if (tr.VX * tr.VX + tr.VY * tr.VY + tr.VZ * tr.VZ > 30 * 30) tr.VX = tr.VY = tr.VZ = 0;
                    }
                    tr.LastX = sx; tr.LastY = sy; tr.LastZ = sz;
                    double stroke = Math.Max(0.2, ship.DamageParts?.D.PumpStroke ?? 1);
                    double secs = capi.World.ElapsedMilliseconds / 1000.0;
                    // the pour, once a stroke, as the brake comes to the end of its swing (where the gush is strongest)
                    double phase = (secs / stroke + 0.75) % 1;
                    if (tr.LastPhase >= 0 && phase < tr.LastPhase)
                        capi.World.PlaySoundAt(new AssetLocation("sounds/effect/water-pour"), sx, sy, sz, null, true, 16, PumpJet.Volume(flow));
                    tr.LastPhase = phase;
                    int color = capi.World.ApplyColorMapOnRgba("climateWaterTint", null, ColorUtil.WhiteArgb, (int)sx, (int)sy, (int)sz);
                    list.Add(new Jet
                    {
                        Key=key,Flow=flow,
                        X = sx, Y = sy, Z = sz, LX = lx + o.X, LY = ly + o.Y, LZ = lz + o.Z,
                        DX = (float)dx, DY = (float)dy, DZ = (float)dz, VX = tr.VX, VY = tr.VY, VZ = tr.VZ, Life = life,
                        Rate = PumpJet.Rate(flow, ship.DamageParts?.D != null ? ShipFlood.PumpRate(ship.DamageParts.D, 1) : 0.2) * PumpJet.Pulse(secs, stroke), Color = (color & 0x00FFFFFF) | (215 << 24)
                    });
                }
            }
            foreach (string key in new List<string>(tracks.Keys)) if (!seen.Contains(key)) tracks.Remove(key);
            jets = list.ToArray();
        }

        /// <summary>A count for this many a second over dt, rounded up or down at random (so a trickle still comes).</summary>
        int Count(double perSecond, float dt)
        {
            double n = perSecond * dt;
            int whole = (int)n;
            return whole + (rand.NextDouble() < n - whole ? 1 : 0);
        }

        /// <summary>The particle thread (vanilla's async spawners, every 33 ms): each spout's water, and its splash on the deck.</summary>
        bool OnParticles(float dt, IAsyncParticleManager manager)
        {
            if (disposed) return false;
            var list = jets;
            if (list.Length == 0) return true;
            foreach (var j in list)
            {
                int n = Count(j.Rate, dt);
                if (n <= 0) continue;
                int m = Count(12, dt);
                if (m <= 0) continue;
                splash.Color = j.Color;
                splash.MinPos.Set(j.LX - 0.12, j.LY + 0.02, j.LZ - 0.12);
                splash.MinVelocity.Set(j.VX - 0.7f, j.VY + 0.4f, j.VZ - 0.7f);
                splash.AddVelocity.Set(1.4f, 0.9f, 1.4f);
                splash.MinQuantity = m;
                splash.AddQuantity = 0;
                manager.Spawn(splash);
            }
            return true;
        }

        public void OnRenderFrame(float dt,EnumRenderStage stage)
        {
            if(disposed) return;
            var live=new HashSet<string>();var cam=capi.World.Player?.Entity?.CameraPos;
            if(cam==null) return;
            foreach(var j in jets)
            {
                live.Add(j.Key);
                var quads=PumpJet.Stream(new Vec3d(j.DX,j.DY,j.DZ),j.Life,j.Flow);
                if(!streams.TryGetValue(j.Key,out var stream))
                {
                    var data=new MeshData(quads.Count*4,quads.Count*6,false,true,true,true);
                    foreach(var q in quads) AddStreamFace(data,q);
                    stream=(capi.Render.UploadMesh(data),data);streams[j.Key]=stream;
                }
                else
                {
                    stream.Data.Clear();foreach(var q in quads) AddStreamFace(stream.Data,q);
                    capi.Render.UpdateMesh(stream.Mesh,stream.Data);
                }
                if(streamTexture==0) streamTexture=capi.Render.GetOrLoadTexture(new AssetLocation("game","block/liquid/waterportion.png"));
                var shader=capi.Render.PreparedStandardShader((int)j.X,(int)j.Y,(int)j.Z);
                try
                {
                    Mat4f.Identity(model);Mat4f.Translate(model,model,(float)(j.X-cam.X),(float)(j.Y-cam.Y),(float)(j.Z-cam.Z));
                    shader.ModelMatrix=model;shader.ViewMatrix=capi.Render.CameraMatrixOriginf;shader.ProjectionMatrix=capi.Render.CurrentProjectionMatrix;
                    shader.Tex2D=streamTexture;capi.Render.GlDisableCullFace();capi.Render.RenderMesh(stream.Mesh);
                }
                finally {shader.Stop();capi.Render.GlEnableCullFace();}
            }
            foreach(var key in new List<string>(streams.Keys)) if(!live.Contains(key)) {streams[key].Mesh.Dispose();streams.Remove(key);}
        }

        static void AddStreamFace(MeshData data,float[] q)
        {
            int first=data.VerticesCount;
            for(int k=0;k<4;k++) data.AddWithFlagsVertex(q[1+8*k],q[2+8*k],q[3+8*k],q[4+8*k]/16,q[5+8*k]/16,-1,VertexFlags.PackNormal(q[6+8*k],q[7+8*k],q[8+8*k]));
            data.AddIndices(first,first+1,first+2,first,first+2,first+3);
        }

        public void Dispose()
        {
            disposed = true;
            jets = Array.Empty<Jet>();
            tracks.Clear();
            foreach(var stream in streams.Values) stream.Mesh.Dispose();streams.Clear();
            capi.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        }
    }
}
