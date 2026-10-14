cbuffer Simulation : register(b0) {
    float4 Equation; float4 Grid; float4 Brush; float4 Kinetics; float4 Phase; float4 Extra;
};
StructuredBuffer<float2> Current : register(t0);
RWStructuredBuffer<float2> Next : register(u0);
RWTexture3D<float4> Display : register(u1);
uint Index(uint3 p, uint n) { return (p.z*n+p.y)*n+p.x; }
uint3 Plus(uint3 p, uint n) { return p == n-1 ? (Kinetics.w > .5 ? 0 : n-1) : p+1; }
uint3 Minus(uint3 p, uint n) { return p == 0 ? (Kinetics.w > .5 ? n-1 : 0) : p-1; }
[numthreads(16,4,2)]
void Evolve(uint3 p : SV_DispatchThreadID)
{
    uint n=(uint)Grid.x; if(any(p>=n)) return;
    uint3 m=Minus(p,n), q=Plus(p,n); uint i=Index(p,n); float2 uv=Current[i];
    float2 lap=Current[Index(uint3(m.x,p.y,p.z),n)]+Current[Index(uint3(q.x,p.y,p.z),n)]+
        Current[Index(uint3(p.x,m.y,p.z),n)]+Current[Index(uint3(p.x,q.y,p.z),n)]+
        Current[Index(uint3(p.x,p.y,m.z),n)]+Current[Index(uint3(p.x,p.y,q.z),n)]-6*uv;
    float u=uv.x,v=uv.y,a=Equation.z,b=Equation.w,eps=Kinetics.x,dt=Grid.y;
    float2 diffusion=Equation.xy*Grid.z*lap; float2 reaction;
    int model=(int)Grid.w;
    if(model==0) reaction=float2(u*(1-u)*(u-(v+b)/a)/eps,u-v);
    else if(model==1) {
        float loss=a*v/(u+b);
        Next[i]=float2((u+dt*(diffusion.x+(u+loss*b)/eps))/(1+dt*(u+loss)/eps),
            v+dt*(diffusion.y+u-v));
        return;
    }
    else if(model==2) reaction=float2((u-u*u*u/3-v)/eps,eps*(u+a-b*v));
    else reaction=float2(-b*u*(u-a)*(u-1)-u*v,
        -(eps+Kinetics.y*v/(u+Kinetics.z))*(v+b*u*(u-a-1)));
    Next[i]=uv+dt*(diffusion+reaction);
}
[numthreads(16,4,2)]
void Inject(uint3 p : SV_DispatchThreadID)
{
    uint n=(uint)Grid.x; if(any(p>=n)) return;
    float3 delta=abs((float3(p)+.5)/n-Brush.xyz);
    if(Kinetics.w>.5) delta=min(delta,1-delta);
    // Stimulation changes the activator only; the recovery field retains its history.
    if(dot(delta,delta)<=Brush.w*Brush.w) Next[Index(p,n)].x=Extra.x;
}
float Angle(uint3 p, uint n) {
    float2 c=Current[Index(p,n)]-Phase.xy;
    return atan2(c.y,c.x);
}
float Wrap(float x) { return x-6.28318530718*floor((x+3.14159265359)/6.28318530718); }
float Winding(float a,float b,float c,float d) {
    return abs(Wrap(b-a)+Wrap(c-b)+Wrap(d-c)+Wrap(a-d))>3.14159265359 ? 1 : 0;
}
[numthreads(16,4,2)]
void Publish(uint3 p : SV_DispatchThreadID)
{
    uint n=(uint)Grid.x; if(any(p>=n)) return;
    uint3 q=Plus(p,n); float a=Angle(p,n);
    float ax=Angle(uint3(q.x,p.y,p.z),n), ay=Angle(uint3(p.x,q.y,p.z),n), az=Angle(uint3(p.x,p.y,q.z),n);
    float filament=max(Winding(a,ax,Angle(uint3(q.x,q.y,p.z),n),ay),
        max(Winding(a,ax,Angle(uint3(q.x,p.y,q.z),n),az),Winding(a,ay,Angle(uint3(p.x,q.y,q.z),n),az)));
    // Circular coordinates avoid interpolation across the phase branch cut.
    Display[p]=float4(saturate(Current[Index(p,n)].x*Phase.z+Phase.w),cos(a),sin(a),filament);
}
