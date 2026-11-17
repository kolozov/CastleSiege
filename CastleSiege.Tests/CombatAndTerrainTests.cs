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

    [Fact]
    public void ClusterProjectile_SplitsOnceAndBlocksSecondSplit()
    {
        using var engine = new GameEngine();
        engine.StartNewGame(
            LevelData.CreateFlat(createBitmap: false),
            spawnCrates: false);

        Assert.True(engine.TrySelectProjectile(ProjectileType.Cluster));
        PointF anchor = engine.CurrentCatapultAnchor;
        Assert.True(engine.TryBeginAim(anchor));
        Assert.True(engine.ReleaseAim(
            new PointF(anchor.X - 60f, anchor.Y + 60f)));

        Assert.False(engine.Projectiles[0].CanSplit);
        Assert.False(engine.TrySplitCluster());

        int steps = 0;
        while (!engine.Projectiles[0].CanSplit && steps < 100)
        {
            engine.Update(0.02f);
            steps++;
        }

        Assert.True(engine.Projectiles[0].CanSplit);
        Assert.True(engine.TrySplitCluster());
        Assert.Equal(3, engine.Projectiles.Count(p => p.IsAlive));

        Assert.False(engine.TrySplitCluster());
        Assert.Equal(3, engine.Projectiles.Count(p => p.IsAlive));

        WaitUntilTurnEnds(engine);
        Assert.Equal(GamePhase.Aiming, engine.Phase);
        Assert.Equal(PlayerId.Red, engine.CurrentPlayer);
    }

    [Fact]
    public void RicochetProjectile_OnSurfaceHit_BouncesTwiceAndExplodesOnThird()
    {
        var direct = new RicochetProjectile(
            PlayerId.Blue, 0f, 0f, 100f, 100f);

        Assert.False(direct.OnSurfaceHit(0f, -1f));
        Assert.Equal(1, direct.BouncesRemaining);
        Assert.Equal(-78f, direct.Vy, 2);

        direct.Vy = 100f;
        Assert.False(direct.OnSurfaceHit(0f, -1f));
        Assert.Equal(0, direct.BouncesRemaining);
        Assert.Equal(-78f, direct.Vy, 2);

        Assert.True(direct.OnSurfaceHit(0f, -1f));
    }

    [Fact]
    public void RicochetBomb_InEngine_ExplodesAfterTwoBouncesAndEndsTurn()
    {
        using var engine = new GameEngine();
        engine.StartNewGame(
            LevelData.CreateFlat(createBitmap: false),
            spawnCrates: false);

        Assert.True(engine.TrySelectProjectile(ProjectileType.Ricochet));
        PointF anchor = engine.CurrentCatapultAnchor;
        Assert.True(engine.TryBeginAim(anchor));
        Assert.True(engine.ReleaseAim(
            new PointF(anchor.X - 20f, anchor.Y + 80f)));

        var ricochet = Assert.IsType<RicochetProjectile>(
            engine.Projectiles[0]);

        int minBounces = 2;
        bool sawExplosion = false;
        for (int i = 0; i < 600 && engine.Phase == GamePhase.Flying; i++)
        {
            engine.Update(0.016f);
            if (ricochet.IsAlive && ricochet.BouncesRemaining < minBounces)
                minBounces = ricochet.BouncesRemaining;
            if (engine.Explosions.Any(
                e => e.MaxRadius >= ricochet.ExplosionRadius))
            {
                sawExplosion = true;
            }
        }

        Assert.Equal(0, minBounces);
        Assert.False(ricochet.IsAlive);
        Assert.True(sawExplosion);
        WaitUntilTurnEnds(engine);
        Assert.Equal(GamePhase.Aiming, engine.Phase);
        Assert.Equal(PlayerId.Red, engine.CurrentPlayer);
    }
}
