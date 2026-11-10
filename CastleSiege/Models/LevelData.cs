using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CastleSiege.Models;

// Данные уровня: 2D-рельеф, замки, катапульты и точки для коробок
public sealed class LevelData : IDisposable
{
    public const int WorldWidth = 1200;
    public const int WorldHeight = 620;
    public const int GridCols = 4;
    public const int GridRows = 4;
    public const int DestructibleMinX = 365;
    public const int DestructibleMaxX = 835;
    public const int DestructibleMaxY = WorldHeight - 1;

    public const float BaseGroundY = 540f;
    public const float Gravity = 430f;
    public const float CastleLeftStartX = 95f;
    public const float BlockWidth = 40f;
    public const float BlockHeight = 34f;
    public const float ColGap = 2f;

    public LocationId Location { get; }
    public string Title { get; }
    public float[] TerrainY { get; }
    public int TerrainRevision { get; private set; }
    public PointF BlueCatapult { get; }
    public PointF RedCatapult { get; }
    public List<Block> Blocks { get; }
    public List<CrateSlot> CrateSlots { get; }
    public Bitmap TerrainBitmap { get; }

    private readonly bool[,] _solid;

    private static readonly PointF[] LitRockFace =
    {
        new(454, 540),
        new(495, 379),
        new(563, 298),
        new(600, 293),
        new(621, 377),
        new(594, 540)
    };

    private static readonly PointF[] ShadedRockFace =
    {
        new(594, 540),
        new(621, 377),
        new(600, 293),
        new(679, 309),
        new(747, 540)
    };

    private LevelData(
        LocationId loc,
        string title,
        float[] terrainY,
        bool[,] solid,
        Bitmap terrainBitmap,
        List<Block> blocks,
        List<CrateSlot> slots)
    {
        Location = loc;
        Title = title;
        TerrainY = terrainY;
        _solid = solid;
        TerrainBitmap = terrainBitmap;
        Blocks = blocks;
        CrateSlots = slots;
        BlueCatapult = new PointF(305f, BaseGroundY - 18f);
        RedCatapult = new PointF(WorldWidth - 305f, BaseGroundY - 18f);
    }

    public static LevelData Create(LocationId location)
    {
        CreateDefaultGrids(location, out var blue, out var red);
        return CreateWithCustomCastles(location, blue, red);
    }

    public static LevelData CreateWithCustomCastles(
        LocationId location,
        BlockType?[,] blueGrid,
        BlockType?[,] redGrid)
    {
        float[] terrain = new float[WorldWidth];
        Array.Fill(terrain, BaseGroundY);
        string title;
        List<CrateSlot> slots;

        if (location == LocationId.MountainPass)
        {
            title = "Горный перевал";
            AddSmoothHill(terrain, 600, 145, 245f);
            slots = MakeSlots(
                terrain,
                new int[] { 425, 600, 775 },
                new float[] { 455f, 600f, 745f },
                new float[] { 210f, 125f, 210f });
        }
        else if (location == LocationId.Fortress)
        {
            title = "Крепость";
            AddSmoothHill(terrain, 435, 55, 34f);
            AddSmoothHill(terrain, 765, 55, 34f);
            slots = MakeSlots(
                terrain,
                new int[] { 435, 600, 765 },
                new float[] { 465f, 600f, 735f },
                new float[] { 225f, 160f, 225f });
        }
        else
        {
            title = "Равнина";
            AddSmoothHill(terrain, 600, 130, 68f);
            slots = MakeSlots(
                terrain,
                new int[] { 435, 600, 765 },
                new float[] { 470f, 600f, 730f },
                new float[] { 240f, 165f, 240f });
        }

        bool[,] solid = new bool[WorldWidth, WorldHeight];
        for (int x = 0; x < WorldWidth; x++)
        {
            int ty = Math.Clamp(
                Convert.ToInt32(MathF.Round(terrain[x])), 0, WorldHeight);
            for (int y = ty; y < WorldHeight; y++)
            {
                solid[x, y] = true;
            }
        }

        Bitmap terrainBitmap = CreateTerrainBitmap(location, terrain);
        return new LevelData(
            location,
            title,
            terrain,
            solid,
            terrainBitmap,
            BuildBlocks(blueGrid, redGrid),
            slots);
    }

    public static LevelData CreateFlat(
        BlockType?[,]? blueGrid = null,
        BlockType?[,]? redGrid = null,
        LocationId location = LocationId.Plains,
        bool createBitmap = true)
    {
        blueGrid ??= new BlockType?[GridCols, GridRows];
        redGrid ??= new BlockType?[GridCols, GridRows];
        float[] terrain = new float[WorldWidth];
        Array.Fill(terrain, BaseGroundY);

        bool[,] solid = new bool[WorldWidth, WorldHeight];
        int ty = Convert.ToInt32(BaseGroundY);
        for (int x = 0; x < WorldWidth; x++)
        {
            for (int y = ty; y < WorldHeight; y++)
            {
                solid[x, y] = true;
            }
        }

        var slots = MakeSlots(
            terrain,
            new int[] { 435, 600, 765 },
            new float[] { 470f, 600f, 730f },
            new float[] { 240f, 165f, 240f });

        Bitmap terrainBitmap = createBitmap
            ? CreateTerrainBitmap(LocationId.Plains, terrain)
            : new Bitmap(1, 1);

        return new LevelData(
            location,
            "Равнина",
            terrain,
            solid,
            terrainBitmap,
            BuildBlocks(blueGrid, redGrid),
            slots);
    }

    private static Bitmap CreateTerrainBitmap(
        LocationId location,
        float[] terrain)
    {
        var bmp = new Bitmap(
            WorldWidth, WorldHeight, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var poly = new PointF[WorldWidth + 4];
        poly[0] = new PointF(-24f, WorldHeight + 24f);
        poly[1] = new PointF(-24f, terrain[0]);
        for (int x = 0; x < WorldWidth; x++)
        {
            poly[x + 2] = new PointF(x, terrain[x]);
        }
        poly[WorldWidth + 2] = new PointF(WorldWidth + 24f, terrain[^1]);
        poly[WorldWidth + 3] = new PointF(
            WorldWidth + 24f, WorldHeight + 24f);

        Color earthTop = location == LocationId.MountainPass
            ? Color.FromArgb(110, 106, 102)
            : Color.FromArgb(116, 82, 52);

        using (var earthBrush = new LinearGradientBrush(
            new PointF(0, 280),
            new PointF(0, WorldHeight),
            earthTop,
            Color.FromArgb(66, 46, 32)))
        {
            g.FillPolygon(earthBrush, poly);
        }

        if (location == LocationId.MountainPass)
        {
            using var outline = new GraphicsPath();
            outline.AddPolygon(poly);
            GraphicsState state = g.Save();
            g.SetClip(outline, CombineMode.Intersect);

            var summit = new PointF(0, 293);
            var baseOfRock = new PointF(0, 540);
            using var light = new LinearGradientBrush(
                summit,
                baseOfRock,
                Color.FromArgb(182, 169, 146),
                Color.FromArgb(108, 101, 88));
            using var shadow = new LinearGradientBrush(
                summit,
                baseOfRock,
                Color.FromArgb(113, 113, 124),
                Color.FromArgb(78, 75, 73));

            g.FillPolygon(light, LitRockFace);
            g.FillPolygon(shadow, ShadedRockFace);
            g.Restore(state);
        }

        Color surfaceColor = Color.FromArgb(76, 143, 78);
        float penWidth = 6f;
        if (location == LocationId.MountainPass)
        {
            surfaceColor = Color.FromArgb(171, 177, 173);
            penWidth = 4f;
        }
        else if (location == LocationId.Fortress)
        {
            surfaceColor = Color.FromArgb(103, 128, 87);
        }

        var surfaceLine = new PointF[WorldWidth + 2];
        surfaceLine[0] = new PointF(-24f, terrain[0]);
        for (int x = 0; x < WorldWidth; x++)
        {
            surfaceLine[x + 1] = new PointF(x, terrain[x]);
        }
        surfaceLine[^1] = new PointF(WorldWidth + 24f, terrain[^1]);

        using (var surfacePen = new Pen(surfaceColor, penWidth)
        {
            LineJoin = LineJoin.Round
        })
        {
            g.DrawLines(surfacePen, surfaceLine);
        }

        return bmp;
    }

    // Стандартный шаблон сетки 4×4 для выбранной локации
    public static void CreateDefaultGrids(
        LocationId location,
        out BlockType?[,] blue,
        out BlockType?[,] red)
    {
        blue = new BlockType?[GridCols, GridRows];
        red = new BlockType?[GridCols, GridRows];
        if (location == LocationId.MountainPass)
        {
            SetCol(blue, 1,
                BlockType.Stone,
                BlockType.King,
                BlockType.Wood,
                BlockType.Stone);
            SetCol(blue, 2,
                BlockType.Stone,
                BlockType.Wood,
                BlockType.Wood,
                BlockType.Stone);
        }
        else if (location == LocationId.Fortress)
        {
            SetCol(blue, 1,
                BlockType.Stone,
                BlockType.Stone,
                BlockType.Wood,
                BlockType.Steel);
            SetCol(blue, 2,
                BlockType.Steel,
                BlockType.King,
                BlockType.Wood,
                BlockType.Steel);
            SetCol(blue, 3,
                BlockType.Stone,
                BlockType.Wood,
                BlockType.Stone,
                BlockType.Steel);
        }
        else
        {
            SetCol(blue, 1,
                BlockType.Wood,
                BlockType.Stone,
                BlockType.Wood);
            SetCol(blue, 2,
                BlockType.Stone,
                BlockType.King,
                BlockType.Stone);
            SetCol(blue, 3,
                BlockType.Wood,
                BlockType.Wood,
                BlockType.Wood);
        }

        for (int c = 0; c < GridCols; c++)
        {
            for (int r = 0; r < GridRows; r++)
            {
                red[GridCols - 1 - c, r] = blue[c, r];
            }
        }
    }

    // Вырезает круглую воронку в центральной зоне гор в стиле Worms
    public void CarveCrater(float centerX, float centerY, float radius)
    {
        if (radius <= 2f) return;
        int startX = Math.Max(0,
            Convert.ToInt32(MathF.Floor(centerX - radius)));
        int endX = Math.Min(WorldWidth - 1,
            Convert.ToInt32(MathF.Ceiling(centerX + radius)));
        if (startX > endX) return;

        int startY = Math.Max(0,
            Convert.ToInt32(MathF.Floor(centerY - radius)));
        int endY = Math.Min(DestructibleMaxY,
            Convert.ToInt32(MathF.Ceiling(centerY + radius)));
        if (startY > endY) return;

        bool changed = false;
        float rSq = radius * radius;
        for (int x = startX; x <= endX; x++)
        {
            float dx = x - centerX;
            float dxSq = dx * dx;
            for (int y = startY; y <= endY; y++)
            {
                float dy = y - centerY;
                if (dxSq + dy * dy <= rSq)
                {
                    if (_solid[x, y])
                    {
                        _solid[x, y] = false;
                        changed = true;
                    }
                }
            }
        }

        if (!changed) return;

        if (TerrainBitmap.Width >= WorldWidth &&
            TerrainBitmap.Height >= WorldHeight)
        {
            using (var g = Graphics.FromImage(TerrainBitmap))
            {
                var clipRect = new Rectangle(0, 0, WorldWidth, WorldHeight);

                g.SetClip(clipRect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceCopy;
                using var clearBrush = new SolidBrush(Color.Transparent);
                g.FillEllipse(
                    clearBrush,
                    centerX - radius,
                    centerY - radius,
                    radius * 2f,
                    radius * 2f);
            }
        }

        TerrainRevision++;

        for (int x = startX; x <= endX; x++)
        {
            TerrainY[x] = GetGroundY(x, 0f);
        }

        foreach (var slot in CrateSlots)
        {
            if (!slot.IsAirborne && slot.ActiveCrate != null)
            {
                slot.ActiveCrate.BasePosition = GetCurrentSlotPosition(slot);
            }
        }
    }

    public bool IsSolid(int x, int y)
    {
        if (x < 0 || x >= WorldWidth || y < 0 || y >= WorldHeight)
        {
            return false;
        }
        return _solid[x, y];
    }

    public bool IsSolid(float x, float y)
    {
        return IsSolid(
            Convert.ToInt32(MathF.Round(x)),
            Convert.ToInt32(MathF.Round(y)));
    }

    public float GetGroundY(float x, float fromY = 0f)
    {
        int ix = Math.Clamp(
            Convert.ToInt32(MathF.Round(x)), 0, WorldWidth - 1);
        int startY = Math.Clamp(
            Convert.ToInt32(MathF.Floor(fromY)), 0, WorldHeight - 1);

        for (int y = startY; y < WorldHeight; y++)
        {
            if (_solid[ix, y]) return y;
        }
        return BaseGroundY;
    }

    public float GetTerrainY(float x) => GetGroundY(x, 0f);

    public PointF GetCurrentSlotPosition(CrateSlot slot)
    {
        if (slot.IsAirborne) return slot.Position;
        return new PointF(
            slot.Position.X,
            GetGroundY(slot.Position.X, 0f) - 18f);
    }

    private static List<CrateSlot> MakeSlots(
        float[] terrain,
        int[] groundXs,
        float[] airXs,
        float[] airYs)
    {
        var list = new List<CrateSlot>(6);
        foreach (int gx in groundXs)
        {
            list.Add(new CrateSlot(
                new PointF(gx, terrain[gx] - 18f), false));
        }
        for (int i = 0; i < airXs.Length; i++)
        {
            list.Add(new CrateSlot(new PointF(airXs[i], airYs[i]), true));
        }
        return list;
    }

    private static void AddSmoothHill(
        float[] terrain,
        int centerX,
        int halfWidth,
        float height)
    {
        int start = Math.Max(0, centerX - halfWidth);
        int end = Math.Min(WorldWidth - 1, centerX + halfWidth);

        for (int x = start; x <= end; x++)
        {
            float offset = height * 0.5f *
                (1f + MathF.Cos(MathF.PI * (x - centerX) / halfWidth));
            terrain[x] = MathF.Min(terrain[x], BaseGroundY - offset);
        }
    }

    private static void SetCol(
        BlockType?[,] grid,
        int col,
        params BlockType[] rows)
    {
        for (int r = 0; r < rows.Length && r < GridRows; r++)
        {
            grid[col, r] = rows[r];
        }
    }

    private static List<Block> BuildBlocks(
        BlockType?[,] blueGrid,
        BlockType?[,] redGrid)
    {
        var blocks = new List<Block>();
        float stepX = BlockWidth + ColGap;
        float rightStartX = WorldWidth - CastleLeftStartX
            - (GridCols * BlockWidth + (GridCols - 1) * ColGap);

        for (int c = 0; c < GridCols; c++)
        {
            float bx = CastleLeftStartX + c * stepX;
            float rx = rightStartX + c * stepX;

            for (int r = 0; r < GridRows; r++)
            {
                float y = BaseGroundY - (r + 1) * BlockHeight;
                var blueCell = blueGrid[c, r];
                var redCell = redGrid[c, r];

                if (blueCell != null)
                {
                    blocks.Add(new Block(
                        PlayerId.Blue,
                        blueCell.Value,
                        c,
                        bx,
                        y,
                        BlockWidth,
                        BlockHeight));
                }

                if (redCell != null)
                {
                    blocks.Add(new Block(
                        PlayerId.Red,
                        redCell.Value,
                        c,
                        rx,
                        y,
                        BlockWidth,
                        BlockHeight));
                }
            }
        }
        return blocks;
    }

    public void Dispose()
    {
        TerrainBitmap?.Dispose();
    }
}
