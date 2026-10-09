using CommunityToolkit.Mvvm.ComponentModel;
using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Settings.Members;

/// <summary>The members section: what the page shows, and what it sends. A stub until the area's packet builds it.</summary>
public sealed partial class MembersViewModel : ObservableObject
{
    private PageContext? _context;

    /// <summary>The core's view of the screen.</summary>
    [ObservableProperty]
    public partial MembersView? View { get; set; }

    internal void Attach(PageContext context) => _context = context;

    internal void Show(MembersView view) => View = view;

    /// <summary>Forwards one of the screen's actions.</summary>
    internal void Send(MembersAction action) => _context?.Send(new UiEvent.Members(action));
}
