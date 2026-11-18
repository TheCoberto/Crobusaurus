using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sailback
{
    /// <summary>Owns the dinosaurs, the tray icon, the shared menu and the animation clock.</summary>
    internal sealed class SailbackApp : ApplicationContext
    {
        public const int MaxDinos = 8;

        public readonly Settings Settings = Settings.Load();
        public readonly RoarSynth Roar = new RoarSynth();
        public readonly int AddDinoMessage = Native.RegisterWindowMessage("Sailback.AddDino.v1");

        private readonly List<Dino> _dinos = new List<Dino>();
        private readonly NotifyIcon _tray;
        private readonly Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Random _rng = new Random();
        private double _last;
        private double _fullscreenCheck;
        private bool _fullscreen;
        private Icon _trayIcon;

        public SailbackApp()
        {
            for (int i = 0; i < Settings.Count; i++) AddDino(fromSky: i > 0);

            _trayIcon = MakeIcon(SystemInformation.SmallIconSize.Width);
            _tray = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "Sailback - right-click for options",
                Visible = true,
                ContextMenu = new ContextMenu(),
            };
            _tray.ContextMenu.Popup += (s, e) => BuildMenu(_tray.ContextMenu, null);
            _tray.MouseDoubleClick += (s, e) => { foreach (var d in _dinos) d.Command("roar"); };

            _timer = new Timer { Interval = 15 };
            _timer.Tick += OnTick;
            _timer.Start();
            _last = _clock.Elapsed.TotalSeconds;
        }

        private void OnTick(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, now - _last); // clamp so a stall (sleep/resume) doesn't teleport anyone
            _last = now;

            _fullscreenCheck -= dt;
            if (_fullscreenCheck <= 0)
            {
                _fullscreenCheck = 1;
                _fullscreen = Settings.HideForFullscreen && ForegroundIsFullscreen();
                if (Settings.AlwaysOnTop && !_fullscreen)
                    foreach (var d in _dinos) d.ApplyTopMost(); // the taskbar likes to jump above us
            }

            foreach (var d in _dinos.ToArray())
            {
                d.PollDragStart();
                d.Tick(dt, _fullscreen);
            }
        }

        // ------------------------------------------------------------ dinos

        public void AddDino(bool fromSky)
        {
            if (_dinos.Count >= MaxDinos) return;
            var used = _dinos.Select(x => x.Palette).ToList();
            var palette = Palette.All.FirstOrDefault(p => !used.Contains(p)) ?? Palette.All[_rng.Next(Palette.All.Length)];
            var d = new Dino(this, palette, _rng.Next());
            d.Show();
            if (fromSky) d.SpawnFalling(); else d.SpawnOnGround();
            _dinos.Add(d);
        }

        private void RemoveDino(Dino d)
        {
            if (_dinos.Count <= 1) return;
            _dinos.Remove(d);
            d.Close();
            d.Dispose();
        }

        /// <summary>Launching Sailback again while it's running adds another dino instead of a second copy.</summary>
        public void OnAnotherInstanceLaunched(Dino receiver)
        {
            if (receiver != _dinos.FirstOrDefault()) return;
            AddDino(fromSky: true);
            SaveCount();
        }

        private void SaveCount()
        {
            Settings.Count = _dinos.Count;
            Settings.Save();
        }

        // ------------------------------------------------------------ menu

        public void ShowMenu(Dino target, Point clientPos)
        {
            var menu = new ContextMenu();
            BuildMenu(menu, target);
            menu.Show(target, clientPos);
        }

        private void BuildMenu(ContextMenu menu, Dino target)
        {
            menu.MenuItems.Clear();
            IEnumerable<Dino> who = target != null ? new[] { target } : (IEnumerable<Dino>)_dinos;
            string suffix = target == null && _dinos.Count > 1 ? " (all)" : "";

            menu.MenuItems.Add("Roar!" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("roar"); }).DefaultItem = true;
            menu.MenuItems.Add("Run!" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("run"); });
            menu.MenuItems.Add("Nap time" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("sleep"); });
            menu.MenuItems.Add("-");

            var add = menu.MenuItems.Add("Add another dino", (s, e) => { AddDino(true); SaveCount(); });
            add.Enabled = _dinos.Count < MaxDinos;
            if (_dinos.Count > 1)
                menu.MenuItems.Add(target != null ? "Send this dino home" : "Remove a dino",
                    (s, e) => { RemoveDino(target ?? _dinos.Last()); SaveCount(); });

            var size = menu.MenuItems.Add("Size");
            foreach (var (label, value) in new[] { ("Small", 0.75f), ("Medium", 1f), ("Large", 1.4f), ("Huge", 2f) })
            {
                var item = size.MenuItems.Add(label, (s, e) =>
                {
                    Settings.Size = value;
                    Settings.Save();
                    foreach (var d in _dinos) d.RefreshSize();
                });
                item.RadioCheck = true;
                item.Checked = Math.Abs(Settings.Size - value) < 0.01f;
            }

            menu.MenuItems.Add("-");
            Toggle(menu, "Sounds", Settings.Sound, v => Settings.Sound = v);
            Toggle(menu, "Run away from the mouse", Settings.RunFromMouse, v => Settings.RunFromMouse = v);
            Toggle(menu, "Always on top", Settings.AlwaysOnTop, v =>
            {
                Settings.AlwaysOnTop = v;
                foreach (var d in _dinos) d.ApplyTopMost();
            });
            Toggle(menu, "Hide during full-screen apps", Settings.HideForFullscreen, v => Settings.HideForFullscreen = v);
            var startup = menu.MenuItems.Add("Start with Windows", (s, e) => Settings.StartWithWindows = !Settings.StartWithWindows);
            startup.Checked = Settings.StartWithWindows;

            menu.MenuItems.Add("-");
            menu.MenuItems.Add("About Sailback", (s, e) => MessageBox.Show(
                "Sailback 1.0\n\nA little sail-backed dinosaur that roams your taskbar.\n\n" +
                "• Click it to make it roar (double-click and it bolts).\n" +
                "• Drag it around and fling it - it lands on its feet.\n" +
                "• Get the mouse close and it may run away.\n" +
                "• Leave it alone long enough and it naps.\n\n" +
                "Run Sailback.exe again to add another dino.",
                "About Sailback", MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.MenuItems.Add("Exit", (s, e) => ExitThread());
        }

        private void Toggle(ContextMenu menu, string text, bool current, Action<bool> set)
        {
            var item = menu.MenuItems.Add(text, (s, e) => { set(!current); Settings.Save(); });
            item.Checked = current;
        }

        // ------------------------------------------------------------ helpers

        private bool ForegroundIsFullscreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (_dinos.Any(d => d.IsHandleCreated && d.Handle == fg)) return false;
            string cls = Native.ClassName(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
            if (!Native.GetWindowRect(fg, out var r)) return false;
            var bounds = Screen.FromHandle(fg).Bounds;
            return r.Left <= bounds.Left && r.Top <= bounds.Top && r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
        }

        private static Icon MakeIcon(int size)
        {
            using (var bmp = Dino.Snapshot(Palette.All[0], Math.Max(16, size)))
            {
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            foreach (var d in _dinos) { d.Close(); d.Dispose(); }
            _dinos.Clear();
            Roar.Dispose();
            base.ExitThreadCore();
        }
    }
}
