# Атласы трёхмерных режимов WPF

Учёт по `Fractal3DKind`: **31 из 41 режима имеют атлас**, **10 пока без отдельного
атласа**. Пресеты и внутренние семейства одного режима считаются вместе.
У части оставшихся режимов уже есть редакторы правил, рандомайзеры или поиск форм.
Для живых симуляций сравнение потребует отдельных пробных запусков с одинаковыми
начальными условиями и временем наблюдения.

## Реализованы · 31

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
| Гиперболические соты | `HyperbolicHoneycombAtlasWindow` |
| ABC-поток | `AbcFlowAtlasWindow` |
| Губка Менгера | `CubeRuleAtlasWindow` |
| Виксек | `CubeRuleAtlasWindow` |
| Пыль Кантора | `CubeRuleAtlasWindow` |
| Аполлонова упаковка сфер | `ApollonianPackingAtlasWindow` — поколения и окраска |
| IFS 3D | `Ifs3DAtlasWindow` — выбранное преобразование |
| Fractal Flame 3D | `Flame3DAtlasWindow` — выбранный слой |
| Странные аттракторы | `Attractor3DAtlasWindow` — коэффициенты, шаг и начальная точка |
| DLA 3D | `Dla3DAtlasWindow` — затравка и параметры роста |
| L-системы 3D | `LSystem3DAtlasWindow` — параметры применённой грамматики |
| Буддаброт 4D | `Buddhabrot4DAtlasWindow` — проекции, повороты, орбиты и RGB |
| Фигуры Лихтенберга 3D | `Lichtenberg3DAtlasWindow` — ветвление, электроды, сетка и seed |

## Живые симуляции · 10

Остались только эти десять режимов. Следующий кандидат — Gray–Scott 3D;
сравнение должно использовать одинаковые начальные условия и время наблюдения.

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
