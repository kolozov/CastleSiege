using System.Drawing;
using System.Drawing.Drawing2D;
using CastleSiege.Engine;
using CastleSiege.Models;

namespace CastleSiege.Rendering;

// Отрисовка игрового мира средствами GDI+ (WinForms)
public sealed class GameRenderer : IDisposable
{
    // Шрифты поля масштабируются вместе с игровым миром
    private readonly Font _smallFont =
        new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);

    private readonly Font _crateFont =
        new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);

    private readonly Font _floatingFont =
        new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);

    private static readonly float[] CloudX =
        { 120f, 390f, 670f, 910f, 1120f };

    private static readonly float[] CloudY =
        { 78f, 118f, 64f, 108f, 84f };

    private static readonly float[] CloudScales =
        { 1f, 0.8f, 1.15f, 0.9f, 0.85f };

    private static readonly float[] CloudSpeeds =
        { 12f, 9f, 14f, 10f, 11f };

    private static readonly PointF[] DistantRidge =
    {
        new(-24, 464), new(90, 433), new(180, 448),
        new(313, 380), new(404, 425), new(481, 391),
        new(579, 450), new(671, 416), new(769, 440),
        new(878, 374), new(1004, 438), new(1120, 411),
        new(1224, 462), new(1224, 644), new(-24, 644)
    };

    private static readonly RectangleF Banner =
        new RectangleF(320f, 190f, 560f, 120f);

    public void Dispose()
    {
        _smallFont.Dispose();
        _crateFont.Dispose();
        _floatingFont.Dispose();
    }

    public void Render(Graphics g, GameEngine engine)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        DrawSkyAndClouds(g, engine);

        PointF shake = engine.CurrentShakeOffset;
        GraphicsState? shakeState = null;
        if (!shake.IsEmpty)
        {
            shakeState = g.Save();
            g.TranslateTransform(shake.X, shake.Y);
        }

        DrawBackgroundHills(g, engine.Level.Location);
        DrawTerrain(g, engine.Level);

        if (engine.Phase == GamePhase.Builder)
        {
            DrawBuilderGrid(
                g, engine.Builder, PlayerId.Blue,
                "СИНИЙ ЗАМОК", Color.RoyalBlue);
            DrawBuilderGrid(
                g, engine.Builder, PlayerId.Red,
                "КРАСНЫЙ ЗАМОК", Color.Crimson);

            if (shakeState != null) g.Restore(shakeState);
            return;
        }

        DrawCrates(g, engine.Level.CrateSlots);

        foreach (var b in engine.Level.Blocks)
        {
            if (!b.IsDestroyed)
            {
                DrawBlockShape(
                    g, b.Bounds, b.Type, b.Owner, b.Hp, b.MaxHp, false);
            }
        }

        foreach (var b in engine.Level.Blocks)
        {
            if (!b.IsDestroyed && b.Type == BlockType.King && b.HasShield)
            {
                DrawKingShieldOverlay(g, b.Bounds);
            }
        }

        DrawCatapult(
            g, engine, PlayerId.Blue, engine.Level.BlueCatapult);
        DrawCatapult(
            g, engine, PlayerId.Red, engine.Level.RedCatapult);

        DrawProjectiles(g, engine.Projectiles);
        DrawDebris(g, engine.Debris);
        DrawExplosions(g, engine.Explosions);

        RectangleF aimBadge = DrawAimingOverlay(g, engine);

        if (shakeState != null) g.Restore(shakeState);
        if (!aimBadge.IsEmpty) aimBadge.Offset(shake);
    }

    private void DrawBuilderGrid(
        Graphics g,
        CastleBuilder builder,
        PlayerId side,
        string title,
        Color teamColor)
    {
        var grid = builder.GetGrid(side);
        float startX = side == PlayerId.Blue
            ? CastleBuilder.BlueGridX
            : CastleBuilder.RedGridX;

        float totalW = CastleBuilder.GridCols * CastleBuilder.CellW +
            (CastleBuilder.GridCols - 1) * CastleBuilder.CellGap;
        float topY = CastleBuilder.GridBottomY -
            CastleBuilder.GridRows * CastleBuilder.CellH;

        using var headerBg = new SolidBrush(Color.FromArgb(200, teamColor));
        using var whiteBrush = new SolidBrush(Color.White);
        var header = new RectangleF(startX, topY - 34f, totalW, 26f);
        g.FillRectangle(headerBg, header);
        DrawFittedText(g, title, _crateFont, whiteBrush, header);

        using var emptyFill = new SolidBrush(
            Color.FromArgb(65, 255, 255, 255));
        using var gridPen = new Pen(
            Color.FromArgb(170, teamColor), 1.5f)
        {
            DashStyle = DashStyle.Dash
        };

        for (int c = 0; c < CastleBuilder.GridCols; c++)
        {
            for (int r = 0; r < CastleBuilder.GridRows; r++)
            {
                RectangleF rect = CastleBuilder.GetCellRect(side, c, r);
                var cell = grid[c, r];
                if (cell != null)
                {
                    DrawBlockShape(
                        g, rect, cell.Value, side, 1, 1, false);
                }
                else
                {
                    g.FillRectangle(emptyFill, rect);
                    g.DrawRectangle(
                        gridPen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        bool hasKing = builder.GetUsed(side, BlockType.King) == 1;
        int remWood = builder.GetRemaining(side, BlockType.Wood);
        int remStone = builder.GetRemaining(side, BlockType.Stone);
        int remSteel = builder.GetRemaining(side, BlockType.Steel);
        string summary =
            $"Король: {(hasKing ? "✓" : "НЕТ!")}  |  " +
            $"Д: {remWood}  К: {remStone}  С: {remSteel}";

        using var sumBg = new SolidBrush(Color.FromArgb(195, 22, 26, 36));
        using var sumBrush = new SolidBrush(
            hasKing ? Color.WhiteSmoke : Color.OrangeRed);

        var summaryBox = new RectangleF(
            startX - 8f,
            CastleBuilder.GridBottomY + 14f,
            totalW + 16f,
            24f);

        g.FillRectangle(sumBg, summaryBox);
        DrawFittedText(g, summary, _smallFont, sumBrush, summaryBox);
    }

    private static void DrawSkyAndClouds(Graphics g, GameEngine engine)
    {
        Color top = Color.FromArgb(105, 178, 240);
        Color bot = Color.FromArgb(215, 238, 255);
        Color sun = Color.FromArgb(255, 244, 160);
        Color clouds = Color.FromArgb(171, 249, 252, 255);
        float cloudHeight = 1f;
        float cloudLift = 0f;

        if (engine.Level.Location == LocationId.MountainPass)
        {
            top = Color.FromArgb(84, 126, 168);
            bot = Color.FromArgb(210, 229, 239);
            sun = Color.FromArgb(245, 241, 210);
            clouds = Color.FromArgb(120, 226, 241, 250);
            cloudHeight = 0.45f;
            cloudLift = -9f;
        }
        else if (engine.Level.Location == LocationId.Fortress)
        {
            top = Color.FromArgb(86, 90, 127);
            bot = Color.FromArgb(222, 167, 135);
            sun = Color.FromArgb(255, 203, 131);
            clouds = Color.FromArgb(135, 166, 132, 149);
            cloudHeight = 0.74f;
            cloudLift = 8f;
        }

        var worldRect = new RectangleF(
            0, 0, LevelData.WorldWidth, LevelData.WorldHeight);

        using (var sky = new LinearGradientBrush(
            worldRect, top, bot, LinearGradientMode.Vertical))
        {
            g.FillRectangle(sky, worldRect);
        }

        using (var halo = new SolidBrush(Color.FromArgb(65, sun)))
        {
            g.FillEllipse(halo, 955f, 38f, 92f, 92f);
        }

        using (var disc = new SolidBrush(sun))
        {
            g.FillEllipse(disc, 973f, 56f, 56f, 56f);
        }

        using var cloudBrush = new SolidBrush(clouds);
        for (int i = 0; i < CloudX.Length; i++)
        {
            float x = (CloudX[i] + engine.CloudTime * CloudSpeeds[i]) %
                (LevelData.WorldWidth + 180f) - 90f;
            float width = CloudScales[i];
            float height = width * cloudHeight;
            float y = CloudY[i] + cloudLift;

            // Единый силуэт не темнеет на стыках полупрозрачных частей
            using var cloud = new GraphicsPath(FillMode.Winding);
            cloud.AddEllipse(
                x, y, (55f + i * 3f) * width, 27f * height);
            cloud.AddEllipse(
                x + 24f * width,
                y - (10f - i) * height,
                52f * width,
                34f * height);
            cloud.AddEllipse(
                x + 49f * width,
                y + 3f * height,
                (41f + i * 4f) * width,
                22f * height);

            g.FillPath(cloudBrush, cloud);
        }
    }

    private static void DrawBackgroundHills(
        Graphics g,
        LocationId location)
    {
        if (location == LocationId.MountainPass)
        {
            using var ridge = new SolidBrush(
                Color.FromArgb(100, 128, 153, 176));
            g.FillPolygon(ridge, DistantRidge);
            return;
        }

        Color hillColor = location == LocationId.Fortress
            ? Color.FromArgb(103, 96, 81, 108)
            : Color.FromArgb(103, 94, 151, 115);

        using var hill = new SolidBrush(hillColor);
        using var horizon = new GraphicsPath();
        horizon.AddBezier(-24, 490, 94, 452, 228, 448, 355, 483);
        horizon.AddBezier(355, 483, 500, 521, 642, 450, 780, 464);
        horizon.AddBezier(780, 464, 971, 480, 1064, 452, 1224, 479);
        horizon.AddLine(1224, 479, 1224, 644);
        horizon.AddLine(1224, 644, -24, 644);
        horizon.CloseFigure();
        g.FillPath(hill, horizon);

        if (location == LocationId.Fortress) DrawDistantFortress(g);
    }

    private static void DrawDistantFortress(Graphics g)
    {
        using var stone = new SolidBrush(Color.FromArgb(109, 77, 61, 91));
        using var silhouette = new GraphicsPath(FillMode.Winding);
        silhouette.AddRectangle(new RectangleF(346, 454, 508, 95));

        for (int x = 394; x < 806; x += 34)
        {
            silhouette.AddRectangle(new RectangleF(x, 441, 16, 14));
        }

        for (int x = 332; x <= 806; x += 474)
        {
            silhouette.AddRectangle(new RectangleF(x, 412, 62, 137));
            for (int tooth = 0; tooth < 3; tooth++)
            {
                silhouette.AddRectangle(
                    new RectangleF(x + tooth * 24, 399, 14, 14));
            }
        }
        g.FillPath(stone, silhouette);
    }

    private static void DrawTerrain(Graphics g, LevelData level)
    {
        g.DrawImageUnscaled(level.TerrainBitmap, 0, 0);
    }

    private void DrawCrates(Graphics g, List<CrateSlot> slots)
    {
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        foreach (var slot in slots)
        {
            if (slot.ActiveCrate == null) continue;
            PointF pos = slot.ActiveCrate.CurrentPosition;
            float r = slot.ActiveCrate.Radius;

            if (slot.ActiveCrate.IsAirborne)
            {
                using var stringPen = new Pen(
                    Color.FromArgb(180, 90, 80, 70), 1.6f);
                g.DrawLine(
                    stringPen, pos.X, pos.Y - r, pos.X, pos.Y - r - 24f);

                var bRect = new RectangleF(
                    pos.X - 15f, pos.Y - r - 58f, 30f, 36f);

                using var bBrush = new LinearGradientBrush(
                    bRect,
                    Color.FromArgb(255, 105, 95),
                    Color.FromArgb(200, 40, 50),
                    LinearGradientMode.ForwardDiagonal);

                using var bPen = new Pen(
                    Color.FromArgb(140, 20, 30), 1.5f);
                using var shine = new SolidBrush(
                    Color.FromArgb(140, Color.White));

                g.FillEllipse(bBrush, bRect);
                g.DrawEllipse(bPen, bRect);
                g.FillEllipse(shine, pos.X - 8f, pos.Y - r - 51f, 7f, 11f);
            }

            RectangleF boxRect = slot.ActiveCrate.Bounds;
            using var boxBrush = new SolidBrush(
                Color.FromArgb(218, 165, 84));
            using var trimPen = new Pen(
                Color.FromArgb(124, 78, 28), 2.2f);
            using var qShadow = new SolidBrush(
                Color.FromArgb(160, 60, 30, 0));
            using var qBrush = new SolidBrush(Color.White);

            g.FillRectangle(boxBrush, boxRect);
            g.DrawRectangle(
                trimPen, boxRect.X, boxRect.Y, boxRect.Width, boxRect.Height);
            g.DrawLine(
                trimPen, boxRect.Left, boxRect.Top,
                boxRect.Right, boxRect.Bottom);
            g.DrawLine(
                trimPen, boxRect.Right, boxRect.Top,
                boxRect.Left, boxRect.Bottom);

            g.DrawString("?", _crateFont, qShadow, pos.X + 1f, pos.Y + 1f, sf);
            g.DrawString("?", _crateFont, qBrush, pos.X, pos.Y, sf);
        }
    }

    private static void DrawBlockShape(
        Graphics g,
        RectangleF rect,
        BlockType type,
        PlayerId owner,
        int hp,
        int maxHp,
        bool hasShield)
    {
        if (type == BlockType.King)
        {
            DrawKingShape(g, rect, owner, hasShield);
            return;
        }

        if (type == BlockType.Wood)
        {
            using var fill = new SolidBrush(Color.FromArgb(188, 129, 70));
            using var grain = new Pen(Color.FromArgb(133, 79, 42), 3f);
            using var border = new Pen(Color.FromArgb(91, 52, 28), 1.8f);
            g.FillRectangle(fill, rect);

            float upper = rect.Top + rect.Height * 0.34f;
            float lower = rect.Top + rect.Height * 0.69f;

            g.DrawBezier(grain,
                rect.Left + 3, upper,
                rect.Left + 12, upper - 5,
                rect.Right - 12, upper + 5,
                rect.Right - 3, upper);

            g.DrawBezier(grain,
                rect.Left + 3, lower,
                rect.Left + 12, lower + 4,
                rect.Right - 12, lower - 3,
                rect.Right - 3, lower);

            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }
        else if (type == BlockType.Stone)
        {
            using var fill = new SolidBrush(Color.FromArgb(162, 160, 153));
            using var seam = new Pen(Color.FromArgb(99, 99, 96), 2.5f);
            using var border = new Pen(Color.FromArgb(75, 77, 76), 1.8f);
            g.FillRectangle(fill, rect);

            float midY = rect.Top + rect.Height / 2f;
            float upperJoint = rect.Left + rect.Width * 0.64f;
            float lowerJoint = rect.Left + rect.Width * 0.36f;

            g.DrawLine(seam, rect.Left + 2, midY, rect.Right - 2, midY);
            g.DrawLine(seam, upperJoint, rect.Top + 2, upperJoint, midY);
            g.DrawLine(seam, lowerJoint, midY, lowerJoint, rect.Bottom - 2);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }
        else
        {
            using var fill = new LinearGradientBrush(
                rect,
                Color.FromArgb(145, 163, 181),
                Color.FromArgb(94, 112, 131),
                LinearGradientMode.Vertical);

            using var rivet = new SolidBrush(Color.FromArgb(46, 57, 70));
            using var edge = new Pen(Color.FromArgb(208, 219, 225), 2f);
            using var border = new Pen(Color.FromArgb(43, 53, 65), 2f);

            g.FillRectangle(fill, rect);
            g.DrawLine(
                edge,
                rect.Left + 2, rect.Top + 2,
                rect.Right - 2, rect.Top + 2);
            g.DrawLine(
                edge,
                rect.Left + 2, rect.Top + 2,
                rect.Left + 2, rect.Bottom - 2);

            g.FillEllipse(rivet, rect.Left + 5, rect.Top + 5, 5, 5);
            g.FillEllipse(rivet, rect.Right - 10, rect.Top + 5, 5, 5);
            g.FillEllipse(rivet, rect.Left + 5, rect.Bottom - 10, 5, 5);
            g.FillEllipse(rivet, rect.Right - 10, rect.Bottom - 10, 5, 5);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }

        if (hp < maxHp)
        {
            int dmg = maxHp - hp;
            using var crackPen = new Pen(
                Color.FromArgb(200, 35, 30, 28), 1.6f);

            g.DrawLine(crackPen,
                rect.Left + 6, rect.Top + 4,
                rect.Left + rect.Width * 0.45f, rect.Top + rect.Height * 0.52f);
            g.DrawLine(crackPen,
                rect.Left + rect.Width * 0.45f, rect.Top + rect.Height * 0.52f,
                rect.Left + rect.Width * 0.3f, rect.Bottom - 4);

            if (dmg >= 2 || (maxHp == 2 && dmg >= 1))
            {
                g.DrawLine(crackPen,
                    rect.Right - 7, rect.Top + 6,
                    rect.Left + rect.Width * 0.55f,
                    rect.Top + rect.Height * 0.48f);
                g.DrawLine(crackPen,
                    rect.Left + rect.Width * 0.55f,
                    rect.Top + rect.Height * 0.48f,
                    rect.Right - 9, rect.Bottom - 5);
            }
            if (dmg >= 3)
            {
                g.DrawLine(crackPen,
                    rect.Left + 4, rect.Top + rect.Height * 0.62f,
                    rect.Right - 5, rect.Top + rect.Height * 0.38f);
                g.DrawLine(crackPen,
                    rect.Left + rect.Width * 0.52f,
                    rect.Top + rect.Height * 0.48f,
                    rect.Left + rect.Width * 0.62f, rect.Bottom - 3);
            }
        }
    }

    private static void DrawKingShape(
        Graphics g,
        RectangleF rect,
        PlayerId owner,
        bool hasShield)
    {
        Color c1 = owner == PlayerId.Blue
            ? Color.FromArgb(52, 118, 215)
            : Color.FromArgb(205, 58, 58);

        Color c2 = owner == PlayerId.Blue
            ? Color.FromArgb(28, 70, 145)
            : Color.FromArgb(135, 30, 30);

        using (var fill = new LinearGradientBrush(
            rect, c1, c2, LinearGradientMode.Vertical))
        {
            g.FillRectangle(fill, rect);
        }

        using (var border = new Pen(Color.Gold, 2.2f))
        {
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }

        float cx = rect.Left + rect.Width / 2f;
        float cy = rect.Top + rect.Height / 2f + 2f;

        PointF[] crown =
        {
            new(cx - 11, cy + 7),
            new(cx - 14, cy - 7),
            new(cx - 6, cy - 2),
            new(cx, cy - 11),
            new(cx + 6, cy - 2),
            new(cx + 14, cy - 7),
            new(cx + 11, cy + 7)
        };

        using (var crownBrush = new SolidBrush(Color.FromArgb(255, 220, 92)))
        {
            g.FillPolygon(crownBrush, crown);
        }

        using (var crownPen = new Pen(Color.FromArgb(61, 38, 20), 2.5f)
        {
            LineJoin = LineJoin.Round
        })
        {
            g.DrawPolygon(crownPen, crown);
        }

        if (hasShield) DrawKingShieldOverlay(g, rect);
    }

    private static void DrawKingShieldOverlay(Graphics g, RectangleF rect)
    {
        var sr = new RectangleF(
            rect.X - 6f, rect.Y - 6f, rect.Width + 12f, rect.Height + 12f);

        using var aura = new SolidBrush(
            Color.FromArgb(65, Color.DeepSkyBlue));
        using var ring = new Pen(
            Color.FromArgb(220, Color.Cyan), 2.5f);

        g.FillEllipse(aura, sr);
        g.DrawEllipse(ring, sr);
    }

    private static void DrawCatapult(
        Graphics g,
        GameEngine engine,
        PlayerId owner,
        PointF anchor)
    {
        bool battleTurn =
            engine.Phase == GamePhase.Aiming ||
            engine.Phase == GamePhase.Flying ||
            engine.Phase == GamePhase.Settling;

        bool active = battleTurn && engine.CurrentPlayer == owner;
        Color teamColor = owner == PlayerId.Blue
            ? Color.FromArgb(57, 113, 226)
            : Color.FromArgb(220, 59, 64);

        float dir = owner == PlayerId.Blue ? 1f : -1f;

        if (active)
        {
            using var glow = new SolidBrush(Color.FromArgb(46, teamColor));
            g.FillEllipse(glow, anchor.X - 36f, anchor.Y - 32f, 72f, 56f);
        }

        Color outline = Color.FromArgb(72, 42, 18);
        using var woodBrush = new SolidBrush(Color.FromArgb(132, 82, 42));
        using var wheelBrush = new SolidBrush(Color.FromArgb(88, 52, 24));
        using var outlinePen = new Pen(outline, 2f);
        using var armPen = new Pen(Color.FromArgb(105, 62, 28), 5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.FillRectangle(woodBrush, anchor.X - 24f, anchor.Y + 4f, 48f, 10f);
        g.DrawRectangle(outlinePen, anchor.X - 24f, anchor.Y + 4f, 48f, 10f);
        g.DrawLine(armPen, anchor.X, anchor.Y + 6f, anchor.X, anchor.Y - 12f);

        g.FillEllipse(wheelBrush, anchor.X - 20f, anchor.Y + 6f, 14f, 14f);
        g.FillEllipse(wheelBrush, anchor.X + 6f, anchor.Y + 6f, 14f, 14f);
        g.DrawEllipse(outlinePen, anchor.X - 20f, anchor.Y + 6f, 14f, 14f);
        g.DrawEllipse(outlinePen, anchor.X + 6f, anchor.Y + 6f, 14f, 14f);

        float flagX = anchor.X - dir * 18f;
        float flagY = anchor.Y - 26f;
        float flagWidth = 16f;
        float flagHeight = 11f;

        using var polePen = new Pen(Color.FromArgb(57, 43, 35), 2f);
        using var flagBrush = new SolidBrush(teamColor);

        PointF[] flag =
        {
            new(flagX, flagY),
            new(flagX - dir * flagWidth, flagY + flagHeight / 2f),
            new(flagX, flagY + flagHeight)
        };

        g.DrawLine(polePen, flagX, anchor.Y + 4f, flagX, flagY);
        g.FillPolygon(flagBrush, flag);
    }

    private RectangleF DrawAimingOverlay(Graphics g, GameEngine engine)
    {
        if (engine.Phase != GamePhase.Aiming) return RectangleF.Empty;
        PointF anchor = engine.CurrentCatapultAnchor;

        if (engine.IsCurrentPlayerFogged)
        {
            using var fogBrush = new SolidBrush(
                Color.FromArgb(110, 150, 110, 200));
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center
            };
            using var textBrush = new SolidBrush(Color.Indigo);
            g.FillEllipse(fogBrush, anchor.X - 42f, anchor.Y - 46f, 84f, 64f);
            g.DrawString(
                "ТУМАН!", _smallFont, textBrush, anchor.X, anchor.Y - 62f, sf);
        }

        if (!engine.IsAiming) return RectangleF.Empty;

        PointF drag = engine.AimDragPoint;
        LaunchParams lp = engine.ComputeLaunchParams(drag);

        bool validDir =
            (engine.CurrentPlayer == PlayerId.Blue && lp.Vx > 25f) ||
            (engine.CurrentPlayer == PlayerId.Red && lp.Vx < -25f);

        bool validAim = lp.PullLen >= GameEngine.MinPullRadius &&
            validDir && lp.Vy <= 80f;

        using (var ringPen = new Pen(
            Color.FromArgb(75, Color.White), 1.5f)
        {
            DashStyle = DashStyle.Dash
        })
        {
            g.DrawEllipse(
                ringPen,
                anchor.X - GameEngine.MaxPullRadius,
                anchor.Y - GameEngine.MaxPullRadius,
                GameEngine.MaxPullRadius * 2f,
                GameEngine.MaxPullRadius * 2f);
        }

        Color slingColor = validAim
            ? Color.FromArgb(220, 65, 40, 25)
            : Color.FromArgb(220, 200, 40, 40);

        using (var slingPen = new Pen(slingColor, 3f))
        {
            g.DrawLine(slingPen, anchor.X - 4f, anchor.Y - 10f, drag.X, drag.Y);
            g.DrawLine(slingPen, anchor.X + 4f, anchor.Y - 10f, drag.X, drag.Y);
        }

        Color projectileColor = GetProjectileColor(
            engine.SelectedProjectile, Color.FromArgb(55, 55, 65));

        using (var ballBrush = new SolidBrush(projectileColor))
        {
            g.FillEllipse(ballBrush, drag.X - 7f, drag.Y - 7f, 14f, 14f);
        }

        if (validAim && !engine.IsCurrentPlayerFogged)
        {
            for (int i = 1; i <= 6; i++)
            {
                float t = i * 0.068f;
                float px = anchor.X + lp.Vx * t;
                float py = anchor.Y - 6f + lp.Vy * t +
                    0.5f * LevelData.Gravity * t * t;

                int alpha = 230 - i * 28;
                float r = 4.2f - i * 0.35f;

                using var dotBrush = new SolidBrush(
                    Color.FromArgb(alpha, Color.White));
                using var dotOutline = new Pen(
                    Color.FromArgb(alpha, Color.Black), 1f);

                g.FillEllipse(dotBrush, px - r, py - r, r * 2f, r * 2f);
                g.DrawEllipse(dotOutline, px - r, py - r, r * 2f, r * 2f);
            }
        }

        string badge = validAim
            ? $"{lp.AngleDeg:0}° | {lp.PowerPct}%"
            : "Отмена / неверный угол";

        SizeF size = g.MeasureString(badge, _smallFont);
        float width = size.Width + 12f;
        float height = size.Height + 8f;

        float bx = Math.Clamp(
            drag.X - width / 2f,
            16f,
            LevelData.WorldWidth - width - 16f);

        float by = Math.Clamp(
            drag.Y + 16f,
            anchor.Y + 28f,
            LevelData.WorldHeight - height - 16f);

        var box = new RectangleF(bx, by, width, height);

        Color badgeBgColor = validAim
            ? Color.FromArgb(210, 25, 30, 40)
            : Color.FromArgb(220, 165, 28, 28);

        using var bg = new SolidBrush(badgeBgColor);
        using var fg = new SolidBrush(Color.White);
        g.FillRectangle(bg, box);
        DrawFittedText(g, badge, _smallFont, fg, box);
        return box;
    }

    private static void DrawProjectiles(
        Graphics g,
        List<Projectile> projectiles)
    {
        foreach (var p in projectiles)
        {
            if (!p.IsAlive) continue;

            for (int i = 0; i < p.Trail.Count; i++)
            {
                float ratio = 1f * (i + 1) / p.Trail.Count;
                float r = p.Radius * 0.55f * ratio;
                int alpha = Convert.ToInt32(ratio * 150f);

                Color tc = p.Type switch
                {
                    ProjectileType.Cluster => Color.Plum,
                    ProjectileType.Ricochet => Color.Orange,
                    _ => Color.WhiteSmoke
                };

                using var tb = new SolidBrush(Color.FromArgb(alpha, tc));
                g.FillEllipse(
                    tb, p.Trail[i].X - r, p.Trail[i].Y - r, r * 2f, r * 2f);
            }

            if (p.Y < 8f)
            {
                float ix = Math.Clamp(
                    p.X, 16f, LevelData.WorldWidth - 16f);

                PointF[] arrow =
                {
                    new PointF(ix, 6f),
                    new PointF(ix - 8f, 20f),
                    new PointF(ix + 8f, 20f)
                };

                using var ab = new SolidBrush(Color.OrangeRed);
                using var ap = new Pen(Color.White, 1.5f);
                g.FillPolygon(ab, arrow);
                g.DrawPolygon(ap, arrow);
                continue;
            }

            Color projColor = GetProjectileColor(
                p.Type, Color.FromArgb(45, 48, 56));

            using (var body = new SolidBrush(projColor))
            {
                g.FillEllipse(
                    body,
                    p.X - p.Radius,
                    p.Y - p.Radius,
                    p.Radius * 2f,
                    p.Radius * 2f);
            }

            using (var rim = new Pen(Color.WhiteSmoke, 1.4f))
            {
                g.DrawEllipse(
                    rim,
                    p.X - p.Radius,
                    p.Y - p.Radius,
                    p.Radius * 2f,
                    p.Radius * 2f);
            }

            if (p is RicochetProjectile rico && rico.BouncesRemaining > 0)
            {
                using var pip = new SolidBrush(Color.Yellow);
                for (int b = 0; b < rico.BouncesRemaining; b++)
                {
                    g.FillEllipse(
                        pip, p.X - 4f + b * 6f, p.Y - 2f, 3.5f, 3.5f);
                }
            }
        }
    }

    private static Color GetProjectileColor(
        ProjectileType type,
        Color standardColor)
    {
        return type switch
        {
            ProjectileType.Cluster => Color.MediumPurple,
            ProjectileType.Ricochet => Color.OrangeRed,
            _ => standardColor
        };
    }

    private static void DrawDebris(
        Graphics g,
        List<DebrisParticle> debris)
    {
        foreach (var d in debris)
        {
            int alpha = Convert.ToInt32(255f * (1f - d.Progress * d.Progress));
            if (alpha <= 5) continue;

            GraphicsState state = g.Save();
            g.TranslateTransform(d.X, d.Y);
            g.RotateTransform(d.RotationDeg);

            using var fill = new SolidBrush(Color.FromArgb(alpha, d.Color));
            using var border = new Pen(
                Color.FromArgb(alpha, 35, 28, 22), 1f);

            float half = d.Size * 0.5f;
            g.FillRectangle(fill, -half, -half * 0.7f, d.Size, d.Size * 0.7f);
            g.DrawRectangle(
                border, -half, -half * 0.7f, d.Size, d.Size * 0.7f);

            g.Restore(state);
        }
    }

    private static void DrawExplosions(
        Graphics g,
        List<ExplosionEffect> explosions)
    {
        foreach (var ex in explosions)
        {
            float r = ex.MaxRadius * (0.35f + 0.65f * ex.Progress);
            int alpha = Convert.ToInt32(220f * (1f - ex.Progress));

            using var fill = new SolidBrush(Color.FromArgb(alpha, ex.Color));
            using var ring = new Pen(Color.FromArgb(alpha, Color.White), 2.5f);

            g.FillEllipse(
                fill, ex.Center.X - r, ex.Center.Y - r, r * 2f, r * 2f);
            g.DrawEllipse(
                ring, ex.Center.X - r, ex.Center.Y - r, r * 2f, r * 2f);
        }
    }
    private static void DrawFittedText(
        Graphics g,
        string text,
        Font font,
        Brush brush,
        RectangleF box)
    {
        box.Inflate(-4f, -2f);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        SizeF size = g.MeasureString(text, font, PointF.Empty, format);
        float scale = Math.Min(
            box.Width / size.Width,
            box.Height / size.Height);

        if (scale >= 1f)
        {
            g.DrawString(text, font, brush, box, format);
        }
        else
        {
            using var fitted = new Font(
                font.FontFamily,
                font.Size * scale * 0.95f,
                font.Style,
                GraphicsUnit.Pixel);

            g.DrawString(text, fitted, brush, box, format);
        }
    }
}
