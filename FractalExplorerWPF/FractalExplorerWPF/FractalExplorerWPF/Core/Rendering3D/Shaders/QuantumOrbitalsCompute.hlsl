cbuffer Parameters : register(b0) {
    float4 Grid;       // side, plane offset, half extent in a₀, display amplitude scale
    float4 OrbitalA;   // n,l,m, basis (0=real,1=complex)
    float4 OrbitalB;
    float4 Weights;    // real coefficient A, complex coefficient B, enabled
};
RWTexture3D<float4> Output : register(u0);

float Factorial(int n) { float v=1; [loop] for(int i=2;i<=n;i++) v*=i; return v; }
float2 Wave(float3 p, float4 state) {
    int n=(int)state.x,l=(int)state.y,m=(int)state.z,am=abs(m);
    float r=length(p),rho=2*r/n;
    int degree=n-l-1;
    float a=1,b=2*l+2-rho;
    [loop] for(int k=2;k<=degree;k++) { float c=((2*k+2*l-rho)*b-(k+2*l)*a)/k; a=b; b=c; }
    float radial=sqrt(pow(2.0/n,3)*Factorial(degree)/(2*n*Factorial(n+l))) *
        exp(-rho*.5)*(l==0 ? 1 : pow(max(rho,1e-20),l))*(degree==0 ? 1 : b);
    float cosine=r>1e-15 ? clamp(p.z/r,-1,1) : 1;
    float legendre=1,root=sqrt(max(0,1-cosine*cosine));
    [loop] for(int i=1;i<=am;i++) legendre*=-(2*i-1)*root;
    if(l>am) {
        float previous=legendre; legendre*=(2*am+1)*cosine;
        [loop] for(int j=am+2;j<=l;j++) {
            float next=((2*j-1)*cosine*legendre-(j+am-1)*previous)/(j-am);
            previous=legendre; legendre=next;
        }
    }
    float amplitude=radial*legendre*sqrt((2*l+1)/(4*3.14159265359)*Factorial(l-am)/Factorial(l+am));
    float phi=dot(p.xy,p.xy)>1e-25 ? atan2(p.y,p.x) : 0;
    if(state.w<.5) return float2(amplitude*(m==0 ? 1 : sqrt(2.0)*(am%2==0 ? 1 : -1)*
        (m>0 ? cos(m*phi) : sin(am*phi))),0);
    if(m<0 && am%2!=0) amplitude=-amplitude;
    return amplitude*float2(cos(m*phi),sin(m*phi));
}
[numthreads(8,8,1)]
void Compute(uint3 id : SV_DispatchThreadID) {
    id.z+=(uint)Grid.y;
    if(any(id>=(uint)Grid.x)) return;
    float3 p=((float3(id)+.5)/Grid.x*2-1)*Grid.z;
    float2 psi=Wave(p,OrbitalA)*Weights.x;
    if(Weights.w>.5) {
        float2 q=Wave(p,OrbitalB);
        psi+=float2(q.x*Weights.y-q.y*Weights.z,q.x*Weights.z+q.y*Weights.y);
    }
    psi/=Grid.w;
    Output[id]=float4(dot(psi,psi),psi,length(p)/Grid.z);
}
