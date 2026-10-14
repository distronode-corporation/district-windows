using System.ComponentModel;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Inbox;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Inbox;

/// <summary>
/// The new message form, as a dialog that is open exactly while the core holds
/// the form open. Its buttons and Escape ask the core; only the core closes it
/// (after a send, opening the message's thread), so a send's answer always
/// lands on a form and a message on its way cannot be dismissed.
/// </summary>
public sealed partial class ComposeDialog : ContentDialog
{
    private bool _closedByCore;

    /// <summary>A dialog over <paramref name="form"/>.</summary>
    public ComposeDialog(ComposeViewModel form)
    {
        Form = form;
        InitializeComponent();
        ShowHeader();
        PrimaryButtonClick += OnSend;
        Closing += OnClosing;
        Form.PropertyChanged += OnFormChanged;
        Closed += (_, _) => Form.PropertyChanged -= OnFormChanged;
    }

    /// <summary>The form shown.</summary>
    public ComposeViewModel Form { get; }

    /// <summary>The title, the buttons' words and whether Send works: the dialog's own properties, set here rather than bound.</summary>
    private void ShowHeader()
    {
        Title = Form.Title;
        AutomationProperties.SetName(this, Form.Title);
        PrimaryButtonText = Form.SendLabel;
        CloseButtonText = Form.CancelLabel;
        IsPrimaryButtonEnabled = Form.CanSend;
    }

    private void OnFormChanged(object? sender, PropertyChangedEventArgs e) => ShowHeader();

    private void OnSuggestionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ComposeSuggestionItem contact)
        {
            Form.Choose(contact);
        }
    }

    /// <summary>Opens the dialog while the core holds the form open, and closes it once the core does not.</summary>
    internal sealed class Host
    {
        private ComposeDialog? _open;

        /// <summary>Brings the dialog in line with <paramref name="form"/>, over <paramref name="owner"/>.</summary>
        internal async void Sync(FrameworkElement owner, PageContext? context, ComposeViewModel form)
        {
            if (!form.IsOpen)
            {
                Close();
                return;
            }
            if (_open is not null || context is null || context.DialogOpen || owner.XamlRoot is null)
            {
                return;
            }
            var dialog = new ComposeDialog(form) { XamlRoot = owner.XamlRoot };
            _open = dialog;
            context.DialogOpen = true;
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _open = null;
                context.DialogOpen = false;
            }
        }

        /// <summary>Closes the dialog without asking the core: the core closed the form, or the page left the screen.</summary>
        internal void Close()
        {
            if (_open is not null)
            {
                _open._closedByCore = true;
                _open.Hide();
            }
        }
    }

    private void OnSend(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The core closes the form once the message is sent.
        args.Cancel = true;
        Form.SubmitCommand.Execute(null);
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_closedByCore)
        {
            return;
        }
        // Cancel and Escape ask the core, which refuses while the message is on its way.
        args.Cancel = true;
        Form.CancelCommand.Execute(null);
    }
}
