// Corner - the click: a short, quiet tick when a task is ticked off (or put back) and when a switch is turned on or off.
// It is made here, not read from a file: 40 ms of a 1.5 kHz tone that dies away at once, at about a fifth of full
// volume. "Click sounds" in Settings turns it off ("Click" = 0 in settings.json). Test runs are silent.
using System;
using System.IO;

namespace Corner
{
    static class Click
    {
        static System.Media.SoundPlayer player; // kept: it holds the sound in memory, ready to play at once

        public static void Play()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("CORNER_DATA") != null || Settings.Get("Click", 1) < 0.5) return;
                if (player == null) { player = new System.Media.SoundPlayer(new MemoryStream(Wav())); player.Load(); }
                player.Play(); // does not wait for it to end
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// The tick as a .wav: 44.1 kHz, 16 bit, one channel.
        static byte[] Wav()
        {
            const int rate = 44100; const double seconds = 0.040, volume = 0.20, decay = 0.006, attack = 0.0008;
            int n = (int)(rate * seconds);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double tone = Math.Sin(2 * Math.PI * 1500 * t) + 0.35 * Math.Sin(2 * Math.PI * 3100 * t); // a little brightness on top
                double shape = Math.Min(1, t / attack) * Math.Exp(-t / decay);
                w.Write((short)(volume / 1.35 * tone * shape * 32767));
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}
