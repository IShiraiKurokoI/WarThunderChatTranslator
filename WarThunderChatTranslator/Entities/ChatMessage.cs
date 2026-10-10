using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Entities
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public string Msg { get; set; }
        public string Sender { get; set; }
        public bool Enemy { get; set; }
        public string Mode { get; set; }
        public int Time { get; set; }
        public string OriginalMessage { get; set; }
        public string TranslatedMessage { get; set; }
        public string SourceLanguageIsoCode { get; set; }
        public bool TranslationSucceeded { get; set; }
        public string PrettyMessage { get; set; }
    }
}
