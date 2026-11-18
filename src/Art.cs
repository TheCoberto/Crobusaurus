using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Sailback
{
    /// <summary>A colour scheme. "Accent" is the sail / feathers / back markings, depending on species.</summary>
    public sealed class Palette
    {
        public string Name;
        public Color Body, BodyDark, Belly, Accent, AccentDark, Outline, Eye;

        public static readonly Palette[] All =
        {
            new Palette { Name = "Fern",     Body = C(0x5E8C4A), BodyDark = C(0x46703A), Belly = C(0xDCD3A2), Accent = C(0xD0683A), AccentDark = C(0x9E4426), Outline = C(0x24301E), Eye = C(0xF2C230) },
            new Palette { Name = "Slate",    Body = C(0x5B7A96), BodyDark = C(0x445E77), Belly = C(0xD8DCCB), Accent = C(0xE0A43A), AccentDark = C(0xA8721C), Outline = C(0x1F2A36), Eye = C(0xF5E663) },
            new Palette { Name = "Dune",     Body = C(0xB58A55), BodyDark = C(0x8E6A3E), Belly = C(0xEADFC0), Accent = C(0x7A4C9A), AccentDark = C(0x553270), Outline = C(0x3A2814), Eye = C(0x9BE15D) },
            new Palette { Name = "Ember",    Body = C(0x9A4A3C), BodyDark = C(0x763629), Belly = C(0xE8C9A0), Accent = C(0x3E8C88), AccentDark = C(0x276461), Outline = C(0x341712), Eye = C(0xFFD34D) },
            new Palette { Name = "Midnight", Body = C(0x3C4466), BodyDark = C(0x2B3150), Belly = C(0xB9C0D8), Accent = C(0x4FC3C9), AccentDark = C(0x2C8E93), Outline = C(0x14172A), Eye = C(0xFF8A3D) },
            new Palette { Name = "Moss",     Body = C(0x7C8C3A), BodyDark = C(0x5F6D2A), Belly = C(0xE4E0B4), Accent = C(0xC9483E), AccentDark = C(0x8F2B24), Outline = C(0x2A2F12), Eye = C(0xFFE066) },
        };

        public static Palette Find(string name) => All.FirstOrDefault(p => p.Name == name) ?? All[0];

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
    /// A kind of dinosaur: its look (vector-drawn, facing right, in "art units") and its gait.
    /// </summary>
    public abstract class Species
    {
        public static readonly Species[] All = { new SailbackSpecies(), new TyrantSpecies(), new RaptorSpecies(), new LongneckSpecies() };
        public static Species Find(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Blurb { get; }
        public abstract float W { get; }
        public abstract float H { get; }
        /// <summary>Y of the feet in art units.</summary>
        public abstract float Ground { get; }

        public virtual float WalkSpeed => 55;
        public virtual float RunSpeed => 190;
        public virtual float FleeSpeed => 240;
        public virtual float WalkStride => 13;
        public virtual float RunStride => 20;
        public virtual float WalkLift => 7;
        public virtual float RunLift => 13;
        public virtual double RoarPitch => 1.0;
        public virtual string DefaultPalette => "Fern";

        public void Draw(Graphics g, Palette p, Pose pose, float scale)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            var root = g.Save();
            g.ScaleTransform(scale, scale);
            if (pose.FacingLeft) { g.TranslateTransform(W, 0); g.ScaleTransform(-1, 1); }
            g.TranslateTransform(pose.Shake, 0);
            using (var outline = new Pen(p.Outline, 2.2f) { LineJoin = LineJoin.Round })
                DrawFigure(g, p, outline, pose);
            g.Restore(root);

            if (pose.Snoring)
            {
                var a = SnoreAnchor(pose);
                Art.DrawZs(g, p, a.X, a.Y, pose, scale, W);
            }
        }

        protected abstract void DrawFigure(Graphics g, Palette p, Pen outline, Pose pose);

        /// <summary>Where the "z z z" float up from (art units, facing right).</summary>
        protected abstract PointF SnoreAnchor(Pose pose);

        protected void DrawShadow(Graphics g, Pose pose, float cx, float width)
        {
            if (pose.Shadow <= 0.01f) return;
            using (var b = new SolidBrush(Color.FromArgb((int)(60 * pose.Shadow), 0, 0, 0)))
                g.FillEllipse(b, cx - width / 2, Ground - 3.5f, width, 7);
        }
    }

    /// <summary>Shared drawing helpers.</summary>
    public static class Art
    {
        public static void Fill(Graphics g, GraphicsPath path, Color c, Pen outline)
        {
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
            if (outline != null) g.DrawPath(outline, path);
        }

        public static GraphicsPath Closed(float tension, params PointF[] pts)
        {
            var path = new GraphicsPath();
            path.AddClosedCurve(pts, tension);
            return path;
        }

        public static void Limb(Graphics g, Color outline, Color fill, float width, float x1, float y1, float x2, float y2)
        {
            using (var o = new Pen(outline, width + 3.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(o, x1, y1, x2, y2);
            using (var f = new Pen(fill, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(f, x1, y1, x2, y2);
        }

        public static void Stroke(Graphics g, Color c, float width, float x1, float y1, float x2, float y2)
        {
            using (var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, x1, y1, x2, y2);
        }

        /// <summary>Where a foot should be this frame (walking, standing, crouching or kicking in the air).</summary>
        public static PointF FootTarget(Pose pose, float hx, float hy, double ph, float ground, float reach, float forward, double dangleOffset)
        {
            if (pose.Dangle)
                return new PointF(
                    hx + forward + (float)Math.Sin(pose.Time * 17 + dangleOffset) * 10,
                    hy + reach * 0.78f + (float)Math.Cos(pose.Time * 17 + dangleOffset) * 4);

            return new PointF(
                hx + forward + (float)(pose.Stride * Math.Cos(ph)) + pose.Crouch * 6,
                ground - 2 - (float)(Math.Max(0, -Math.Sin(ph)) * pose.Lift));
        }

        /// <summary>Two-bone IK leg. Returns where the foot actually ended up.</summary>
        public static PointF Leg(Graphics g, Palette p, Color fill, float hx, float hy, PointF target,
            float a, float b, bool kneeForward, float thighW, float shinW)
        {
            float dx = target.X - hx, dy = target.Y - hy;
            float d = Clamp((float)Math.Sqrt(dx * dx + dy * dy), 1, a + b - 0.5f);
            double th = Math.Atan2(dy, dx);
            double A = Math.Acos(Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1));
            double kneeAngle = kneeForward ? th - A : th + A;
            float kx = hx + (float)(a * Math.Cos(kneeAngle)), ky = hy + (float)(a * Math.Sin(kneeAngle));
            double sh = Math.Atan2(target.Y - ky, target.X - kx);
            float fx = kx + (float)(b * Math.Cos(sh)), fy = ky + (float)(b * Math.Sin(sh));

            Limb(g, p.Outline, fill, thighW, hx, hy, kx, ky);
            Limb(g, p.Outline, fill, shinW, kx, ky, fx, fy);
            return new PointF(fx, fy);
        }

        /// <summary>Theropod foot: toes pointing forward plus a claw.</summary>
        public static void BirdFoot(Graphics g, Palette p, Color fill, PointF f, float toe, float width)
        {
            Limb(g, p.Outline, fill, width, f.X - 2, f.Y, f.X + toe, f.Y + 0.5f);
            using (var claw = new SolidBrush(p.Outline))
                g.FillPolygon(claw, new[] { new PointF(f.X + toe, f.Y - 2), new PointF(f.X + toe + 5, f.Y + 1.5f), new PointF(f.X + toe, f.Y + 2.5f) });
        }

        public static void Teeth(Graphics g, float fromX, float toX, float baseY, float length, float step = 6)
        {
            using (var b = new SolidBrush(Color.FromArgb(255, 250, 246, 230)))
                for (float x = fromX; x <= toX; x += step)
                    g.FillPolygon(b, new[] { new PointF(x - 1.6f, baseY), new PointF(x + 1.6f, baseY), new PointF(x, baseY + length) });
        }

        public static void Eye(Graphics g, Palette p, float x, float y, float size, bool closed)
        {
            if (closed)
            {
                using (var lid = new Pen(p.Outline, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(lid, x, y + 1, size, size * 0.7f, 10, 160);
                return;
            }
            using (var eye = new SolidBrush(p.Eye)) g.FillEllipse(eye, x, y, size, size);
            using (var pupil = new SolidBrush(p.Outline)) g.FillEllipse(pupil, x + size * 0.44f, y + size * 0.07f, size * 0.29f, size * 0.86f);
            using (var glint = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillEllipse(glint, x + size * 0.21f, y + size * 0.14f, size * 0.26f, size * 0.26f);
            using (var ring = new Pen(p.Outline, 1.2f)) g.DrawEllipse(ring, x, y, size, size);
        }

        public static void MouthInterior(Graphics g, PointF[] upper, PointF pivot, float jawAngle, PointF[] lowerLine)
        {
            using (var m = new Matrix())
            {
                m.RotateAt(jawAngle, pivot);
                var lower = (PointF[])lowerLine.Clone();
                m.TransformPoints(lower);
                var poly = upper.Concat(lower.Reverse()).ToArray();
                using (var b = new SolidBrush(Color.FromArgb(255, 120, 30, 36))) g.FillPolygon(b, poly);
            }
        }

        /// <summary>A tapered tube (tail, neck) along a cubic Bezier spine.</summary>
        public static GraphicsPath Tube(PointF p0, PointF p1, PointF p2, PointF p3, float w0, float w1, int samples = 18)
        {
            var left = new PointF[samples + 1];
            var right = new PointF[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                var pt = Bez(p0, p1, p2, p3, t);
                var d = BezD(p0, p1, p2, p3, t);
                float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (len < 1e-3f) len = 1;
                float nx = -d.Y / len, ny = d.X / len;
                float w = Lerp(w0, w1, t) / 2;
                left[i] = new PointF(pt.X + nx * w, pt.Y + ny * w);
                right[i] = new PointF(pt.X - nx * w, pt.Y - ny * w);
            }
            var path = new GraphicsPath();
            path.AddLines(left);
            path.AddLines(right.Reverse().ToArray());
            path.CloseFigure();
            return path;
        }

        public static PointF Bez(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float u = 1 - t;
            return new PointF(
                u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
                u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
        }

        public static PointF BezD(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float u = 1 - t;
            return new PointF(
                3 * u * u * (b.X - a.X) + 6 * u * t * (c.X - b.X) + 3 * t * t * (d.X - c.X),
                3 * u * u * (b.Y - a.Y) + 6 * u * t * (c.Y - b.Y) + 3 * t * t * (d.Y - c.Y));
        }

        public static void DrawZs(Graphics g, Palette p, float ax, float ay, Pose pose, float scale, float canvasW)
        {
            for (int k = 0; k < 3; k++)
            {
                double cycle = (pose.Time * 0.55 + k / 3.0) % 1.0;
                float lx = ax + (float)cycle * 22 + (float)Math.Sin(cycle * 8) * 3;
                float ly = ay - (float)cycle * 50;
                if (pose.FacingLeft) lx = canvasW - lx;
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

        public static Color Light(Color c, float amount) => ControlPaint.Light(c, amount);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
