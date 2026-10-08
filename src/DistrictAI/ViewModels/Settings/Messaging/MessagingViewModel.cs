using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Messaging;

/// <summary>The messaging accounts section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class MessagingViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial MessagingView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(MessagingView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(MessagingAction action) => _context?.Send(new UiEvent.Messaging(action));
}
