using System.Collections.Concurrent;
using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Incidence geometry of the six regular convex polychora, independent of UI and GPU.</summary>
internal sealed class Polytope4DBody
{
    private static readonly ConcurrentDictionary<Polytope4DKind, Polytope4DBody> Cache = new();
    public required Vector4[] Vertices { get; init; }
    public required (int A, int B)[] Edges { get; init; }
    public required int[][] Cells { get; init; }
    public required Vector4[] Normals { get; init; }
    public required int[][] CellEdges { get; init; }
    public int FaceCount => Edges.Length - Vertices.Length + Cells.Length; // Euler on S³.
    public static Polytope4DBody Get(Polytope4DKind kind) => Cache.GetOrAdd(kind, Build);

    private static Polytope4DBody Build(Polytope4DKind kind)
    {
        var v = new List<Vector4>(); var cells = new List<int[]>();
        if (kind == Polytope4DKind.Cell120)
        {
            var dual = Get(Polytope4DKind.Cell600);
            v.AddRange(dual.Cells.Select(c => Vector4.Normalize(c.Aggregate(Vector4.Zero, (a, i) => a + dual.Vertices[i]))));
            for (int i = 0; i < dual.Vertices.Length; i++)
                cells.Add(Enumerable.Range(0, dual.Cells.Length).Where(j => dual.Cells[j].Contains(i)).ToArray());
        }
        else if (kind == Polytope4DKind.Simplex)
        {
            float w = 1 / MathF.Sqrt(5);
            v.AddRange(new[] { new Vector4(1,1,1,-w), new(1,-1,-1,-w), new(-1,1,-1,-w), new(-1,-1,1,-w), new(0,0,0,4*w) }.Select(Vector4.Normalize));
            for (int i = 0; i < 5; i++) cells.Add(Enumerable.Range(0, 5).Where(j => j != i).ToArray());
        }
        else if (kind == Polytope4DKind.Tesseract)
            for (int mask = 0; mask < 16; mask++) v.Add(new(Sign(mask,0)*.5f, Sign(mask,1)*.5f, Sign(mask,2)*.5f, Sign(mask,3)*.5f));
        else if (kind == Polytope4DKind.Orthoplex)
            v.AddRange(Axes());
        else if (kind == Polytope4DKind.Cell24)
        {
            for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++)
            for (int mask = 0; mask < 4; mask++)
            { var q = Vector4.Zero; q[a] = Sign(mask,0) / MathF.Sqrt(2); q[b] = Sign(mask,1) / MathF.Sqrt(2); v.Add(q); }
        }
        else
        {
            v.AddRange(Axes());
            for (int mask = 0; mask < 16; mask++) v.Add(new(Sign(mask,0)*.5f, Sign(mask,1)*.5f, Sign(mask,2)*.5f, Sign(mask,3)*.5f));
            float phi = (1 + MathF.Sqrt(5)) / 2;
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
            for (int c = 0; c < 4; c++) for (int d = 0; d < 4; d++)
            {
                int[] p = [a,b,c,d];
                if (p.Distinct().Count() != 4) continue;
                int inversions = 0;
                for (int i = 0; i < 4; i++) for (int j = i+1; j < 4; j++) if (p[i] > p[j]) inversions++;
                if (inversions % 2 != 0) continue;
                for (int mask = 0; mask < 8; mask++)
                {
                    float[] q = [0, Sign(mask,0)*.5f, Sign(mask,1)*phi*.5f, Sign(mask,2)*.5f/phi];
                    v.Add(new(q[a],q[b],q[c],q[d]));
                }
            }
        }
        float length2 = float.MaxValue;
        for (int i = 0; i < v.Count; i++) for (int j = i+1; j < v.Count; j++)
            length2 = Math.Min(length2, Vector4.DistanceSquared(v[i], v[j]));
        var edges = new List<(int A, int B)>();
        var adjacent = new bool[v.Count,v.Count];
        for (int i = 0; i < v.Count; i++) for (int j = i+1; j < v.Count; j++)
            if (Math.Abs(Vector4.DistanceSquared(v[i],v[j])-length2) < 1e-5)
            { edges.Add((i,j)); adjacent[i,j] = adjacent[j,i] = true; }
        if (kind == Polytope4DKind.Cell600)
        {
            // Every K4 of the edge graph is one tetrahedral boundary cell.
            foreach (var (a,b) in edges)
            for (int c = b+1; c < v.Count; c++) if (adjacent[a,c] && adjacent[b,c])
            for (int d = c+1; d < v.Count; d++) if (adjacent[a,d] && adjacent[b,d] && adjacent[c,d])
                cells.Add([a,b,c,d]);
        }
        else if (cells.Count == 0)
        {
            IEnumerable<Vector4> normals = kind switch
            {
                Polytope4DKind.Tesseract => Axes(),
                Polytope4DKind.Orthoplex => Get(Polytope4DKind.Tesseract).Vertices,
                _ => Axes().Concat(Get(Polytope4DKind.Tesseract).Vertices)
            };
            foreach (var n in normals)
            {
                float support = v.Max(q => Vector4.Dot(q,n));
                cells.Add(Enumerable.Range(0,v.Count).Where(i => support - Vector4.Dot(v[i],n) < 1e-5).ToArray());
            }
        }
        return new()
        {
            Vertices = v.ToArray(), Edges = edges.ToArray(), Cells = cells.ToArray(),
            Normals = cells.Select(c => Vector4.Normalize(c.Aggregate(Vector4.Zero,(a,i) => a+v[i]))).ToArray(),
            CellEdges = cells.Select(c => Enumerable.Range(0,edges.Count).Where(i => c.Contains(edges[i].A) && c.Contains(edges[i].B)).ToArray()).ToArray()
        };
    }
    private static float Sign(int mask, int bit) => (mask & (1 << bit)) == 0 ? -1 : 1;
    private static IEnumerable<Vector4> Axes()
    {
        for (int axis = 0; axis < 4; axis++) for (int sign = -1; sign <= 1; sign += 2)
        { var v = Vector4.Zero; v[axis] = sign; yield return v; }
    }
}

internal sealed class Polytope4DGeometry
{
    public required Vector4[] Primitives { get; init; } // four float4s: A/r, B/kind, C/color, metadata
    public required Vector4[] Nodes { get; init; }
    public required Vector3[] SectionVertices { get; init; }
    public required int[][] SectionFaces { get; init; }
    public required (int A, int B)[] SectionEdges { get; init; }
    public required double MinW { get; init; }
    public required double MaxW { get; init; }
    public double SurfaceArea => SectionFaces.Sum(f => Enumerable.Range(1,f.Length-2).Sum(j =>
        Vector3.Cross(SectionVertices[f[j]]-SectionVertices[f[0]],SectionVertices[f[j+1]]-SectionVertices[f[0]]).Length()*.5));
    public double Volume => Math.Abs(SectionFaces.Sum(f => Enumerable.Range(1,f.Length-2).Sum(j =>
        Vector3.Dot(SectionVertices[f[0]],Vector3.Cross(SectionVertices[f[j]],SectionVertices[f[j+1]]))/6.0)));

    public static Vector4 Rotate(Vector4 q, Polytope4DSettings s)
    {
        foreach (var (i,j,degrees) in new (int,int,double)[] { (0,1,s.XY),(0,2,s.XZ),(0,3,s.XW),(1,2,s.YZ),(1,3,s.YW),(2,3,s.ZW) })
        {
            double angle = degrees * Math.PI / 180, c = Math.Cos(angle), sn = Math.Sin(angle);
            (q[i],q[j]) = ((float)(c*q[i]-sn*q[j]), (float)(sn*q[i]+c*q[j]));
        }
        return q;
    }

    public static Polytope4DGeometry Build(Polytope4DSettings s, CancellationToken token)
    {
        s.Validate(); token.ThrowIfCancellationRequested();
        var body = Polytope4DBody.Get(s.Body);
        var rotated = body.Vertices.Select(v => Rotate(v,s)).ToArray();
        var vertices = new List<Vector3>(); var faces = new List<int[]>(); var faceCells = new List<int>();
        var sectionEdges = new HashSet<(int A,int B)>(); var seenFaces = new HashSet<string>();
        int Vertex(Vector3 p)
        {
            int found = vertices.FindIndex(q => Vector3.DistanceSquared(p,q) < 1e-10f);
            if (found >= 0) return found;
            vertices.Add(p); return vertices.Count-1;
        }
        var edgeCuts = new int[body.Edges.Length][];
        const float eps = 1e-6f;
        for (int i = 0; i < body.Edges.Length; i++)
        {
            var (a,b) = body.Edges[i]; var p = rotated[a]; var q = rotated[b];
            float da = p.W-(float)s.Offset, db = q.W-(float)s.Offset;
            var cuts = new List<int>();
            if (Math.Abs(da) <= eps) cuts.Add(Vertex(Xyz(p)));
            if (Math.Abs(db) <= eps) cuts.Add(Vertex(Xyz(q)));
            if (da < -eps && db > eps || db < -eps && da > eps)
                cuts.Add(Vertex(Xyz(Vector4.Lerp(p,q,da/(da-db)))));
            edgeCuts[i] = cuts.Distinct().ToArray();
        }
        for (int cell = 0; cell < body.Cells.Length; cell++)
        {
            token.ThrowIfCancellationRequested();
            int[] ids = body.CellEdges[cell].SelectMany(i => edgeCuts[i]).Distinct().ToArray();
            if (ids.Length < 2) continue;
            if (ids.Length == 2) { Edge(ids[0],ids[1]); continue; }
            Vector3 n = Xyz(Rotate(body.Normals[cell],s));
            // A whole 3D cell in the hyperplane is handled by its neighbouring cells' faces.
            if (n.LengthSquared() < 1e-10) continue;
            n = Vector3.Normalize(n);
            Vector3 center = ids.Aggregate(Vector3.Zero,(a,i) => a+vertices[i])/ids.Length;
            Vector3 u = Vector3.Normalize(vertices[ids[0]]-center), v = Vector3.Cross(n,u);
            ids = ids.OrderBy(i => Math.Atan2(Vector3.Dot(vertices[i]-center,v), Vector3.Dot(vertices[i]-center,u))).ToArray();
            if (Vector3.Cross(vertices[ids[1]]-vertices[ids[0]],vertices[ids[2]]-vertices[ids[0]]).LengthSquared() < 1e-14) continue;
            string key = string.Join(",",ids.Order());
            if (!seenFaces.Add(key)) continue;
            faces.Add(ids); faceCells.Add(cell);
            for (int j = 0; j < ids.Length; j++) Edge(ids[j],ids[(j+1)%ids.Length]);
        }
        void Edge(int a,int b) { if (a != b) sectionEdges.Add(a < b ? (a,b) : (b,a)); }

        var primitives = new List<Vector4>(); var bounds = new List<(Vector3 Lo,Vector3 Hi)>();
        void Add(Vector3 a, Vector3 b, Vector3 c, float radius, int type, float color, float id)
        {
            if (type == 1 && Vector3.Cross(b-a,c-a).LengthSquared() < 1e-14) return;
            primitives.Add(new(a,radius)); primitives.Add(new(b,type)); primitives.Add(new(c,color)); primitives.Add(new(id,0,0,0));
            bounds.Add((Vector3.Min(a,Vector3.Min(b,c))-new Vector3(radius),Vector3.Max(a,Vector3.Max(b,c))+new Vector3(radius)));
        }
        float thickness = (float)s.Thickness;
        if (s.View != Polytope4DView.Section)
        {
            Vector3 Project(Vector4 q) => Xyz(q) * (s.Perspective ? (float)(s.EyeDistance/(s.EyeDistance-q.W)) : 1);
            for (int i = 0; i < body.Edges.Length; i++)
            {
                var (a,b) = body.Edges[i]; Vector3 p = Project(rotated[a]), q = Project(rotated[b]);
                Add(p,q,q, s.View == Polytope4DView.Projection ? thickness : thickness*.35f,0,
                    s.View == Polytope4DView.Projection ? (rotated[a].W+rotated[b].W+2)/4 : .05f, i/(float)body.Edges.Length);
            }
            if (s.Vertices) foreach (var q in rotated) { Vector3 p = Project(q); Add(p,p,p,thickness*2.2f,0,(q.W+1)/2,.5f); }
        }
        if (s.View != Polytope4DView.Projection)
        {
            // In the combined view the slice lies in the SAME projective image as the wireframe.
            // w=t is constant, so this is a uniform scale; measurements remain in intrinsic 3D units.
            float sectionScale=s.View==Polytope4DView.SectionWithProjection && s.Perspective
                ? (float)(s.EyeDistance/(s.EyeDistance-s.Offset)) : 1;
            Vector3 SectionPoint(int i) => vertices[i]*sectionScale;
            for (int f = 0; f < faces.Count; f++)
            {
                var ids = faces[f]; float cellId=faceCells[f]/(float)Math.Max(1,body.Cells.Length-1), color=.25f+.7f*cellId;
                if (s.Faces) for (int j = 1; j < ids.Length-1; j++)
                    Add(SectionPoint(ids[0]),SectionPoint(ids[j]),SectionPoint(ids[j+1]),.0015f,1,color,cellId);
            }
            if (s.Edges) foreach (var (a,b) in sectionEdges) Add(SectionPoint(a),SectionPoint(b),SectionPoint(b),thickness,0,.98f,.98f);
            if (s.Vertices) foreach (var p in vertices) Add(p*sectionScale,p*sectionScale,p*sectionScale,thickness*2.2f,0,.98f,.98f);
        }
        var nodes = new List<Vector4>(); int[] order = Enumerable.Range(0,bounds.Count).ToArray();
        if (order.Length > 0) Node(0,order.Length);
        void Node(int start,int count)
        {
            token.ThrowIfCancellationRequested();
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int i=start;i<start+count;i++) { lo=Vector3.Min(lo,bounds[order[i]].Lo); hi=Vector3.Max(hi,bounds[order[i]].Hi); }
            int index=nodes.Count; nodes.Add(default); nodes.Add(new(hi,count==1?order[start]:-1));
            if (count>1)
            {
                Vector3 size=hi-lo; int axis=size.X>=size.Y&&size.X>=size.Z?0:size.Y>=size.Z?1:2;
                Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>(bounds[a].Lo[axis]+bounds[a].Hi[axis]).CompareTo(bounds[b].Lo[axis]+bounds[b].Hi[axis])));
                int left=count/2; Node(start,left); Node(start+left,count-left);
            }
            nodes[index]=new(lo,nodes.Count/2);
        }
        // An explicit sentinel leaf represents empty sections without zero-sized D3D buffers.
        if (nodes.Count == 0) { nodes.Add(new(new Vector3(1e9f),1)); nodes.Add(new(new Vector3(1e9f),-2)); }
        return new() { Primitives = primitives.Count == 0 ? new Vector4[4] : primitives.ToArray(), Nodes=nodes.ToArray(),
            SectionVertices=vertices.ToArray(), SectionFaces=faces.ToArray(), SectionEdges=sectionEdges.Order().ToArray(),
            MinW=rotated.Min(v=>v.W),MaxW=rotated.Max(v=>v.W) };
    }
    private static Vector3 Xyz(Vector4 v) => new(v.X,v.Y,v.Z);
}
