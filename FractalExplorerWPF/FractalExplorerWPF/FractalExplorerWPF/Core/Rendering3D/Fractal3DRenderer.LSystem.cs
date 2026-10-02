using System.Numerics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private LSystem3DSettings? _lSystemKey;
    private LSystem3DGeometry? _lSystemGeometry;
    private ID3D11Buffer? _lSystemNodes, _lSystemCapsules;
    private ID3D11ShaderResourceView? _lSystemNodesView, _lSystemCapsulesView;

    public int LSystemSegmentCount => _lSystemGeometry?.Segments.Length ?? 0;
    public int LSystemSymbolCount => _lSystemGeometry?.SymbolCount ?? 0;

    private void EnsureLSystem(Fractal3DState state, CancellationToken token)
    {
        if (state.Kind != Fractal3DKind.LSystem3D) return;
        state.LSystem.Validate();
        LSystem3DSettings key = state.LSystem.GeometryKey();
        if (_lSystemKey != key)
        {
            LSystem3DGeometry geometry = LSystem3DGeometry.Build(key, token);
            token.ThrowIfCancellationRequested();
            // Allocate both buffers before replacing the previous valid scene.
            var nodes = CreateLSystemBuffer(geometry.Nodes);
            ID3D11Buffer? capsules = null;
            ID3D11ShaderResourceView? nodeView = null, capsuleView = null;
            try
            {
                capsules = CreateLSystemBuffer(geometry.Capsules);
                nodeView = _device!.CreateShaderResourceView(nodes);
                capsuleView = _device.CreateShaderResourceView(capsules);
            }
            catch { capsuleView?.Dispose(); nodeView?.Dispose(); capsules?.Dispose(); nodes.Dispose(); throw; }
            DisposeLSystemResources();
            _lSystemNodes = nodes; _lSystemCapsules = capsules;
            _lSystemNodesView = nodeView; _lSystemCapsulesView = capsuleView;
            _lSystemGeometry = geometry; _lSystemKey = key;
        }
        _context!.PSSetShaderResource(2, _lSystemNodesView!);
        _context.PSSetShaderResource(3, _lSystemCapsulesView!);
    }

    private ID3D11Buffer CreateLSystemBuffer(Vector4[] values)
    {
        var buffer = _device!.CreateBuffer(new BufferDescription
        {
            ByteWidth = checked((uint)values.Length * 16), Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource, CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 16
        });
        try
        {
            var mapped = _context!.Map(buffer, MapMode.WriteDiscard);
            try
            {
                float[] floats = new float[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                { floats[i * 4] = values[i].X; floats[i * 4 + 1] = values[i].Y; floats[i * 4 + 2] = values[i].Z; floats[i * 4 + 3] = values[i].W; }
                Marshal.Copy(floats, 0, mapped.DataPointer, floats.Length);
            }
            finally { _context.Unmap(buffer, 0); }
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    private void DisposeLSystemResources()
    {
        _lSystemNodesView?.Dispose(); _lSystemCapsulesView?.Dispose();
        _lSystemNodes?.Dispose(); _lSystemCapsules?.Dispose();
        _lSystemNodesView = _lSystemCapsulesView = null;
        _lSystemNodes = _lSystemCapsules = null;
        _lSystemGeometry = null; _lSystemKey = null;
    }
}
