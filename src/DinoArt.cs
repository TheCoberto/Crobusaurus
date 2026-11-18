using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sailback
{
    /// <summary>A colour scheme for one dinosaur.</summary>
    public sealed class Palette
    {
        public string Name;
        public Color Body, BodyDark, Belly, Sail, SailStripe, Outline, Eye;

        public static readonly Palette[] All =
        {
            new Palette { Name = "Fern",   Body = C(0x5E8C4A), BodyDark = C(0x46703A), Belly = C(0xDCD3A2), Sail = C(0xD0683A), SailStripe = C(0x9E4426), Outline = C(0x24301E), Eye = C(0xF2C230) },
            new Palette { Name = "Slate",  Body = C(0x5B7A96), BodyDark = C(0x445E77), Belly = C(0xD8DCCB), Sail = C(0xE0A43A), SailStripe = C(0xA8721C), Outline = C(0x1F2A36), Eye = C(0xF5E663) },
            new Palette { Name = "Dune",   Body = C(0xB58A55), BodyDark = C(0x8E6A3E), Belly = C(0xEADFC0), Sail = C(0x7A4C9A), SailStripe = C(0x553270), Outline = C(0x3A2814), Eye = C(0x9BE15D) },
            new Palette { Name = "Ember",  Body = C(0x9A4A3C), BodyDark = C(0x763629), Belly = C(0xE8C9A0), Sail = C(0x3E8C88), SailStripe = C(0x276461), Outline = C(0x341712), Eye = C(0xFFD34D) },
        };

        private static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    /// <summary>Everything that varies between animation frames.</summary>
    public struct Pose
    {
        public double Phase;      // gait cycle, radians
        public double Time;       // seconds since spawn (for idle wiggles)
        public float Stride;      // foot travel in art units
        public float Lift;        // foot lift during swing
        public float Bob;         // vertical body offset
        public float HeadDrop;    // 0 = level, 1 = nose to the ground, negative = head raised
        public float Jaw;         // 0 = closed, 1 = wide open
        public float TailSway;
        public float Crouch;      // 0 = standing, 1 = lying down
        public float Shake;       // horizontal jitter
        public float Shadow;      // 0..1 ground-shadow opacity
        public bool EyesClosed;
        public bool Dangle;       // legs kicking in the air
        public bool FacingLeft;
        public bool Snoring;
    }

    /// <summary>
    /// Procedural renderer for the dinosaur. Everything is vector-drawn in a 200x140 "art unit"
    /// space and scaled to the monitor DPI, so it stays sharp at any size.
    /// </summary>
    public static class DinoArt
    {
        public const float W = 200, H = 140, Ground = 134;

        public static void Draw(Graphics g, Palette p, Pose pose, float scale)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            var root = g.Save();
            g.ScaleTransform(scale, scale);
            if (pose.FacingLeft) { g.TranslateTransform(W, 0); g.ScaleTransform(-1, 1); }
            g.TranslateTransform(pose.Shake, 0);

            float hx = 76, hy = Ground - 46 + pose.Bob + pose.Crouch * 17;

            if (pose.Shadow > 0.01f)
                using (var b = new SolidBrush(Color.FromArgb((int)(60 * pose.Shadow), 0, 0, 0)))
                    g.FillEllipse(b, hx - 40, Ground - 3.5f, 108, 7);

            using (var outline = new Pen(p.Outline, 2.2f) { LineJoin = LineJoin.Round })
            {
                DrawLeg(g, p, hx + 3, hy, pose, pose.Phase + Math.PI, true);
                DrawTail(g, p, outline, hx, hy, pose);
                DrawSail(g, p, outline, hx, hy, pose);
                DrawArm(g, p, hx - 2, hy, pose, true);
                DrawBody(g, p, outline, hx, hy);
                DrawNeckAndHead(g, p, outline, hx, hy, pose);
                DrawLeg(g, p, hx, hy, pose, pose.Phase, false);
                DrawArm(g, p, hx, hy, pose, false);
            }

            g.Restore(root);

            if (pose.Snoring) DrawZs(g, p, hx, hy, pose, scale);
        }

        // ---------------------------------------------------------------- parts

        private static void DrawTail(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float s = pose.TailSway;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(hx - 8, hy - 32, hx - 40, hy - 36, 34, hy - 38 + s, 6, hy - 37 + s * 1.5f);
                path.AddLine(6, hy - 37 + s * 1.5f, 8, hy - 33 + s * 1.5f);
                path.AddBezier(8, hy - 33 + s * 1.5f, 36, hy - 26 + s, hx - 42, hy - 6, hx - 4, hy + 2);
                path.CloseFigure();
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                g.DrawPath(outline, path);
            }
            // a few darker bands along the tail
            using (var band = new Pen(p.BodyDark, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < 4; i++)
                {
                    float t = 0.25f + i * 0.17f;
                    float x = Lerp(hx - 12, 14, t);
                    float yTop = Lerp(hy - 33, hy - 37 + s * 1.5f, t) + 1.5f;
                    g.DrawLine(band, x, yTop, x + 2, yTop + Lerp(9, 2.5f, t));
                }
            }
        }

        private static void DrawSail(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float x0 = hx - 26, x1 = hx + 42;
            const int n = 40;
            var top = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float x = Lerp(x0, x1, t);
                float baseY = Lerp(hy - 26, hy - 30, t);
                float h = 40f * (float)Math.Pow(Math.Sin(Math.PI * t), 0.85) * (1.05f - 0.25f * t);
                h *= 1f - 0.06f * (float)Math.Abs(Math.Sin(t * Math.PI * 8)); // scalloped spine tips
                h *= 1f - pose.Crouch * 0.08f;
                top[i] = new PointF(x, baseY - h);
            }

            using (var path = new GraphicsPath())
            {
                path.AddLine(x0, hy - 18, x0, hy - 26);
                path.AddCurve(top, 0.3f);
                path.AddLine(x1, hy - 30, x1, hy - 18);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new PointF(0, hy - 72), new PointF(0, hy - 20),
                           ControlPaint.Light(p.Sail, 0.25f), p.Sail))
                    g.FillPath(b, path);

                // spines
                using (var spine = new Pen(p.SailStripe, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 3; i < n; i += 5)
                        g.DrawLine(spine, top[i].X, top[i].Y + 2, top[i].X + 1, hy - 22);

                g.DrawPath(outline, path);
            }
        }

        private static void DrawBody(Graphics g, Palette p, Pen outline, float hx, float hy)
        {
            var st = g.Save();
            g.TranslateTransform(hx + 12, hy - 16);
            g.RotateTransform(-9);
            var bodyRect = new RectangleF(-44, -21, 88, 42);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bodyRect);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);

                // belly, clipped to the body
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var belly = new SolidBrush(p.Belly)) g.FillEllipse(belly, -34, 4, 74, 30);
                using (var spots = new SolidBrush(Color.FromArgb(90, p.BodyDark)))
                {
                    g.FillEllipse(spots, -26, -10, 9, 6);
                    g.FillEllipse(spots, -10, -14, 7, 5);
                    g.FillEllipse(spots, 6, -9, 8, 5);
                    g.FillEllipse(spots, -18, -1, 6, 4);
                }
                g.Clip = clip;
                g.DrawPath(outline, path);
            }
            g.Restore(st);
        }

        private static void DrawNeckAndHead(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float drop = pose.HeadDrop;
            float bx = hx + 60 + drop * 4, by = hy - 44 + drop * 38;

            // neck
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new[]
                {
                    new PointF(hx + 30, hy - 30),
                    new PointF(bx - 2, by - 9),
                    new PointF(bx + 4, by + 7),
                    new PointF(hx + 48, hy - 4),
                    new PointF(hx + 38, hy - 14),
                }, 0.35f);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                g.DrawPath(outline, path);
            }

            var st = g.Save();
            g.TranslateTransform(bx, by);
            g.RotateTransform(drop * 26);

            float jawAngle = pose.Jaw * 34;

            // mouth interior (visible when open)
            if (pose.Jaw > 0.05f)
            {
                PointF tip, back;
                using (var jt = JawTransform(jawAngle))
                {
                    tip = Apply(jt, new PointF(50, 4));
                    back = Apply(jt, new PointF(4, 6));
                }
                using (var m = new SolidBrush(Color.FromArgb(255, 120, 30, 36)))
                    g.FillPolygon(m, new[] { new PointF(0, 4), new PointF(54, 2), tip, back });
            }

            // lower jaw
            var js = g.Save();
            g.TranslateTransform(0, 4);
            g.RotateTransform(jawAngle);
            g.TranslateTransform(0, -4);
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new[]
                {
                    new PointF(-6, 4), new PointF(22, 4), new PointF(50, 3), new PointF(55, 6),
                    new PointF(40, 10), new PointF(14, 12), new PointF(-4, 11),
                }, 0.3f);
                using (var b = new SolidBrush(p.Belly)) g.FillPath(b, path);
                if (pose.Jaw > 0.05f) DrawTeeth(g, 14, 50, 4.2f, -3.2f);
                g.DrawPath(outline, path);
            }
            g.Restore(js);

            // skull with long snout
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new[]
                {
                    new PointF(-8, -9), new PointF(6, -15), new PointF(20, -13), new PointF(38, -7),
                    new PointF(54, -4), new PointF(58, 0), new PointF(54, 3.5f), new PointF(30, 4),
                    new PointF(6, 5.5f), new PointF(-8, 8),
                }, 0.3f);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                if (pose.Jaw > 0.05f) DrawTeeth(g, 14, 52, 3.6f, 3.4f);
                g.DrawPath(outline, path);
            }

            // brow ridge
            using (var brow = new Pen(p.BodyDark, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(brow, 6, -16, 14, 8, 200, 120);

            // eye
            if (pose.EyesClosed)
            {
                using (var lid = new Pen(p.Outline, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(lid, 9.5f, -9, 7, 5, 10, 160);
            }
            else
            {
                using (var eye = new SolidBrush(p.Eye)) g.FillEllipse(eye, 9.5f, -10, 7, 7);
                using (var pupil = new SolidBrush(p.Outline)) g.FillEllipse(pupil, 12.6f, -9.5f, 2, 6);
                using (var glint = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillEllipse(glint, 11, -9, 1.8f, 1.8f);
                using (var ring = new Pen(p.Outline, 1.2f)) g.DrawEllipse(ring, 9.5f, -10, 7, 7);
            }

            // nostril
            using (var n = new SolidBrush(p.Outline)) g.FillEllipse(n, 49, -3.6f, 3.2f, 2);

            g.Restore(st);
        }

        private static void DrawTeeth(Graphics g, float fromX, float toX, float baseY, float length)
        {
            using (var b = new SolidBrush(Color.FromArgb(255, 250, 246, 230)))
                for (float x = fromX; x <= toX; x += 6)
                    g.FillPolygon(b, new[] { new PointF(x - 1.6f, baseY), new PointF(x + 1.6f, baseY), new PointF(x, baseY + length) });
        }

        private static void DrawLeg(Graphics g, Palette p, float hx, float hy, Pose pose, double ph, bool far)
        {
            const float a = 26, b = 26; // thigh, shin
            float fx, fy;
            if (pose.Dangle)
            {
                fx = hx + 8 + (float)Math.Sin(pose.Time * 17 + (far ? 1.9 : 0)) * 10;
                fy = hy + 38 + (float)Math.Cos(pose.Time * 17 + (far ? 1.9 : 0)) * 4;
            }
            else
            {
                fx = hx + 6 + (float)(pose.Stride * Math.Cos(ph));
                fy = Ground - 2 - (float)(Math.Max(0, -Math.Sin(ph)) * pose.Lift);
                if (pose.Crouch > 0) fx += pose.Crouch * 6;
            }

            // two-bone IK with the knee bending forward
            float dx = fx - hx, dy = fy - hy;
            float d = Clamp((float)Math.Sqrt(dx * dx + dy * dy), 1, a + b - 0.5f);
            double th = Math.Atan2(dy, dx);
            double A = Math.Acos(Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1));
            float kx = hx + (float)(a * Math.Cos(th - A)), ky = hy + (float)(a * Math.Sin(th - A));
            double sh = Math.Atan2(fy - ky, fx - kx);
            fx = kx + (float)(b * Math.Cos(sh)); fy = ky + (float)(b * Math.Sin(sh));

            Color c = far ? p.BodyDark : p.Body;
            Limb(g, p.Outline, c, 15, hx, hy, kx, ky);
            Limb(g, p.Outline, c, 8, kx, ky, fx, fy);
            Limb(g, p.Outline, c, 5.5f, fx - 2, fy, fx + 11, fy + 0.5f);
            using (var claw = new SolidBrush(p.Outline))
                g.FillPolygon(claw, new[] { new PointF(fx + 11, fy - 2), new PointF(fx + 16, fy + 1.5f), new PointF(fx + 11, fy + 2.5f) });
        }

        private static void DrawArm(Graphics g, Palette p, float hx, float hy, Pose pose, bool far)
        {
            float wave = pose.Dangle ? (float)Math.Sin(pose.Time * 14 + (far ? 2 : 0)) * 5 : (float)Math.Sin(pose.Phase) * 1.5f;
            float sx = hx + 44, sy = hy - 12 + pose.Crouch * 2;
            float ex = sx + 4, ey = sy + 12 + wave * 0.4f;
            float wx = ex + 8, wy = ey - 1 + wave;
            Color c = far ? p.BodyDark : p.Body;
            Limb(g, p.Outline, c, 6.5f, sx, sy, ex, ey);
            Limb(g, p.Outline, c, 5f, ex, ey, wx, wy);
            using (var claw = new Pen(p.Outline, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(claw, wx + 1, wy, wx + 4, wy + 2.5f);
                g.DrawLine(claw, wx, wy + 1.5f, wx + 2, wy + 4.5f);
            }
        }

        private static void DrawZs(Graphics g, Palette p, float hx, float hy, Pose pose, float scale)
        {
            for (int k = 0; k < 3; k++)
            {
                double cycle = (pose.Time * 0.55 + k / 3.0) % 1.0;
                float lx = hx + 64 + (float)cycle * 22 + (float)Math.Sin(cycle * 8) * 3;
                float ly = hy - 40 - (float)cycle * 50;
                if (pose.FacingLeft) lx = W - lx;
                int alpha = (int)(255 * Math.Sin(cycle * Math.PI));
                float size = 9 + (float)cycle * 9;
                using (var path = new GraphicsPath())
                using (var family = new FontFamily("Segoe UI"))
                {
                    path.AddString("z", family, (int)FontStyle.Bold, size * scale,
                        new PointF(lx * scale, ly * scale), StringFormat.GenericDefault);
                    using (var pen = new Pen(Color.FromArgb(alpha, p.Outline), 2.5f * scale) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, path);
                    using (var b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                        g.FillPath(b, path);
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void Limb(Graphics g, Color outline, Color fill, float width, float x1, float y1, float x2, float y2)
        {
            using (var o = new Pen(outline, width + 3.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(o, x1, y1, x2, y2);
            using (var f = new Pen(fill, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(f, x1, y1, x2, y2);
        }

        private static Matrix JawTransform(float angle)
        {
            var m = new Matrix();
            m.Translate(0, 4);
            m.Rotate(angle);
            m.Translate(0, -4);
            return m;
        }

        private static PointF Apply(Matrix m, PointF pt)
        {
            var pts = new[] { pt };
            m.TransformPoints(pts);
            return pts[0];
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
