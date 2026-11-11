using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using FileFlow.Core.Localization;

namespace FileFlow.App.Localization;

/// <summary>
/// Runtime language switch for the whole UI.
/// XAML binds to the string indexer through the <see cref="TExtension"/> markup extension
/// (<c>Text="{loc:T Nav_Files}"</c>); raising <c>Item[]</c> refreshes every bound string at once,
/// so changing the language needs no restart. Code-behind and view models use
/// <see cref="S"/> / <see cref="F"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, string> _strings = AppStrings.English;

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    /// <summary>The active language code ("en" or "zh-CN").</summary>
    public string Language { get; private set; } = AppLanguages.English;

    public bool IsChinese => Language == AppLanguages.Chinese;

    /// <summary>Returns the localized string; an unknown key returns the key itself (never throws).</summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return _strings.TryGetValue(key, out var value) ? value : key;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed so view models can refresh generated text.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Applies the stored language before the first window is created.</summary>
    public static void Initialize(string? language) => Instance.SetLanguage(language, raiseEvent: false);

    public void SetLanguage(string? language, bool raiseEvent = true)
    {
        var normalized = AppLanguages.Normalize(language);
        _strings = normalized == AppLanguages.Chinese ? AppStrings.Chinese : AppStrings.English;
        if (normalized == Language && raiseEvent)
        {
            // Same language selected again: nothing to do.
            return;
        }

        Language = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        if (raiseEvent)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string S(string key) => this[key];

    public string F(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, this[key], args);
}

/// <summary>
/// Binds a XAML string to the localized resource dictionary: <c>{loc:T Key}</c>.
/// A binding (not a one-off value) keeps the text live when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
