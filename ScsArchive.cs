using System.IO.Compression;
using System.Text;

namespace ATSRoadTripConverter;

internal static class ScsArchive
{
    private const uint LocalHeader = 0x04034b50;
    private const uint CentralHeader = 0x02014b50;
    private const uint EndOfCentralDirectory = 0x06054b50;

    public static void Extract(
        string archivePath,
        string destination,
        Action<string>? log = null,
        Func<string, bool>? include = null)
    {
        if (HashFsArchive.IsHashFs(archivePath))
        {
            var result = HashFsArchive.Extract(archivePath, destination, log, include);
            log?.Invoke($"[ARCHIVE] Extracted {result.FilesExtracted} file(s) from HashFS archive.");
            if (result.SkippedFiles.Count > 0)
                log?.Invoke($"[WARNING] {result.SkippedFiles.Count} file(s) could not be extracted from the HashFS archive (packed textures or missing entries).");
            return;
        }

        ExtractZip(archivePath, destination, log, include);
    }

    private static void ExtractZip(
        string archivePath,
        string destination,
        Action<string>? log,
        Func<string, bool>? include)
    {
        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var eocdOffset = FindEndOfCentralDirectory(stream);
        stream.Position = eocdOffset;

        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8,
            leaveOpen: true);

        if (reader.ReadUInt32() != EndOfCentralDirectory)
            throw new InvalidDataException(
                "Invalid ZIP end-of-central-directory record.");

        _ = reader.ReadUInt16(); // disk number
        _ = reader.ReadUInt16(); // central directory disk

        var entriesThisDisk = reader.ReadUInt16();
        var totalEntries = reader.ReadUInt16();
        var centralSize = reader.ReadUInt32();
        var centralOffset = reader.ReadUInt32();
        var commentLength = reader.ReadUInt16();

        if (entriesThisDisk != totalEntries)
            throw new InvalidDataException(
                "Multi-disk SCS archives are not supported.");

        if (totalEntries == ushort.MaxValue ||
            centralSize == uint.MaxValue ||
            centralOffset == uint.MaxValue)
        {
            throw new InvalidDataException(
                "ZIP64 archives are not supported by this SCS reader yet.");
        }

        if ((long)centralOffset + centralSize > stream.Length)
            throw new InvalidDataException(
                "The archive central directory points outside the file.");

        _ = commentLength;

        Directory.CreateDirectory(destination);
        var fullDestination = Path.GetFullPath(destination);
        var basePath = fullDestination.TrimEnd(
                           Path.DirectorySeparatorChar,
                           Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;

        stream.Position = centralOffset;

        for (var index = 0; index < totalEntries; index++)
        {
            var centralEntryStart = stream.Position;

            if (reader.ReadUInt32() != CentralHeader)
                throw new InvalidDataException(
                    $"Invalid central-directory entry at index {index}.");

            _ = reader.ReadUInt16(); // version made by
            _ = reader.ReadUInt16(); // version needed

            var flags = reader.ReadUInt16();
            var method = reader.ReadUInt16();

            _ = reader.ReadUInt16(); // mod time
            _ = reader.ReadUInt16(); // mod date
            _ = reader.ReadUInt32(); // CRC
            var compressedSize = reader.ReadUInt32();
            var uncompressedSize = reader.ReadUInt32();
            var nameLength = reader.ReadUInt16();
            var extraLength = reader.ReadUInt16();
            var commentLen = reader.ReadUInt16();

            _ = reader.ReadUInt16(); // disk number
            _ = reader.ReadUInt16(); // internal attributes
            _ = reader.ReadUInt32(); // external attributes
            var localHeaderOffset = reader.ReadUInt32();

            var nameBytes = reader.ReadBytes(nameLength);
            _ = reader.ReadBytes(extraLength);
            _ = reader.ReadBytes(commentLen);

            var entryName = DecodeEntryName(nameBytes, flags)
                .Replace('\\', '/');

            if (string.IsNullOrWhiteSpace(entryName) ||
                entryName.StartsWith("/", StringComparison.Ordinal) ||
                entryName.Contains(":/", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unsafe archive path: {entryName}");
            }

            var normalized = entryName.TrimStart('/');
            var target = Path.GetFullPath(
                Path.Combine(
                    fullDestination,
                    normalized.Replace('/',
                        Path.DirectorySeparatorChar)));

            if (!target.StartsWith(
                    basePath,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    target.TrimEnd(Path.DirectorySeparatorChar),
                    fullDestination,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsafe archive path: {entryName}");
            }

            var isDirectoryEntry = entryName.EndsWith("/", StringComparison.Ordinal);
            if (isDirectoryEntry || (include != null && !include(normalized)))
            {
                if (isDirectoryEntry && include == null)
                    Directory.CreateDirectory(target);
                stream.Position =
                    centralEntryStart +
                    46L +
                    nameLength +
                    extraLength +
                    commentLen;
                continue;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)!);

            var dataOffset = GetLocalDataOffset(
                stream,
                localHeaderOffset,
                entryName);

            if (dataOffset < 0 ||
                dataOffset + compressedSize > stream.Length)
            {
                throw new InvalidDataException(
                    $"Invalid data range for archive entry: {entryName}");
            }

            stream.Position = dataOffset;

            using var limited = new LimitedReadStream(
                stream,
                compressedSize);

            using var output = new FileStream(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            switch (method)
            {
                case 0:
                    limited.CopyTo(output);
                    break;

                case 8:
                    using (var deflate = new DeflateStream(
                               limited,
                               CompressionMode.Decompress))
                    {
                        deflate.CopyTo(output);
                    }
                    break;

                default:
                    throw new InvalidDataException(
                        $"Unsupported ZIP compression method {method} for '{entryName}'.");
            }

            if (uncompressedSize != 0 &&
                output.Length != uncompressedSize)
            {
                throw new InvalidDataException(
                    $"Extracted size mismatch for '{entryName}'. " +
                    $"Expected {uncompressedSize} bytes, got {output.Length}.");
            }

            // SCS mod archives sometimes have inconsistent encryption flags or
            // local-header compression values. The central-directory method/sizes
            // are the values used here because they describe the actual payload.
            if ((flags & 0x0001) != 0)
                log?.Invoke("[ARCHIVE] Ignoring nonstandard encrypted flag for " + entryName);

            stream.Position =
                centralEntryStart +
                46L +
                nameLength +
                extraLength +
                commentLen;
        }
    }

    private static long GetLocalDataOffset(
        FileStream stream,
        uint localHeaderOffset,
        string entryName)
    {
        stream.Position = localHeaderOffset;

        using var reader = new BinaryReader(
            stream,
            Encoding.UTF8,
            leaveOpen: true);

        if (reader.ReadUInt32() != LocalHeader)
            throw new InvalidDataException(
                $"Invalid local header for archive entry: {entryName}");

        _ = reader.ReadUInt16(); // version needed
        _ = reader.ReadUInt16(); // flags
        _ = reader.ReadUInt16(); // local compression method
        _ = reader.ReadUInt16(); // mod time
        _ = reader.ReadUInt16(); // mod date
        _ = reader.ReadUInt32(); // CRC
        _ = reader.ReadUInt32(); // compressed size
        _ = reader.ReadUInt32(); // uncompressed size

        var localNameLength = reader.ReadUInt16();
        var localExtraLength = reader.ReadUInt16();

        _ = reader.ReadBytes(localNameLength);
        _ = reader.ReadBytes(localExtraLength);

        return stream.Position;
    }

    private static string DecodeEntryName(
        byte[] bytes,
        ushort flags)
    {
        if ((flags & 0x0800) != 0)
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }

        // ATS mod paths are overwhelmingly ASCII. ASCII also avoids adding
        // a separate System.Text.Encoding.CodePages dependency to the project.
        return Encoding.ASCII.GetString(bytes);
    }

    private static long FindEndOfCentralDirectory(
        FileStream stream)
    {
        const int eocdSize = 22;
        const int maxCommentLength = ushort.MaxValue;

        var scanSize = (int)Math.Min(
            stream.Length,
            eocdSize + maxCommentLength);

        var buffer = new byte[scanSize];
        stream.Position = stream.Length - scanSize;

        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(
                buffer,
                read,
                buffer.Length - read);

            if (count == 0)
                break;

            read += count;
        }

        for (var i = read - eocdSize; i >= 0; i--)
        {
            if (buffer[i] != 0x50 ||
                buffer[i + 1] != 0x4b ||
                buffer[i + 2] != 0x05 ||
                buffer[i + 3] != 0x06)
            {
                continue;
            }

            var absolute = stream.Length - scanSize + i;
            var commentLength =
                BitConverter.ToUInt16(buffer, i + 20);

            if (absolute + eocdSize + commentLength <=
                stream.Length)
            {
                return absolute;
            }
        }

        throw new InvalidDataException(
            "No ZIP end-of-central-directory record was found. " +
            "The selected file is not a compatible SCS/ZIP archive.");
    }

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LimitedReadStream(
            Stream inner,
            long length)
        {
            _inner = inner;
            _remaining = length;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            if (_remaining <= 0)
                return 0;

            count = (int)Math.Min(
                count,
                _remaining);

            var read = _inner.Read(
                buffer,
                offset,
                count);

            _remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0)
                return 0;

            var count = (int)Math.Min(
                buffer.Length,
                _remaining);

            var read = _inner.Read(
                buffer[..count]);

            _remaining -= read;
            return read;
        }

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
