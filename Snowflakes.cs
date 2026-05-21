using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Snowflakes
{
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool SystemParametersInfo(int uAction, int uParam, System.Text.StringBuilder lpvParam, int fuWinIni);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

    const int GWL_STYLE = -16;
    const int WS_CHILD = 0x40000000;
    const int SPI_GETDESKWALLPAPER = 0x0073;

    public static List<ScreensaverForm> forms = new List<ScreensaverForm>();
    public static Image wallpaper;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();

        // Load wallpaper once — works reliably regardless of how screensaver is launched
        var sb = new System.Text.StringBuilder(512);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0))
        {
            string path = sb.ToString();
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                try { wallpaper = Image.FromFile(path); } catch { }
        }
        // Registry fallback
        if (wallpaper == null)
        {
            try
            {
                var key = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);
                if (key != null && System.IO.File.Exists(key.ToString()))
                    wallpaper = Image.FromFile(key.ToString());
            }
            catch { }
        }

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "/s";

        if (mode == "/c")
        {
            MessageBox.Show("Snowflakes Screensaver\n\n" +
                "A native Windows 11 screensaver.\n" +
                "Effects: desktop freeze, hexagonal ice crystals,\n" +
                "falling snow, and bottom accumulation.",
                "Snowflakes", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var form = new ScreensaverForm();
            form.Bounds = screen.Bounds;
            form.IsPreview = false;
            forms.Add(form); // add before Show so sibling Deactivate sees correct count
            form.Show();
        }

        Application.Run();
    }

    static void RunPreview(IntPtr previewHwnd)
    {
        var form = new ScreensaverForm();
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

class ScreensaverForm : Form
{
    public bool IsPreview = false;

    class Flake
    {
        public float X, Y, Z, Size, Speed, Wind;
        public float WanderPhase, WanderSpeed, WanderAmp;
        public float Opacity, Rotation, RotSpeed;
        public float WindResponse;
        public bool Stuck;
        public bool OnCrystal;
        public float CrystalX, CrystalY;
        public float CrystalStuckTime;
    }

    class IceCrystal
    {
        public float X, Y, Radius, Rotation;
        public float BirthTime, GrowDuration, LifeDuration, FadeStartFrac, Opacity;
        public int CrystalType;
    }

    List<Flake> flakes = new List<Flake>();
    List<IceCrystal> crystals = new List<IceCrystal>();
    float[] accumHeights;
    int accumWidth = 400;
    float frostAlpha = 0;
    float frostTarget = 0.75f;
    float frostRate = 0.0009f;

    float windTime = 0;
    float gustStrength = 0;
    float gustTarget = 0;
    float gustTimer = 0;
    DateTime startTime;
    DateTime reactivateUntil;
    bool closing;
    int initialScreenCount;
    System.Threading.Timer watchdog;
    Random rng = new Random();

    Bitmap bgImage;
    Timer timer;

    public ScreensaverForm()
    {
        startTime = DateTime.Now;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(6, 8, 14);
        // Cursor.Hide() moved to OnLoad — only for screensaver mode, not preview

        InitCrystals();
        accumHeights = new float[accumWidth];

        timer = new Timer();
        timer.Interval = 33;
        timer.Tick += (s, e) => { OnFrame(); };
        timer.Start();

        // Watchdog: if display config changes, force-exit even if UI thread is blocked
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
            // Multi-monitor: don't fight activation to avoid infinite ping-pong with sibling forms
            if (Snowflakes.forms.Count > 1) return;
            reactivateUntil = DateTime.Now.AddMilliseconds(800);
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
            if (!closing) { e.Cancel = true; reactivateUntil = DateTime.Now.AddMilliseconds(800); }
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Only hide cursor in full screensaver mode, not preview
        if (!IsPreview) Cursor.Hide();
    }

    void OnFrame()
    {
        if (closing || Width <= 0 || Height <= 0) return;

        // Lazy-init flakes (after bounds are set)
        if (flakes.Count == 0) InitFlakes();
        // Lazy-init background (after bounds are set)
        if (bgImage == null && !IsPreview && Snowflakes.wallpaper != null)
            CreateBgFromWallpaper();

        UpdateAnimation();
        Invalidate();
    }

    void CreateBgFromWallpaper()
    {
        if (Snowflakes.wallpaper == null) return;

        bgImage = new Bitmap(Width, Height);
        using (Graphics g = Graphics.FromImage(bgImage))
        {
            float scale = Math.Max((float)Width / Snowflakes.wallpaper.Width,
                                   (float)Height / Snowflakes.wallpaper.Height);
            int sw = (int)(Snowflakes.wallpaper.Width * scale);
            int sh = (int)(Snowflakes.wallpaper.Height * scale);
            g.DrawImage(Snowflakes.wallpaper, (Width - sw) / 2, (Height - sh) / 2, sw, sh);

            // Frosted glass: blur via downscale → upscale
            int bw = Math.Max(1, Width / 6);
            int bh = Math.Max(1, Height / 6);
            using (Bitmap blurSmall = new Bitmap(bw, bh))
            {
                using (Graphics bg = Graphics.FromImage(blurSmall))
                {
                    bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    bg.DrawImage(bgImage, 0, 0, bw, bh);
                }
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImage(blurSmall, 0, 0, Width, Height);
            }
        }
    }

    void InitCrystals()
    {
        for (int i = 0; i < 35; i++)
        {
            float birthTime = (float)rng.NextDouble() * 58000; // staggered across 0–58s

            crystals.Add(new IceCrystal
            {
                X = rng.Next(0, 3000),
                Y = rng.Next(0, 3000),
                Radius = 18 + (float)rng.NextDouble() * 75,
                Rotation = (float)rng.NextDouble() * (float)Math.PI * 2,
                BirthTime = birthTime,
                GrowDuration = 10000 + (float)rng.NextDouble() * 15000,  // grow over 10–25s
                LifeDuration = 50000 + (float)rng.NextDouble() * 20000, // total life 50–70s
                FadeStartFrac = 0.55f + (float)rng.NextDouble() * 0.25f, // fade begins at 55–80% of life
                Opacity = 0.3f + (float)rng.NextDouble() * 0.55f,
                CrystalType = rng.Next(0, 6)
            });
        }
    }

    void InitFlakes()
    {
        int flakeCount = Math.Min(800, Math.Max(400, Width * Height / 1800));
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
            ? -10 - (float)rng.NextDouble() * Height * 1.2f
            : -8 - (float)rng.NextDouble() * 25;
        f.Z = (float)rng.NextDouble();
        f.Size = 1.0f + (float)rng.NextDouble() * 4.0f;
        f.Speed = 1.2f + f.Z * 3.2f + (float)rng.NextDouble() * 2.5f;
        f.Wind = 0;
        f.WindResponse = 0.3f + (float)rng.NextDouble() * 1.4f;
        f.WanderPhase = (float)rng.NextDouble() * (float)Math.PI * 2;
        f.WanderSpeed = 0.006f + (float)rng.NextDouble() * 0.02f;
        f.WanderAmp = 0.08f + (float)rng.NextDouble() * 0.35f;
        f.Opacity = 0.35f + (float)rng.NextDouble() * 0.65f;
        f.Rotation = (float)rng.NextDouble() * (float)Math.PI * 2;
        f.RotSpeed = ((float)rng.NextDouble() - 0.5f) * 0.02f;
        f.Stuck = false;
        f.OnCrystal = false;
        f.CrystalX = 0;
        f.CrystalY = 0;
        f.CrystalStuckTime = 0;
    }

    float GetWind(float yFrac, float t)
    {
        return (float)(Math.Sin(t * 0.45 + yFrac * 2.2) * 0.45
                     + Math.Sin(t * 0.85 + yFrac * 4.5) * 0.22
                     + Math.Cos(t * 0.28) * 0.18);
    }

    void UpdateAnimation()
    {
        float elapsed = (float)(DateTime.Now - startTime).TotalMilliseconds;
        if (elapsed > 500)
            frostAlpha = Math.Min(frostTarget, frostAlpha + frostRate);

        gustTimer -= 0.015f;
        if (gustTimer <= 0)
        {
            gustTarget = ((float)rng.NextDouble() - 0.5f) * 6.0f;
            gustTimer = 1.0f + (float)rng.NextDouble() * 4.0f;
        }
        gustStrength += (gustTarget - gustStrength) * 0.018f;

        float baseY = Height;

        foreach (var f in flakes)
        {
            if (f.Stuck || f.OnCrystal) continue;

            float yFrac = Math.Max(0, Math.Min(1, f.Y / Math.Max(1, Height)));
            float gw = GetWind(yFrac, windTime);
            float totalWind = (gw + gustStrength) * f.WindResponse;
            f.Wind += (totalWind - f.Wind) * 0.03f;
            f.WanderPhase += f.WanderSpeed;
            f.X += f.Wind + (float)Math.Sin(f.WanderPhase) * f.WanderAmp;
            f.Y += f.Speed + Math.Abs(f.Wind) * 0.7f;
            f.Rotation += f.RotSpeed;

            if (f.X > Width + 15) f.X = -15;
            if (f.X < -15) f.X = Width + 15;

            int col = (int)(f.X / Width * accumWidth);
            col = Math.Max(0, Math.Min(accumWidth - 1, col));
            float snowTop = baseY - accumHeights[col];

            if (f.Y >= snowTop - f.Size)
            {
                LandFlake(f.X);
                f.Stuck = true;
                ResetFlake(f, false);
            }
        }

        // ── Crystal lifecycle & collision ──
        float frameElapsed = (float)(DateTime.Now - startTime).TotalMilliseconds;
        for (int ci = 0; ci < crystals.Count; ci++)
        {
            var ic = crystals[ci];

            // Respawn crystal when its life ends
            float age = frameElapsed - ic.BirthTime;
            if (age > ic.LifeDuration)
            {
                ic.X = rng.Next(0, 3000);
                ic.Y = rng.Next(0, 3000);
                ic.Radius = 18 + (float)rng.NextDouble() * 75;
                ic.Rotation = (float)rng.NextDouble() * (float)Math.PI * 2;
                ic.BirthTime = frameElapsed;
                ic.GrowDuration = 10000 + (float)rng.NextDouble() * 15000;
                ic.LifeDuration = 50000 + (float)rng.NextDouble() * 20000;
                ic.FadeStartFrac = 0.55f + (float)rng.NextDouble() * 0.25f;
                ic.Opacity = 0.3f + (float)rng.NextDouble() * 0.55f;
                ic.CrystalType = rng.Next(0, 6);
                age = 0;
            }
            if (age < 0) continue; // not born yet

            // Grow phase
            float growFrac = Math.Min(1, age / ic.GrowDuration);
            float grow = 1 - (float)Math.Pow(1 - growFrac, 3); // ease-out

            // Fade phase
            float fade = 1f;
            float lifeFrac = age / ic.LifeDuration;
            if (lifeFrac > ic.FadeStartFrac)
            {
                float fadeFrac = (lifeFrac - ic.FadeStartFrac) / (1 - ic.FadeStartFrac);
                fade = 1 - fadeFrac;
                if (fade < 0) fade = 0;
            }

            float r = ic.Radius * grow;
            float a = ic.Opacity * frostAlpha * grow * fade;
            if (r < 2 || a < 0.005f) continue;

            float cx = ic.X % Width;
            float cy = ic.Y % Height;

            // Snowflakes only land on crystals in the lower half of screen
            if (cy < Height * 0.5f) continue;

            foreach (var f in flakes)
            {
                if (f.Stuck || f.OnCrystal) continue;
                float dx = f.X - cx;
                float dy = f.Y - cy;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < r * 0.05f || dist > r * 0.75f) continue;

                float angle = (float)Math.Atan2(dy, dx) - ic.Rotation;
                float segAngle = (float)(Math.PI * 2 / 6);
                angle = ((angle % segAngle) + segAngle) % segAngle;
                float armHalf = 12f * (float)Math.PI / 180f;
                if (angle > armHalf && angle < segAngle - armHalf)
                {
                    if (rng.NextDouble() > 0.2f) continue;
                    f.OnCrystal = true;
                    f.CrystalX = f.X;
                    f.CrystalY = f.Y;
                    f.CrystalStuckTime = 0;
                }
            }
        }

        // Age stuck-on-crystal flakes and reset after lifetime expires
        float frameDt = 0.033f;
        foreach (var f in flakes)
        {
            if (!f.OnCrystal) continue;
            f.CrystalStuckTime += frameDt;
            float maxLife = 10f + f.Z * 8f;
            if (f.CrystalStuckTime > maxLife)
            {
                f.OnCrystal = false;
                f.CrystalStuckTime = 0;
                ResetFlake(f, false);
            }
        }

        windTime += 0.005f;
    }

    void LandFlake(float x)
    {
        int col = (int)(x / Width * accumWidth);
        col = Math.Max(0, Math.Min(accumWidth - 1, col));
        float spread = 2f + (float)rng.NextDouble() * 3f;
        float strength = 0.3f + (float)rng.NextDouble() * 1.2f;

        for (int i = Math.Max(0, col - 5); i <= Math.Min(accumWidth - 1, col + 5); i++)
        {
            float dist = Math.Abs(i - col);
            accumHeights[i] += (float)Math.Exp(-dist * dist / (2 * spread * spread)) * strength * 0.5f;
        }
        if (rng.NextDouble() < 0.15) accumHeights[col] += 0.6f;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (closing || Width <= 0 || Height <= 0) return;
        try
        {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (bgImage != null)
        {
            g.DrawImage(bgImage, 0, 0, Width, Height);
        }
        else
        {
            using (GraphicsPath bgPath = new GraphicsPath())
            {
                bgPath.AddRectangle(new Rectangle(0, 0, Width, Height));
                using (PathGradientBrush pgb = new PathGradientBrush(bgPath))
                {
                    pgb.CenterPoint = new PointF(Width * 0.3f, Height * 0.2f);
                    pgb.CenterColor = Color.FromArgb(26, 32, 48);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(4, 6, 8) };
                    g.FillRectangle(pgb, 0, 0, Width, Height);
                }
            }
        }

        if (frostAlpha > 0.002f)
        {
            // Semi-transparent frosted glass overlay
            int alpha = (int)(frostAlpha * 0.45f * 255);
            if (alpha > 255) alpha = 255;
            using (SolidBrush fb = new SolidBrush(Color.FromArgb(alpha, 180, 205, 230)))
                g.FillRectangle(fb, 0, 0, Width, Height);

            // ── Subtle noise texture ──
            if (frostAlpha > 0.06f && Width > 0 && Height > 0)
            {
                int noiseAlpha = (int)(frostAlpha * 0.15f * 255);
                if (noiseAlpha > 90) noiseAlpha = 90;
                int noiseCount = Width * Height / 1200;
                using (SolidBrush nb = new SolidBrush(Color.FromArgb(noiseAlpha, 255, 255, 255)))
                    for (int n = 0; n < noiseCount; n++)
                        g.FillRectangle(nb, rng.Next(Width), rng.Next(Height), 1.5f, 1.5f);
            }

            float elapsed = (float)(DateTime.Now - startTime).TotalMilliseconds;

            // Draw each crystal with lifecycle (grow + fade)
            foreach (var ic in crystals)
            {
                float age = elapsed - ic.BirthTime;
                if (age < 0) continue; // not born yet

                float growFrac = Math.Min(1, age / ic.GrowDuration);
                float grow = 1 - (float)Math.Pow(1 - growFrac, 3);

                float fade = 1f;
                float lifeFrac = age / ic.LifeDuration;
                if (lifeFrac > ic.FadeStartFrac)
                {
                    float fadeFrac = (lifeFrac - ic.FadeStartFrac) / (1 - ic.FadeStartFrac);
                    fade = 1 - fadeFrac;
                    if (fade < 0) fade = 0;
                }

                float r = ic.Radius * grow;
                float a = ic.Opacity * frostAlpha * grow * fade;
                if (r < 2 || a < 0.004f) continue;

                DrawHexCrystal(g, ic.X % Width, ic.Y % Height, r, a, ic.Rotation, ic.CrystalType);
            }

            // ── Draw snowflakes stuck on ice crystals ──
            foreach (var f in flakes)
            {
                if (!f.OnCrystal) continue;
                float fade = 1 - (f.CrystalStuckTime / (10f + f.Z * 8f));
                if (fade < 0.05f) continue;
                int stickAlpha = (int)(fade * f.Opacity * 230);
                if (stickAlpha > 255) stickAlpha = 255;
                if (stickAlpha < 15) continue;

                float s = f.Size * 0.7f;
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(stickAlpha, 255, 255, 255)))
                    g.FillEllipse(sb, f.CrystalX - s / 2, f.CrystalY - s / 2, s, s);

                // Tiny sparkle for freshly landed flakes
                if (f.CrystalStuckTime < 0.5f)
                {
                    int sparkAlpha = (int)((1 - f.CrystalStuckTime / 0.5f) * 180);
                    float sparkR = s * 2.5f;
                    using (SolidBrush spb = new SolidBrush(Color.FromArgb(sparkAlpha, 220, 240, 255)))
                        g.FillEllipse(spb, f.CrystalX - sparkR / 2, f.CrystalY - sparkR / 2, sparkR, sparkR);
                }
            }
        }

        foreach (var f in flakes)
        {
            if (f.Stuck || f.OnCrystal) continue;
            int alpha = (int)(f.Opacity * 255);
            if (alpha > 255) alpha = 255;
            if (alpha < 20) continue;
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.FillEllipse(sb, f.X - f.Size / 2, f.Y - f.Size / 2, f.Size, f.Size);
            if (f.Size > 1.3f && f.Opacity > 0.7f)
            {
                int glowAlpha = (int)(f.Opacity * 0.22f * 255);
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(glowAlpha, 255, 255, 255)))
                    g.FillEllipse(gb, f.X - f.Size, f.Y - f.Size, f.Size * 2, f.Size * 2);
            }
        }

        DrawAccumulation(g);
        }
        catch { }
        base.OnPaint(e);
    }

    void DrawAccumulation(Graphics g)
    {
        float baseY = Height;
        float minY = baseY;
        for (int i = 0; i < accumWidth; i++)
        {
            float y = baseY - accumHeights[i];
            if (y < minY) minY = y;
        }
        if (baseY - minY < 1) return;

        using (GraphicsPath path = new GraphicsPath())
        {
            path.StartFigure();
            path.AddLine(0, baseY, 0, baseY - accumHeights[0]);
            for (int i = 1; i < accumWidth; i++)
            {
                float x = (float)i / accumWidth * Width;
                path.AddLine(x, baseY - accumHeights[i - 1], x, baseY - accumHeights[i]);
            }
            path.AddLine(Width, baseY - accumHeights[accumWidth - 1], Width, baseY);
            path.CloseFigure();

            using (LinearGradientBrush grad = new LinearGradientBrush(
                new PointF(0, minY), new PointF(0, baseY),
                Color.FromArgb(235, 248, 250, 255),
                Color.FromArgb(140, 190, 208, 240)))
                g.FillPath(grad, path);
        }
    }

    void DrawFrostCreep(Graphics g, float frost)
    {
        // Frost creeping inward from screen edges — simulates ice forming on glass
        float depth = frost * 280f; // Max creep depth in pixels
        if (depth < 3) return;

        int edgeAlpha = (int)(frost * 120);
        if (edgeAlpha > 80) edgeAlpha = 80;
        int edgeAlpha2 = (int)(frost * 70);
        if (edgeAlpha2 > 50) edgeAlpha2 = 50;

        // Draw creeping frost on each edge
        using (GraphicsPath creepPath = new GraphicsPath())
        {
            float jitter = depth * 0.6f;
            int segments = 12;

            // Top edge
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments * Width;
                float y = (float)(Math.Abs(Math.Sin(x * 0.02 + frost * 5)) * jitter +
                       Math.Abs(Math.Sin(x * 0.037 + frost * 7.3)) * jitter * 0.7f +
                       rng.NextDouble() * jitter * 0.3f);
                y = Math.Min(y, depth);
                if (i == 0) creepPath.StartFigure();
                creepPath.AddLine(x, 0, x, y);
            }

            // Bottom edge
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments * Width;
                float y = (float)(Math.Abs(Math.Sin(x * 0.025 + frost * 4.7)) * jitter +
                       Math.Abs(Math.Cos(x * 0.033 + frost * 6.1)) * jitter * 0.7f +
                       rng.NextDouble() * jitter * 0.3f);
                y = Math.Min(y, depth);
                creepPath.StartFigure();
                creepPath.AddLine(x, Height, x, Height - y);
            }

            // Left edge
            for (int i = 0; i <= segments; i++)
            {
                float y = (float)i / segments * Height;
                float x = (float)(Math.Abs(Math.Sin(y * 0.022 + frost * 5.5)) * jitter +
                       Math.Abs(Math.Cos(y * 0.035 + frost * 6.8)) * jitter * 0.7f +
                       rng.NextDouble() * jitter * 0.3f);
                x = Math.Min(x, depth);
                creepPath.StartFigure();
                creepPath.AddLine(0, y, x, y);
            }

            // Right edge
            for (int i = 0; i <= segments; i++)
            {
                float y = (float)i / segments * Height;
                float x = (float)(Math.Abs(Math.Sin(y * 0.024 + frost * 4.9)) * jitter +
                       Math.Abs(Math.Cos(y * 0.031 + frost * 7.0)) * jitter * 0.7f +
                       rng.NextDouble() * jitter * 0.3f);
                x = Math.Min(x, depth);
                creepPath.StartFigure();
                creepPath.AddLine(Width, y, Width - x, y);
            }

            using (Pen cp = new Pen(Color.FromArgb(edgeAlpha, 210, 235, 250), 1.2f))
            {
                cp.LineJoin = LineJoin.Round;
                g.DrawPath(cp, creepPath);
            }
        }

        // Second pass: broader, more transparent frost bloom behind the creep lines
        using (GraphicsPath bloomPath = new GraphicsPath())
        {
            float bDepth = depth * 0.55f;
            // Just corners for bloom
            float[][] corners = new float[][] {
                new float[] { 0, 0, 0.7f, 0.7f },       // top-left
                new float[] { Width, 0, -0.7f, 0.7f },  // top-right
                new float[] { 0, Height, 0.7f, -0.7f }, // bottom-left
                new float[] { Width, Height, -0.7f, -0.7f } // bottom-right
            };

            foreach (var c in corners)
            {
                float cx = c[0], cy = c[1];
                float dx1 = c[2] * bDepth, dy1 = c[3] * bDepth;
                float dx2 = c[2] * bDepth * 0.4f, dy2 = c[3] * bDepth * 0.4f;

                bloomPath.StartFigure();
                bloomPath.AddPolygon(new PointF[] {
                    new PointF(cx, cy),
                    new PointF(cx + dx1, cy),
                    new PointF(cx, cy + dy1)
                });
                bloomPath.CloseFigure();
            }

            using (SolidBrush bb = new SolidBrush(Color.FromArgb(edgeAlpha2, 175, 210, 240)))
                foreach (var c in corners)
                {
                    float cx = c[0], cy = c[1];
                    float dx1 = c[2] * bDepth, dy1 = c[3] * bDepth;
                    g.FillPolygon(bb, new PointF[] {
                        new PointF(cx, cy),
                        new PointF(cx + dx1, cy),
                        new PointF(cx + dx1 * 0.3f, cy + dy1 * 0.3f),
                        new PointF(cx, cy + dy1)
                    });
                }
        }
    }

    void DrawHexCrystal(Graphics g, float cx, float cy, float radius, float alpha, float rotation, int type)
    {
        int a = (int)(alpha * 255);
        if (a > 255) a = 255;
        if (a < 3) return;

        var oldTransform = g.Transform;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(rotation * 180f / (float)Math.PI);

        // ── Outer glow ──
        int glowA = (int)(a * 0.5f);
        if (glowA > 3)
        {
            float glowR = radius * 0.85f;
            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(-glowR, -glowR, glowR * 2, glowR * 2);
                using (PathGradientBrush glowBrush = new PathGradientBrush(glowPath))
                {
                    glowBrush.CenterColor = Color.FromArgb(glowA, 160, 200, 245);
                    glowBrush.CenterPoint = new PointF(0, 0);
                    glowBrush.SurroundColors = new Color[] { Color.FromArgb(0, 100, 160, 230) };
                    g.FillEllipse(glowBrush, -glowR, -glowR, glowR * 2, glowR * 2);
                }
            }
        }

        // ── Center hexagonal plate ──
        float cR = radius * 0.18f;
        PointF[] hex6 = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            double ang = Math.PI * 2.0 / 6.0 * i - Math.PI / 6.0;
            hex6[i] = new PointF((float)Math.Cos(ang) * cR, (float)Math.Sin(ang) * cR);
        }
        using (GraphicsPath hxPath = new GraphicsPath())
        {
            hxPath.AddPolygon(hex6);
            using (PathGradientBrush hb = new PathGradientBrush(hxPath))
            {
                hb.CenterPoint = new PointF(0, 0);
                hb.CenterColor = Color.FromArgb((int)(a * 0.95f), 215, 232, 253);
                hb.SurroundColors = new Color[] { Color.FromArgb((int)(a * 0.4f), 150, 190, 242) };
                g.FillPolygon(hb, hex6);
            }
            using (Pen hp = new Pen(Color.FromArgb((int)(a * 0.7f), 170, 210, 248), 0.5f))
                g.DrawPolygon(hp, hex6);
        }

        // ── Inner hex ring ──
        float ir = cR * 0.55f;
        PointF[] inner6 = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            double ang = Math.PI * 2.0 / 6.0 * i;
            inner6[i] = new PointF((float)Math.Cos(ang) * ir, (float)Math.Sin(ang) * ir);
        }
        using (Pen ip = new Pen(Color.FromArgb((int)(a * 0.5f), 190, 220, 250), 0.4f))
            g.DrawPolygon(ip, inner6);

        // ── Specular highlight ──
        float spotR = radius * 0.06f;
        using (GraphicsPath spotPath = new GraphicsPath())
        {
            spotPath.AddEllipse(-spotR, -spotR * 0.7f, spotR * 2, spotR * 1.4f);
            using (PathGradientBrush sb = new PathGradientBrush(spotPath))
            {
                sb.CenterColor = Color.FromArgb((int)(a * 0.9f), 255, 255, 255);
                sb.CenterPoint = new PointF(0, 0);
                sb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                g.FillEllipse(sb, -spotR, -spotR * 0.7f, spotR * 2, spotR * 1.4f);
            }
        }

        // ── Center twinkling star ──
        float twinkleTime = (float)(DateTime.Now - startTime).TotalMilliseconds / 1000f;
        float tPhase = (cx * 1.7f + cy) % 6.28318f;
        float tVal = (float)(Math.Sin(twinkleTime * 4.2f + tPhase) * 0.5 + 0.5);
        tVal = (float)Math.Pow(tVal, 4);
        if (tVal > 0.12f)
        {
            int starA = (int)(tVal * a * 0.95f);
            float starLen = radius * 0.11f * (0.5f + tVal * 0.5f);
            float starW = Math.Max(0.3f, radius * 0.018f);
            using (Pen sp = new Pen(Color.FromArgb(starA, 255, 255, 255), starW))
            {
                sp.StartCap = LineCap.Round; sp.EndCap = LineCap.Round;
                g.DrawLine(sp, -starLen, 0, starLen, 0);
                g.DrawLine(sp, 0, -starLen, 0, starLen);
                float d = starLen * 0.6f;
                g.DrawLine(sp, -d, -d, d, d);
                g.DrawLine(sp, -d, d, d, -d);
            }
            float dotR = starLen * 0.22f;
            using (SolidBrush db = new SolidBrush(Color.FromArgb((int)(tVal * 255), 255, 255, 255)))
                g.FillEllipse(db, -dotR, -dotR, dotR * 2, dotR * 2);
        }

        // ── Inter-arm small diamonds (between arms near center) ──
        for (int i = 0; i < 6; i++)
        {
            double midAng = Math.PI * 2.0 / 6.0 * (i + 0.5);
            float mx = (float)Math.Cos(midAng) * cR * 1.15f;
            float my = (float)Math.Sin(midAng) * cR * 1.15f;
            float ds = Math.Max(1.2f, radius * 0.045f);
            PointF[] diamond = new PointF[] {
                new PointF(mx, my - ds * 0.6f),
                new PointF(mx + ds, my),
                new PointF(mx, my + ds * 0.6f),
                new PointF(mx - ds, my)
            };
            using (SolidBrush db = new SolidBrush(Color.FromArgb((int)(a * 0.45f), 175, 215, 248)))
                g.FillPolygon(db, diamond);
        }

        // ── 6 Main Arms ──
        for (int i = 0; i < 6; i++)
        {
            double armAngle = Math.PI * 2.0 / 6.0 * i;
            var armSave = g.Transform;
            g.RotateTransform((float)(armAngle * 180.0 / Math.PI));

            float as0 = radius * 0.12f;  // arm base start
            float ae = radius * 0.82f;   // arm tip
            float aw0 = Math.Max(1.5f, radius * 0.10f);  // base half-width
            float aw1 = Math.Max(0.5f, radius * 0.022f); // tip half-width

            // Main arm body (filled tapered polygon)
            PointF[] armBody = new PointF[] {
                new PointF(as0, -aw0), new PointF(ae, -aw1),
                new PointF(ae, aw1), new PointF(as0, aw0)
            };
            using (GraphicsPath ap = new GraphicsPath())
            {
                ap.AddPolygon(armBody);
                using (PathGradientBrush agb = new PathGradientBrush(ap))
                {
                    agb.CenterPoint = new PointF(ae * 0.4f, 0);
                    agb.CenterColor = Color.FromArgb((int)(a * 1.0f), 210, 230, 252);
                    agb.SurroundColors = new Color[] { Color.FromArgb((int)(a * 0.5f), 145, 185, 240) };
                    g.FillPolygon(agb, armBody);
                }
            }
            // Arm outline
            using (Pen aop = new Pen(Color.FromArgb((int)(a * 0.65f), 160, 200, 245), 0.4f))
                g.DrawPolygon(aop, armBody);

            // Center ridge line
            float ridgeAlpha = a * 0.55f;
            using (Pen rp = new Pen(Color.FromArgb((int)ridgeAlpha, 230, 242, 255), Math.Max(0.3f, radius * 0.014f)))
            {
                rp.EndCap = LineCap.Round;
                g.DrawLine(rp, as0 * 0.6f, 0, ae * 0.92f, 0);
            }

            // ── Side branches (dendrite pairs along arm) ──
            int nBranches = 6;
            for (int j = 0; j < nBranches; j++)
            {
                float t = (j + 0.7f) / (nBranches + 0.4f);
                float bx = as0 + t * (ae - as0);
                float bLen = radius * (0.30f - t * 0.18f);
                float bW = Math.Max(0.15f, radius * (0.035f - t * 0.022f));
                float bAng = 0.50f + t * 0.10f; // 28-35°

                float bex = bx + bLen * (float)Math.Cos(bAng);
                float bey = bLen * (float)Math.Sin(bAng);

                using (Pen bp = new Pen(Color.FromArgb((int)(a * 0.65f), 170, 210, 248), bW))
                {
                    bp.EndCap = LineCap.Round; bp.StartCap = LineCap.Round;
                    g.DrawLine(bp, bx, 0, bex, -bey);
                    g.DrawLine(bp, bx, 0, bex, bey);
                }

                // Sub-branches on the first 4 branch pairs
                if (j < 4)
                {
                    float sbl = bLen * 0.40f;
                    float sbw = Math.Max(0.10f, bW * 0.50f);
                    float midX = bx + bLen * 0.55f * (float)Math.Cos(bAng);
                    float midY = bLen * 0.55f * (float)Math.Sin(bAng);
                    float sbAng = bAng + 0.38f;
                    using (Pen sbp = new Pen(Color.FromArgb((int)(a * 0.40f), 155, 198, 244), sbw))
                    {
                        sbp.EndCap = LineCap.Round;
                        g.DrawLine(sbp, midX, -midY, midX + sbl * (float)Math.Cos(sbAng), -(midY + sbl * (float)Math.Sin(sbAng)));
                        g.DrawLine(sbp, midX, -midY, midX - sbl * (float)Math.Cos(sbAng - 0.3f), -(midY + sbl * (float)Math.Sin(sbAng - 0.3f)));
                        g.DrawLine(sbp, midX, midY, midX + sbl * (float)Math.Cos(sbAng), midY + sbl * (float)Math.Sin(sbAng));
                        g.DrawLine(sbp, midX, midY, midX - sbl * (float)Math.Cos(sbAng - 0.3f), midY + sbl * (float)Math.Sin(sbAng - 0.3f));
                    }
                }

                // Small sparkle at branch tips
                float ss = 0.25f;
                using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(a * 0.45f), 220, 238, 254)))
                {
                    g.FillEllipse(sb, bex - ss, -bey - ss, ss * 2, ss * 2);
                    g.FillEllipse(sb, bex - ss, bey - ss, ss * 2, ss * 2);
                }
            }

            // ── Diamond end-plate at arm tip ──
            float dpX = ae * 0.88f, dpW = radius * 0.12f, dpH = radius * 0.06f;
            PointF[] dPlate = new PointF[] {
                new PointF(dpX, -dpW), new PointF(dpX + dpH, 0),
                new PointF(dpX, dpW), new PointF(dpX - dpH * 0.3f, 0)
            };
            using (GraphicsPath dpPath = new GraphicsPath())
            {
                dpPath.AddPolygon(dPlate);
                using (PathGradientBrush dgb = new PathGradientBrush(dpPath))
                {
                    dgb.CenterPoint = new PointF(dpX, 0);
                    dgb.CenterColor = Color.FromArgb((int)(a * 0.9f), 220, 238, 254);
                    dgb.SurroundColors = new Color[] { Color.FromArgb((int)(a * 0.5f), 150, 195, 242) };
                    g.FillPolygon(dgb, dPlate);
                }
            }
            using (Pen dpp = new Pen(Color.FromArgb((int)(a * 0.7f), 165, 208, 248), 0.35f))
                g.DrawPolygon(dpp, dPlate);

            // Tip sparkle
            using (SolidBrush tsp = new SolidBrush(Color.FromArgb((int)(a * 0.85f), 255, 255, 255)))
                g.FillEllipse(tsp, dpX - 0.5f, -0.5f, 1f, 1f);

            g.Transform = armSave;
        }

        g.Transform = oldTransform;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (bgImage != null) { bgImage.Dispose(); bgImage = null; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (DateTime.Now < reactivateUntil) return;
        if ((DateTime.Now - startTime).TotalSeconds > 2) CloseAll();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (DateTime.Now < reactivateUntil) return;
        CloseAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DateTime.Now < reactivateUntil) return;
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
            // Multi-monitor: don't block exit, as sibling forms cause deactivation ping-pong
            if (!IsPreview && !closing && Snowflakes.forms.Count <= 1)
                reactivateUntil = DateTime.Now.AddMilliseconds(800);
        }
        base.WndProc(ref m);
    }

    void CloseAll()
    {
        if (closing) return;
        closing = true;
        if (timer != null) timer.Stop();
        // Use BeginInvoke to avoid blocking if another form is hung (e.g. disconnected monitor)
        foreach (var f in Snowflakes.forms)
        {
            if (f == this || f.IsDisposed) continue;
            try { f.closing = true; f.BeginInvoke(new Action(() => { try { f.Close(); } catch { } })); } catch { }
        }
        // Ensure we always exit, even if other forms can't close
        try { Close(); } catch { }
        Application.Exit();
    }
}
