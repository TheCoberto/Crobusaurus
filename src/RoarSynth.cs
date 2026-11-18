using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Linq;
using System.Text;

namespace Crobusaurus
{
    /// <summary>
    /// Synthesises dinosaur roars in memory (no audio files shipped). Each pitch is rendered on
    /// first use and cached, with a couple of variants so repeated roars don't sound identical.
    /// </summary>
    internal sealed class RoarSynth : IDisposable
    {
        private const int SampleRate = 22050;
        private readonly Dictionary<int, SoundPlayer[]> _cache = new Dictionary<int, SoundPlayer[]>();
        private readonly Random _rng = new Random();

        /// <param name="pitch">1 = medium; lower is deeper (bigger animals).</param>
        public void Play(double pitch)
        {
            int key = (int)Math.Round(pitch * 20); // quantise so we cache a handful of voices
            if (!_cache.TryGetValue(key, out var players))
            {
                double p = key / 20.0;
                players = new[] { p * 0.93, p * 1.07 }
                    .Select((v, i) => Make(v, p < 0.7 ? 1.7 : p > 1.4 ? 0.9 : 1.3, 17 + i))
                    .ToArray();
                _cache[key] = players;
            }
            try { players[_rng.Next(players.Length)].Play(); } catch { /* no audio device - stay quiet */ }
        }

        private static SoundPlayer Make(double pitch, double seconds, int seed)
        {
            var player = new SoundPlayer(new MemoryStream(Render(pitch, seconds, seed)));
            player.Load();
            return player;
        }

        public void Dispose()
        {
            foreach (var set in _cache.Values) foreach (var p in set) p.Dispose();
            _cache.Clear();
        }

        private static byte[] Render(double pitch, double seconds, int seed)
        {
            int n = (int)(SampleRate * seconds);
            var samples = new short[n];
            var rng = new Random(seed);
            double ph = 0, noiseLp = 0, noiseBp = 0, outLp = 0;
            double decay = 2.0 * 1.3 / seconds;

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;

                double env = t < 0.07 ? t / 0.07 : Math.Exp(-(t - 0.07) * decay);
                env *= 1 + 0.18 * Math.Sin(2 * Math.PI * 6.5 * t);
                if (t > seconds - 0.08) env *= (seconds - t) / 0.08;

                double f = pitch * (88 + 70 * Math.Exp(-t * 2.8) + 7 * Math.Sin(2 * Math.PI * 5.3 * t));
                ph += 2 * Math.PI * f / SampleRate;
                double growl = Math.Sin(ph) + 0.55 * Math.Sin(2 * ph) + 0.38 * Math.Sin(3 * ph)
                             + 0.26 * Math.Sin(4 * ph) + 0.18 * Math.Sin(5 * ph) + 0.12 * Math.Sin(7 * ph);
                growl *= 0.65 + 0.35 * Math.Sin(2 * Math.PI * 31 * pitch * t);

                double w = rng.NextDouble() * 2 - 1;
                noiseLp += Math.Min(0.6, 0.12 * pitch) * (w - noiseLp);
                noiseBp += 0.5 * (noiseLp - noiseBp);
                double breath = (noiseLp - noiseBp) * 6;

                double sig = (growl * 0.42 + breath * 0.9) * env;
                double a = Math.Min(0.9, (0.18 + 0.5 * Math.Exp(-t * 1.8)) * Math.Sqrt(pitch));
                outLp += a * (sig - outLp);

                samples[i] = (short)(Math.Tanh(outLp * 1.9) * 0.85 * short.MaxValue);
            }
            return ToWav(samples);
        }

        private static byte[] ToWav(short[] samples)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(SampleRate); w.Write(SampleRate * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
                foreach (var s in samples) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
