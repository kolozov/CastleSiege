using System.Drawing;
using CastleSiege.Models;

namespace CastleSiege.Engine;

// Логика конструктора уровня: локация, общий бюджет блоков и две сетки 4×4
public sealed class CastleBuilder
{
    public const int GridCols = LevelData.GridCols;
    public const int GridRows = LevelData.GridRows;
    public const int MaxTotalBlocks = GridCols * GridRows - 1;

    public const float CellW = 48f;
    public const float CellH = 40f;
    public const float CellGap = 3f;
    public const float BlueGridX = 92f;
    public const float GridBottomY = LevelData.BaseGroundY;

    public const float RedGridX = LevelData.WorldWidth - BlueGridX
        - (GridCols * CellW + (GridCols - 1) * CellGap);

    public LocationId SelectedLocation { get; set; } = LocationId.Plains;
    public BlockType SelectedBrush { get; set; } = BlockType.Wood;
    public int WoodBudget { get; private set; } = 7;
    public int StoneBudget { get; private set; } = 4;
    public int SteelBudget { get; private set; } = 2;

    public BlockType?[,] BlueGrid { get; } =
        new BlockType?[GridCols, GridRows];

    public BlockType?[,] RedGrid { get; } =
        new BlockType?[GridCols, GridRows];

    public int TotalBudget => WoodBudget + StoneBudget + SteelBudget;

    public CastleBuilder() => LoadLocationPreset(LocationId.Plains);

    public int GetBudget(BlockType type)
    {
        if (type == BlockType.Wood) return WoodBudget;
        if (type == BlockType.Stone) return StoneBudget;
        if (type == BlockType.Steel) return SteelBudget;
        return type == BlockType.King ? 1 : 0;
    }

    public BlockType?[,] GetGrid(PlayerId side) =>
        side == PlayerId.Blue ? BlueGrid : RedGrid;

    public int GetUsed(PlayerId side, BlockType type)
    {
        var grid = GetGrid(side);
        int count = 0;
        for (int c = 0; c < GridCols; c++)
        {
            for (int r = 0; r < GridRows; r++)
            {
                if (grid[c, r] == type) count++;
            }
        }
        return count;
    }

    public int GetRemaining(PlayerId side, BlockType type) =>
        Math.Max(0, GetBudget(type) - GetUsed(side, type));

    public bool TryChangeBudget(
        BlockType type,
        int delta,
        out string message)
    {
        if (type == BlockType.King)
        {
            return Fail(out message,
                "Король всегда ровно 1 у каждого замка.");
        }

        int next = GetBudget(type) + delta;
        if (next < 0)
        {
            return Fail(out message,
                "Количество блоков не может быть меньше 0.");
        }

        if (delta > 0 && TotalBudget + delta > MaxTotalBlocks)
        {
            return Fail(out message,
                $"Максимум {MaxTotalBlocks} блоков " +
                "(плюс 1 Король) для сетки 4×4!");
        }

        if (type == BlockType.Wood) WoodBudget = next;
        else if (type == BlockType.Stone) StoneBudget = next;
        else if (type == BlockType.Steel) SteelBudget = next;

        int blueRemoved = TrimExcessBlocks(BlueGrid, type, next);
        int redRemoved = TrimExcessBlocks(RedGrid, type, next);

        message = $"Бюджет обновлён ({TotalBudget}/{MaxTotalBlocks}).";
        if (blueRemoved > 0 || redRemoved > 0)
        {
            message += $" Удалено блоков: Синий — {blueRemoved}, " +
                $"Красный — {redRemoved}.";
        }
        return true;
    }

    // Загружает стандартный шаблон локации и подгоняет бюджеты под него
    public void LoadLocationPreset(LocationId location)
    {
        SelectedLocation = location;
        LevelData.CreateDefaultGrids(location, out var blue, out var red);
        for (int c = 0; c < GridCols; c++)
        {
            for (int r = 0; r < GridRows; r++)
            {
                BlueGrid[c, r] = blue[c, r];
                RedGrid[c, r] = red[c, r];
            }
        }
        WoodBudget = GetUsed(PlayerId.Blue, BlockType.Wood);
        StoneBudget = GetUsed(PlayerId.Blue, BlockType.Stone);
        SteelBudget = GetUsed(PlayerId.Blue, BlockType.Steel);
    }

    // Автоматически расставляет симметричные замки в пределах бюджета
    public void AutoFillFromCurrentBudget()
    {
        ClearGrids();
        BlueGrid[1, 0] = BlockType.King;
        int[] cols = new int[]
        {
            2, 1, 2, 0, 3, 0, 1, 2, 3, 0, 3, 1, 2, 0, 3
        };
        int[] rows = new int[]
        {
            0, 1, 1, 0, 0, 1, 2, 2, 1, 2, 2, 3, 3, 3, 3
        };

        var pool = new Queue<BlockType>();
        for (int i = 0; i < SteelBudget; i++) pool.Enqueue(BlockType.Steel);
        for (int i = 0; i < StoneBudget; i++) pool.Enqueue(BlockType.Stone);
        for (int i = 0; i < WoodBudget; i++) pool.Enqueue(BlockType.Wood);

        for (int i = 0; i < cols.Length && pool.Count > 0; i++)
        {
            BlueGrid[cols[i], rows[i]] = pool.Dequeue();
        }

        for (int c = 0; c < GridCols; c++) CompactColumn(BlueGrid, c);

        for (int c = 0; c < GridCols; c++)
        {
            for (int r = 0; r < GridRows; r++)
            {
                RedGrid[GridCols - 1 - c, r] = BlueGrid[c, r];
            }
        }
    }

    public void ClearGrids()
    {
        Array.Clear(BlueGrid);
        Array.Clear(RedGrid);
    }

    public static RectangleF GetCellRect(PlayerId side, int col, int row)
    {
        float startX = side == PlayerId.Blue ? BlueGridX : RedGridX;
        float x = startX + col * (CellW + CellGap);
        float y = GridBottomY - (row + 1) * CellH;
        return new RectangleF(x, y, CellW, CellH);
    }

    public bool TryHitTestCell(
        PointF worldPt,
        out PlayerId side,
        out int col,
        out int row)
    {
        PlayerId[] sides = new PlayerId[] { PlayerId.Blue, PlayerId.Red };
        foreach (var candidate in sides)
        {
            for (int c = 0; c < GridCols; c++)
            {
                for (int r = 0; r < GridRows; r++)
                {
                    if (GetCellRect(candidate, c, r).Contains(worldPt))
                    {
                        side = candidate;
                        col = c;
                        row = r;
                        return true;
                    }
                }
            }
        }
        side = PlayerId.Blue;
        col = -1;
        row = -1;
        return false;
    }

    public bool TryPlaceBlock(
        PlayerId side,
        int col,
        int row,
        out string message)
    {
        var grid = GetGrid(side);
        if (grid[col, row] != null)
        {
            return Fail(out message,
                "Ячейка уже занята (ПКМ — удалить блок).");
        }

        if (SelectedBrush == BlockType.King)
        {
            for (int c = 0; c < GridCols; c++)
            {
                for (int r = 0; r < GridRows; r++)
                {
                    if (grid[c, r] == BlockType.King)
                    {
                        grid[c, r] = null;
                        CompactColumn(grid, c);
                    }
                }
            }
        }
        else if (GetRemaining(side, SelectedBrush) <= 0)
        {
            string name = side == PlayerId.Blue ? "Синего" : "Красного";
            return Fail(out message,
                $"У {name} закончились блоки выбранного типа!");
        }

        int emptyRow = 0;
        while (emptyRow < GridRows && grid[col, emptyRow] != null)
        {
            emptyRow++;
        }

        if (emptyRow >= GridRows)
        {
            return Fail(out message,
                "Этот столбец полностью заполнен!");
        }

        grid[col, emptyRow] = SelectedBrush;
        if (SelectedBrush == BlockType.King)
        {
            message = side == PlayerId.Blue
                ? "Синий Король размещён."
                : "Красный Король размещён.";
        }
        else
        {
            message = "Блок установлен.";
        }
        return true;
    }

    public bool TryRemoveBlock(
        PlayerId side,
        int col,
        int row,
        out string message)
    {
        var grid = GetGrid(side);
        if (grid[col, row] == null)
        {
            return Fail(out message, "Клетка уже пуста.");
        }
        grid[col, row] = null;
        CompactColumn(grid, col);
        message = "Блок возвращён в запас.";
        return true;
    }

    public bool TryValidateForBattle(
        out string errorMessage,
        out string? unusedWarning)
    {
        unusedWarning = null;
        if (GetUsed(PlayerId.Blue, BlockType.King) != 1 ||
            GetUsed(PlayerId.Red, BlockType.King) != 1)
        {
            return Fail(out errorMessage,
                "У каждого замка обязательно должен быть размещён Король!");
        }

        int blueLeft = GetRemaining(PlayerId.Blue, BlockType.Wood) +
                       GetRemaining(PlayerId.Blue, BlockType.Stone) +
                       GetRemaining(PlayerId.Blue, BlockType.Steel);
        int redLeft = GetRemaining(PlayerId.Red, BlockType.Wood) +
                      GetRemaining(PlayerId.Red, BlockType.Stone) +
                      GetRemaining(PlayerId.Red, BlockType.Steel);

        if (blueLeft > 0 || redLeft > 0)
        {
            unusedWarning =
                "Использованы не все доступные блоки:\n" +
                $"• Синий замок — осталось: {blueLeft}\n" +
                $"• Красный замок — осталось: {redLeft}\n\n" +
                "Всё равно начать бой с текущими замками?";
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool Fail(out string message, string text)
    {
        message = text;
        return false;
    }

    private static void CompactColumn(BlockType?[,] grid, int col)
    {
        int writeRow = 0;
        for (int r = 0; r < GridRows; r++)
        {
            var cell = grid[col, r];
            if (cell != null)
            {
                grid[col, r] = null;
                grid[col, writeRow++] = cell.Value;
            }
        }
    }

    private static int TrimExcessBlocks(
        BlockType?[,] grid,
        BlockType type,
        int maxAllowed)
    {
        int count = 0;
        for (int c = 0; c < GridCols; c++)
        {
            for (int r = 0; r < GridRows; r++)
            {
                if (grid[c, r] == type) count++;
            }
        }

        int excess = Math.Max(0, count - maxAllowed);
        if (excess == 0) return 0;

        for (int r = GridRows - 1; r >= 0 && count > maxAllowed; r--)
        {
            for (int c = 0; c < GridCols && count > maxAllowed; c++)
            {
                if (grid[c, r] == type)
                {
                    grid[c, r] = null;
                    count--;
                }
            }
        }

        for (int c = 0; c < GridCols; c++) CompactColumn(grid, c);
        return excess;
    }
}
