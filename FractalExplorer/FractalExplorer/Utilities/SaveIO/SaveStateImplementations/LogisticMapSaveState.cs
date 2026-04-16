namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для формы орбит логистического отображения.
    /// </summary>
    public class LogisticMapSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Zoom { get; set; }
        public decimal R { get; set; }
        public decimal X0 { get; set; }
        public int Iterations { get; set; }
        public int TransientIterations { get; set; }
        public string PaletteName { get; set; } = string.Empty;

        public LogisticMapSaveState()
        {
        }

        public LogisticMapSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
