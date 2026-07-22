using System;
using System.Windows.Data;
using System.Windows.Markup;
using PaDDY.Services;

namespace PaDDY.Helpers
{
    [MarkupExtensionReturnType(typeof(object))]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocExtension()
        {
            Key = string.Empty;
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return string.Empty;

            var binding = new System.Windows.Data.Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
