using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Sailback
{
    /// <summary>
    /// One dinosaur: a borderless, per-pixel-alpha layered window that lives on top of the
    /// taskbar. Transparent pixels are click-through, so it never blocks the desktop.
    /// </summary>
    internal sealed class Dino : Form
    {
        private enum State { Walk, Run, Idle, Sniff, Roar, Sleep, Flee, Dragged, Falling, Landing }

        private readonly SailbackApp _app;
        private readonly Palette _pal;
        private readonly Random _rng;

        private double _x, _y, _vx, _vy;
        private int _dir = 1;
        private State _state;
        private double _t, _dur;
        private double _phase, _time;
        private double _fleeCooldown, _maxFallSpeed;
        private float _scale = 1;
        private int _dpi = 96;
        private double _dpiCheck;
        private Bitmap _frame;
        private bool _hidden;
        private bool _wasHidden = true;

        // mouse
        private bool _pressed, _dragging;
        private Point _pressPos;
        private double _grabDx, _grabDy;
        private Point _lastCursor;
        private double _throwVx, _throwVy;
        private double _lastClickTime = -10;

        public Dino(SailbackApp app, Palette palette, int seed)
        {
            _app = app;
            _pal = palette;
            _rng = new Random(seed);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = WindowTitle;
            Size = new Size(1, 1);

            MouseDown += OnMouseDown;
            MouseUp += OnMouseUp;
        }

        public const string WindowTitle = "Sailback dino";

        public Palette Palette => _pal;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (_app == null || _app.Settings.AlwaysOnTop) cp.ExStyle |= Native.WS_EX_TOPMOST; // called from base ctor before _app is set
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            if (_app != null && m.Msg == _app.AddDinoMessage && m.Msg != 0)
            {
                _app.OnAnotherInstanceLaunched(this);
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------ spawning

        /// <summary>Place on the taskbar of the primary screen.</summary>
        public void SpawnOnGround()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            UpdateScale(wa.Left + wa.Width / 2, wa.Bottom - 10, force: true);
            _x = wa.Left + _rng.NextDouble() * Math.Max(1, wa.Width - CanvasW);
            _y = GroundY(_x + CanvasW / 2);
            _dir = _rng.Next(2) == 0 ? -1 : 1;
            Go(State.Walk);
        }

        /// <summary>Drop in from the top of the screen.</summary>
        public void SpawnFalling()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            UpdateScale(wa.Left + wa.Width / 2, wa.Bottom - 10, force: true);
            _x = wa.Left + _rng.NextDouble() * Math.Max(1, wa.Width - CanvasW);
            _y = wa.Top - CanvasH;
            _vx = (_rng.NextDouble() - 0.5) * 300 * _scale;
            _vy = 0;
            _dir = _vx < 0 ? -1 : 1;
            Go(State.Falling);
        }

        // ------------------------------------------------------------ geometry

        private int CanvasW => (int)Math.Ceiling(DinoArt.W * _scale);
        private int CanvasH => (int)Math.Ceiling(DinoArt.H * _scale);
        private double FeetY => _y + DinoArt.Ground * _scale;
        private double CenterX => _x + CanvasW / 2.0;

        private Rectangle ScreenAt(double cx, double feetY) =>
            Screen.FromPoint(new Point((int)cx, (int)feetY - 2)).WorkingArea;

        /// <summary>Window top that puts the feet on the top edge of the taskbar.</summary>
        private double GroundY(double cx) => ScreenAt(cx, FeetY).Bottom - DinoArt.Ground * _scale;

        private void UpdateScale(int px, int py, bool force = false)
        {
            int dpi = Native.DpiAt(px, py);
            float scale = dpi / 96f * _app.Settings.Size;
            if (!force && Math.Abs(scale - _scale) < 0.001f) return;

            // keep feet and centre anchored while resizing
            double feet = FeetY, cx = CenterX;
            _dpi = dpi;
            _scale = scale;
            _frame?.Dispose();
            _frame = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppPArgb);
            if (!force)
            {
                _x = cx - CanvasW / 2.0;
                _y = feet - DinoArt.Ground * _scale;
            }
        }

        public void RefreshSize() => UpdateScale((int)CenterX, (int)FeetY - 2, force: false);

        // ------------------------------------------------------------ state machine

        private void Go(State s)
        {
            _state = s;
            _t = 0;
            switch (s)
            {
                case State.Walk:
                    _dur = 3 + _rng.NextDouble() * 6;
                    if (_rng.NextDouble() < 0.35) _dir = -_dir;
                    break;
                case State.Run: _dur = 1.8 + _rng.NextDouble() * 2.2; break;
                case State.Idle: _dur = 1.5 + _rng.NextDouble() * 3; break;
                case State.Sniff: _dur = 1.8 + _rng.NextDouble() * 1.5; break;
                case State.Roar:
                    _dur = 1.45;
                    if (_app.Settings.Sound) _app.Roar.Play(_app.Settings.Size);
                    break;
                case State.Sleep: _dur = 10 + _rng.NextDouble() * 15; break;
                case State.Flee: _dur = 1.4 + _rng.NextDouble() * 1.2; break;
                case State.Landing: _dur = 0.35; break;
                case State.Falling: _maxFallSpeed = 0; break;
                case State.Dragged: _dur = double.MaxValue; break;
            }
        }

        private void PickNext()
        {
            double r = _rng.NextDouble();
            if (r < 0.42) Go(State.Walk);
            else if (r < 0.62) Go(State.Idle);
            else if (r < 0.74) Go(State.Sniff);
            else if (r < 0.85) Go(State.Run);
            else if (r < 0.91) Go(State.Roar);
            else Go(State.Sleep);
        }

        public void Command(string what)
        {
            switch (what)
            {
                case "roar": Go(State.Roar); break;
                case "run": Go(State.Run); break;
                case "sleep": if (_state != State.Falling && _state != State.Dragged) Go(State.Sleep); break;
            }
        }

        public void Tick(double dt, bool hideForFullscreen)
        {
            _time += dt;
            _t += dt;
            _fleeCooldown -= dt;

            _dpiCheck -= dt;
            if (_dpiCheck <= 0) { _dpiCheck = 0.5; UpdateScale((int)CenterX, (int)FeetY - 2); }

            var cursor = Cursor.Position;
            var vs = SystemInformation.VirtualScreen;
            double minX = vs.Left - 12 * _scale, maxX = vs.Right - CanvasW + 12 * _scale;

            switch (_state)
            {
                case State.Walk: Locomote(dt, 55, 13, minX, maxX); break;
                case State.Run: Locomote(dt, 190, 20, minX, maxX); break;
                case State.Flee: Locomote(dt, 240, 21, minX, maxX); break;
                case State.Idle:
                case State.Sniff:
                case State.Roar:
                case State.Sleep:
                case State.Landing:
                    StickToGround();
                    break;
                case State.Dragged: UpdateDrag(cursor, dt); break;
                case State.Falling: UpdateFall(dt, minX, maxX); break;
            }

            if (_state != State.Dragged && _state != State.Falling && _t >= _dur)
            {
                if (_state == State.Flee) _fleeCooldown = 4;
                if (_state == State.Landing && _maxFallSpeed > 1400 * _scale) Go(State.Roar);
                else if (_state == State.Sleep) Go(State.Idle);
                else PickNext();
            }

            CheckFlee(cursor);
            _lastCursor = cursor;

            _hidden = hideForFullscreen;
            Render();
            Present();
        }

        private void Locomote(double dt, double speedArt, float stride, double minX, double maxX)
        {
            double speed = speedArt * _scale;
            _x += _dir * speed * dt;
            if (_x < minX) { _x = minX; _dir = 1; }
            if (_x > maxX) { _x = maxX; _dir = -1; }
            _phase += dt * speedArt * Math.PI / (2 * stride);
            StickToGround();
        }

        private void StickToGround()
        {
            double g = GroundY(CenterX);
            if (_y < g - 3 * _scale) { _vx = _dir * 40 * _scale; _vy = 0; Go(State.Falling); }
            else _y = g;
        }

        private void UpdateFall(double dt, double minX, double maxX)
        {
            _vy += 2600 * _scale * dt;
            _x += _vx * dt;
            _y += _vy * dt;
            _maxFallSpeed = Math.Max(_maxFallSpeed, _vy);
            if (Math.Abs(_vx) > 60 * _scale) _dir = _vx < 0 ? -1 : 1;

            if (_x < minX) { _x = minX; _vx = -_vx * 0.5; }
            if (_x > maxX) { _x = maxX; _vx = -_vx * 0.5; }

            double g = GroundY(CenterX);
            if (_y >= g)
            {
                _y = g;
                if (_vy > 700 * _scale)
                {
                    _vy = -_vy * 0.28;
                    _vx *= 0.55;
                }
                else
                {
                    _vx = _vy = 0;
                    Go(State.Landing);
                }
            }
        }

        private void CheckFlee(Point cursor)
        {
            if (!_app.Settings.RunFromMouse || _fleeCooldown > 0 || _pressed) return;
            if (_state != State.Walk && _state != State.Idle && _state != State.Sniff) return;

            double dx = cursor.X - CenterX;
            double feet = FeetY;
            bool near = Math.Abs(dx) < 210 * _scale && cursor.Y > feet - 230 * _scale && cursor.Y < feet + 60 * _scale;
            bool wasFar = Math.Abs(_lastCursor.X - CenterX) >= 210 * _scale || _lastCursor.Y <= feet - 230 * _scale
                          || _lastCursor.Y >= feet + 60 * _scale;
            var bounds = new Rectangle((int)_x, (int)_y, CanvasW, CanvasH);
            if (near && wasFar && !bounds.Contains(cursor))
            {
                if (_rng.NextDouble() < 0.7)
                {
                    _dir = dx > 0 ? -1 : 1;
                    Go(State.Flee);
                }
                else _fleeCooldown = 3; // curious this time
            }
        }

        // ------------------------------------------------------------ mouse

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Native.SetForegroundWindow(Handle);
                _app.ShowMenu(this, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _pressed = true;
            _dragging = false;
            _pressPos = Cursor.Position;
            _grabDx = _pressPos.X - _x;
            _grabDy = _pressPos.Y - _y;
            _throwVx = _throwVy = 0;
            _lastCursor = _pressPos;
        }

        private void UpdateDrag(Point cursor, double dt)
        {
            double nx = cursor.X - _grabDx, ny = cursor.Y - _grabDy;
            if (dt > 0)
            {
                // smoothed velocity so a flick of the mouse throws the dino
                _throwVx = _throwVx * 0.6 + (nx - _x) / dt * 0.4;
                _throwVy = _throwVy * 0.6 + (ny - _y) / dt * 0.4;
            }
            _x = nx;
            _y = ny;
            if (Math.Abs(_throwVx) > 40 * _scale) _dir = _throwVx < 0 ? -1 : 1;
        }

        public void PollDragStart()
        {
            if (!_pressed) return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                // button released somewhere we didn't hear about (capture lost) - let go
                if (_dragging) Release(); else _pressed = false;
                return;
            }
            if (_dragging) return;
            var c = Cursor.Position;
            if (Math.Abs(c.X - _pressPos.X) + Math.Abs(c.Y - _pressPos.Y) > 5)
            {
                _dragging = true;
                Go(State.Dragged);
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Release();
        }

        private void Release()
        {
            if (!_pressed) return;
            _pressed = false;

            if (_dragging)
            {
                _dragging = false;
                double cap = 3000 * _scale;
                _vx = Math.Max(-cap, Math.Min(cap, _throwVx));
                _vy = Math.Max(-cap, Math.Min(cap, _throwVy));
                Go(State.Falling);
                return;
            }

            // a plain click: roar; a quick second click: bolt!
            if (_time - _lastClickTime < 0.45) Go(State.Run);
            else Go(State.Roar);
            _lastClickTime = _time;
        }

        // ------------------------------------------------------------ drawing

        private Pose BuildPose()
        {
            var p = new Pose
            {
                Phase = _phase,
                Time = _time,
                FacingLeft = _dir < 0,
                Shadow = 1,
                TailSway = (float)Math.Sin(_time * 1.7) * 2.5f,
                Bob = (float)Math.Sin(_time * 2.2) * 0.6f,
                EyesClosed = (_time % 4.3) < 0.13,
            };

            switch (_state)
            {
                case State.Walk:
                    p.Stride = 13; p.Lift = 7;
                    p.Bob = -(float)Math.Abs(Math.Sin(_phase)) * 2;
                    p.TailSway = (float)Math.Sin(_phase) * 2;
                    break;

                case State.Run:
                case State.Flee:
                    p.Stride = 20; p.Lift = 13;
                    p.Bob = -(float)Math.Abs(Math.Sin(_phase)) * 4;
                    p.TailSway = (float)Math.Sin(_phase) * 3;
                    p.HeadDrop = 0.12f;
                    if (_state == State.Flee) { p.Jaw = 0.35f + 0.1f * (float)Math.Sin(_time * 20); p.EyesClosed = false; }
                    break;

                case State.Sniff:
                {
                    float k = (float)Math.Min(1, Math.Min(_t / 0.45, (_dur - _t) / 0.45));
                    p.HeadDrop = Math.Max(0, k) * (0.95f + 0.05f * (float)Math.Sin(_time * 22));
                    p.Jaw = 0;
                    break;
                }

                case State.Roar:
                {
                    float k = (float)Math.Min(1, Math.Min(_t / 0.18, (_dur - _t) / 0.3));
                    k = Math.Max(0, k);
                    p.Jaw = k;
                    p.HeadDrop = -0.38f * k;
                    p.Shake = (float)Math.Sin(_time * 70) * 1.3f * k;
                    p.TailSway = (float)Math.Sin(_time * 9) * 5 * k;
                    p.EyesClosed = false;
                    break;
                }

                case State.Sleep:
                {
                    float k = (float)Math.Max(0, Math.Min(1, Math.Min(_t / 0.7, (_dur - _t) / 0.7)));
                    p.Crouch = k;
                    p.HeadDrop = 0.55f * k;
                    p.EyesClosed = k > 0.5f;
                    p.Snoring = k > 0.8f;
                    p.Bob = (float)Math.Sin(_time * 1.3) * 1.2f * k;
                    p.TailSway = 0;
                    break;
                }

                case State.Landing:
                {
                    float k = (float)Math.Sin(Math.PI * Math.Min(1, _t / _dur));
                    p.Crouch = 0.5f * k;
                    break;
                }

                case State.Dragged:
                case State.Falling:
                    p.Dangle = true;
                    p.Shadow = 0;
                    p.Jaw = 0.3f + 0.15f * (float)Math.Sin(_time * 11);
                    p.TailSway = (float)Math.Sin(_time * 8) * 7;
                    p.HeadDrop = -0.15f;
                    p.EyesClosed = false;
                    break;
            }

            // fade the shadow as the dino leaves the ground
            if (p.Shadow > 0)
            {
                double above = GroundY(CenterX) - _y;
                if (above > 1) p.Shadow = (float)Math.Max(0, 1 - above / (60 * _scale));
            }
            return p;
        }

        private void Render()
        {
            using (var g = Graphics.FromImage(_frame))
                DinoArt.Draw(g, _pal, BuildPose(), _scale);
        }

        private void Present()
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = _frame.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBitmap);
            try
            {
                var size = new Native.SIZE(_frame.Width, _frame.Height);
                var src = new Native.POINT(0, 0);
                var dst = new Native.POINT((int)Math.Round(_x), (int)Math.Round(_y));
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    SourceConstantAlpha = (byte)(_hidden ? 0 : 255),
                    AlphaFormat = Native.AC_SRC_ALPHA,
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }

            if (_wasHidden && !_hidden) ApplyTopMost();
            _wasHidden = _hidden;
        }

        public void ApplyTopMost()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, _app.Settings.AlwaysOnTop ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _frame?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Render a standalone picture of a standing dino (used for the icons).</summary>
        public static Bitmap Snapshot(Palette pal, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            float scale = size / DinoArt.W;
            using (var art = new Bitmap((int)Math.Ceiling(DinoArt.W * scale), (int)Math.Ceiling(DinoArt.H * scale), PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(art))
                    DinoArt.Draw(g, pal, new Pose { Stride = 13, Lift = 7, Phase = 0.6, HeadDrop = -0.1f, Jaw = 0.55f }, scale);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(art, 0, (size - art.Height) / 2 + size * 0.04f, art.Width, art.Height);
                }
            }
            return bmp;
        }
    }
}
