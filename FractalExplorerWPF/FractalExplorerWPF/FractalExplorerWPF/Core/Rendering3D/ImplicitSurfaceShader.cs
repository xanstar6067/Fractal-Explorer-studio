using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class ImplicitSurfaceShader
{
    internal static string Build(ImplicitSurfaceSettings settings)
    {
        var expression = settings.Validate();
        return Helpers + "\nfloat ImplicitField(float3 p) { float3 q=p*ShapeA.x; return " + expression.Hlsl +
            "-ShapeA.y; }\nfloat2 ImplicitInterval(float3 lo,float3 hi) { float2 ix=float2(lo.x,hi.x),iy=float2(lo.y,hi.y),iz=float2(lo.z,hi.z); return ISub(" +
            expression.IntervalHlsl + ",float2(ShapeA.y,ShapeA.y)); }\n" + Geometry;
    }

    private const string Helpers = """
        float RealPow(float v,int n)
        {
            // The syntax bounds |n| by 16. A fixed multiplication tree avoids the
            // compiler's costly expansion of many nested integer-power loops.
            float r=1; uint k=(uint)abs(n);
            if((k&1u)!=0) r*=v; v*=v;
            if((k&2u)!=0) r*=v; v*=v;
            if((k&4u)!=0) r*=v; v*=v;
            if((k&8u)!=0) r*=v; v*=v;
            if((k&16u)!=0) r*=v;
            return n<0 ? 1/r : r;
        }
        float2 IWide() { return float2(-1e30,1e30); }
        bool IUnbounded(float2 v) { return v.x<=-1e29 || v.y>=1e29 || !all(isfinite(v)); }
        float2 IPad(float2 v)
        {
            if(!all(isfinite(v))) return IWide();
            float pad=max(abs(v.x),abs(v.y))*2e-6+1e-30;
            return float2(v.x-pad,v.y+pad);
        }
        float2 IAdd(float2 a,float2 b) { return IPad(a+b); }
        float2 ISub(float2 a,float2 b) { return IPad(float2(a.x-b.y,a.y-b.x)); }
        float2 IMul(float2 a,float2 b)
        {
            float4 r=float4(a.x*b.x,a.x*b.y,a.y*b.x,a.y*b.y);
            return IPad(float2(min(min(r.x,r.y),min(r.z,r.w)),max(max(r.x,r.y),max(r.z,r.w))));
        }
        float2 IDiv(float2 a,float2 b)
        {
            if(b.x<=0 && b.y>=0) return IWide();
            return IMul(a,float2(1/b.y,1/b.x));
        }
        float2 IPow(float2 a,int n)
        {
            if(n==0) return float2(1,1);
            if(IUnbounded(a)) return IWide();
            int k=abs(n);
            float l=RealPow(a.x,k),r=RealPow(a.y,k);
            float2 result=float2(min(l,r),max(l,r));
            if(k%2==0 && a.x<=0 && a.y>=0) result.x=0;
            return n<0 ? IDiv(float2(1,1),result) : IPad(result);
        }
        float2 ISin(float2 a)
        {
            if(a.y-a.x>=6.2831853 || IUnbounded(a)) return float2(-1,1);
            float2 r=float2(min(sin(a.x),sin(a.y)),max(sin(a.x),sin(a.y)));
            if(ceil((a.x-1.5707963)/6.2831853)<=floor((a.y-1.5707963)/6.2831853)) r.y=1;
            if(ceil((a.x+1.5707963)/6.2831853)<=floor((a.y+1.5707963)/6.2831853)) r.x=-1;
            return IPad(r);
        }
        float2 ICos(float2 a) { return ISin(a+1.57079632679); }
        float2 IAbs(float2 a)
        {
            return float2(a.x<=0 && a.y>=0 ? 0 : min(abs(a.x),abs(a.y)),max(abs(a.x),abs(a.y)));
        }
        float2 ISqrt(float2 a) { return a.x<0 ? IWide() : IPad(sqrt(a)); }
        float2 ILog(float2 a) { return a.x<=0 ? IWide() : IPad(log(a)); }
        float2 IExp(float2 a) { return IPad(exp(a)); }
        float2 IMin(float2 a,float2 b) { return min(a,b); }
        float2 IMax(float2 a,float2 b) { return max(a,b); }
        """;

    private const string Geometry = """
        float3 ImplicitGradient(float3 p,float h)
        {
            return float3(ImplicitField(p+float3(h,0,0))-ImplicitField(p-float3(h,0,0)),
                ImplicitField(p+float3(0,h,0))-ImplicitField(p-float3(0,h,0)),
                ImplicitField(p+float3(0,0,h))-ImplicitField(p-float3(0,0,h)))/(2*h);
        }
        float3 ImplicitNormal(float3 p,float h)
        {
            float3 g=ImplicitGradient(p,max(h,1e-4));
            // At a singular point the normal is undefined; use a finite viewing normal.
            return all(isfinite(g)) && dot(g,g)>1e-20 ? normalize(g) : -CameraForward.xyz;
        }
        float ImplicitMap(float3 p,out float4 trap)
        {
            float f=ImplicitField(p);
            float g=length(ImplicitGradient(p,1e-4));
            float d=isfinite(f) && isfinite(g) ? abs(f)/max(g,1e-12) : 1;
            float3 box=abs(p)-1;
            float boundary=length(max(box,0))+min(max(box.x,max(box.y,box.z)),0);
            int cut=(int)BoxInversion.x;
            if(cut>0) boundary=max(boundary,p[cut-1]-BoxInversion.y);
            trap=float4(length(p),min(abs(p.x),min(abs(p.y),abs(p.z))),0,abs(f));
            return max(d,boundary);
        }
        void ImplicitTrap(float3 p,out float4 trap)
        {
            trap=float4(.5,min(abs(p.x),min(abs(p.y),abs(p.z))),0,abs(ImplicitField(p)));
            if((int)Flags.x!=2) return;
            // H = 1/2 div(normal), in formula coordinates, with a finite singular fallback.
            float h=.002;
            float divergence=(ImplicitNormal(p+float3(h,0,0),h).x-ImplicitNormal(p-float3(h,0,0),h).x
                +ImplicitNormal(p+float3(0,h,0),h).y-ImplicitNormal(p-float3(0,h,0),h).y
                +ImplicitNormal(p+float3(0,0,h),h).z-ImplicitNormal(p-float3(0,0,h),h).z)/(2*h);
            float curvature=divergence/(2*ShapeA.x);
            trap.x=isfinite(curvature) ? .5+atan(curvature)/3.14159265 : .5;
        }
        bool TraceImplicit(float3 origin,float3 direction,float limit,out float distance,out int steps,out float4 trap)
        {
            distance=0; steps=0; trap=0;
            float3 lower=-1,upper=1;
            int cut=(int)BoxInversion.x;
            if(cut==1) upper.x=min(1,BoxInversion.y);
            if(cut==2) upper.y=min(1,BoxInversion.y);
            if(cut==3) upper.z=min(1,BoxInversion.y);
            if(any(upper<lower)) return false;
            float begin=0,end=limit;
            [unroll] for(int axis=0;axis<3;axis++)
            {
                if(abs(direction[axis])<1e-9)
                {
                    if(origin[axis]<lower[axis] || origin[axis]>upper[axis]) return false;
                }
                else
                {
                    float t0=(lower[axis]-origin[axis])/direction[axis],t1=(upper[axis]-origin[axis])/direction[axis];
                    begin=max(begin,min(t0,t1)); end=min(end,max(t0,t1));
                }
            }
            if(begin>end) return false;
            // Binary tree path stored in one integer: avoids a dynamically indexed
            // local stack (D3DCompile register allocation for that stack is very slow).
            uint node=1; int depth=0;
            int budget=min((int)March.x*4,4096);
            float pixel=2*March.y/max(Resolution.y*CameraPosition.w,1);
            // Ordered interval subdivision, not |F|/|gradient| sphere tracing.
            // Intervals retain even-multiplicity zeros and do not step over thin sheets.
            [loop] for(int visit=0;visit<budget;visit++)
            {
                steps=visit+1;
                uint scale=1u<<depth;
                float span=(end-begin)/scale;
                float2 segment=begin+float2(node-scale,node-scale+1)*span;
                float3 p0=(origin+direction*segment.x)*ShapeA.x,p1=(origin+direction*segment.y)*ShapeA.x;
                float2 range=ImplicitInterval(min(p0,p1),max(p0,p1));
                float middle=(segment.x+segment.y)*.5;
                float epsilon=max(pixel*max(middle,.05)*.15,2e-6);
                bool possible=range.x<=0 && range.y>=0;
                if(possible && segment.y-segment.x>epsilon && depth<21)
                { node*=2; depth++; continue; }
                if(possible && !IUnbounded(range))
                {
                    // Poles and invalid domains must not be mistaken for zeros.
                    float selected=middle, residual=abs(ImplicitField(origin+direction*middle));
                    float f0=abs(ImplicitField(origin+direction*segment.x)),f1=abs(ImplicitField(origin+direction*segment.y));
                    if(f0<residual) { residual=f0; selected=segment.x; }
                    if(f1<residual) { residual=f1; selected=segment.y; }
                    float3 p=origin+direction*selected;
                    float slope=length(ImplicitGradient(p,max(epsilon,1e-5)));
                    if(isfinite(residual) && isfinite(slope) && slope>1e-12 && residual<=epsilon*slope*2)
                    {
                        distance=selected; ImplicitTrap(p,trap); return true;
                    }
                }
                // After a discarded node, visit its next sibling, climbing completed
                // right subtrees. This preserves front-to-back order without a stack.
                [loop] while(node>1 && (node&1u)!=0) { node>>=1; depth--; }
                if(node==1) break;
                node++;
            }
            return false;
        }
        """;
}
