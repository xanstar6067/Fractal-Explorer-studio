# Отражённые полиномы и конструктор гибридов WPF

Добавлены 46 формул и их динамические плоскости Жюлиа: 92 отдельных режима,
а также `Hybrid` и `JuliaHybrid`. В каждом семействе теперь 61 режим.
Каждый новый пункт имеет собственный ключ, категорию сохранений, два готовых вида
и вычисляемое превью. Карта C Жюлиа использует ту же формулу/последовательность.

Формулы сверены с [Kalles Fraktaler](https://mathr.co.uk/kf/manual.html#formulas).
В строках Cubic Partial BS справочника опущено `+ c`; здесь, как и в остальных
параметрических семействах, C добавляется после преобразования Z.
Классический WPF Burning Ship степени 2 сохраняет прежнюю ориентацию;
новые именованные степени 3–5 следуют записанному в справочнике `(abs(x)+i abs(y))^p+c`.
Гибрид с базовым Burning Ship использует прежнюю WPF-ориентацию с отрицательным `abs(y)`.

## Каталог формул

| Ключ параметрической плоскости | Название | Степень |
|---|---|---|
| `CubicBurningShip` | Cubic Burning Ship | 3 |
| `CubicBuffalo` | Cubic Buffalo | 3 |
| `CubicCeltic` | Cubic Celtic | 3 |
| `CubicMandelbar` | Cubic Mandelbar | 3 |
| `QuarticBurningShip` | Quartic Burning Ship | 4 |
| `QuarticBuffalo` | Quartic Buffalo | 4 |
| `QuarticCeltic` | Quartic Celtic | 4 |
| `QuarticMandelbar` | Quartic Mandelbar | 4 |
| `QuinticBurningShip` | Quintic Burning Ship | 5 |
| `QuinticBuffalo` | Quintic Buffalo | 5 |
| `QuinticCeltic` | Quintic Celtic | 5 |
| `QuinticMandelbar` | Quintic Mandelbar | 5 |
| `CubicPartialBurningShipReal` | Cubic Partial Burning Ship Real | 3 |
| `CubicPartialBurningShipImag` | Cubic Partial Burning Ship Imag | 3 |
| `CubicQuasiPerpendicular` | Cubic Quasi Perpendicular | 3 |
| `CubicCelticQuasiPerpendicular` | Cubic Celtic Quasi Perpendicular | 3 |
| `CubicQuasiPerpendicularBurningShip` | Cubic Quasi Perpendicular Burning Ship | 3 |
| `CubicQuasiPerpendicularBuffalo` | Cubic Quasi Perpendicular Buffalo | 3 |
| `QuarticPartialBurningShipImag` | Quartic Partial Burning Ship Imag | 4 |
| `QuarticPartialBurningShipReal` | Quartic Partial Burning Ship Real | 4 |
| `QuarticPartialBurningShipRealMandelbar` | Quartic Partial Burning Ship Real Mandelbar | 4 |
| `QuarticCelticPartialBurningShipImag` | Quartic Celtic Partial Burning Ship Imag | 4 |
| `QuarticCelticPartialBurningShipReal` | Quartic Celtic Partial Burning Ship Real | 4 |
| `QuarticCelticPartialBurningShipRealMandelbar` | Quartic Celtic Partial Burning Ship Real Mandelbar | 4 |
| `QuarticBuffaloPartialImag` | Quartic Buffalo Partial Imag | 4 |
| `QuarticCelticMandelbar` | Quartic Celtic Mandelbar | 4 |
| `QuarticFalseQuasiPerpendicular` | Quartic False Quasi Perpendicular | 4 |
| `QuarticFalseQuasiHeart` | Quartic False Quasi Heart | 4 |
| `QuarticCelticFalseQuasiPerpendicular` | Quartic Celtic False Quasi Perpendicular | 4 |
| `QuarticCelticFalseQuasiHeart` | Quartic Celtic False Quasi Heart | 4 |
| `QuarticImagQuasi` | Quartic Imag Quasi | 4 |
| `QuarticRealQuasiPerpendicular` | Quartic Real Quasi Perpendicular | 4 |
| `QuarticRealQuasiHeart` | Quartic Real Quasi Heart | 4 |
| `QuarticCelticImagQuasi` | Quartic Celtic Imag Quasi | 4 |
| `QuarticCelticRealQuasiPerpendicular` | Quartic Celtic Real Quasi Perpendicular | 4 |
| `QuarticCelticRealQuasiHeart` | Quartic Celtic Real Quasi Heart | 4 |
| `QuinticPartialBurningShipReal` | Quintic Partial Burning Ship Real | 5 |
| `QuinticPartialBurningShipRealMandelbar` | Quintic Partial Burning Ship Real Mandelbar | 5 |
| `QuinticCelticMandelbar` | Quintic Celtic Mandelbar | 5 |
| `QuinticQuasiBurningShip` | Quintic Quasi Burning Ship | 5 |
| `QuinticQuasiPerpendicular` | Quintic Quasi Perpendicular | 5 |
| `QuinticQuasiHeart` | Quintic Quasi Heart | 5 |
| `QuinticQuasiPerpendicularBurningShip` | Quintic Quasi Perpendicular Burning Ship | 5 |
| `QuinticQuasiPerpendicularBuffalo` | Quintic Quasi Perpendicular Buffalo | 5 |
| `QuinticCelticQuasiPerpendicular` | Quintic Celtic Quasi Perpendicular | 5 |
| `QuinticCelticQuasiHeart` | Quintic Celtic Quasi Heart | 5 |

Ключ Жюлиа — `Julia` перед ключом таблицы. Численные значения существующих
MandelbrotVariant сохранены; новые значения дописаны в конец.
Mothbrot, TheRedshiftRider и трансцендентные формулы в этот пакет не входят.

## Редактор гибрида

Строки исполняются сверху вниз, затем цикл повторяется. Каждый повтор — одна
итерация `z <- F(z)+c`, с отдельной проверкой выхода. В параметрической плоскости
z0=0 и c=пиксель; в Жюлиа z0=пиксель и c фиксирована.

- 1–16 строк, 1–32 повторов, не более 256 шагов в цикле.
- Базовым формулам можно задать целую степень 2–5; именованные новые формулы имеют фиксированную степень.
- «Добавить шаг», стрелки вверх/вниз и удаление меняют последовательность.
- Пустой цикл, рекурсивные гибриды, Julia-формулы в строках и неверные числа отклоняются.
- Сохранение и готовые виды хранят независимую копию последовательности.
- Карта C и миниатюра обновляются при смене последовательности; открытая карта получает её копию.

## Расчёт

`Core/Math/FoldedPolynomialProgram.cs` строит небольшой граф сложений,
умножений и модулей. Обычный double-расчёт и якобиан компилируются один раз
на формулу. BigFloat готовит опорную орбиту с адаптивной точностью по общему
планировщику, а FloatExp хранит узлы опоры и отклонения каждого пикселя.
Для умножения используются Ra*db+Rb*da+da*db, для модуля — разбор знаков,
без вычитания почти одинаковых значений. Промежуточные отражения поэтому
сохраняют значащие компоненты за границей диапазона double.

BLA использует общую вещественную таблицу 2×2. Лист ограничен безопасным
радиусом каждого модуля и верхней оценкой гессиана; таблица не применяется
к ловушкам, полосам и DE. Производные DE проходят через весь граф.
Кэш содержит до восьми опорных контекстов; орбита ограничена 16384 шагами,
после чего продолжается пертурбация от фазовой опоры. Новый код работает на ЦП.

Гибрид при смене опоры выбирает шаг той же фазы. При смешанных степенях
сглаживание использует сумму log(p) и средний log(p) цикла, поэтому не
подменяет последовательность последней степенью. Гистограмма и рельеф
сохраняют общий второй проход. Тайлы учитывают полную геометрию кадра,
DE считает дополнительное кольцо пикселей для совпадения границ тайлов.

## Проверки

`folded-polynomials` проверяет независимые раскрытые формулы, decimal и BigFloat,
конечные разности якобианов, стабильные отклонения, все семь окрасок,
границу Жюлиа до 1e1000 по прямому BigFloat, BLA, пресеты, WPF/JSON-сохранения,
редактор и передачу последовательности на карту C. Общая группа `mandelbrot-saves`
охватывает все 122 режима, а `catalog` — 205 плиток.
