using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class SnowflakesOcean
{
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

    const int GWL_STYLE = -16;
    const int WS_CHILD = 0x40000000;

    public static List<OceanForm> forms = new List<OceanForm>();

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "/s";

        if (mode == "/c")
        {
            MessageBox.Show("Snowflakes Ocean Screensaver\n\n" +
                "Snow falls gently onto a dark ocean.\n" +
                "Multi-monitor support.",
                "Snowflakes Ocean", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        else if (mode == "/p" && args.Length > 1)
        {
            IntPtr previewHwnd = IntPtr.Zero;
            try { previewHwnd = new IntPtr(long.Parse(args[1])); } catch { }
            RunPreview(previewHwnd);
        }
        else
        {
            RunScreensaver();
        }
    }

    static void RunScreensaver()
    {
        foreach (Screen screen in Screen.AllScreens)
        {
            var form = new OceanForm();
            form.Bounds = screen.Bounds;
            form.IsPreview = false;
            forms.Add(form);
            form.Show();
        }
        Application.Run();
    }

    static void RunPreview(IntPtr previewHwnd)
    {
        var form = new OceanForm();
        form.IsPreview = true;

        if (previewHwnd != IntPtr.Zero)
        {
            RECT rect;
            GetClientRect(previewHwnd, out rect);
            form.Size = new Size(rect.right - rect.left, rect.bottom - rect.top);
            form.Location = Point.Empty;
            SetWindowLong(form.Handle, GWL_STYLE,
                GetWindowLong(form.Handle, GWL_STYLE) | WS_CHILD);
            SetParent(form.Handle, previewHwnd);
        }
        else
        {
            form.Size = new Size(800, 600);
        }
        form.ShowDialog();
    }
}

class OceanForm : Form
{
    public bool IsPreview = false;

    class Flake
    {
        public float X, Y;
        public float Size, Speed;
        public float Wind, WindResponse;
        public float WanderPhase, WanderSpeed, WanderAmp;
        public float Opacity, Rotation, RotSpeed;
    }

    class Ripple
    {
        public float X, Y;
        public float Radius, MaxRadius;
        public float Life, MaxLife;
    }

    class Star
    {
        public float X, Y;
        public float Brightness, Speed, Phase;
        public float Size;
    }

    class Meteor
    {
        public float X, Y;
        public float Vx, Vy;
        public float Life, MaxLife;
        public float Length;
        public float Brightness;
    }

    class DiamondSpark
    {
        public float X, Y;
        public float Phase, Speed;
        public float Size;
        public float MaxBrightness;
    }

    List<Flake> flakes = new List<Flake>();
    List<Ripple> ripples = new List<Ripple>();
    List<Star> stars = new List<Star>();
    List<Meteor> meteors = new List<Meteor>();
    List<DiamondSpark> diamondSparks = new List<DiamondSpark>();

    float oceanTop; // Y position of ocean surface (fraction of height)
    float waveTime;
    Random rng = new Random();
    DateTime startTime;
    bool closing;
    int initialScreenCount;
    System.Threading.Timer watchdog;
    Timer timer;

    public OceanForm()
    {
        startTime = DateTime.Now;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(6, 8, 20);

        oceanTop = 0.89f; // horizon at 89% from top (11% ocean)

        timer = new Timer();
        timer.Interval = 33;
        timer.Tick += (s, e) => { OnFrame(); };
        timer.Start();

        initialScreenCount = Screen.AllScreens.Length;
        watchdog = new System.Threading.Timer(_ =>
        {
            try
            {
                if (Screen.AllScreens.Length != initialScreenCount)
                    Environment.Exit(0);
            }
            catch { Environment.Exit(0); }
        }, null, 500, 500);

        Deactivate += (s, e) =>
        {
            if (IsPreview || IsDisposed || closing) return;
            if (SnowflakesOcean.forms.Count > 1) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    try { if (!IsDisposed) { Activate(); BringToFront(); } } catch { }
                }));
            }
            catch { }
        };

        FormClosing += (s, e) =>
        {
            if (!closing) { e.Cancel = true; }
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!IsPreview) Cursor.Hide();
    }

    void InitStars()
    {
        int starCount = Width * Height / 1600;
        for (int i = 0; i < starCount; i++)
        {
            stars.Add(new Star
            {
                X = (float)rng.NextDouble() * Width,
                Y = (float)rng.NextDouble() * Height * oceanTop,
                Brightness = 0.4f + (float)rng.NextDouble() * 0.6f,
                Speed = 0.5f + (float)rng.NextDouble() * 2.5f,
                Phase = (float)rng.NextDouble() * (float)Math.PI * 2,
                Size = 0.6f + (float)rng.NextDouble() * 2.2f
            });
        }
    }

    void InitFlakes()
    {
        int flakeCount = Math.Min(600, Math.Max(200, Width * Height / 2500));
        for (int i = 0; i < flakeCount; i++)
        {
            var f = new Flake();
            ResetFlake(f, true);
            flakes.Add(f);
        }
    }

    void ResetFlake(Flake f, bool init)
    {
        f.X = (float)rng.NextDouble() * Width * 1.1f - Width * 0.05f;
        f.Y = init
            ? -10 - (float)rng.NextDouble() * Height * 0.8f
            : -8 - (float)rng.NextDouble() * 30;
        f.Size = 0.8f + (float)rng.NextDouble() * 3.5f;
        f.Speed = 1.5f + (float)rng.NextDouble() * 4.5f;
        f.Wind = 0;
        f.WindResponse = 0.2f + (float)rng.NextDouble() * 1.2f;
        f.WanderPhase = (float)rng.NextDouble() * (float)Math.PI * 2;
        f.WanderSpeed = 0.004f + (float)rng.NextDouble() * 0.018f;
        f.WanderAmp = 0.05f + (float)rng.NextDouble() * 0.25f;
        f.Opacity = 0.4f + (float)rng.NextDouble() * 0.6f;
        f.Rotation = (float)rng.NextDouble() * (float)Math.PI * 2;
        f.RotSpeed = ((float)rng.NextDouble() - 0.5f) * 0.015f;
    }

    void InitDiamondSparks()
    {
        int count = Width / 60;
        float horizonY = Height * oceanTop;
        for (int i = 0; i < count; i++)
        {
            diamondSparks.Add(new DiamondSpark
            {
                X = (float)rng.NextDouble() * Width,
                Y = horizonY + 20 + (float)rng.NextDouble() * (Height - horizonY - 40),
                Phase = (float)rng.NextDouble() * (float)Math.PI * 2,
                Speed = 0.3f + (float)rng.NextDouble() * 0.9f,
                Size = 8f + (float)rng.NextDouble() * 14f,
                MaxBrightness = 0.4f + (float)rng.NextDouble() * 0.6f
            });
        }
    }

    float OceanSurfaceY(float x, float t)
    {
        float horizonY = Height * oceanTop;
        float w1 = (float)Math.Sin(x * 0.015f + t * 0.6f) * 14f;
        float w2 = (float)Math.Sin(x * 0.032f + t * 0.85f) * 9f;
        float w3 = (float)Math.Sin(x * 0.07f + t * 1.3f) * 4.5f;
        float w4 = (float)Math.Sin(x * 0.12f + t * 1.7f) * 2.5f;
        return horizonY + w1 + w2 + w3 + w4;
    }

    void OnFrame()
    {
        if (closing || Width <= 0 || Height <= 0) return;
        if (flakes.Count == 0) InitFlakes();
        if (stars.Count == 0) InitStars();
        if (diamondSparks.Count == 0) InitDiamondSparks();

        float elapsed = (float)(DateTime.Now - startTime).TotalMilliseconds;
        waveTime = elapsed * 0.001f;

        // ── Update flakes ──
        foreach (var f in flakes)
        {
            f.WanderPhase += f.WanderSpeed;
            float wander = (float)Math.Sin(f.WanderPhase) * f.WanderAmp;
            float windEffect = (float)(Math.Sin(waveTime * 0.45f + f.Y * 0.003f) * 0.35f
                               + Math.Sin(waveTime * 0.78f) * 0.22f) * f.WindResponse;
            f.Wind += (windEffect - f.Wind) * 0.03f;
            f.X += f.Wind + wander;
            f.Y += f.Speed + Math.Abs(f.Wind) * 0.5f;
            f.Rotation += f.RotSpeed;

            if (f.X > Width + 15) f.X = -15;
            if (f.X < -15) f.X = Width + 15;

            // Check ocean collision
            float surfaceY = OceanSurfaceY(f.X, waveTime);
            if (f.Y >= surfaceY - f.Size * 0.5f)
            {
                // Land on ocean — create ripple
                ripples.Add(new Ripple
                {
                    X = f.X,
                    Y = surfaceY,
                    Radius = f.Size * 0.8f,
                    MaxRadius = f.Size * 6f + (float)rng.NextDouble() * 12f,
                    Life = 0,
                    MaxLife = 0.8f + (float)rng.NextDouble() * 1.5f
                });
                ResetFlake(f, false);
            }
        }

        // ── Update ripples ──
        float dt = 0.033f;
        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            var r = ripples[i];
            r.Life += dt;
            float frac = r.Life / r.MaxLife;
            r.Radius = r.MaxRadius * (1 - (float)Math.Pow(1 - frac, 2));
            if (r.Life >= r.MaxLife)
                ripples.RemoveAt(i);
        }

        // Limit ripples
        while (ripples.Count > 200)
            ripples.RemoveAt(0);

        // ── Spawn & update meteors ──
        if (rng.NextDouble() < 0.03f && meteors.Count < 3)
        {
            float angle = (float)(Math.PI * 0.35f + rng.NextDouble() * Math.PI * 0.3f); // 63°–117° (downward)
            float speed = 4f + (float)rng.NextDouble() * 8f;
            meteors.Add(new Meteor
            {
                X = (float)rng.NextDouble() * Width,
                Y = (float)rng.NextDouble() * Height * 0.66f,
                Vx = (float)Math.Cos(angle) * speed,
                Vy = (float)Math.Sin(angle) * speed,
                Life = 0,
                MaxLife = 0.6f + (float)rng.NextDouble() * 0.9f,
                Length = 80f + (float)rng.NextDouble() * 160f,
                Brightness = 0.5f + (float)rng.NextDouble() * 0.5f
            });
        }
        for (int i = meteors.Count - 1; i >= 0; i--)
        {
            var m = meteors[i];
            m.Life += 0.033f;
            m.X += m.Vx;
            m.Y += m.Vy;
            if (m.Life > m.MaxLife || m.Y > Height * oceanTop + 40)
                meteors.RemoveAt(i);
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (closing || Width <= 0 || Height <= 0) return;
        try
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float horizonY = Height * oceanTop;

            // ── Sky gradient ──
            using (LinearGradientBrush skyBrush = new LinearGradientBrush(
                new PointF(0, 0), new PointF(0, horizonY),
                Color.FromArgb(8, 12, 28),
                Color.FromArgb(25, 55, 80)))
            {
                ColorBlend skyBlend = new ColorBlend(4);
                skyBlend.Colors = new Color[] {
                    Color.FromArgb(8, 12, 28),
                    Color.FromArgb(14, 22, 42),
                    Color.FromArgb(20, 40, 65),
                    Color.FromArgb(30, 60, 90)
                };
                skyBlend.Positions = new float[] { 0f, 0.4f, 0.75f, 1f };
                skyBrush.InterpolationColors = skyBlend;
                g.FillRectangle(skyBrush, 0, 0, Width, horizonY);
            }

            // ── Stars ──
            float t = (float)(DateTime.Now - startTime).TotalMilliseconds * 0.001f;
            foreach (var s in stars)
            {
                float twinkle = (float)(Math.Sin(t * s.Speed + s.Phase) * 0.5 + 0.5);
                twinkle = (float)Math.Pow(twinkle, 3);
                int alpha = (int)(twinkle * s.Brightness * 255);
                if (alpha < 15) continue;
                float sz = s.Size * (0.6f + twinkle * 0.4f);
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(alpha, 220, 235, 255)))
                    g.FillEllipse(sb, s.X - sz / 2, s.Y - sz / 2, sz, sz);
                if (twinkle > 0.7f)
                {
                    int glowA = (int)((twinkle - 0.7f) / 0.3f * 60);
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(glowA, 200, 220, 255)))
                        g.FillEllipse(gb, s.X - sz, s.Y - sz, sz * 2, sz * 2);
                }
            }

            // ── Meteors ──
            foreach (var m in meteors)
            {
                float frac = m.Life / m.MaxLife;
                float fadeAlpha = 1f;
                if (frac > 0.7f) fadeAlpha = 1 - (frac - 0.7f) / 0.3f;
                if (fadeAlpha <= 0) continue;

                // Trail: fading gradient line
                float trailLen = m.Length * fadeAlpha;
                float tx = m.X - m.Vx * 0.04f * trailLen;
                float ty = m.Y - m.Vy * 0.04f * trailLen;
                int headAlpha = (int)(fadeAlpha * m.Brightness * 255);
                int tailAlpha = (int)(fadeAlpha * m.Brightness * 40);

                using (LinearGradientBrush trailBrush = new LinearGradientBrush(
                    new PointF(tx, ty), new PointF(m.X, m.Y),
                    Color.FromArgb(tailAlpha, 180, 210, 255),
                    Color.FromArgb(headAlpha, 255, 255, 255)))
                using (Pen trailPen = new Pen(trailBrush, 1.2f))
                {
                    trailPen.StartCap = LineCap.Round;
                    trailPen.EndCap = LineCap.Round;
                    g.DrawLine(trailPen, tx, ty, m.X, m.Y);
                }

                // Bright head
                int headGlow = (int)(fadeAlpha * m.Brightness * 200);
                float headR = 2.5f;
                using (GraphicsPath headPath = new GraphicsPath())
                {
                    headPath.AddEllipse(m.X - headR, m.Y - headR, headR * 2, headR * 2);
                    using (PathGradientBrush headBrush = new PathGradientBrush(headPath))
                    {
                        headBrush.CenterPoint = new PointF(m.X, m.Y);
                        headBrush.CenterColor = Color.FromArgb(headGlow, 255, 255, 255);
                        headBrush.SurroundColors = new Color[] { Color.FromArgb(0, 200, 230, 255) };
                        g.FillEllipse(headBrush, m.X - headR * 1.8f, m.Y - headR * 1.8f, headR * 3.6f, headR * 3.6f);
                    }
                }
            }

            // ── Ocean body ──
            using (LinearGradientBrush oceanBrush = new LinearGradientBrush(
                new PointF(0, horizonY - 20), new PointF(0, Height),
                Color.FromArgb(20, 80, 120),
                Color.FromArgb(3, 12, 30)))
            {
                ColorBlend oceanBlend = new ColorBlend(4);
                oceanBlend.Colors = new Color[] {
                    Color.FromArgb(30, 100, 150),
                    Color.FromArgb(18, 60, 100),
                    Color.FromArgb(8, 25, 55),
                    Color.FromArgb(3, 10, 28)
                };
                oceanBlend.Positions = new float[] { 0f, 0.25f, 0.6f, 1f };
                oceanBrush.InterpolationColors = oceanBlend;
                g.FillRectangle(oceanBrush, 0, horizonY, Width, Height - horizonY);
            }

            // ── Ocean surface wave highlight ──
            using (GraphicsPath wavePath = new GraphicsPath())
            {
                wavePath.StartFigure();
                wavePath.AddLine(0, Height, 0, OceanSurfaceY(0, waveTime));
                for (int x = 1; x <= Width; x += 3)
                    wavePath.AddLine(x - 1, OceanSurfaceY(x - 1, waveTime),
                                     x, OceanSurfaceY(x, waveTime));
                wavePath.AddLine(Width, OceanSurfaceY(Width, waveTime), Width, Height);
                wavePath.CloseFigure();

                using (PathGradientBrush surfBrush = new PathGradientBrush(wavePath))
                {
                    surfBrush.CenterPoint = new PointF(Width / 2, horizonY + 8);
                    surfBrush.CenterColor = Color.FromArgb(60, 140, 200);
                    Color[] surround = new Color[] { Color.FromArgb(0, 30, 80) };
                    surfBrush.SurroundColors = surround;
                    g.FillPath(surfBrush, wavePath);
                }
            }

            // ── Wave surface line ──
            using (Pen wavePen = new Pen(Color.FromArgb(80, 160, 220), 1.2f))
            {
                wavePen.StartCap = LineCap.Round;
                wavePen.EndCap = LineCap.Round;
                for (int x = 0; x < Width; x += 3)
                {
                    float y0 = OceanSurfaceY(x, waveTime);
                    float y1 = OceanSurfaceY(x + 3, waveTime);
                    g.DrawLine(wavePen, x, y0, x + 3, y1);
                }
            }

            // Brighter foam crest line
            using (Pen foamPen = new Pen(Color.FromArgb(40, 200, 240), 0.6f))
            {
                for (int x = 0; x < Width; x += 6)
                {
                    float y = OceanSurfaceY(x, waveTime);
                    float y2 = OceanSurfaceY(x + 3, waveTime);
                    if (y2 < y)
                        g.DrawLine(foamPen, x, y - 1.5f, x + 3, y2 - 1.5f);
                }
            }

            // ── Wave shimmer (波光粼粼) ──
            int shimmerCount = Width / 8;
            for (int i = 0; i < shimmerCount; i++)
            {
                float sx = (float)i / shimmerCount * Width;
                float sy = OceanSurfaceY(sx, waveTime);
                float sy2 = OceanSurfaceY(sx + 3, waveTime);
                float crest = Math.Max(0, (sy - sy2) / 6f);
                // Flicker: combine a slow shimmer and a fast sharp blink
                float slowShimmer = (float)(Math.Sin(waveTime * 1.8f + sx * 0.04f) * 0.5 + 0.5);
                float fastBlink = (float)(Math.Sin(waveTime * 5.5f + sx * 0.13f) * 0.5 + 0.5);
                fastBlink = (float)Math.Pow(fastBlink, 6); // sharp on-off
                float flicker = slowShimmer * 0.4f + fastBlink * 0.6f;
                float spark = crest * flicker;
                if (spark < 0.12f) continue;
                int sa = (int)(spark * 200);
                if (sa > 180) sa = 180;
                float szW = 2f + spark * 4f;
                float szH = 0.3f + spark * 0.8f;
                using (SolidBrush spb = new SolidBrush(Color.FromArgb(sa, 255, 245, 210)))
                    g.FillEllipse(spb, sx - szW / 2, sy - 1 - szH / 2, szW, szH);
            }

            // ── Diamond sparkles on ocean surface ──
            foreach (var d in diamondSparks)
            {
                float twinkle = (float)(Math.Sin(t * d.Speed + d.Phase) * 0.5 + 0.5);
                twinkle = (float)Math.Pow(twinkle, 5);
                float bright = twinkle * d.MaxBrightness;
                if (bright < 0.08f) continue;

                int alpha = (int)(bright * 255);
                if (alpha > 220) alpha = 220;
                float sz = d.Size * (0.7f + twinkle * 0.3f);

                // Four-pointed star: outer tips (long & sharp) + inner notches
                float outerR = sz * 0.85f;
                float innerR = outerR * 0.15f; // very thin waist → sharp arms
                PointF[] starPts = new PointF[]
                {
                    new PointF(d.X, d.Y - outerR),              // top tip
                    new PointF(d.X + innerR, d.Y - innerR),     // top-right notch
                    new PointF(d.X + outerR, d.Y),              // right tip
                    new PointF(d.X + innerR, d.Y + innerR),     // bottom-right notch
                    new PointF(d.X, d.Y + outerR),              // bottom tip
                    new PointF(d.X - innerR, d.Y + innerR),     // bottom-left notch
                    new PointF(d.X - outerR, d.Y),              // left tip
                    new PointF(d.X - innerR, d.Y - innerR)      // top-left notch
                };

                // Gradient from bright center fading outward
                using (GraphicsPath starPath = new GraphicsPath())
                {
                    starPath.AddPolygon(starPts);
                    using (PathGradientBrush starBrush = new PathGradientBrush(starPath))
                    {
                        starBrush.CenterPoint = new PointF(d.X, d.Y);
                        starBrush.CenterColor = Color.FromArgb(Math.Min(255, alpha + 30), 255, 255, 250);
                        starBrush.SurroundColors = new Color[] { Color.FromArgb(0, 255, 245, 210) };
                        g.FillPolygon(starBrush, starPts);
                    }
                }
            }

            // ── Moon reflection on ocean ──
            float reflX = Width * 0.35f;
            float reflY = horizonY + 15;
            using (GraphicsPath reflPath = new GraphicsPath())
            {
                float reflWidth = Width * 0.08f;
                float reflHeight = Height * 0.15f;
                reflPath.AddEllipse(reflX - reflWidth / 2, reflY, reflWidth, reflHeight);
                using (PathGradientBrush reflBrush = new PathGradientBrush(reflPath))
                {
                    reflBrush.CenterPoint = new PointF(reflX, reflY + reflHeight * 0.2f);
                    reflBrush.CenterColor = Color.FromArgb(25, 200, 230, 255);
                    reflBrush.SurroundColors = new Color[] { Color.FromArgb(0, 40, 80, 120) };
                    g.FillEllipse(reflBrush, reflX - reflWidth / 2, reflY, reflWidth, reflHeight);
                }
            }

            // ── Ripples ──
            foreach (var r in ripples)
            {
                float lifeFrac = r.Life / r.MaxLife;
                float alpha = 1 - lifeFrac;
                int ra = (int)(alpha * 160);
                if (ra < 5) continue;
                float width = Math.Max(0.3f, 1.5f * (1 - lifeFrac));
                using (Pen rp = new Pen(Color.FromArgb(ra, 200, 235, 255), width))
                {
                    rp.StartCap = LineCap.Round;
                    rp.EndCap = LineCap.Round;
                    float ry = OceanSurfaceY(r.X, waveTime);
                    g.DrawEllipse(rp, r.X - r.Radius, ry - r.Radius * 0.3f, r.Radius * 2, r.Radius * 0.6f);
                }
            }

            // ── Snowflakes ──
            foreach (var f in flakes)
            {
                int alpha = (int)(f.Opacity * 255);
                if (alpha > 255) alpha = 255;
                if (alpha < 18) continue;
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(alpha, 245, 250, 255)))
                    g.FillEllipse(sb, f.X - f.Size / 2, f.Y - f.Size / 2, f.Size, f.Size);
                if (f.Size > 1.2f && f.Opacity > 0.6f)
                {
                    int glowA = (int)(f.Opacity * 0.18f * 255);
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(glowA, 220, 235, 255)))
                        g.FillEllipse(gb, f.X - f.Size * 0.8f, f.Y - f.Size * 0.8f, f.Size * 1.6f, f.Size * 1.6f);
                }
            }
        }
        catch { }
        base.OnPaint(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        stars.Clear(); flakes.Clear(); diamondSparks.Clear();
        InitStars(); InitFlakes(); InitDiamondSparks();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if ((DateTime.Now - startTime).TotalSeconds > 2) CloseAll();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        CloseAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Alt && e.KeyCode == Keys.F4) return;
        CloseAll();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (timer != null) { timer.Stop(); timer.Dispose(); }
        if (watchdog != null) { watchdog.Dispose(); watchdog = null; }
        base.OnFormClosed(e);
    }

    const int WM_CLOSE = 0x0010;
    const int WM_SYSCOMMAND = 0x0112;
    const int SC_CLOSE = 0xF060;
    const int WM_ACTIVATEAPP = 0x001C;
    const int WM_DISPLAYCHANGE = 0x007E;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLOSE && !closing) return;
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_CLOSE && !closing) return;
        if (m.Msg == WM_DISPLAYCHANGE) { BeginInvoke(new Action(CloseAll)); return; }
        if (m.Msg == WM_ACTIVATEAPP && m.WParam.ToInt32() == 0)
        {
            if (!IsPreview && !closing && SnowflakesOcean.forms.Count <= 1)
                { /* ignore deactivation in single-monitor mode */ }
        }
        base.WndProc(ref m);
    }

    void CloseAll()
    {
        if (closing) return;
        closing = true;
        if (timer != null) timer.Stop();
        foreach (var f in SnowflakesOcean.forms)
        {
            if (f == this || f.IsDisposed) continue;
            try { f.closing = true; f.BeginInvoke(new Action(() => { try { f.Close(); } catch { } })); } catch { }
        }
        try { Close(); } catch { }
        Application.Exit();
    }
}
