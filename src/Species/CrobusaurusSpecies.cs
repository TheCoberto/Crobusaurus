using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Crobusaurus
{
    /// <summary>Crobusaurus, the mascot: a long-snouted, sail-backed fisher. (Originally called "Sailback".)</summary>
    public sealed class CrobusaurusSpecies : Species
    {
        public override string Id => "crobusaurus";
        public override string Name => "Crobusaurus";
        public override string Blurb => "Tall sail, long snout. Curious and easily startled.";
        public override float W => 200;
        public override float H => 140;
        public override float Ground => 134;

        protected override PointF SnoreAnchor(Pose pose) => new PointF(76 + 64, Ground - 46 + pose.Crouch * 17 - 40);

        protected override void DrawFigure(Graphics g, Palette p, Pen outline, Pose pose)
        {
            float hx = 76, hy = Ground - 46 + pose.Bob + pose.Crouch * 17;
            DrawShadow(g, pose, hx + 14, 108);

            DrawLeg(g, p, hx + 3, hy, pose, pose.Phase + Math.PI, true);
            DrawTail(g, p, outline, hx, hy, pose);
            DrawSail(g, p, outline, hx, hy, pose);
            DrawArm(g, p, hx - 2, hy, pose, true);
            DrawBody(g, p, outline, hx, hy);
            DrawNeckAndHead(g, p, outline, hx, hy, pose);
            DrawLeg(g, p, hx, hy, pose, pose.Phase, false);
            DrawArm(g, p, hx, hy, pose, false);
        }

        private void DrawTail(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float s = pose.TailSway;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(hx - 8, hy - 32, hx - 40, hy - 36, 34, hy - 38 + s, 6, hy - 37 + s * 1.5f);
                path.AddLine(6, hy - 37 + s * 1.5f, 8, hy - 33 + s * 1.5f);
                path.AddBezier(8, hy - 33 + s * 1.5f, 36, hy - 26 + s, hx - 42, hy - 6, hx - 4, hy + 2);
                path.CloseFigure();
                Art.Fill(g, path, p.Body, outline);
            }
            using (var band = new Pen(p.BodyDark, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 4; i++)
                {
                    float t = 0.25f + i * 0.17f;
                    float x = Art.Lerp(hx - 12, 14, t);
                    float yTop = Art.Lerp(hy - 33, hy - 37 + s * 1.5f, t) + 1.5f;
                    g.DrawLine(band, x, yTop, x + 2, yTop + Art.Lerp(9, 2.5f, t));
                }
        }

        private void DrawSail(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float x0 = hx - 26, x1 = hx + 42;
            const int n = 40;
            var top = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float x = Art.Lerp(x0, x1, t);
                float baseY = Art.Lerp(hy - 26, hy - 30, t);
                float h = 40f * (float)Math.Pow(Math.Sin(Math.PI * t), 0.85) * (1.05f - 0.25f * t);
                h *= 1f - 0.06f * (float)Math.Abs(Math.Sin(t * Math.PI * 8));
                h *= 1f - pose.Crouch * 0.08f;
                top[i] = new PointF(x, baseY - h);
            }

            using (var path = new GraphicsPath())
            {
                path.AddLine(x0, hy - 18, x0, hy - 26);
                path.AddCurve(top, 0.3f);
                path.AddLine(x1, hy - 30, x1, hy - 18);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new PointF(0, hy - 72), new PointF(0, hy - 20), Art.Light(p.Accent, 0.25f), p.Accent))
                    g.FillPath(b, path);
                using (var spine = new Pen(p.AccentDark, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 3; i < n; i += 5)
                        g.DrawLine(spine, top[i].X, top[i].Y + 2, top[i].X + 1, hy - 22);
                g.DrawPath(outline, path);
            }
        }

        private void DrawBody(Graphics g, Palette p, Pen outline, float hx, float hy)
        {
            var st = g.Save();
            g.TranslateTransform(hx + 12, hy - 16);
            g.RotateTransform(-9);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-44, -21, 88, 42);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
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

        private void DrawNeckAndHead(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float drop = pose.HeadDrop;
            float bx = hx + 60 + drop * 4, by = hy - 44 + drop * 38;

            using (var path = Art.Closed(0.35f,
                new PointF(hx + 30, hy - 30), new PointF(bx - 2, by - 9), new PointF(bx + 4, by + 7),
                new PointF(hx + 48, hy - 4), new PointF(hx + 38, hy - 14)))
                Art.Fill(g, path, p.Body, outline);

            var st = g.Save();
            g.TranslateTransform(bx, by);
            g.RotateTransform(drop * 26);
            float jaw = pose.Jaw * 34;

            if (pose.Jaw > 0.05f)
                Art.MouthInterior(g, new[] { new PointF(0, 4), new PointF(54, 2) }, new PointF(0, 4), jaw,
                    new[] { new PointF(4, 6), new PointF(50, 4) });

            var js = g.Save();
            g.TranslateTransform(0, 4); g.RotateTransform(jaw); g.TranslateTransform(0, -4);
            using (var path = Art.Closed(0.3f,
                new PointF(-6, 4), new PointF(22, 4), new PointF(50, 3), new PointF(55, 6),
                new PointF(40, 10), new PointF(14, 12), new PointF(-4, 11)))
            {
                using (var b = new SolidBrush(p.Belly)) g.FillPath(b, path);
                if (pose.Jaw > 0.05f) Art.Teeth(g, 14, 50, 4.2f, -3.2f);
                g.DrawPath(outline, path);
            }
            g.Restore(js);

            using (var path = Art.Closed(0.3f,
                new PointF(-8, -9), new PointF(6, -15), new PointF(20, -13), new PointF(38, -7),
                new PointF(54, -4), new PointF(58, 0), new PointF(54, 3.5f), new PointF(30, 4),
                new PointF(6, 5.5f), new PointF(-8, 8)))
            {
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                if (pose.Jaw > 0.05f) Art.Teeth(g, 14, 52, 3.6f, 3.4f);
                g.DrawPath(outline, path);
            }

            using (var brow = new Pen(p.BodyDark, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(brow, 6, -16, 14, 8, 200, 120);
            Art.Eye(g, p, 9.5f, -10, 7, pose.EyesClosed);
            using (var n = new SolidBrush(p.Outline)) g.FillEllipse(n, 49, -3.6f, 3.2f, 2);
            g.Restore(st);
        }

        private void DrawLeg(Graphics g, Palette p, float hx, float hy, Pose pose, double ph, bool far)
        {
            var target = Art.FootTarget(pose, hx, hy, ph, Ground, 46, 6, far ? 1.9 : 0);
            Color c = far ? p.BodyDark : p.Body;
            var foot = Art.Leg(g, p, c, hx, hy, target, 26, 26, true, 15, 8);
            Art.BirdFoot(g, p, c, foot, 11, 5.5f);
        }

        private void DrawArm(Graphics g, Palette p, float hx, float hy, Pose pose, bool far)
        {
            float wave = pose.Dangle ? (float)Math.Sin(pose.Time * 14 + (far ? 2 : 0)) * 5 : (float)Math.Sin(pose.Phase) * 1.5f;
            float sx = hx + 44, sy = hy - 12 + pose.Crouch * 2;
            float ex = sx + 4, ey = sy + 12 + wave * 0.4f;
            float wx = ex + 8, wy = ey - 1 + wave;
            Color c = far ? p.BodyDark : p.Body;
            Art.Limb(g, p.Outline, c, 6.5f, sx, sy, ex, ey);
            Art.Limb(g, p.Outline, c, 5f, ex, ey, wx, wy);
            Art.Stroke(g, p.Outline, 1.4f, wx + 1, wy, wx + 4, wy + 2.5f);
            Art.Stroke(g, p.Outline, 1.4f, wx, wy + 1.5f, wx + 2, wy + 4.5f);
        }
    }
}
