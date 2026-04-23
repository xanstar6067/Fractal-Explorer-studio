namespace FractalExplorer.Utilities
{
    /// <summary>
    /// Представляет комплексное число, используя тип decimal для высокой точности.
    /// Эта структура является неизменяемой (immutable), то есть её значения не могут быть изменены после создания.
    /// </summary>
    public readonly struct ComplexDecimal
    {
        #region Fields

        /// <summary>
        /// Действительная часть комплексного числа.
        /// </summary>
        public readonly decimal Real;

        /// <summary>
        /// Мнимая часть комплексного числа.
        /// </summary>
        public readonly decimal Imaginary;

        #endregion

        #region Properties
        /// <summary>
        /// Получает аргумент (угол) комплексного числа в радианах.
        /// Возвращает значение от -π до π.
        /// </summary>
        public double Argument => Math.Atan2((double)Imaginary, (double)Real);

        /// <summary>
        /// Получает квадрат модуля (длины) комплексного числа.
        /// Вычисляется как Real^2 + Imaginary^2.
        /// </summary>
        public decimal MagnitudeSquared => Real * Real + Imaginary * Imaginary;

        /// <summary>
        /// Получает модуль (длину) комплексного числа.
        /// Вычисляется как квадратный корень из MagnitudeSquared.
        /// </summary>
        public decimal Magnitude => DecimalMath.Sqrt(MagnitudeSquared);

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр структуры <see cref="ComplexDecimal"/>
        /// с указанными действительной и мнимой частями.
        /// </summary>
        /// <param name="real">Действительная часть комплексного числа.</param>
        /// <param name="imaginary">Мнимая часть комплексного числа.</param>
        public ComplexDecimal(decimal real, decimal imaginary)
        {
            Real = real;
            Imaginary = imaginary;
        }

        #endregion

        #region Static Properties

        /// <summary>
        /// Представляет комплексное число 0 + 0i.
        /// </summary>
        public static readonly ComplexDecimal Zero = new ComplexDecimal(0m, 0m);
        public static readonly ComplexDecimal One = new ComplexDecimal(1, 0);

        #endregion

        #region Operators

        /// <summary>
        /// Определяет оператор сложения для двух комплексных чисел.
        /// </summary>
        /// <param name="a">Первое комплексное число.</param>
        /// <param name="b">Второе комплексное число.</param>
        /// <returns>Новое комплексное число, представляющее сумму a и b.</returns>
        public static ComplexDecimal operator +(ComplexDecimal a, ComplexDecimal b)
        {
            return new ComplexDecimal(a.Real + b.Real, a.Imaginary + b.Imaginary);
        }

        /// <summary>
        /// Определяет оператор умножения для двух комплексных чисел.
        /// </summary>
        /// <param name="a">Первое комплексное число.</param>
        /// <param name="b">Второе комплексное число.</param>
        /// <returns>Новое комплексное число, представляющее произведение a и b.</returns>
        public static ComplexDecimal operator *(ComplexDecimal a, ComplexDecimal b)
        {
            decimal realPart = a.Real * b.Real - a.Imaginary * b.Imaginary;
            decimal imaginaryPart = a.Real * b.Imaginary + a.Imaginary * b.Real;
            return new ComplexDecimal(realPart, imaginaryPart);
        }

        /// <summary>
        /// Определяет оператор вычитания для двух комплексных чисел.
        /// </summary>
        /// <param name="a">Комплексное число, из которого вычитается.</param>
        /// <param name="b">Комплексное число, которое вычитается.</param>
        /// <returns>Новое комплексное число, представляющее разность a и b.</returns>
        public static ComplexDecimal operator -(ComplexDecimal a, ComplexDecimal b)
        {
            return new ComplexDecimal(a.Real - b.Real, a.Imaginary - b.Imaginary);
        }

        /// <summary>
        /// Определяет оператор унарного минуса (отрицания) для комплексного числа.
        /// </summary>
        /// <param name="value">Комплексное число для отрицания.</param>
        /// <returns>Новое комплексное число, представляющее отрицание value.</returns>
        public static ComplexDecimal operator -(ComplexDecimal value)
        {
            return new ComplexDecimal(-value.Real, -value.Imaginary);
        }

        /// <summary>
        /// Определяет оператор деления для двух комплексных чисел.
        /// </summary>
        /// <param name="a">Комплексное число (делимое).</param>
        /// <param name="b">Комплексное число (делитель).</param>
        /// <returns>Новое комплексное число, представляющее частное a и b.</returns>
        /// <exception cref="DivideByZeroException">Выбрасывается, если делитель b равен нулю.</exception>
        public static ComplexDecimal operator /(ComplexDecimal a, ComplexDecimal b)
        {
            decimal denominator = b.MagnitudeSquared;
            if (denominator == 0)
            {
                throw new DivideByZeroException("Деление на комплексное число, равное нулю.");
            }

            decimal realPart = (a.Real * b.Real + a.Imaginary * b.Imaginary) / denominator;
            decimal imaginaryPart = (a.Imaginary * b.Real - a.Real * b.Imaginary) / denominator;
            return new ComplexDecimal(realPart, imaginaryPart);
        }

        /// <summary>
        /// Определяет оператор равенства для двух комплексных чисел.
        /// </summary>
        /// <param name="a">Первое комплексное число.</param>
        /// <param name="b">Второе комплексное число.</param>
        /// <returns>True, если действительные и мнимые части чисел равны; иначе False.</returns>
        public static bool operator ==(ComplexDecimal a, ComplexDecimal b)
        {
            return a.Real == b.Real && a.Imaginary == b.Imaginary;
        }

        /// <summary>
        /// Определяет оператор неравенства для двух комплексных чисел.
        /// </summary>
        /// <param name="a">Первое комплексное число.</param>
        /// <param name="b">Второе комплексное число.</param>
        /// <returns>True, если действительные или мнимые части чисел не равны; иначе False.</returns>
        public static bool operator !=(ComplexDecimal a, ComplexDecimal b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Определяет оператор сложения комплексного числа и скаляра (decimal).
        /// </summary>
        /// <param name="a">Комплексное число.</param>
        /// <param name="b">Скалярное значение.</param>
        /// <returns>Новое комплексное число, представляющее сумму.</returns>
        public static ComplexDecimal operator +(ComplexDecimal a, decimal b)
        {
            return new ComplexDecimal(a.Real + b, a.Imaginary);
        }

        /// <summary>
        /// Определяет оператор вычитания скаляра (decimal) из комплексного числа.
        /// </summary>
        /// <param name="a">Комплексное число.</param>
        /// <param name="b">Скалярное значение.</param>
        /// <returns>Новое комплексное число, представляющее разность.</returns>
        public static ComplexDecimal operator -(ComplexDecimal a, decimal b)
        {
            return new ComplexDecimal(a.Real - b, a.Imaginary);
        }

        /// <summary>
        /// Определяет оператор умножения комплексного числа на скаляр (decimal).
        /// </summary>
        /// <param name="a">Комплексное число.</param>
        /// <param name="b">Скалярное значение.</param>
        /// <returns>Новое комплексное число, представляющее произведение.</returns>
        public static ComplexDecimal operator *(ComplexDecimal a, decimal b)
        {
            return new ComplexDecimal(a.Real * b, a.Imaginary * b);
        }

        /// <summary>
        /// Определяет оператор умножения скаляра (decimal) на комплексное число.
        /// </summary>
        /// <param name="b">Скалярное значение.</param>
        /// <param name="a">Комплексное число.</param>
        /// <returns>Новое комплексное число, представляющее произведение.</returns>
        public static ComplexDecimal operator *(decimal b, ComplexDecimal a)
        {
            // Умножение коммутативно, поэтому просто вызываем существующий оператор.
            return a * b;
        }

        /// <summary>
        /// Определяет оператор деления комплексного числа на скаляр (decimal).
        /// </summary>
        /// <param name="a">Комплексное число.</param>
        /// <param name="b">Скалярное значение.</param>
        /// <returns>Новое комплексное число, представляющее частное.</returns>
        /// <exception cref="DivideByZeroException">Выбрасывается, если делитель b равен нулю.</exception>
        public static ComplexDecimal operator /(ComplexDecimal a, decimal b)
        {
            if (b == 0)
            {
                throw new DivideByZeroException("Деление на скаляр, равный нулю.");
            }
            return new ComplexDecimal(a.Real / b, a.Imaginary / b);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Вычисляет комплексно сопряженное число к данному.
        /// </summary>
        /// <returns>Новое комплексное число, представляющее комплексно сопряженное.</returns>
        public ComplexDecimal Conjugate()
        {
            return new ComplexDecimal(Real, -Imaginary);
        }

        /// <summary>
        /// Создает комплексное число из полярных координат (модуля и аргумента).
        /// </summary>
        /// <param name="magnitude">Модуль (длина) комплексного числа.</param>
        /// <param name="angleRadians">Аргумент (угол) комплексного числа в радианах.</param>
        /// <returns>Новое комплексное число, созданное из полярных координат.</returns>
        public static ComplexDecimal FromPolarCoordinates(double magnitude, double angleRadians)
        {
            decimal real = (decimal)(magnitude * Math.Cos(angleRadians));
            decimal imaginary = (decimal)(magnitude * Math.Sin(angleRadians));
            return new ComplexDecimal(real, imaginary);
        }

        /// <summary>
        /// Создает комплексное число из полярных координат (модуля и аргумента) в decimal-точности.
        /// </summary>
        /// <param name="magnitude">Модуль (длина) комплексного числа.</param>
        /// <param name="angleRadians">Аргумент (угол) комплексного числа в радианах.</param>
        /// <returns>Новое комплексное число, созданное из полярных координат.</returns>
        public static ComplexDecimal FromPolarCoordinatesDecimal(decimal magnitude, decimal angleRadians)
        {
            decimal real = magnitude * DecimalMath.Cos(angleRadians);
            decimal imaginary = magnitude * DecimalMath.Sin(angleRadians);
            return new ComplexDecimal(real, imaginary);
        }

        /// <summary>
        /// Ограничивает десятичное значение заданным минимальным и максимальным диапазоном.
        /// </summary>
        /// <param name="value">Десятичное значение для ограничения.</param>
        /// <param name="min">Минимально допустимое значение.</param>
        /// <param name="max">Максимально допустимое значение.</param>
        /// <returns>Ограниченное десятичное значение.</returns>
        private decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        #endregion


        #region Static Methods

        /// <summary>
        /// Возводит комплексное число в комплексную степень с высокой точностью.
        /// Формула: z^p = exp(p * log(z))
        /// </summary>
        /// <param name="z">Основание.</param>
        /// <param name="p">Показатель степени.</param>
        /// <returns>Результат возведения в степень.</returns>
        public static ComplexDecimal Pow(ComplexDecimal z, ComplexDecimal p)
        {
            // Обработка простых случаев для скорости и точности
            if (p == Zero) return One;
            if (p == One) return z;
            if (z == Zero) return Zero;

            // 1. Вычисляем log(z) = ln|z| + i*arg(z)
            // Magnitude возвращает double, но его основа - MagnitudeSquared (decimal)
            decimal magnitude = DecimalMath.Sqrt(z.MagnitudeSquared);
            decimal argument = DecimalMath.Atan2(z.Imaginary, z.Real);
            decimal log_real = DecimalMath.Log(magnitude);
            decimal log_imag = argument;

            // 2. Умножаем p * log(z)
            // (p.Re + i*p.Im) * (log_real + i*log_imag)
            decimal product_real = p.Real * log_real - p.Imaginary * log_imag;
            decimal product_imag = p.Real * log_imag + p.Imaginary * log_real;

            // 3. Вычисляем exp(product) = e^(product_real) * (cos(product_imag) + i*sin(product_imag))
            decimal exp_product_real = DecimalMath.Exp(product_real);

            decimal final_real = exp_product_real * DecimalMath.Cos(product_imag);
            decimal final_imag = exp_product_real * DecimalMath.Sin(product_imag);

            return new ComplexDecimal(final_real, final_imag);
        }

        #endregion



        #region Overrides

        /// <summary>
        /// Определяет, равен ли указанный объект текущему объекту.
        /// </summary>
        /// <param name="obj">Объект, который требуется сравнить с текущим объектом.</param>
        /// <returns>True, если указанный объект равен текущему; в противном случае False.</returns>
        public override bool Equals(object obj)
        {
            if (obj is ComplexDecimal other)
            {
                return this == other;
            }
            return false;
        }

        /// <summary>
        /// Возвращает хэш-код для текущего экземпляра.
        /// </summary>
        /// <returns>Хэш-код для текущего объекта.</returns>
        public override int GetHashCode()
        {
            // Используем XOR для объединения хэш-кодов действительной и мнимой частей.
            return Real.GetHashCode() ^ Imaginary.GetHashCode();
        }

        /// <summary>
        /// Возвращает строковое представление текущего комплексного числа.
        /// </summary>
        /// <returns>Строка, представляющая текущее комплексное число (например, "1.23 + 4.56i").</returns>
        public override string ToString()
        {
            // Форматируем вывод в виде "Real + Imaginary i".
            return $"{Real} + {Imaginary}i";
        }

        #endregion
    }
}
