using Microsoft.UI.Xaml.Markup;

namespace WarThunderChatTranslator.Helpers
{
    [MarkupExtensionReturnType(ReturnType = typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        protected override object ProvideValue()
        {
            return Localization.GetString(Key);
        }
    }
}