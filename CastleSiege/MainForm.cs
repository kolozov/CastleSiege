using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using CastleSiege.Audio;
using CastleSiege.Engine;
using CastleSiege.Models;
using CastleSiege.Rendering;

namespace CastleSiege;

// Главное окно игры: меню, правила, конструктор уровня, бой и игровой цикл
public sealed class MainForm : Form
{
    private sealed class BufferedCanvas : Panel
    {
        public BufferedCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable,
                true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x02000000;
                return cp;
            }
        }
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() => DoubleBuffered = true;
    }

    private sealed class ReadableButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled)
            {
                base.OnPaint(e);
                return;
            }

            // У плоской кнопки WinForms затемняет текст сильнее, чем нужно
            e.Graphics.Clear(BackColor);
            using var border = new Pen(FlatAppearance.BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }
    }

    private static readonly Color NeutralButton =
        Color.FromArgb(58, 64, 76);

    private static readonly Color MainAction =
        Color.FromArgb(43, 118, 85);

    private static readonly Color DestructiveAction =
        Color.FromArgb(134, 49, 51);

    private static readonly LocationId[] Locations =
    {
        LocationId.Plains,
        LocationId.MountainPass,
        LocationId.Fortress
    };

    private readonly SoundManager _sound = new SoundManager();
    private readonly GameRenderer _renderer = new GameRenderer();
    private readonly GameEngine _engine;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly System.Windows.Forms.Timer _frameTimer =
        new System.Windows.Forms.Timer { Interval = 16 };

    private readonly Dictionary<(float, FontStyle), Font> _fonts = new();

    private readonly BufferedCanvas _canvas =
        new BufferedCanvas { Dock = DockStyle.Fill };

    private readonly FlowLayoutPanel _battleTopPanel = MakeCommandBar();
    private readonly FlowLayoutPanel _builderTopPanel = MakeCommandBar();

    private readonly Panel _bottomBar = new Panel
    {
        Dock = DockStyle.Bottom,
        BackColor = Color.FromArgb(24, 28, 36)
    };

    private readonly Label _builderHint = new Label();
    private readonly RichTextBox _rulesText = new RichTextBox();

    private readonly BufferedPanel _menuPanel = new BufferedPanel
    {
        Size = new Size(340, 356),
        BackColor = Color.FromArgb(26, 30, 40),
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly BufferedPanel _rulesPanel = new BufferedPanel
    {
        Size = new Size(620, 420),
        BackColor = Color.FromArgb(26, 30, 40),
        BorderStyle = BorderStyle.FixedSingle,
        Visible = false
    };

    private readonly BufferedPanel _builderCenterPanel = new BufferedPanel
    {
        Size = new Size(376, 336),
        BackColor = Color.FromArgb(26, 30, 40),
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly BufferedPanel _battleSettingsPanel = new BufferedPanel
    {
        Size = new Size(300, 236),
        BackColor = Color.FromArgb(26, 30, 40),
        BorderStyle = BorderStyle.FixedSingle,
        Visible = false
    };

    private readonly BufferedPanel _gameOverPanel = new BufferedPanel
    {
        Size = new Size(500, 180),
        BackColor = Color.FromArgb(26, 30, 40),
        BorderStyle = BorderStyle.FixedSingle,
        Visible = false
    };

    private readonly Label _statusLabel = new Label();
    private readonly Label _turnLabel = new Label();
    private readonly Label _lblTotalBudget = new Label();
    private readonly Label _lblWoodBudget = new Label();
    private readonly Label _lblStoneBudget = new Label();
    private readonly Label _lblSteelBudget = new Label();
    private readonly Label _lblGameOverTitle = new Label();
    private readonly Label _lblGameOverSubtitle = new Label();

    private readonly Button _btnGameOverRematch = new Button();
    private readonly Button _btnGameOverBuilder = new Button();
    private readonly Button _btnGameOverMenu = new Button();
    private readonly Button _btnRulesBack = new Button();

    private readonly Button _btnStandard = new ReadableButton();
    private readonly Button _btnCluster = new ReadableButton();
    private readonly Button _btnRicochet = new ReadableButton();
    private readonly Button _btnBattleSettings = new ReadableButton();

    private readonly Button _btnBattleMusic = new ReadableButton();
    private readonly Button _btnMenuMusic = new ReadableButton();
    private readonly Button _btnBuilderMusic = new ReadableButton();

    private readonly Button _btnBattleSound = new Button();
    private readonly Button _btnMenuSound = new Button();
    private readonly Button _btnBuilderSound = new Button();

    private readonly Button _btnBrushKing = new Button();
    private readonly Button _btnBrushWood = new Button();
    private readonly Button _btnBrushStone = new Button();
    private readonly Button _btnBrushSteel = new Button();

    private readonly ComboBox _cmbLocation = new ComboBox();

    private double _lastFrameSeconds;
    private int _aimGesture;
    private bool _musicAvailable;
    private bool _disposed;

    public MainForm()
    {
        _engine = new GameEngine(_sound);
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Осада замка — ЛР № 3";
        ClientSize = new Size(1200, 724);
        MinimumSize = new Size(1020, 620);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        BuildLayout();
        BindEvents();
        SyncScreenVisibility();
        RefreshAudioUi();
        RefreshAllUi();
        ResumeLayout(true);

        _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
        _frameTimer.Tick += OnFrameTick;
        _frameTimer.Start();
    }

    private void BuildLayout()
    {
        _turnLabel.AutoSize = false;
        _turnLabel.Size = new Size(242, 42);
        _turnLabel.Font = UiFont(10f, FontStyle.Bold);
        _turnLabel.ForeColor = Color.White;
        _turnLabel.TextAlign = ContentAlignment.MiddleCenter;
        _turnLabel.BorderStyle = BorderStyle.FixedSingle;
        _turnLabel.Margin = new Padding(0, 2, 6, 2);

        InitButton(_btnStandard, "Ядро (∞)", 0, 0, 96, 42);
        InitButton(_btnCluster, "Кассета (2)", 0, 0, 116, 42);
        InitButton(_btnRicochet, "Рикошет (2)", 0, 0, 124, 42);
        InitButton(_btnBattleSettings, "Настройки", 0, 0, 116, 42);
        _btnBattleSettings.BackColor = Color.FromArgb(195, 145, 25);
        _btnBattleSettings.FlatAppearance.BorderColor =
            Color.FromArgb(235, 185, 45);

        _battleTopPanel.Controls.Add(
            MakeCommandGroup(
                _turnLabel,
                _btnStandard,
                _btnCluster,
                _btnRicochet,
                _btnBattleSettings));

        InitButton(_btnBattleMusic, "Музыка: ВКЛ", 35, 52, 230, 38, 9.5f);
        InitButton(_btnBattleSound, "Звук: ВКЛ", 35, 96, 230, 38, 9.5f);

        _battleSettingsPanel.Controls.AddRange(new Control[]
        {
            MakeLabel(
                "НАСТРОЙКИ", 15, 16, 270, 28, Color.Gold, 13f, true,
                ContentAlignment.MiddleCenter),
            _btnBattleMusic,
            _btnBattleSound,
            MakeButton(
                "В главное меню", 35, 140, 230, 38, NeutralButton,
                (_, _) =>
                {
                    _battleSettingsPanel.Visible = false;
                    OpenMenu();
                }, 10f),
            MakeButton(
                "Вернуться к бою", 35, 184, 230, 38, MainAction,
                (_, _) =>
                {
                    _sound.PlayUiClick();
                    _battleSettingsPanel.Visible = false;
                    _canvas.Focus();
                }, 10f)
        });

        _lblGameOverTitle.SetBounds(10, 18, 480, 36);
        _lblGameOverTitle.Font = UiFont(16f, FontStyle.Bold);
        _lblGameOverTitle.TextAlign = ContentAlignment.MiddleCenter;

        _lblGameOverSubtitle.SetBounds(10, 58, 480, 24);
        _lblGameOverSubtitle.Font = UiFont(10f);
        _lblGameOverSubtitle.ForeColor = Color.LightGray;
        _lblGameOverSubtitle.TextAlign = ContentAlignment.MiddleCenter;

        InitButton(_btnGameOverRematch, "Реванш", 22, 104, 140, 48, 10.5f);
        _btnGameOverRematch.BackColor = MainAction;
        _btnGameOverRematch.Click += (_, _) =>
        {
            _gameOverPanel.Visible = false;
            StartBattle();
        };

        InitButton(_btnGameOverBuilder,
            "Конструктор", 180, 104, 140, 48, 10.5f);
        _btnGameOverBuilder.BackColor = Color.FromArgb(41, 105, 176);
        _btnGameOverBuilder.Click += (_, _) =>
        {
            _gameOverPanel.Visible = false;
            OpenBuilder();
        };

        InitButton(_btnGameOverMenu, "В меню", 338, 104, 140, 48, 10.5f);
        _btnGameOverMenu.BackColor = NeutralButton;
        _btnGameOverMenu.Click += (_, _) =>
        {
            _gameOverPanel.Visible = false;
            OpenMenu();
        };

        _gameOverPanel.Controls.AddRange(new Control[]
        {
            _lblGameOverTitle,
            _lblGameOverSubtitle,
            _btnGameOverRematch,
            _btnGameOverBuilder,
            _btnGameOverMenu
        });

        InitButton(_btnBuilderMusic, "Музыка: ВКЛ", 0, 0, 145, 36, 8.5f);
        InitButton(_btnBuilderSound, "Звук: ВКЛ", 0, 0, 101, 36, 8.5f);

        _builderHint.Text = "Выберите блоки и расставьте замок.";
        _builderHint.Size = new Size(580, 36);
        _builderHint.Font = UiFont(10f, FontStyle.Bold);
        _builderHint.TextAlign = ContentAlignment.MiddleLeft;
        _builderHint.ForeColor = Color.WhiteSmoke;
        _builderHint.Margin = new Padding(8, 3, 8, 3);

        _builderTopPanel.Controls.Add(_builderHint);
        _builderTopPanel.Controls.Add(
            MakeCommandGroup(
                _btnBuilderMusic,
                _btnBuilderSound,
                MakeButton("В меню", 0, 0, 76, 36, NeutralButton,
                    (_, _) => OpenMenu())));

        InitButton(_btnMenuMusic, "Музыка: ВКЛ", 45, 188, 250, 42, 10.5f);
        InitButton(_btnMenuSound, "Звук: ВКЛ", 45, 240, 250, 42, 10.5f);

        _menuPanel.Controls.AddRange(new Control[]
        {
            MakeLabel(
                "ОСАДА ЗАМКА", 20, 24, 300, 42, Color.Gold, 20f, true,
                ContentAlignment.MiddleCenter),
            MakeButton(
                "Новая игра", 45, 82, 250, 46, MainAction,
                (_, _) => OpenBuilder(), 11.5f),
            _btnMenuMusic,
            _btnMenuSound,
            MakeButton(
                "Выход", 45, 292, 250, 42, DestructiveAction,
                (_, _) => Close(), 10.5f)
        });

        _cmbLocation.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbLocation.Font = UiFont(9.5f);
        _cmbLocation.SetBounds(86, 12, 168, 28);
        _cmbLocation.TabStop = false;
        _cmbLocation.Items.AddRange(new object[]
        {
            "Равнина", "Горный перевал", "Крепость"
        });
        _cmbLocation.SelectedIndex = 0;

        _lblTotalBudget.SetBounds(14, 43, 348, 31);
        _lblTotalBudget.TextAlign = ContentAlignment.MiddleLeft;
        _lblTotalBudget.ForeColor = Color.Gold;
        _lblTotalBudget.Font = UiFont(9.5f, FontStyle.Bold);

        AddBudgetRow("Дерево (1 HP):", BlockType.Wood, 76, _lblWoodBudget);
        AddBudgetRow("Камень (2 HP):", BlockType.Stone, 108, _lblStoneBudget);
        AddBudgetRow("Сталь (4 HP):", BlockType.Steel, 140, _lblSteelBudget);

        InitButton(_btnBrushKing, "Король", 14, 198, 82, 36);
        InitButton(_btnBrushWood, "Дерево", 102, 198, 82, 36);
        InitButton(_btnBrushStone, "Камень", 190, 198, 82, 36);
        InitButton(_btnBrushSteel, "Сталь", 278, 198, 82, 36);

        _builderCenterPanel.Controls.AddRange(new Control[]
        {
            MakeLabel("Локация:", 14, 15, 70, 22, Color.WhiteSmoke, 9.5f, true),
            _cmbLocation,
            MakeButton(
                "Шаблон", 264, 11, 96, 28, NeutralButton,
                (_, _) =>
                {
                    _engine.LoadBuilderPreset();
                    RefreshAllUi();
                }, 8.5f),
            _lblTotalBudget,
            MakeLabel(
                "Кисть для постройки (ЛКМ):",
                14, 176, 250, 18, Color.LightGray, 9f, true),
            _btnBrushKing,
            _btnBrushWood,
            _btnBrushStone,
            _btnBrushSteel,
            MakeButton(
                "Авторасстановка", 14, 244, 170, 32, NeutralButton,
                (_, _) =>
                {
                    _engine.AutoFillBuilder();
                    RefreshAllUi();
                }, 9f),
            MakeButton(
                "Очистить всё", 192, 244, 168, 32, DestructiveAction,
                (_, _) =>
                {
                    _engine.ClearBuilder();
                    RefreshAllUi();
                }, 9f),
            MakeButton(
                "В БОЙ!", 14, 284, 346, 40, MainAction,
                OnStartBattleClicked, 11.5f)
        });

        _canvas.Controls.AddRange(new Control[]
        {
            _menuPanel,
            _builderCenterPanel,
            _battleSettingsPanel,
            _gameOverPanel
        });

        _bottomBar.Padding = new Padding(12, 2, 12, 2);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.WhiteSmoke;
        _statusLabel.Font = UiFont(9.5f);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _bottomBar.Controls.Add(_statusLabel);
        _bottomBar.SizeChanged += (_, _) => FitStatus();

        Controls.AddRange(new Control[]
        {
            _canvas,
            _battleTopPanel,
            _builderTopPanel,
            _bottomBar
        });
    }

    private static FlowLayoutPanel MakeCommandBar()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 4),
            BackColor = Color.FromArgb(30, 34, 44)
        };
    }

    private static FlowLayoutPanel MakeCommandGroup(params Control[] controls)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        group.Controls.AddRange(controls);
        return group;
    }

    private Font UiFont(float size, FontStyle style = FontStyle.Regular)
    {
        if (!_fonts.TryGetValue((size, style), out var font))
        {
            font = new Font("Segoe UI", size, style);
            _fonts.Add((size, style), font);
        }
        return font;
    }

    private void AddBudgetRow(
        string title,
        BlockType type,
        int y,
        Label valueLabel)
    {
        valueLabel.SetBounds(210, y + 3, 46, 22);
        valueLabel.ForeColor = Color.White;
        valueLabel.Font = UiFont(10f, FontStyle.Bold);
        valueLabel.TextAlign = ContentAlignment.MiddleCenter;

        _builderCenterPanel.Controls.AddRange(new Control[]
        {
            MakeLabel(
                title, 14, y + 4, 140, 22, Color.WhiteSmoke, 9.5f, false),
            MakeButton("-", 168, y, 36, 26,
                Color.FromArgb(46, 52, 66),
                (_, _) => ChangeBudget(type, -1)),
            valueLabel,
            MakeButton("+", 262, y, 36, 26,
                Color.FromArgb(46, 52, 66),
                (_, _) => ChangeBudget(type, +1))
        });
    }

    private void ChangeBudget(BlockType type, int delta)
    {
        _engine.ChangeBuilderBudget(type, delta);
        RefreshAllUi();
    }

    private Label MakeLabel(
        string text,
        int x,
        int y,
        int w,
        int h,
        Color color,
        float fontSize,
        bool bold,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var font = UiFont(fontSize, bold ? FontStyle.Bold : FontStyle.Regular);
        var lbl = new Label
        {
            Text = text,
            ForeColor = color,
            TextAlign = align,
            Font = font
        };
        lbl.SetBounds(x, y, w, h);
        return lbl;
    }

    private Button MakeButton(
        string text,
        int x,
        int y,
        int w,
        int h,
        Color backColor,
        EventHandler onClick,
        float fontSize = 9.5f)
    {
        var btn = new Button();
        InitButton(btn, text, x, y, w, h, fontSize);
        btn.BackColor = backColor;
        btn.Click += onClick;
        return btn;
    }

    private void InitButton(
        Button btn,
        string text,
        int x,
        int y,
        int w,
        int h,
        float fontSize = 9.5f)
    {
        btn.Text = text;
        btn.SetBounds(x, y, w, h);
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 110);
        btn.BackColor = Color.FromArgb(46, 52, 66);
        btn.ForeColor = Color.White;
        btn.Font = UiFont(fontSize, FontStyle.Bold);
        btn.Cursor = Cursors.Hand;
        btn.TabStop = false;
        btn.Margin = new Padding(0, 2, 6, 2);
    }

    private void BindEvents()
    {
        _btnStandard.Click += (_, _) =>
        {
            if (_engine.TrySelectProjectile(ProjectileType.Standard))
            {
                _sound.PlayUiClick();
            }
            RefreshAllUi();
            _canvas.Focus();
        };

        _btnCluster.Click += (_, _) =>
        {
            if (_engine.TrySelectProjectile(ProjectileType.Cluster))
            {
                _sound.PlayUiClick();
            }
            RefreshAllUi();
            _canvas.Focus();
        };

        _btnRicochet.Click += (_, _) =>
        {
            if (_engine.TrySelectProjectile(ProjectileType.Ricochet))
            {
                _sound.PlayUiClick();
            }
            RefreshAllUi();
            _canvas.Focus();
        };

        _btnBattleSettings.Click += (_, _) =>
        {
            _sound.PlayUiClick();
            _battleSettingsPanel.Visible = !_battleSettingsPanel.Visible;
            if (_battleSettingsPanel.Visible)
            {
                _battleSettingsPanel.BringToFront();
                CenterOverlayPanels();
            }
            else
            {
                _canvas.Focus();
            }
        };

        _cmbLocation.SelectedIndexChanged += (_, _) =>
        {
            if (_engine.Phase != GamePhase.Builder ||
                _cmbLocation.SelectedIndex < 0)
            {
                return;
            }

            LocationId location = Locations[_cmbLocation.SelectedIndex];
            if (_engine.Builder.SelectedLocation == location) return;

            _sound.PlayUiClick();
            _engine.PreviewBuilderLocation(location);
            RefreshAllUi();
        };

        _btnBrushKing.Click += (_, _) => SelectBrush(BlockType.King);
        _btnBrushWood.Click += (_, _) => SelectBrush(BlockType.Wood);
        _btnBrushStone.Click += (_, _) => SelectBrush(BlockType.Stone);
        _btnBrushSteel.Click += (_, _) => SelectBrush(BlockType.Steel);

        EventHandler toggleMusic = (_, _) =>
        {
            _sound.PlayUiClick();
            _sound.ToggleMusic();
            RefreshAudioUi();
            _canvas.Focus();
        };

        EventHandler toggleSound = (_, _) =>
        {
            _sound.ToggleSoundEffects();
            if (_sound.SoundEffectsEnabled) _sound.PlayUiClick();
            RefreshAudioUi();
            _canvas.Focus();
        };

        foreach (var b in new[]
        {
            _btnBattleMusic, _btnMenuMusic, _btnBuilderMusic
        })
        {
            b.Click += toggleMusic;
        }

        foreach (var b in new[]
        {
            _btnBattleSound, _btnMenuSound, _btnBuilderSound
        })
        {
            b.Click += toggleSound;
        }

        _canvas.SizeChanged += (_, _) => CenterOverlayPanels();

        _canvas.Paint += (_, e) =>
        {
            float scaleX = 1f * _canvas.Width / LevelData.WorldWidth;
            float scaleY = 1f * _canvas.Height / LevelData.WorldHeight;
            e.Graphics.ScaleTransform(scaleX, scaleY);
            _renderer.Render(e.Graphics, _engine);
        };

        _canvas.MouseDown += OnCanvasMouseDown;

        _canvas.MouseMove += (_, e) =>
        {
            if (_engine.IsAiming)
            {
                _engine.UpdateAim(ToWorld(e.Location));
            }
        };

        _canvas.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !_engine.IsAiming) return;
            _engine.ReleaseAim(ToWorld(e.Location));
            _canvas.Capture = false;
            RefreshAllUi();
        };

        _canvas.MouseCaptureChanged += (_, _) =>
        {
            if (_canvas.Capture ||
                !_engine.IsAiming ||
                !IsHandleCreated ||
                _disposed)
            {
                return;
            }

            int gesture = _aimGesture;
            BeginInvoke(() =>
            {
                if (gesture == _aimGesture && !_canvas.Capture)
                {
                    CancelAim();
                }
            });
        };

        Deactivate += (_, _) => CancelAim();
        Shown += (_, _) => FitWindowToScreen();

        DpiChanged += (_, _) => BeginInvoke(() =>
        {
            FitWindowToScreen();
            CenterOverlayPanels();
            FitStatus();
        });
    }

    private void SelectBrush(BlockType brush)
    {
        _sound.PlayUiClick();
        _engine.Builder.SelectedBrush = brush;
        RefreshAllUi();
    }

    private void OpenMenu()
    {
        _sound.PlayUiClick();
        _engine.OpenMainMenu();
        SyncScreenVisibility();
        RefreshAllUi();
    }

    private void OpenBuilder()
    {
        _sound.PlayUiClick();
        _engine.OpenBuilder();
        SyncScreenVisibility();
        RefreshAllUi();
    }

    private void StartBattle()
    {
        _sound.PlayUiClick();
        _engine.StartBattleFromBuilder();
        SyncScreenVisibility();
        RefreshAllUi();
        _canvas.Focus();
    }

    private void CancelAim()
    {
        if (_disposed || !_engine.IsAiming) return;
        _engine.CancelAim();
        _canvas.Capture = false;
        RefreshAllUi();
    }

    private void OnStartBattleClicked(object? sender, EventArgs e)
    {
        if (!_engine.ValidateBuilder(out string? unusedWarning))
        {
            RefreshAllUi();
            MessageBox.Show(
                this,
                _engine.StatusMessage,
                "Нельзя начать бой",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (unusedWarning != null)
        {
            _sound.PlayUiClick();
            DialogResult res = MessageBox.Show(
                this,
                unusedWarning,
                "Не все блоки использованы",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res != DialogResult.Yes) return;
        }

        StartBattle();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool inBattle =
            _engine.Phase != GamePhase.MainMenu &&
            _engine.Phase != GamePhase.Builder &&
            _engine.Phase != GamePhase.GameOver;

        if (keyData == Keys.Escape && inBattle)
        {
            if (_engine.IsAiming)
            {
                _sound.PlayUiClick();
                CancelAim();
                return true;
            }

            _sound.PlayUiClick();
            _battleSettingsPanel.Visible = !_battleSettingsPanel.Visible;
            if (_battleSettingsPanel.Visible)
            {
                _battleSettingsPanel.BringToFront();
                CenterOverlayPanels();
            }
            else
            {
                _canvas.Focus();
            }
            return true;
        }

        if (keyData == Keys.Space && _engine.Phase == GamePhase.Flying)
        {
            _engine.TrySplitCluster();
            RefreshAllUi();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = Convert.ToSingle(now - _lastFrameSeconds);
        _lastFrameSeconds = now;

        GamePhase previousPhase = _engine.Phase;
        string previousStatus = _engine.StatusMessage;
        int prevCluster = _engine.GetAmmo(
            _engine.CurrentPlayer, ProjectileType.Cluster);
        int prevRicochet = _engine.GetAmmo(
            _engine.CurrentPlayer, ProjectileType.Ricochet);

        _engine.Update(dt);

        int curCluster = _engine.GetAmmo(
            _engine.CurrentPlayer, ProjectileType.Cluster);
        int curRicochet = _engine.GetAmmo(
            _engine.CurrentPlayer, ProjectileType.Ricochet);

        bool stateChanged =
            previousPhase != _engine.Phase ||
            previousStatus != _engine.StatusMessage ||
            prevCluster != curCluster ||
            prevRicochet != curRicochet;

        if (stateChanged) RefreshAllUi();
        if (_musicAvailable != _sound.MusicAvailable) RefreshAudioUi();
        _canvas.Invalidate(false);
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        _canvas.Focus();
        if (_battleSettingsPanel.Visible)
        {
            _battleSettingsPanel.Visible = false;
            return;
        }

        PointF worldPt = ToWorld(e.Location);

        if (_engine.Phase == GamePhase.Builder)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                _engine.HandleBuilderClick(
                    worldPt, e.Button == MouseButtons.Right);
                RefreshAllUi();
            }
            else
            {
                _sound.PlayError();
            }
            return;
        }

        if (_engine.Phase == GamePhase.MainMenu
            || e.Button != MouseButtons.Left)
        {
            if (e.Button == MouseButtons.Right && _engine.IsAiming)
            {
                _sound.PlayUiClick();
                CancelAim();
            }
            else
            {
                _sound.PlayError();
            }
            return;
        }

        if (_engine.Phase == GamePhase.Flying)
        {
            _engine.TrySplitCluster();
        }
        else if (_engine.TryBeginAim(worldPt))
        {
            _aimGesture++;
            _canvas.Capture = true;
        }
        RefreshAllUi();
    }

    private PointF ToWorld(Point p)
    {
        float x = 1f * p.X * LevelData.WorldWidth /
            Math.Max(1, _canvas.Width);
        float y = 1f * p.Y * LevelData.WorldHeight /
            Math.Max(1, _canvas.Height);
        return new PointF(x, y);
    }

    private void SyncScreenVisibility()
    {
        bool isMenu = _engine.Phase == GamePhase.MainMenu;
        bool isBuilder = _engine.Phase == GamePhase.Builder;
        bool isGameOver = _engine.Phase == GamePhase.GameOver;

        _menuPanel.Visible = isMenu;
        _builderTopPanel.Visible = isBuilder;
        _builderCenterPanel.Visible = isBuilder;
        _battleTopPanel.Visible = !isMenu && !isBuilder;
        _battleSettingsPanel.Visible = false;
        _gameOverPanel.Visible = isGameOver;

        CenterOverlayPanels();
    }

    private void FitWindowToScreen()
    {
        if (_disposed || WindowState != FormWindowState.Normal) return;

        Rectangle area = Screen.FromControl(this).WorkingArea;
        int width = Math.Max(MinimumSize.Width, Math.Min(Width, area.Width));
        int height = Math.Max(
            MinimumSize.Height, Math.Min(Height, area.Height));

        if (width == Width && height == Height) return;

        int x = Math.Clamp(
            Left, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(
            Top, area.Top, Math.Max(area.Top, area.Bottom - height));

        SetBounds(x, y, width, height);
    }

    private void CenterOverlayPanels()
    {
        int margin = LogicalToDeviceUnits(10);

        _menuPanel.Location = new Point(
            Math.Max(margin, (_canvas.Width - _menuPanel.Width) / 2),
            Math.Max(margin, (_canvas.Height - _menuPanel.Height) / 2 -
                LogicalToDeviceUnits(20)));

        _builderCenterPanel.Location = new Point(
            Math.Max(margin, (_canvas.Width - _builderCenterPanel.Width) / 2),
            LogicalToDeviceUnits(12));

        _battleSettingsPanel.Location = new Point(
            Math.Max(margin,
                (_canvas.Width - _battleSettingsPanel.Width) / 2),
            Math.Max(margin,
                (_canvas.Height - _battleSettingsPanel.Height) / 2));

        _gameOverPanel.Location = new Point(
            Math.Max(margin, (_canvas.Width - _gameOverPanel.Width) / 2),
            Math.Max(margin, (_canvas.Height - _gameOverPanel.Height) / 2));
    }

    private void ShowStatus(string text)
    {
        _statusLabel.Text = text;
        FitStatus();
    }

    private void FitStatus()
    {
        int width =
            _bottomBar.ClientSize.Width - _bottomBar.Padding.Horizontal;
        if (width <= 0) return;

        Size size = TextRenderer.MeasureText(
            _statusLabel.Text,
            _statusLabel.Font,
            new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak |
            TextFormatFlags.TextBoxControl |
            TextFormatFlags.NoPrefix);

        _bottomBar.Height = Math.Max(
            LogicalToDeviceUnits(30),
            size.Height + _bottomBar.Padding.Vertical);
    }

    private void RefreshAudioUi()
    {
        _musicAvailable = _sound.MusicAvailable;
        bool musicEnabled = _sound.MusicEnabled;
        bool soundEnabled = _sound.SoundEffectsEnabled;
        Color audioOn = Color.FromArgb(32, 111, 98);

        foreach (var button in new[]
        {
            _btnMenuMusic, _btnBuilderMusic, _btnBattleMusic
        })
        {
            button.Enabled = _musicAvailable;
            button.Text = !_musicAvailable
                ? "Музыка недоступна"
                : (musicEnabled ? "Музыка: ВКЛ" : "Музыка: ВЫКЛ");
            button.BackColor = musicEnabled && _musicAvailable
                ? audioOn
                : NeutralButton;
        }

        foreach (var button in new[]
        {
            _btnMenuSound, _btnBuilderSound, _btnBattleSound
        })
        {
            button.Text = soundEnabled ? "Звук: ВКЛ" : "Звук: ВЫКЛ";
            button.BackColor = soundEnabled ? audioOn : NeutralButton;
        }
    }

    private void RefreshAllUi()
    {
        if (_engine.Phase == GamePhase.Builder)
        {
            var b = _engine.Builder;
            int locIndex = Array.IndexOf(Locations, b.SelectedLocation);
            if (_cmbLocation.SelectedIndex != locIndex)
            {
                _cmbLocation.SelectedIndex = locIndex;
            }

            _lblTotalBudget.Text =
                $"Запас на игрока: {b.TotalBudget}/" +
                $"{CastleBuilder.MaxTotalBlocks} (+ Король)";

            _lblWoodBudget.Text = b.WoodBudget.ToString();
            _lblStoneBudget.Text = b.StoneBudget.ToString();
            _lblSteelBudget.Text = b.SteelBudget.ToString();

            StyleBrush(_btnBrushKing,
                b.SelectedBrush == BlockType.King,
                Color.FromArgb(148, 112, 24));
            StyleBrush(_btnBrushWood,
                b.SelectedBrush == BlockType.Wood,
                Color.FromArgb(145, 92, 42));
            StyleBrush(_btnBrushStone,
                b.SelectedBrush == BlockType.Stone,
                Color.FromArgb(105, 112, 122));
            StyleBrush(_btnBrushSteel,
                b.SelectedBrush == BlockType.Steel,
                Color.FromArgb(72, 92, 112));
        }
        else if (_engine.Phase != GamePhase.MainMenu)
        {
            if (_engine.Phase == GamePhase.GameOver)
            {
                _turnLabel.Text = _engine.IsDraw
                    ? "НИЧЬЯ!"
                    : (_engine.Winner == PlayerId.Blue
                        ? "ПОБЕДА СИНЕГО!"
                        : "ПОБЕДА КРАСНОГО!");
                _turnLabel.BackColor = Color.FromArgb(140, 105, 20);

                _lblGameOverTitle.Text = _engine.IsDraw
                    ? "БОЕВАЯ НИЧЬЯ!"
                    : (_engine.Winner == PlayerId.Blue
                        ? "ПОБЕДИЛ СИНИЙ ЗАМОК!"
                        : "ПОБЕДИЛ КРАСНЫЙ ЗАМОК!");

                _lblGameOverTitle.ForeColor = _engine.IsDraw
                    ? Color.Gold
                    : (_engine.Winner == PlayerId.Blue
                        ? Color.DeepSkyBlue
                        : Color.OrangeRed);

                _lblGameOverSubtitle.Text =
                    $"Раундов сыграно: {_engine.RoundNumber}";

                if (!_gameOverPanel.Visible)
                {
                    _gameOverPanel.Visible = true;
                    _gameOverPanel.BringToFront();
                    CenterOverlayPanels();
                }
            }
            else
            {
                if (_gameOverPanel.Visible) _gameOverPanel.Visible = false;

                string playerText = _engine.CurrentPlayer == PlayerId.Blue
                    ? "СИНИЙ"
                    : "КРАСНЫЙ";
                _turnLabel.Text =
                    $"Ход: {playerText}  •  Раунд {_engine.RoundNumber}";

                _turnLabel.BackColor =
                    _engine.CurrentPlayer == PlayerId.Blue
                        ? Color.FromArgb(35, 85, 175)
                        : Color.FromArgb(170, 42, 42);
            }

            int cluster = _engine.GetAmmo(
                _engine.CurrentPlayer, ProjectileType.Cluster);
            int ricochet = _engine.GetAmmo(
                _engine.CurrentPlayer, ProjectileType.Ricochet);

            _btnCluster.Text = $"Кассета ({cluster})";
            _btnRicochet.Text = $"Рикошет ({ricochet})";

            bool canSelect = _engine.Phase == GamePhase.Aiming;
            StyleAmmo(
                _btnStandard,
                _engine.SelectedProjectile == ProjectileType.Standard,
                canSelect);
            StyleAmmo(
                _btnCluster,
                _engine.SelectedProjectile == ProjectileType.Cluster,
                canSelect && cluster > 0);
            StyleAmmo(
                _btnRicochet,
                _engine.SelectedProjectile == ProjectileType.Ricochet,
                canSelect && ricochet > 0);
        }

        ShowStatus(_engine.StatusMessage);
        _canvas.Invalidate(false);
    }

    private static void StyleBrush(
        Button btn,
        bool selected,
        Color baseColor)
    {
        btn.BackColor = baseColor;
        btn.FlatAppearance.BorderColor =
            selected ? Color.Gold : Color.FromArgb(80, 90, 110);
        btn.FlatAppearance.BorderSize = selected ? 2 : 1;
    }

    private static void StyleAmmo(
        Button button,
        bool selected,
        bool enabled)
    {
        button.Enabled = enabled;
        button.BackColor = !enabled
            ? Color.FromArgb(38, 40, 46)
            : (selected ? Color.FromArgb(81, 72, 55) : NeutralButton);
        button.ForeColor = enabled ? Color.White : Color.LightGray;
        button.FlatAppearance.BorderColor = enabled && selected
            ? Color.Gold
            : Color.FromArgb(80, 90, 110);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;
        _frameTimer.Stop();
        _frameTimer.Dispose();
        _sound.Dispose();
        _engine.Dispose();
        base.Dispose(disposing);
        _renderer.Dispose();
        foreach (var font in _fonts.Values) font.Dispose();
    }
}
