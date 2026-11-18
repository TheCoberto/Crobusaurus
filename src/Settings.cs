using System;
using System.Globalization;
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
        public int Count = 1;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k == null) return s;
                    s.Sound = Get(k, "Sound", s.Sound);
                    s.RunFromMouse = Get(k, "RunFromMouse", s.RunFromMouse);
                    s.AlwaysOnTop = Get(k, "AlwaysOnTop", s.AlwaysOnTop);
                    s.HideForFullscreen = Get(k, "HideForFullscreen", s.HideForFullscreen);
                    if (float.TryParse(k.GetValue("Size") as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                        s.Size = Math.Max(0.5f, Math.Min(2.5f, size));
                    if (k.GetValue("Count") is int c) s.Count = Math.Max(1, Math.Min(SailbackApp.MaxDinos, c));
                }
            }
            catch { /* fall back to defaults */ }
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
                    k.SetValue("Size", Size.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
                    k.SetValue("Count", Count, RegistryValueKind.DWord);
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
