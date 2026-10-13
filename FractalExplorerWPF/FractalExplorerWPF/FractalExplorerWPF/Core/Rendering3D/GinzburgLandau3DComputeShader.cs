using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GinzburgLandau3DComputeShader
{
    public const uint GroupX = 16, GroupY = 4, GroupZ = 2;
    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } =
        new[] { "React", "Diffuse", "Inject", "Publish" }
            .Select(e => new ShaderCacheEntry("ginzburg-landau3d-" + e, Source, e, "cs_5_0")).ToArray();

    public const string Source = """
        cbuffer Simulation : register(b0) {
            float4 Equation; // c1, c3, dt/h², exp(dt)-1 (half reaction)
            float4 Grid;     // side, boundary, exp(dt), brush mode
            float4 Brush;
        };
        StructuredBuffer<float2> Current : register(t0);
        RWStructuredBuffer<float2> Next : register(u0);
        RWTexture3D<float4> Display : register(u1);
        uint Index(uint3 p, uint n) { return (p.z*n+p.y)*n+p.x; }
        uint3 Plus(uint3 p, uint n) { return p == n-1 ? (Grid.y > .5 ? 0 : n-1) : p+1; }
        uint3 Minus(uint3 p, uint n) { return p == 0 ? (Grid.y > .5 ? n-1 : 0) : p-1; }
        float2 Rotate(float2 a, float angle) {
            float s,c; sincos(angle,s,c); return float2(c*a.x-s*a.y,s*a.x+c*a.y);
        }
        [numthreads(16,4,2)]
        void React(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint i=Index(p,n); float2 a=Current[i];
            float denominator=1+dot(a,a)*Equation.w;
            Next[i]=sqrt(Grid.z/denominator)*Rotate(a,-.5*Equation.y*log(denominator));
        }
        [numthreads(16,4,2)]
        void Diffuse(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint3 m=Minus(p,n),q=Plus(p,n); uint i=Index(p,n); float2 a=Current[i];
            float2 lap=Current[Index(uint3(m.x,p.y,p.z),n)]+Current[Index(uint3(q.x,p.y,p.z),n)]+
                Current[Index(uint3(p.x,m.y,p.z),n)]+Current[Index(uint3(p.x,q.y,p.z),n)]+
                Current[Index(uint3(p.x,p.y,m.z),n)]+Current[Index(uint3(p.x,p.y,q.z),n)]-6*a;
            Next[i]=a+Equation.z*float2(lap.x-Equation.x*lap.y,lap.y+Equation.x*lap.x);
        }
        [numthreads(16,4,2)]
        void Inject(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            float3 delta=abs((float3(p)+.5)/n-Brush.xyz);
            if(Grid.y>.5) delta=min(delta,1-delta);
            float r=length(delta)/Brush.w;
            if(r>=1) return;
            float weight=1-smoothstep(0,1,r); uint i=Index(p,n);
            Next[i]=Grid.w>.5 ? Rotate(Next[i],3.14159265359*weight) : Next[i]*(1-weight);
        }
        float Angle(uint3 p,uint n) { float2 a=Current[Index(p,n)]; return atan2(a.y,a.x); }
        float Wrap(float x) { return x-6.28318530718*floor((x+3.14159265359)/6.28318530718); }
        float Winding(uint3 a,uint3 b,uint3 c,uint3 d,uint n) {
            // The phase at a truly zero field is undefined: it cannot certify a vortex.
            if(min(min(dot(Current[Index(a,n)],Current[Index(a,n)]),dot(Current[Index(b,n)],Current[Index(b,n)])),
                min(dot(Current[Index(c,n)],Current[Index(c,n)]),dot(Current[Index(d,n)],Current[Index(d,n)])))<1e-12) return 0;
            float aa=Angle(a,n),bb=Angle(b,n),cc=Angle(c,n),dd=Angle(d,n);
            return abs(Wrap(bb-aa)+Wrap(cc-bb)+Wrap(dd-cc)+Wrap(aa-dd))>3.14159265359 ? 1 : 0;
        }
        [numthreads(16,4,2)]
        void Publish(uint3 p : SV_DispatchThreadID) {
            uint n=(uint)Grid.x; if(any(p>=n)) return;
            uint3 q=Plus(p,n);
            float filament=max(Winding(p,uint3(q.x,p.y,p.z),uint3(q.x,q.y,p.z),uint3(p.x,q.y,p.z),n),
                max(Winding(p,uint3(q.x,p.y,p.z),uint3(q.x,p.y,q.z),uint3(p.x,p.y,q.z),n),
                    Winding(p,uint3(p.x,q.y,p.z),uint3(p.x,q.y,q.z),uint3(p.x,p.y,q.z),n)));
            float2 a=Current[Index(p,n)]; float amplitude=length(a);
            // Interpolate circular phase coordinates, never an angle across its branch cut.
            Display[p]=float4(amplitude,amplitude>1e-6 ? a/amplitude : float2(0,0),filament);
        }
        """;
}
