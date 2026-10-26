# Атласы трёхмерных режимов WPF

Учёт по `Fractal3DKind`: **18 из 41 режима имеют атлас**, **23 пока без отдельного
атласа**. Пресеты и внутренние семейства одного режима считаются вместе.
У части оставшихся режимов уже есть редакторы правил, рандомайзеры или поиск форм.
Для живых симуляций сравнение потребует отдельных пробных запусков с одинаковыми
начальными условиями и временем наблюдения.

## Реализованы · 18

| Режим | Атлас |
|---|---|
| Мандельбульб | `MandelbulbAtlasWindow` |
| Жюлиабульб | `JuliabulbAtlasWindow` |
| Мандельбокс | `MandelboxAtlasWindow` |
| Гибрид бульба и бокса | `BulbBoxHybridAtlasWindow` |
| KIFS | `KifsAtlasWindow` |
| Тетраэдр Серпинского | `SierpinskiAtlasWindow` |
| Периодические поверхности | `PeriodicSurfaceAtlasWindow` |
| Неявные поверхности | `ImplicitSurfaceAtlasWindow` |
| Кватернионное Жюлиа | `QuaternionJuliaAtlasWindow` |
| Phoenix 3D | `Phoenix3DAtlasWindow` |
| Ньютон 3D | `Newton3DAtlasWindow` |
| Ляпунов 3D | `Lyapunov3DAtlasWindow` |
| Группы Клейна | `KleinianAtlasWindow` |
| Ожерелье Антуана | `AntoineAtlasWindow` |
| Ландшафты | `TerrainAtlasWindow` |
| Горящий корабль 3D | `BurningShipAtlasWindow` |
| Горящий корабль 3D — Жюлиа | `BurningShipJuliaAtlasWindow` |
| Расслоение Хопфа | `HopfAtlasWindow` |

## Следующие кандидаты · 13

| Режим | Ключ |
|---|---|
| Гиперболические соты | `HyperbolicHoneycomb` |
| ABC-поток | `AbcFlow` |
| Губка Менгера | `MengerSponge` |
| Виксек | `Vicsek` |
| Пыль Кантора | `CantorDust` |
| Аполлонова упаковка сфер | `ApollonianPacking` |
| IFS 3D | `Ifs3D` |
| Fractal Flame 3D | `Flame3D` |
| Странные аттракторы | `StrangeAttractor` |
| DLA 3D | `Dla3D` |
| L-системы 3D | `LSystem3D` |
| Буддаброт 4D | `Buddhabrot4D` |
| Фигуры Лихтенберга 3D | `Lichtenberg3D` |

Для каждого кандидата отдельно выбирается подходящий способ сравнения:
числовые параметры, поколения, правила или результаты генерации.
Ближайшая предлагаемая очередь: гиперболические соты → ABC-поток.

## Живые симуляции · 10

| Режим | Ключ |
|---|---|
| Gray–Scott 3D | `GrayScott3D` |
| Тьюринг 3D | `Turing3D` |
| Кан–Хиллиард 3D | `CahnHilliard3D` |
| Physarum 3D | `Physarum3D` |
| Lenia 3D | `Lenia3D` |
| Кобаяси 3D | `Kobayashi3D` |
| Возбудимая среда 3D | `Excitable3D` |
| Гинзбург–Ландау 3D | `GinzburgLandau3D` |
| Свифт–Хоэнберг 3D | `SwiftHohenberg3D` |
| Фазовое поле кристалла 3D | `PhaseFieldCrystal3D` |

Основные источники для сверки: `Models/Fractal3DKind.cs`, реестр
`Models/Fractal3DCatalog.cs`, адаптеры `Views/*AtlasWindow.cs` и
группы проверок в [Verification/README.md](Verification/README.md).
