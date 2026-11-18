using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Crobusaurus
{
    /// <summary>A small, quick, feathered hunter with a sickle claw.</summary>
    public sealed class RaptorSpecies : Species
    {
        public override string Id => "raptor";
        public override string Name => "Swiftclaw";
        public override string Blurb => "Small, feathery and very fast. Hard to catch!";
        public override float W => 186;
        public override float H => 128;
        public override float Ground => 122;
        public override float WalkSpeed => 75;
        public override float RunSpeed => 260;
        public override float FleeSpeed => 320;
        public override float WalkStride => 12;
        public override float RunStride => 19;
        public override float RunLift => 14;
        public override double RoarPitch => 1.75;
        public override string DefaultPalette => "Slate";

        protected override PointF SnoreAnchor(Pose pose) => new PointF(78 + 54, Ground - 44 + pose.Crouch * 16 - 36);

        protected override void DrawFigure(Graphics g, Palette p, Pen outline, Pose pose)
        {
            float hx = 78, hy = Ground - 44 + pose.Bob * 0.9f + pose.Crouch * 16;
            DrawShadow(g, pose, hx + 6, 90);

            DrawLeg(g, p, hx + 3, hy, pose, pose.Phase + Math.PI, true);

            // long, stiff tail with a feather fan at the tip
            float s = pose.TailSway * 0.6f;
            var t0 = new PointF(hx - 12, hy - 20);
            var t1 = new PointF(hx - 36, hy - 24);
            var t2 = new PointF(36, hy - 28 + s);
            var t3 = new PointF(12, hy - 30 + s * 1.5f);
            DrawFeatherFan(g, p, t3.X + 2, t3.Y, s);
            using (var tail = Art.Tube(t0, t1, t2, t3, 17, 4))
                Art.Fill(g, tail, p.Body, outline);
            using (var band = new Pen(p.AccentDark, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 5; i++)
                {
                    float t = 0.18f + i * 0.15f;
                    var pt = Art.Bez(t0, t1, t2, t3, t);
                    float w = Art.Lerp(17, 4, t) / 2;
                    g.DrawLine(band, pt.X, pt.Y - w + 1.2f, pt.X + 1, pt.Y + w * 0.1f);
                }

            DrawArm(g, p, hx - 3, hy, pose, true);

            // body
            var st = g.Save();
            g.TranslateTransform(hx + 10, hy - 16);
            g.RotateTransform(-8);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-31, -15, 62, 30);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var belly = new SolidBrush(p.Belly)) g.FillEllipse(belly, -24, 3, 52, 20);
                using (var stripe = new Pen(p.AccentDark, 2.6f) { StartCap = LineCap.Round })
                    for (int i = 0; i < 5; i++)
                        g.DrawLine(stripe, -22 + i * 10, -16, -20 + i * 10, -7);
                g.Clip = clip;
                g.DrawPath(outline, path);
            }
            g.Restore(st);

            DrawNeckAndHead(g, p, outline, hx, hy, pose);
            DrawLeg(g, p, hx, hy, pose, pose.Phase, false);
            DrawArm(g, p, hx, hy, pose, false);
        }

        private static void DrawFeatherFan(Graphics g, Palette p, float x, float y, float sway)
        {
            for (int i = -2; i <= 2; i++)
            {
                var st = g.Save();
                g.TranslateTransform(x, y);
                g.RotateTransform(180 + i * 16 + sway * 2);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(0, -3, 18, 6);
                    using (var b = new SolidBrush(i % 2 == 0 ? p.Accent : p.AccentDark)) g.FillPath(b, path);
                    using (var o = new Pen(p.Outline, 1.4f)) g.DrawPath(o, path);
                }
                g.Restore(st);
            }
        }

        private void DrawNeckAndHead(Graphics g, Palette p, Pen outline, float hx, float hy, Pose pose)
        {
            float drop = pose.HeadDrop;
            var b = new PointF(hx + 56 + drop * 4, hy - 42 + drop * 32);

            using (var neck = Art.Tube(new PointF(hx + 30, hy - 20), new PointF(hx + 44, hy - 24),
                       new PointF(b.X - 10, b.Y + 8), new PointF(b.X + 2, b.Y + 1), 15, 10))
                Art.Fill(g, neck, p.Body, outline);

            var st = g.Save();
            g.TranslateTransform(b.X, b.Y);
            g.RotateTransform(drop * 24);
            float jaw = pose.Jaw * 32;

            // head crest feathers
            using (var crest = new Pen(p.Accent, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var crestO = new Pen(p.Outline, 5.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float wob = (float)Math.Sin(pose.Time * 6) * 1.2f;
                var tips = new[] { new PointF(-16, -16 + wob), new PointF(-12, -20 + wob), new PointF(-6, -19 + wob) };
                foreach (var t in tips) g.DrawLine(crestO, 0, -7, t.X, t.Y);
                foreach (var t in tips) g.DrawLine(crest, 0, -7, t.X, t.Y);
            }

            if (pose.Jaw > 0.05f)
                Art.MouthInterior(g, new[] { new PointF(0, 3), new PointF(34, 2) }, new PointF(0, 3), jaw,
                    new[] { new PointF(2, 5), new PointF(30, 4) });

            var js = g.Save();
            g.TranslateTransform(0, 3); g.RotateTransform(jaw); g.TranslateTransform(0, -3);
            using (var path = Art.Closed(0.3f, new PointF(-4, 3), new PointF(18, 3), new PointF(32, 3),
                       new PointF(34, 5), new PointF(20, 8), new PointF(-2, 8)))
            {
                using (var br = new SolidBrush(p.Belly)) g.FillPath(br, path);
                if (pose.Jaw > 0.05f) Art.Teeth(g, 10, 30, 3.4f, -2.4f, 4.5f);
                g.DrawPath(outline, path);
            }
            g.Restore(js);

            using (var path = Art.Closed(0.3f, new PointF(-6, -7), new PointF(6, -11), new PointF(20, -8.5f),
                       new PointF(33, -3.5f), new PointF(37, 0), new PointF(32, 3), new PointF(8, 4), new PointF(-6, 6)))
            {
                using (var br = new SolidBrush(p.Body)) g.FillPath(br, path);
                if (pose.Jaw > 0.05f) Art.Teeth(g, 9, 33, 2.8f, 2.6f, 4.5f);
                g.DrawPath(outline, path);
            }
            Art.Eye(g, p, 5, -8, 6.5f, pose.EyesClosed);
            using (var n = new SolidBrush(p.Outline)) g.FillEllipse(n, 30, -2.6f, 2.6f, 1.8f);
            g.Restore(st);
        }

        private void DrawLeg(Graphics g, Palette p, float hx, float hy, Pose pose, double ph, bool far)
        {
            var target = Art.FootTarget(pose, hx, hy, ph, Ground, 44, 5, far ? 1.9 : 0);
            Color c = far ? p.BodyDark : p.Body;
            var f = Art.Leg(g, p, c, hx, hy, target, 24, 22, true, 12, 6);
            Art.BirdFoot(g, p, c, f, 10, 4.5f);
            // raised sickle claw
            using (var path = new GraphicsPath())
            {
                path.AddBezier(f.X + 2, f.Y - 1, f.X + 3, f.Y - 8, f.X + 9, f.Y - 9, f.X + 10, f.Y - 5);
                path.AddBezier(f.X + 10, f.Y - 5, f.X + 7, f.Y - 6, f.X + 5, f.Y - 4, f.X + 4, f.Y);
                path.CloseFigure();
                using (var b = new SolidBrush(p.Outline)) g.FillPath(b, path);
            }
        }

        private void DrawArm(Graphics g, Palette p, float hx, float hy, Pose pose, bool far)
        {
            float wave = pose.Dangle ? (float)Math.Sin(pose.Time * 14 + (far ? 2 : 0)) * 6 : (float)Math.Sin(pose.Phase) * 1.5f;
            float sx = hx + 34, sy = hy - 16 + pose.Crouch * 2;
            float ex = sx + 6, ey = sy + 10 + wave * 0.4f;
            float wx = ex + 10, wy = ey - 2 + wave;
            Color c = far ? p.BodyDark : p.Body;

            // wing feathers hanging off the forearm
            Color fc = far ? p.AccentDark : p.Accent;
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f;
                float ax = Art.Lerp(ex, wx, t), ay = Art.Lerp(ey, wy, t);
                Art.Limb(g, p.Outline, fc, 3.2f, ax, ay, ax - 6 + i, ay + 9 - i);
            }
            Art.Limb(g, p.Outline, c, 5.5f, sx, sy, ex, ey);
            Art.Limb(g, p.Outline, c, 4.5f, ex, ey, wx, wy);
            Art.Stroke(g, p.Outline, 1.4f, wx + 1, wy, wx + 4, wy + 2.5f);
            Art.Stroke(g, p.Outline, 1.4f, wx, wy + 1.5f, wx + 2, wy + 4.5f);
        }
    }
}
