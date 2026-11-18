using System;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Sailback
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Dev helper: Sailback.exe --export-icon path.ico   (regenerates the app icon from the vector art)
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                IconWriter.Write(args[1], Palette.All[0], 16, 24, 32, 48, 64, 128, 256);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--export-sheet")
            {
                ExportSheet(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--export-png")
            {
                using (var bmp = Dino.Snapshot(Palette.All[0], 512)) bmp.Save(args[1], ImageFormat.Png);
                return 0;
            }

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
                Application.Run(new SailbackApp());
            }
            return 0;
        }

        /// <summary>Dev helper: renders every pose into one image for eyeballing the art.</summary>
        private static void ExportSheet(string path)
        {
            var poses = new (string, Pose)[]
            {
                ("walk A", new Pose { Stride = 13, Lift = 7, Phase = 0, Shadow = 1 }),
                ("walk B", new Pose { Stride = 13, Lift = 7, Phase = Math.PI * 1.5, Shadow = 1 }),
                ("run", new Pose { Stride = 20, Lift = 13, Phase = Math.PI * 1.5, HeadDrop = 0.12f, Bob = -4, Shadow = 1 }),
                ("sniff", new Pose { HeadDrop = 1, Shadow = 1 }),
                ("roar", new Pose { Jaw = 1, HeadDrop = -0.38f, TailSway = 4, Shadow = 1 }),
                ("sleep", new Pose { Crouch = 1, HeadDrop = 0.55f, EyesClosed = true, Snoring = true, Time = 0.4, Shadow = 1 }),
                ("dangle", new Pose { Dangle = true, Jaw = 0.4f, HeadDrop = -0.15f, TailSway = 6, Time = 0.3 }),
                ("facing left", new Pose { Stride = 13, Lift = 7, Phase = 1, FacingLeft = true, Shadow = 1 }),
            };
            const float scale = 1.5f;
            int cw = (int)(DinoArt.W * scale), ch = (int)(DinoArt.H * scale) + 18;
            using (var sheet = new System.Drawing.Bitmap(cw * 4, ch * 2))
            using (var g = System.Drawing.Graphics.FromImage(sheet))
            using (var font = new System.Drawing.Font("Segoe UI", 10))
            {
                g.Clear(System.Drawing.Color.FromArgb(235, 238, 242));
                for (int i = 0; i < poses.Length; i++)
                {
                    int x = (i % 4) * cw, y = (i / 4) * ch;
                    using (var cell = new System.Drawing.Bitmap(cw, ch - 18, PixelFormat.Format32bppPArgb))
                    {
                        using (var cg = System.Drawing.Graphics.FromImage(cell))
                            DinoArt.Draw(cg, Palette.All[i % Palette.All.Length], poses[i].Item2, scale);
                        g.DrawImage(cell, x, y + 18);
                    }
                    g.DrawString(poses[i].Item1, font, System.Drawing.Brushes.Black, x + 6, y + 2);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }

    /// <summary>Writes a multi-resolution .ico (PNG-compressed entries) from the vector art.</summary>
    internal static class IconWriter
    {
        public static void Write(string path, Palette pal, params int[] sizes)
        {
            var images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bmp = Dino.Snapshot(pal, sizes[i]))
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
