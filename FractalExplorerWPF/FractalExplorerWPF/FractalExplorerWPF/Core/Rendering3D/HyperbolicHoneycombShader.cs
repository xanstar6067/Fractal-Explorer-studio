namespace FractalExplorerWPF.Core.Rendering3D;

internal static class HyperbolicHoneycombShader
{
    internal const string Geometry = """
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
        """;

    internal const string Rendering = """
        float3 HNormal(float3 p,float epsilon,bool isEdge)
        {
            float2 k=float2(1,-1);
            float e,f; float4 trap;
            float3 n=0;
            float3 offsets[4]={k.xyy,k.yyx,k.yxy,k.xxx};
            [unroll] for(int i=0;i<4;i++)
            {
                HDistances(p+offsets[i]*epsilon,e,f,trap);
                n+=offsets[i]*(isEdge ? e : f);
            }
            return normalize(n+float3(1e-12,0,0));
        }

        float3 HShade(float3 p,float3 rd,float distance,float epsilon,float4 trap,bool isEdge,int steps)
        {
            float3 n=HNormal(p,epsilon,isEdge);
            if(dot(n,rd)>0) n=-n;
            float ao=Flags.z>.5 ? lerp(1,Occlusion(p,n,.12),saturate(Surface.a)) : 1;
            float diffuse=saturate(dot(n,Light.xyz));
            float shadow=Flags.y>0 && diffuse>0 ? SoftShadow(p+n*epsilon*4,Light.xyz,epsilon*8,2,Flags.y) : 1;
            float3 albedo=SurfaceAlbedo(n,trap,distance,p,rd,ao,(float)steps/March.x,8);
            float3 ambient=Flags.w*lerp(float3(1,1,1),SkyAt(n)*3,saturate(Style.z));
            float specular=Light.w*pow(saturate(dot(n,normalize(Light.xyz-rd))),32)*shadow;
            float strength=max(Style.y,0);
            int style=(int)Style.x;
            float3 color=albedo*(ambient+diffuse*shadow*LightColor.rgb)*ao+specular*LightColor.rgb;
            if(style==1) color=albedo*(ambient*.8+saturate(dot(n,Light.xyz)*.5+.5)*shadow*LightColor.rgb)*ao*ao;
            if(style==2) color=albedo*(ambient+diffuse*.25)*ao*.6+
                albedo*(SkyAt(reflect(rd,n))*2+LightColor.rgb*pow(saturate(dot(reflect(rd,n),Light.xyz)),96)*4)*strength;
            if(style==3) color+=albedo*strength*1.2;
            if(style==4) color=SamplePalette(trap.x*ShapeC.y+ShapeC.z,0)*max(strength,.1);
            if(style==5)
            {
                float2 sn=float2(dot(n,CameraRight.xyz),dot(n,CameraUp.xyz));
                color=albedo*(saturate(dot(sn,normalize(float2(-.55,.62)))*.6+.55)+
                    saturate(dot(sn,normalize(float2(.7,-.3)))*.5+.5)*.25+Flags.w*.3)*ao;
            }
            if(style==6)
            {
                float levels=max(2,floor(2+strength*2));
                color=albedo*(ambient+floor(diffuse*shadow*levels)/(levels-1)*LightColor.rgb)*ao*
                    smoothstep(.12,.42,saturate(dot(n,-rd)));
            }
            if(style==7) color+=albedo*pow(saturate(dot(-n,Light.xyz))*.6+.4,2)*strength*LightColor.rgb;
            if(style==8) color=lerp(color,MirrorReflection(p,n,rd,epsilon,epsilon,8)*albedo,saturate(strength));
            return lerp(color,SkyAt(rd),saturate(distance/16));
        }

        float4 RenderHyperbolic(float3 ro,float3 rd,float pixelRadius)
        {
            float radius=HGeometry.y;
            float closest=-dot(ro,rd),disc=closest*closest-dot(ro,ro)+radius*radius;
            float3 sky=SkyAt(rd);
            if(disc<0) return Probe.x>.5 ? PackFloat(-1) : float4(LinearToSrgb(sky),1);
            float halfChord=sqrt(disc),start=max(0,closest-halfChord),end=closest+halfChord;
            if(end<=start) return Probe.x>.5 ? PackFloat(-1) : float4(LinearToSrgb(sky),1);
            // Intersect the kept half-space before sampling, so an outside cut cannot
            // masquerade as a hit and the exposed tube/face caps remain visible.
            int cut=(int)BoxInversion.x;
            if(cut>0)
            {
                int axis=cut-1;
                if(abs(rd[axis])<1e-8 && ro[axis]>BoxInversion.y) end=-1;
                else if(abs(rd[axis])>=1e-8)
                {
                    float crossing=(BoxInversion.y-ro[axis])/rd[axis];
                    if(rd[axis]>0) end=min(end,crossing); else start=max(start,crossing);
                }
            }
            float travelled=start,transmission=1;
            float3 accumulated=0;
            bool inFace=false,inEdge=false;
            bool traceEdgeExit=false,traceFaceExit=false;
            [loop] for(int step=0;step<(int)March.x && travelled<=end && transmission>.015;step++)
            {
                float3 p=ro+rd*travelled;
                float edge,face; float4 trap;
                HDistances(p,edge,face,trap);
                float epsilon=max(pixelRadius*max(travelled,.1)*.12,2e-6);
                // Keep signed entry detection (including a rounded step just inside),
                // but a camera initially inside material must find the exit instead.
                if(step==0) { traceEdgeExit=edge<-epsilon; traceFaceExit=face<-epsilon; }
                bool edgeHit=traceEdgeExit ? edge>=-epsilon : edge<=epsilon;
                bool faceHit=traceFaceExit ? face>=-epsilon : face<=epsilon;
                if(Probe.x>.5 && (edgeHit || faceHit)) return PackFloat(travelled);
                if((edgeHit && !inEdge) || (faceHit && !inFace && !edgeHit))
                {
                    bool isEdge=edgeHit;
                    float alpha=isEdge ? 1 : ShapeB.w;
                    if((int)Style.x==4) alpha=isEdge ? .35 : alpha*.5;
                    float3 color=HShade(p,rd,travelled,epsilon,trap,isEdge,step);
                    accumulated+=transmission*alpha*color;
                    transmission*=1-alpha;
                }
                if(edgeHit) traceEdgeExit=false;
                if(faceHit) traceFaceExit=false;
                // Hysteresis avoids painting the same face repeatedly at a grazing ray.
                if(faceHit) inFace=true; else if(face>epsilon*4) inFace=false;
                if(edgeHit) inEdge=true; else if(edge>epsilon*4) inEdge=false;
                float distance=min(abs(edge),abs(face));
                travelled+=max(distance*.85,epsilon*2);
            }
            return Probe.x>.5 ? PackFloat(-1) : float4(LinearToSrgb(accumulated+transmission*sky),1);
        }
        """;
}
