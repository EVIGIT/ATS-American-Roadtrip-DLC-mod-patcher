using System.IO.Compression;
using System.Text;

namespace ATSRoadTripConverter;

/// <summary>
/// Reader for SCS Software's native HashFS (.scs) archives, versions 1 and 2.
/// Base game files (def.scs, base.scs) and official DLC archives use this format.
/// </summary>
internal static class HashFsArchive
{
    public const uint Magic = 0x23534353; // "SCS#"

    private const ulong V2BlockSize = 16;
    private const int V2MetadataWordSize = 4;

    private const byte ChunkImage = 1;
    private const byte ChunkPlain = 128;
    private const byte ChunkDirectory = 129;

    private sealed record Entry(
        ulong Offset,
        uint CompressedSize,
        uint Size,
        bool IsCompressed,
        bool IsDirectory,
        bool IsImage);

    public sealed class ExtractResult
    {
        public int FilesExtracted;
        public List<string> SkippedFiles { get; } = new();
    }

    public static bool IsHashFs(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 4)
            return false;
        Span<byte> magic = stackalloc byte[4];
        stream.ReadExactly(magic);
        return BitConverter.ToUInt32(magic) == Magic;
    }

    public static ExtractResult Extract(
        string archivePath,
        string destination,
        Action<string>? log = null,
        Func<string, bool>? include = null)
    {
        using var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a HashFS archive.");

        var version = reader.ReadUInt16();
        var salt = reader.ReadUInt16();
        var hashMethod = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (hashMethod != "CITY")
            throw new NotSupportedException($"Unsupported HashFS hash method '{hashMethod}'.");

        Dictionary<ulong, List<Entry>> entries = version switch
        {
            1 => ReadV1Entries(reader),
            2 => ReadV2Entries(reader),
            _ => throw new NotSupportedException($"HashFS version {version} is not supported."),
        };

        log?.Invoke($"[ARCHIVE] HashFS v{version}, {entries.Count} entries.");

        Directory.CreateDirectory(destination);
        var fullDestination = Path.GetFullPath(destination);
        var result = new ExtractResult();

        if (!TryGet(entries, "", salt, out var root) || !root[0].IsDirectory)
        {
            throw new InvalidDataException(
                "This HashFS archive has no root directory listing, so its file names cannot be recovered. " +
                "Ask the mod author for an unprotected copy.");
        }

        var pending = new Stack<string>();
        pending.Push("");
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (!visited.Add(dir))
                continue;

            if (!TryGet(entries, dir, salt, out var dirEntries))
                continue;

            var (subdirs, files) = ReadListing(stream, reader, dirEntries, version);

            foreach (var sub in subdirs)
                pending.Push(Combine(dir, sub));

            foreach (var file in files)
            {
                var path = Combine(dir, file);
                if (include != null && !include(path))
                    continue;

                if (!TryGet(entries, path, salt, out var fileEntries))
                {
                    result.SkippedFiles.Add(path);
                    continue;
                }

                var entry = fileEntries[0];
                if (entry.IsDirectory)
                {
                    pending.Push(path);
                    continue;
                }

                if (entry.IsImage)
                {
                    result.SkippedFiles.Add(path);
                    log?.Invoke($"[ARCHIVE] Skipped packed texture (HashFS v2 image): {path}");
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(
                    fullDestination, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(fullDestination + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"Unsafe archive path: {path}");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, ReadContent(stream, reader, entry));
                result.FilesExtracted++;
            }
        }

        return result;
    }

    private static string Combine(string dir, string name) =>
        dir.Length == 0 ? name : dir + "/" + name;

    private static bool TryGet(
        Dictionary<ulong, List<Entry>> entries,
        string path,
        ushort salt,
        out List<Entry> entry)
    {
        var hashed = salt != 0 ? salt + path : path;
        var bytes = Encoding.UTF8.GetBytes(hashed);
        return entries.TryGetValue(CityHash.CityHash64(bytes, (ulong)bytes.Length), out entry!);
    }

    private static Dictionary<ulong, List<Entry>> ReadV1Entries(BinaryReader reader)
    {
        var count = reader.ReadUInt32();
        var start = reader.ReadUInt64();
        var stream = reader.BaseStream;

        if (start + count * 32UL > (ulong)stream.Length)
            start = (ulong)stream.Length - count * 32UL;

        stream.Position = (long)start;
        var entries = new Dictionary<ulong, List<Entry>>();

        for (var i = 0; i < count; i++)
        {
            var hash = reader.ReadUInt64();
            var offset = reader.ReadUInt64();
            var flags = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // crc
            var size = reader.ReadUInt32();
            var compressedSize = reader.ReadUInt32();

            var entry = new Entry(
                offset,
                compressedSize,
                size,
                IsCompressed: (flags & 0x2) != 0,
                IsDirectory: (flags & 0x1) != 0,
                IsImage: false);

            Add(entries, hash, entry);
        }

        return entries;
    }

    private static Dictionary<ulong, List<Entry>> ReadV2Entries(BinaryReader reader)
    {
        _ = reader.ReadUInt32(); // entry count
        var entryTableLength = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // metadata entry count
        var metadataTableLength = reader.ReadUInt32();
        var entryTableStart = reader.ReadUInt64();
        var metadataTableStart = reader.ReadUInt64();

        var stream = reader.BaseStream;

        stream.Position = (long)entryTableStart;
        var entryTable = Inflate(reader.ReadBytes((int)entryTableLength));

        stream.Position = (long)metadataTableStart;
        var metadataTable = Inflate(reader.ReadBytes((int)metadataTableLength));

        var entries = new Dictionary<ulong, List<Entry>>();

        for (var pos = 0; pos + 16 <= entryTable.Length; pos += 16)
        {
            var hash = BitConverter.ToUInt64(entryTable, pos);
            var metadataIndex = BitConverter.ToUInt32(entryTable, pos + 8);
            var metadataCount = BitConverter.ToUInt16(entryTable, pos + 12);

            if (metadataCount == 0)
                continue;

            var metaPos = (int)(metadataIndex * V2MetadataWordSize);
            var firstChunkType = metadataTable[metaPos + 3];
            var dataPos = metaPos + metadataCount * 4;

            switch (firstChunkType)
            {
                case ChunkPlain:
                case ChunkDirectory:
                {
                    var main = ReadMainMetadata(metadataTable, dataPos);
                    Add(entries, hash, main with { IsDirectory = firstChunkType == ChunkDirectory });
                    break;
                }

                case ChunkImage:
                {
                    // Packed tobj/dds metadata is 12 bytes, followed by the main metadata.
                    var main = ReadMainMetadata(metadataTable, dataPos + 12);
                    Add(entries, hash, main with { IsImage = true });
                    break;
                }
            }
        }

        return entries;
    }

    private static Entry ReadMainMetadata(byte[] table, int pos)
    {
        var compressedWord = BitConverter.ToUInt32(table, pos);
        var sizeWord = BitConverter.ToUInt32(table, pos + 4);
        var offsetBlock = BitConverter.ToUInt32(table, pos + 12);

        return new Entry(
            Offset: offsetBlock * V2BlockSize,
            CompressedSize: compressedWord & 0x0FFFFFFF,
            Size: sizeWord & 0x0FFFFFFF,
            IsCompressed: (compressedWord & 0x10000000) != 0,
            IsDirectory: false,
            IsImage: false);
    }

    private static void Add(Dictionary<ulong, List<Entry>> entries, ulong hash, Entry entry)
    {
        if (!entries.TryGetValue(hash, out var list))
            entries[hash] = list = new List<Entry>();
        list.Add(entry);
    }

    private static (List<string> Subdirs, List<string> Files) ReadListing(
        Stream stream,
        BinaryReader reader,
        List<Entry> dirEntries,
        int version)
    {
        var subdirs = new List<string>();
        var files = new List<string>();

        foreach (var entry in dirEntries.Where(e => e.IsDirectory))
        {
            var content = ReadContent(stream, reader, entry);

            if (version == 1)
            {
                foreach (var line in Encoding.UTF8.GetString(content)
                             .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith('*'))
                        subdirs.Add(line[1..]);
                    else
                        files.Add(line);
                }
            }
            else
            {
                var count = BitConverter.ToInt32(content, 0);
                var nameOffset = 4 + count;
                for (var i = 0; i < count; i++)
                {
                    var length = content[4 + i];
                    var name = Encoding.UTF8.GetString(content, nameOffset, length);
                    nameOffset += length;

                    if (name.StartsWith('/'))
                        subdirs.Add(name[1..]);
                    else
                        files.Add(name);
                }
            }
        }

        return (subdirs, files);
    }

    private static byte[] ReadContent(Stream stream, BinaryReader reader, Entry entry)
    {
        if (entry.Size == 0)
            return Array.Empty<byte>();

        stream.Position = (long)entry.Offset;

        if (!entry.IsCompressed)
            return reader.ReadBytes((int)entry.Size);

        return Inflate(reader.ReadBytes((int)entry.CompressedSize));
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
