using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using Hansa;
using Sea;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
    // Read-only audit log: caravel #642's launch position in the reported session.
    const double TestX=460545, TestZ=30103;
    static int failures;
    static void Check(bool ok, string message) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {message}"); if (!ok) failures++; }
    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) => {
            string game = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? @"C:\Users\andri\AppData\Roaming\Vintagestory";
            foreach (string dir in new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") }) {
                string file = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(file)) return ctx.LoadFromAssemblyPath(file);
            }
            return null;
        };
        return Run(args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string seaRoot = Path.GetFullPath(args.Length > 1 ? args[1] : @"C:\VS mods\Sea");
        SeaDry.VerifySource(Path.Combine(seaRoot,"src/Water/WaterShader.cs"));
        Console.WriteLine("PASS float port matches the pinned seaDry/seaDryHalfWidth source");
        var def = JsonConvert.DeserializeObject<ShipDefinition>(File.ReadAllText(Path.Combine(root, "assets/ships/config/ships/caravel.json")));
        def.Init();
        var clip = ShipWater.Clip(def, 0, 0, 0, 0, 0, 0, 0, 1.5f, double.NaN);
        string output = Path.Combine(root, "obj/issue30"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "clip.json"), JsonConvert.SerializeObject(clip, Formatting.Indented));
        var hashes = new Dictionary<string,string>();
        foreach (string f in new[] {"assets/ships/config/ships/caravel.json", "assets/ships/config/shipmeshes/caravel.json", "src/Ship/ShipWater.cs"}) hashes[f] = Hash(Path.Combine(root,f));
        foreach (string f in new[] {"src/Water/WaterShader.cs", "src/Water/WaterRenderer.cs", "src/Water/WaveField.cs", "modinfo.json"}) hashes["Sea/"+f] = Hash(Path.Combine(seaRoot,f));
        hashes["Ships.dll"] = Hash(typeof(ShipWater).Assembly.Location);
        hashes["Sea.dll"] = Hash(typeof(WaterClipBox).Assembly.Location);
        File.WriteAllText(Path.Combine(output,"hashes.json"),JsonConvert.SerializeObject(hashes,Formatting.Indented));
        Console.WriteLine($"clip: length {clip.HalfLength}, centre {clip.CentreOffset}, inset {clip.DryInset}, band {-clip.DryDepth}..{clip.DryHeight}, rows {clip.DryLevel0}/{clip.DryLevelStep}");
        var mesh = JObject.Parse(File.ReadAllText(Path.Combine(root, "assets/ships/config/shipmeshes/caravel.json")));
        var shell = new InnerShell(mesh);
        Sections(def,shell,clip,output);
        Sweep(def,shell,output);
        Flood(def,output);
        Storm(def,output);
        Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    static double DryWidth(in WaterClipBox c, double x, double y)
    {
        double len = c.HalfLength-c.DryInset, h = y-.1;
        if (x-c.CentreOffset > len || x-c.CentreOffset < c.DryRake*h-len) return 0;
        return c.DryHalfWidthAt((x-c.CentreOffset)/len,h);
    }

    static void Sections(ShipDefinition def, InnerShell shell, WaterClipBox clip, string output)
    {
        double[] stations=Enumerable.Range(0,32).Select(i=>clip.CentreOffset+(clip.HalfLength-clip.DryInset)*(2.0*i/31-1)).ToArray();
        Check(def.WaterClip.DryRowY0 > -1.875,"bilge uses the constant bottom profile row, permitting a continuous affine coverage check");
        var exact=shell.ExactBilgeCoverage(-2.7,-1.875,stations,p=>DryWidth(clip,p.X,p.Y));
        var exactFloor=shell.ExactBilgeCoverage(-1.875,-1.875,stations,p=>DryWidth(clip,p.X,p.Y));
        Check(exact.Vertices>1000 && exact.Missed==0,$"continuous bilge coverage: {exact.Vertices} polygon vertices/station crossings, {exact.Missed} misses, min clearance {exact.Margin:F5}");
        Check(exactFloor.Vertices>100 && exactFloor.Missed==0,$"continuous hold-floor coverage: {exactFloor.Vertices} vertices/crossings, {exactFloor.Missed} misses, min clearance {exactFloor.Margin:F5}");
        File.WriteAllText(Path.Combine(output,"continuous.json"),JsonConvert.SerializeObject(new {
            bilge=new {exact.Vertices,exact.Missed,exact.Margin,exact.Worst},
            floor=new {exactFloor.Vertices,exactFloor.Missed,exactFloor.Margin,exactFloor.Worst}},Formatting.Indented));
        using var csv = new StreamWriter(Path.Combine(output,"sections.csv"));
        csv.WriteLine("x,y,inner,authoredOuter,dry");
        int n = 0, missed = 0; double minimum = double.PositiveInfinity, floorMin = double.PositiveInfinity;
        string worst = "";
        foreach (double y in new[] {-2.625,-2.375,-2.125,-1.975,-1.875,-1.625,-.875,.125})
        for (int i=0;i<=900;i++) {
            double x = -14.5+i*.025, inner = shell.Width(x,y), dry = DryWidth(clip,x,y);
            csv.WriteLine($"{x},{y},{inner},{def.WaterClip.DryOutline.At(x,y)},{dry}");
            if (inner <= .1) continue;
            n++; double margin = dry-inner;
            if (margin<minimum) { minimum=margin; worst=$"x {x:F3}, y {y:F3}"; }
            if (y == -1.875) floorMin = Math.Min(floorMin,margin);
            if (margin<0) missed++;
        }
        Check(n>6000 && missed==0,$"{n} exact planking sections, {missed} narrower cuts; min clearance {minimum:F5} ({worst}); floor min {floorMin:F5}");
        using var cross = new StreamWriter(Path.Combine(output,"cross-section.csv"));
        cross.WriteLine("y,inner,outer,dry");
        for (int i=0;i<=180;i++) {
            double y=-2.75+i*.02;
            cross.WriteLine($"{y},{shell.Width(-2.5,y)},{def.WaterClip.DryOutline.At(-2.5,y)},{DryWidth(clip,-2.5,y)}");
        }
    }

    static void Sweep(ShipDefinition def, InnerShell shell, string output)
    {
        var points = shell.Samples();
        Check(points.Count>30000,$"{points.Count} actual inner-planking samples, at most 0.15 blocks apart, 0.005 into air");
        int poses=0; long boundary=0, boundaryMiss=0, planes=0, planeMiss=0, cpuMismatch=0;
        double minOffset=double.PositiveInfinity, maxOffset=double.NegativeInfinity;
        var rows = new List<object>();
        foreach (double yaw in new[]{0.0,.37,Math.PI/2})
        foreach (double pd in new[]{-12.0,0,12})
        foreach (double rd in new[]{-37.0,-20,0,20,37})
        foreach (double heave in new[]{-6.0,0,6}) {
            poses++;
            double p=pd*Math.PI/180,r=rd*Math.PI/180,ox=TestX,oy=109+heave,oz=TestZ;
            var c = ShipWater.Clip(def,ox,oy,oz,yaw,p,r,12*Math.PI/180,1.5f,double.NaN);
            ShipFrame.ToWorld(yaw,p,r,-4,-.025,.8,out double cx,out double cy,out double cz);
            cx+=ox;cy+=oy;cz+=oz;
            int bm=0,pm=0;
            foreach (P local in points) {
                ShipFrame.ToWorld(yaw,p,r,local.X,local.Y,local.Z,out double wx,out double wy,out double wz);
                wx+=ox;wy+=oy;wz+=oz;
                bool gpu = SeaDry.Contains(c,wx,wy,wz,cx,cy,cz);
                bool cpu = c.InDryHull(wx,wy,wz);
                boundary++;if (!gpu) bm++;
                if (gpu!=cpu) cpuMismatch++;
            }
            // Actual horizontal sea planes at fixed mean-sea offsets -6..6, against
            // the exact inner planking (ray/triangle), including below the floor.
            double sp=Math.Sin(p),cp=Math.Cos(p),sr=Math.Sin(r),cr=Math.Cos(r);
            for (int si=-24;si<=24;si++) for (int xi=0;xi<=90;xi++) for (int zi=-14;zi<=14;zi++) {
                double sea=si*.25,x=-14.5+xi*.25,z=zi*.25;
                double y=(sea-heave-sp*x+cp*sr*z)/(cp*cr);
                if (y < -2.7 || y > .525 || Math.Abs(z) > shell.Width(x,y)-.01) continue;
                ShipFrame.ToWorld(yaw,p,r,x,y,z,out double wx,out double wy,out double wz);
                wx+=ox;wy+=oy;wz+=oz;
                planes++;if(!SeaDry.Contains(c,wx,wy,wz,cx,cy,cz))pm++;
                minOffset=Math.Min(minOffset,wy-c.WaterY);maxOffset=Math.Max(maxOffset,wy-c.WaterY);
            }
            boundaryMiss+=bm;planeMiss+=pm;
            rows.Add(new {yaw,pitch=pd,roll=rd,heave,boundaryMiss=bm,planeMiss=pm});
        }
        Check(boundaryMiss==0,$"{poses} poses / {boundary} inner-planking tests: {boundaryMiss} float seaDry misses");
        Check(planeMiss==0,$"{planes} interior points on sea planes, -6..6 relative mean sea: {planeMiss} misses; waterline-relative range {minOffset:F3}..{maxOffset:F3}");
        Check(cpuMismatch==0,$"float shader port agrees with Sea.InDryHull at every boundary sample ({cpuMismatch} disagreements)");
        // Negative controls: the harness must detect a genuinely undersized outline
        // and a sea plane below an explicitly supplied flooding level.
        var dry = ShipWater.Clip(def,0,0,0,0,0,0,0,1.5f,double.NaN);
        double edge = shell.Width(-2.5,-1.875)-.01;
        ShipFrame.ToWorld(0,0,0,-2.5,-1.875,edge,out double nx,out double ny,out double nz);
        Check(SeaDry.Contains(dry,nx,ny,nz,0,0,0),"floor side-pocket negative-control point is cut in a dry ship");
        dry.DryProfile = dry.DryProfile.Select(v=>v*.5f).ToArray();
        Check(!SeaDry.Contains(dry,nx,ny,nz,0,0,0),"half-width negative control detects a real side-pocket gap");
        File.WriteAllText(Path.Combine(output,"sweep.json"),JsonConvert.SerializeObject(new {poses,samples=points.Count,boundary,boundaryMiss,planes,planeMiss,cpuMismatch,minOffset,maxOffset,rows},Formatting.Indented));
    }

    static void Flood(ShipDefinition def, string output)
    {
        double level=-1.975, volume=def.DamageParts.AirVolume(level);
        var surface=ShipWater.Surface(def,level,0,0,ShipWater.DefaultInset);
        File.WriteAllText(Path.Combine(output,"flood-surface.json"),JsonConvert.SerializeObject(new {level,volume,runs=surface},Formatting.Indented));
        Console.WriteLine($"Hansa flat water at {level:F3} (floor -1.875): {volume:F4} m3 stored water; drawing threshold 0.5; surface {surface.Count} runs");
        Check(volume>.5 && surface.Count>0,"stored bilge water can draw a flat sheet just below the hold floor");
        var c=ShipWater.Clip(def,0,0,0,0,0,0,0,1.5f,level);
        ShipFrame.ToWorld(0,0,0,-2.5,level-.1,1.0,out double wx,out double wy,out double wz);
        Check(!SeaDry.Contains(c,wx,wy,wz,0,0,0),"Sea deliberately remains visible below an explicitly registered flood plane");
        c.Flooded=false;
        Check(SeaDry.Contains(c,wx,wy,wz,0,0,0),"same point is cut when the hull has no stored floodwater");
    }

    static void Storm(ShipDefinition def, string output)
    {
        // Isolated intake diagnostic: intact closed hull, no pumps or damage, no
        // sinkage/list feedback. Not the unknown live world's seed or stored water.
        var rows=new List<object>();
        def.Waterplane(out double len,out double beam,out double center);
        foreach (double strength in new[]{.3,1.5}) {
            var cfg=new SeaConfig{SeaWindStrength=(float)strength,SeaWindFrom=90};
            var model=new WindModel(new WorldShape(),cfg);
            var sea=model.SeaAt(0,0,100);
            foreach (int heading in new[]{0,90,180,270}) foreach (double speed in new[]{0.0,13.3}) {
                double yaw=Math.PI-heading*Math.PI/180,volume=0,first=-1,peak=0,minH=999,maxH=-999;
                var waves=new WavePlace();waves.Update(new WaveSpectrum(4242,0),0,sea,new Wind(-strength,0));
                using var csv=new StreamWriter(Path.Combine(output,$"storm-{strength}-{heading}-{speed}.csv"));
                csv.WriteLine("seconds,heave,pitch,roll,seaAtOrigin,water,rate");
                for(double t=0;t<240;t+=.25) {
                    double x=TestX+Math.Sin(yaw)*speed*t,z=TestZ+Math.Cos(yaw)*speed*t;
                    var a=ShipMotion.Waves(waves.ToTrains(),1,t,x,z,yaw,speed,len,beam,center,def.Motion,0);
                    var pose=new FloodPose{X=x,Y=a.Heave,Z=z,Yaw=yaw,Pitch=a.Pitch,Roll=a.Roll,Surface=(px,pz)=>waves.HeightAt(px,pz,t)};
                    double rate=ShipFlood.Downflooding(def.DamageParts,pose,_=>false);
                    volume+=rate*.25;peak=Math.Max(peak,rate);if(volume>.5 && first<0)first=t;
                    minH=Math.Min(minH,a.Heave);maxH=Math.Max(maxH,a.Heave);
                    csv.WriteLine($"{t},{a.Heave},{a.Pitch},{a.Roll},{waves.HeightAt(x,z,t)},{volume},{rate}");
                }
                Console.WriteLine($"speed {speed} / from E / strength {strength} / heading {heading}: Hs {sea.Height:F3}, water {volume:F4} in 240s, first draw {first:F2}s, peak {peak:F4}, heave {minH:F3}..{maxH:F3}");
                rows.Add(new {strength,heading,speed,Hs=sea.Height,volume,first,peak,minH,maxH});
                if (strength<.4) Check(volume==0,$"breeze intake control, heading {heading}");
            }
        }
        var crest=new FloodPose {Surface=(x,z)=>def.DeckY+1};
        Check(ShipFlood.Downflooding(def.DamageParts,crest,_=>false)>.5,
            "a crest over the deck admits water with an intact hull and all hatches closed (the authored DeckShare)");
        File.WriteAllText(Path.Combine(output,"intake.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
    }
}
