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

    private static void ShootToPoint(
        GameEngine engine,
        PointF target,
        float flightTime)
    {
        PointF anchor = engine.CurrentCatapultAnchor;
        float vx = (target.X - anchor.X) / flightTime;
        float vy = (target.Y - (anchor.Y - 6f)
            - 0.5f * LevelData.Gravity * flightTime * flightTime) / flightTime;
        float scale = GameEngine.MaxPullRadius / GameEngine.MaxLaunchSpeed;

        Assert.True(engine.TryBeginAim(anchor));
        Assert.True(engine.ReleaseAim(
            new PointF(anchor.X - vx * scale, anchor.Y - vy * scale)));
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
    public void Aiming_RejectsInvalidShotsAndPreservesAmmoOnCancel()
    {
        using var engine = new GameEngine();
        engine.StartNewGame(
            LevelData.CreateFlat(createBitmap: false),
            spawnCrates: false);

        Assert.False(engine.TryBeginAim(engine.Level.RedCatapult));

        int ammoBefore = engine.GetAmmo(PlayerId.Blue, ProjectileType.Cluster);
        Assert.True(engine.TrySelectProjectile(ProjectileType.Cluster));

        PointF anchor = engine.CurrentCatapultAnchor;
        Assert.True(engine.TryBeginAim(anchor));
        Assert.False(engine.ReleaseAim(
            new PointF(anchor.X - 5f, anchor.Y)));
        Assert.Equal(
            ammoBefore,
            engine.GetAmmo(PlayerId.Blue, ProjectileType.Cluster));

        Assert.True(engine.TryBeginAim(anchor));
        Assert.False(engine.ReleaseAim(
            new PointF(anchor.X + 80f, anchor.Y + 20f)));

        Assert.True(engine.TryBeginAim(anchor));
        engine.UpdateAim(new PointF(anchor.X - 60f, anchor.Y + 40f));
        engine.CancelAim();
        Assert.False(engine.IsAiming);
        Assert.Equal(GamePhase.Aiming, engine.Phase);
        Assert.Empty(engine.Projectiles);
        Assert.Equal(
            ammoBefore,
            engine.GetAmmo(PlayerId.Blue, ProjectileType.Cluster));
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

    [Fact]
    public void DirectProjectileHit_DestroysEnemyKingAndEndsGame()
    {
        var blue = new BlockType?[LevelData.GridCols, LevelData.GridRows];
        var red = new BlockType?[LevelData.GridCols, LevelData.GridRows];
        blue[1, 0] = BlockType.King;
        red[2, 0] = BlockType.King;

        using var engine = new GameEngine();
        engine.StartNewGame(
            LevelData.CreateFlat(blue, red, createBitmap: false),
            spawnCrates: false);

        Block redKing = engine.Level.Blocks.First(
            b => b.Owner == PlayerId.Red && b.IsKing);
        ShootToPoint(engine, redKing.Center, 1.6f);
        WaitUntilTurnEnds(engine);

        Assert.True(redKing.IsDestroyed);
        Assert.Equal(GamePhase.GameOver, engine.Phase);
        Assert.Equal(PlayerId.Blue, engine.Winner);
    }

    [Fact]
    public void CrateHit_ConsumesProjectileWithoutExplosion()
    {
        LevelData level = LevelData.CreateFlat(createBitmap: false);
        CrateSlot slot = level.CrateSlots[0];
        slot.ActiveCrate = new Crate(slot.Position, false, 0f);

        using var engine = new GameEngine();
        engine.StartNewGame(level, spawnCrates: false);

        ShootToPoint(engine, slot.Position, 0.4f);
        WaitUntilTurnEnds(engine);

        Assert.Null(slot.ActiveCrate);
        Assert.Empty(engine.Explosions);
        Assert.Equal(GamePhase.Aiming, engine.Phase);
        Assert.Equal(PlayerId.Red, engine.CurrentPlayer);
    }

    [Fact]
    public void ExplosionSplash_DamagesAdjacentWallButProtectsKing()
    {
        var blue = new BlockType?[LevelData.GridCols, LevelData.GridRows];
        var red = new BlockType?[LevelData.GridCols, LevelData.GridRows];
        blue[1, 0] = BlockType.King;
        red[0, 0] = BlockType.Steel;
        red[1, 0] = BlockType.King;

        using var level = LevelData.CreateFlat(blue, red, createBitmap: false);
        Block steelWall = level.Blocks.First(
            b => b.Owner == PlayerId.Red && b.Type == BlockType.Steel);
        Block redKing = level.Blocks.First(
            b => b.Owner == PlayerId.Red && b.IsKing);

        float midX = (steelWall.Center.X + redKing.Center.X) / 2f;
        float midY = redKing.Center.Y;
        ExplosionDamageResult result = CastlePhysics.ApplyExplosionDamage(
            level.Blocks, midX, midY, 42f, 2, 2, null);

        Assert.Contains(steelWall, result.DamagedBlocks);
        Assert.DoesNotContain(redKing, result.DamagedBlocks);
        Assert.Equal(steelWall.MaxHp - 2, steelWall.Hp);
        Assert.False(redKing.IsDestroyed);
        Assert.Equal(redKing.MaxHp, redKing.Hp);
    }
}
