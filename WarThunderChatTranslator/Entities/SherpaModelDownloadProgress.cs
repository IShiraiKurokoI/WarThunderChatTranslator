#nullable enable

namespace WarThunderChatTranslator.Entities
{
    internal sealed record SherpaModelDownloadProgress(long BytesReceived, long? TotalBytes)
    {
        public double? Percent => TotalBytes is > 0
            ? BytesReceived * 100.0 / TotalBytes.Value
            : null;
    }
}
