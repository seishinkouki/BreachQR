using BreachQR;

for (int trial = 0; trial < 25; trial++)
{
    var random = new Random(1000 + trial);
    byte[] original = new byte[random.Next(1, 180_000)];
    random.NextBytes(original);

    var encoder = new FountainEncoder(original, $"trial-{trial}.bin", 1200, trial.ToString("X32"));
    var decoder = new FountainDecoder();

    // Begin after every systematic packet and randomly drop 38% of all repair packets.
    ulong sequence = (ulong)(encoder.BlockCount + random.Next(0, 100));
    int accepted = 0;
    int packetLimit = encoder.BlockCount * 8 + 100;
    while (!decoder.IsComplete && accepted < packetLimit)
    {
        FountainPacket packet = encoder.CreatePacket(sequence++);
        if (random.NextDouble() < 0.38)
        {
            continue;
        }

        if (!FountainPacket.TryParse(packet.Serialize(), out FountainPacket parsed))
        {
            throw new InvalidOperationException($"Protocol parse failed in trial {trial}.");
        }

        decoder.AddPacket(parsed);
        accepted++;
    }

    if (!decoder.IsComplete || !original.SequenceEqual(decoder.GetDecodedData()))
    {
        throw new InvalidOperationException(
            $"Trial {trial} failed: recovered {decoder.SolvedBlockCount}/{encoder.BlockCount} blocks from {accepted} packets.");
    }

    Console.WriteLine($"trial {trial:D2}: {encoder.BlockCount} blocks recovered from {accepted} repair packets");
}

var emptyEncoder = new FountainEncoder(Array.Empty<byte>(), "empty.bin", sessionId: new string('F', 32));
var emptyDecoder = new FountainDecoder();
emptyDecoder.AddPacket(emptyEncoder.CreatePacket(7));
if (!emptyDecoder.IsComplete || emptyDecoder.GetDecodedData().Length != 0)
{
    throw new InvalidOperationException("Empty-file recovery failed.");
}

Console.WriteLine("All fountain-code loss simulations passed.");
