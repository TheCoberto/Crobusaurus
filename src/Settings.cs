using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Sailback
{
    /// <summary>User preferences, stored per-user in HKCU\Software\Sailback (no admin rights needed).</summary>
    internal sealed class Settings
    {
        private const string KeyPath = @"Software\Sailback";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Sailback";

        public bool Sound = true;
        public bool RunFromMouse = true;
        public bool AlwaysOnTop = true;
        public bool HideForFullscreen = true;
        public float Size = 1f;
        public WalkMode Mode = WalkMode.Taskbar;

        /// <summary>One entry per dinosaur: species id + palette name.</summary>
        public List<(string Species, string Palette)> Herd = new List<(string, string)>();

        /// <summary>False until the user has been through the "choose your dinosaur" window once.</summary>
        public bool HasChosen;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k != null)
                    {
                        s.Sound = Get(k, "Sound", s.Sound);
                        s.RunFromMouse = Get(k, "RunFromMouse", s.RunFromMouse);
                        s.AlwaysOnTop = Get(k, "AlwaysOnTop", s.AlwaysOnTop);
                        s.HideForFullscreen = Get(k, "HideForFullscreen", s.HideForFullscreen);
                        s.HasChosen = Get(k, "HasChosen", false);
                        if (float.TryParse(k.GetValue("Size") as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                            s.Size = Math.Max(0.5f, Math.Min(2.5f, size));
                        if (Enum.TryParse(k.GetValue("Mode") as string, true, out WalkMode mode)) s.Mode = mode;
                        if (k.GetValue("Herd") is string herd)
                            s.Herd = herd.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(e => e.Split(':'))
                                .Where(a => a.Length == 2)
                                .Select(a => (a[0], a[1]))
                                .Take(SailbackApp.MaxDinos)
                                .ToList();
                    }
                }
            }
            catch { /* fall back to defaults */ }
            if (s.Herd.Count == 0) s.Herd.Add(("sailback", "Fern"));
            return s;
        }

        public void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("Sound", Sound ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("RunFromMouse", RunFromMouse ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("AlwaysOnTop", AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("HideForFullscreen", HideForFullscreen ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("HasChosen", HasChosen ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("Size", Size.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
                    k.SetValue("Mode", Mode.ToString(), RegistryValueKind.String);
                    k.SetValue("Herd", string.Join(";", Herd.Select(h => h.Species + ":" + h.Palette)), RegistryValueKind.String);
                }
            }
            catch { /* non-fatal */ }
        }

        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                        return k?.GetValue(RunValue) is string v && v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        if (value) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue(RunValue, false);
                    }
                }
                catch { }
            }
        }

        private static bool Get(RegistryKey k, string name, bool fallback) =>
            k.GetValue(name) is int v ? v != 0 : fallback;
    }
}
