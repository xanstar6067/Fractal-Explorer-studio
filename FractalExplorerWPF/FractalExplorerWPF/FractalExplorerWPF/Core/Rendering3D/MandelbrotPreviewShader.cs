using FractalExplorerWPF.Infrastructure;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class MandelbrotPreviewShader
{
    internal static ShaderCacheEntry CacheEntry => new("julia-preview-metrics", Source, "Render", "cs_5_0");
    // Coordinates and the orbit use doubles. Transcendental functions only affect colour metrics.
    private const string Source = """
        cbuffer Parameters : register(b0)
        {
            double OriginX; double OriginY;
            double StepX; double StepY;
            double ConstantX; double ConstantY;
            double ThresholdSquared; double StripeFrequency;
            uint Width; uint Height; uint FirstRow; uint RowCount;
            uint Limit; uint IsJulia; uint IsShip; uint Mode;
        };
        struct Metrics { float Iterations; float Smooth; float Trap; float Stripe; float Distance; };
        StructuredBuffer<double> Coordinates : register(t0);
        RWStructuredBuffer<Metrics> Output : register(u0);
        [numthreads(8, 8, 1)]
        void Render(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= Width || id.y >= RowCount) return;
            uint y = FirstRow + id.y;
            precise double x0 = Coordinates[id.x];
            precise double y0 = Coordinates[Width + y];
            precise double cr = IsJulia ? ConstantX : x0;
            precise double ci = IsJulia ? ConstantY : y0;
            precise double zr = IsJulia ? x0 : 0.0;
            precise double zi = IsJulia ? y0 : 0.0;
            precise double d11 = IsJulia ? 1.0 : 0.0, d12 = 0.0;
            precise double d21 = 0.0, d22 = d11;
            float trap = 3.402823466e+38F, stripe = 0.0F;
            uint n = 0;
            // The same cardioid/bulb shortcut as the CPU parameter map.
            if (!IsJulia && !IsShip)
            {
                precise double u = x0 - 0.25, v = y0 * y0, q = u*u + v;
                if (q*(q+u) <= 0.25*v || (x0+1.0)*(x0+1.0)+v <= 0.0625) n = Limit;
            }
            [loop] while (n < Limit && zr*zr + zi*zi <= ThresholdSquared)
            {
                if (Mode == 3) trap = min(trap, min(abs((float)zr), abs((float)zi)));
                if (Mode == 4) stripe += 0.5F + 0.5F * sin((float)StripeFrequency * (zr == 0.0 && zi == 0.0 ? 0.0F : atan2((float)zi, (float)zr)));
                if (Mode == 6)
                {
                    precise double a = 2.0*zr, b = -2.0*zi, c = 2.0*zi, d = 2.0*zr;
                    if (IsShip)
                    {
                        precise double sx = zr > 0.0 ? 1.0 : (zr < 0.0 ? -1.0 : 0.0);
                        precise double sy = zi > 0.0 ? -1.0 : (zi < 0.0 ? 1.0 : 0.0);
                        a = 2.0*abs(zr)*sx; b = 2.0*abs(zi)*sy;
                        c = -2.0*abs(zi)*sx; d = 2.0*abs(zr)*sy;
                    }
                    precise double e11 = a*d11 + b*d21 + (IsJulia ? 0.0 : 1.0);
                    precise double e12 = a*d12 + b*d22;
                    precise double e21 = c*d11 + d*d21;
                    precise double e22 = c*d12 + d*d22 + (IsJulia ? 0.0 : 1.0);
                    d11=e11; d12=e12; d21=e21; d22=e22;
                }
                if (IsShip) { zr=abs(zr); zi=-abs(zi); }
                precise double next = zr*zr - zi*zi + cr;
                zi = 2.0*zr*zi + ci; zr = next;
                n++;
            }
            float r2 = (float)(zr*zr + zi*zi);
            float smooth = (float)n;
            if (n < Limit && r2 > 1.0F)
            {
                float nu = log(max(log(r2)*0.5F, 1e-30F)/log(2.0F))/log(2.0F);
                if (isfinite(nu)) smooth = n + 1.0F - nu;
            }
            float distance = 0.0F;
            if (Mode == 6 && n < Limit && r2 > 1.0F && isfinite(r2))
            {
                float gx = (float)(zr*d11 + zi*d21)/r2;
                float gy = (float)(zr*d12 + zi*d22)/r2;
                float gl = sqrt(gx*gx + gy*gy);
                if (gl > 0.0F && isfinite(gl)) distance = 0.25F*log(r2)/gl;
            }
            Metrics m;
            m.Iterations=n; m.Smooth=smooth;
            m.Trap=trap == 3.402823466e+38F ? 0.0F : trap;
            m.Stripe=n == 0 ? 0.0F : stripe/(float)n; m.Distance=distance;
            Output[id.y*Width + id.x]=m;
        }
        """;
}
