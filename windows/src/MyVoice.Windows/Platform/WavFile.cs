using System.Runtime.InteropServices;
using MyVoice.Windows.Core;
using NAudio.Wave;

namespace MyVoice.Windows.Platform;

/// <summary>16 kHz / 16-bit / mono WAV files: test fixtures, --simulate input and the debug last_recording.wav.</summary>
static class WavFile
{
    static readonly WaveFormat Format = new(CapturedAudio.SampleRate, 16, 1);

    public static short[] Read(string path)
    {
        using var reader = new WaveFileReader(path);
        var f = reader.WaveFormat;
        if (f.SampleRate != Format.SampleRate || f.BitsPerSample != 16 || f.Channels != 1)
            throw new InvalidDataException($"{path} is {f}; MyVoice needs 16 kHz 16-bit mono");
        var bytes = new byte[reader.Length];
        var read = reader.Read(bytes);
        return MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, read)).ToArray();
    }

    public static void Write(string path, short[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, Format);
        writer.Write(MemoryMarshal.AsBytes(samples.AsSpan()));
    }
}
