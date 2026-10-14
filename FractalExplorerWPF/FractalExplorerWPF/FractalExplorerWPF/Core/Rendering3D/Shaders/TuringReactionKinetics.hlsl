// Shared by the 2D and 3D DirectCompute implementations; P[8]/P[9] are reserved for kinetics.
float2 Reaction(float2 uv) {
    float u = uv.x, v = uv.y, uu = u*u, a = P[8].y, b = P[8].z;
    if (P[8].x == 1) return float2(a-(b+1)*u+uu*v,b*u-uu*v);
    if (P[8].x == 2) return float2(a-u+uu*v,b-uu*v);
    return float2(a-u+uu/max(v,1e-6),uu-b*v);
}
float ReactionDisplayValue(float u) { return (u-P[9].z)/(u+P[9].z); }
