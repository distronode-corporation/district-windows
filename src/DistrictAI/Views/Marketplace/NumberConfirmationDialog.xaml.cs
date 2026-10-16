using System.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Marketplace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Marketplace;

/// <summary>
/// The core's confirmation before a write that cannot be undone or costs money
/// (releasing a number, the paid lookup, the two registrations), as a dialog
/// open exactly while the core asks it. Its buttons and Escape ask the core;
/// only the core closes it, and not while the write is on its way. A release
/// is destructive, so its default button is the one that does nothing.
/// </summary>
public sealed partial class NumberConfirmationDialog : ContentDialog
{
    private readonly NumberAdminViewModel _admin;
    private bool _closedByCore;

    /// <summary>A dialog over <paramref name="admin"/>'s confirmation.</summary>
    public NumberConfirmationDialog(NumberAdminViewModel admin)
    {
        _admin = admin;
        InitializeComponent();
        DialogButtons.WrapLabels(this);
        Show(admin.Confirmation);
        PrimaryButtonClick += OnConfirm;
        Closing += OnClosing;
        admin.PropertyChanged += OnAdminChanged;
        Closed += (_, _) => admin.PropertyChanged -= OnAdminChanged;
    }

    private void OnAdminChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NumberAdminViewModel.Confirmation))
        {
            Show(_admin.Confirmation);
        }
    }

    private void Show(NumberConfirmationView? confirmation)
    {
        if (confirmation is null)
        {
            return;
        }
        Title = confirmation.Title;
        AutomationProperties.SetName(this, confirmation.Title);
        Body.Text = confirmation.Body;
        PrimaryButtonText = confirmation.Confirm;
        CloseButtonText = confirmation.Close;
        IsPrimaryButtonEnabled = confirmation.CanConfirm;
        DefaultButton = confirmation.Destructive ? ContentDialogButton.Close : ContentDialogButton.Primary;
        Running.Visibility = confirmation.Running ? Visibility.Visible : Visibility.Collapsed;
        var failure = string.Join(" ", new[] { confirmation.Failure, confirmation.Unrepeatable }.OfType<string>());
        Failure.Message = failure;
        Failure.IsOpen = failure.Length > 0;
    }

    /// <summary>Opens the dialog while the core asks, and closes it once it does not.</summary>
    internal sealed class Host
    {
        private NumberConfirmationDialog? _open;

        /// <summary>Brings the dialog in line with <paramref name="admin"/>, over <paramref name="owner"/>.</summary>
        internal async void Sync(FrameworkElement owner, PageContext? context, NumberAdminViewModel admin)
        {
            if (admin.Confirmation is null)
            {
                Close();
                return;
            }
            if (_open is not null || context is null || context.DialogOpen || owner.XamlRoot is null)
            {
                return;
            }
            var dialog = new NumberConfirmationDialog(admin) { XamlRoot = owner.XamlRoot };
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

        /// <summary>Closes the dialog without asking the core: the core closed it, or the page left the screen.</summary>
        internal void Close()
        {
            if (_open is not null)
            {
                _open._closedByCore = true;
                _open.Hide();
            }
        }
    }

    private void OnConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The core closes it once the write lands.
        args.Cancel = true;
        _admin.ConfirmCommand.Execute(null);
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_closedByCore)
        {
            return;
        }
        // Cancel and Escape ask the core, which refuses while the write is on its way.
        args.Cancel = true;
        _admin.CancelConfirmationCommand.Execute(null);
    }
}
