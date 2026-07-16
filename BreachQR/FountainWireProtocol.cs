using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BreachQR
{
    public sealed class FountainManifest
    {
        public string SessionId { get; init; }
        public int BlockCount { get; init; }
        public int BlockSize { get; init; }
        public long FileSize { get; init; }
        public string FileName { get; init; }
        public string FileHash { get; init; }

        public FountainPacket CreatePacket(ulong sequence, byte[] payload)
        {
            return new FountainPacket
            {
                SessionId = SessionId,
                Sequence = sequence,
                BlockCount = BlockCount,
                BlockSize = BlockSize,
                FileSize = FileSize,
                FileName = FileName,
                FileHash = FileHash,
                Payload = payload
            };
        }
    }

    public sealed class FountainDataFrame
    {
        public string SessionId { get; init; }
        public ulong Sequence { get; init; }
        public byte[] Payload { get; init; }
    }

    public static class FountainWireProtocol
    {
        private const byte ManifestFrameType = 1;
        private const byte DataFrameType = 2;
        private const int HeaderSize = 21;
        private static readonly byte[] Magic = { (byte)'B', (byte)'Q', (byte)'R', (byte)'3' };

        public static byte[] CreateManifestFrame(FountainEncoder encoder)
        {
            byte[] fileName = Encoding.UTF8.GetBytes(encoder.FileName);
            if (fileName.Length is < 1 or > 255)
            {
                throw new InvalidDataException("The UTF-8 file name must contain between 1 and 255 bytes.");
            }

            byte[] hash = Base64Url.Decode(encoder.FileHash);
            int length = HeaderSize + 4 + 2 + 8 + SHA256.HashSizeInBytes + 1 + fileName.Length + 4;
            byte[] frame = new byte[length];
            WriteHeader(frame, ManifestFrameType, encoder.SessionId);
            int offset = HeaderSize;
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset), encoder.BlockCount);
            offset += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset), checked((ushort)encoder.BlockSize));
            offset += 2;
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset), encoder.FileSize);
            offset += 8;
            hash.CopyTo(frame, offset);
            offset += hash.Length;
            frame[offset++] = checked((byte)fileName.Length);
            fileName.CopyTo(frame, offset);
            WriteCrc(frame);
            return frame;
        }

        public static byte[] CreateDataFrame(FountainPacket packet)
        {
            int length = HeaderSize + 8 + 2 + packet.Payload.Length + 4;
            byte[] frame = new byte[length];
            WriteHeader(frame, DataFrameType, packet.SessionId);
            int offset = HeaderSize;
            BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(offset), packet.Sequence);
            offset += 8;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset), checked((ushort)packet.Payload.Length));
            offset += 2;
            packet.Payload.CopyTo(frame, offset);
            WriteCrc(frame);
            return frame;
        }

        public static bool TryParseManifest(ReadOnlySpan<byte> frame, out FountainManifest manifest)
        {
            manifest = null;
            if (!TryReadHeader(frame, ManifestFrameType, out string sessionId) || !HasValidCrc(frame))
            {
                return false;
            }

            int minimumLength = HeaderSize + 4 + 2 + 8 + SHA256.HashSizeInBytes + 1 + 1 + 4;
            if (frame.Length < minimumLength)
            {
                return false;
            }

            int offset = HeaderSize;
            int blockCount = BinaryPrimitives.ReadInt32LittleEndian(frame[offset..]);
            offset += 4;
            int blockSize = BinaryPrimitives.ReadUInt16LittleEndian(frame[offset..]);
            offset += 2;
            long fileSize = BinaryPrimitives.ReadInt64LittleEndian(frame[offset..]);
            offset += 8;
            byte[] hash = frame.Slice(offset, SHA256.HashSizeInBytes).ToArray();
            offset += hash.Length;
            int fileNameLength = frame[offset++];
            if (fileNameLength < 1 || offset + fileNameLength + 4 != frame.Length)
            {
                return false;
            }

            string fileName;
            try
            {
                fileName = Path.GetFileName(new UTF8Encoding(false, true).GetString(frame.Slice(offset, fileNameLength)));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileName) || !HasValidMetadata(blockCount, blockSize, fileSize))
            {
                return false;
            }

            manifest = new FountainManifest
            {
                SessionId = sessionId,
                BlockCount = blockCount,
                BlockSize = blockSize,
                FileSize = fileSize,
                FileName = fileName,
                FileHash = Base64Url.Encode(hash)
            };
            return true;
        }

        public static bool TryParseData(ReadOnlySpan<byte> frame, out FountainDataFrame dataFrame)
        {
            dataFrame = null;
            if (!TryReadHeader(frame, DataFrameType, out string sessionId) ||
                !HasValidCrc(frame) ||
                frame.Length < HeaderSize + 8 + 2 + 1 + 4)
            {
                return false;
            }

            int offset = HeaderSize;
            ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(frame[offset..]);
            offset += 8;
            int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(frame[offset..]);
            offset += 2;
            if (payloadLength < 1 || payloadLength > TransferLimits.MaxBlockSize || offset + payloadLength + 4 != frame.Length)
            {
                return false;
            }

            dataFrame = new FountainDataFrame
            {
                SessionId = sessionId,
                Sequence = sequence,
                Payload = frame.Slice(offset, payloadLength).ToArray()
            };
            return true;
        }

        private static void WriteHeader(Span<byte> frame, byte frameType, string sessionId)
        {
            Magic.CopyTo(frame);
            frame[4] = frameType;
            Convert.FromHexString(sessionId).CopyTo(frame[5..HeaderSize]);
        }

        private static bool TryReadHeader(ReadOnlySpan<byte> frame, byte expectedType, out string sessionId)
        {
            sessionId = null;
            if (frame.Length < HeaderSize + 4 || !frame[..4].SequenceEqual(Magic) || frame[4] != expectedType)
            {
                return false;
            }

            sessionId = Convert.ToHexString(frame.Slice(5, 16));
            return true;
        }

        private static bool HasValidMetadata(int blockCount, int blockSize, long fileSize)
        {
            return blockCount is >= 1 and <= TransferLimits.MaxBlockCount &&
                   blockSize is >= 1 and <= TransferLimits.MaxBlockSize &&
                   fileSize is >= 0 and <= TransferLimits.MaxFileSize &&
                   blockCount == Math.Max(1, (int)((fileSize + blockSize - 1) / blockSize));
        }

        private static void WriteCrc(Span<byte> frame)
        {
            uint crc = Crc32.Compute(frame[..^4]);
            BinaryPrimitives.WriteUInt32LittleEndian(frame[^4..], crc);
        }

        private static bool HasValidCrc(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 4)
            {
                return false;
            }

            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(frame[^4..]);
            return Crc32.Compute(frame[..^4]) == expected;
        }
    }
}
