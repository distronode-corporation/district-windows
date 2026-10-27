using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Hq;

/// <summary>
/// The voice dock: a District HQ voice session's controls and cards on every
/// screen but District HQ, copied from the core's <see cref="HqVoiceDockView"/>
/// (<see cref="ShellView.VoiceDock"/>). The session goes on when the member
/// leaves District HQ, so the microphone is never live without the controls
/// that stop it on screen. Review in District HQ goes back to the conversation.
/// </summary>
public sealed partial class VoiceDockViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The session's controls and cards, as on District HQ.</summary>
    public HqVoiceViewModel Voice { get; } = new();

    /// <summary>Whether the dock shows.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Its heading, and the landmark's name.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Review in District HQ's words.</summary>
    [ObservableProperty]
    public partial string ReviewLabel { get; set; } = string.Empty;

    /// <summary>Whether Review in District HQ is offered: while changes are waiting.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    public partial bool CanReview { get; set; }

    internal void Attach(PageContext context)
    {
        _context = context;
        Voice.Attach(context);
    }

    /// <summary>Draws <paramref name="dock"/>, or hides the dock when there is none.</summary>
    internal void Show(HqVoiceDockView? dock)
    {
        IsShown = dock is not null;
        if (dock is null)
        {
            CanReview = false;
            return;
        }
        Title = dock.Title;
        ReviewLabel = dock.ReviewLabel;
        CanReview = dock.CanReview;
        Voice.Show(dock.Voice);
    }

    [RelayCommand(CanExecute = nameof(CanReview))]
    private void Review() => _context?.Send(new UiEvent.Hq(new HqAction.Open()));
}
