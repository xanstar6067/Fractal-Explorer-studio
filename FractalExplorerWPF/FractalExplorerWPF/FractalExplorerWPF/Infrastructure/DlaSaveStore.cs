using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class DlaSaveStore() : FractalSaveStore<DlaState>("DLA", state => state.SaveName);
