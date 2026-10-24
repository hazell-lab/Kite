using System.Windows.Media;
using Kite.App.Services;
using Kite.Core.Search;

namespace Kite.App.ViewModels;

/// <summary>one row in the results list, ready to show.</summary>
public sealed class ResultItemViewModel : ObservableObject
{
    private ImageSource? _icon;

    public ResultItemViewModel(SearchResult result)
    {
        Result = result;
    }

    public SearchResult Result { get; }

    public string Title => Result.Title;

    public string Subtitle => Result.Subtitle;

    public string ActionLabel => Result.ActionLabel;

    public bool HasAction => Result.Action.Kind != ActionKind.None;

    /// <summary>text read out by screen readers.</summary>
    public string AutomationName => string.IsNullOrEmpty(Subtitle) ? Title : $"{Title}, {Subtitle}";

    /// <summary>a symbol from the windows icon font, used until a real icon loads.</summary>
    public string Glyph => Result.Icon.Kind switch
    {
        IconKind.Application => "",
        IconKind.Calculator => "",
        IconKind.Web => "",
        IconKind.Bookmark => "",
        IconKind.Folder => "",
        IconKind.File => "",
        IconKind.Alias => "",
        IconKind.Command => "",
        IconKind.Warning => "",
        _ => "",
    };

    public bool IsWarning => Result.Icon.Kind == IconKind.Warning;

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    /// <summary>start loading the real icon. the symbol stays if it fails.</summary>
    internal async void LoadIcon(ShellIconLoader loader)
    {
        if (string.IsNullOrEmpty(Result.Icon.ShellPath))
        {
            return;
        }

        try
        {
            Icon = await loader.GetIconAsync(Result.Icon.ShellPath);
        }
        catch (Exception ex)
        {
            Log.Error("Icon load failed", ex);
        }
    }
}
