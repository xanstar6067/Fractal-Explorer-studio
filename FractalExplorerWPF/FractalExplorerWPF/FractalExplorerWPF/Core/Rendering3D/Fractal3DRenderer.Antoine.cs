using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private (AntoineSettings Settings, int Depth)? _antoineKey;
    private ID3D11Buffer? _antoineNodes;
    private ID3D11ShaderResourceView? _antoineNodesView;
    public int AntoineRingCount { get; private set; }
    internal int AntoineGeometryBuilds { get; private set; }

    private void EnsureAntoine(Fractal3DState state, CancellationToken token)
    {
        if (state.Kind != Fractal3DKind.AntoineNecklace) return;
        var key = (state.Antoine, state.Iterations);
        if (_antoineKey != key)
        {
            var geometry = AntoineGeometry.Build(state.Antoine, state.Iterations, token);
            token.ThrowIfCancellationRequested();
            var buffer = CreateLSystemBuffer(geometry.Nodes);
            ID3D11ShaderResourceView view;
            try { view = _device!.CreateShaderResourceView(buffer); }
            catch { buffer.Dispose(); throw; }
            DisposeAntoineResources();
            _antoineNodes = buffer;
            _antoineNodesView = view;
            _antoineKey = key;
            AntoineRingCount = geometry.RingCount;
            AntoineGeometryBuilds++;
        }
        _context!.PSSetShaderResource(2, _antoineNodesView!);
    }

    private void DisposeAntoineResources()
    {
        _antoineNodesView?.Dispose();
        _antoineNodes?.Dispose();
        _antoineNodesView = null;
        _antoineNodes = null;
        _antoineKey = null;
        AntoineRingCount = 0;
    }
}
