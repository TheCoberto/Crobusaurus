using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Crobusaurus
{
    /// <summary>Owns the dinosaurs, the tray icon, the shared menu and the animation clock.</summary>
    internal sealed class CrobusaurusApp : ApplicationContext
    {
        public const int MaxDinos = 8;

        public readonly Settings Settings = Settings.Load();
        public readonly RoarSynth Roar = new RoarSynth();
        public readonly Surfaces Surfaces = new Surfaces();
        public readonly int AddDinoMessage = Native.RegisterWindowMessage("Crobusaurus.AddDino.v1");

        private readonly List<Dino> _dinos = new List<Dino>();
        private readonly NotifyIcon _tray;
        private readonly Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Random _rng = new Random();
        private double _last, _fullscreenCheck, _surfaceCheck;
        private bool _fullscreen;
        private PickerForm _picker;

        public CrobusaurusApp(bool forcePicker)
        {
            if (!Settings.HasChosen || forcePicker)
            {
                using (var picker = new PickerForm(Settings))
                    if (picker.ShowDialog() == DialogResult.OK) ApplyChoice(picker, spawn: false);
                Settings.HasChosen = true;
                Settings.Save();
            }

            for (int i = 0; i < Settings.Herd.Count; i++)
                AddDino(Species.Find(Settings.Herd[i].Species), Palette.Find(Settings.Herd[i].Palette), fromSky: i > 0, save: false);

            _tray = new NotifyIcon
            {
                Icon = MakeIcon(SystemInformation.SmallIconSize.Width),
                Text = "Crobusaurus - right-click for options",
                Visible = true,
                ContextMenu = new ContextMenu(),
            };
            _tray.ContextMenu.Popup += (s, e) => BuildMenu(_tray.ContextMenu, null);
            _tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenPicker(); };

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

            if (Settings.Mode == WalkMode.Windows)
            {
                _surfaceCheck -= dt;
                if (_surfaceCheck <= 0)
                {
                    _surfaceCheck = 0.1;
                    Surfaces.Refresh(_dinos.Where(d => d.IsHandleCreated).Select(d => d.Handle)
                        .Concat(_picker != null && _picker.IsHandleCreated ? new[] { _picker.Handle } : new IntPtr[0]));
                }
            }

            foreach (var d in _dinos.ToArray())
            {
                d.PollDragStart();
                d.Tick(dt, _fullscreen);
            }
        }

        // ------------------------------------------------------------ dinos

        private void AddDino(Species species, Palette palette, bool fromSky, bool save = true)
        {
            if (_dinos.Count >= MaxDinos) return;
            var d = new Dino(this, species, palette, _rng.Next());
            d.Show();
            if (fromSky) d.SpawnFalling(); else d.SpawnOnGround();
            _dinos.Add(d);
            if (save) SaveHerd();
        }

        private void AddRandomDino()
        {
            var used = _dinos.Select(x => x.Palette).ToList();
            var species = _dinos.Count > 0 ? _dinos[_dinos.Count - 1].Species : Species.All[0];
            var palette = Palette.All.FirstOrDefault(p => !used.Contains(p)) ?? Palette.All[_rng.Next(Palette.All.Length)];
            AddDino(species, palette, fromSky: true);
        }

        private void RemoveDino(Dino d)
        {
            if (_dinos.Count <= 1) return;
            _dinos.Remove(d);
            d.Close();
            d.Dispose();
            SaveHerd();
        }

        private void SaveHerd()
        {
            Settings.Herd = _dinos.Select(d => (d.Species.Id, d.Palette.Name)).ToList();
            Settings.Save();
        }

        /// <summary>Launching Crobusaurus again while it's running adds another dino instead of a second copy.</summary>
        public void OnAnotherInstanceLaunched(Dino receiver)
        {
            if (receiver == _dinos.FirstOrDefault()) AddRandomDino();
        }

        // ------------------------------------------------------------ picker

        private void OpenPicker()
        {
            if (_picker != null) { _picker.Activate(); return; }
            _picker = new PickerForm(Settings);
            _picker.FormClosed += (s, e) =>
            {
                if (_picker.DialogResult == DialogResult.OK) ApplyChoice(_picker, spawn: true);
                _picker.Dispose();
                _picker = null;
            };
            _picker.Show();
        }

        private void ApplyChoice(PickerForm picker, bool spawn)
        {
            bool modeChanged = Settings.Mode != picker.ChosenMode;
            Settings.Mode = picker.ChosenMode;
            Settings.Size = picker.ChosenSize;
            Settings.Herd = picker.BuildHerd();
            Settings.HasChosen = true;
            Settings.Save();
            if (!spawn) return;

            foreach (var d in _dinos) { d.Close(); d.Dispose(); }
            _dinos.Clear();
            for (int i = 0; i < Settings.Herd.Count; i++)
                AddDino(Species.Find(Settings.Herd[i].Species), Palette.Find(Settings.Herd[i].Palette), fromSky: i > 0, save: false);
            if (modeChanged) _surfaceCheck = 0;
        }

        private void SetMode(WalkMode mode)
        {
            Settings.Mode = mode;
            Settings.Save();
            _surfaceCheck = 0;
            if (mode == WalkMode.Windows)
                Surfaces.Refresh(_dinos.Where(d => d.IsHandleCreated).Select(d => d.Handle));
            foreach (var d in _dinos) d.OnModeChanged();
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

            menu.MenuItems.Add("Choose dinosaur...", (s, e) => OpenPicker()).DefaultItem = target == null;
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Roar!" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("roar"); }).DefaultItem = target != null;
            menu.MenuItems.Add("Run!" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("run"); });
            if (Settings.Mode == WalkMode.Windows)
                menu.MenuItems.Add("Jump up!" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("jump"); });
            menu.MenuItems.Add("Nap time" + suffix, (s, e) => { foreach (var d in who.ToArray()) d.Command("sleep"); });

            if (target != null)
            {
                menu.MenuItems.Add("-");
                var sp = menu.MenuItems.Add("Species");
                foreach (var species in Species.All)
                {
                    var item = sp.MenuItems.Add(species.Name, (s, e) => { target.SetLook(species, target.Palette); SaveHerd(); });
                    item.RadioCheck = true;
                    item.Checked = target.Species == species;
                }
                var col = menu.MenuItems.Add("Colours");
                foreach (var palette in Palette.All)
                {
                    var item = col.MenuItems.Add(palette.Name, (s, e) => { target.SetLook(target.Species, palette); SaveHerd(); });
                    item.RadioCheck = true;
                    item.Checked = target.Palette == palette;
                }
            }

            menu.MenuItems.Add("-");
            var where = menu.MenuItems.Add("Where to walk");
            foreach (var (label, mode) in new[]
                     {
                         ("Along the taskbar", WalkMode.Taskbar),
                         ("Taskbar and on top of windows", WalkMode.Windows),
                         ("Anywhere on screen", WalkMode.Roam),
                     })
            {
                var item = where.MenuItems.Add(label, (s, e) => SetMode(mode));
                item.RadioCheck = true;
                item.Checked = Settings.Mode == mode;
            }

            var size = menu.MenuItems.Add("Size");
            foreach (var (label, value) in PickerForm.Sizes)
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

            var add = menu.MenuItems.Add("Add another dino", (s, e) => AddRandomDino());
            add.Enabled = _dinos.Count < MaxDinos;
            if (_dinos.Count > 1)
                menu.MenuItems.Add(target != null ? "Send this dino home" : "Remove a dino", (s, e) => RemoveDino(target ?? _dinos.Last()));

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
            menu.MenuItems.Add("About Crobusaurus", (s, e) => MessageBox.Show(
                "Crobusaurus 2.0\n\nLittle dinosaurs that roam your desktop.\n\n" +
                "• Click one to make it roar (double-click and it bolts).\n" +
                "• Drag it around and fling it - it lands on its feet.\n" +
                "• Get the mouse close and it may run away.\n" +
                "• Leave it alone long enough and it naps.\n" +
                "• Right-click a dino to change its species or colours.\n\n" +
                "Run Crobusaurus.exe again to add another dino.",
                "About Crobusaurus", MessageBoxButtons.OK, MessageBoxIcon.Information));
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

        public static Icon MakeIcon(int size)
        {
            using (var bmp = Dino.Snapshot(Species.All[0], Palette.All[0], Math.Max(16, size)))
                return Icon.FromHandle(bmp.GetHicon());
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
            _picker?.Close();
            _tray.Visible = false;
            _tray.Dispose();
            foreach (var d in _dinos) { d.Close(); d.Dispose(); }
            _dinos.Clear();
            Roar.Dispose();
            base.ExitThreadCore();
        }
    }
}
