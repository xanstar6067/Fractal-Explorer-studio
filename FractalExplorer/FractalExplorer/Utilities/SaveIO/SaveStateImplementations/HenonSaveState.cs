namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для карты Хенона.
    /// </summary>
    public class HenonSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Zoom { get; set; }

        public decimal A { get; set; }
        public decimal B { get; set; }
        public decimal X0 { get; set; }
        public decimal Y0 { get; set; }
        public int Iterations { get; set; }
        public int DiscardIterations { get; set; }

        public HenonSaveState()
        {
        }

        public HenonSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
