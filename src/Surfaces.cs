using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sailback
{
    /// <summary>Where the dinosaurs are allowed to walk.</summary>
    public enum WalkMode
    {
        Taskbar,   // along the top edge of the taskbar only
        Windows,   // taskbar plus the top edges of open windows (they jump up and fall off)
        Roam,      // anywhere on the screen, in any direction
    }

    /// <summary>
    /// A periodically refreshed snapshot of the visible top-level windows, in z-order, used to let
    /// dinosaurs stand on window title bars. Only the uncovered parts of a window's top edge count.
    /// </summary>
    internal sealed class Surfaces
    {
        public struct Win
        {
            public IntPtr Hwnd;
            public Rectangle Rect;
            public bool Standable;
        }

        private readonly List<Win> _wins = new List<Win>(); // topmost first
        private readonly HashSet<IntPtr> _ignore = new HashSet<IntPtr>();

        public void Refresh(IEnumerable<IntPtr> ourWindows)
        {
            _ignore.Clear();
            foreach (var h in ourWindows) _ignore.Add(h);
            _wins.Clear();
            Native.EnumWindows(Visit, IntPtr.Zero);
        }

        private bool Visit(IntPtr h, IntPtr _)
        {
            if (_ignore.Contains(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            if (Native.IsCloaked(h)) return true; // other virtual desktops, suspended UWP windows, etc.

            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true; // click-through overlays
            string cls = Native.ClassName(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;

            bool titled = Native.GetWindowTextLength(h) > 0;
            bool tool = (ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0;
            if (!titled && tool) return true;

            var r = Native.FrameBounds(h);
            if (r.Width <= 0 || r.Height <= 0) return true;

            var wa = Screen.FromRectangle(r).WorkingArea;
            bool standable = titled && !tool && r.Width >= 120 && r.Height >= 60 && r.Top > wa.Top + 40 && r.Top < wa.Bottom - 20;
            _wins.Add(new Win { Hwnd = h, Rect = r, Standable = standable });
            return true;
        }

        /// <summary>Dev helper: describe every window considered, in z-order.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var w in _wins)
            {
                var title = new System.Text.StringBuilder(256);
                Native.GetWindowText(w.Hwnd, title, 256);
                sb.AppendLine($"{(w.Standable ? "STAND" : "     ")} {w.Rect,-40} {Native.ClassName(w.Hwnd),-30} {title}");
            }
            return sb.ToString();
        }

        /// <summary>Is the top edge of window #index uncovered at x?</summary>
        private bool TopVisibleAt(int index, int x)
        {
            var w = _wins[index];
            var probe = new Point(x, w.Rect.Top + 3);
            for (int i = 0; i < index; i++)
                if (_wins[i].Rect.Contains(probe)) return false;
            return true;
        }

        /// <summary>
        /// The highest walkable window edge under x that is at or below <paramref name="minY"/>
        /// and above <paramref name="belowY"/>. Returns false if there is none.
        /// </summary>
        public bool FindSurface(int x, double minY, double belowY, double margin, out IntPtr hwnd, out int top)
        {
            hwnd = IntPtr.Zero;
            top = int.MaxValue;
            for (int i = 0; i < _wins.Count; i++)
            {
                var w = _wins[i];
                if (!w.Standable) continue;
                if (x < w.Rect.Left + margin || x > w.Rect.Right - margin) continue;
                if (w.Rect.Top < minY || w.Rect.Top >= belowY || w.Rect.Top >= top) continue;
                if (!TopVisibleAt(i, x)) continue;
                hwnd = w.Hwnd;
                top = w.Rect.Top;
            }
            return hwnd != IntPtr.Zero;
        }

        /// <summary>Current rectangle of a window we're standing on, if its top edge is still walkable at x.</summary>
        public bool TryGet(IntPtr hwnd, int x, double margin, out Rectangle rect)
        {
            for (int i = 0; i < _wins.Count; i++)
            {
                if (_wins[i].Hwnd != hwnd) continue;
                rect = _wins[i].Rect;
                return _wins[i].Standable && x >= rect.Left + margin && x <= rect.Right - margin && TopVisibleAt(i, x);
            }
            rect = Rectangle.Empty;
            return false;
        }

        /// <summary>Pick a window top above the given feet position to jump up onto.</summary>
        public bool PickJumpTarget(Random rng, double cx, double feetY, double maxUp, double maxSide, double margin,
            out int targetX, out int top)
        {
            var options = new List<(int x, int top)>();
            for (int i = 0; i < _wins.Count; i++)
            {
                var w = _wins[i];
                if (!w.Standable) continue;
                if (w.Rect.Top > feetY - 40 || w.Rect.Top < feetY - maxUp) continue;
                int lo = (int)Math.Max(w.Rect.Left + margin, cx - maxSide);
                int hi = (int)Math.Min(w.Rect.Right - margin, cx + maxSide);
                if (hi - lo < 20) continue;
                for (int tries = 0; tries < 4; tries++)
                {
                    int x = rng.Next(lo, hi);
                    if (TopVisibleAt(i, x)) { options.Add((x, w.Rect.Top)); break; }
                }
            }
            if (options.Count == 0) { targetX = 0; top = 0; return false; }
            var pick = options[rng.Next(options.Count)];
            targetX = pick.x;
            top = pick.top;
            return true;
        }
    }
}
