using System;
using System.IO;
using System.Media;
using System.Text;

namespace Sailback
{
    /// <summary>
    /// Synthesises a short dinosaur roar at startup (no audio files shipped). A few pitch
    /// variants are rendered so repeated roars don't sound identical.
    /// </summary>
    internal sealed class RoarSynth : IDisposable
    {
        private const int SampleRate = 22050;
        private readonly SoundPlayer[] _players;
        private readonly Random _rng = new Random();

        public RoarSynth()
        {
            double[] pitches = { 0.85, 1.0, 1.18 };
            _players = new SoundPlayer[pitches.Length];
            for (int i = 0; i < pitches.Length; i++)
            {
                var p = new SoundPlayer(new MemoryStream(Render(pitches[i], 1.3, 17 + i)));
                p.Load();
                _players[i] = p;
            }
        }

        public void Play(float sizeFactor)
        {
            // bigger dinos get the deeper voice
            int idx = sizeFactor >= 1.3f ? 0 : sizeFactor <= 0.8f ? 2 : _rng.Next(_players.Length);
            try { _players[idx].Play(); } catch { /* no audio device - stay quiet */ }
        }

        public void Dispose()
        {
            foreach (var p in _players) p.Dispose();
        }

        private static byte[] Render(double pitch, double seconds, int seed)
        {
            int n = (int)(SampleRate * seconds);
            var samples = new short[n];
            var rng = new Random(seed);
            double ph = 0, noiseLp = 0, noiseBp = 0, outLp = 0;

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;

                double env = t < 0.07 ? t / 0.07 : Math.Exp(-(t - 0.07) * 2.0);
                env *= 1 + 0.18 * Math.Sin(2 * Math.PI * 6.5 * t);
                if (t > seconds - 0.08) env *= (seconds - t) / 0.08;

                // growl: falling fundamental with vibrato and a rattling amplitude modulation
                double f = pitch * (88 + 70 * Math.Exp(-t * 2.8) + 7 * Math.Sin(2 * Math.PI * 5.3 * t));
                ph += 2 * Math.PI * f / SampleRate;
                double growl = Math.Sin(ph) + 0.55 * Math.Sin(2 * ph) + 0.38 * Math.Sin(3 * ph)
                             + 0.26 * Math.Sin(4 * ph) + 0.18 * Math.Sin(5 * ph) + 0.12 * Math.Sin(7 * ph);
                growl *= 0.65 + 0.35 * Math.Sin(2 * Math.PI * 31 * t);

                // breathy noise, band-limited
                double w = rng.NextDouble() * 2 - 1;
                noiseLp += 0.12 * (w - noiseLp);
                noiseBp += 0.5 * (noiseLp - noiseBp);
                double breath = (noiseLp - noiseBp) * 6;

                double sig = (growl * 0.42 + breath * 0.9) * env;

                // "mouth" low-pass that closes as the roar fades
                double a = 0.18 + 0.5 * Math.Exp(-t * 1.8);
                outLp += a * (sig - outLp);

                double y = Math.Tanh(outLp * 1.9) * 0.85;
                samples[i] = (short)(y * short.MaxValue);
            }

            return ToWav(samples);
        }

        private static byte[] ToWav(short[] samples)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);              // PCM
                w.Write((short)1);              // mono
                w.Write(SampleRate);
                w.Write(SampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataLen);
                foreach (var s in samples) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
