using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Nitemare3DDataEditor;

public sealed class DatEntry
{
    public int Index { get; init; }
    public uint OriginalOffset { get; init; }
    public ushort OriginalLength { get; init; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public bool IsEmpty => Data.Length == 0;
    public string Sha1 => Convert.ToHexString(SHA1.HashData(Data)).ToLowerInvariant()[..12];
}

public sealed class DatArchive
{
    public string SourcePath { get; private set; } = "";
    public int HeaderSlots { get; private set; }
    public List<DatEntry> Entries { get; } = new();
    public bool Dirty { get; private set; }

    public static DatArchive Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 6) throw new InvalidDataException("DAT súbor je príliš krátky.");
        uint firstOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(2, 4));
        if (firstOffset < 6 || firstOffset > bytes.Length || firstOffset % 6 != 0)
            throw new InvalidDataException("Neplatná DAT hlavička: prvý offset nie je platný.");
        int slots = checked((int)firstOffset / 6);
        var archive = new DatArchive { SourcePath = path, HeaderSlots = slots };
        for (int i = 0; i < slots; i++)
        {
            int p = i * 6;
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p, 2));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 2, 4));
            if (length > 0 && (offset > bytes.Length || offset + length > bytes.Length))
                throw new InvalidDataException($"Položka {i} presahuje koniec súboru.");
            byte[] data = length == 0 ? Array.Empty<byte>() : bytes.AsSpan((int)offset, length).ToArray();
            archive.Entries.Add(new DatEntry { Index = i, OriginalOffset = offset, OriginalLength = length, Data = data });
        }
        return archive;
    }

    public void Replace(int index, byte[] data)
    {
        if ((uint)index >= (uint)Entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (data.Length > ushort.MaxValue) throw new InvalidDataException("DAT length je 16-bitový; položka nesmie prekročiť 65535 bajtov.");
        Entries[index].Data = data;
        Dirty = true;
    }

    public void Save(string path)
    {
        if (Entries.Count != HeaderSlots) throw new InvalidDataException("Počet slotov sa zmenil.");
        int headerBytes = checked(HeaderSlots * 6);
        var offsets = new uint[HeaderSlots];
        var lengths = new ushort[HeaderSlots];
        using var output = new MemoryStream();
        output.SetLength(headerBytes);
        // Preserve aliasing of unchanged duplicate entries, but never write data over the header.
        var groups = new Dictionary<string, (uint offset, ushort length)>();
        for (int i = 0; i < Entries.Count; i++)
        {
            byte[] data = Entries[i].Data;
            if (data.Length == 0) { offsets[i] = 0; lengths[i] = 0; continue; }
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
            if (!groups.TryGetValue(key, out var known))
            {
                uint offset = checked((uint)output.Position);
                output.Write(data);
                known = (offset, checked((ushort)data.Length));
                groups.Add(key, known);
            }
            offsets[i] = known.offset;
            lengths[i] = known.length;
        }
        output.Position = 0;
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            for (int i = 0; i < HeaderSlots; i++) { writer.Write(lengths[i]); writer.Write(offsets[i]); }
        }
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, output.ToArray());
        File.Move(temp, path, true);
        SourcePath = path;
        Dirty = false;
    }
}

