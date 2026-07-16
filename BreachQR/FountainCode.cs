using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BreachQR
{
    public sealed class FountainPacket
    {
        private const string ProtocolPrefix = "BQR2";

        public string SessionId { get; init; }
        public ulong Sequence { get; init; }
        public int BlockCount { get; init; }
        public int BlockSize { get; init; }
        public long FileSize { get; init; }
        public string FileName { get; init; }
        public string FileHash { get; init; }
        public byte[] Payload { get; init; }

        public string Serialize()
        {
            string encodedName = Base64Url.Encode(Encoding.UTF8.GetBytes(FileName));
            string encodedPayload = Convert.ToBase64String(Payload);
            return string.Join("|",
                ProtocolPrefix,
                SessionId,
                Sequence.ToString("X"),
                BlockCount,
                BlockSize,
                FileSize,
                encodedName,
                FileHash,
                Crc32.Compute(Payload).ToString("X8"),
                encodedPayload);
        }

        public static bool TryParse(string text, out FountainPacket packet)
        {
            packet = null;
            string[] parts = text?.Split('|');
            if (parts == null || parts.Length != 10 || parts[0] != ProtocolPrefix)
            {
                return false;
            }

            try
            {
                if (parts[1].Length != 32 ||
                    !ulong.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out ulong sequence) ||
                    !int.TryParse(parts[3], out int blockCount) ||
                    !int.TryParse(parts[4], out int blockSize) ||
                    !long.TryParse(parts[5], out long fileSize) ||
                    !uint.TryParse(parts[8], System.Globalization.NumberStyles.HexNumber, null, out uint expectedCrc) ||
                    blockCount < 1 || blockCount > 1_000_000 ||
                    blockSize < 1 || blockSize > 4096 ||
                    fileSize < 0 || fileSize > (long)blockCount * blockSize ||
                    blockCount != Math.Max(1, (int)((fileSize + blockSize - 1) / blockSize)))
                {
                    return false;
                }

                if (Convert.FromHexString(parts[1]).Length != 16)
                {
                    return false;
                }

                string fileName = Path.GetFileName(Encoding.UTF8.GetString(Base64Url.Decode(parts[6])));
                byte[] payload = Convert.FromBase64String(parts[9]);
                if (string.IsNullOrWhiteSpace(fileName) || payload.Length != blockSize || Crc32.Compute(payload) != expectedCrc)
                {
                    return false;
                }

                byte[] hash = Base64Url.Decode(parts[7]);
                if (hash.Length != SHA256.HashSizeInBytes)
                {
                    return false;
                }

                packet = new FountainPacket
                {
                    SessionId = parts[1],
                    Sequence = sequence,
                    BlockCount = blockCount,
                    BlockSize = blockSize,
                    FileSize = fileSize,
                    FileName = fileName,
                    FileHash = parts[7],
                    Payload = payload
                };
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    public sealed class FountainEncoder
    {
        public const int DefaultBlockSize = 1200;

        private readonly byte[][] blocks;

        public FountainEncoder(byte[] data, string fileName, int blockSize = DefaultBlockSize, string sessionId = null)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (blockSize < 1 || blockSize > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSize));
            }

            FileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(FileName))
            {
                throw new ArgumentException("A file name is required.", nameof(fileName));
            }

            FileSize = data.LongLength;
            BlockSize = blockSize;
            BlockCount = Math.Max(1, (int)((FileSize + blockSize - 1) / blockSize));
            FileHash = Base64Url.Encode(SHA256.HashData(data));
            SessionId = sessionId ?? Guid.NewGuid().ToString("N");
            if (SessionId.Length != 32 || Convert.FromHexString(SessionId).Length != 16)
            {
                throw new ArgumentException("Session ID must contain 32 characters.", nameof(sessionId));
            }

            blocks = new byte[BlockCount][];
            for (int index = 0; index < BlockCount; index++)
            {
                blocks[index] = new byte[BlockSize];
                int sourceOffset = index * BlockSize;
                int length = Math.Min(BlockSize, data.Length - sourceOffset);
                if (length > 0)
                {
                    Buffer.BlockCopy(data, sourceOffset, blocks[index], 0, length);
                }
            }
        }

        public string SessionId { get; }
        public int BlockCount { get; }
        public int BlockSize { get; }
        public long FileSize { get; }
        public string FileName { get; }
        public string FileHash { get; }

        public static FountainEncoder FromFile(string path, int blockSize = DefaultBlockSize)
        {
            return new FountainEncoder(File.ReadAllBytes(path), Path.GetFileName(path), blockSize);
        }

        public FountainPacket CreatePacket(ulong sequence)
        {
            int[] sourceIndices = FountainSampler.GetSourceIndices(BlockCount, SessionId, sequence);
            byte[] payload = new byte[BlockSize];
            foreach (int index in sourceIndices)
            {
                Xor(payload, blocks[index]);
            }

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

        internal static void Xor(byte[] target, byte[] source)
        {
            for (int index = 0; index < target.Length; index++)
            {
                target[index] ^= source[index];
            }
        }
    }

    public sealed class FountainDecoder
    {
        private readonly HashSet<ulong> receivedSequences = new();
        private readonly Dictionary<int, byte[]> solvedBlocks = new();
        private readonly List<Equation> equations = new();

        public string SessionId { get; private set; }
        public int BlockCount { get; private set; }
        public int BlockSize { get; private set; }
        public long FileSize { get; private set; }
        public string FileName { get; private set; }
        public string FileHash { get; private set; }
        public int ReceivedPacketCount => receivedSequences.Count;
        public int SolvedBlockCount => solvedBlocks.Count;
        public bool IsComplete => BlockCount > 0 && SolvedBlockCount == BlockCount;

        public bool AddPacket(FountainPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (SessionId == null)
            {
                Initialize(packet);
            }
            else if (!HasMatchingMetadata(packet))
            {
                return false;
            }

            if (!receivedSequences.Add(packet.Sequence))
            {
                return false;
            }

            var indices = new HashSet<int>(FountainSampler.GetSourceIndices(BlockCount, SessionId, packet.Sequence));
            byte[] payload = (byte[])packet.Payload.Clone();
            foreach (int index in indices.ToArray())
            {
                if (solvedBlocks.TryGetValue(index, out byte[] solved))
                {
                    FountainEncoder.Xor(payload, solved);
                    indices.Remove(index);
                }
            }

            if (indices.Count > 0)
            {
                equations.Add(new Equation(indices, payload));
                Peel();
                TryGaussianSolve();
            }

            return true;
        }

        public byte[] GetDecodedData()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("The fountain stream is not complete.");
            }

            byte[] result = new byte[checked((int)FileSize)];
            for (int index = 0; index < BlockCount; index++)
            {
                int destinationOffset = index * BlockSize;
                int length = Math.Min(BlockSize, result.Length - destinationOffset);
                if (length > 0)
                {
                    Buffer.BlockCopy(solvedBlocks[index], 0, result, destinationOffset, length);
                }
            }

            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(result), Base64Url.Decode(FileHash)))
            {
                throw new InvalidDataException("Decoded file hash does not match the sender.");
            }

            return result;
        }

        private void Initialize(FountainPacket packet)
        {
            SessionId = packet.SessionId;
            BlockCount = packet.BlockCount;
            BlockSize = packet.BlockSize;
            FileSize = packet.FileSize;
            FileName = packet.FileName;
            FileHash = packet.FileHash;
        }

        private bool HasMatchingMetadata(FountainPacket packet)
        {
            return packet.SessionId == SessionId &&
                   packet.BlockCount == BlockCount &&
                   packet.BlockSize == BlockSize &&
                   packet.FileSize == FileSize &&
                   packet.FileName == FileName &&
                   packet.FileHash == FileHash;
        }

        private void Peel()
        {
            while (true)
            {
                int equationIndex = equations.FindIndex(equation => equation.Indices.Count == 1);
                if (equationIndex < 0)
                {
                    return;
                }

                Equation equation = equations[equationIndex];
                equations.RemoveAt(equationIndex);
                int solvedIndex = equation.Indices.First();
                if (solvedBlocks.ContainsKey(solvedIndex))
                {
                    continue;
                }

                solvedBlocks.Add(solvedIndex, equation.Payload);
                for (int index = equations.Count - 1; index >= 0; index--)
                {
                    Equation pending = equations[index];
                    if (!pending.Indices.Remove(solvedIndex))
                    {
                        continue;
                    }

                    FountainEncoder.Xor(pending.Payload, equation.Payload);
                    if (pending.Indices.Count == 0)
                    {
                        equations.RemoveAt(index);
                    }
                }
            }
        }

        private void TryGaussianSolve()
        {
            int unsolvedCount = BlockCount - solvedBlocks.Count;
            if (unsolvedCount == 0 || equations.Count < unsolvedCount)
            {
                return;
            }

            var pivots = new Dictionary<int, Equation>();
            foreach (Equation source in equations)
            {
                var row = new Equation(new HashSet<int>(source.Indices), (byte[])source.Payload.Clone());
                while (row.Indices.Count > 0)
                {
                    int pivot = row.Indices.Min();
                    if (!pivots.TryGetValue(pivot, out Equation existing))
                    {
                        pivots.Add(pivot, row);
                        break;
                    }

                    row.Indices.SymmetricExceptWith(existing.Indices);
                    FountainEncoder.Xor(row.Payload, existing.Payload);
                }
            }

            if (pivots.Count != unsolvedCount)
            {
                return;
            }

            foreach ((int pivot, Equation row) in pivots.OrderByDescending(item => item.Key))
            {
                foreach (int index in row.Indices.Where(index => index != pivot))
                {
                    if (!solvedBlocks.TryGetValue(index, out byte[] solved))
                    {
                        return;
                    }

                    FountainEncoder.Xor(row.Payload, solved);
                }

                solvedBlocks[pivot] = row.Payload;
            }

            equations.Clear();
        }

        private sealed class Equation
        {
            public Equation(HashSet<int> indices, byte[] payload)
            {
                Indices = indices;
                Payload = payload;
            }

            public HashSet<int> Indices { get; }
            public byte[] Payload { get; }
        }
    }

    internal static class FountainSampler
    {
        private static readonly ConcurrentDictionary<int, double[]> DegreeDistributions = new();

        public static int[] GetSourceIndices(int blockCount, string sessionId, ulong sequence)
        {
            if (sequence < (ulong)blockCount)
            {
                return new[] { (int)sequence };
            }

            ulong sessionSeed = BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(sessionId.AsSpan(0, 16)));
            var random = new SplitMix64(sessionSeed ^ sequence ^ 0x9E3779B97F4A7C15UL);
            int degree = SampleDegree(blockCount, random.NextDouble());
            var indices = new HashSet<int>();
            while (indices.Count < degree)
            {
                indices.Add((int)(random.NextUInt64() % (ulong)blockCount));
            }

            return indices.OrderBy(index => index).ToArray();
        }

        private static int SampleDegree(int blockCount, double value)
        {
            if (blockCount == 1)
            {
                return 1;
            }

            double[] cumulative = DegreeDistributions.GetOrAdd(blockCount, CreateRobustSolitonDistribution);
            int index = Array.BinarySearch(cumulative, value);
            if (index < 0)
            {
                index = ~index;
            }

            return Math.Min(index + 1, blockCount);
        }

        private static double[] CreateRobustSolitonDistribution(int blockCount)
        {
            const double c = 0.1;
            const double delta = 0.05;
            double r = c * Math.Log(blockCount / delta) * Math.Sqrt(blockCount);
            int pivot = Math.Clamp((int)Math.Floor(blockCount / r), 1, blockCount);
            var probabilities = new double[blockCount];

            probabilities[0] = 1.0 / blockCount;
            for (int degree = 2; degree <= blockCount; degree++)
            {
                probabilities[degree - 1] = 1.0 / (degree * (degree - 1.0));
            }

            for (int degree = 1; degree < pivot; degree++)
            {
                probabilities[degree - 1] += r / (degree * blockCount);
            }

            probabilities[pivot - 1] += r * Math.Log(r / delta) / blockCount;
            double normalizer = probabilities.Sum();
            var cumulative = new double[blockCount];
            double sum = 0;
            for (int index = 0; index < blockCount; index++)
            {
                sum += probabilities[index] / normalizer;
                cumulative[index] = sum;
            }

            cumulative[^1] = 1.0;
            return cumulative;
        }

        private sealed class SplitMix64
        {
            private ulong state;

            public SplitMix64(ulong seed)
            {
                state = seed;
            }

            public ulong NextUInt64()
            {
                ulong value = state += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }

            public double NextDouble()
            {
                return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
            }
        }
    }

    internal static class Base64Url
    {
        public static string Encode(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] Decode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }

    internal static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(byte[] data)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in data)
            {
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320U ^ (value >> 1) : value >> 1;
                }

                table[index] = value;
            }

            return table;
        }
    }
}
