using System.IO;
using System.Runtime.InteropServices;

namespace Core.Services;

internal static class Tone
{
    private const uint SndAsync = 0x1, SndNoDefault = 0x2, SndMemory = 0x4;
    private const int Rate = 44100;
    private static readonly Dictionary<int, IntPtr> Waves = new();
    private static readonly object Gate = new();

    [DllImport("winmm.dll")]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    public static void Beep(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        if (volume == 0) return;
        IntPtr wave;
        lock (Gate)
        {
            if (!Waves.TryGetValue(volume, out wave))
            {
                var bytes = Wave(880, 0.12, volume / 100.0);
                wave = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, wave, bytes.Length);
                Waves[volume] = wave;
            }
        }
        try { PlaySound(wave, IntPtr.Zero, SndAsync | SndMemory | SndNoDefault); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { App.LogError(ex); }
    }

    internal static byte[] Wave(double hz, double seconds, double gain)
    {
        int samples = (int)(Rate * seconds), fade = Rate / 200;
        var bytes = new byte[44 + samples * 2];
        using (var w = new BinaryWriter(new MemoryStream(bytes)))
        {
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + samples * 2);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8.ToArray());
            w.Write(samples * 2);
            for (int i = 0; i < samples; i++)
            {
                double edge = Math.Min(1, Math.Min(i, samples - 1 - i) / (double)fade);
                w.Write((short)Math.Round(Math.Sin(2 * Math.PI * hz * i / Rate) * edge * gain * short.MaxValue * 0.8));
            }
        }
        return bytes;
    }
}
