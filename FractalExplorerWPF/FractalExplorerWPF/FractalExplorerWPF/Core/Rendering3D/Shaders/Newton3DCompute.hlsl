// q = x + i y + j z + ij w, i² = j² = −1, ij = ji.
// Идемпотентные компоненты q+ = (x+w)+i(y−z), q− = (x−w)+i(y+z).
// Newton(q) распадается на два независимых комплексных Newton(z^n−1).
cbuffer Parameters : register(b0)
{
    float4 Grid;   // size, degree, maxIterations, z plane
    float4 Model;  // extent, w, relaxation, boundaryIterations
    float4 Region; // region, plusRoot, minusRoot, unused
};
RWTexture3D<float4> Output : register(u0);

float2 Multiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y, a.x*b.y+a.y*b.x); }

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
