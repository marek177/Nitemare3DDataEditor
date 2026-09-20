using System.Buffers.Binary;

namespace Nitemare3DDataEditor;

public static class AudioCodec
{
    // Nitemare 3D SND.DAT PCM effects are unsigned 8-bit mono at 11025 Hz.
    // This matches Adam Biser's N3DExtractor output byte-for-byte apart from
    // the RIFF/WAVE header it adds around the original raw PCM payload.
    public const int DefaultSampleRate = 11025;

    public static bool IsMidi(byte[] b) => b.Length >= 4 && b[0] == 'M' && b[1] == 'T' && b[2] == 'h' && b[3] == 'd';

    public static byte[] ToWave(byte[] pcm, int sampleRate = DefaultSampleRate)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        int dataLength = pcm.Length; w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLength); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate); w.Write((short)1); w.Write((short)8);
        w.Write("data"u8.ToArray()); w.Write(dataLength); w.Write(pcm); return s.ToArray();
    }

    public static byte[] FromWave(byte[] wave)
    {
        if (wave.Length < 44 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wave.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("Nie je WAV.");
        int pos = 12; short format = 0, channels = 0, bits = 0; byte[]? data = null;
        while (pos + 8 <= wave.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wave, pos, 4); int n = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(pos + 4, 4)); pos += 8; if (n < 0 || pos + n > wave.Length) break;
            if (id == "fmt ") { format = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(pos)); channels = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(pos + 2)); bits = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(pos + 14)); }
            if (id == "data") data = wave.AsSpan(pos, n).ToArray(); pos += n + (n & 1);
        }
        if (data is null || format != 1 || channels < 1) throw new InvalidDataException("WAV musí byť PCM.");
        if (bits == 8 && channels == 1) return data;
        if (bits == 16) { var output = new byte[data.Length / 2 / channels]; for (int i = 0, o = 0; i + channels * 2 <= data.Length; i += channels * 2, o++) { short sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i)); output[o] = (byte)((sample + 32768) >> 8); } return output; }
        throw new InvalidDataException("Podporovaný import je 8-bit alebo 16-bit PCM WAV.");
    }
}
