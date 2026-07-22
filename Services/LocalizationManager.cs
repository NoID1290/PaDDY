using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace PaDDY.Services
{
    public class LocalizationManager : INotifyPropertyChanged
    {
        private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
        public static LocalizationManager Instance => _instance.Value;

        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;

        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            private set
            {
                if (_currentCulture != value)
                {
                    _currentCulture = value;
                    OnPropertyChanged(nameof(CurrentCulture));
                    OnPropertyChanged(string.Empty); // Refresh all indexer bindings
                }
            }
        }

        private LocalizationManager()
        {
            _resourceManager = new ResourceManager("PaDDY.Resources.Strings", typeof(LocalizationManager).Assembly);
            _currentCulture = CultureInfo.CurrentUICulture;
        }

        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return string.Empty;
                try
                {
                    string? value = _resourceManager.GetString(key, _currentCulture);
                    return value ?? $"[{key}]";
                }
                catch
                {
                    return $"[{key}]";
                }
            }
        }

        public void SetCulture(string cultureCode)
        {
            if (string.IsNullOrWhiteSpace(cultureCode))
                cultureCode = "en";

            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureCode);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CurrentCulture = culture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting culture to '{cultureCode}': {ex.Message}");
            }
        }

        public string GetString(string key, params object[] args)
        {
            string text = this[key];
            if (args != null && args.Length > 0)
            {
                try
                {
                    return string.Format(text, args);
                }
                catch
                {
                    return text;
                }
            }
            return text;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
