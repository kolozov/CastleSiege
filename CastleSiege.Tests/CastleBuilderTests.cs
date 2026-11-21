using CastleSiege.Engine;
using CastleSiege.Models;

namespace CastleSiege.Tests;

// Тесты конструктора замка, бюджета, защиты Короля, щита и ремонта
public class CastleBuilderTests
{
    private static void ResetBudgets(CastleBuilder builder)
    {
        builder.TryChangeBudget(BlockType.Wood, -builder.WoodBudget, out _);
        builder.TryChangeBudget(BlockType.Stone, -builder.StoneBudget, out _);
        builder.TryChangeBudget(BlockType.Steel, -builder.SteelBudget, out _);
    }

    [Fact]
    public void Budget_EnforcesExact15BlockLimit()
    {
        var builder = new CastleBuilder();
        ResetBudgets(builder);

        bool woodOk = builder.TryChangeBudget(BlockType.Wood, 12, out _);
        bool exact15Ok = builder.TryChangeBudget(BlockType.Stone, 3, out _);
        bool over16 = builder.TryChangeBudget(BlockType.Steel, 1, out _);

        Assert.True(woodOk);
        Assert.True(exact15Ok);
        Assert.False(over16);
        Assert.Equal(12, builder.WoodBudget);
        Assert.Equal(3, builder.StoneBudget);
        Assert.Equal(0, builder.SteelBudget);
    }

    [Fact]
    public void BattleValidation_FailsWithoutKings()
    {
        var builder = new CastleBuilder();
        builder.ClearGrids();

        bool canStart = builder.TryValidateForBattle(out _, out _);

        Assert.False(canStart);
    }

    [Fact]
    public void King_SurvivesRoofAndBlockCollapseWithIntactHp()
    {
        var blue = new BlockType?[LevelData.GridCols, LevelData.GridRows];
        var red = new BlockType?[LevelData.GridCols, LevelData.GridRows];
        blue[1, 0] = BlockType.King;
        red[1, 0] = BlockType.Stone;
        red[1, 1] = BlockType.King;
        red[1, 2] = BlockType.Stone;

        using var level = LevelData.CreateFlat(blue, red, createBitmap: false);

        Block redKing = level.Blocks.First(
            b => b.Owner == PlayerId.Red && b.IsKing);
        Block roofBlock = level.Blocks.First(
            b => b.Owner == PlayerId.Red
                && b.ColumnIndex == 1
                && b.Y < redKing.Y);
        Block floorUnderKing = level.Blocks.First(
            b => b.Owner == PlayerId.Red
                && b.ColumnIndex == 1
                && b.Y > redKing.Y);

        int kingHpBefore = redKing.Hp;
        floorUnderKing.TakeDamage(10);

        Assert.True(CastlePhysics.ComputeDropTargets(level.Blocks));

        bool stillMoving = true;
        int steps = 0;
        while (stillMoving && steps < 300)
        {
            stillMoving = CastlePhysics.StepSettling(level.Blocks, 0.02f);
            steps++;
        }

        Assert.False(stillMoving);
        Assert.False(redKing.IsDestroyed);
        Assert.Equal(kingHpBefore, redKing.Hp);
        Assert.Equal(LevelData.BaseGroundY - redKing.Height, redKing.Y, 1);
        Assert.Equal(redKing.Y - roofBlock.Height, roofBlock.Y, 1);
    }

    [Fact]
    public void KingShield_AbsorbsExactlyOneHit()
    {
        var king = new Block(
            PlayerId.Red,
            BlockType.King,
            1, 0f, 0f,
            LevelData.BlockWidth,
            LevelData.BlockHeight);
        king.HasShield = true;

        bool firstHit = king.TakeDamage(1);
        Assert.True(firstHit);
        Assert.False(king.HasShield);
        Assert.False(king.IsDestroyed);

        bool secondHit = king.TakeDamage(1);
        Assert.True(secondHit);
        Assert.True(king.IsDestroyed);
    }

    [Fact]
    public void Block_RepairOneHp_HealsDamagedBlockWithinBounds()
    {
        var steel = new Block(
            PlayerId.Blue,
            BlockType.Steel,
            0, 0f, 0f,
            LevelData.BlockWidth,
            LevelData.BlockHeight);
        var king = new Block(
            PlayerId.Red,
            BlockType.King,
            1, 0f, 0f,
            LevelData.BlockWidth,
            LevelData.BlockHeight);

        Assert.False(steel.RepairOneHp());
        Assert.True(steel.TakeDamage(2));
        Assert.True(steel.RepairOneHp());
        Assert.Equal(steel.MaxHp - 1, steel.Hp);
        Assert.True(steel.RepairOneHp());
        Assert.Equal(steel.MaxHp, steel.Hp);
        Assert.False(steel.RepairOneHp());

        Assert.True(steel.TakeDamage(steel.MaxHp));
        Assert.False(steel.RepairOneHp());
        Assert.False(king.RepairOneHp());
    }

    [Fact]
    public void AutoFillFromCurrentBudget_FillsMirroredCastlesMatchingBudget()
    {
        var builder = new CastleBuilder();
        ResetBudgets(builder);
        Assert.True(builder.TryChangeBudget(BlockType.Wood, 6, out _));
        Assert.True(builder.TryChangeBudget(BlockType.Stone, 3, out _));
        Assert.True(builder.TryChangeBudget(BlockType.Steel, 2, out _));

        builder.AutoFillFromCurrentBudget();

        PlayerId[] sides = new PlayerId[] { PlayerId.Blue, PlayerId.Red };
        foreach (PlayerId side in sides)
        {
            Assert.Equal(1, builder.GetUsed(side, BlockType.King));
            Assert.Equal(
                builder.GetBudget(BlockType.Wood),
                builder.GetUsed(side, BlockType.Wood));
            Assert.Equal(
                builder.GetBudget(BlockType.Stone),
                builder.GetUsed(side, BlockType.Stone));
            Assert.Equal(
                builder.GetBudget(BlockType.Steel),
                builder.GetUsed(side, BlockType.Steel));
        }

        for (int c = 0; c < CastleBuilder.GridCols; c++)
        {
            for (int r = 0; r < CastleBuilder.GridRows; r++)
            {
                Assert.Equal(
                    builder.BlueGrid[c, r],
                    builder.RedGrid[CastleBuilder.GridCols - 1 - c, r]);
            }
        }
    }

    [Fact]
    public void TryRemoveBlock_CompactsColumnAndRestoresBudget()
    {
        var builder = new CastleBuilder();
        ResetBudgets(builder);
        Assert.True(builder.TryChangeBudget(BlockType.Wood, 1, out _));
        Assert.True(builder.TryChangeBudget(BlockType.Steel, 1, out _));

        builder.ClearGrids();
        builder.SelectedBrush = BlockType.Wood;
        Assert.True(builder.TryPlaceBlock(PlayerId.Blue, 0, 0, out _));
        int woodLeftBefore = builder.GetRemaining(
            PlayerId.Blue, BlockType.Wood);

        builder.SelectedBrush = BlockType.Steel;
        Assert.True(builder.TryPlaceBlock(PlayerId.Blue, 0, 1, out _));

        Assert.True(builder.TryRemoveBlock(PlayerId.Blue, 0, 0, out _));
        Assert.Equal(BlockType.Steel, builder.BlueGrid[0, 0]);
        Assert.Null(builder.BlueGrid[0, 1]);
        Assert.Equal(
            woodLeftBefore + 1,
            builder.GetRemaining(PlayerId.Blue, BlockType.Wood));
    }

    [Fact]
    public void TryPlaceBlock_WithKingBrushMovesUniqueKing()
    {
        var builder = new CastleBuilder();
        builder.ClearGrids();
        builder.SelectedBrush = BlockType.King;

        Assert.True(builder.TryPlaceBlock(PlayerId.Blue, 0, 0, out _));
        Assert.True(builder.TryPlaceBlock(PlayerId.Blue, 2, 0, out _));

        Assert.Null(builder.BlueGrid[0, 0]);
        Assert.Equal(BlockType.King, builder.BlueGrid[2, 0]);
        Assert.Equal(1, builder.GetUsed(PlayerId.Blue, BlockType.King));
    }
}
