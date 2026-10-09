using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.VoiceStudio;

/// <summary>The Voice section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class VoiceStudioViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial VoiceStudioView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(VoiceStudioView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(VoiceStudioAction action) => _context?.Send(new UiEvent.VoiceStudio(action));
}
