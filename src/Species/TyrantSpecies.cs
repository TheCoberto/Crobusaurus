using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sailback
{
    /// <summary>A heavy, big-headed theropod with tiny arms and a deep voice.</summary>
    public sealed class TyrantSpecies : Species
    {
        public override string Id => "tyrant";
        public override string Name => "Bigjaw";
        public override string Blurb => "Huge head, tiny arms, enormous roar.";
        public override float W => 214;
        public override float H => 152;
        public override float Ground => 146;
        public override float WalkSpeed => 45;
        public override float RunSpeed => 150;
        public override float FleeSpeed => 185;
        public override float WalkStride => 15;
        public override float RunStride => 21;
        public override double RoarPitch => 0.72;
        public override string DefaultPalette => "Ember";

        protected override PointF SnoreAnchor(Pose pose) => new PointF(84 + 66, Ground - 54 + pose.Crouch * 20 - 50);

        protected override void DrawFigure(Graphics g, Palette p, Pen outline, Pose pose)
        {
            float hx = 84, hy = Ground - 54 + pose.Bob * 1.2f + pose.Crouch * 20;
            DrawShadow(g, pose, hx + 12, 120);

            DrawLeg(g, p, hx + 4, hy, pose, pose.Phase + Math.PI, true);

            // tail
            float s = pose.TailSway;
            using (var tail = Art.Tube(new PointF(hx - 14, hy - 22), new PointF(hx - 50, hy - 30),
                       new PointF(40, hy - 42 + s), new PointF(4, hy - 44 + s * 1.6f), 34, 3))
                Art.Fill(g, tail, p.Body, outline);
            using (var band = new Pen(p.AccentDark, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 4; i++)
                {
                    float t = 0.22f + i * 0.16f;
                    var pt = Art.Bez(new PointF(hx - 14, hy - 22), new PointF(hx - 50, hy - 30), new PointF(40, hy - 42 + s), new PointF(4, hy - 44 + s * 1.6f), t);
                    float w = Art.Lerp(34, 3, t) / 2;
                    g.DrawLine(band, pt.X, pt.Y - w + 1.5f, pt.X + 2, pt.Y - w * 0.2f);
                }

            DrawArm(g, p, hx - 3, hy, pose, true);

            // body
            var st = g.Save();
            g.TranslateTransform(hx + 10, hy - 20);
            g.RotateTransform(-14);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-48, -27, 96, 54);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var belly = new SolidBrush(p.Belly)) g.FillEllipse(belly, -38, 8, 84, 34);
                using (var stripe = new Pen(p.AccentDark, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 6; i++)
                    {
                        float x = -36 + i * 13;
                        g.DrawLine(stripe, x, -30, x + 3, -16 + (i % 2) * 3);
                    }
                g.Clip = clip;
                g.DrawPath(outline, path);
            }
            g.Restore(st);

            DrawHead(g, p, outline, hx, hy, pose);
            DrawLeg(g, p, hx, hy, pose, pose.Phase, false);
            DrawArm(g, p, hx, hy, pose, false);
        }

        private void DrawHead(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float drop = pose.HeadDrop;
            float bx = hx + 52 + drop * 6, by = hy - 50 + drop * 42;

            using (var neck = Art.Closed(0.35f,
                new PointF(hx + 26, hy - 42), new PointF(bx - 6, by - 14), new PointF(bx + 8, by + 13),
                new PointF(hx + 46, hy - 8), new PointF(hx + 36, hy - 22)))
                Art.Fill(g, neck, p.Body, outline);

            var st = g.Save();
            g.TranslateTransform(bx, by);
            g.RotateTransform(drop * 24);
            float jaw = pose.Jaw * 30;

            if (pose.Jaw > 0.05f)
                Art.MouthInterior(g, new[] { new PointF(-4, 6), new PointF(56, 5) }, new PointF(-4, 6), jaw,
                    new[] { new PointF(-2, 9), new PointF(52, 8) });

            var js = g.Save();
            g.TranslateTransform(-4, 6); g.RotateTransform(jaw); g.TranslateTransform(4, -6);
            using (var path = Art.Closed(0.3f,
                new PointF(-10, 6), new PointF(20, 7), new PointF(52, 6), new PointF(57, 9),
                new PointF(44, 16), new PointF(16, 19), new PointF(-6, 16)))
            {
                using (var b = new SolidBrush(p.Belly)) g.FillPath(b, path);
                if (pose.Jaw > 0.05f) Art.Teeth(g, 10, 52, 7.5f, -4f);
                g.DrawPath(outline, path);
            }
            g.Restore(js);

            using (var path = Art.Closed(0.28f,
                new PointF(-12, -12), new PointF(2, -21), new PointF(24, -20), new PointF(44, -14),
                new PointF(57, -8), new PointF(61, -1), new PointF(57, 6), new PointF(30, 7.5f),
                new PointF(4, 9), new PointF(-12, 10)))
            {
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                if (pose.Jaw > 0.05f) Art.Teeth(g, 8, 54, 6.5f, 4.6f);
                g.DrawPath(outline, path);
            }

            // brow horn, cheek line, eye, nostril
            using (var horn = new SolidBrush(p.BodyDark))
                g.FillPolygon(horn, new[] { new PointF(6, -18), new PointF(14, -26), new PointF(19, -18) });
            Art.Stroke(g, p.BodyDark, 2f, -6, 2, 10, 4);
            Art.Eye(g, p, 9, -14, 7.5f, pose.EyesClosed);
            using (var brow = new Pen(p.Outline, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(brow, 7, -15.5f, 18, -13);
            using (var n = new SolidBrush(p.Outline)) g.FillEllipse(n, 52, -7, 3.6f, 2.4f);
            g.Restore(st);
        }

        private void DrawLeg(Graphics g, Palette p, float hx, float hy, Pose pose, double ph, bool far)
        {
            var target = Art.FootTarget(pose, hx, hy, ph, Ground, 54, 6, far ? 1.9 : 0);
            Color c = far ? p.BodyDark : p.Body;
            var foot = Art.Leg(g, p, c, hx, hy, target, 30, 29, true, 19, 10);
            Art.BirdFoot(g, p, c, foot, 13, 7f);
        }

        private void DrawArm(Graphics g, Palette p, float hx, float hy, Pose pose, bool far)
        {
            float wave = pose.Dangle ? (float)Math.Sin(pose.Time * 14 + (far ? 2 : 0)) * 3 : (float)Math.Sin(pose.Phase) * 1f;
            float sx = hx + 42, sy = hy - 14 + pose.Crouch * 2;
            float ex = sx + 5, ey = sy + 7;
            float wx = ex + 5, wy = ey - 1 + wave;
            Color c = far ? p.BodyDark : p.Body;
            Art.Limb(g, p.Outline, c, 5f, sx, sy, ex, ey);
            Art.Limb(g, p.Outline, c, 4f, ex, ey, wx, wy);
            Art.Stroke(g, p.Outline, 1.3f, wx + 1, wy, wx + 3, wy + 2);
        }
    }
}
