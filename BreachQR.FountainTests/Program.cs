using BreachQR;
using Net.Codecrete.QrCodeGenerator;
using ZXing;

RunLossSimulations();
RunManifestAfterDataSimulation();
RunInterleavedSessionSimulation();
RunCorruptionChecks();
RunLimitChecks();
RunQrBinaryRoundTrip();
Console.WriteLine("All fountain protocol simulations passed.");

static void RunLossSimulations()
{
    for (int trial = 0; trial < 25; trial++)
    {
        var random = new Random(1000 + trial);
        byte[] original = new byte[random.Next(1, 180_000)];
        random.NextBytes(original);

        var encoder = new FountainEncoder(original, $"trial-{trial}.bin", 1200, trial.ToString("X32"));
        var receiver = new FountainSessionReceiver();
        DateTime now = DateTime.UnixEpoch;
        receiver.AddFrame(FountainWireProtocol.CreateManifestFrame(encoder), now);

        ulong sequence = (ulong)(encoder.BlockCount + random.Next(0, 100));
        int accepted = 0;
        int packetLimit = encoder.BlockCount * 8 + 100;
        byte[] completed = null;
        while (completed == null && accepted < packetLimit)
        {
            FountainPacket packet = encoder.CreatePacket(sequence++);
            if (random.NextDouble() < 0.38)
            {
                continue;
            }

            FountainReceiveUpdate update = receiver.AddFrame(FountainWireProtocol.CreateDataFrame(packet), now);
            completed = update?.CompletedData;
            accepted++;
        }

        Assert(completed != null && original.SequenceEqual(completed),
            $"Loss simulation {trial} failed after {accepted} packets.");
    }
}

static void RunManifestAfterDataSimulation()
{
    byte[] original = CreateData(45_000, 7);
    var encoder = new FountainEncoder(original, "late-manifest.bin", sessionId: new string('A', 32));
    var receiver = new FountainSessionReceiver();
    DateTime now = DateTime.UnixEpoch;

    int preManifestPackets = Math.Min(TransferLimits.MaxPendingPacketsPerSession, encoder.BlockCount + 20);
    for (ulong sequence = 0; sequence < (ulong)preManifestPackets; sequence++)
    {
        receiver.AddFrame(FountainWireProtocol.CreateDataFrame(encoder.CreatePacket(sequence)), now);
    }

    FountainReceiveUpdate update = receiver.AddFrame(FountainWireProtocol.CreateManifestFrame(encoder), now);
    ulong repairSequence = (ulong)preManifestPackets;
    while (update?.CompletedData == null)
    {
        update = receiver.AddFrame(FountainWireProtocol.CreateDataFrame(encoder.CreatePacket(repairSequence++)), now);
    }

    Assert(original.SequenceEqual(update.CompletedData), "Manifest-after-data recovery failed.");
}

static void RunInterleavedSessionSimulation()
{
    byte[] first = CreateData(72_000, 11);
    byte[] second = CreateData(66_000, 12);
    var firstEncoder = new FountainEncoder(first, "first.bin", sessionId: new string('B', 32));
    var secondEncoder = new FountainEncoder(second, "second.bin", sessionId: new string('C', 32));
    var receiver = new FountainSessionReceiver();
    DateTime now = DateTime.UnixEpoch;
    receiver.AddFrame(FountainWireProtocol.CreateManifestFrame(firstEncoder), now);
    receiver.AddFrame(FountainWireProtocol.CreateManifestFrame(secondEncoder), now);

    byte[] firstCompleted = null;
    byte[] secondCompleted = null;
    for (ulong sequence = 0; firstCompleted == null || secondCompleted == null; sequence++)
    {
        if (firstCompleted == null)
        {
            FountainReceiveUpdate update = receiver.AddFrame(
                FountainWireProtocol.CreateDataFrame(firstEncoder.CreatePacket(sequence)), now);
            firstCompleted = update?.CompletedData;
        }

        if (secondCompleted == null)
        {
            FountainReceiveUpdate update = receiver.AddFrame(
                FountainWireProtocol.CreateDataFrame(secondEncoder.CreatePacket(sequence)), now);
            secondCompleted = update?.CompletedData;
        }
    }

    Assert(first.SequenceEqual(firstCompleted), "First interleaved session failed.");
    Assert(second.SequenceEqual(secondCompleted), "Second interleaved session failed.");
}

static void RunCorruptionChecks()
{
    var encoder = new FountainEncoder(CreateData(4096, 21), "crc.bin", sessionId: new string('D', 32));
    byte[] manifest = FountainWireProtocol.CreateManifestFrame(encoder);
    manifest[10] ^= 0x55;
    Assert(!FountainWireProtocol.TryParseManifest(manifest, out _), "Corrupted manifest was accepted.");

    byte[] data = FountainWireProtocol.CreateDataFrame(encoder.CreatePacket(0));
    data[^5] ^= 0x55;
    Assert(!FountainWireProtocol.TryParseData(data, out _), "Corrupted data frame was accepted.");
}

static void RunLimitChecks()
{
    AssertThrows<ArgumentOutOfRangeException>(() =>
        new FountainEncoder(new byte[TransferLimits.MaxFileSize + 1], "too-large.bin"));

    var receiver = new FountainSessionReceiver();
    DateTime now = DateTime.UnixEpoch;
    for (int index = 0; index < TransferLimits.MaxConcurrentSessions + 2; index++)
    {
        string sessionId = index.ToString("X32");
        var encoder = new FountainEncoder(new byte[] { (byte)index }, $"{index}.bin", sessionId: sessionId);
        receiver.AddFrame(FountainWireProtocol.CreateManifestFrame(encoder), now.AddSeconds(index));
    }

    Assert(receiver.ActiveSessionCount == TransferLimits.MaxConcurrentSessions, "Session limit was not enforced.");
}

static void RunQrBinaryRoundTrip()
{
    var encoder = new FountainEncoder(CreateData(4096, 31), "qr-roundtrip.bin", sessionId: new string('E', 32));
    byte[] expected = FountainWireProtocol.CreateDataFrame(encoder.CreatePacket(17));
    QrCode qrCode = QrCode.EncodeBinary(expected, QrCode.Ecc.Low);

    const int moduleSize = 5;
    const int border = 4;
    int imageSize = (qrCode.Size + border * 2) * moduleSize;
    byte[] rgb = Enumerable.Repeat((byte)255, imageSize * imageSize * 3).ToArray();
    for (int y = 0; y < qrCode.Size; y++)
    {
        for (int x = 0; x < qrCode.Size; x++)
        {
            if (!qrCode.GetModule(x, y))
            {
                continue;
            }

            int startX = (x + border) * moduleSize;
            int startY = (y + border) * moduleSize;
            for (int pixelY = 0; pixelY < moduleSize; pixelY++)
            {
                for (int pixelX = 0; pixelX < moduleSize; pixelX++)
                {
                    int offset = ((startY + pixelY) * imageSize + startX + pixelX) * 3;
                    rgb[offset] = 0;
                    rgb[offset + 1] = 0;
                    rgb[offset + 2] = 0;
                }
            }
        }
    }

    var reader = new BarcodeReaderGeneric();
    Result result = reader.Decode(rgb, imageSize, imageSize, RGBLuminanceSource.BitmapFormat.RGB24);
    Assert(QrBinaryPayload.TryExtract(result, out byte[] actual), "QR byte segment was not extracted.");
    Assert(expected.SequenceEqual(actual), "QR binary payload changed during encode/decode.");
    Assert(!expected.SequenceEqual(result.RawBytes), "Test assumption failed: RawBytes unexpectedly matched the byte segment.");
}

static byte[] CreateData(int length, int seed)
{
    var random = new Random(seed);
    byte[] data = new byte[length];
    random.NextBytes(data);
    return data;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
