using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Crobusaurus
{
    /// <summary>A gentle four-legged giant with a very long neck.</summary>
    public sealed class LongneckSpecies : Species
    {
        public override string Id => "longneck";
        public override string Name => "Longneck";
        public override string Blurb => "A gentle giant. Ambles along and loves a nap.";
        public override float W => 244;
        public override float H => 200;
        public override float Ground => 194;
        public override float WalkSpeed => 38;
        public override float RunSpeed => 110;
        public override float FleeSpeed => 140;
        public override float WalkStride => 11;
        public override float RunStride => 16;
        public override float WalkLift => 6;
        public override float RunLift => 10;
        public override double RoarPitch => 0.55;
        public override string DefaultPalette => "Dune";

        private const float HipX = 104;

        private PointF HeadPos(Pose pose, float hy)
        {
            float d = pose.HeadDrop;
            var neutral = new PointF(HipX + 96, hy - 98);
            if (d >= 0)
            {
                var low = new PointF(HipX + 112, Ground - 22 + pose.Crouch * 4);
                return new PointF(Art.Lerp(neutral.X, low.X, d), Art.Lerp(neutral.Y, low.Y, d));
            }
            return new PointF(neutral.X - 8 * -d, neutral.Y - 18 * -d);
        }

        protected override PointF SnoreAnchor(Pose pose)
        {
            float hy = Ground - 56 + pose.Crouch * 24;
            var h = HeadPos(pose, hy);
            return new PointF(h.X - 8, h.Y - 18);
        }

        protected override void DrawFigure(Graphics g, Palette p, Pen outline, Pose pose)
        {
            float hx = HipX, hy = Ground - 56 + pose.Bob + pose.Crouch * 24;
            float rearX = hx - 30, frontX = hx + 34;
            DrawShadow(g, pose, hx + 2, 150);

            // far legs (diagonal pairs move together)
            DrawLeg(g, p, rearX + 4, hy - 4, pose, pose.Phase + Math.PI, true, true);
            DrawLeg(g, p, frontX + 4, hy - 2, pose, pose.Phase, true, false);

            // tail
            float s = pose.TailSway;
            using (var tail = Art.Tube(new PointF(hx - 52, hy - 26), new PointF(hx - 76, hy - 18),
                       new PointF(30, hy - 6 + s), new PointF(4, hy - 14 + s * 1.8f), 30, 3))
                Art.Fill(g, tail, p.Body, outline);

            // neck
            var head = HeadPos(pose, hy);
            var n0 = new PointF(hx + 46, hy - 32);
            var n1 = new PointF(hx + 72, hy - 46);
            var n2 = new PointF(head.X - 14, head.Y + 34 * (1 - Math.Max(0, pose.HeadDrop)));
            var n3 = head;
            using (var neck = Art.Tube(n0, n1, n2, n3, 30, 12))
                Art.Fill(g, neck, p.Body, outline);
            using (var belly = new Pen(p.Belly, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                var pts = new PointF[10];
                for (int i = 0; i < pts.Length; i++)
                {
                    float t = 0.1f + i * 0.08f;
                    var pt = Art.Bez(n0, n1, n2, n3, t);
                    var d = Art.BezD(n0, n1, n2, n3, t);
                    float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    float w = Art.Lerp(30, 12, t) / 2 - 3.5f;
                    pts[i] = new PointF(pt.X + d.Y / len * w, pt.Y - d.X / len * w);
                }
                g.DrawLines(belly, pts);
            }

            // body
            var st = g.Save();
            g.TranslateTransform(hx + 2, hy - 22);
            g.RotateTransform(-4);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-58, -32, 116, 64);
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var belly = new SolidBrush(p.Belly)) g.FillEllipse(belly, -46, 10, 96, 34);
                using (var spot = new SolidBrush(Color.FromArgb(170, p.Accent)))
                {
                    g.FillEllipse(spot, -36, -24, 14, 9);
                    g.FillEllipse(spot, -14, -30, 12, 8);
                    g.FillEllipse(spot, 8, -26, 15, 9);
                    g.FillEllipse(spot, 30, -18, 10, 7);
                    g.FillEllipse(spot, -24, -10, 9, 6);
                    g.FillEllipse(spot, 0, -12, 11, 7);
                    g.FillEllipse(spot, 20, -6, 8, 5);
                }
                g.Clip = clip;
                g.DrawPath(outline, path);
            }
            g.Restore(st);

            // near legs
            DrawLeg(g, p, rearX, hy - 4, pose, pose.Phase, false, true);
            DrawLeg(g, p, frontX, hy - 2, pose, pose.Phase + Math.PI, false, false);

            // head, aligned with the end of the neck
            var tan = Art.BezD(n0, n1, n2, n3, 1);
            float ang = (float)(Math.Atan2(tan.Y, tan.X) * 180 / Math.PI);
            DrawHead(g, p, outline, head, ang * 0.55f + 10 + Math.Max(0, pose.HeadDrop) * 30, pose);
        }

        private void DrawHead(Graphics g, Palette p, Pen outline, PointF at, float angle, Pose pose)
        {
            var st = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(angle);
            float jaw = pose.Jaw * 26;

            if (pose.Jaw > 0.05f)
                Art.MouthInterior(g, new[] { new PointF(0, 3), new PointF(24, 2) }, new PointF(0, 3), jaw,
                    new[] { new PointF(2, 5), new PointF(22, 4) });

            var js = g.Save();
            g.TranslateTransform(0, 3); g.RotateTransform(jaw); g.TranslateTransform(0, -3);
            using (var path = Art.Closed(0.35f, new PointF(-6, 3), new PointF(14, 3), new PointF(24, 3.5f),
                       new PointF(22, 7), new PointF(6, 9), new PointF(-6, 7)))
            {
                using (var b = new SolidBrush(p.Belly)) g.FillPath(b, path);
                g.DrawPath(outline, path);
            }
            g.Restore(js);

            using (var path = Art.Closed(0.4f, new PointF(-9, -6), new PointF(2, -11), new PointF(12, -10),
                       new PointF(22, -5), new PointF(27, 0), new PointF(24, 4), new PointF(6, 5), new PointF(-9, 6)))
            {
                using (var b = new SolidBrush(p.Body)) g.FillPath(b, path);
                g.DrawPath(outline, path);
            }
            // nose bump + smile line
            using (var bump = new SolidBrush(p.BodyDark)) g.FillEllipse(bump, 8, -13, 9, 6);
            if (pose.Jaw < 0.05f)
                using (var smile = new Pen(p.Outline, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(smile, 10, -2, 14, 6, 20, 120);
            Art.Eye(g, p, 1, -7, 6, pose.EyesClosed);
            using (var n = new SolidBrush(p.Outline)) g.FillEllipse(n, 21, -5, 2.6f, 1.8f);
            g.Restore(st);
        }

        private void DrawLeg(Graphics g, Palette p, float hx, float hy, Pose pose, double ph, bool far, bool rear)
        {
            var target = Art.FootTarget(pose, hx, hy, ph, Ground, 56, 2, (far ? 1.9 : 0) + (rear ? 0.8 : 0));
            target.X -= pose.Crouch * 6 * (rear ? 2.2f : -0.5f);
            Color c = far ? p.BodyDark : p.Body;
            var f = Art.Leg(g, p, c, hx, hy, target, 29, 28, rear, 22, 16);
            using (var pad = new SolidBrush(c))
            using (var o = new Pen(p.Outline, 2f))
            {
                g.FillEllipse(pad, f.X - 10, f.Y - 5, 20, 8);
                g.DrawEllipse(o, f.X - 10, f.Y - 5, 20, 8);
            }
            using (var toe = new SolidBrush(Art.Light(p.Belly, 0.2f)))
                for (int i = 0; i < 3; i++) g.FillEllipse(toe, f.X - 5 + i * 5 + (far ? 0 : 1), f.Y - 1.5f, 3.5f, 3);
        }
    }
}
