cbuffer Simulation : register(b0) {
    float4 Equation; // epsilon, q0², M*dt, conserved mean
    float4 Grid;     // N, 2pi/(N*dx), brush mode, q0*N*dx/sqrt(2)
    float4 Brush;
};
StructuredBuffer<float> Field : register(t0);
StructuredBuffer<float4> Spectrum : register(t1);
RWStructuredBuffer<float> Next : register(u0);
RWTexture3D<float2> Display : register(u1);
RWStructuredBuffer<float4> Work : register(u2);
uint Index(uint3 p,uint n) { return (p.z*n+p.y)*n+p.x; }
bool Retained(uint3 p,uint n) { return all(min(p,n-p)<n/4); }
[numthreads(16,4,2)]
void Prepare(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return;
    uint i=Index(p,n); float u=Field[i]; Work[i]=float4(u,0,u*u*u,0);
}
[numthreads(16,4,2)]
void Evolve(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
    if(i==0) { Work[i]=float4(Equation.w*n*n*n,0,0,0); return; }
    if(!Retained(p,n)) { Work[i]=0; return; }
    float3 k=Grid.y*float3(min(p,n-p)); float k2=dot(k,k), d=Equation.y-k2;
    // Effective time M*dt, lambda without M; the cubic chemical potential has -k².
    float lambda=k2*(Equation.x-d*d), x=Equation.z*lambda, growth=exp(x);
    float weight=abs(x)<.001 ? Equation.z*(1+x*(.5+x*(1.0/6+x/24))) : (growth-1)/lambda;
    Work[i]=float4(growth*Spectrum[i].xy-weight*k2*Spectrum[i].zw,0,0);
}
[numthreads(16,4,2)]
void Filter(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
    Work[i]=i==0 ? float4(Equation.w*n*n*n,0,0,0) : Retained(p,n) ? float4(Spectrum[i].xy,0,0) : 0;
}
[numthreads(16,4,2)]
void Finish(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return; Next[Index(p,n)]=Spectrum[Index(p,n)].x;
}
[numthreads(16,4,2)]
void Inject(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return;
    float3 delta=(float3(p)+.5)/n-Brush.xyz; delta-=floor(delta+.5);
    float radius=length(delta)/Brush.w; if(radius>=1) return;
    float3 c=cos(delta*Grid.w);
    float target=Equation.w+(Grid.z<.5 ? .25*(c.x*c.y+c.y*c.z+c.z*c.x) : 0);
    uint i=Index(p,n); Next[i]=lerp(Next[i],target,1-smoothstep(0,1,radius));
}
[numthreads(16,4,2)]
void Measure(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
    float u=Field[i]-Equation.w; Work[i]=float4(u*u,0,0,0);
}
[numthreads(16,4,2)]
void OrderFilter(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
    float3 k=Grid.y*float3(min(p,n-p));
    // Gaussian average of squared fluctuations; sigma_k = 0.3*q0.
    Work[i]=float4(Spectrum[i].xy*exp(-dot(k,k)/(.18*Equation.y)),0,0);
}
[numthreads(16,4,2)]
void Publish(uint3 p : SV_DispatchThreadID) {
    uint n=(uint)Grid.x; if(any(p>=n)) return; uint i=Index(p,n);
    Display[p]=float2(Field[i],sqrt(max(Spectrum[i].x,0)));
}
