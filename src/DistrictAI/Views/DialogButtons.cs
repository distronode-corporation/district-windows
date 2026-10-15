using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DistrictAI.Views;

/// <summary>
/// A dialog's buttons whose labels wrap onto a second line instead of being
/// cut: a ContentDialog's buttons share its width in equal halves and never
/// grow, so at 200 percent text "Save booking assistant" lost its end. The
/// labels are the core's, in either language.
/// </summary>
internal static class DialogButtons
{
    private static readonly string[] _parts = ["PrimaryButton", "SecondaryButton", "CloseButton"];

    /// <summary>From the time <paramref name="dialog"/> opens, its button labels wrap.</summary>
    internal static void WrapLabels(ContentDialog dialog) => dialog.Opened += OnOpened;

    private static void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (Application.Current.Resources["WrappingLabelTemplate"] is not DataTemplate template)
        {
            return;
        }
        // The dialog's template is under it; the popup it shows in is searched too.
        var roots = new List<DependencyObject> { sender };
        if (sender.XamlRoot is { } xamlRoot)
        {
            roots.AddRange(VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Select(popup => popup.Child).OfType<DependencyObject>());
        }
        foreach (var part in _parts)
        {
            if (roots.Select(root => Find(root, part)).FirstOrDefault(found => found is not null) is Button button)
            {
                button.ContentTemplate = template;
            }
        }
    }

    // The element with that name under root, searched depth first.
    private static FrameworkElement? Find(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }
            if (Find(child, name) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
