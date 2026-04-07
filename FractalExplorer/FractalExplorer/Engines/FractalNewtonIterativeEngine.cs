using FractalExplorer.Parsers;
using FractalExplorer.Resources;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace FractalExplorer.Engines
{
    public enum NewtonIterationMethod
    {
        Newton = 0,
        Halley = 1,
        Householder = 2
    }

    /// <summary>
    /// Движок бассейнов Ньютона с переключаемым методом итерации.
    /// </summary>
    public class FractalNewtonIterativeEngine
    {
        private ExpressionNode f_ast;
        private ExpressionNode f_deriv_ast;
        private ExpressionNode f_second_deriv_ast;
        private readonly List<ExpressionNode> _inverseDerivatives = new();

        private const double epsilon = 1e-6;

        public int MaxIterations { get; set; } = 100;
        public double CenterX { get; set; } = 0.0;
        public double CenterY { get; set; } = 0.0;
        public double Scale { get; set; } = 3.0;
        public List<Complex> Roots { get; private set; } = new List<Complex>();
        public Color[] RootColors { get; set; } = Array.Empty<Color>();
        public Color BackgroundColor { get; set; } = Color.Black;
        public bool UseGradient { get; set; } = false;

        public NewtonIterationMethod IterationMethod { get; set; } = NewtonIterationMethod.Newton;

        private int _householderOrder = 3;
        public int HouseholderOrder
        {
            get => _householderOrder;
            set
            {
                _householderOrder = Math.Max(2, value);
                if (f_ast != null)
                {
                    BuildInverseDerivatives();
                }
            }
        }

        public bool SetFormula(string expression, out string debugInfo)
        {
            var sb = new StringBuilder();
            try
            {
                var tokenizer = new Tokenizer(expression);
                var tokens = tokenizer.Tokenize();
                var parser = new Parser(tokens);

                f_ast = parser.Parse().Simplify();
                f_deriv_ast = f_ast.Differentiate("z").Simplify();
                f_second_deriv_ast = f_deriv_ast.Differentiate("z").Simplify();

                BuildInverseDerivatives();

                sb.AppendLine("Источник (legacy parser): " + expression);
                sb.AppendLine("Токены: " + string.Join(" ", tokens.Select(t => $"[{t.Type}:{t.Value}]")));
                sb.AppendLine("Исходная функция: f(z) = " + f_ast);
                sb.AppendLine("Производная: f'(z) = " + f_deriv_ast);
                sb.AppendLine("Вторая производная: f''(z) = " + f_second_deriv_ast);

                debugInfo = sb.ToString();
                FindRootsInternal();
                return true;
            }
            catch (Exception ex)
            {
                f_ast = null;
                f_deriv_ast = null;
                f_second_deriv_ast = null;
                _inverseDerivatives.Clear();
                Roots.Clear();
                debugInfo = $"ОШИБКА ПАРСИНГА:\n{ex.Message}";
                return false;
            }
        }

        public byte[] RenderSingleTile(TileInfo tile, int canvasWidth, int canvasHeight, out int bytesPerPixel)
        {
            bytesPerPixel = 4;
            byte[] buffer = new byte[tile.Bounds.Width * tile.Bounds.Height * bytesPerPixel];

            if (f_ast == null || f_deriv_ast == null || Roots.Count == 0)
            {
                return buffer;
            }

            double halfWidthPixels = canvasWidth / 2.0;
            double halfHeightPixels = canvasHeight / 2.0;
            double unitsPerPixel = Scale / canvasWidth;
            var variables = new Dictionary<string, Complex>();

            for (int y = 0; y < tile.Bounds.Height; y++)
            {
                int canvasY = tile.Bounds.Y + y;
                if (canvasY >= canvasHeight) continue;

                for (int x = 0; x < tile.Bounds.Width; x++)
                {
                    int canvasX = tile.Bounds.X + x;
                    if (canvasX >= canvasWidth) continue;

                    double complexReal = CenterX + (canvasX - halfWidthPixels) * unitsPerPixel;
                    double complexImaginary = CenterY + (canvasY - halfHeightPixels) * unitsPerPixel;
                    Complex z = new Complex(complexReal, complexImaginary);

                    int iter = IteratePoint(ref z, variables);
                    Color pixelColor = GetPixelColor(z, iter);
                    int i = (y * tile.Bounds.Width + x) * bytesPerPixel;
                    buffer[i] = pixelColor.B;
                    buffer[i + 1] = pixelColor.G;
                    buffer[i + 2] = pixelColor.R;
                    buffer[i + 3] = 255;
                }
            }
            return buffer;
        }

        public Bitmap RenderToBitmap(int renderWidth, int renderHeight, int numThreads, Action<int> reportProgressCallback, CancellationToken cancellationToken = default)
        {
            if (renderWidth <= 0 || renderHeight <= 0 || f_ast == null || f_deriv_ast == null)
            {
                return new Bitmap(1, 1);
            }

            if (Roots.Count == 0)
            {
                var emptyBmp = new Bitmap(renderWidth, renderHeight);
                using (var g = Graphics.FromImage(emptyBmp)) g.Clear(BackgroundColor);
                return emptyBmp;
            }

            var bmp = new Bitmap(renderWidth, renderHeight, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(bmpData.Stride) * renderHeight];

            var po = new ParallelOptions { MaxDegreeOfParallelism = numThreads, CancellationToken = cancellationToken };
            long done = 0;
            double unitsPerPixel = Scale / renderWidth;

            Parallel.For(0, renderHeight, po, y =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var variables = new Dictionary<string, Complex>();
                int rowOffset = y * bmpData.Stride;
                for (int x = 0; x < renderWidth; x++)
                {
                    Complex z = new(
                        CenterX + (x - renderWidth / 2.0) * unitsPerPixel,
                        CenterY + (y - renderHeight / 2.0) * unitsPerPixel);

                    int iter = IteratePoint(ref z, variables);
                    Color pixelColor = GetPixelColor(z, iter);
                    int index = rowOffset + x * 3;
                    buffer[index] = pixelColor.B;
                    buffer[index + 1] = pixelColor.G;
                    buffer[index + 2] = pixelColor.R;
                }

                long currentDone = Interlocked.Increment(ref done);
                reportProgressCallback((int)(100.0 * currentDone / renderHeight));
            });

            Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        public Bitmap RenderToBitmapSSAA(int finalWidth, int finalHeight, int numThreads, Action<int> reportProgressCallback, int supersamplingFactor, CancellationToken cancellationToken = default)
        {
            if (finalWidth <= 0 || finalHeight <= 0) return new Bitmap(1, 1);
            if (supersamplingFactor <= 1)
            {
                return RenderToBitmap(finalWidth, finalHeight, numThreads, reportProgressCallback, cancellationToken);
            }

            int highResWidth = finalWidth * supersamplingFactor;
            int highResHeight = finalHeight * supersamplingFactor;
            Color[,] tempColorBuffer = new Color[highResWidth, highResHeight];
            var po = new ParallelOptions { MaxDegreeOfParallelism = numThreads, CancellationToken = cancellationToken };
            long doneLines = 0;
            double unitsPerPixel = Scale / finalWidth;

            Parallel.For(0, highResHeight, po, y =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var variables = new Dictionary<string, Complex>();
                for (int x = 0; x < highResWidth; x++)
                {
                    Complex z = new(
                        CenterX + (x - highResWidth / 2.0) * (unitsPerPixel / supersamplingFactor),
                        CenterY + (y - highResHeight / 2.0) * (unitsPerPixel / supersamplingFactor));

                    int iter = IteratePoint(ref z, variables);
                    tempColorBuffer[x, y] = GetPixelColor(z, iter);
                }

                long currentDone = Interlocked.Increment(ref doneLines);
                reportProgressCallback((int)(50.0 * currentDone / highResHeight));
            });

            Bitmap bmp = new Bitmap(finalWidth, finalHeight, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, finalWidth, finalHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] finalBuffer = new byte[Math.Abs(bmpData.Stride) * finalHeight];
            int sampleCount = supersamplingFactor * supersamplingFactor;
            doneLines = 0;

            Parallel.For(0, finalHeight, po, finalY =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int rowOffset = finalY * bmpData.Stride;
                for (int finalX = 0; finalX < finalWidth; finalX++)
                {
                    long totalR = 0, totalG = 0, totalB = 0;
                    int startX = finalX * supersamplingFactor;
                    int startY = finalY * supersamplingFactor;
                    for (int subY = 0; subY < supersamplingFactor; subY++)
                    {
                        for (int subX = 0; subX < supersamplingFactor; subX++)
                        {
                            Color pixelColor = tempColorBuffer[startX + subX, startY + subY];
                            totalR += pixelColor.R;
                            totalG += pixelColor.G;
                            totalB += pixelColor.B;
                        }
                    }
                    int index = rowOffset + finalX * 3;
                    finalBuffer[index] = (byte)(totalB / sampleCount);
                    finalBuffer[index + 1] = (byte)(totalG / sampleCount);
                    finalBuffer[index + 2] = (byte)(totalR / sampleCount);
                }
                long currentDone = Interlocked.Increment(ref doneLines);
                reportProgressCallback(50 + (int)(50.0 * currentDone / finalHeight));
            });

            Marshal.Copy(finalBuffer, 0, bmpData.Scan0, finalBuffer.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        private int IteratePoint(ref Complex z, Dictionary<string, Complex> variables)
        {
            int iter = 0;
            while (iter < MaxIterations)
            {
                variables["z"] = z;
                Complex fValue = f_ast.Evaluate(variables);
                if (fValue.Magnitude < epsilon) break;

                Complex step = IterationMethod switch
                {
                    NewtonIterationMethod.Newton => ComputeNewtonStep(variables, fValue),
                    NewtonIterationMethod.Halley => ComputeHalleyStep(variables, fValue),
                    NewtonIterationMethod.Householder => ComputeHouseholderStep(variables),
                    _ => ComputeNewtonStep(variables, fValue)
                };

                if (step == Complex.Zero || double.IsNaN(step.Real) || double.IsNaN(step.Imaginary) ||
                    double.IsInfinity(step.Real) || double.IsInfinity(step.Imaginary))
                {
                    break;
                }

                z += step;
                iter++;
            }

            return iter;
        }

        private Complex ComputeNewtonStep(Dictionary<string, Complex> variables, Complex fValue)
        {
            Complex fDerivValue = f_deriv_ast.Evaluate(variables);
            if (fDerivValue.Magnitude < epsilon) return Complex.Zero;
            return -fValue / fDerivValue;
        }

        private Complex ComputeHalleyStep(Dictionary<string, Complex> variables, Complex fValue)
        {
            Complex f1 = f_deriv_ast.Evaluate(variables);
            if (f1.Magnitude < epsilon) return Complex.Zero;

            Complex f2 = f_second_deriv_ast.Evaluate(variables);
            Complex denominator = 2 * f1 * f1 - fValue * f2;
            if (denominator.Magnitude < epsilon) return Complex.Zero;

            return -(2 * fValue * f1) / denominator;
        }

        private Complex ComputeHouseholderStep(Dictionary<string, Complex> variables)
        {
            int d = Math.Max(2, HouseholderOrder);
            if (_inverseDerivatives.Count <= d) return Complex.Zero;

            Complex gPrev = _inverseDerivatives[d - 1].Evaluate(variables);
            Complex gCurr = _inverseDerivatives[d].Evaluate(variables);
            if (gCurr.Magnitude < epsilon) return Complex.Zero;

            return d * (gPrev / gCurr);
        }

        private void BuildInverseDerivatives()
        {
            _inverseDerivatives.Clear();

            var one = new NumberNode(Complex.One);
            ExpressionNode inverse = new BinaryOpNode(one, "/", f_ast).Simplify();
            _inverseDerivatives.Add(inverse);

            int maxDerivative = Math.Max(2, HouseholderOrder);
            ExpressionNode current = inverse;
            for (int i = 1; i <= maxDerivative; i++)
            {
                current = current.Differentiate("z").Simplify();
                _inverseDerivatives.Add(current);
            }
        }

        private Color GetPixelColor(Complex z, int iter)
        {
            if (Roots.Count == 0 || RootColors.Length == 0) return BackgroundColor;

            int rootIndex = -1;
            double minDist = double.MaxValue;

            for (int r = 0; r < Roots.Count; r++)
            {
                double dist = (z - Roots[r]).Magnitude;
                if (dist < minDist)
                {
                    minDist = dist;
                    rootIndex = r;
                }
            }

            if (rootIndex != -1 && minDist < epsilon)
            {
                Color baseColor = RootColors[rootIndex % RootColors.Length];
                if (UseGradient)
                {
                    double t = Math.Min(1.0, (double)iter / MaxIterations);
                    t = 1.0 - Math.Pow(1.0 - t, 2);
                    return LerpColor(baseColor, BackgroundColor, t);
                }
                return baseColor;
            }

            return BackgroundColor;
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private void FindRootsInternal(int maxIter = 100)
        {
            Roots.Clear();
            if (f_ast == null || f_deriv_ast == null) return;

            var startPoints = new List<Complex>();
            for (double r = 0.1; r < 2.5; r += 0.4)
            {
                for (int i = 0; i < 16; i++)
                {
                    double angle = 2 * Math.PI * i / 16.0;
                    startPoints.Add(Complex.FromPolarCoordinates(r, angle));
                }
            }
            startPoints.Add(Complex.Zero);

            foreach (var startPoint in startPoints)
            {
                Complex z = startPoint;
                var variables = new Dictionary<string, Complex>();

                for (int i = 0; i < maxIter; i++)
                {
                    variables["z"] = z;
                    Complex fValue = f_ast.Evaluate(variables);
                    Complex fDerivValue = f_deriv_ast.Evaluate(variables);

                    if (fDerivValue.Magnitude < epsilon / 100) break;

                    Complex step = fValue / fDerivValue;
                    z -= step;

                    if (step.Magnitude < epsilon)
                    {
                        if (!Roots.Any(root => (z - root).Magnitude < epsilon))
                        {
                            variables["z"] = z;
                            if (f_ast.Evaluate(variables).Magnitude < epsilon * 10)
                            {
                                Roots.Add(z);
                            }
                        }
                        break;
                    }

                    if (z.Magnitude > 1e4) break;
                }
            }

            Roots = Roots.OrderBy(r => r.Real).ThenBy(r => r.Imaginary).ToList();
        }
    }
}
