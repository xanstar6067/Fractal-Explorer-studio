using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private HopfSettings? _hopfKey;
    private ID3D11Buffer? _hopfNodes, _hopfRings;
    private ID3D11ShaderResourceView? _hopfNodesView, _hopfRingsView;

    private void EnsureHopf(Fractal3DState state, CancellationToken token)
    {
        if (state.Kind != Fractal3DKind.Hopf) return;
        HopfSettings settings = state.Hopf;
        settings.Validate();
        if (_hopfKey is null || !settings.GeometryEquals(_hopfKey))
        {
            var geometry = HopfGeometry.Build(settings, token);
            token.ThrowIfCancellationRequested();
            var nodes = CreateLSystemBuffer(geometry.Nodes);
            ID3D11Buffer? rings = null;
            ID3D11ShaderResourceView? nodesView = null, ringsView = null;
            try
            {
                rings = CreateLSystemBuffer(geometry.Rings);
                nodesView = _device!.CreateShaderResourceView(nodes);
                ringsView = _device.CreateShaderResourceView(rings);
            }
            catch { ringsView?.Dispose(); nodesView?.Dispose(); rings?.Dispose(); nodes.Dispose(); throw; }
            DisposeHopfResources();
            _hopfNodes = nodes; _hopfRings = rings;
            _hopfNodesView = nodesView; _hopfRingsView = ringsView;
            _hopfKey = settings.Copy();
        }
        _context!.PSSetShaderResource(2, _hopfNodesView!);
        _context.PSSetShaderResource(3, _hopfRingsView!);
    }

    private void DisposeHopfResources()
    {
        _hopfNodesView?.Dispose(); _hopfRingsView?.Dispose();
        _hopfNodes?.Dispose(); _hopfRings?.Dispose();
        _hopfNodesView = _hopfRingsView = null;
        _hopfNodes = _hopfRings = null; _hopfKey = null;
    }
}
