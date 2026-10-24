using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CastleSiege;

public sealed class MainForm : Form
{
    private readonly Panel _canvas = new Panel { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _frameTimer =
        new System.Windows.Forms.Timer { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrameSeconds;

    public MainForm()
    {
        Text = "Осада замка";
        Size = new Size(1220, 740);
        Controls.Add(_canvas);
        _canvas.Paint += (_, e) => e.Graphics.Clear(Color.SkyBlue);
        _frameTimer.Tick += OnFrameTick;
        _frameTimer.Start();
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        _lastFrameSeconds = now;
        _canvas.Invalidate(false);
    }
}
