using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Crobusaurus
{
    /// <summary>
    /// One dinosaur: a borderless, per-pixel-alpha layered window. Transparent pixels are
    /// click-through, so it never blocks the desktop.
    /// </summary>
    internal sealed class Dino : Form
    {
        private enum State { Walk, Run, Idle, Sniff, Roar, Sleep, Flee, Dragged, Falling, Landing }

        public const string WindowTitle = "Crobusaurus dino";

        private readonly CrobusaurusApp _app;
        private readonly Random _rng;
        private Species _species;
        private Palette _pal;

        private double _x, _y, _vx, _vy;      // window top-left, velocity (physical pixels)
        private int _dir = 1;
        private State _state;
        private double _t, _dur;
        private double _phase, _time;
        private double _fleeCooldown, _maxFallSpeed;
        private float _scale = 1;
        private double _dpiCheck;
        private Bitmap _frame;
        private bool _hidden;
        private bool _wasHidden = true;

        // ground-walking modes
        private IntPtr _standOn;              // window whose top edge we're standing on (Zero = taskbar)
        private int _standLeft;
        private bool _turnAtEdges;
        private bool _leaping;                // in the air on purpose (a jump), not dropped

        // roam mode
        private double _tx, _ty;              // target for the feet
        private double _headX, _headY;        // unit heading

        // mouse
        private bool _pressed, _dragging;
        private Point _pressPos;
        private double _grabDx, _grabDy;
        private Point _lastCursor;
        private double _throwVx, _throwVy;
        private double _lastClickTime = -10;

        public Dino(CrobusaurusApp app, Species species, Palette palette, int seed)
        {
            _app = app;
            _species = species;
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

        public Species Species => _species;
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

        private WalkMode Mode => _app.Settings.Mode;

        // ------------------------------------------------------------ spawning / look

        public void SpawnOnGround()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            UpdateScale(wa.Left + wa.Width / 2, wa.Bottom - 10, force: true);
            _x = wa.Left + _rng.NextDouble() * Math.Max(1, wa.Width - CanvasW);
            if (Mode == WalkMode.Roam)
                _y = wa.Top + CanvasH * 0.2 + _rng.NextDouble() * Math.Max(1, wa.Height - CanvasH * 1.2);
            else
                _y = wa.Bottom - _species.Ground * _scale;
            _dir = _rng.Next(2) == 0 ? -1 : 1;
            Go(State.Walk);
        }

        public void SpawnFalling()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            UpdateScale(wa.Left + wa.Width / 2, wa.Bottom - 10, force: true);
            _x = wa.Left + _rng.NextDouble() * Math.Max(1, wa.Width - CanvasW);
            _y = wa.Top - CanvasH;
            _vx = (_rng.NextDouble() - 0.5) * 300 * _scale;
            _vy = 0;
            _dir = _vx < 0 ? -1 : 1;
            if (Mode == WalkMode.Roam)
            {
                // drop to a random spot on screen, then roam from there
                _ty = wa.Top + CanvasH + _rng.NextDouble() * Math.Max(1, wa.Height - CanvasH * 1.5);
            }
            Go(State.Falling);
        }

        public void SetLook(Species species, Palette palette)
        {
            double feet = FeetY, cx = CenterX;
            _species = species;
            _pal = palette;
            UpdateScale((int)cx, (int)feet - 2, force: true);
            _x = cx - CanvasW / 2.0;
            _y = feet - _species.Ground * _scale;
        }

        public void OnModeChanged()
        {
            _standOn = IntPtr.Zero;
            if (_state == State.Dragged) return;
            if (Mode == WalkMode.Roam) { if (_state == State.Falling) Go(State.Idle); }
            else if (_state != State.Falling) { _vx = 0; _vy = 0; Go(State.Falling); } // drop down to something solid
        }

        // ------------------------------------------------------------ geometry

        private int CanvasW => (int)Math.Ceiling(_species.W * _scale);
        private int CanvasH => (int)Math.Ceiling(_species.H * _scale);
        private double FeetY => _y + _species.Ground * _scale;
        private double CenterX => _x + CanvasW / 2.0;
        private double EdgeMargin => 18 * _scale;

        private Rectangle WorkAreaAt(double cx, double feetY) =>
            Screen.FromPoint(new Point((int)cx, (int)feetY - 2)).WorkingArea;

        /// <summary>
        /// The feet-Y of the first thing to stand on at cx, searching downward from <paramref name="fromFeet"/>.
        /// </summary>
        private double FindGround(double cx, double fromFeet, double tolerance, out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            double floor = WorkAreaAt(cx, fromFeet).Bottom;
            if (Mode == WalkMode.Windows &&
                _app.Surfaces.FindSurface((int)cx, fromFeet - tolerance, floor, EdgeMargin, out var h, out int top))
            {
                hwnd = h;
                return top;
            }
            return floor;
        }

        private void UpdateScale(int px, int py, bool force = false)
        {
            float scale = Native.DpiAt(px, py) / 96f * _app.Settings.Size;
            if (!force && Math.Abs(scale - _scale) < 0.001f) return;

            double feet = FeetY, cx = CenterX;
            bool hadFrame = _frame != null;
            _scale = scale;
            CreateFrame();
            if (!force && hadFrame)
            {
                _x = cx - CanvasW / 2.0;
                _y = feet - _species.Ground * _scale;
            }
        }

        public void RefreshSize() => UpdateScale((int)CenterX, (int)FeetY - 2);

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
                    _turnAtEdges = _rng.NextDouble() < 0.6;
                    if (Mode == WalkMode.Roam) PickRoamTarget(600);
                    break;
                case State.Run:
                    _dur = 1.8 + _rng.NextDouble() * 2.2;
                    _turnAtEdges = _rng.NextDouble() < 0.4;
                    if (Mode == WalkMode.Roam) PickRoamTarget(1200);
                    break;
                case State.Idle: _dur = 1.5 + _rng.NextDouble() * 3; break;
                case State.Sniff: _dur = 1.8 + _rng.NextDouble() * 1.5; break;
                case State.Roar:
                    _dur = 1.45;
                    if (_app.Settings.Sound) _app.Roar.Play(_species.RoarPitch / Math.Sqrt(_app.Settings.Size));
                    break;
                case State.Sleep: _dur = 10 + _rng.NextDouble() * 15; break;
                case State.Flee: _dur = 1.4 + _rng.NextDouble() * 1.2; _turnAtEdges = false; break;
                case State.Landing: _dur = 0.35; _leaping = false; break;
                case State.Falling: _maxFallSpeed = 0; _standOn = IntPtr.Zero; break;
                case State.Dragged: _dur = double.MaxValue; _standOn = IntPtr.Zero; _leaping = false; break;
            }
        }

        private void PickNext()
        {
            if (Mode == WalkMode.Windows && _rng.NextDouble() < 0.2 && TryJumpUp()) return;

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
            if (_state == State.Falling || _state == State.Dragged) return;
            switch (what)
            {
                case "roar": Go(State.Roar); break;
                case "run": Go(State.Run); break;
                case "sleep": Go(State.Sleep); break;
                case "jump": if (Mode == WalkMode.Windows) TryJumpUp(); break;
            }
        }

        /// <summary>Leap onto a window above us (Windows mode).</summary>
        private bool TryJumpUp()
        {
            double feet = FeetY, cx = CenterX;
            if (!_app.Surfaces.PickJumpTarget(_rng, cx, feet, 480 * _scale, 520 * _scale, EdgeMargin * 1.5, out int tx, out int top))
                return false;

            double g = Gravity;
            double apex = top - 60 * _scale;              // feet-Y at the top of the arc
            double up = feet - apex, down = top - apex;
            double vy0 = -Math.Sqrt(2 * g * up);
            double flight = -vy0 / g + Math.Sqrt(2 * down / g);
            _vx = (tx - cx) / flight;
            _vy = vy0;
            _dir = _vx < 0 ? -1 : 1;
            Go(State.Falling);
            _leaping = true;
            return true;
        }

        private double Gravity => 2600 * _scale;

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
                case State.Walk: Locomote(dt, _species.WalkSpeed, _species.WalkStride, minX, maxX); break;
                case State.Run: Locomote(dt, _species.RunSpeed, _species.RunStride, minX, maxX); break;
                case State.Flee: Locomote(dt, _species.FleeSpeed, _species.RunStride, minX, maxX); break;
                case State.Idle:
                case State.Sniff:
                case State.Roar:
                case State.Sleep:
                case State.Landing:
                    StayGrounded();
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

        // ------------------------------------------------------------ movement

        private void Locomote(double dt, double speedArt, float stride, double minX, double maxX)
        {
            _phase += dt * speedArt * Math.PI / (2 * stride);
            if (Mode == WalkMode.Roam) { RoamStep(dt, speedArt * _scale); return; }

            double step = _dir * speedArt * _scale * dt;

            // on a window: maybe turn around at the edge instead of stepping off
            if (_standOn != IntPtr.Zero && _turnAtEdges && _state != State.Flee &&
                !_app.Surfaces.TryGet(_standOn, (int)(CenterX + step + _dir * 10 * _scale), EdgeMargin, out _))
            {
                _dir = -_dir;
                return;
            }

            _x += step;
            if (_x < minX) { _x = minX; _dir = 1; }
            if (_x > maxX) { _x = maxX; _dir = -1; }
            StayGrounded();
        }

        /// <summary>Keep the feet on whatever we're standing on; start falling if it's gone.</summary>
        private void StayGrounded()
        {
            if (Mode == WalkMode.Roam) { ClampRoam(); return; }

            double cx = CenterX, feet = FeetY;

            if (_standOn != IntPtr.Zero)
            {
                if (_app.Surfaces.TryGet(_standOn, (int)cx, EdgeMargin * 0.5, out var r) && Math.Abs(r.Top - feet) < 120 * _scale)
                {
                    _x += r.Left - _standLeft;   // ride along when the window is dragged
                    _standLeft = r.Left;
                    _y = r.Top - _species.Ground * _scale;
                    return;
                }
                _standOn = IntPtr.Zero;
            }

            double ground = FindGround(cx, feet, 4 * _scale, out var hwnd);
            if (ground > feet + 3 * _scale)
            {
                _vx = _dir * 40 * _scale;
                _vy = 0;
                Go(State.Falling);
                return;
            }
            _y = ground - _species.Ground * _scale;
            Land(hwnd);
        }

        private void Land(IntPtr hwnd)
        {
            _standOn = hwnd;
            if (hwnd != IntPtr.Zero && _app.Surfaces.TryGet(hwnd, (int)CenterX, 0, out var r)) _standLeft = r.Left;
        }

        private void UpdateFall(double dt, double minX, double maxX)
        {
            double prevFeet = FeetY;
            _vy += Gravity * dt;
            _x += _vx * dt;
            _y += _vy * dt;
            _maxFallSpeed = Math.Max(_maxFallSpeed, _vy);
            if (Math.Abs(_vx) > 60 * _scale) _dir = _vx < 0 ? -1 : 1;

            if (_x < minX) { _x = minX; _vx = -_vx * 0.5; }
            if (_x > maxX) { _x = maxX; _vx = -_vx * 0.5; }

            if (_vy <= 0) return; // still going up: platforms are one-way

            double ground;
            IntPtr hwnd = IntPtr.Zero;
            if (Mode == WalkMode.Roam)
                ground = _ty > 0 ? _ty : WorkAreaAt(CenterX, prevFeet).Bottom;
            else
                ground = FindGround(CenterX, prevFeet, 2 * _scale, out hwnd);

            if (FeetY < ground) return;

            _y = ground - _species.Ground * _scale;
            if (_vy > 700 * _scale && !_leaping)
            {
                _vy = -_vy * 0.28;
                _vx *= 0.55;
            }
            else
            {
                _vx = _vy = 0;
                _ty = 0;
                Land(hwnd);
                Go(State.Landing);
            }
        }

        // ---- roam mode

        private void PickRoamTarget(double range)
        {
            double cx = CenterX, feet = FeetY;
            for (int i = 0; i < 8; i++)
            {
                double tx = cx + (_rng.NextDouble() * 2 - 1) * range * _scale;
                double ty = feet + (_rng.NextDouble() * 2 - 1) * range * 0.6 * _scale;
                var wa = WorkAreaAt(tx, ty);
                if (tx < wa.Left + CanvasW / 2.0 || tx > wa.Right - CanvasW / 2.0) continue;
                if (ty < wa.Top + CanvasH || ty > wa.Bottom - 4) continue;
                _tx = tx; _ty = ty;
                SetHeading(tx - cx, ty - feet);
                return;
            }
            // nowhere good nearby: head for the middle of this screen
            var home = WorkAreaAt(cx, feet);
            _tx = home.Left + home.Width / 2.0;
            _ty = home.Top + home.Height * 0.6;
            SetHeading(_tx - cx, _ty - feet);
        }

        private void SetHeading(double dx, double dy)
        {
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            _headX = dx / len;
            _headY = dy / len;
            if (Math.Abs(dx) > 4) _dir = dx < 0 ? -1 : 1;
        }

        private void RoamStep(double dt, double speed)
        {
            double cx = CenterX, feet = FeetY;
            if (_state != State.Flee)
            {
                double dx = _tx - cx, dy = _ty - feet;
                if (dx * dx + dy * dy < (speed * dt * 2) * (speed * dt * 2) + 4) { PickNext(); return; }
                SetHeading(dx, dy);
            }
            _x += _headX * speed * dt;
            _y += _headY * speed * dt * 0.8; // a touch slower vertically reads as "depth"
            ClampRoam();
        }

        private void ClampRoam()
        {
            var wa = WorkAreaAt(CenterX, FeetY);
            double feet = FeetY;
            if (feet > wa.Bottom) { feet = wa.Bottom; _headY = -Math.Abs(_headY); }
            if (feet < wa.Top + CanvasH * 0.9) { feet = wa.Top + CanvasH * 0.9; _headY = Math.Abs(_headY); }
            _y = feet - _species.Ground * _scale;
            var vs = SystemInformation.VirtualScreen;
            if (_x < vs.Left - 12 * _scale) { _x = vs.Left - 12 * _scale; _headX = Math.Abs(_headX); _dir = 1; }
            if (_x > vs.Right - CanvasW + 12 * _scale) { _x = vs.Right - CanvasW + 12 * _scale; _headX = -Math.Abs(_headX); _dir = -1; }
        }

        private void CheckFlee(Point cursor)
        {
            if (!_app.Settings.RunFromMouse || _fleeCooldown > 0 || _pressed) return;
            if (_state != State.Walk && _state != State.Idle && _state != State.Sniff) return;

            double cx = CenterX, midY = _y + CanvasH * 0.6;
            var bounds = new Rectangle((int)_x, (int)_y, CanvasW, CanvasH);
            bool Near(Point c)
            {
                double dx = c.X - cx, dy = c.Y - midY;
                return Mode == WalkMode.Roam
                    ? dx * dx + dy * dy < (230 * _scale) * (230 * _scale)
                    : Math.Abs(dx) < 210 * _scale && c.Y > FeetY - 230 * _scale && c.Y < FeetY + 60 * _scale;
            }

            if (Near(cursor) && !Near(_lastCursor) && !bounds.Contains(cursor))
            {
                if (_rng.NextDouble() < 0.7)
                {
                    double dx = cx - cursor.X, dy = midY - cursor.Y;
                    _dir = dx < 0 ? -1 : 1;
                    Go(State.Flee);
                    if (Mode == WalkMode.Roam) SetHeading(dx, dy);
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
                if (Mode == WalkMode.Roam)
                {
                    // set down where it was dropped (a hard fling still slides it a little)
                    _vx = Math.Max(-1500 * _scale, Math.Min(1500 * _scale, _throwVx));
                    _vy = Math.Min(0, _throwVy) * 0.5;
                    _ty = Math.Min(WorkAreaAt(CenterX, FeetY).Bottom, FeetY + 30 * _scale);
                    Go(State.Falling);
                    return;
                }
                double cap = 3000 * _scale;
                _vx = Math.Max(-cap, Math.Min(cap, _throwVx));
                _vy = Math.Max(-cap, Math.Min(cap, _throwVy));
                Go(State.Falling);
                return;
            }

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
                    p.Stride = _species.WalkStride; p.Lift = _species.WalkLift;
                    p.Bob = -(float)Math.Abs(Math.Sin(_phase)) * 2;
                    p.TailSway = (float)Math.Sin(_phase) * 2;
                    break;

                case State.Run:
                case State.Flee:
                    p.Stride = _species.RunStride; p.Lift = _species.RunLift;
                    p.Bob = -(float)Math.Abs(Math.Sin(_phase)) * 4;
                    p.TailSway = (float)Math.Sin(_phase) * 3;
                    p.HeadDrop = 0.12f;
                    if (_state == State.Flee) { p.Jaw = 0.35f + 0.1f * (float)Math.Sin(_time * 20); p.EyesClosed = false; }
                    break;

                case State.Sniff:
                {
                    float k = (float)Math.Min(1, Math.Min(_t / 0.45, (_dur - _t) / 0.45));
                    p.HeadDrop = Math.Max(0, k) * (0.95f + 0.05f * (float)Math.Sin(_time * 22));
                    break;
                }

                case State.Roar:
                {
                    float k = Math.Max(0, (float)Math.Min(1, Math.Min(_t / 0.18, (_dur - _t) / 0.3)));
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
                    if (_leaping)
                    {
                        // a deliberate jump: legs tucked mid-stride, head forward
                        p.Stride = _species.RunStride; p.Lift = _species.RunLift;
                        p.Phase = Math.PI * 1.5;
                        p.HeadDrop = 0.1f;
                        p.Shadow = 0;
                        break;
                    }
                    p.Dangle = true;
                    p.Shadow = 0;
                    p.Jaw = 0.3f + 0.15f * (float)Math.Sin(_time * 11);
                    p.TailSway = (float)Math.Sin(_time * 8) * 7;
                    p.HeadDrop = -0.15f;
                    p.EyesClosed = false;
                    break;
            }
            return p;
        }

        private void Render()
        {
            using (var g = Graphics.FromImage(_frame))
                _species.Draw(g, _pal, BuildPose(), _scale);
        }

        // The frame is a GDI DIB section that GDI+ draws into directly, so presenting a frame
        // needs no copy or allocation.
        private IntPtr _memDc, _dib, _oldBmp;

        private void CreateFrame()
        {
            FreeFrame();
            int w = CanvasW, h = CanvasH;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                var bmi = new Native.BITMAPINFOHEADER
                {
                    biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
                };
                _dib = Native.CreateDIBSection(screenDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
                _memDc = Native.CreateCompatibleDC(screenDc);
                _oldBmp = Native.SelectObject(_memDc, _dib);
                _frame = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screenDc); }
        }

        private void FreeFrame()
        {
            _frame?.Dispose();
            _frame = null;
            if (_memDc != IntPtr.Zero)
            {
                Native.SelectObject(_memDc, _oldBmp);
                Native.DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero) { Native.DeleteObject(_dib); _dib = IntPtr.Zero; }
        }

        private void Present()
        {
            if (!IsHandleCreated) return;
            Native.GdiFlush();
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
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
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
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
            FreeFrame();
            base.Dispose(disposing);
        }

        /// <summary>Render a standalone picture of a dino, centred in a square (icons, picker).</summary>
        public static Bitmap Snapshot(Species species, Palette pal, int size, Pose? pose = null)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            float scale = size / Math.Max(species.W, species.H);
            using (var art = new Bitmap((int)Math.Ceiling(species.W * scale), (int)Math.Ceiling(species.H * scale), PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(art))
                    species.Draw(g, pal, pose ?? new Pose { Stride = species.WalkStride, Lift = species.WalkLift, Phase = 0.6, HeadDrop = -0.1f, Jaw = 0.55f }, scale);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(art, (size - art.Width) / 2f, (size - art.Height) / 2f + size * 0.04f, art.Width, art.Height);
                }
            }
            return bmp;
        }
    }
}
