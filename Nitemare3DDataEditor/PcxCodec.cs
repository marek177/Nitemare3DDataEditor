using System.Drawing;
using System.Drawing.Imaging;

namespace Nitemare3DDataEditor;

public static class PcxCodec
{
    public const int Width = 320;
    public const int Height = 200;

    public static bool IsPcx(byte[] data) => data.Length >= 128 && data[0] == 0x0A && data[1] == 5 && data[3] == 8;

    public static Bitmap Decode(byte[] data)
    {
        if (!IsPcx(data)) throw new InvalidDataException("Položka nie je 256-farebný PCX.");
        int[] palette = ReadPalette(data);
        byte[,] pixels = new byte[Height, Width];
        int x = 0, y = 0, pos = 128;
        while (y < Height && pos < data.Length)
        {
            byte b = data[pos++]; int count = 1; byte value = b;
            if ((b & 0xC0) == 0xC0) { count = b & 0x3F; if (pos >= data.Length) break; value = data[pos++]; }
            for (int k = 0; k < count && y < Height; k++)
            {
                if (x < Width) pixels[y, x] = value;
                if (++x >= Width) { x = 0; y++; }
            }
        }
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        for (y = 0; y < Height; y++) for (x = 0; x < Width; x++)
        {
            int rgb = palette[pixels[y, x]];
            bitmap.SetPixel(x, y, Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255));
        }
        return bitmap;
    }

    public static int[] ReadPalette(byte[] data)
    {
        var palette = new int[256];
        int start = data.Length >= 769 && data[^769] == 12 ? data.Length - 768 : -1;
        for (int i = 0; i < 256; i++)
        {
            if (start >= 0) palette[i] = (data[start + i * 3] << 16) | (data[start + i * 3 + 1] << 8) | data[start + i * 3 + 2];
            else palette[i] = (i << 16) | (i << 8) | i;
        }
        return palette;
    }

    public static byte[] Encode(Bitmap source, int[]? palette = null)
    {
        palette ??= DefaultPalette();
        using var pixels = new MemoryStream();
        for (int y = 0; y < Height; y++)
        {
            int x = 0;
            while (x < Width)
            {
                Color c = source.GetPixel(x, y);
                byte index = Nearest(c, palette); int run = 1;
                while (x + run < Width && run < 63 && Nearest(source.GetPixel(x + run, y), palette) == index) run++;
                if (run > 1 || index >= 0xC0) pixels.WriteByte((byte)(0xC0 | run));
                pixels.WriteByte(index); x += run;
            }
        }
        using var result = new MemoryStream();
        byte[] header = new byte[128]; header[0] = 10; header[1] = 5; header[2] = 1; header[3] = 8;
        BitConverter.GetBytes((ushort)0).CopyTo(header, 4); BitConverter.GetBytes((ushort)0).CopyTo(header, 6);
        BitConverter.GetBytes((ushort)319).CopyTo(header, 8); BitConverter.GetBytes((ushort)199).CopyTo(header, 10);
        header[65] = 1; BitConverter.GetBytes((ushort)320).CopyTo(header, 66); BitConverter.GetBytes((ushort)1).CopyTo(header, 68);
        result.Write(header); result.Write(pixels.ToArray()); result.WriteByte(12);
        foreach (int rgb in palette) { result.WriteByte((byte)(rgb >> 16)); result.WriteByte((byte)(rgb >> 8)); result.WriteByte((byte)rgb); }
        return result.ToArray();
    }

    private static byte Nearest(Color c, int[] palette)
    {
        int best = 0, distance = int.MaxValue;
        for (int i = 0; i < palette.Length; i++) { int p = palette[i]; int dr = c.R - (p >> 16 & 255), dg = c.G - (p >> 8 & 255), db = c.B - (p & 255); int d = dr * dr + dg * dg + db * db; if (d < distance) { distance = d; best = i; } }
        return (byte)best;
    }

    public static int[] DefaultPalette() { var p = new int[256]; for (int i = 0; i < 256; i++) p[i] = (i << 16) | (i << 8) | i; return p; }
}

