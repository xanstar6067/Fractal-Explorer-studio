cbuffer HyperbolicChamber : register(b1)
{
    float4 HMirror[4];
    float4 HVertex;
    float4 HEdgeCentre;
    float4 HGeometry; // edge half length, displayed ball radius
};

float HDot(float4 a, float4 b) { return dot(a.xyz,b.xyz)-a.w*b.w; }
float HAsinh(float x) { return log(x+sqrt(1+x*x)); }
float HAcosh(float x) { x=max(x,1); return log(x+sqrt(max(x*x-1,0))); }

float4 HBoost(float4 h)
{
    float3 a=ShapeB.xyz;
    float k=2/max(1-dot(a,a),.001);
    float d=dot(a,h.xyz);
    return float4(h.xyz+a*k*(h.w+d), (k-1)*h.w+k*d);
}

// A hyperbolic ball of radius d about p contains a Euclidean ball of this radius.
// Therefore this lower bound cannot jump over tubes shrinking at the ideal boundary.
float HWorldDistance(float d,float radius)
{
    float t=tanh(abs(d)*.5);
    return sign(d)*(1-radius*radius)*t/(1+radius*t);
}

void HDistances(float3 p,out float edge,out float face,out float4 trap)
{
    float radius=length(p);
    float boundary=radius-HGeometry.y;
    int cut=(int)BoxInversion.x;
    if(cut>0) boundary=max(boundary,p[cut-1]-BoxInversion.y);
    float3 ballPoint=p*min(1.0,.9999/max(radius,.0001));
    float denominator=1-dot(ballPoint,ballPoint);
    float4 h=HBoost(float4(2*ballPoint/denominator,(1+dot(ballPoint,ballPoint))/denominator));
    float shell=HAsinh(length(h.xyz));
    int reflections=0;
    // Each outer mirror decreases distance to the central cell; other mirrors
    // implement its finite symmetry group. Render only the physical faces/edges.
    [loop] for(int reflectionPass=0;reflectionPass<96;reflectionPass++)
    {
        bool changed=false;
        [unroll] for(int i=0;i<4;i++)
        {
            float side=HDot(h,HMirror[i]);
            if(side>2e-6) { h-=2*side*HMirror[i]; reflections++; changed=true; }
        }
        if(!changed) break;
    }
    // Remove accumulated single-precision norm drift after large boosts.
    h.w=sqrt(1+dot(h.xyz,h.xyz));
    float s2=HDot(h,HMirror[2]),s3=HDot(h,HMirror[3]);
    float cr=-HDot(HMirror[2],HMirror[3]);
    float lineDistance=HAsinh(sqrt(max((s2*s2+2*cr*s2*s3+s3*s3)/(1-cr*cr),0)));
    float ch=cosh(HGeometry.x),sh=sinh(HGeometry.x);
    float4 tangent=(HVertex-HEdgeCentre*ch)/sh;
    float along=HDot(h,tangent)/max(-HDot(h,HEdgeCentre),1e-6);
    // Clamp the nearest point to the physical half-edge, not its infinite line.
    float edgeDistance=along<0 ? HAcosh(-HDot(h,HEdgeCentre))
        : along>tanh(HGeometry.x) ? HAcosh(-HDot(h,HVertex)) : lineDistance;
    float metricRadius=length(ballPoint);
    edge=max(HWorldDistance(edgeDistance-ShapeA.x,metricRadius),boundary);
    face=max(HWorldDistance(HAsinh(abs(s3))-ShapeA.y*.5,metricRadius),boundary);
    if(BoxInversion.z<.5) edge=10;
    if(BoxInversion.w<.5 || ShapeB.w<=0) face=10;
    trap=float4(shell*.35,abs(s2),reflections,edgeDistance);
}

float HyperbolicMap(float3 p,out float4 trap)
{
    float edge,face;
    HDistances(p,edge,face,trap);
    return min(edge,face);
}
