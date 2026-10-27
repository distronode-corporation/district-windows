using System.ComponentModel;
using DistrictAI.Core.Ffi;
using DistrictAI.ViewModels;
using DistrictAI.ViewModels.Hq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DistrictAI.Views.Hq;

/// <summary>
/// The voice dock: a District HQ voice session's controls on every screen but
/// District HQ, under the call strip, outside the pages, so it stays as the
/// member moves about the app. Shown while <see cref="ShellView.VoiceDock"/>
/// is set: what the assistant is doing, the microphone, Stop voice, and every
/// change proposed by voice in full, with Review in District HQ to go back to
/// the conversation. A landmark named "District HQ voice". The keyboard
/// follows the session in the dock: Stop voice while it runs, then the dismiss
/// button, then Review in District HQ.
/// </summary>
public sealed partial class VoiceDock : UserControl
{
    /// <summary>The share of the window's height the cards may take.</summary>
    private const double CardShare = 0.3;

    /// <summary>The least room the cards are given, however short the window.</summary>
    private const double MinCardHeight = 120;

    /// <summary>The most room the cards take, however tall the window.</summary>
    private const double MaxCardHeight = 320;

    /// <summary>A dock with no session.</summary>
    public VoiceDock()
    {
        InitializeComponent();
        DockCards.Attach(ViewModel.Voice);
        ViewModel.Voice.PropertyChanged += OnVoiceChanged;
    }

    /// <summary>What the dock shows, and its buttons' commands.</summary>
    public VoiceDockViewModel ViewModel { get; } = new();

    internal void Attach(PageContext context) => ViewModel.Attach(context);

    internal void Show(ShellView shell) => ViewModel.Show(shell.VoiceDock);

    /// <summary>
    /// Holds the cards to their share of a window area <paramref name="height"/>
    /// high: 320 at most, as on a full-height window, and less in a short one
    /// (120 at least, a card's head and its buttons), so the call strip above
    /// and the cards together leave the page some room, and the cards scroll.
    /// </summary>
    internal void LimitCards(double height) =>
        DockCardScroll.MaxHeight = Math.Clamp(height * CardShare, MinCardHeight, MaxCardHeight);

    /// <summary>
    /// The session moved to another stage while the keyboard was in the dock
    /// (or nowhere, its control gone): it goes to the first of Stop voice, the
    /// dismiss button and Review in District HQ that shows, once this snapshot
    /// has shown it.
    /// </summary>
    private void OnVoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HqVoiceViewModel.Stage) || !ViewModel.IsShown || !IsLoaded || XamlRoot is null)
        {
            return;
        }
        if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && !HqPage.IsWithin(focused, Dock))
        {
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            Control[] order = [DockStop, DockDismiss, DockReview];
            order.FirstOrDefault(control => control.Visibility == Visibility.Visible)?.Focus(FocusState.Programmatic);
        });
    }
}
