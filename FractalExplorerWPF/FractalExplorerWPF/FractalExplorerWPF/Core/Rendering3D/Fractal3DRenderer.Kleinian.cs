using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private (KleinianSettings Settings, int Depth)? _kleinianKey;
    private ID3D11Buffer? _kleinianNodes, _kleinianSpheres;
    private ID3D11ShaderResourceView? _kleinianNodesView, _kleinianSpheresView;
    public int KleinianBallCount { get; private set; }
    public bool KleinianBudgetLimited { get; private set; }

    private void EnsureKleinian(Fractal3DState state, CancellationToken token)
    {
        if (state.Kind != Fractal3DKind.Kleinian) return;
        Fractal3DCatalog.Mode(state.Kind).Validate(state);
        var key = (state.Kleinian with { Thickness = 0 }, state.Iterations);
        if (_kleinianKey != key)
        {
            var geometry = KleinianGeometry.Build(key.Item1, key.Item2, token);
            var nodes = CreateLSystemBuffer(geometry.Nodes);
            ID3D11Buffer? spheres = null;
            ID3D11ShaderResourceView? nodesView = null, spheresView = null;
            try
            {
                spheres = CreateLSystemBuffer(geometry.Spheres);
                nodesView = _device!.CreateShaderResourceView(nodes);
                spheresView = _device.CreateShaderResourceView(spheres);
            }
            catch { spheresView?.Dispose(); nodesView?.Dispose(); spheres?.Dispose(); nodes.Dispose(); throw; }
            DisposeKleinianResources();
            _kleinianNodes = nodes; _kleinianSpheres = spheres;
            _kleinianNodesView = nodesView; _kleinianSpheresView = spheresView;
            _kleinianKey = key;
            KleinianBallCount = geometry.BallCount;
            KleinianBudgetLimited = geometry.BudgetLimited;
        }
        _context!.PSSetShaderResource(2, _kleinianNodesView!);
        _context.PSSetShaderResource(3, _kleinianSpheresView!);
    }

    private void DisposeKleinianResources()
    {
        _kleinianNodesView?.Dispose(); _kleinianSpheresView?.Dispose();
        _kleinianNodes?.Dispose(); _kleinianSpheres?.Dispose();
        _kleinianNodesView = _kleinianSpheresView = null;
        _kleinianNodes = _kleinianSpheres = null;
        _kleinianKey = null;
        KleinianBallCount = 0; KleinianBudgetLimited = false;
    }
}
