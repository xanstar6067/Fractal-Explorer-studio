// Principal complex branches, matching System.Numerics.Complex away from cuts/poles.
float2 Cx_Mul(float2 a,float2 b) { return float2(a.x*b.x-a.y*b.y,a.x*b.y+a.y*b.x); }
float2 Cx_Div(float2 a,float2 b) {
    // Scale to avoid squaring a large or tiny denominator.
    float scale=max(abs(b.x),abs(b.y));
    if(scale==0) return float2(asfloat(0x7fc00000),asfloat(0x7fc00000));
    float2 q=b/scale;
    return Cx_Mul(a/scale,float2(q.x,-q.y))/dot(q,q);
}
float2 Cx_exp(float2 z) { return exp(z.x)*float2(cos(z.y),sin(z.y)); }
float2 Cx_log(float2 z) { return float2(log(length(z)),atan2(z.y,z.x)); }
float2 Cx_sqrt(float2 z) {
    float r=length(z);
    return float2(sqrt(max(0,(r+z.x)*.5)),(z.y<0?-1:1)*sqrt(max(0,(r-z.x)*.5)));
}
float2 Cx_sin(float2 z) { return float2(sin(z.x)*cosh(z.y),cos(z.x)*sinh(z.y)); }
float2 Cx_cos(float2 z) { return float2(cos(z.x)*cosh(z.y),-sin(z.x)*sinh(z.y)); }
float2 Cx_tan(float2 z) { return Cx_Div(Cx_sin(z),Cx_cos(z)); }
float2 Cx_sinh(float2 z) { return float2(sinh(z.x)*cos(z.y),cosh(z.x)*sin(z.y)); }
float2 Cx_cosh(float2 z) { return float2(cosh(z.x)*cos(z.y),sinh(z.x)*sin(z.y)); }
float2 Cx_tanh(float2 z) { return Cx_Div(Cx_sinh(z),Cx_cosh(z)); }
float2 Cx_asin(float2 z) {
    float2 v=Cx_log(float2(-z.y,z.x)+Cx_sqrt(float2(1,0)-Cx_Mul(z,z)));
    return float2(v.y,-v.x);
}
float2 Cx_acos(float2 z) { return float2(1.5707963267948966,0)-Cx_asin(z); }
float2 Cx_atan(float2 z) {
    float2 iz=float2(-z.y,z.x);
    float2 v=Cx_log(float2(1,0)-iz)-Cx_log(float2(1,0)+iz);
    return float2(-v.y,v.x)*.5;
}
float2 Cx_IntPow(float2 z,int power) {
    float2 value=float2(1,0),factor=z;
    uint n=(uint)abs(power);
    [loop] while(n>0) { if((n&1)!=0)value=Cx_Mul(value,factor);n>>=1;if(n>0)factor=Cx_Mul(factor,factor); }
    return power<0 ? Cx_Div(float2(1,0),value) : value;
}
float2 Cx_Pow(float2 z,float2 power) {
    if(all(power==0))return float2(1,0);
    if(all(z==0))return float2(0,0);
    return Cx_exp(Cx_Mul(power,Cx_log(z)));
}
