using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WarThunderChatTranslator.Helpers
{
    public static class TtsCacheKey
    {
        public static string Compute(string text, string voiceId)
            => Compute(text, "Windows", voiceId, 1.0);

        public static string Compute(string text, string voiceId, double speakingRate)
            => Compute(text, "Windows", voiceId, speakingRate);

        public static string Compute(string text, string providerId, string voiceId, double speakingRate)
        {
            var normalizedRate = Math.Clamp(speakingRate, 0.5, 6.0)
                .ToString("0.###", CultureInfo.InvariantCulture);
            var payload = $"{providerId ?? string.Empty}\n{voiceId ?? string.Empty}\n{normalizedRate}\n{text ?? string.Empty}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }
    }
}
