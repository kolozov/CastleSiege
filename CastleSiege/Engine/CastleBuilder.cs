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
    public int WoodBudget { get; private set; } = 6;
    public int StoneBudget { get; private set; } = 4;
    public int SteelBudget { get; private set; } = 2;

    public BlockType?[,] BlueGrid { get; } =
        new BlockType?[GridCols, GridRows];

    public BlockType?[,] RedGrid { get; } =
        new BlockType?[GridCols, GridRows];

    public int TotalBudget => WoodBudget + StoneBudget + SteelBudget;

    public CastleBuilder() { }

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
        // Опускание верхних блоков добавим следующим шагом.
        message = "Блок возвращён в запас.";
        return true;
    }

    private static bool Fail(out string message, string text)
    {
        message = text;
        return false;
    }
}
