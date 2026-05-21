using CommunityToolkit.Mvvm.ComponentModel;
using ContextMenuMgr.Frontend.Services;

namespace ContextMenuMgr.Frontend.ViewModels;

/// <summary>
/// Represents the language Option View Model.
/// </summary>
public partial class LanguageOptionViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageOptionViewModel"/> class.
    /// </summary>
    public LanguageOptionViewModel(AppLanguageOption option, LocalizationService localization)
    {
        Option = option;
        DisplayName = GetDisplayName();
    }

    /// <summary>
    /// Gets the option.
    /// </summary>
    public AppLanguageOption Option { get; }

    /// <summary>
    /// Gets or sets the display Name.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayName { get; private set; }

    private static string GetDisplayName(AppLanguageOption option) => option switch
    {
        AppLanguageOption.System => "Follow system",
        AppLanguageOption.ChineseSimplified => "简体中文",
        AppLanguageOption.ChineseTraditionalTaiwan => "繁體中文（台灣）",
        AppLanguageOption.EnglishUnitedStates => "English (United States)",
        _ => "Follow system"
    };

    private string GetDisplayName() => GetDisplayName(Option);

    /// <summary>
    /// Executes dispose.
    /// </summary>
    public void Dispose()
    {
    }
}
