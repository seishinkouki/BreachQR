using System;
using System.Collections.Generic;
using System.IO;
using ZXing;

namespace BreachQR
{
    public static class QrBinaryPayload
    {
        public static bool TryExtract(Result result, out byte[] payload)
        {
            payload = null;
            if (result?.ResultMetadata == null ||
                !result.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out object metadata))
            {
                return false;
            }

            if (metadata is byte[] singleSegment)
            {
                payload = singleSegment;
                return payload.Length > 0;
            }

            if (metadata is not IEnumerable<byte[]> segments)
            {
                return false;
            }

            using var stream = new MemoryStream();
            foreach (byte[] segment in segments)
            {
                if (segment is { Length: > 0 })
                {
                    stream.Write(segment);
                }
            }

            payload = stream.ToArray();
            return payload.Length > 0;
        }
    }
}
