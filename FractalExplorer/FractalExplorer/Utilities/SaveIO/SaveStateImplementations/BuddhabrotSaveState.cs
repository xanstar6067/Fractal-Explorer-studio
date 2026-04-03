using FractalExplorer.Utilities.SaveIO.ColorPalettes;

namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    public sealed class BuddhabrotSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Zoom { get; set; }

        public int MaxIterations { get; set; }
        public int SampleCount { get; set; }
        public string PaletteName { get; set; } = string.Empty;
        public BuddhabrotColorPalette? Palette { get; set; }
        public int RenderMode { get; set; }

        public decimal SampleMinRe { get; set; }
        public decimal SampleMaxRe { get; set; }
        public decimal SampleMinIm { get; set; }
        public decimal SampleMaxIm { get; set; }

        public BuddhabrotSaveState() { }

        public BuddhabrotSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
