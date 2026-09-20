using System.Buffers.Binary;

namespace Nitemare3DDataEditor;

public static class AudioCodec
{
    // Windows Nitemare 3D SND.DAT stores raw unsigned 8-bit mono PCM at
    // 11025 Hz. Older DOS SND.DAT versions store Creative VOC files instead;
    // their rate is encoded in each VOC block and must not be treated as raw PCM.
    public const int DefaultSampleRate = 11025;

    public static bool IsMidi(byte[] b) => b.Length >= 4 && b[0] == 'M' && b[1] == 'T' && b[2] == 'h' && b[3] == 'd';

    public static bool IsVoc(byte[] b) =>
        b.Length >= 26 &&
        b.AsSpan(0, 19).SequenceEqual("Creative Voice File"u8) &&
        b[19] == 0x1A;

    public static byte[] ToWave(byte[] audio, int sampleRate = DefaultSampleRate)
        => IsVoc(audio) ? VocToWave(audio) : BuildWave(audio, sampleRate, 1, 8);

    public static byte[] VocToWave(byte[] voc)
    {
        if (!IsVoc(voc))
            throw new InvalidDataException("Nie je Creative VOC.");

        int firstBlock = BinaryPrimitives.ReadUInt16LittleEndian(voc.AsSpan(20, 2));
        if (firstBlock < 26 || firstBlock > voc.Length)
            throw new InvalidDataException("Neplatná VOC hlavička.");

        using var pcm = new MemoryStream();
        int pos = firstBlock;
        int outRate = 0, outBits = 0, outChannels = 0, currentCodec = -1;
        int? extendedRate = null, extendedChannels = null, extendedCodec = null;

        // Creative codec 0x01 = Sound Blaster 8-bit -> 4-bit ADPCM.
        // The first byte is an uncompressed predictor sample, then every byte
        // contains two ADPCM codes (high nibble first). Continuation block 0x02
        // keeps the predictor and adaptive step state from the previous block.
        int creativeLast = 0;
        int creativeStepIndex = 0;
        bool creativeInitialized = false;
        int[] creativeSteps = [0x100, 0x200, 0x400, 0x800];
        int[] creativeChanges = [-1, 0, 0, 0, 0, 1, 1, 1];

        void WriteS16(int sample)
        {
            short s = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
            pcm.WriteByte((byte)(s & 0xFF));
            pcm.WriteByte((byte)((s >> 8) & 0xFF));
        }

        int DecodeCreativeNibble(int code)
        {
            code &= 0x0F;
            int magnitude = ((code & 7) << 1) | 1;
            int delta = ((creativeSteps[creativeStepIndex] * magnitude) >> 1) & ~0xFF;
            if ((code & 8) != 0) delta = -delta;

            creativeLast = Math.Clamp(creativeLast + delta, short.MinValue, short.MaxValue);
            creativeStepIndex = Math.Clamp(creativeStepIndex + creativeChanges[code & 7], 0, creativeSteps.Length - 1);
            return creativeLast;
        }

        void DecodeCreative4(ReadOnlySpan<byte> data, bool newStream)
        {
            if (newStream)
            {
                creativeInitialized = false;
                creativeStepIndex = 0;
            }

            int i = 0;
            if (!creativeInitialized)
            {
                if (data.Length == 0)
                    throw new InvalidDataException("VOC Creative ADPCM blok nemá úvodný predictor sample.");

                // VOC Creative ADPCM predictor is unsigned 8-bit PCM.
                creativeLast = (data[0] - 128) << 8;
                creativeStepIndex = 0;
                creativeInitialized = true;
                WriteS16(creativeLast);
                i = 1;
            }

            for (; i < data.Length; i++)
            {
                byte b = data[i];
                WriteS16(DecodeCreativeNibble(b >> 4));
                WriteS16(DecodeCreativeNibble(b & 0x0F));
            }
        }

        int CodecOutputBits(int codec) => codec switch
        {
            0x00 => 8,
            0x01 => 16, // decoded Creative 4-bit ADPCM -> signed 16-bit PCM
            0x04 => 16,
            _ => throw new NotSupportedException($"VOC codec 0x{codec:X} zatiaľ nie je podporovaný.")
        };

        void EnsureFormat(int rate, int channels, int codec, int? bitsHint = null)
        {
            int bits = CodecOutputBits(codec);

            // For block type 9 the stored bit depth can describe the compressed
            // source. Codec 0x01 is 4-bit ADPCM but decodes to 16-bit PCM, so do
            // not compare its source bit hint against the decoded WAV bit depth.
            if (bitsHint.HasValue && codec != 0x01 && bitsHint.Value != bits)
                throw new InvalidDataException("VOC bit depth nesúhlasí s codec ID.");
            if (rate <= 0 || channels <= 0)
                throw new InvalidDataException("Neplatný VOC audio formát.");
            if (codec == 0x01 && channels != 1)
                throw new NotSupportedException("Creative VOC ADPCM codec 0x01 je zatiaľ podporovaný iba mono.");

            if (outRate == 0)
            {
                outRate = rate;
                outChannels = channels;
                outBits = bits;
                currentCodec = codec;
            }
            else if (outRate != rate || outChannels != channels || outBits != bits)
            {
                throw new NotSupportedException("VOC mení sample rate/kanály/bit depth v rámci jedného zvuku.");
            }
        }

        void AppendAudio(ReadOnlySpan<byte> data, int rate, int channels, int codec, int? bitsHint = null, bool newStream = false)
        {
            EnsureFormat(rate, channels, codec, bitsHint);
            if (codec == 0x01)
                DecodeCreative4(data, newStream);
            else
                pcm.Write(data);
        }

        while (pos < voc.Length)
        {
            byte type = voc[pos++];
            if (type == 0x00) break; // terminator
            if (type == 0x07) continue; // repeat end has no size field
            if (pos + 3 > voc.Length)
                throw new InvalidDataException("VOC blok má neúplnú hlavičku.");

            int len = voc[pos] | (voc[pos + 1] << 8) | (voc[pos + 2] << 16);
            pos += 3;
            if (len < 0 || pos + len > voc.Length)
                throw new InvalidDataException("VOC blok presahuje koniec položky.");

            int end = pos + len;
            switch (type)
            {
                case 0x01: // sound data with format
                    if (len < 2) throw new InvalidDataException("VOC sound block je príliš krátky.");
                    int rate, channels, codec;
                    if (extendedRate.HasValue)
                    {
                        rate = extendedRate.Value;
                        channels = extendedChannels ?? 1;
                        codec = extendedCodec ?? voc[pos + 1];
                        extendedRate = extendedChannels = extendedCodec = null;
                    }
                    else
                    {
                        int divisor = voc[pos];
                        rate = (int)Math.Round(1_000_000.0 / (256 - divisor));
                        channels = 1;
                        codec = voc[pos + 1];
                    }
                    AppendAudio(voc.AsSpan(pos + 2, len - 2), rate, channels, codec, newStream: true);
                    currentCodec = codec;
                    break;

                case 0x02: // sound continuation
                    if (outRate == 0 || currentCodec < 0)
                        throw new InvalidDataException("VOC continuation bez predchádzajúceho sound bloku.");
                    AppendAudio(voc.AsSpan(pos, len), outRate, outChannels, currentCodec, newStream: false);
                    break;

                case 0x03: // silence
                    if (len >= 3)
                    {
                        int samples = BinaryPrimitives.ReadUInt16LittleEndian(voc.AsSpan(pos, 2)) + 1;
                        int rateSilence = (int)Math.Round(1_000_000.0 / (256 - voc[pos + 2]));
                        if (outRate == 0) EnsureFormat(rateSilence, 1, 0x00);
                        if (outRate != rateSilence)
                            throw new NotSupportedException("VOC silence používa iný sample rate.");
                        int bytesPerFrame = outChannels * (outBits / 8);
                        byte silenceValue = outBits == 8 ? (byte)0x80 : (byte)0x00;
                        for (int i = 0; i < samples * bytesPerFrame; i++) pcm.WriteByte(silenceValue);
                    }
                    break;

                case 0x04: // marker
                case 0x05: // text
                    break;

                case 0x06: // repeat start
                    throw new NotSupportedException("VOC repeat blok zatiaľ nie je podporovaný.");

                case 0x08: // extended format, applies to following block 1
                    if (len < 4) throw new InvalidDataException("VOC extended block je príliš krátky.");
                    int tc = BinaryPrimitives.ReadUInt16LittleEndian(voc.AsSpan(pos, 2));
                    int ch = voc[pos + 3] + 1;
                    extendedRate = (int)Math.Round(256_000_000.0 / (ch * (65536 - tc)));
                    extendedCodec = voc[pos + 2];
                    extendedChannels = ch;
                    break;

                case 0x09: // new sound data format
                    if (len < 12) throw new InvalidDataException("VOC new-sound block je príliš krátky.");
                    int newRate = BinaryPrimitives.ReadInt32LittleEndian(voc.AsSpan(pos, 4));
                    int newBits = voc[pos + 4];
                    int newChannels = voc[pos + 5];
                    int newCodec = BinaryPrimitives.ReadUInt16LittleEndian(voc.AsSpan(pos + 6, 2));
                    AppendAudio(voc.AsSpan(pos + 12, len - 12), newRate, newChannels, newCodec, newBits, newStream: true);
                    currentCodec = newCodec;
                    break;

                default:
                    // Unknown non-audio metadata block: safely skip it.
                    break;
            }

            pos = end;
        }

        if (outRate == 0 || pcm.Length == 0)
            throw new InvalidDataException("VOC neobsahuje podporované PCM audio.");

        return BuildWave(pcm.ToArray(), outRate, outChannels, outBits);
    }

    private static byte[] BuildWave(byte[] pcm, int sampleRate, int channels, int bits)
    {
        if (sampleRate <= 0 || channels <= 0 || (bits != 8 && bits != 16))
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        int blockAlign = checked(channels * (bits / 8));
        int byteRate = checked(sampleRate * blockAlign);
        int dataLength = pcm.Length;

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataLength);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bits);
        w.Write("data"u8.ToArray());
        w.Write(dataLength);
        w.Write(pcm);
        return s.ToArray();
    }

    public static byte[] FromWave(byte[] wave)
    {
        var (data, _, channels, bits) = ReadWave(wave);
        if (bits == 8 && channels == 1) return data;
        if (bits == 8)
        {
            var output = new byte[data.Length / channels];
            for (int i = 0, o = 0; i + channels <= data.Length; i += channels, o++) output[o] = data[i];
            return output;
        }
        if (bits == 16)
        {
            var output = new byte[data.Length / 2 / channels];
            for (int i = 0, o = 0; i + channels * 2 <= data.Length; i += channels * 2, o++)
            {
                short sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i));
                output[o] = (byte)((sample + 32768) >> 8);
            }
            return output;
        }
        throw new InvalidDataException("Podporovaný import je 8-bit alebo 16-bit PCM WAV.");
    }

    public static byte[] WaveToVoc(byte[] wave)
    {
        var (data, rate, channels, bits) = ReadWave(wave);
        byte[] pcm8;
        if (bits == 8)
        {
            pcm8 = channels == 1 ? data : Enumerable.Range(0, data.Length / channels).Select(i => data[i * channels]).ToArray();
        }
        else if (bits == 16)
        {
            pcm8 = new byte[data.Length / 2 / channels];
            for (int i = 0, o = 0; i + channels * 2 <= data.Length; i += channels * 2, o++)
            {
                short sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i));
                pcm8[o] = (byte)((sample + 32768) >> 8);
            }
        }
        else throw new InvalidDataException("VOC import podporuje 8-bit alebo 16-bit PCM WAV.");

        int divisor = 256 - (int)Math.Round(1_000_000.0 / rate);
        if (divisor < 0 || divisor > 255)
            throw new InvalidDataException("Sample rate WAV nie je možné zapísať do klasického VOC bloku.");

        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        w.Write("Creative Voice File"u8.ToArray());
        w.Write((byte)0x1A);
        w.Write((ushort)26);
        ushort version = 0x010A;
        w.Write(version);
        w.Write((ushort)(~version + 0x1234));
        w.Write((byte)0x01);
        int blockLength = checked(pcm8.Length + 2);
        w.Write((byte)(blockLength & 0xFF));
        w.Write((byte)((blockLength >> 8) & 0xFF));
        w.Write((byte)((blockLength >> 16) & 0xFF));
        w.Write((byte)divisor);
        w.Write((byte)0x00);
        w.Write(pcm8);
        w.Write((byte)0x00);
        return s.ToArray();
    }

    private static (byte[] data, int rate, int channels, int bits) ReadWave(byte[] wave)
    {
        if (wave.Length < 44 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wave.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Nie je WAV.");

        int pos = 12, rate = 0;
        short format = 0, channels = 0, bits = 0;
        byte[]? data = null;
        while (pos + 8 <= wave.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wave, pos, 4);
            int n = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(pos + 4, 4));
            pos += 8;
            if (n < 0 || pos + n > wave.Length) break;
            if (id == "fmt " && n >= 16)
            {
                format = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(pos));
                channels = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(pos + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(pos + 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(pos + 14));
            }
            if (id == "data") data = wave.AsSpan(pos, n).ToArray();
            pos += n + (n & 1);
        }

        if (data is null || format != 1 || channels < 1 || rate <= 0)
            throw new InvalidDataException("WAV musí byť PCM.");
        return (data, rate, channels, bits);
    }
}
