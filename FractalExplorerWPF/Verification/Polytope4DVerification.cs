using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPolytope4DAsync(string[] args)
    {
        const Fractal3DKind kind = Fractal3DKind.Polytope4D;
        string output = ReadOption(args,"--out") ?? Path.Combine(AppContext.BaseDirectory,"Polytope4DVerification");
        Directory.CreateDirectory(output);
        AppPaths.OverrideDataRoot(Path.Combine(output,"Data",Guid.NewGuid().ToString("N")));
        VerifyCatalogData(); VerifyCatalogSearch();
        int[][] expected = [[5,10,10,5,4],[16,32,24,8,8],[8,24,32,16,4],[24,96,96,24,6],[600,1200,720,120,20],[120,720,1200,600,4]];
        foreach (var k in Enum.GetValues<Polytope4DKind>())
        {
            var b=Polytope4DBody.Get(k); var e=expected[(int)k];
            Check(b.Vertices.Length==e[0] && b.Edges.Length==e[1] && b.FaceCount==e[2] && b.Cells.Length==e[3],$"{k}: f-vector.");
            Check(b.Cells.All(c=>c.Length==e[4]),$"{k}: cell sizes.");
            Check(b.Vertices.All(v=>Math.Abs(v.Length()-1)<2e-6),$"{k}: common unit circumradius.");
            float edge=Vector4.Distance(b.Vertices[b.Edges[0].A],b.Vertices[b.Edges[0].B]);
            Check(b.Edges.All(p=>Math.Abs(Vector4.Distance(b.Vertices[p.A],b.Vertices[p.B])-edge)<2e-6),$"{k}: equal edges.");
            for(int i=0;i<b.Cells.Length;i++)
            {
                float support=Vector4.Dot(b.Vertices[b.Cells[i][0]],b.Normals[i]);
                Check(b.Cells[i].All(j=>Math.Abs(Vector4.Dot(b.Vertices[j],b.Normals[i])-support)<2e-5),$"{k}: flat cells.");
                Check(b.Vertices.All(v=>Vector4.Dot(v,b.Normals[i])<=support+2e-5),$"{k}: supporting hyperplanes.");
            }
            var s=new Polytope4DSettings { Body=k, View=Polytope4DView.Section, XY=13,XZ=7,XW=31,YW=19,ZW=23 };
            Check(Math.Abs(Polytope4DGeometry.Rotate(b.Vertices[0],s).Length()-1)<2e-6,"4D rotations are orthogonal.");
            foreach(double t in new[] { -.7,-.2,0,.3,.7 })
            {
                var g=Polytope4DGeometry.Build(s with { Offset=t },CancellationToken.None);
                if(g.SectionFaces.Length==0) continue;
                Check(g.SectionVertices.Length-g.SectionEdges.Length+g.SectionFaces.Length==2,$"{k}: section Euler characteristic at {t}.");
                foreach(var edgePair in g.SectionEdges)
                {
                    int incidence=g.SectionFaces.Count(f=>Enumerable.Range(0,f.Length).Any(i=>
                        f[i]==edgePair.A && f[(i+1)%f.Length]==edgePair.B || f[i]==edgePair.B && f[(i+1)%f.Length]==edgePair.A));
                    Check(incidence==2,$"{k}: watertight section at {t}.");
                }
                foreach(var face in g.SectionFaces)
                {
                    var a=g.SectionVertices[face[0]]; var n=Vector3.Normalize(Vector3.Cross(g.SectionVertices[face[1]]-a,g.SectionVertices[face[2]]-a));
                    Check(face.All(i=>Math.Abs(Vector3.Dot(g.SectionVertices[i]-a,n))<2e-5),"Section polygons are planar.");
                }
            }
            var empty=Polytope4DGeometry.Build(s with { Offset=1.05 },CancellationToken.None);
            Check(empty.SectionVertices.Length==0 && empty.Nodes.Length==2,"Empty sections use safe GPU sentinel.");
            Console.WriteLine($"{k}: {b.Vertices.Length} vertices, {b.Edges.Length} edges, {b.Cells.Length} cells; topology passed.");
        }
        var cubeSettings=new Polytope4DSettings { View=Polytope4DView.Section,XW=0,YW=0 };
        var cube=Polytope4DGeometry.Build(cubeSettings,CancellationToken.None);
        Check(cube.SectionVertices.Length==8 && cube.SectionEdges.Length==12 && cube.SectionFaces.Length==6,"Central tesseract section is a cube.");
        Check(cube.SectionVertices.All(p=>Math.Abs(Math.Abs(p.X)-.5)<1e-6 && Math.Abs(Math.Abs(p.Y)-.5)<1e-6 && Math.Abs(Math.Abs(p.Z)-.5)<1e-6),"Cube coordinates.");
        Check(Math.Abs(cube.SurfaceArea-6)<1e-6 && Math.Abs(cube.Volume-1)<1e-6,"Independent cube area and volume.");
        var tangent=Polytope4DGeometry.Build(cubeSettings with { Offset=.5 },CancellationToken.None);
        Check(tangent.SectionVertices.Length==8 && tangent.SectionEdges.Length==12 && tangent.SectionFaces.Length==6,"Coincident cell handled once through neighbours.");
        var octa=Polytope4DGeometry.Build(cubeSettings with { Body=Polytope4DKind.Orthoplex },CancellationToken.None);
        Check(octa.SectionVertices.Length==6 && octa.SectionEdges.Length==12 && octa.SectionFaces.Length==8,"Central orthoplex section is an octahedron.");
        Check(Math.Abs(octa.Volume-4.0/3)<1e-6 && Math.Abs(octa.SurfaceArea-4*Math.Sqrt(3))<1e-6,"Independent octahedron area and volume.");
        var combined=Polytope4DGeometry.Build(cubeSettings with { View=Polytope4DView.SectionWithProjection,Offset=.2,Edges=false },CancellationToken.None);
        int triangle=Enumerable.Range(0,combined.Primitives.Length/4).First(i=>combined.Primitives[i*4+1].W==1);
        Check(Math.Abs(Math.Abs(combined.Primitives[triangle*4].X)-.5*2.5/2.3)<1e-6,"Combined section aligns with the perspective wireframe.");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel(); bool caught=false;
            try { Polytope4DGeometry.Build(cubeSettings,cancelled.Token); } catch(OperationCanceledException) { caught=true; }
            Check(caught,"Geometry cancellation.");
        }
        foreach(var bad in new[] { cubeSettings with { Offset=double.NaN },cubeSettings with { EyeDistance=1 },
            cubeSettings with { XW=double.PositiveInfinity },cubeSettings with { Body=(Polytope4DKind)99 },
            cubeSettings with { Faces=false,Edges=false,Vertices=false } })
        {
            bool caught=false; try { bad.Validate(); } catch(ArgumentException) { caught=true; }
            Check(caught,"Invalid input rejected.");
        }
        using var renderer=new Fractal3DRenderer();
        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var bitmap=await renderer.RenderAsync(preset,240,240,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap),240,240),$"Visible preset: {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output,$"preset-{index++}.png"),bitmap);
        }
        var state=Fractal3DCatalog.CreateDefaultState(kind); state.Polytope4D=cubeSettings;
        byte[] reference=Pixels(await renderer.RenderAsync(state,128,128,null,CancellationToken.None));
        int builds=renderer.Polytope4DBuilds;
        var camera=state.Clone(); camera.CameraYaw+=30;
        byte[] cameraPixels=Pixels(await renderer.RenderAsync(camera,128,128,null,CancellationToken.None));
        Check(!reference.SequenceEqual(cameraPixels),"3D camera changes view.");
        Check(renderer.Polytope4DBuilds==builds,"Camera does not rebuild 4D geometry.");
        // Independent slab intersection of the known central cube, compared with GPU surface probes.
        state.Detail=.05;
        int hits=0;
        for(int y=35;y<=65;y+=10) for(int x=35;x<=65;x+=10)
        {
            var origin=Fractal3DCamera.Build(state).Position;
            var ray=Fractal3DCamera.PixelRay(state,x+.5,y+.5,100,100);
            double near=0,far=double.PositiveInfinity;
            for(int axis=0;axis<3;axis++)
            {
                if(Math.Abs(ray[axis])<1e-10) continue;
                double a=(-.5-origin[axis])/ray[axis],b=(.5-origin[axis])/ray[axis];
                near=Math.Max(near,Math.Min(a,b)); far=Math.Min(far,Math.Max(a,b));
            }
            if(near>=far) continue;
            double actual=await renderer.ProbeDistanceAsync(state,(x+.5)*10,(y+.5)*10,1000,1000,CancellationToken.None);
            Check(double.IsFinite(actual) && Math.Abs(actual-near)<.025,$"GPU cube surface: {actual} vs {near}."); hits++;
        }
        Check(hits>=4,"Independent probes hit face interiors.");
        state.Polytope4D=cubeSettings with { View=Polytope4DView.SectionWithProjection,Offset=.1234567890123,XW=23.456789012345, Speed=-12.345678901 };
        var store=new Fractal3DSaveStore(kind); state.SaveName="Polytope precise roundtrip";
        store.Save(state);
        Check(store.Load().Single(s=>s.SaveName==state.SaveName).Polytope4D==state.Polytope4D,"Save roundtrip includes complete laboratory settings.");
        Check(!ReferenceEquals(state.Clone().Polytope4D,state.Polytope4D),"Independent settings clone.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-Polytope4D-pixel")),"Shader participates in shared cache.");
        EnsureWindowThemeStyles();
        var window=new Fractal3DWindow(kind);
        try
        {
            window.LoadState(state);
            Check(window.CaptureState("ui").Polytope4D==state.Polytope4D,"UI preserves exact parameters.");
            var editor=(Polytope4DEditor)window.FindName("Polytope4DEditor");
            Check(editor.Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("ShapeExpander")).Visibility==Visibility.Collapsed,"Dedicated laboratory layout.");
            var offset=(TextBox)editor.FindName("OffsetBox"); offset.Text="NaN"; NumericInput.CommitAll(editor);
            Check(double.IsFinite(editor.Capture().Offset),"Non-finite numeric input recovers.");
            ((ComboBox)editor.FindName("ViewBox")).SelectedIndex=0;
            offset.Text="invalid"; editor.Capture();
            ((ComboBox)editor.FindName("ViewBox")).SelectedIndex=1;
            offset.Text="999"; NumericInput.CommitAll(window);
            Check(editor.Capture().Offset==1.05,"Section offset clamps visibly.");
            editor.Load(cubeSettings);
            ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.IsPlaying,"Animation starts on demand.");
            editor.OnFrameDisplayed(cubeSettings,cube);
            editor.AdvanceAnimation(.1);
            Check(Math.Abs(editor.Capture().XW-1.8)<1e-9,"Animation advances in degrees per second.");
            var advanced=editor.Capture();
            Check(!editor.IsCurrentFrame(cubeSettings) && editor.IsCurrentFrame(advanced),"Outdated frame cannot replace the current orientation.");
            editor.AdvanceAnimation(.1);
            Check(Math.Abs(editor.Capture().XW-1.8)<1e-9,"Animation waits for frame publication.");
            editor.Stop();
            Check(!editor.IsPlaying && editor.Capture().XW==0,"Pause restores the displayed orientation.");
            Check(!editor.IsCurrentFrame(advanced),"In-flight animated frame is rejected after pause.");
            ((Button)editor.FindName("DualButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Capture().Body==Polytope4DKind.Orthoplex,"Dual type preserves view controls.");
            editor.Load(cubeSettings with { View=Polytope4DView.Projection,Animation=Polytope4DAnimation.Section,Offset=1.04,Speed=60 });
            ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Capture().View==Polytope4DView.Section,"Section animation activates section view.");
            editor.AdvanceAnimation(.1);
            Check(editor.Capture().Offset== -1.05,"Section animation cycles outside the body.");
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!editor.IsPlaying,"Cancel stops laboratory animation.");
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (polytope4d): six regular bodies, exact sections, GPU presets/probes, caching, saves and numeric UI. Images: {output}");
    }
}
