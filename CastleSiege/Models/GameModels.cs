using System.Drawing;

namespace CastleSiege.Models;

// Идентификатор игрока
public enum PlayerId { Blue, Red }

// Материал блока замка или Король
public enum BlockType { Wood, Stone, Steel, King }

// Тип снаряда
public enum ProjectileType { Standard, Cluster, Ricochet }

// Текущая фаза игры
public enum GamePhase
{
    MainMenu,
    Builder,
    Aiming,
    Flying,
    Settling,
    GameOver
}

// Игровая локация
public enum LocationId { Plains, MountainPass, Fortress }

// Случайный эффект из сбитой коробки
public enum CrateEffectType
{
    KingShield,
    RepairCastle,
    ExtraAmmo,
    AimFogDebuff,
    SabotageDebuff
}

// Прямоугольный блок замка или Король
public sealed class Block
{
    public PlayerId Owner { get; }
    public BlockType Type { get; }
    public int ColumnIndex { get; }
    public float X { get; }
    public float Y { get; set; }
    public float TargetY { get; set; }
    public float Width { get; }
    public float Height { get; }
    public int Hp { get; private set; }
    public int MaxHp { get; }
    public bool HasShield { get; set; }

    public bool IsDestroyed => Hp <= 0;
    public bool IsKing => Type == BlockType.King;
    public RectangleF Bounds => new RectangleF(X, Y, Width, Height);
    public PointF Center => new PointF(X + Width / 2f, Y + Height / 2f);

    public Block(
        PlayerId owner,
        BlockType type,
        int columnIndex,
        float x,
        float y,
        float width,
        float height)
    {
        Owner = owner;
        Type = type;
        ColumnIndex = columnIndex;
        X = x;
        Y = y;
        TargetY = y;
        Width = width;
        Height = height;

        if (type == BlockType.Stone) MaxHp = 2;
        else if (type == BlockType.Steel) MaxHp = 4;
        else MaxHp = 1;

        Hp = MaxHp;
    }

    // Наносит урон блоку (у Короля со щитом сначала снимает щит)
    public bool TakeDamage(int amount)
    {
        if (IsDestroyed || amount <= 0) return false;
        if (IsKing && HasShield)
        {
            HasShield = false;
            return true;
        }
        Hp = Math.Max(0, Hp - amount);
        return true;
    }

    // Восстанавливает 1 HP повреждённому обычному блоку
    public bool RepairOneHp()
    {
        if (IsDestroyed || IsKing || Hp >= MaxHp) return false;
        Hp++;
        return true;
    }
}

// Активная коробка с бонусом или дебаффом
public sealed class Crate
{
    public PointF BasePosition { get; set; }
    public bool IsAirborne { get; }
    public float Radius => 18f;
    public float BobPhase { get; set; }

    public PointF CurrentPosition => IsAirborne
        ? new PointF(BasePosition.X,
            BasePosition.Y + MathF.Sin(BobPhase) * 6f)
        : BasePosition;

    public RectangleF Bounds
    {
        get
        {
            PointF center = CurrentPosition;
            return new RectangleF(
                center.X - Radius,
                center.Y - Radius,
                Radius * 2f,
                Radius * 2f);
        }
    }

    public Crate(
        PointF basePosition,
        bool isAirborne,
        float initialPhase = 0f)
    {
        BasePosition = basePosition;
        IsAirborne = isAirborne;
        BobPhase = initialPhase;
    }
}

// Фиксированная точка возможного появления коробки
public sealed class CrateSlot
{
    public PointF Position { get; }
    public bool IsAirborne { get; }
    public Crate? ActiveCrate { get; set; }
    public bool IsOccupied => ActiveCrate != null;

    public CrateSlot(PointF position, bool isAirborne)
    {
        Position = position;
        IsAirborne = isAirborne;
    }
}

// Кратковременный эффект взрыва
public sealed class ExplosionEffect
{
    public PointF Center { get; }
    public float MaxRadius { get; }
    public Color Color { get; }
    public float Elapsed { get; private set; }
    public float Duration { get; }
    public float Progress => Math.Clamp(Elapsed / Duration, 0f, 1f);
    public bool IsAlive => Elapsed < Duration;

    public ExplosionEffect(
        PointF center,
        float maxRadius,
        Color color,
        float duration = 0.36f)
    {
        Center = center;
        MaxRadius = maxRadius;
        Color = color;
        Duration = duration;
    }

    public void Update(float dt) => Elapsed += dt;
}

// Всплывающая надпись на поле боя
public sealed class FloatingText
{
    public string Text { get; }
    public float X { get; }
    public float Y { get; private set; }
    public Color Color { get; }
    public float Elapsed { get; private set; }
    public float Duration { get; }
    public float Progress => Math.Clamp(Elapsed / Duration, 0f, 1f);
    public bool IsAlive => Elapsed < Duration;

    public FloatingText(
        string text,
        PointF position,
        Color color,
        float duration = 1.6f)
    {
        Text = text;
        X = position.X;
        Y = position.Y;
        Color = color;
        Duration = duration;
    }

    public void Update(float dt)
    {
        Elapsed += dt;
        Y -= 28f * dt;
    }
}

// Физический осколок (щепка, камень, земля) с отскоком от рельефа
public sealed class DebrisParticle
{
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Vx { get; private set; }
    public float Vy { get; private set; }
    public float Size { get; }
    public float RotationDeg { get; private set; }
    public float AngularSpeed { get; private set; }
    public Color Color { get; }
    public float Elapsed { get; private set; }
    public float Duration { get; }
    public float Progress => Math.Clamp(Elapsed / Duration, 0f, 1f);
    public bool IsAlive => Elapsed < Duration;

    public DebrisParticle(
        float x,
        float y,
        float vx,
        float vy,
        float size,
        float angularSpeed,
        Color color,
        float duration = 1.15f)
    {
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Size = size;
        AngularSpeed = angularSpeed;
        Color = color;
        Duration = duration;
    }

    public void Update(float dt, float gravity, LevelData level)
    {
        Elapsed += dt;
        Vy += gravity * dt;
        X += Vx * dt;
        Y += Vy * dt;
        RotationDeg += AngularSpeed * dt;

        if (X >= 0f && X < LevelData.WorldWidth)
        {
            float ground = level.GetGroundY(X, Y);
            if (Y + Size * 0.5f < ground) return;
            Y = ground - Size * 0.5f;
            Vy = -MathF.Abs(Vy) * 0.36f;
            Vx *= 0.68f;
            AngularSpeed *= 0.6f;
        }
    }
}
