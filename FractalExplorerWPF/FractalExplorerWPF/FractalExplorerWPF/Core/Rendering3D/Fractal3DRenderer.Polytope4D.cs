using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private Polytope4DSettings? _polytope4DKey;
    private ID3D11Buffer? _polytope4DNodes, _polytope4DPrimitives;
    private ID3D11ShaderResourceView? _polytope4DNodesView, _polytope4DPrimitivesView;
    internal Polytope4DGeometry? Polytope4DGeometry { get; private set; }
    internal int Polytope4DBuilds { get; private set; }

    private void EnsurePolytope4D(Fractal3DState state, CancellationToken token)
    {
        if (state.Kind != Fractal3DKind.Polytope4D) return;
        var settings = state.Polytope4D with { Animation = default, Speed = 0 };
        if (settings != _polytope4DKey)
        {
            var geometry = Core.Rendering3D.Polytope4DGeometry.Build(settings,token);
            token.ThrowIfCancellationRequested();
            var nodes = CreateLSystemBuffer(geometry.Nodes);
            ID3D11Buffer? primitives = null;
            ID3D11ShaderResourceView? nodesView = null, primitivesView = null;
            try
            {
                primitives = CreateLSystemBuffer(geometry.Primitives);
                nodesView = _device!.CreateShaderResourceView(nodes);
                primitivesView = _device.CreateShaderResourceView(primitives);
            }
            catch { primitivesView?.Dispose(); nodesView?.Dispose(); primitives?.Dispose(); nodes.Dispose(); throw; }
            DisposePolytope4DResources();
            _polytope4DNodes = nodes; _polytope4DPrimitives = primitives;
            _polytope4DNodesView = nodesView; _polytope4DPrimitivesView = primitivesView;
            _polytope4DKey = settings; Polytope4DGeometry = geometry; Polytope4DBuilds++;
        }
        _context!.PSSetShaderResource(2,_polytope4DNodesView!);
        _context.PSSetShaderResource(3,_polytope4DPrimitivesView!);
    }
    private void DisposePolytope4DResources()
    {
        _polytope4DNodesView?.Dispose(); _polytope4DPrimitivesView?.Dispose();
        _polytope4DNodes?.Dispose(); _polytope4DPrimitives?.Dispose();
        _polytope4DNodesView = _polytope4DPrimitivesView = null;
        _polytope4DNodes = _polytope4DPrimitives = null;
        _polytope4DKey = null; Polytope4DGeometry = null;
    }
}
