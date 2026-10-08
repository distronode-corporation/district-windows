using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels;

/// <summary>A screen this build does not have yet, and the way back from it.</summary>
public sealed partial class UnavailableViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The heading.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Where to go instead.</summary>
    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    internal void Attach(PageContext context) => _context = context;

    internal void Show(string title, string body)
    {
        Title = title;
        Body = body;
    }

    [RelayCommand]
    private void Back() => _context?.Send(new UiEvent.Back());
}
