using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class Lenia3DMath
{
    internal static float[] Kernel(Lenia3DSettings s)
    {
        s.Validate(); int n = s.Size; var result = new float[n*n*n]; double sum = 0; var b = s.Shells;
        for (int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
        {
            double r = Math.Sqrt(Math.Pow(Math.Min(x,n-x),2)+Math.Pow(Math.Min(y,n-y),2)+Math.Pow(Math.Min(z,n-z),2))/s.Radius;
            if (r >= 1) continue;
            double br = b.Length*r, t = br-Math.Floor(br);
            double v = Math.Pow(4*t*(1-t),4)*b[(int)br];
            result[(z*n+y)*n+x] = (float)v; sum += v;
        }
        if (sum <= 0) throw new ArgumentException("Ядро Lenia слишком узкое для этой сетки. Увеличьте радиус.");
        for(int i=0;i<result.Length;i++) result[i]=(float)(result[i]/sum);
        return result;
    }

    internal static float[] InitialField(Lenia3DSettings s)
    {
        s.Validate(); int n=s.Size; var values=new float[n*n*n]; var random=new Random(s.Seed);
        if(s.SeedShape == Lenia3DSeed.Empty) return values;
        if(s.SeedShape == Lenia3DSeed.RandomCloud)
        {
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                double r=Math.Sqrt(Math.Pow(x-n/2,2)+Math.Pow(y-n/2,2)+Math.Pow(z-n/2,2));
                if(r<s.Radius*.65) values[(z*n+y)*n+x]=(float)(.25+.6*random.NextDouble());
            }
            return values;
        }
        var seed=Lenia3DSeedLibrary.Get(s.SeedShape); double scale=s.Radius/seed.Radius;
        double Sample(int x,int y,int z) => x<0||y<0||z<0||x>=seed.X||y>=seed.Y||z>=seed.Z ? 0 : seed.Cells[(z*seed.Y+y)*seed.X+x]/255.0;
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
        {
            double px=(x-n/2.0)/scale+seed.X/2, py=(y-n/2.0)/scale+seed.Y/2, pz=(z-n/2.0)/scale+seed.Z/2;
            int ix=(int)Math.Floor(px), iy=(int)Math.Floor(py), iz=(int)Math.Floor(pz);
            double fx=px-ix,fy=py-iy,fz=pz-iz,v=0;
            for(int dz=0;dz<2;dz++) for(int dy=0;dy<2;dy++) for(int dx=0;dx<2;dx++)
                v+=Sample(ix+dx,iy+dy,iz+dz)*(dx==0?1-fx:fx)*(dy==0?1-fy:fy)*(dz==0?1-fz:fz);
            if(v>0) v*=1+s.SeedNoise*(2*random.NextDouble()-1);
            values[(z*n+y)*n+x]=(float)Math.Clamp(v,0,1);
        }
        return values;
    }
}
