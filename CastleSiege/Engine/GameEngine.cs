using System.Drawing;
using CastleSiege.Audio;
using CastleSiege.Models;

namespace CastleSiege.Engine;

// Параметры прицеливания перед выстрелом
public struct LaunchParams
{
    public float Vx;
    public float Vy;
    public float PullLen;
    public float AngleDeg;
    public int PowerPct;
}

// Основной движок игры: физика снарядов, коллизии,
// обрушение блоков и правила ходов
public sealed class GameEngine : IDisposable
{
    public const float MaxPullRadius = 115f;
    public const float MinPullRadius = 15f;
    public const float MaxLaunchSpeed = 760f;

    private const float GrabRadius = 75f;
    private const int MaxActiveCrates = 2;
    private const int CrateRespawnRounds = 3;

    private readonly Random _rng = new Random();
    private readonly SoundManager _sound;

    private readonly Dictionary<PlayerId, Dictionary<ProjectileType, int>>
        _ammo = new();

    private readonly Dictionary<PlayerId, ProjectileType>
        _selectedProjectile = new();

    private readonly Dictionary<PlayerId, bool> _aimFog = new();
    private readonly List<int> _crateRespawnRounds = new List<int>();

    private float _shakeTimer;
    private float _shakeDuration;
    private float _shakeIntensity;

    public CastleBuilder Builder { get; } = new CastleBuilder();
    public LevelData Level { get; private set; } = null!;
    public PlayerId CurrentPlayer { get; private set; }
    public GamePhase Phase { get; private set; }
    public PlayerId? Winner { get; private set; }
    public bool IsDraw { get; private set; }
    public int TurnNumber { get; private set; }
    public int RoundNumber { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;
    public float CloudTime { get; private set; }
    public bool IsAiming { get; private set; }
    public PointF AimDragPoint { get; private set; }
    public PointF CurrentShakeOffset { get; private set; }

    public List<Projectile> Projectiles { get; } =
        new List<Projectile>();

    public List<ExplosionEffect> Explosions { get; } =
        new List<ExplosionEffect>();

    public List<FloatingText> FloatingTexts { get; } =
        new List<FloatingText>();

    public List<DebrisParticle> Debris { get; } =
        new List<DebrisParticle>();

    public ProjectileType SelectedProjectile =>
        _selectedProjectile[CurrentPlayer];

    public bool IsCurrentPlayerFogged => _aimFog[CurrentPlayer];

    public PointF CurrentCatapultAnchor =>
        CurrentPlayer == PlayerId.Blue
            ? Level.BlueCatapult
            : Level.RedCatapult;

    public GameEngine() : this(new SoundManager()) { }

    public GameEngine(SoundManager sound)
    {
        _sound = sound;
        StartNewGame(LocationId.Plains);
        Phase = GamePhase.MainMenu;
        StatusMessage = "Добро пожаловать в «Осаду замка»!";
    }

    public void OpenMainMenu() =>
        EnterScreen(GamePhase.MainMenu, "Главное меню.");

    public void OpenBuilder() =>
        EnterScreen(
            GamePhase.Builder,
            "Конструктор: ЛКМ — поставить блок, ПКМ — убрать. " +
            "Настройте локацию и замки.");

    private void EnterScreen(GamePhase phase, string status)
    {
        ResetVisualLists();
        Level?.Dispose();
        Level = LevelData.Create(Builder.SelectedLocation);
        Phase = phase;
        StatusMessage = status;
    }

    public void PreviewBuilderLocation(LocationId location)
    {
        Builder.SelectedLocation = location;
        Level?.Dispose();
        Level = LevelData.Create(location);
        StatusMessage = $"Выбрана локация «{Level.Title}».";
    }

    public void StartBattleFromBuilder() =>
        InitializeBattle(LevelData.CreateWithCustomCastles(
            Builder.SelectedLocation,
            Builder.BlueGrid,
            Builder.RedGrid));

    public void StartNewGame(LocationId location) =>
        InitializeBattle(LevelData.Create(location));

    // Движок принимает владение level и освобождает его при смене или Dispose
    public void StartNewGame(LevelData level, bool spawnCrates = true) =>
        InitializeBattle(level, spawnCrates);

    private void InitializeBattle(LevelData level, bool spawnCrates = true)
    {
        if (!ReferenceEquals(Level, level)) Level?.Dispose();
        Level = level;
        CurrentPlayer = PlayerId.Blue;
        Phase = GamePhase.Aiming;
        Winner = null;
        IsDraw = false;
        TurnNumber = 1;
        RoundNumber = 1;
        ResetVisualLists();
        _crateRespawnRounds.Clear();

        foreach (PlayerId p in Enum.GetValues<PlayerId>())
        {
            _ammo[p] = new Dictionary<ProjectileType, int>
            {
                [ProjectileType.Standard] = int.MaxValue,
                [ProjectileType.Cluster] = 2,
                [ProjectileType.Ricochet] = 2
            };
            _selectedProjectile[p] = ProjectileType.Standard;
            _aimFog[p] = false;
        }

        if (spawnCrates)
        {
            for (int i = 0; i < MaxActiveCrates; i++)
            {
                TrySpawnOneCrate();
            }
        }

        StatusMessage =
            "Ход Синего замка: оттяните снаряд от катапульты.";
    }

    private void ResetVisualLists()
    {
        IsAiming = false;
        _shakeTimer = 0f;
        CurrentShakeOffset = PointF.Empty;
        Projectiles.Clear();
        Explosions.Clear();
        FloatingTexts.Clear();
        Debris.Clear();
    }

    public int GetAmmo(PlayerId player, ProjectileType type) =>
        _ammo[player][type];

    public bool TrySelectProjectile(ProjectileType type)
    {
        if (Phase != GamePhase.Aiming)
        {
            return RejectAction(
                "Нельзя менять снаряд во время выстрела " +
                "или после конца игры.");
        }

        if (_ammo[CurrentPlayer][type] <= 0)
        {
            return RejectAction("Снаряды этого типа закончились!");
        }

        _selectedProjectile[CurrentPlayer] = type;

        if (type == ProjectileType.Cluster)
        {
            StatusMessage =
                "Выбран кассетный снаряд " +
                "(клик или Пробел в полёте — разделение).";
        }
        else if (type == ProjectileType.Ricochet)
        {
            StatusMessage =
                "Выбрана бомба с рикошетом " +
                "(2 отскока, взрыв на 3-е касание).";
        }
        else
        {
            StatusMessage = "Выбрано обычное ядро.";
        }
        return true;
    }

    public bool TryBeginAim(PointF worldPt)
    {
        if (Phase == GamePhase.GameOver)
        {
            _sound.PlayError();
            return false;
        }

        if (Phase != GamePhase.Aiming)
        {
            return RejectAction("Подождите, пока замки осядут.");
        }

        float dx = worldPt.X - CurrentCatapultAnchor.X;
        float dy = worldPt.Y - CurrentCatapultAnchor.Y;
        if (dx * dx + dy * dy > GrabRadius * GrabRadius)
        {
            return RejectAction("Тяните мышью от своей катапульты!");
        }

        IsAiming = true;
        UpdateAim(worldPt);
        return true;
    }

    public void UpdateAim(PointF worldPt)
    {
        if (!IsAiming) return;
        PointF anchor = CurrentCatapultAnchor;
        float dx = worldPt.X - anchor.X;
        float dy = worldPt.Y - anchor.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist > MaxPullRadius && dist > 0.001f)
        {
            float scale = MaxPullRadius / dist;
            AimDragPoint = new PointF(
                anchor.X + dx * scale,
                anchor.Y + dy * scale);
        }
        else
        {
            AimDragPoint = worldPt;
        }
    }

    public void CancelAim()
    {
        if (!IsAiming) return;
        IsAiming = false;
        StatusMessage = "Прицеливание отменено.";
    }

    public bool ReleaseAim(PointF worldPt)
    {
        if (!IsAiming) return false;
        UpdateAim(worldPt);
        IsAiming = false;

        LaunchParams lp = ComputeLaunchParams(AimDragPoint);
        if (lp.PullLen < MinPullRadius)
        {
            return RejectAction(
                "Слишком слабое натяжение — выстрел отменён.");
        }

        if (lp.Vy > 80f)
        {
            return RejectAction(
                "Слишком низкий прицел — направьте выстрел выше!");
        }

        ProjectileType chosen = SelectedProjectile;
        if (_ammo[CurrentPlayer][chosen] <= 0)
        {
            return RejectAction("Снаряды этого типа закончились!");
        }

        if (chosen != ProjectileType.Standard)
        {
            _ammo[CurrentPlayer][chosen]--;
        }

        float sx = CurrentCatapultAnchor.X;
        float sy = CurrentCatapultAnchor.Y - 6f;

        Projectile proj = chosen switch
        {
            ProjectileType.Cluster =>
                new ClusterProjectile(
                    CurrentPlayer, sx, sy, lp.Vx, lp.Vy),
            ProjectileType.Ricochet =>
                new RicochetProjectile(
                    CurrentPlayer, sx, sy, lp.Vx, lp.Vy),
            _ =>
                new StandardProjectile(
                    CurrentPlayer, sx, sy, lp.Vx, lp.Vy)
        };

        Projectiles.Clear();
        Projectiles.Add(proj);
        Phase = GamePhase.Flying;
        _sound.PlayShoot();

        StatusMessage = chosen == ProjectileType.Cluster
            ? "Кассетный снаряд в воздухе! Клик или Пробел — разделить."
            : "Снаряд в полёте...";

        return true;
    }

    // Разделяет активный кассетный снаряд в полёте
    public bool TrySplitCluster()
    {
        if (Phase != GamePhase.Flying) return false;

        for (int i = 0; i < Projectiles.Count; i++)
        {
            var p = Projectiles[i];
            if (p is not ClusterProjectile || !p.IsAlive) continue;

            if (!p.TrySplit(out var children))
            {
                return RejectAction(
                    "Кассета только вылетела. Попробуйте чуть позже.");
            }

            Projectiles.AddRange(children);
            Explosions.Add(new ExplosionEffect(
                new PointF(p.X, p.Y),
                22f,
                Color.MediumPurple,
                0.22f));

            _sound.PlaySplit();
            StatusMessage = "Кассета раскрылась!";
            return true;
        }

        return RejectAction(
            "Сейчас в полёте нет кассеты, которую можно разделить.");
    }

    public LaunchParams ComputeLaunchParams(PointF dragPoint)
    {
        PointF anchor = CurrentCatapultAnchor;
        float dx = anchor.X - dragPoint.X;
        float dy = anchor.Y - dragPoint.Y;
        float pullLen = MathF.Sqrt(dx * dx + dy * dy);

        if (pullLen < 0.001f) return new LaunchParams();

        float clamped = Math.Min(pullLen, MaxPullRadius);
        float speed = (clamped / MaxPullRadius) * MaxLaunchSpeed;
        float vx = (dx / pullLen) * speed;
        float vy = (dy / pullLen) * speed;
        float forwardX = CurrentPlayer == PlayerId.Blue ? vx : -vx;

        return new LaunchParams
        {
            Vx = vx,
            Vy = vy,
            PullLen = pullLen,
            AngleDeg = MathF.Atan2(-vy, forwardX) * (180f / MathF.PI),
            PowerPct = Convert.ToInt32(
                MathF.Round((clamped / MaxPullRadius) * 100f))
        };
    }

    public bool ValidateBuilder(out string? unusedWarning)
    {
        if (Builder.TryValidateForBattle(out string error, out unusedWarning))
        {
            return true;
        }
        return RejectAction(error);
    }

    public void ChangeBuilderBudget(BlockType type, int delta)
    {
        if (Builder.TryChangeBudget(type, delta, out string message))
        {
            StatusMessage = message;
            _sound.PlayPlaceBlock();
        }
        else
        {
            RejectAction(message);
        }
    }

    public void LoadBuilderPreset()
    {
        Builder.LoadLocationPreset(Builder.SelectedLocation);
        StatusMessage = $"Загружен шаблон «{Level.Title}».";
        _sound.PlayPlaceBlock();
    }

    public void AutoFillBuilder()
    {
        Builder.AutoFillFromCurrentBudget();
        StatusMessage = "Оба замка расставлены по текущему бюджету.";
        _sound.PlayPlaceBlock();
    }

    public void ClearBuilder()
    {
        Builder.ClearGrids();
        StatusMessage = "Обе сетки очищены. Блоки возвращены в запас.";
        _sound.PlayRemoveBlock();
    }

    public void HandleBuilderClick(PointF worldPt, bool isRightClick)
    {
        if (Phase != GamePhase.Builder) return;

        if (!Builder.TryHitTestCell(
            worldPt, out var side, out int col, out int row))
        {
            RejectAction(
                "Кликните по ячейке левой (Синий) или " +
                "правой (Красный) сетки замка!");
            return;
        }

        bool ok = isRightClick
            ? Builder.TryRemoveBlock(side, col, row, out string msg)
            : Builder.TryPlaceBlock(side, col, row, out msg);

        if (ok)
        {
            StatusMessage = msg;
            if (isRightClick) _sound.PlayRemoveBlock();
            else _sound.PlayPlaceBlock();
        }
        else
        {
            RejectAction(msg);
        }
    }

    public void Update(float dt)
    {
        dt = Math.Min(dt, 0.05f);
        CloudTime += dt;
        if (_shakeTimer > 0f)
        {
            _shakeTimer = MathF.Max(0f, _shakeTimer - dt);
        }

        foreach (var slot in Level.CrateSlots)
        {
            if (slot.ActiveCrate != null)
            {
                slot.ActiveCrate.BobPhase += dt * 2.4f;
            }
        }

        for (int i = Explosions.Count - 1; i >= 0; i--)
        {
            Explosions[i].Update(dt);
            if (!Explosions[i].IsAlive) Explosions.RemoveAt(i);
        }

        for (int i = FloatingTexts.Count - 1; i >= 0; i--)
        {
            FloatingTexts[i].Update(dt);
            if (!FloatingTexts[i].IsAlive) FloatingTexts.RemoveAt(i);
        }

        for (int i = Debris.Count - 1; i >= 0; i--)
        {
            Debris[i].Update(dt, LevelData.Gravity, Level);
            if (!Debris[i].IsAlive) Debris.RemoveAt(i);
        }

        if (Phase == GamePhase.Flying)
        {
            UpdateProjectiles(dt);
        }
        else if (Phase == GamePhase.Settling)
        {
            UpdateSettling(dt);
        }

        CurrentShakeOffset = PointF.Empty;
        if (_shakeTimer > 0f)
        {
            float amount = _shakeIntensity * (_shakeTimer / _shakeDuration);
            CurrentShakeOffset = new PointF(
                (_rng.NextSingle() * 2f - 1f) * amount,
                (_rng.NextSingle() * 2f - 1f) * amount);
        }
    }

    private void UpdateProjectiles(float dt)
    {
        float subDt = dt / 6f;
        for (int step = 0; step < 6; step++)
        {
            for (int i = 0; i < Projectiles.Count; i++)
            {
                Projectile p = Projectiles[i];
                if (!p.IsAlive) continue;

                p.Step(subDt, LevelData.Gravity);
                if (p.X < -30f ||
                    p.X > LevelData.WorldWidth + 30f ||
                    p.Y > LevelData.WorldHeight + 60f)
                {
                    p.IsAlive = false;
                    continue;
                }

                if (!CheckCrateCollision(p) && !CheckBlockCollision(p))
                {
                    CheckTerrainCollision(p);
                }
            }
        }

        bool anyAlive = false;
        foreach (var p in Projectiles)
        {
            if (p.IsAlive) { anyAlive = true; break; }
        }

        if (!anyAlive)
        {
            if (UpdateBlockTargets()) Phase = GamePhase.Settling;
            else FinishTurnResolution();
        }
    }

    private bool CheckCrateCollision(Projectile p)
    {
        foreach (var slot in Level.CrateSlots)
        {
            if (slot.ActiveCrate == null) continue;
            if (TouchesRectangle(p, slot.ActiveCrate.Bounds, out _, out _))
            {
                CollectCrate(slot, p.Owner);
                p.IsAlive = false;
                return true;
            }
        }
        return false;
    }

    private bool CheckBlockCollision(Projectile p)
    {
        foreach (var block in Level.Blocks)
        {
            if (block.IsDestroyed) continue;
            RectangleF rect = block.Bounds;
            if (!TouchesRectangle(p, rect, out float cx, out float cy))
            {
                continue;
            }

            ComputeBlockContact(
                p, rect, ref cx, ref cy, out float nx, out float ny);
            HandleSurfaceHit(p, nx, ny, cx, cy, block, Color.Orange);
            return true;
        }
        return false;
    }

    private static bool TouchesRectangle(
        Projectile p,
        RectangleF rect,
        out float x,
        out float y)
    {
        x = Math.Clamp(p.X, rect.Left, rect.Right);
        y = Math.Clamp(p.Y, rect.Top, rect.Bottom);
        float dx = p.X - x;
        float dy = p.Y - y;
        return dx * dx + dy * dy <= p.Radius * p.Radius;
    }

    private static void ComputeBlockContact(
        Projectile p,
        RectangleF rect,
        ref float x,
        ref float y,
        out float nx,
        out float ny)
    {
        float dx = p.X - x;
        float dy = p.Y - y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);

        if (distance > 0f)
        {
            nx = dx / distance;
            ny = dy / distance;
            return;
        }

        float left = p.X - rect.Left;
        float right = rect.Right - p.X;
        float top = p.Y - rect.Top;
        float bottom = rect.Bottom - p.Y;
        float nearest = MathF.Min(
            MathF.Min(left, right), MathF.Min(top, bottom));

        if (nearest == top)
        {
            nx = 0f; ny = -1f; y = rect.Top;
        }
        else if (nearest == bottom)
        {
            nx = 0f; ny = 1f; y = rect.Bottom;
        }
        else if (nearest == left)
        {
            nx = -1f; ny = 0f; x = rect.Left;
        }
        else
        {
            nx = 1f; ny = 0f; x = rect.Right;
        }
    }

    private void CheckTerrainCollision(Projectile p)
    {
        if (p.X < -30f ||
            p.X >= LevelData.WorldWidth + 30f ||
            p.Y < -80f)
        {
            return;
        }

        if (p.Y >= LevelData.WorldHeight - 8f)
        {
            HandleSurfaceHit(
                p, 0f, -1f, p.X, LevelData.WorldHeight - 8f, null, Color.Khaki);
            return;
        }

        int px = Convert.ToInt32(MathF.Round(p.X));
        int py = Convert.ToInt32(MathF.Round(p.Y));
        int r = Math.Max(1, Convert.ToInt32(MathF.Ceiling(p.Radius)));

        bool hit = Level.IsSolid(px, py);
        int hitX = px;
        int hitY = py;

        if (!hit)
        {
            int d = Convert.ToInt32(r * 0.7071f);
            int[,] testPoints =
            {
                { 0, r }, { 0, -r }, { r, 0 }, { -r, 0 },
                { d, d }, { d, -d }, { -d, d }, { -d, -d }
            };

            for (int i = 0; i < 8; i++)
            {
                int tx = px + testPoints[i, 0];
                int ty = py + testPoints[i, 1];
                if (Level.IsSolid(tx, ty))
                {
                    hit = true;
                    hitX = tx;
                    hitY = ty;
                    break;
                }
            }
        }

        if (!hit) return;

        float nx = 0f;
        float ny = 0f;
        int sampleR = Math.Max(3, r + 1);

        for (int dy = -sampleR; dy <= sampleR; dy++)
        {
            for (int dx = -sampleR; dx <= sampleR; dx++)
            {
                if (dx * dx + dy * dy <= sampleR * sampleR &&
                    Level.IsSolid(hitX + dx, hitY + dy))
                {
                    nx -= dx;
                    ny -= dy;
                }
            }
        }

        float len = MathF.Sqrt(nx * nx + ny * ny);
        if (len > 0.001f)
        {
            nx /= len;
            ny /= len;
        }
        else
        {
            float vlen = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            if (vlen > 0.001f)
            {
                nx = -p.Vx / vlen;
                ny = -p.Vy / vlen;
            }
            else
            {
                nx = 0f;
                ny = -1f;
            }
        }

        HandleSurfaceHit(p, nx, ny, hitX, hitY, null, Color.Khaki);
    }

    private void HandleSurfaceHit(
        Projectile p,
        float nx,
        float ny,
        float x,
        float y,
        Block? hitBlock,
        Color sparkColor)
    {
        bool leavingSurface = p is RicochetProjectile &&
            p.Vx * nx + p.Vy * ny > 0f;

        if (leavingSurface || !p.OnSurfaceHit(nx, ny))
        {
            p.X = x + nx * (p.Radius + 0.5f);
            p.Y = y + ny * (p.Radius + 0.5f);
            if (!leavingSurface)
            {
                _sound.PlayRicochet();
                Explosions.Add(new ExplosionEffect(
                    new PointF(x, y), 16f, sparkColor, 0.18f));
            }
            return;
        }

        if (hitBlock == null)
        {
            p.X = x;
            p.Y = y;
        }

        ExplodeProjectile(p, hitBlock);
        p.IsAlive = false;
    }

    private void ExplodeProjectile(Projectile p, Block? directBlock)
    {
        bool heavy = p is RicochetProjectile;
        _sound.PlayExplosion(heavy);

        Explosions.Add(new ExplosionEffect(
            new PointF(p.X, p.Y),
            p.ExplosionRadius,
            heavy ? Color.OrangeRed : Color.Gold));

        TriggerShake(heavy ? 10f : 5f, heavy ? 0.28f : 0.16f);
        Level.CarveCrater(p.X, p.Y, p.ExplosionRadius * 0.85f);

        if (directBlock == null)
        {
            SpawnDebrisBurst(
                new PointF(p.X, p.Y - 4f),
                8,
                Color.FromArgb(124, 88, 56),
                190f);
        }

        ExplosionDamageResult dmg = CastlePhysics.ApplyExplosionDamage(
            Level.Blocks,
            p.X,
            p.Y,
            p.ExplosionRadius,
            p.DirectDamage,
            p.SplashDamage,
            directBlock);

        if (directBlock != null && dmg.ShieldAbsorbedDirectHit)
        {
            FloatingTexts.Add(new FloatingText(
                "Щит снят!", directBlock.Center, Color.DeepSkyBlue));
            SpawnDebrisBurst(directBlock.Center, 8, Color.Cyan, 200f);
        }

        if (dmg.DirectKingDestroyed)
        {
            TriggerShake(14f, 0.40f);
        }

        foreach (var block in dmg.DamagedBlocks)
        {
            if (dmg.ShieldAbsorbedDirectHit && block == directBlock) continue;
            SpawnBlockDebris(block);
        }
    }

    private void TriggerShake(float intensity, float duration)
    {
        if (intensity >= _shakeIntensity || _shakeTimer <= 0f)
        {
            _shakeIntensity = intensity;
            _shakeDuration = duration;
            _shakeTimer = duration;
        }
    }

    private void SpawnBlockDebris(Block block)
    {
        Color color = block.Type switch
        {
            BlockType.Wood => Color.FromArgb(178, 118, 60),
            BlockType.Stone => Color.FromArgb(148, 154, 162),
            BlockType.Steel => Color.FromArgb(118, 134, 152),
            _ => Color.Gold
        };

        SpawnDebrisBurst(
            block.Center,
            block.IsDestroyed ? 9 : 4,
            color,
            block.IsDestroyed ? 220f : 150f);
    }

    private void SpawnDebrisBurst(
        PointF origin,
        int count,
        Color color,
        float maxSpeed)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = -MathF.PI * (0.1f + 0.8f * _rng.NextSingle());
            float speed = maxSpeed * (0.4f + 0.6f * _rng.NextSingle());
            Debris.Add(new DebrisParticle(
                origin.X,
                origin.Y,
                MathF.Cos(angle) * speed,
                MathF.Sin(angle) * speed,
                4.5f + 4f * _rng.NextSingle(),
                (_rng.NextSingle() * 2f - 1f) * 360f,
                color));
        }
    }

    private void CollectCrate(CrateSlot slot, PlayerId collector)
    {
        PointF pos = slot.ActiveCrate != null
            ? slot.ActiveCrate.CurrentPosition
            : slot.Position;

        slot.ActiveCrate = null;
        _crateRespawnRounds.Add(RoundNumber + CrateRespawnRounds + 1);
        SpawnDebrisBurst(pos, 7, Color.FromArgb(218, 165, 84), 190f);

        PlayerId opponent = collector == PlayerId.Blue
            ? PlayerId.Red
            : PlayerId.Blue;

        CrateEffectType[] effects = Enum.GetValues<CrateEffectType>();
        CrateEffectType effect = effects[_rng.Next(effects.Length)];

        if (effect == CrateEffectType.KingShield)
        {
            Block? king = null;
            foreach (var b in Level.Blocks)
            {
                if (b.Owner == collector && b.IsKing && !b.IsDestroyed)
                {
                    king = b;
                    break;
                }
            }

            if (king != null && !king.HasShield)
            {
                king.HasShield = true;
                FloatingTexts.Add(new FloatingText(
                    "+1 Щит Короля!", pos, Color.DeepSkyBlue));
                _sound.PlayCrateBonus();
            }
            else
            {
                GrantRandomAmmo(collector, pos);
            }
        }
        else if (effect == CrateEffectType.RepairCastle)
        {
            int repaired = 0;
            foreach (var block in Level.Blocks)
            {
                if (block.Owner == collector && block.RepairOneHp())
                {
                    repaired++;
                }
            }

            if (repaired > 0)
            {
                FloatingTexts.Add(new FloatingText(
                    $"Ремонт (+{repaired} бл.)", pos, Color.LimeGreen));
                _sound.PlayCrateBonus();
            }
            else
            {
                GrantRandomAmmo(collector, pos);
            }
        }
        else if (effect == CrateEffectType.ExtraAmmo)
        {
            GrantRandomAmmo(collector, pos);
        }
        else
        {
            GrantRandomAmmo(collector, pos);
        }
    }

    private void GrantRandomAmmo(PlayerId player, PointF pos)
    {
        ProjectileType bonus = _rng.Next(2) == 0
            ? ProjectileType.Cluster
            : ProjectileType.Ricochet;

        _ammo[player][bonus]++;
        FloatingTexts.Add(new FloatingText(
            bonus == ProjectileType.Cluster
                ? "+1 Кассета!"
                : "+1 Рикошет!",
            pos,
            Color.Gold));
        _sound.PlayCrateBonus();
    }

    private bool UpdateBlockTargets() =>
        CastlePhysics.ComputeDropTargets(Level.Blocks);

    private void UpdateSettling(float dt)
    {
        if (!CastlePhysics.StepSettling(Level.Blocks, dt))
        {
            FinishTurnResolution();
        }
    }

    private void FinishTurnResolution()
    {
        bool blueDead = false;
        bool redDead = false;

        foreach (var b in Level.Blocks)
        {
            if (b.Owner == PlayerId.Blue && b.IsKing && b.IsDestroyed)
            {
                blueDead = true;
            }
            if (b.Owner == PlayerId.Red && b.IsKing && b.IsDestroyed)
            {
                redDead = true;
            }
        }

        if (blueDead && redDead)
        {
            Phase = GamePhase.GameOver;
            IsDraw = true;
            StatusMessage = "Ничья! Оба Короля пали в одном ходу!";
            _sound.PlayVictory();
            return;
        }

        if (blueDead || redDead)
        {
            Phase = GamePhase.GameOver;
            Winner = blueDead ? PlayerId.Red : PlayerId.Blue;

            bool selfHit =
                (Winner == PlayerId.Red && CurrentPlayer == PlayerId.Blue) ||
                (Winner == PlayerId.Blue && CurrentPlayer == PlayerId.Red);

            if (Winner == PlayerId.Blue)
            {
                StatusMessage = selfHit
                    ? "Победа Синего замка! (Красный поразил своего Короля)"
                    : "Победа Синего замка!";
            }
            else
            {
                StatusMessage = selfHit
                    ? "Победа Красного замка! (Синий поразил своего Короля)"
                    : "Победа Красного замка!";
            }

            _sound.PlayVictory();
            return;
        }

        AdvanceTurn();
    }

    private void AdvanceTurn()
    {
        _aimFog[CurrentPlayer] = false;
        CurrentPlayer = CurrentPlayer == PlayerId.Blue
            ? PlayerId.Red
            : PlayerId.Blue;

        TurnNumber++;

        if (CurrentPlayer == PlayerId.Blue)
        {
            RoundNumber++;
            for (int i = _crateRespawnRounds.Count - 1; i >= 0; i--)
            {
                if (RoundNumber < _crateRespawnRounds[i]) continue;
                _crateRespawnRounds.RemoveAt(i);
                TrySpawnOneCrate();
            }
        }

        if (_ammo[CurrentPlayer][_selectedProjectile[CurrentPlayer]] <= 0)
        {
            _selectedProjectile[CurrentPlayer] = ProjectileType.Standard;
        }

        Phase = GamePhase.Aiming;
        string name = CurrentPlayer == PlayerId.Blue
            ? "Синего замка"
            : "Красного замка";

        StatusMessage = IsCurrentPlayerFogged
            ? $"Ход {name} (ТУМАН ПРИЦЕЛА: подсказка траектории скрыта!)"
            : $"Ход {name}: оттяните снаряд от катапульты.";
    }

    private void TrySpawnOneCrate()
    {
        int activeCount = 0;
        var freeSlots = new List<CrateSlot>();

        foreach (var s in Level.CrateSlots)
        {
            if (s.IsOccupied) activeCount++;
            else freeSlots.Add(s);
        }

        if (activeCount >= MaxActiveCrates || freeSlots.Count == 0) return;

        var chosen = freeSlots[_rng.Next(freeSlots.Count)];
        chosen.ActiveCrate = new Crate(
            Level.GetCurrentSlotPosition(chosen),
            chosen.IsAirborne,
            _rng.NextSingle() * MathF.PI * 2f);
    }

    private bool RejectAction(string message)
    {
        StatusMessage = message;
        _sound.PlayError();
        return false;
    }

    public void Dispose()
    {
        Level?.Dispose();
    }
}
