using System;
using System.Collections.Generic;
using System.Linq;

namespace BreachQR
{
    public sealed record FountainReceiveUpdate(
        string SessionId,
        FountainManifest Manifest,
        int RecoveredBlocks,
        int ReceivedPackets,
        byte[] CompletedData);

    public sealed class FountainSessionReceiver
    {
        private readonly Dictionary<string, ReceiveSession> sessions = new(StringComparer.Ordinal);

        public int ActiveSessionCount => sessions.Count;

        public FountainReceiveUpdate AddFrame(byte[] frame, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(frame);
            RemoveExpiredSessions(utcNow);

            if (FountainWireProtocol.TryParseManifest(frame, out FountainManifest manifest))
            {
                ReceiveSession session = GetOrCreateSession(manifest.SessionId, utcNow);
                session.SetManifest(manifest);
                return ProcessPendingPackets(session, utcNow);
            }

            if (!FountainWireProtocol.TryParseData(frame, out FountainDataFrame dataFrame))
            {
                return null;
            }

            ReceiveSession dataSession = GetOrCreateSession(dataFrame.SessionId, utcNow);
            dataSession.LastSeenUtc = utcNow;
            if (dataSession.Decoder == null)
            {
                dataSession.Enqueue(dataFrame);
                return null;
            }

            return ProcessDataPacket(dataSession, dataFrame, utcNow);
        }

        private FountainReceiveUpdate ProcessPendingPackets(ReceiveSession session, DateTime utcNow)
        {
            if (session.IsComplete)
            {
                return null;
            }

            FountainReceiveUpdate update = CreateUpdate(session, null);
            while (session.PendingPackets.Count > 0 && !session.IsComplete)
            {
                update = ProcessDataPacket(session, session.PendingPackets.Dequeue(), utcNow);
            }

            return update;
        }

        private FountainReceiveUpdate ProcessDataPacket(ReceiveSession session, FountainDataFrame dataFrame, DateTime utcNow)
        {
            if (session.IsComplete)
            {
                return null;
            }

            if (dataFrame.Payload.Length != session.Manifest.BlockSize)
            {
                return CreateUpdate(session, null);
            }

            FountainPacket packet = session.Manifest.CreatePacket(dataFrame.Sequence, dataFrame.Payload);
            if (!session.Decoder.AddPacket(packet))
            {
                return CreateUpdate(session, null);
            }

            session.LastSeenUtc = utcNow;
            byte[] completedData = null;
            if (session.Decoder.IsComplete)
            {
                completedData = session.Decoder.GetDecodedData();
                session.IsComplete = true;
            }

            return CreateUpdate(session, completedData);
        }

        private ReceiveSession GetOrCreateSession(string sessionId, DateTime utcNow)
        {
            if (sessions.TryGetValue(sessionId, out ReceiveSession session))
            {
                session.LastSeenUtc = utcNow;
                return session;
            }

            if (sessions.Count >= TransferLimits.MaxConcurrentSessions)
            {
                string oldestSessionId = sessions.OrderBy(pair => pair.Value.LastSeenUtc).First().Key;
                sessions.Remove(oldestSessionId);
            }

            session = new ReceiveSession(sessionId, utcNow);
            sessions.Add(sessionId, session);
            return session;
        }

        private void RemoveExpiredSessions(DateTime utcNow)
        {
            foreach (string sessionId in sessions
                         .Where(pair => utcNow - pair.Value.LastSeenUtc > TransferLimits.SessionTimeout)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                sessions.Remove(sessionId);
            }
        }

        private static FountainReceiveUpdate CreateUpdate(ReceiveSession session, byte[] completedData)
        {
            if (session.Manifest == null)
            {
                return null;
            }

            return new FountainReceiveUpdate(
                session.SessionId,
                session.Manifest,
                session.Decoder.SolvedBlockCount,
                session.Decoder.ReceivedPacketCount,
                completedData);
        }

        private sealed class ReceiveSession
        {
            public ReceiveSession(string sessionId, DateTime lastSeenUtc)
            {
                SessionId = sessionId;
                LastSeenUtc = lastSeenUtc;
            }

            public string SessionId { get; }
            public FountainManifest Manifest { get; private set; }
            public FountainDecoder Decoder { get; private set; }
            public Queue<FountainDataFrame> PendingPackets { get; } = new();
            public DateTime LastSeenUtc { get; set; }
            public bool IsComplete { get; set; }

            public void SetManifest(FountainManifest manifest)
            {
                if (Manifest != null)
                {
                    return;
                }

                Manifest = manifest;
                Decoder = new FountainDecoder();
            }

            public void Enqueue(FountainDataFrame dataFrame)
            {
                if (PendingPackets.Count >= TransferLimits.MaxPendingPacketsPerSession)
                {
                    PendingPackets.Dequeue();
                }

                PendingPackets.Enqueue(dataFrame);
            }
        }
    }
}
