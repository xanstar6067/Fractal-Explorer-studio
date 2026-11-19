using FractalExplorerWPF.Models;
using System.Numerics;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ParametricSurfaceSettings? _parametricGeometryKey;
    private ID3D11ShaderResourceView? _parametricNodes, _parametricTriangles, _parametricVertices;
    private float _parametricRadius;
    internal int ParametricSkippedSamples { get; private set; }
    internal int ParametricMeshBuilds { get; private set; }

    private void PrepareParametricSurface(Fractal3DState state, CancellationToken token, ref FrameConstants constants)
    {
        if (state.Kind != Fractal3DKind.ParametricSurface) return;

        var settings = state.ParametricSurface ?? new();
        var key = settings.GeometryKey();
        if (key != _parametricGeometryKey)
        {
            ParametricSurfaceMesh mesh = ParametricSurfaceMesh.Build(settings, token);
            token.ThrowIfCancellationRequested();
            ID3D11ShaderResourceView? nodes = null, triangles = null, vertices = null;
            try
            {
                nodes = CreateView(mesh.Nodes);
                triangles = CreateView(mesh.Triangles);
                vertices = CreateView(mesh.Vertices);
            }
            catch
            {
                nodes?.Dispose();
                triangles?.Dispose();
                vertices?.Dispose();
                throw;
            }

            DisposeParametricSurface();
            (_parametricNodes, _parametricTriangles, _parametricVertices) = (nodes, triangles, vertices);
            _parametricRadius = mesh.Radius;
            ParametricSkippedSamples = mesh.SkippedSamples;
            _parametricGeometryKey = key;
            ParametricMeshBuilds++;
        }

        constants.ShapeA.X = _parametricRadius;
        _context!.PSSetShaderResource(2, _parametricNodes!);
        _context.PSSetShaderResource(3, _parametricTriangles!);
        _context.PSSetShaderResource(4, _parametricVertices!);
    }

    private ID3D11ShaderResourceView CreateView(Vector4[] data)
    {
        using var buffer = CreateLSystemBuffer(data);
        return _device!.CreateShaderResourceView(buffer);
    }

    private void DisposeParametricSurface()
    {
        _parametricNodes?.Dispose();
        _parametricTriangles?.Dispose();
        _parametricVertices?.Dispose();
        _parametricNodes = _parametricTriangles = _parametricVertices = null;
        _parametricGeometryKey = null;
    }
}
