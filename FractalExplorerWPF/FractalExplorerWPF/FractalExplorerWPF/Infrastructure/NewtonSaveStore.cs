using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class NewtonSaveStore() : FractalSaveStore<NewtonState>("NewtonPools", state => state.SaveName);
