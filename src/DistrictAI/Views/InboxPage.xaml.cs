using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views;

/// <summary>The conversations, and the search over every message.</summary>
public sealed partial class InboxPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public InboxPage()
    {
        InitializeComponent();
        Status.Attach(ViewModel.Load);
        Refresh.Attach(ViewModel.Load);
    }

    /// <summary>What the page shows.</summary>
    public InboxViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(InboxView view)
    {
        ViewModel.Show(view);
        // The box is the user's while they are in it; otherwise it shows the
        // core's query (cleared when the core clears the search).
        if (SearchBox.FocusState == FocusState.Unfocused
            && !string.Equals(SearchBox.Text, ViewModel.Query, StringComparison.Ordinal))
        {
            SearchBox.Text = ViewModel.Query;
        }
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.Search(sender.Text);
        }
    }

    private void OnThreadClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ThreadRowItem row)
        {
            ViewModel.OpenThread(row);
        }
    }

    private void OnHitClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchHitItem hit)
        {
            ViewModel.OpenHit(hit);
        }
    }
}
