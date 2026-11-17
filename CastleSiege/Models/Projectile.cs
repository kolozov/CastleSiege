using System.Drawing;

namespace CastleSiege.Models;

// Базовый класс снаряда с баллистикой и столкновениями
public abstract class Projectile
{
    private const int MaxTrailPoints = 28;
    private float _trailTimer;

    public PlayerId Owner { get; }
    public ProjectileType Type { get; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Vx { get; set; }
    public float Vy { get; set; }
    public float Radius { get; }
    public bool IsAlive { get; set; } = true;
    public float AgeSeconds { get; private set; }
    public List<PointF> Trail { get; } = new List<PointF>(MaxTrailPoints);

    public virtual int DirectDamage => 1;
    public virtual int SplashDamage => 1;
    public virtual float ExplosionRadius => 23f;
    public virtual bool CanSplit => false;

    protected Projectile(
        PlayerId owner,
        ProjectileType type,
        float x, float y,
        float vx, float vy,
        float radius)
    {
        Owner = owner;
        Type = type;
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Radius = radius;
        Trail.Add(new PointF(x, y));
    }

    // Продвигает снаряд на шаг времени и добавляет точку в след
    public void Step(float dt, float gravity)
    {
        Vy += gravity * dt;
        X += Vx * dt;
        Y += Vy * dt;
        AgeSeconds += dt;
        _trailTimer += dt;
        if (_trailTimer >= 0.018f)
        {
            _trailTimer = 0f;
            if (Trail.Count >= MaxTrailPoints) Trail.RemoveAt(0);
            Trail.Add(new PointF(X, Y));
        }
    }

    // Возвращает true при взрыве и false при отскоке
    public virtual bool OnSurfaceHit(float nx, float ny) => true;

    // Делит кассетный снаряд на осколки
    public virtual bool TrySplit(out List<Projectile> children)
    {
        children = new List<Projectile>();
        return false;
    }
}

// Обычное ядро
public sealed class StandardProjectile : Projectile
{
    public StandardProjectile(
        PlayerId owner,
        float x, float y,
        float vx, float vy)
        : base(owner, ProjectileType.Standard, x, y, vx, vy, 7.5f) { }
}

// Кассетный снаряд: разделяется на 3 мини-ядра
public sealed class ClusterProjectile : Projectile
{
    public bool HasSplit { get; private set; }
    public override bool CanSplit =>
        !HasSplit && IsAlive && AgeSeconds >= 0.08f;

    public ClusterProjectile(
        PlayerId owner,
        float x, float y,
        float vx, float vy)
        : base(owner, ProjectileType.Cluster, x, y, vx, vy, 8.5f) { }

    public override bool TrySplit(out List<Projectile> children)
    {
        if (!CanSplit)
        {
            children = new List<Projectile>();
            return false;
        }

        HasSplit = true;
        IsAlive = false;
        float[] angles = new float[] { 0f, -0.24f, 0.24f };
        float[] scales = new float[] { 1.02f, 0.96f, 0.96f };
        children = new List<Projectile>(3);

        for (int i = 0; i < angles.Length; i++)
        {
            float c = MathF.Cos(angles[i]);
            float s = MathF.Sin(angles[i]);
            float childVx = (Vx * c - Vy * s) * scales[i];
            float childVy = (Vx * s + Vy * c) * scales[i];
            children.Add(new ClusterChildProjectile(
                Owner, X, Y, childVx, childVy));
        }
        return true;
    }
}

// Осколок кассетного снаряда
public sealed class ClusterChildProjectile : Projectile
{
    public override float ExplosionRadius => 18f;

    public ClusterChildProjectile(
        PlayerId owner,
        float x, float y,
        float vx, float vy)
        : base(owner, ProjectileType.Cluster, x, y, vx, vy, 5.5f) { }
}

