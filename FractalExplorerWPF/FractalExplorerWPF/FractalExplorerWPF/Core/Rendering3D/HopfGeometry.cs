using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Analytic stereographic images of great circles, with a threaded BVH for the GPU.</summary>
public sealed class HopfGeometry
{
    public const float Scale = .65f;
    public required Vector4[] Rings { get; init; }
    public required Vector4[] Nodes { get; init; }

    // Complex coordinates (x+iy,z+iw): H=(2 Re(z1 conj(z2)),2 Im(z1 conj(z2)),|z1|²-|z2|²).
    // Multiplying both coordinates by exp(it) describes exactly one fiber.
    private static (double[] U, double[] V) Frame(HopfPoint point, HopfSettings settings)
    {
        double lon = point.Longitude * Math.PI / 180, lat = point.Latitude * Math.PI / 180;
        double a = Math.Sqrt(Math.Max(0, (1 + Math.Sin(lat)) / 2));
        double b, c;
        if (a < 1e-12) { b = 1; c = 0; }
        else { b = Math.Cos(lat) * Math.Cos(lon) / (2 * a); c = -Math.Cos(lat) * Math.Sin(lon) / (2 * a); }
        double[] u = [a, 0, b, c], v = [0, a, -c, b];
        foreach (var (i, j, degrees) in new (int, int, double)[]
        { (0,1,settings.XY), (0,2,settings.XZ), (0,3,settings.XW), (1,2,settings.YZ), (1,3,settings.YW), (2,3,settings.ZW) })
        {
            double angle = degrees * Math.PI / 180, co = Math.Cos(angle), si = Math.Sin(angle);
            Rotate(u); Rotate(v);
            void Rotate(double[] q) { (q[i], q[j]) = (co * q[i] - si * q[j], si * q[i] + co * q[j]); }
        }
        return (u, v);
    }

    public static Vector4 PointOnS3(HopfPoint point, double phase, HopfSettings settings)
    {
        var (u, v) = Frame(point, settings);
        double c = Math.Cos(phase), s = Math.Sin(phase);
        return new((float)(u[0]*c+v[0]*s), (float)(u[1]*c+v[1]*s), (float)(u[2]*c+v[2]*s), (float)(u[3]*c+v[3]*s));
    }

    public static HopfGeometry Build(HopfSettings settings, CancellationToken token)
    {
        settings.Validate();
        var points = settings.BasePoints();
        var rings = new List<Vector4>();
        var bounds = new List<(Vector3 Lo, Vector3 Hi)>();
        float clip = (float)settings.ClipRadius;
        for (int id = 0; id < points.Count; id++)
        {
            token.ThrowIfCancellationRequested();
            if (settings.OnlySelected && id != settings.SelectedFiber) continue;
            var (u,v) = Frame(points[id], settings);
            // |cross(U.xyz,V.xyz)|² = 1-U.w²-V.w², computed without subtracting nearly equal numbers.
            double nx=u[1]*v[2]-u[2]*v[1], ny=u[2]*v[0]-u[0]*v[2], nz=u[0]*v[1]-u[1]*v[0];
            double d=nx*nx+ny*ny+nz*nz;
            bool line = d < 1e-20;
            Vector3 center = Vector3.Zero, normal;
            float radius = 0;
            if (line)
            {
                var a = new Vector3((float)u[0], (float)u[1], (float)u[2]);
                var b = new Vector3((float)v[0], (float)v[1], (float)v[2]);
                normal = Vector3.Normalize(a.LengthSquared() > b.LengthSquared() ? a : b);
            }
            else
            {
                double root = Math.Sqrt(d);
                normal = new((float)(nx/root),(float)(ny/root),(float)(nz/root));
                center = new((float)(Scale*(u[3]*u[0]+v[3]*v[0])/d),
                    (float)(Scale*(u[3]*u[1]+v[3]*v[1])/d), (float)(Scale*(u[3]*u[2]+v[3]*v[2])/d));
                radius = (float)(Scale/root);
            }
            float thickness = (float)settings.Thickness * (id == settings.SelectedFiber ? 1.6f : 1);
            rings.Add(new(center,radius)); rings.Add(new(normal,thickness));
            rings.Add(new((float)((points[id].Longitude+180)/360), (float)((points[id].Latitude+90)/540),
                id/(float)Math.Max(points.Count-1,1), line ? 1 : 0));
            // For huge circles, subtraction in an AABB can lose precision. The clipping box is conservative.
            Vector3 lo = new(-clip), hi = new(clip);
            if (!line && radius < 100)
            {
                var extent = new Vector3(radius*MathF.Sqrt(Math.Max(0,1-normal.X*normal.X))+thickness,
                    radius*MathF.Sqrt(Math.Max(0,1-normal.Y*normal.Y))+thickness,
                    radius*MathF.Sqrt(Math.Max(0,1-normal.Z*normal.Z))+thickness);
                lo = Vector3.Max(lo, center-extent); hi = Vector3.Min(hi, center+extent);
            }
            bounds.Add((lo,hi));
        }
        // Empty custom selections still have valid, nonzero D3D buffer sizes; the shader uses the count.
        if (bounds.Count == 0) return new() { Rings = new Vector4[3], Nodes = new Vector4[2] };
        int[] indices = Enumerable.Range(0,bounds.Count).ToArray();
        var nodes = new List<Vector4>();
        BuildNode(0,indices.Length);
        return new() { Rings = rings.ToArray(), Nodes = nodes.ToArray() };

        void BuildNode(int start, int count)
        {
            token.ThrowIfCancellationRequested();
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for(int i=start;i<start+count;i++) { lo=Vector3.Min(lo,bounds[indices[i]].Lo); hi=Vector3.Max(hi,bounds[indices[i]].Hi); }
            int node=nodes.Count;
            nodes.Add(default); nodes.Add(new(hi,count==1?indices[start]:-1));
            if(count>1)
            {
                Vector3 size=hi-lo; int axis=size.X>=size.Y&&size.X>=size.Z?0:size.Y>=size.Z?1:2;
                Array.Sort(indices,start,count,Comparer<int>.Create((a,b)=>
                    (bounds[a].Lo[axis]+bounds[a].Hi[axis]).CompareTo(bounds[b].Lo[axis]+bounds[b].Hi[axis])));
                int left=count/2; BuildNode(start,left); BuildNode(start+left,count-left);
            }
            nodes[node]=new(lo,nodes.Count/2);
        }
    }
}
