using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;

namespace WarThunderChatTranslator.Helpers
{
    [MarkupExtensionReturnType(ReturnType = typeof(string))]
    public sealed class LocExtension : MarkupExtension, INotifyPropertyChanged
    {
        private string key;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Key
        {
            get => key;
            set
            {
                if (key != value)
                {
                    key = value;
                    NotifyValueChanged();
                }
            }
        }

        public string Value => Localization.GetString(Key);

        public LocExtension()
        {
            Localization.CultureChanged += NotifyValueChanged;
        }

        protected override object ProvideValue()
        {
            return new Binding
            {
                Source = this,
                Path = new PropertyPath("Value"),
                Mode = BindingMode.OneWay
            };
        }

        private void NotifyValueChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}