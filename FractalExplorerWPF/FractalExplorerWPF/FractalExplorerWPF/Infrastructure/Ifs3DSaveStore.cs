using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class Ifs3DSaveStore() : FractalSaveStore<Ifs3DState>("IFS3D", state => state.SaveName);
