//! The brand's accent and semantic colours, light and dark, as the core
//! defines them (district-core's `palette`, `LIGHT` and `DARK`), for the app's
//! accent resources.
//!
//! Each colour crosses as a `0xAARRGGBB` value, opaque, converted from the
//! core's `#rrggbb` when this crate compiles: a colour the core spells any
//! other way fails the build. The neutrals stay Windows' own, and under high
//! contrast the app sets none of these (`BrandPalette.cs`).

use district_core::palette::{DARK, LIGHT, Palette};

/// One theme's brand colours, each `0xAARRGGBB`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, uniffi::Record)]
pub struct PaletteView {
    /// The accent: suggested actions, selections, links and focus.
    pub accent: u32,
    /// The accent under the pointer.
    pub accent_hover: u32,
    /// Text and icons drawn on the accent or on a semantic colour.
    pub on_accent: u32,
    /// Something went well.
    pub success: u32,
    /// Something needs attention.
    pub warning: u32,
    /// Something is destroyed or failed.
    pub destructive: u32,
    /// Neutral information.
    pub info: u32,
}

/// The light theme's colours.
const LIGHT_VIEW: PaletteView = view(&LIGHT);

/// The dark theme's colours.
const DARK_VIEW: PaletteView = view(&DARK);

/// The brand's colours for a dark theme (`true`) or a light one.
#[uniffi::export]
pub fn brand_palette(dark: bool) -> PaletteView {
    if dark { DARK_VIEW } else { LIGHT_VIEW }
}

/// `palette`, each colour as `0xAARRGGBB`.
const fn view(palette: &Palette) -> PaletteView {
    PaletteView {
        accent: argb(palette.accent),
        accent_hover: argb(palette.accent_hover),
        on_accent: argb(palette.on_accent),
        success: argb(palette.success),
        warning: argb(palette.warning),
        destructive: argb(palette.destructive),
        info: argb(palette.info),
    }
}

/// `#rrggbb` as an opaque `0xAARRGGBB`. Anything else panics, which in a
/// constant is a failed build.
const fn argb(hex: &str) -> u32 {
    let bytes = hex.as_bytes();
    assert!(
        bytes.len() == 7 && bytes[0] == b'#',
        "a palette colour is #rrggbb"
    );
    let mut value = 0xff_u32;
    let mut at = 1;
    while at < 7 {
        value = (value << 4) | nibble(bytes[at]);
        at += 1;
    }
    value
}

/// One hexadecimal digit's value.
const fn nibble(digit: u8) -> u32 {
    match digit {
        b'0'..=b'9' => (digit - b'0') as u32,
        b'a'..=b'f' => (digit - b'a' + 10) as u32,
        b'A'..=b'F' => (digit - b'A' + 10) as u32,
        _ => panic!("a palette colour is #rrggbb"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The colours, written out. A core bump that changes the brand fails
    /// here, so the change is seen (and the app's look checked) before it
    /// ships, not only inherited.
    #[test]
    fn the_palette_is_the_brands() {
        assert_eq!(
            brand_palette(false),
            PaletteView {
                accent: 0xff01_657d,
                accent_hover: 0xff04_5063,
                on_accent: 0xffff_ffff,
                success: 0xff01_6f4e,
                warning: 0xff75_4603,
                destructive: 0xffb4_022d,
                info: 0xff22_3092,
            }
        );
        assert_eq!(
            brand_palette(true),
            PaletteView {
                accent: 0xff67_cded,
                accent_hover: 0xff8b_e2ff,
                on_accent: 0xff0e_1c1f,
                success: 0xff74_e9aa,
                warning: 0xffea_b90c,
                destructive: 0xfffe_737f,
                info: 0xff77_bafe,
            }
        );
    }

    /// Each value is the core's own colour, token by token.
    #[test]
    fn each_colour_is_the_cores() {
        for (dark, palette) in [(false, &LIGHT), (true, &DARK)] {
            let view = brand_palette(dark);
            let ours = [
                view.accent,
                view.accent_hover,
                view.on_accent,
                view.success,
                view.warning,
                view.destructive,
                view.info,
            ];
            for ((token, hex), argb) in palette.tokens().into_iter().zip(ours) {
                assert_eq!(format!("#{:06x}", argb & 0x00ff_ffff), hex, "{token}");
                assert_eq!(argb >> 24, 0xff, "{token} is opaque");
            }
            assert_eq!(view, super::view(palette));
        }
    }

    #[test]
    fn hex_is_read_in_either_case() {
        assert_eq!(argb("#0aF19B"), 0xff0a_f19b);
        assert_eq!(argb("#000000"), 0xff00_0000);
    }

    #[test]
    #[should_panic(expected = "a palette colour is #rrggbb")]
    fn a_short_colour_is_refused() {
        argb("#fff");
    }

    #[test]
    #[should_panic(expected = "a palette colour is #rrggbb")]
    fn a_colour_without_its_hash_is_refused() {
        argb("0165 7d");
    }

    #[test]
    #[should_panic(expected = "a palette colour is #rrggbb")]
    fn a_colour_with_a_non_digit_is_refused() {
        argb("#01657g");
    }
}
