using System.IO.Hashing;
using System.Text;

namespace ComfyGenReader.Core.Tests;

public static class ExifFixtures
{
    public static void WriteJpeg(string path, IDictionary<ushort, string> ifd0Ascii)
    {
        var tiff = BuildTiff(ifd0Ascii);
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8, 0xFF, 0xE1]);
        var length = (ushort)(2 + 6 + tiff.Length);
        ms.WriteByte((byte)(length >> 8));
        ms.WriteByte((byte)(length & 0xFF));
        ms.Write("Exif\0\0"u8);
        ms.Write(tiff);
        ms.Write([0xFF, 0xD9]);
        File.WriteAllBytes(path, ms.ToArray());
    }

    public static void WriteWebP(string path, IDictionary<ushort, string> ifd0Ascii)
    {
        var tiff = BuildTiff(ifd0Ascii);
        using var body = new MemoryStream();
        body.Write("WEBP"u8);
        body.Write("VP8X"u8);
        body.Write(BitConverter.GetBytes(10u));
        body.Write([0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0]); // EXIF flag, 1x1 canvas (width-1 = height-1 = 0)
        body.Write("EXIF"u8);
        body.Write(BitConverter.GetBytes((uint)tiff.Length));
        body.Write(tiff);
        if (tiff.Length % 2 != 0) body.WriteByte(0);
        using var file = new MemoryStream();
        file.Write("RIFF"u8);
        file.Write(BitConverter.GetBytes((uint)body.Length));
        file.Write(body.ToArray());
        File.WriteAllBytes(path, file.ToArray());
    }

    public static void WritePng(string path, IDictionary<string, string> textChunks)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        // 1x1, 8-bit grayscale
        WriteChunk(ms, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 0, 0, 0, 0]);
        var latin1 = Encoding.Latin1;
        foreach (var (keyword, text) in textChunks)
            WriteChunk(ms, "tEXt", latin1.GetBytes(keyword + "\0" + text));
        // zlib stream for one scanline: filter byte 0 + one pixel byte
        WriteChunk(ms, "IDAT", [0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]);
        WriteChunk(ms, "IEND", []);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(BigEndian((uint)data.Length));
        s.Write(typeBytes);
        s.Write(data);
        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        s.Write(BigEndian(crc.GetCurrentHashAsUInt32()));
    }

    private static byte[] BigEndian(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] BuildTiff(IDictionary<ushort, string> ascii)
    {
        var entries = ascii.OrderBy(e => e.Key).ToList();
        var ifdSize = 2 + entries.Count * 12 + 4;
        var dataOffset = 8 + ifdSize;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms); // little-endian
        w.Write("II*\0"u8);
        w.Write(8u);
        w.Write((ushort)entries.Count);
        var values = new MemoryStream();
        foreach (var (tag, text) in entries)
        {
            var bytes = Encoding.ASCII.GetBytes(text + "\0");
            w.Write(tag);
            w.Write((ushort)2);
            w.Write((uint)bytes.Length);
            if (bytes.Length <= 4)
            {
                w.Write(bytes);
                w.Write(new byte[4 - bytes.Length]);
            }
            else
            {
                w.Write((uint)(dataOffset + values.Length));
                values.Write(bytes);
                if (bytes.Length % 2 != 0) values.WriteByte(0);
            }
        }
        w.Write(0u);
        w.Write(values.ToArray());
        w.Flush();
        return ms.ToArray();
    }
}
