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

