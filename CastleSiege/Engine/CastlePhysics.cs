using CastleSiege.Models;

namespace CastleSiege.Engine;

// Результат расчёта урона взрыва по блокам замка
public sealed class ExplosionDamageResult
{
    public List<Block> DamagedBlocks { get; } = new List<Block>();
    public bool ShieldAbsorbedDirectHit { get; set; }
    public bool DirectKingDestroyed { get; set; }
}

// Чистая физика оседания колонн и взрывного урона по блокам
public static class CastlePhysics
{
    public const float SettlingSpeed = 260f;

    public static bool ComputeDropTargets(List<Block> blocks)
    {
        bool anyWillFall = false;
        foreach (var side in new PlayerId[] { PlayerId.Blue, PlayerId.Red })
        {
            for (int col = 0; col < LevelData.GridCols; col++)
            {
                var colBlocks = new List<Block>();
                foreach (var b in blocks)
                {
                    if (!b.IsDestroyed &&
                        b.Owner == side &&
                        b.ColumnIndex == col)
                    {
                        colBlocks.Add(b);
                    }
                }

                colBlocks.Sort((a, b) => b.Y.CompareTo(a.Y));
                float floorY = LevelData.BaseGroundY;

                foreach (var b in colBlocks)
                {
                    b.TargetY = floorY - b.Height;
                    if (b.TargetY - b.Y > 0.5f) anyWillFall = true;
                    floorY = b.TargetY;
                }
            }
        }
        return anyWillFall;
    }

    public static bool StepSettling(List<Block> blocks, float dt)
    {
        bool stillMoving = false;
        foreach (var block in blocks)
        {
            if (block.IsDestroyed || block.Y >= block.TargetY - 0.5f)
            {
                continue;
            }

            block.Y = MathF.Min(block.TargetY, block.Y + SettlingSpeed * dt);
            if (block.Y < block.TargetY - 0.5f)
            {
                stillMoving = true;
            }
            else
            {
                block.Y = block.TargetY;
            }
        }
        return stillMoving;
    }

    public static ExplosionDamageResult ApplyExplosionDamage(
        List<Block> blocks,
        float explosionX,
        float explosionY,
        float explosionRadius,
        int directDamage,
        int splashDamage,
        Block? directBlock)
    {
        var result = new ExplosionDamageResult();

        // Король получает урон только от прямого попадания
        if (directBlock != null)
        {
            bool wasShielded = directBlock.IsKing && directBlock.HasShield;
            if (directBlock.TakeDamage(directDamage))
            {
                result.DamagedBlocks.Add(directBlock);
                if (wasShielded)
                {
                    result.ShieldAbsorbedDirectHit = true;
                }
                else if (directBlock.IsKing && directBlock.IsDestroyed)
                {
                    result.DirectKingDestroyed = true;
                }
            }
        }

        float radiusSq = explosionRadius * explosionRadius;
        foreach (var block in blocks)
        {
            if (block.IsDestroyed || block.IsKing || block == directBlock)
            {
                continue;
            }

            float dx = block.Center.X - explosionX;
            float dy = block.Center.Y - explosionY;
            if (dx * dx + dy * dy <= radiusSq &&
                block.TakeDamage(splashDamage))
            {
                result.DamagedBlocks.Add(block);
            }
        }

        return result;
    }
}
