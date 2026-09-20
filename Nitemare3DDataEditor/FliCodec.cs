using System.Buffers.Binary;
using System.Drawing.Imaging;

namespace Nitemare3DDataEditor;

public sealed class FliFrame
{
    public int Index { get; init; }
    public byte[] Raw { get; init; } = Array.Empty<byte>();
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
    public int[] Palette { get; init; } = Array.Empty<int>();
    public bool Modified { get; set; }
}

public sealed class FliAnimation
{
    public const ushort Magic = 0xAF11;
    public const ushort FrameMagic = 0xF1FA;
    public const int Width = 320, Height = 200;
    public string SourcePath { get; private set; } = "";
    public ushort Speed { get; private set; }
    public List<FliFrame> Frames { get; } = new();

    public static FliAnimation Load(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        if (file.Length < 128 || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4)) != Magic) throw new InvalidDataException("Súbor nie je štandardný FLI (0xAF11).");
        int fileSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0, 4)));
        ushort frameCount = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6)); ushort width = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(8)); ushort height = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(10));
        if (width != Width || height != Height) throw new InvalidDataException($"FLI má rozmery {width}×{height}; editor podporuje 320×200.");
        var a = new FliAnimation { SourcePath = path, Speed = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(16)) };
        byte[] pixels = new byte[Width * Height]; int[] palette = PcxCodec.DefaultPalette(); int pos = 128;
        for (int fi = 0; fi < frameCount && pos + 16 <= file.Length; fi++)
        {
            uint frameSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos)); ushort frameMagic = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(pos + 4)); ushort chunks = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(pos + 6));
            if (frameMagic != FrameMagic || frameSize < 16 || pos + frameSize > file.Length) throw new InvalidDataException($"Neplatný FLI frame {fi}.");
            int frameStart = pos, q = pos + 16; int frameEnd = checked(pos + (int)frameSize);
            for (int ci = 0; ci < chunks && q + 6 <= frameEnd; ci++)
            {
                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(q)); ushort type = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(q + 4));
                if (chunkSize < 6 || q + chunkSize > frameEnd) break;
                var payload = file.AsSpan(q + 6, (int)chunkSize - 6); switch (type) { case 11: DecodePalette(payload, palette, true); break; case 4: DecodePalette(payload, palette, false); break; case 12: DecodeLc(payload, pixels); break; case 13: Array.Clear(pixels, 0, pixels.Length); break; case 15: DecodeBrun(payload, pixels); break; case 16: payload[..Math.Min(payload.Length, pixels.Length)].CopyTo(pixels); break; }
                q += (int)chunkSize;
            }
            var framePalette = (int[])palette.Clone();
            a.Frames.Add(new FliFrame { Index = fi, Raw = file.AsSpan(frameStart, (int)frameSize).ToArray(), Pixels = (byte[])pixels.Clone(), Palette = framePalette }); pos = frameEnd;
        }
        if (a.Frames.Count == 0) throw new InvalidDataException("FLI neobsahuje žiadne snímky.");
        return a;
    }

    public static Bitmap ToBitmap(FliFrame frame)
    {
        var b = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) { int rgb = frame.Palette[frame.Pixels[y * Width + x]]; b.SetPixel(x, y, Color.FromArgb(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255)); }
        return b;
    }

    public void ReplaceFrame(int index, Bitmap source)
    {
        var f = Frames[index]; using var scaled = new Bitmap(Width, Height); using (var g = Graphics.FromImage(scaled)) g.DrawImage(source, 0, 0, Width, Height);
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) f.Pixels[y * Width + x] = Nearest(scaled.GetPixel(x, y), f.Palette);
        f.Modified = true;
    }

    public void Save(string path)
    {
        using var output = new MemoryStream(); byte[] header = new byte[128]; using (var old = File.OpenRead(SourcePath)) old.ReadExactly(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), checked((ushort)Frames.Count)); output.Write(header);
        foreach (var frame in Frames) output.Write(frame.Modified ? EncodeCopyFrame(frame.Pixels) : frame.Raw);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), checked((uint)output.Length)); long end = output.Position; output.Position = 0; output.Write(header); output.Position = end;
        string temp = path + ".tmp"; File.WriteAllBytes(temp, output.ToArray()); File.Move(temp, path, true); SourcePath = path;
    }

    private static byte[] EncodeCopyFrame(byte[] pixels)
    {
        int chunkSize = 6 + pixels.Length, frameSize = 16 + chunkSize; byte[] data = new byte[frameSize]; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), (uint)frameSize); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FrameMagic); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 1); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)chunkSize); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), 16); pixels.CopyTo(data, 22); return data;
    }
    private static void DecodePalette(ReadOnlySpan<byte> p, int[] palette, bool color64) { if (p.Length < 2) return; int q = 2, packets = BinaryPrimitives.ReadUInt16LittleEndian(p); for (int n = 0; n < packets && q + 2 <= p.Length; n++) { int skip = p[q++], count = p[q++]; if (count == 0) count = 256; int index = skip; for (int i = 0; i < count && index < 256 && q + 3 <= p.Length; i++, index++) { int r = p[q++], g = p[q++], b = p[q++]; if (color64) { r = (r << 2) | (r >> 4); g = (g << 2) | (g >> 4); b = (b << 2) | (b >> 4); } palette[index] = r << 16 | g << 8 | b; } } }
    private static void DecodeLc(ReadOnlySpan<byte> p, byte[] pixels) { if (p.Length < 4) return; int first = BinaryPrimitives.ReadUInt16LittleEndian(p), lines = BinaryPrimitives.ReadUInt16LittleEndian(p[2..]), q = 4, y = first; for (int ly = 0; ly < lines && y < Height && q < p.Length; ly++, y++) { int x = p[q++], packets = q < p.Length ? p[q++] : 0; for (int k = 0; k < packets && q + 2 <= p.Length; k++) { x += p[q++]; sbyte count = unchecked((sbyte)p[q++]); if (count >= 0) { for (int i = 0; i < count && q < p.Length && x < Width; i++) pixels[y * Width + x++] = p[q++]; } else { byte value = q < p.Length ? p[q++] : (byte)0; for (int i = 0; i < -count && x < Width; i++) pixels[y * Width + x++] = value; } } } }
    private static void DecodeBrun(ReadOnlySpan<byte> p, byte[] pixels) { int q = 0; for (int y = 0; y < Height && q < p.Length; y++) { if (q >= p.Length) break; int packets = p[q++], x = 0; for (int k = 0; k < packets && q < p.Length; k++) { sbyte count = unchecked((sbyte)p[q++]); if (count >= 0) { byte value = q < p.Length ? p[q++] : (byte)0; for (int i = 0; i < count && x < Width; i++) pixels[y * Width + x++] = value; } else { for (int i = 0; i < -count && q < p.Length && x < Width; i++) pixels[y * Width + x++] = p[q++]; } } } }
    private static byte Nearest(Color c, int[] palette) { int best = 0, distance = int.MaxValue; for (int i = 0; i < palette.Length; i++) { int p = palette[i], dr = c.R - (p >> 16 & 255), dg = c.G - (p >> 8 & 255), db = c.B - (p & 255), d = dr * dr + dg * dg + db * db; if (d < distance) { distance = d; best = i; } } return (byte)best; }
}

