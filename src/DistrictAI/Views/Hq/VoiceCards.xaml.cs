using DistrictAI.ViewModels.Hq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DistrictAI.Views.Hq;

/// <summary>
/// The changes a District HQ voice session proposed, one card each, with
/// Confirm and Cancel: on District HQ and in the voice dock, from the
/// <see cref="HqVoiceViewModel"/> of each. A change is confirmed only by
/// someone who can read what it is, so the dock shows the whole card too.
/// </summary>
public sealed partial class VoiceCards : UserControl
{
    private HqVoiceViewModel? _voice;

    /// <summary>A list with no cards.</summary>
    public VoiceCards() => InitializeComponent();

    /// <summary>Shows <paramref name="voice"/>'s cards and sends their clicks through it.</summary>
    internal void Attach(HqVoiceViewModel voice)
    {
        _voice = voice;
        Cards.ItemsSource = voice.Cards;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HqVoiceCardItem card)
        {
            _voice?.Confirm(card);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HqVoiceCardItem card)
        {
            _voice?.Cancel(card);
        }
    }
}
