using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Signature (+++−). Reflections and boosts preserve the future unit hyperboloid.</summary>
internal readonly record struct LorentzVector(double X, double Y, double Z, double W)
{
    public double Dot(LorentzVector b) => X*b.X + Y*b.Y + Z*b.Z - W*b.W;
    public static LorentzVector operator +(LorentzVector a, LorentzVector b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z,a.W+b.W);
    public static LorentzVector operator *(LorentzVector a, double s) => new(a.X*s,a.Y*s,a.Z*s,a.W*s);
    public Vector4 ToSingle() => new((float)X,(float)Y,(float)Z,(float)W);
    public Vector3 Ball => new((float)(X/(W+1)),(float)(Y/(W+1)),(float)(Z/(W+1)));
}

/// <summary>
/// Coxeter orthoscheme [p,q,r], with cell centre V3 at the origin. Its physical cell face
/// is mirror 3, and the physical edge V0–V1 is the intersection of mirrors 2 and 3.
/// Mirrors 0–2 are internal cell symmetries, never rendered as extra cell walls.
/// See Nelson &amp; Segerman, https://arxiv.org/abs/1511.02851.
/// </summary>
internal sealed class HyperbolicHoneycombGeometry
{
    public LorentzVector[] Mirrors { get; }
    public LorentzVector Vertex { get; }
    public LorentzVector EdgeCentre { get; }
    public double EdgeHalfLength { get; }
    public double VertexDistance => Math.Acosh(Vertex.W);

    public HyperbolicHoneycombGeometry(HyperbolicHoneycombKind kind)
    {
        var (p,q,r) = new HyperbolicHoneycombSettings { Tessellation=kind }.Symbol;
        double cp=Math.Cos(Math.PI/p), sp=Math.Sin(Math.PI/p), cq=Math.Cos(Math.PI/q), cr=Math.Cos(Math.PI/r);
        double y=-cq/sp, z=Math.Sqrt(1-y*y), nz=-cr/z, nt=Math.Sqrt(nz*nz-1);
        Mirrors = [new(1,0,0,0),new(-cp,sp,0,0),new(0,y,z,0),new(0,0,nz,nt)];
        // Solve <Vi,nj>=0 for j!=i with time coordinate 1, then normalise.
        double vz=nt/nz, vy=-z*vz/y, vx=sp*vy/cp;
        Vertex=Unit(new(vx,vy,vz,1));
        EdgeCentre=Unit(new(0,vy,vz,1));
        EdgeHalfLength=Math.Acosh(-Vertex.Dot(EdgeCentre));
    }

    private static LorentzVector Unit(LorentzVector v) => v * (1/Math.Sqrt(-v.Dot(v)));

    public double ClipRadius(int depth) => Math.Tanh((VertexDistance + .12 + (depth-1)*.45)/2);

    public Vector4[] Constants(int depth) => [..Mirrors.Select(n=>n.ToSingle()), Vertex.ToSingle(), EdgeCentre.ToSingle(),
        new((float)EdgeHalfLength,(float)ClipRadius(depth),0,0)];

    public static LorentzVector Lift(Vector3 p)
    {
        double d=1-(double)p.X*p.X-(double)p.Y*p.Y-(double)p.Z*p.Z;
        if(d<=0) throw new ArgumentException("Точка должна лежать внутри шара Пуанкаре.");
        return new(2*p.X/d,2*p.Y/d,2*p.Z/d,2/d-1);
    }

    public static LorentzVector Reflect(LorentzVector v,LorentzVector n) => v+n*(-2*v.Dot(n));

    /// <summary>Pure translation taking the origin to a, without a spatial rotation.</summary>
    public static LorentzVector Boost(LorentzVector v,Vector3 a)
    {
        double a2=(double)a.X*a.X+(double)a.Y*a.Y+(double)a.Z*a.Z;
        double gamma=(1+a2)/(1-a2), k=2/(1-a2), dot=a.X*v.X+a.Y*v.Y+a.Z*v.Z;
        double s=k*v.W+2*dot/(1-a2);
        return new(v.X+a.X*s,v.Y+a.Y*s,v.Z+a.Z*s,gamma*v.W+k*dot);
    }

    /// <summary>Move in the current observer's local axes; every step is an isometry.</summary>
    public static Vector3 Move(Vector3 origin,Vector3 direction,double distance)
    {
        if(direction.LengthSquared()==0) return origin;
        Vector3 local=Vector3.Normalize(direction)*(float)Math.Tanh(distance/2);
        Vector3 moved=Boost(Lift(local),origin).Ball;
        // Stay just inside the validation boundary after float rounding in the UI.
        return moved.Length()>.849999f ? Vector3.Normalize(moved)*.849999f : moved;
    }
}
