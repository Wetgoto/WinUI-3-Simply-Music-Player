using System.Buffers.Binary;
using System.Text;

namespace Music.Services;

public sealed record EmbeddedMetadata(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    TimeSpan Duration,
    uint Bitrate,
    byte[]? Picture);

/// <summary>
/// Reads tags and embedded cover art directly from the common local audio
/// containers. The Windows thumbnail/property handlers don't consistently
/// expose FLAC pictures or ID3 APIC frames, so this is used before the shell
/// thumbnail fallback.
/// </summary>
public static class EmbeddedMetadataReader
{
    private const int MaxTagBytes = 32 * 1024 * 1024;

    public static EmbeddedMetadata? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream);
        }
        catch
        {
            return null;
        }
    }

    public static EmbeddedMetadata? Read(Stream stream)
    {
        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[12];
            var read = stream.Read(header);
            if (read >= 4 && header[..4].SequenceEqual("fLaC"u8))
            {
                stream.Position = 4;
                return ReadFlac(stream);
            }

            if (read >= 3 && header[..3].SequenceEqual("ID3"u8))
            {
                stream.Position = 0;
                return ReadMp3(stream);
            }

            if (read >= 4 && header[..4].SequenceEqual("RIFF"u8))
            {
                stream.Position = 0;
                return ReadWav(stream);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static EmbeddedMetadata? ReadFlac(Stream stream)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? picture = null;
        var duration = TimeSpan.Zero;

        while (true)
        {
            var blockHeader = new byte[4];
            if (!ReadExactly(stream, blockHeader))            {
                break;
            }

            var isLast = (blockHeader[0] & 0x80) != 0;
            var blockType = blockHeader[0] & 0x7F;
            var length = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
            if (length < 0 || length > MaxTagBytes)
            {
                break;
            }

            if (blockType is 0 or 4 or 6)
            {
                var data = new byte[length];
                if (!ReadExactly(stream, data))
                {
                    break;
                }

                if (blockType == 0)
                {
                    duration = ReadFlacStreamInfo(data);
                }
                else if (blockType == 4)
                {
                    ParseVorbisComments(data, tags, ref picture);
                }
                else
                {
                    picture ??= ExtractFlacPicture(data);
                }
            }
            else if (stream.CanSeek)
            {
                stream.Seek(length, SeekOrigin.Current);
            }
            else
            {
                break;
            }

            if (isLast)
            {
                break;
            }
        }

        return Create(tags, duration, picture, stream);
    }

    private static TimeSpan ReadFlacStreamInfo(byte[] data)
    {
        if (data.Length < 18)
        {
            return TimeSpan.Zero;
        }

        var packed = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(10, 8));
        var sampleRate = (int)((packed >> 44) & 0xFFFFF);
        var totalSamples = packed & 0xFFFFFFFFF;
        if (sampleRate <= 0 || totalSamples == 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds((double)totalSamples / sampleRate);
    }

    private static void ParseVorbisComments(
        byte[] data,
        Dictionary<string, string> tags,
        ref byte[]? picture)
    {
        var position = 0;
        if (!TryReadUInt32LittleEndian(data, ref position, out var vendorLength) ||
            vendorLength > (uint)data.Length ||
            position + vendorLength + 4 > data.Length)
        {
            return;
        }

        position += (int)vendorLength;
        if (!TryReadUInt32LittleEndian(data, ref position, out var commentCount))
        {
            return;
        }

        for (var i = 0; i < commentCount && i < 4096; i++)
        {
            if (!TryReadUInt32LittleEndian(data, ref position, out var length) ||
                length > (uint)(data.Length - position))
            {
                break;
            }

            var comment = Encoding.UTF8.GetString(data, position, (int)length);
            position += (int)length;
            var separator = comment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = comment[..separator].Trim();
            var value = comment[(separator + 1)..].Trim();
            if (key.Equals("METADATA_BLOCK_PICTURE", StringComparison.OrdinalIgnoreCase))
            {
                if (picture is null)
                {
                    try
                    {
                        picture = ExtractFlacPicture(Convert.FromBase64String(value));
                    }
                    catch
                    {
                        // Ignore malformed base64 pictures.
                    }
                }
            }
            else if (value.Length > 0)
            {
                tags.TryAdd(key, value);
            }
        }
    }

    private static byte[]? ExtractFlacPicture(byte[] data)
    {
        var position = 0;
        if (!TryReadUInt32BigEndian(data, ref position, out _) ||
            !TryReadUInt32BigEndian(data, ref position, out var mimeLength) ||
            mimeLength > (uint)data.Length - (uint)position)
        {
            return null;
        }

        position += (int)mimeLength;
        if (!TryReadUInt32BigEndian(data, ref position, out var descriptionLength) ||
            descriptionLength > (uint)data.Length - (uint)position)
        {
            return null;
        }

        position += (int)descriptionLength;
        position += 16; // width, height, depth, color count
        if (position > data.Length ||
            !TryReadUInt32BigEndian(data, ref position, out var pictureLength) ||
            pictureLength > (uint)(data.Length - position))
        {
            return null;
        }

        return data.AsSpan(position, (int)pictureLength).ToArray();
    }

    private static EmbeddedMetadata? ReadMp3(Stream stream)
    {
        var header = new byte[10];
        if (!ReadExactly(stream, header) || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
        {
            return null;
        }

        var major = header[3];
        var tagSize = ReadSynchsafe(header, 6);
        if (tagSize <= 0 || tagSize > MaxTagBytes)
        {
            return null;
        }

        var tag = new byte[tagSize];
        if (!ReadExactly(stream, tag))
        {
            return null;
        }

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? picture = null;
        ParseId3Frames(tag, major, tags, ref picture);
        return Create(tags, TimeSpan.Zero, picture, stream);
    }

    private static void ParseId3Frames(
        byte[] data,
        int major,
        Dictionary<string, string> tags,
        ref byte[]? picture)
    {
        var position = 0;
        if (data.Length >= 10 &&
            data[0] == 'I' && data[1] == 'D' && data[2] == '3')
        {
            major = data[3];
            var size = ReadSynchsafe(data, 6);
            var flags = data[5];
            position = 10;
            if (size > 0 && size < data.Length)
            {
                // The ID3 chunk's declared size is authoritative.
                Array.Resize(ref data, Math.Min(data.Length, 10 + size));
            }

            if ((flags & 0x40) != 0 && position + 4 <= data.Length)
            {
                var extended = major == 4
                    ? ReadSynchsafe(data, position)
                    : (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
                position += Math.Max(4, extended);
            }
        }

        while (position + 10 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, position, 4);
            if (string.IsNullOrWhiteSpace(id) || id[0] == '\0')
            {
                break;
            }

            var size = major == 4
                ? ReadSynchsafe(data, position + 4)
                : (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 4, 4));
            if (size <= 0 || size > data.Length - position - 10)
            {
                break;
            }

            var frame = data.AsSpan(position + 10, size);
            position += 10 + size;

            if (id is "TIT2" or "TPE1" or "TALB" or "TPE2")
            {
                var text = DecodeId3Text(frame);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var key = id switch
                    {
                        "TIT2" => "TITLE",
                        "TPE1" => "ARTIST",
                        "TALB" => "ALBUM",
                        _ => "ALBUMARTIST"
                    };
                    tags.TryAdd(key, text);
                }
            }
            else if (id == "APIC")
            {
                picture ??= ExtractApic(frame);
            }
        }
    }

    private static string DecodeId3Text(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            return string.Empty;
        }

        var encoding = frame[0];
        var payload = frame[1..];
        var text = encoding switch
        {
            0 => Encoding.Latin1.GetString(payload),
            1 => Encoding.Unicode.GetString(payload),
            2 => Encoding.BigEndianUnicode.GetString(payload),
            3 => Encoding.UTF8.GetString(payload),
            _ => Encoding.UTF8.GetString(payload)
        };

        var nullIndex = text.IndexOf('\0');
        if (nullIndex >= 0)
        {
            text = text[..nullIndex];
        }

        return text.Trim('\uFEFF', ' ', '\0');
    }

    private static byte[]? ExtractApic(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            return null;
        }

        var encoding = frame[0];
        var position = 1;
        var mimeEnd = frame[position..].IndexOf((byte)0);
        if (mimeEnd < 0)
        {
            return null;
        }

        position += mimeEnd + 1;
        if (position >= frame.Length)
        {
            return null;
        }

        position++; // picture type
        var descriptionEnd = FindEncodedTerminator(frame, position, encoding);
        if (descriptionEnd < 0)
        {
            return null;
        }

        position = descriptionEnd;
        return position < frame.Length ? frame[position..].ToArray() : null;
    }

    private static int FindEncodedTerminator(ReadOnlySpan<byte> data, int start, byte encoding)
    {
        if (encoding is 1 or 2)
        {
            for (var i = start; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    return i + 2;
                }
            }
        }
        else
        {
            for (var i = start; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    return i + 1;
                }
            }
        }

        return -1;
    }

    private static EmbeddedMetadata? ReadWav(Stream stream)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? picture = null;
        uint byteRate = 0;
        long dataSize = 0;

        stream.Position = 12;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkHeader = new byte[8];
            if (!ReadExactly(stream, chunkHeader))
            {
                break;
            }

            var id = Encoding.ASCII.GetString(chunkHeader[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
            var next = stream.Position + size + (size & 1);

            if (id == "fmt ")
            {
                var data = new byte[Math.Min(size, 64u)];
                if (ReadExactly(stream, data) && data.Length >= 12)
                {
                    byteRate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
                }
            }
            else if (id == "data")
            {
                dataSize = size;
            }
            else if (id is "ID3 " or "id3 ")
            {
                if (size <= MaxTagBytes)
                {
                    var data = new byte[size];
                    if (ReadExactly(stream, data))
                    {
                        ParseId3Frames(data, 0, tags, ref picture);
                    }
                }
            }
            else if (id == "LIST" && size <= MaxTagBytes)
            {
                var data = new byte[size];
                if (ReadExactly(stream, data))
                {
                    ParseRiffInfo(data, tags);
                }
            }

            if (next > stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        var duration = byteRate > 0 && dataSize > 0
            ? TimeSpan.FromSeconds((double)dataSize / byteRate)
            : TimeSpan.Zero;
        if (byteRate > 0)
        {
            tags.TryAdd("BITRATE", (byteRate * 8).ToString());
        }

        return Create(tags, duration, picture, stream);
    }

    private static void ParseRiffInfo(byte[] data, Dictionary<string, string> tags)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "INFO")
        {
            return;
        }

        var position = 4;
        while (position + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4, 4));
            position += 8;
            if (size > data.Length - position)
            {
                break;
            }

            var value = Encoding.UTF8.GetString(data, position, (int)size).Trim('\0', ' ');
            if (value.Length > 0)
            {
                var key = id switch
                {
                    "INAM" => "TITLE",
                    "IART" => "ARTIST",
                    "IPRD" => "ALBUM",
                    _ => null
                };
                if (key is not null)
                {
                    tags.TryAdd(key, value);
                }
            }

            position += (int)size + (int)(size & 1);
        }
    }

    private static EmbeddedMetadata? Create(
        Dictionary<string, string> tags,
        TimeSpan duration,
        byte[]? picture,
        Stream stream)
    {
        tags.TryGetValue("TITLE", out var title);
        tags.TryGetValue("ARTIST", out var artist);
        tags.TryGetValue("ALBUM", out var album);
        tags.TryGetValue("ALBUMARTIST", out var albumArtist);

        uint bitrate = 0;
        if (duration > TimeSpan.Zero && stream.CanSeek && stream.Length > 0)
        {
            var bitsPerSecond = stream.Length * 8d / duration.TotalSeconds;
            if (bitsPerSecond > 0 && bitsPerSecond <= uint.MaxValue)
            {
                bitrate = (uint)Math.Round(bitsPerSecond);
            }
        }

        if (string.IsNullOrWhiteSpace(title) &&
            string.IsNullOrWhiteSpace(artist) &&
            string.IsNullOrWhiteSpace(album) &&
            picture is null)
        {
            return null;
        }

        return new EmbeddedMetadata(title, artist, album, albumArtist, duration, bitrate, picture);
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool TryReadUInt32LittleEndian(byte[] data, ref int position, out uint value)
    {
        if (position + 4 > data.Length)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position, 4));
        position += 4;
        return true;
    }

    private static bool TryReadUInt32BigEndian(byte[] data, ref int position, out uint value)
    {
        if (position + 4 > data.Length)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
        position += 4;
        return true;
    }

    private static int ReadSynchsafe(byte[] data, int offset)
    {
        if (offset + 4 > data.Length)
        {
            return 0;
        }

        return (data[offset] & 0x7F) << 21 |
               (data[offset + 1] & 0x7F) << 14 |
               (data[offset + 2] & 0x7F) << 7 |
               (data[offset + 3] & 0x7F);
    }
}
