using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Calls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Calls;

/// <summary>
/// The dialler (design A): the country button and its searchable list, the
/// number box, the line naming where the call rings, and Call. Enter in the
/// box is the same as pressing Call. Everything the page shows is the core's
/// <see cref="DialerView"/>; the page decides nothing, and only carries the
/// box's cursor, which way a delete went, and when the member stopped typing.
/// </summary>
public sealed partial class DialerPage : UserControl
{
    /// <summary>A page with nothing shown yet.</summary>
    public DialerPage()
    {
        InitializeComponent();
        ViewModel.CaretSource = () => NumberBox.SelectionStart;
        ViewModel.CaretMoved += (_, caret) =>
            // After the binding has written the box, which puts the cursor at the start.
            DispatcherQueue.TryEnqueue(() => NumberBox.SelectionStart = Math.Min(caret, NumberBox.Text.Length));
        // A switched-off button takes no press, so its row watches for one.
        CallRow.AddHandler(PointerPressedEvent, new PointerEventHandler(OnCallRowPressed), handledEventsToo: true);
    }

    /// <summary>What the page shows, and Call's command.</summary>
    public DialerViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(DialerView view) => ViewModel.Show(view);

    /// <summary>The dialler came on screen: the box takes the keyboard, so typing and pasting go straight into it.</summary>
    internal void FocusNumber() => NumberBox.Focus(FocusState.Programmatic);

    private void OnNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Delete:
                ViewModel.NextDeleteForward = true;
                return;
            case VirtualKey.Back:
                ViewModel.NextDeleteForward = false;
                return;
            case VirtualKey.Enter:
                e.Handled = true;
                if (ViewModel.DialCommand.CanExecute(null))
                {
                    ViewModel.DialCommand.Execute(null);
                }
                else
                {
                    // A refused Call says a wrong length, as leaving the box does.
                    ViewModel.CheckLength();
                }
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// The box lost the keyboard. Looked at once it has landed: the country
    /// button and its list are part of typing the number, so moving there is
    /// not stopping.
    /// </summary>
    private void OnNumberLostFocus(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (focused == CountryButton || CountryFlyout.IsOpen)
            {
                return;
            }
            ViewModel.CheckLength();
        });

    private void OnCallRowPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CallButton.IsEnabled)
        {
            ViewModel.CheckLength();
        }
    }

    private void OnCountriesOpening(object? sender, object e)
    {
        CountrySearch.Text = string.Empty;
        ViewModel.SearchCountries(string.Empty);
    }

    private void OnCountrySearchChanged(object sender, TextChangedEventArgs e) =>
        ViewModel.SearchCountries(CountrySearch.Text);

    private void OnCountrySearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }
        e.Handled = true;
        ViewModel.PickFirstCountry();
        CountryFlyout.Hide();
        FocusNumber();
    }

    private void OnCountryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CountryRowItem row || row.IsHeading)
        {
            return;
        }
        ViewModel.PickCountry(row.Iso);
        CountryFlyout.Hide();
        FocusNumber();
    }
}
