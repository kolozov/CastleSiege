using System.Drawing;
using CastleSiege.Engine;
using CastleSiege.Models;

namespace CastleSiege.Tests;

// Тесты баллистики, спецснарядов, ящиков, сплеша и разрушения рельефа
public class CombatAndTerrainTests
{
    private static void WaitUntilTurnEnds(
        GameEngine engine,
        int maxSteps = 600)
    {
        int steps = 0;
        while (steps < maxSteps
            && engine.Phase != GamePhase.Aiming
            && engine.Phase != GamePhase.GameOver)
        {
            engine.Update(0.02f);
            steps++;
        }

        bool settled = engine.Phase == GamePhase.Aiming
            || engine.Phase == GamePhase.GameOver;
        Assert.True(
            settled,
            $"Ход не завершился за {maxSteps} шагов (фаза {engine.Phase}).");
    }

    [Fact]
    public void Terrain_CarvesCraterButProtectsCatapultPlatforms()
    {
        using var level = LevelData.Create(LocationId.MountainPass);
        int centerX = LevelData.WorldWidth / 2;

        float peakBefore = level.TerrainY[centerX];
        level.CarveCrater(centerX, peakBefore, 45f);
        Assert.True(level.TerrainY[centerX] > peakBefore + 20f);

        int blueX = Convert.ToInt32(level.BlueCatapult.X);
        int redX = Convert.ToInt32(level.RedCatapult.X);
        int groundY = Convert.ToInt32(LevelData.BaseGroundY);

        level.CarveCrater(blueX, LevelData.BaseGroundY, 25f);
        level.CarveCrater(redX, LevelData.BaseGroundY, 25f);
        Assert.True(level.IsSolid(blueX, groundY));
        Assert.True(level.IsSolid(redX, groundY));
        Assert.Equal(LevelData.BaseGroundY, level.TerrainY[blueX]);
        Assert.Equal(LevelData.BaseGroundY, level.TerrainY[redX]);
    }
}
