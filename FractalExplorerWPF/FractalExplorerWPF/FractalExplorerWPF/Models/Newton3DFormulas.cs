using System.Globalization;

namespace FractalExplorerWPF.Models;

public sealed record Newton3DFormulaExample(string Name, string Formula);

public static class Newton3DFormulas
{
    public static IReadOnlyList<Newton3DFormulaExample> Examples { get; } =
    [
        new("Классический кубический", "z^3-1"),
        new("Пятая степень с линейным членом", "z^5-z+1"),
        new("Четыре комплексных корня", "(z-1)*(z+1)*(z-i)*(z+i)"),
        new("Кубический с циклами Ньютона", "z^3-2z+2"),
        new("Рациональная функция", "(z^3-1)/(z^2+2)"),
        new("Синус", "sin(z)"),
        new("Экспонента", "exp(z)-1"),
        new("Косинус и линейный член", "cos(z)-z"),
        new("Гиперболический синус", "sinh(z)"),
        new("Логарифм с полиномом", "log(z)+z^3-1"),
        new("Квадратный корень с полиномом", "sqrt(z)+z^3-1")
    ];

    public static string RandomFormula(Random random)
    {
        int count = random.Next(3, 7);
        double phase = random.NextDouble() * Math.Tau;
        var factors = new List<string>();
        for (int k = 0; k < count; k++)
        {
            double angle = phase + Math.Tau * k / count;
            double radius = .5 + random.NextDouble() * .8;
            string real = (radius * Math.Cos(angle)).ToString("0.000", CultureInfo.InvariantCulture);
            string imaginary = (radius * Math.Sin(angle)).ToString("+0.000;-0.000", CultureInfo.InvariantCulture);
            factors.Add($"(z-({real}{imaginary}i))");
        }
        return string.Join("*", factors);
    }
}
