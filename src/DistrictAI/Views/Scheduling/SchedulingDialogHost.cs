using System.ComponentModel;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Scheduling;

/// <summary>
/// The confirm-once dialog every section of the booking scheduler's
/// administration shows its writes in, over an <see cref="AdminDialogViewModel"/>:
/// open exactly while the core holds it, its title and buttons the core's.
/// The confirm and the way out (Close, Escape) ask the core, and only the core
/// closes it: a write that landed, or the way out taken. A destructive confirm
/// is never the default button. Each section gives its own content, which
/// shows the dialog's failure and progress beside its own fields.
/// </summary>
internal sealed class SchedulingDialogHost(string automationId)
{
    private ContentDialog? _open;
    private bool _closedByCore;

    /// <summary>Brings the dialog in line with <paramref name="dialog"/>, over <paramref name="owner"/>.</summary>
    internal async void Sync(FrameworkElement owner, PageContext? context, AdminDialogViewModel dialog, Func<UIElement> content)
    {
        if (!dialog.IsOpen)
        {
            Close();
            return;
        }
        if (_open is not null || context is null || context.DialogOpen || owner.XamlRoot is null)
        {
            return;
        }
        var box = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Content = content(),
        };
        AutomationProperties.SetAutomationId(box, automationId);
        Apply(box, dialog);
        void OnChanged(object? sender, PropertyChangedEventArgs e) => Apply(box, dialog);
        box.PrimaryButtonClick += (_, args) =>
        {
            // The core closes it once the write lands.
            args.Cancel = true;
            dialog.ConfirmCommand.Execute(null);
        };
        box.Closing += (_, args) =>
        {
            if (_closedByCore)
            {
                return;
            }
            // Close and Escape ask the core, which closes it.
            args.Cancel = true;
            dialog.CloseCommand.Execute(null);
        };
        dialog.PropertyChanged += OnChanged;
        _open = box;
        context.DialogOpen = true;
        try
        {
            await box.ShowAsync();
        }
        finally
        {
            dialog.PropertyChanged -= OnChanged;
            _open = null;
            _closedByCore = false;
            context.DialogOpen = false;
        }
    }

    /// <summary>Closes the dialog without asking the core: the core closed it, or the page left the screen.</summary>
    internal void Close()
    {
        if (_open is not null)
        {
            _closedByCore = true;
            _open.Hide();
        }
    }

    private static void Apply(ContentDialog box, AdminDialogViewModel dialog)
    {
        box.Title = dialog.Title;
        AutomationProperties.SetName(box, dialog.Title);
        box.PrimaryButtonText = dialog.ConfirmText;
        box.CloseButtonText = dialog.CloseText;
        box.IsPrimaryButtonEnabled = dialog.CanConfirm;
        box.DefaultButton = dialog.Destructive ? ContentDialogButton.Close : ContentDialogButton.Primary;
    }
}
