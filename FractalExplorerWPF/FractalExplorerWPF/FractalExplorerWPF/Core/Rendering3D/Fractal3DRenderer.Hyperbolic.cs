using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11Buffer? _hyperbolicConstants;
    private (HyperbolicHoneycombKind Kind,int Depth)? _hyperbolicKey;

    private void EnsureHyperbolic(Fractal3DState state)
    {
        if(state.Kind!=Fractal3DKind.HyperbolicHoneycomb) return;
        var s=state.HyperbolicHoneycomb;
        s.Validate();
        _hyperbolicConstants ??= _device!.CreateBuffer(new BufferDescription {
            ByteWidth=7*16, Usage=ResourceUsage.Dynamic, BindFlags=BindFlags.ConstantBuffer, CPUAccessFlags=CpuAccessFlags.Write });
        if(_hyperbolicKey!=(s.Tessellation,s.Depth))
        {
            var data=new HyperbolicHoneycombGeometry(s.Tessellation).Constants(s.Depth);
            var mapped=_context!.Map(_hyperbolicConstants,MapMode.WriteDiscard);
            try { for(int i=0;i<data.Length;i++) Marshal.StructureToPtr(data[i],IntPtr.Add(mapped.DataPointer,i*16),false); }
            finally { _context.Unmap(_hyperbolicConstants,0); }
            _hyperbolicKey=(s.Tessellation,s.Depth);
        }
        _context!.PSSetConstantBuffer(1,_hyperbolicConstants);
    }
}
