using System.Drawing;
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

    public GameEngine()
    {
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

        Projectile proj = new StandardProjectile(
            CurrentPlayer, sx, sy, lp.Vx, lp.Vy);

        Projectiles.Clear();
        Projectiles.Add(proj);
        Phase = GamePhase.Flying;

        StatusMessage = chosen == ProjectileType.Cluster
            ? "Кассетный снаряд в воздухе! Клик или Пробел — разделить."
            : "Снаряд в полёте...";

        return true;
    }

    // Разделяет активный кассетный снаряд в полёте
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
    }

    public void AutoFillBuilder()
    {
        Builder.AutoFillFromCurrentBudget();
        StatusMessage = "Оба замка расставлены по текущему бюджету.";
    }

    public void ClearBuilder()
    {
        Builder.ClearGrids();
        StatusMessage = "Обе сетки очищены. Блоки возвращены в запас.";
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
        float subDt = dt;
        for (int step = 0; step < 1; step++)
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

                CheckTerrainCollision(p);
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
        p.X = x;
        p.Y = y;
        p.IsAlive = false;
    }

    private bool UpdateBlockTargets()
    {
        bool anyWillFall = false;
        foreach (var side in new PlayerId[] { PlayerId.Blue, PlayerId.Red })
        {
            for (int col = 0; col < LevelData.GridCols; col++)
            {
                var colBlocks = new List<Block>();
                foreach (var b in Level.Blocks)
                {
                    if (!b.IsDestroyed && b.Owner == side && b.ColumnIndex == col)
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

    private void UpdateSettling(float dt)
    {
        bool stillMoving = false;
        foreach (var block in Level.Blocks)
        {
            if (block.IsDestroyed || block.Y >= block.TargetY - 0.5f)
            {
                continue;
            }

            block.Y = MathF.Min(block.TargetY, block.Y + 260f * dt);
            if (block.Y < block.TargetY - 0.5f)
            {
                stillMoving = true;
            }
            else
            {
                block.Y = block.TargetY;
            }
        }

        if (!stillMoving) FinishTurnResolution();
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
        return false;
    }

    public void Dispose()
    {
        Level?.Dispose();
    }
}
