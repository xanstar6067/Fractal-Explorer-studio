// q = x + i y + j z + ij w, i² = j² = −1, ij = ji.
// Идемпотентные компоненты q+ = (x+w)+i(y−z), q− = (x−w)+i(y+z).
// Newton(q) распадается на два независимых комплексных Newton(z^n−1).
cbuffer Parameters : register(b0)
{
    float4 Grid;   // size, degree, maxIterations, z plane
    float4 Model;  // extent, w, relaxation, boundaryIterations
    float4 Region; // region, plusRoot, minusRoot, unused
#if NEWTON_CUSTOM
    float4 KnownRoots[32];
#endif
};
RWTexture3D<float4> Output : register(u0);

#if NEWTON_CUSTOM
#include "ComplexFunctions.hlsl"
#endif
#include "@Formula"

float2 Multiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y, a.x*b.y+a.y*b.x); }

#if NEWTON_CUSTOM
float FindRoot(float2 z) {
    float best=1e30,index=-1;
    [loop] for(int k=0;k<(int)Grid.y;k++) {
        float distance=length(z-KnownRoots[k].xy);
        if(distance<best) { best=distance;index=k; }
    }
    return index>=0 && best<=1e-3*(1+length(KnownRoots[(int)index].xy)) ? index : -1;
}
float2 Newton(float2 z) {
    int limit=(int)Grid.z;
    [loop] for(int step=0;step<=limit;step++) {
        float2 value=FormulaValue(z);
        if(any(isnan(value)) || any(isinf(value)))break;
        bool small=dot(value,value)<1e-10;
        if(small) {float root=FindRoot(z);if(root>=0)return float2(root,step);}
        if(step==limit)break;
        float2 derivative=FormulaDerivative(z);
        float denominator=dot(derivative,derivative);
        if(any(isnan(derivative)) || any(isinf(derivative)) || denominator<1e-24 || denominator>1e30 || any(abs(z)>1e6))break;
        float2 correction=Cx_Div(value,derivative);
        if(small && dot(correction,correction)<1e-10)return float2(-1,step);
        z-=Model.z*correction;
        if(any(isnan(z)) || any(isinf(z)))break;
    }
    return float2(-1,limit);
}
#else
// root = −1 means singular derivative, divergence or no convergence in the budget.
float2 Newton(float2 z)
{
    int degree = (int)Grid.y, limit = (int)Grid.z;
    [loop] for (int step = 0; step <= limit; step++)
    {
        float2 derivativePower = float2(1,0);
        [loop] for (int k = 1; k < degree; k++) derivativePower = Multiply(derivativePower,z);
        float2 residual = Multiply(derivativePower,z)-float2(1,0);
        if (dot(residual,residual) < 1e-10)
        {
            float angle = atan2(z.y,z.x);
            int root = ((int)round(angle*degree/6.28318530718)+degree)%degree;
            return float2(root,step);
        }
        float2 derivative = degree*derivativePower;
        float denominator = dot(derivative,derivative);
        if (step == limit || denominator < 1e-24 || denominator > 1e30 || any(abs(z)>1e6)) break;
        float2 correction = Multiply(residual,float2(derivative.x,-derivative.y))/denominator;
        z -= Model.z*correction;
        if (any(isnan(z)) || any(isinf(z))) break;
    }
    return float2(-1,limit);
}
#endif

[numthreads(8,8,1)]
void Compute(uint3 id : SV_DispatchThreadID)
{
    uint n = (uint)Grid.x;
    if (id.x >= n || id.y >= n) return;
    id.z += (uint)Grid.w;
    if (id.z >= n) return;
    float3 p = ((float3(id)+.5)/n*2-1)*Model.x;
    float2 plus = Newton(float2(p.x+Model.y,p.y-p.z));
    float2 minus = Newton(float2(p.x-Model.y,p.y+p.z));
    bool converged = plus.x >= 0 && minus.x >= 0;
    float iterations = max(plus.y,minus.y);
    float density = saturate(.5+(iterations-Model.w)*.12);
    if (Region.x > 1.5) density = converged && plus.x == Region.y && minus.x == Region.z ? 1 : 0;
    else if (Region.x > .5) density = plus.x == Region.y ? 1 : 0;
    // Each root keeps its own missing-value marker, even when the other component failed.
    Output[id] = float4(density,plus.x+1,iterations/Grid.z,minus.x+1);
}

#if NEWTON_CUSTOM
// Numerical verification uses the same generated functions and actual GPU arithmetic.
[numthreads(8,8,1)]
void ProbeExpression(uint3 id : SV_DispatchThreadID) {
    uint n=(uint)Grid.x;
    if(id.x>=n || id.y>=n)return;
    id.z+=(uint)Grid.w;
    if(id.z>=n)return;
    float2 z=((float2(id.xy)+.5)/n*2-1)*Model.x;
    Output[id]=float4(FormulaValue(z),FormulaDerivative(z));
}
#endif
