using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class GrayScottComputeShader
{
    internal static IReadOnlyList<ShaderCacheEntry> CacheEntries => new[] { "Evolve", "Inject", "Render" }
        .Select(name => new ShaderCacheEntry($"gray-scott-{name}", Source, name, "cs_5_0")).ToArray();

    internal const string Source = """
        cbuffer Parameters : register(b0) { float4 P[8]; }
        StructuredBuffer<float2> Field : register(t0);
        StructuredBuffer<uint> Palette : register(t1);
        RWStructuredBuffer<float2> Target : register(u0);
        RWStructuredBuffer<uint> Pixels : register(u1);
        int Size() { return (int)P[0].x; }
        // All callers sample in [-1,n]. Avoid dynamic integer division for every
        // neighbour: it is particularly expensive on older DirectCompute GPUs.
        int Wrap(int p, int n) { return p<0 ? p+n : p>=n ? p-n : p; }
        int Index(int x, int y) { int n=Size(); return Wrap(y,n)*n + Wrap(x,n); }
        float2 At(int x, int y) { return Field[Index(x,y)]; }

        [numthreads(16,16,1)]
        void Evolve(uint3 id : SV_DispatchThreadID)
        {
            int n=Size(), x=id.x, y=id.y; if(x>=n || y>=n) return;
            int left=x==0 ? n-1 : x-1, right=x==n-1 ? 0 : x+1;
            int row=y*n, above=(y==0 ? n-1 : y-1)*n, below=(y==n-1 ? 0 : y+1)*n;
            precise float2 value=Field[row+x];
            precise float2 cross=Field[row+left]+Field[row+right]+Field[above+x]+Field[below+x];
            precise float2 diagonal=Field[above+left]+Field[above+right]+Field[below+left]+Field[below+right];
            precise float2 laplacian=-value+.2*cross+.05*diagonal;
            precise float reaction=value.x*value.y*value.y;
            precise float u=value.x+(P[0].y*laplacian.x-reaction+P[0].w*(1-value.x))*P[1].y;
            precise float v=value.y+(P[0].z*laplacian.y+reaction-(P[0].w+P[1].x)*value.y)*P[1].y;
            Target[y*n+x]=saturate(float2(u,v));
        }

        [numthreads(16,16,1)]
        void Inject(uint3 id : SV_DispatchThreadID)
        {
            int n=Size(), x=id.x, y=id.y; if(x>=n || y>=n) return;
            int dx=abs(x-(int)P[2].x), dy=abs(y-(int)P[2].y);
            dx=min(dx,n-dx); dy=min(dy,n-dy);
            int r=(int)P[1].z;
            if(dx*dx+dy*dy<=r*r)
            {
                uint hash=(uint)(y*n+x)*747796405u+2891336453u; hash^=hash>>16;
                float noise=((int)(hash&1023u)-512)*(1.0/32768.0);
                Target[y*n+x]=float2(.5+noise,.25-noise);
            }
        }

        float2 Sample(float2 position)
        {
            int2 base=(int2)floor(position); float2 f=frac(position);
            return lerp(lerp(At(base.x,base.y),At(base.x+1,base.y),f.x),
                lerp(At(base.x,base.y+1),At(base.x+1,base.y+1),f.x),f.y);
        }

        [numthreads(16,16,1)]
        void Render(uint3 id : SV_DispatchThreadID)
        {
            int width=(int)P[3].x, height=(int)P[3].y;
            if(id.x>=width || id.y>=height) return;
            float logicalWidth=P[4].z>0 ? height*P[4].z : width;
            float side=min(logicalWidth,(float)height);
            float2 normalized=(float2((id.x+.5)*logicalWidth/width,id.y+.5)
                -.5*(float2(logicalWidth,height)-side))/side;
            uint color=0xff000000;
            if(all(normalized>=0) && all(normalized<=1))
            {
                float2 value=Sample(normalized*Size()-.5);
                int mode=(int)P[3].z;
                float scalar=mode==1 ? value.x : mode==2 ? value.x-value.y : value.y;
                float t=saturate((scalar-P[4].x)/(P[4].y-P[4].x));
                if(P[3].w!=0) t=1-t;
                color=Palette[clamp((int)round(t*1023),0,1023)];
            }
            Pixels[id.y*width+id.x]=color;
        }
        """;
}
