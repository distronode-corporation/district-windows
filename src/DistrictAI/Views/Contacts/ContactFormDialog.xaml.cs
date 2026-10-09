using System.ComponentModel;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Contacts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Contacts;

/// <summary>
/// The form adding a contact, or changing the open one, as a dialog that is
/// open exactly while the core holds the form open. Its buttons and Escape
/// ask the core; only the core closes it, so a save's answer always lands on
/// a form and a form being saved cannot be dismissed.
/// </summary>
public sealed partial class ContactFormDialog : ContentDialog
{
    private bool _closedByCore;

    /// <summary>A dialog over <paramref name="form"/>.</summary>
    public ContactFormDialog(ContactFormViewModel form)
    {
        Form = form;
        InitializeComponent();
        ShowHeader();
        PrimaryButtonClick += OnSubmit;
        Closing += OnClosing;
        Form.PropertyChanged += OnFormChanged;
        Closed += (_, _) => Form.PropertyChanged -= OnFormChanged;
    }

    /// <summary>The heading, the buttons' words and whether the form can be sent: the dialog's own properties, set here rather than bound.</summary>
    private void ShowHeader()
    {
        Title = Form.Title;
        AutomationProperties.SetName(this, Form.Title);
        PrimaryButtonText = Form.SubmitLabel;
        IsPrimaryButtonEnabled = Form.CanSubmit;
    }

    private void OnFormChanged(object? sender, PropertyChangedEventArgs e) => ShowHeader();

    /// <summary>The form shown.</summary>
    public ContactFormViewModel Form { get; }

    /// <summary>Opens the form's dialog while the core holds the form open, and closes it once the core does not.</summary>
    internal sealed class Host
    {
        private ContactFormDialog? _open;

        /// <summary>Brings the dialog in line with <paramref name="form"/>, over <paramref name="owner"/>.</summary>
        internal async void Sync(FrameworkElement owner, PageContext? context, ContactFormViewModel form)
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
            var dialog = new ContactFormDialog(form) { XamlRoot = owner.XamlRoot };
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

    private void OnSubmit(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The core closes the form once the contact is saved.
        args.Cancel = true;
        Form.SubmitCommand.Execute(null);
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_closedByCore)
        {
            return;
        }
        // Cancel and Escape ask the core, which refuses while the form is saving.
        args.Cancel = true;
        Form.CancelCommand.Execute(null);
    }
}
