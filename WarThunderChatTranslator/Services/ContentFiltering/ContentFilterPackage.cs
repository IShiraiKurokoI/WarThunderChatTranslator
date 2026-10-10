#nullable enable

using System.Collections.Generic;

namespace WarThunderChatTranslator.Services.ContentFiltering
{
    public sealed class ContentFilterPackage
    {
        public int FormatVersion { get; set; } = 1;
        public List<ContentFilterEntry> Blocklist { get; set; } = [];
        public List<string> Allowlist { get; set; } = [];
    }
}
