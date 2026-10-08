using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Local mutations around published 3D species, with finite-horizon survival validation.</summary>
public static class Lenia3DRandomizer
{
    public const int TrialCount = 8;
    public static Lenia3DSettings Candidate(Lenia3DSettings current, bool variation, Random random)
    {
        var basis = variation ? current : Lenia3DSeedLibrary.Settings((Lenia3DSeed)random.Next(5));
        double Jitter(double x, double scale) => x * Math.Exp((random.NextDouble()*2-1)*scale);
        return basis with
        {
            Size = current.Size, Radius = Math.Clamp(Jitter(basis.Radius,.04),2,current.Size/2.0-1),
            GrowthMean = Math.Clamp(basis.GrowthMean+(random.NextDouble()*2-1)*.003,.01,.5),
            GrowthWidth = Math.Clamp(Jitter(basis.GrowthWidth,.08),.002,.15),
            Seed = random.Next(), SeedNoise = variation ? basis.SeedNoise : .01+random.NextDouble()*.04,
            StepsPerFrame = current.StepsPerFrame, CutAxis = current.CutAxis, CutPosition = current.CutPosition,
            Field = variation ? current.Field : null, Live = null
        };
    }

    public static LeniaSearchResult? Search(Direct3DDeviceHost host, Lenia3DSettings current, bool variation,
        IProgress<LeniaSearchProgress>? progress, CancellationToken token, int? seed = null)
    {
        current.Validate(); token.ThrowIfCancellationRequested(); var random = new Random(seed ?? Random.Shared.Next());
        using var simulation = new Lenia3DGpuSimulation(host,current with { Live=null });
        var ranked = new List<(Lenia3DSettings Settings, double Score, double Activity)>();
        for(int i=0;i<TrialCount;i++)
        {
            token.ThrowIfCancellationRequested(); var candidate=Candidate(current,variation,random);
            simulation.Reset(candidate); simulation.Advance(72,token); token.ThrowIfCancellationRequested();
            var early=simulation.ReadCurrent();
            simulation.Advance(72,token); token.ThrowIfCancellationRequested(); var late=simulation.ReadCurrent();
            var metrics=Measure(early.Cells,late.Cells);
            if(metrics.Score>0) ranked.Add((candidate with {Field=late},metrics.Score,metrics.Activity));
            progress?.Report(new(i+1,TrialCount,false));
        }
        foreach(var candidate in ranked.OrderByDescending(c=>c.Score).Take(3))
        {
            progress?.Report(new(TrialCount,TrialCount,true)); token.ThrowIfCancellationRequested();
            simulation.Reset(candidate.Settings); simulation.Advance(144,token); token.ThrowIfCancellationRequested();
            var final=simulation.ReadCurrent(); var metrics=Measure(candidate.Settings.Field!.Cells,final.Cells);
            if(metrics.Score>0) return new(candidate.Settings with {Field=final,Threshold=.2},TrialCount,metrics.Activity);
        }
        return null;
    }

    internal static (double Score,double Activity) Measure(ReadOnlySpan<float> before,ReadOnlySpan<float> after)
    {
        if(before.Length!=after.Length || before.Length==0) return (0,0);
        double mass=0,previous=0,delta=0; int active=0;
        for(int i=0;i<after.Length;i++)
        {
            float a=after[i],b=before[i];
            if(!float.IsFinite(a)||a<0||a>1||!float.IsFinite(b)||b<0||b>1) return (0,0);
            mass+=a; previous+=b; delta+=Math.Abs(a-b); if(a>.1) active++;
        }
        if(mass<4 || previous<4 || active<12 || active>after.Length*.18 || mass/previous is < .45 or > 2.2) return (0,0);
        double activity=delta/Math.Max(mass,previous);
        return (Math.Log(1+active)*(1+Math.Min(activity,1)),activity);
    }
}
