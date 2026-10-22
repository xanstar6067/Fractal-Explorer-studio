using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11Buffer? _hybridProgramBuffer;
    private readonly float[] _hybridProgram = new float[BulbBoxProgram.FloatCount];

    private void WriteHybridProgram(Fractal3DState state)
    {
        if (state.Kind != Fractal3DKind.BulbBoxHybrid) return;
        BulbBoxProgram.Fill(state.HybridSequence, _hybridProgram);
        _hybridProgramBuffer ??= _device!.CreateBuffer(new BufferDescription
        {
            ByteWidth = BulbBoxProgram.FloatCount * sizeof(float), Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write
        });
        var mapped = _context!.Map(_hybridProgramBuffer, MapMode.WriteDiscard);
        try { Marshal.Copy(_hybridProgram, 0, mapped.DataPointer, _hybridProgram.Length); }
        finally { _context.Unmap(_hybridProgramBuffer, 0); }
        _context.PSSetConstantBuffer(1, _hybridProgramBuffer);
    }
}
