using DistrictAI.ViewModels.Hq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DistrictAI.Views.Hq;

/// <summary>
/// Hold to talk, on District HQ and in the voice dock, for a push-to-talk
/// session: the microphone is on while the button is held down, with the
/// pointer, or with Space or Enter, and off when it is let go, when the
/// pointer or the keyboard leaves it, or when it leaves the screen. Which is
/// which is the view model's (<see cref="HqVoiceViewModel.Hold"/> and
/// <see cref="HqVoiceViewModel.Release"/>): one Microphone(true) per press,
/// one (false) after it.
/// </summary>
internal static class HoldToTalk
{
    /// <summary>Makes <paramref name="button"/> hold the microphone of <paramref name="voice"/>.</summary>
    public static void Attach(Button button, Func<HqVoiceViewModel> voice)
    {
        // The button's own press handling marks the pointer events handled, so
        // these listen to handled ones too.
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => voice().Hold()), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => voice().Release()), true);
        button.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => voice().Release()), true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => voice().Release()), true);
        // Space and Enter hold it down too, and do not click it: a held key
        // repeats, and Hold sends once.
        button.PreviewKeyDown += (_, e) =>
        {
            if (IsTalkKey(e.Key))
            {
                voice().Hold();
                e.Handled = true;
            }
        };
        button.PreviewKeyUp += (_, e) =>
        {
            if (IsTalkKey(e.Key))
            {
                voice().Release();
                e.Handled = true;
            }
        };
        button.LostFocus += (_, _) => voice().Release();
        button.Unloaded += (_, _) => voice().Release();
    }

    private static bool IsTalkKey(VirtualKey key) => key is VirtualKey.Space or VirtualKey.Enter;
}
