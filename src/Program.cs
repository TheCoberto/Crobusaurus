using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Sailback
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Dev helpers (regenerate the icon and docs images from the vector art).
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                IconWriter.Write(args[1], 16, 24, 32, 48, 64, 128, 256);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--export-sheet") { ExportSheet(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--dump-surfaces")
            {
                var s = new Surfaces();
                s.Refresh(new IntPtr[0]);
                File.WriteAllText(args[1], s.Describe());
                return 0;
            }
            if (args.Length == 2 && args[0] == "--export-lineup") { ExportLineup(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--export-picker")
            {
                using (var f = new PickerForm(new Settings { Herd = { ("sailback", "Fern") } }))
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-20000, 0);
                    f.Opacity = 0.01;
                    f.Show();
                    Application.DoEvents();
                    f.Opacity = 1;
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(args[1], ImageFormat.Png);
                }
                return 0;
            }

            bool forcePicker = args.Any(a => a.Equals("--pick", StringComparison.OrdinalIgnoreCase));

            using (var mutex = new Mutex(true, @"Local\Sailback.SingleInstance", out bool first))
            {
                if (!first)
                {
                    // Already running: ask the running copy to drop in another dino.
                    IntPtr running = Native.FindWindow(null, Dino.WindowTitle);
                    if (running != IntPtr.Zero)
                        Native.PostMessage(running, Native.RegisterWindowMessage("Sailback.AddDino.v1"), IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SailbackApp(forcePicker));
            }
            return 0;
        }

        /// <summary>Every species in every main pose, one row per species.</summary>
        private static void ExportSheet(string path)
        {
            var poses = new (string, Func<Species, Pose>)[]
            {
                ("walk", s => new Pose { Stride = s.WalkStride, Lift = s.WalkLift, Phase = 0.3, Shadow = 1 }),
                ("run", s => new Pose { Stride = s.RunStride, Lift = s.RunLift, Phase = Math.PI * 1.5, HeadDrop = 0.12f, Bob = -4, Shadow = 1 }),
                ("sniff", s => new Pose { HeadDrop = 1, Shadow = 1 }),
                ("roar", s => new Pose { Jaw = 1, HeadDrop = -0.38f, TailSway = 4, Shadow = 1 }),
                ("sleep", s => new Pose { Crouch = 1, HeadDrop = 0.55f, EyesClosed = true, Snoring = true, Time = 0.4, Shadow = 1 }),
                ("held", s => new Pose { Dangle = true, Jaw = 0.4f, HeadDrop = -0.15f, TailSway = 6, Time = 0.3 }),
            };
            const float scale = 1.0f;
            int cw = (int)(Species.All.Max(s => s.W) * scale) + 10, ch = (int)(Species.All.Max(s => s.H) * scale) + 20;
            using (var sheet = new Bitmap(cw * poses.Length, ch * Species.All.Length))
            using (var g = Graphics.FromImage(sheet))
            using (var font = new Font("Segoe UI", 9))
            {
                g.Clear(Color.FromArgb(235, 238, 242));
                for (int r = 0; r < Species.All.Length; r++)
                {
                    var sp = Species.All[r];
                    for (int c = 0; c < poses.Length; c++)
                    {
                        int x = c * cw, y = r * ch;
                        using (var cell = new Bitmap((int)Math.Ceiling(sp.W * scale), (int)Math.Ceiling(sp.H * scale), PixelFormat.Format32bppPArgb))
                        {
                            using (var cg = Graphics.FromImage(cell))
                                sp.Draw(cg, Palette.Find(sp.DefaultPalette), poses[c].Item2(sp), scale);
                            g.DrawImage(cell, x + (cw - cell.Width) / 2, y + ch - cell.Height - 2);
                        }
                        g.DrawString(sp.Name + " - " + poses[c].Item1, font, Brushes.Black, x + 4, y + 2);
                    }
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>All species side by side, standing on a common ground line.</summary>
        private static void ExportLineup(string path)
        {
            const float scale = 1.6f;
            int w = (int)(Species.All.Sum(s => s.W) * scale) + 40;
            int h = (int)(Species.All.Max(s => s.H) * scale) + 20;
            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                float x = 20;
                foreach (var sp in Species.All)
                {
                    using (var cell = new Bitmap((int)Math.Ceiling(sp.W * scale), (int)Math.Ceiling(sp.H * scale), PixelFormat.Format32bppPArgb))
                    {
                        using (var cg = Graphics.FromImage(cell))
                            sp.Draw(cg, Palette.Find(sp.DefaultPalette), new Pose { Stride = sp.WalkStride, Lift = sp.WalkLift, Phase = 0.6, Shadow = 1, Jaw = 0.4f, HeadDrop = -0.1f }, scale);
                        g.DrawImage(cell, x, h - 10 - sp.Ground * scale);
                    }
                    x += sp.W * scale;
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    /// <summary>Writes a multi-resolution .ico (PNG-compressed entries) from the vector art.</summary>
    internal static class IconWriter
    {
        public static void Write(string path, params int[] sizes)
        {
            var images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bmp = Dino.Snapshot(Species.All[0], Palette.All[0], sizes[i]))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images[i] = ms.ToArray();
                }

            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }
}
