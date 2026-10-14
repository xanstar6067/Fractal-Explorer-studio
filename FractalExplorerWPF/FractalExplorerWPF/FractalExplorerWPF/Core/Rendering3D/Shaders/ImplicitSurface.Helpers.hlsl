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
