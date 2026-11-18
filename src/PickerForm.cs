using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace Crobusaurus
{
    /// <summary>"Choose your dinosaur": species, colours, how many, size and where they walk.</summary>
    internal sealed class PickerForm : Form
    {
        public static readonly (string Label, float Value)[] Sizes =
            { ("Small", 0.75f), ("Medium", 1f), ("Large", 1.4f), ("Huge", 2f) };

        private const int MixPalette = -1;

        private readonly List<SpeciesCard> _cards = new List<SpeciesCard>();
        private readonly List<Swatch> _swatches = new List<Swatch>();
        private readonly RadioButton[] _modes;
        private readonly NumericUpDown _count;
        private readonly ComboBox _size;
        private readonly CheckBox _mixSpecies;
        private readonly Timer _anim;
        private readonly float _s;
        private double _time;

        private Species _species;
        private int _palette;

        public WalkMode ChosenMode => _modes[1].Checked ? WalkMode.Windows : _modes[2].Checked ? WalkMode.Roam : WalkMode.Taskbar;
        public float ChosenSize => Sizes[Math.Max(0, _size.SelectedIndex)].Value;

        public PickerForm(Settings settings)
        {
            // DeviceDpi is unreliable on .NET Framework before the handle exists; ask the monitor directly.
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            _s = Native.DpiAt(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2) / 96f;
            int S(float v) => (int)Math.Round(v * _s);

            Text = "Crobusaurus - choose your dinosaur";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f * 1); // GDI+ fonts scale with DPI on their own
            BackColor = Color.FromArgb(246, 244, 238);
            TopMost = true;
            Icon = CrobusaurusApp.MakeIcon(32);

            var first = settings.Herd.FirstOrDefault();
            _species = Species.Find(first.Species ?? "crobusaurus");
            var paletteNames = settings.Herd.Select(h => h.Palette).Distinct().ToList();
            _palette = paletteNames.Count > 1 ? MixPalette : Array.FindIndex(Palette.All, p => p.Name == (first.Palette ?? "Fern"));
            if (_palette < MixPalette) _palette = 0;

            int pad = S(16), y = pad;

            AddLabel("Pick a dinosaur", pad, y, bold: true, size: 12);
            y += S(30);

            int cardW = S(170), cardH = S(176), gap = S(10);
            for (int i = 0; i < Species.All.Length; i++)
            {
                var card = new SpeciesCard(this, Species.All[i]) { Bounds = new Rectangle(pad + i * (cardW + gap), y, cardW, cardH) };
                card.Click += (s, e) => { _species = ((SpeciesCard)s).Species; RefreshSelection(); };
                _cards.Add(card);
                Controls.Add(card);
            }
            int contentW = Species.All.Length * cardW + (Species.All.Length - 1) * gap;
            y += cardH + S(14);

            AddLabel("Colours", pad, y, bold: true, size: 10.5f);
            y += S(26);
            int sw = S(38);
            for (int i = 0; i < Palette.All.Length; i++)
            {
                var swatch = new Swatch(i) { Bounds = new Rectangle(pad + i * (sw + S(8)), y, sw, sw) };
                swatch.Click += (s, e) => { _palette = ((Swatch)s).Index; RefreshSelection(); };
                new ToolTip().SetToolTip(swatch, Palette.All[i].Name);
                _swatches.Add(swatch);
                Controls.Add(swatch);
            }
            var mix = new Swatch(MixPalette) { Bounds = new Rectangle(pad + Palette.All.Length * (sw + S(8)), y, sw, sw) };
            mix.Click += (s, e) => { _palette = MixPalette; RefreshSelection(); };
            new ToolTip().SetToolTip(mix, "Mixed - every dino gets different colours");
            _swatches.Add(mix);
            Controls.Add(mix);

            // how many + size, to the right of the swatches
            int rightX = pad + contentW - S(250);
            AddLabel("How many", rightX, y - S(26), bold: true, size: 10.5f);
            _count = new NumericUpDown
            {
                Minimum = 1, Maximum = CrobusaurusApp.MaxDinos, Value = Math.Max(1, Math.Min(CrobusaurusApp.MaxDinos, settings.Herd.Count)),
                Bounds = new Rectangle(rightX, y + S(6), S(70), S(26)),
            };
            Controls.Add(_count);
            AddLabel("Size", rightX + S(100), y - S(26), bold: true, size: 10.5f);
            _size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(rightX + S(100), y + S(6), S(150), S(26)) };
            foreach (var (label, _) in Sizes) _size.Items.Add(label);
            int sizeIdx = Array.FindIndex(Sizes, z => Math.Abs(z.Value - settings.Size) < 0.01f);
            _size.SelectedIndex = sizeIdx >= 0 ? sizeIdx : 1;
            Controls.Add(_size);
            y += sw + S(8);

            _mixSpecies = new CheckBox
            {
                Text = "Mix species too (a herd of different dinosaurs)",
                AutoSize = true,
                Location = new Point(pad, y),
                Checked = settings.Herd.Select(h => h.Species).Distinct().Count() > 1,
            };
            Controls.Add(_mixSpecies);
            y += S(34);

            AddLabel("Where should they walk?", pad, y, bold: true, size: 10.5f);
            y += S(26);
            var modeText = new[]
            {
                ("Along the taskbar", "The classic: back and forth along the top of the taskbar."),
                ("Taskbar and on top of windows", "They jump up onto open windows, walk along the title bars, ride along when you move a window, and tumble off the edges."),
                ("Anywhere on the screen", "Free roam: they wander all over the desktop in every direction."),
            };
            _modes = new RadioButton[modeText.Length];
            for (int i = 0; i < modeText.Length; i++)
            {
                _modes[i] = new RadioButton
                {
                    Text = modeText[i].Item1,
                    Font = new Font(Font, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(pad, y),
                };
                Controls.Add(_modes[i]);
                var desc = AddLabel(modeText[i].Item2, pad + S(20), y + S(22), bold: false, size: 9, color: Color.FromArgb(90, 90, 90));
                desc.MaximumSize = new Size(contentW - S(20), 0);
                y += S(22) + desc.PreferredHeight + S(8);
            }
            _modes[(int)settings.Mode].Checked = true;
            y += S(6);

            var ok = new Button { Text = "Let's go!", DialogResult = DialogResult.OK, Font = new Font(Font, FontStyle.Bold), Bounds = new Rectangle(pad + contentW - S(220), y, S(110), S(34)) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(pad + contentW - S(100), y, S(100), S(34)) };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            y += S(34) + pad;

            ClientSize = new Size(contentW + pad * 2, y);

            _anim = new Timer { Interval = 33 };
            _anim.Tick += (s, e) => { _time += 0.033; foreach (var c in _cards) c.Invalidate(); };
            _anim.Start();
            RefreshSelection();
        }

        private Label AddLabel(string text, int x, int y, bool bold, float size, Color? color = null)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? Color.FromArgb(40, 40, 40),
                BackColor = Color.Transparent,
            };
            Controls.Add(l);
            return l;
        }

        private void RefreshSelection()
        {
            foreach (var c in _cards) { c.Selected = c.Species == _species; c.Invalidate(); }
            foreach (var s in _swatches) { s.Selected = s.Index == _palette; s.Invalidate(); }
        }

        /// <summary>Palette shown on the cards (the species' favourite when "mixed" is selected).</summary>
        private Palette PreviewPalette(Species s) => _palette >= 0 ? Palette.All[_palette] : Palette.Find(s.DefaultPalette);

        public List<(string, string)> BuildHerd()
        {
            int n = (int)_count.Value;
            var herd = new List<(string, string)>();
            for (int i = 0; i < n; i++)
            {
                var species = _mixSpecies.Checked ? Species.All[(Array.IndexOf(Species.All, _species) + i) % Species.All.Length] : _species;
                var palette = _palette >= 0 ? Palette.All[_palette]
                    : i == 0 ? Palette.Find(species.DefaultPalette)
                    : Palette.All[(Array.FindIndex(Palette.All, p => p.Name == _species.DefaultPalette) + i) % Palette.All.Length];
                herd.Add((species.Id, palette.Name));
            }
            return herd;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _anim.Stop();
            _anim.Dispose();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------ controls

        private sealed class SpeciesCard : Control
        {
            private readonly PickerForm _owner;
            public readonly Species Species;
            public bool Selected;

            public SpeciesCard(PickerForm owner, Species species)
            {
                _owner = owner;
                Species = species;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = _owner._s;
                var r = new RectangleF(1, 1, Width - 3, Height - 3);
                using (var path = Rounded(r, 10 * s))
                {
                    using (var b = new SolidBrush(Selected ? Color.FromArgb(255, 255, 248, 225) : Color.White)) g.FillPath(b, path);
                    using (var pen = new Pen(Selected ? Color.FromArgb(214, 140, 40) : Color.FromArgb(215, 210, 200), Selected ? 3 * s : 1.2f * s))
                        g.DrawPath(pen, path);
                }

                // animated walking preview
                int artH = (int)(Height * 0.62f);
                float scale = Math.Min((Width - 16 * s) / Species.W, artH / Species.H);
                double t = _owner._time;
                double phase = t * Species.WalkSpeed * Math.PI / (2 * Species.WalkStride);
                var pose = new Pose
                {
                    Phase = phase, Time = t, Stride = Species.WalkStride, Lift = Species.WalkLift, Shadow = 1,
                    Bob = -(float)Math.Abs(Math.Sin(phase)) * 2, TailSway = (float)Math.Sin(phase) * 2,
                    EyesClosed = (t % 4.3) < 0.13,
                };
                if (Selected && (t % 5) > 3.6) { float k = (float)Math.Sin(Math.PI * ((t % 5) - 3.6) / 1.4); pose.Jaw = k; pose.HeadDrop = -0.38f * k; pose.Stride = 0; }
                using (var bmp = new Bitmap((int)Math.Ceiling(Species.W * scale), (int)Math.Ceiling(Species.H * scale), PixelFormat.Format32bppPArgb))
                {
                    using (var bg = Graphics.FromImage(bmp)) Species.Draw(bg, _owner.PreviewPalette(Species), pose, scale);
                    g.DrawImage(bmp, (Width - bmp.Width) / 2f, 8 * s + (artH - bmp.Height));
                }

                using (var nameFont = new Font("Segoe UI", 11, FontStyle.Bold))
                using (var blurbFont = new Font("Segoe UI", 8.25f))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
                {
                    float ty = artH + 12 * s;
                    g.DrawString(Species.Name, nameFont, Brushes.Black, new RectangleF(4, ty, Width - 8, 24 * s), fmt);
                    using (var grey = new SolidBrush(Color.FromArgb(95, 95, 95)))
                        g.DrawString(Species.Blurb, blurbFont, grey, new RectangleF(8 * s, ty + 22 * s, Width - 16 * s, Height - ty - 24 * s), fmt);
                }
            }
        }

        private sealed class Swatch : Control
        {
            public readonly int Index;
            public bool Selected;

            public Swatch(int index)
            {
                Index = index;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float inset = Width * 0.14f;
                var r = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);

                if (Index >= 0)
                {
                    var p = Palette.All[Index];
                    using (var body = new SolidBrush(p.Body)) g.FillPie(body, r.X, r.Y, r.Width, r.Height, 90, 180);
                    using (var accent = new SolidBrush(p.Accent)) g.FillPie(accent, r.X, r.Y, r.Width, r.Height, 270, 180);
                    using (var o = new Pen(p.Outline, 1.5f)) g.DrawEllipse(o, r);
                }
                else
                {
                    for (int i = 0; i < 6; i++)
                        using (var b = new SolidBrush(Palette.All[i % Palette.All.Length].Body))
                            g.FillPie(b, r.X, r.Y, r.Width, r.Height, i * 60, 60);
                    using (var o = new Pen(Color.FromArgb(60, 60, 60), 1.5f)) g.DrawEllipse(o, r);
                }

                if (Selected)
                    using (var ring = new Pen(Color.FromArgb(214, 140, 40), Width * 0.08f))
                        g.DrawEllipse(ring, 2, 2, Width - 5, Height - 5);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
