using DistrictAI.Core.Ffi;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace DistrictAI.Platform;

/// <summary>
/// The brand's accent and semantic colours, from district-core's palette
/// (<see cref="DistrictFfi.BrandPalette"/>), as the app's accent resources in
/// its light and dark themes. The neutrals stay Windows' own.
/// </summary>
/// <remarks>
/// Under high contrast the app sets none of these: the person's own contrast
/// colours win. Windows says when high contrast turns on or off, on a thread of
/// its own; the change is made on the UI thread, and the window's content is
/// asked to look its resources up again.
/// <para>
/// The colours go into a dictionary of their own, merged into each theme
/// dictionary of the application's resources, so whatever App.xaml defines
/// there stays, and turning them off is removing that one dictionary.
/// </para>
/// </remarks>
internal sealed class BrandPalette : IDisposable
{
    private readonly Window _window;
    private readonly DispatcherQueue _queue;
    // Held for as long as the subscription lasts.
    private readonly AccessibilitySettings _accessibility = new();
    private readonly ResourceDictionary _light = Theme(DistrictFfi.BrandPalette(false), dark: false);
    private readonly ResourceDictionary _dark = Theme(DistrictFfi.BrandPalette(true), dark: true);
    private bool _applied;
    private bool _disposed;

    /// <summary>Sets the brand's colours on the app, unless high contrast is on, and follows high contrast from here.</summary>
    public BrandPalette(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _queue = window.DispatcherQueue;
        _accessibility.HighContrastChanged += OnHighContrastChanged;
        Apply();
    }

    /// <summary>Stops following high contrast. The colours set stay for as long as the app runs.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _accessibility.HighContrastChanged -= OnHighContrastChanged;
    }

    private void OnHighContrastChanged(AccessibilitySettings sender, object args) =>
        _queue.TryEnqueue(Apply);

    /// <summary>The brand's colours on, or off under high contrast. On the UI thread.</summary>
    private void Apply()
    {
        if (_disposed)
        {
            return;
        }
        var wanted = !_accessibility.HighContrast;
        if (wanted == _applied)
        {
            return;
        }
        Set(ThemeDictionary("Light"), _light, wanted);
        Set(ThemeDictionary("Dark"), _dark, wanted);
        _applied = wanted;
        Refresh();
    }

    private static void Set(ResourceDictionary theme, ResourceDictionary brand, bool on)
    {
        if (on)
        {
            theme.MergedDictionaries.Add(brand);
        }
        else
        {
            _ = theme.MergedDictionaries.Remove(brand);
        }
    }

    /// <summary>The application's theme dictionary <paramref name="key"/>, made if App.xaml has none.</summary>
    private static ResourceDictionary ThemeDictionary(string key)
    {
        var themes = Application.Current.Resources.ThemeDictionaries;
        if (themes.TryGetValue(key, out var existing) && existing is ResourceDictionary theme)
        {
            return theme;
        }
        var made = new ResourceDictionary();
        themes[key] = made;
        return made;
    }

    /// <summary>
    /// Has the window's content look its theme resources up again: a theme
    /// resource already resolved changes only when the theme does, so the
    /// theme is changed and changed back.
    /// </summary>
    private void Refresh()
    {
        if (_window.Content is not FrameworkElement root)
        {
            return;
        }
        var theme = root.RequestedTheme;
        root.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        root.RequestedTheme = theme;
    }

    /// <summary>
    /// One theme's resources. The accent ramp is what WinUI derives its accent
    /// brushes from (the light theme reads the Dark steps, the dark theme the
    /// Light steps), and the brushes the controls name are set as well, so a
    /// control reads the brand whichever it looks up.
    /// </summary>
    private static ResourceDictionary Theme(PaletteView palette, bool dark)
    {
        var accent = Argb(palette.Accent);
        var hover = Argb(palette.AccentHover);
        var onAccent = Argb(palette.OnAccent);
        var resources = new ResourceDictionary
        {
            ["SystemAccentColor"] = accent,
            // The light theme's fill is Dark1 and its text Dark2; the dark
            // theme's fill is Light2 and its text Light3. The step that is
            // text takes the hover colour, which reads further from the
            // background in each theme.
            ["SystemAccentColorDark1"] = accent,
            ["SystemAccentColorDark2"] = dark ? accent : hover,
            ["SystemAccentColorDark3"] = dark ? accent : hover,
            ["SystemAccentColorLight1"] = accent,
            ["SystemAccentColorLight2"] = accent,
            ["SystemAccentColorLight3"] = dark ? hover : accent,

            ["AccentFillColorDefaultBrush"] = Brush(accent),
            ["AccentFillColorSecondaryBrush"] = Brush(hover),
            ["AccentFillColorTertiaryBrush"] = Brush(accent, 0.8),
            ["AccentFillColorSelectedTextBackgroundBrush"] = Brush(accent),
            ["AccentTextFillColorPrimaryBrush"] = Brush(accent),
            ["AccentTextFillColorSecondaryBrush"] = Brush(hover),
            ["AccentTextFillColorTertiaryBrush"] = Brush(accent),
            ["TextOnAccentFillColorPrimaryBrush"] = Brush(onAccent),
            ["TextOnAccentFillColorSecondaryBrush"] = Brush(onAccent, 0.7),
            ["TextOnAccentFillColorSelectedTextBrush"] = Brush(onAccent),
        };
        Semantic(resources, "SystemFillColorSuccess", palette.Success);
        Semantic(resources, "SystemFillColorCaution", palette.Warning);
        Semantic(resources, "SystemFillColorCritical", palette.Destructive);
        Semantic(resources, "SystemFillColorAttention", palette.Info);
        return resources;
    }

    /// <summary>A semantic colour, as the colour and the brush WinUI names after it.</summary>
    private static void Semantic(ResourceDictionary resources, string key, uint argb)
    {
        var color = Argb(argb);
        resources[key] = color;
        resources[key + "Brush"] = Brush(color);
    }

    private static Color Argb(uint argb) =>
        ColorHelper.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static SolidColorBrush Brush(Color color, double opacity = 1) => new(color) { Opacity = opacity };
}
