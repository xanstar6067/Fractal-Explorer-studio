using FractalExplorer.Engines;

namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    public sealed class IFSSaveState : FractalSaveStateBase
    {
        public IfsPreset Preset { get; set; }
        public int Iterations { get; set; }
        public Color FractalColor { get; set; }
        public Color BackgroundColor { get; set; }

        public IFSSaveState()
        {
        }

        public IFSSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
