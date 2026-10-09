//! Voice Studio, District Studio's Voice: the voice engine the receptionist
//! speaks through, as the Linux app's Voice Studio page shows it.
//!
//! The recipes on the Stable or Latest tier, the signal chain with an editor
//! for the leg it is open on (its pickers, the voice, and its tuning, the
//! advanced part behind a disclosure), the time-to-first-word meter, where the
//! call is processed, and the save. Every word of it but the heading and a
//! failed read is the service's, in the reader's portal language, shown as the
//! core hands it over.
//!
//! The core holds the edits and decides what each one does; a save sends only
//! the keys that changed and reads the Studio back, and a save the read does
//! not hold is shown as the read's own "not saved". Leaving with edits not
//! saved is asked about first (`crate::guard`). Voice Studio is a section a
//! viewer's role is never offered (`Capabilities::allows`), and the core
//! refuses an edit from any role that may not change the workspace; for one,
//! the controls are shown and do not work. The core offers no preview here:
//! nothing on the page plays audio or costs anything.

use district_core::studio::{
    INTERRUPTION_PREFIX, PickerKind, PickerOption, StudioReady, TuningControl,
};
use district_core::{
    Event, Model, Route, SignedIn, StudioEdit, StudioSaveState, VoiceStudioEvent, VoiceStudioLoad,
    VoiceStudioSection, WorkspaceSection,
};
use district_model::{StudioRecipe, VoiceStudioResponse};
use serde::Serialize;

use crate::screen::ScreenView;
use crate::views::{FactView, LoadStatus};

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The page's heading: the hub row's name for the section.
pub const VOICE_STUDIO_TITLE: &str = "Voice";
/// The Stable tier, as the core names it.
pub const TIER_STABLE: &str = "stable";
/// The Latest tier.
pub const TIER_LATEST: &str = "latest";
/// The section of a tuning key shown with the leg's pickers; every other is
/// behind Advanced.
const MAIN_SECTION: &str = "main";
/// Between the parts of a line.
const DOT: &str = " \u{b7} ";
/// A slider's numbers cross the boundary in thousandths, as whole numbers, so
/// every view stays comparable; the core snaps what comes back to the step.
pub const SLIDER_SCALE: f64 = 1000.0;

/// The Voice section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct VoiceStudioView {
    /// The heading, [`VOICE_STUDIO_TITLE`].
    pub title: String,
    /// Where the read stands. `Failed` carries "Could not load Voice Studio",
    /// with "Try again" (`UiEvent::Refresh`) when the core says it may work.
    pub status: LoadStatus,
    /// The Studio, once read.
    pub studio: Option<StudioFormView>,
}

/// The Studio, read: what it holds and what can be done with it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioFormView {
    /// The line under the heading.
    pub description: String,
    /// Whether the controls work: read, no save on its way, and a role that
    /// may change the workspace.
    pub editable: bool,
    /// Whether the member's role may change the workspace; `false` is the
    /// read-only view.
    pub can_change: bool,
    /// The tier switch's label, "Models".
    pub tier_label: String,
    /// What the tiers mean.
    pub tier_description: String,
    /// Stable and Latest, in order.
    pub tiers: Vec<StudioChoiceView>,
    /// The tier shown (`stable` or `latest`).
    pub tier: String,
    /// The recipes' heading, "Starting point".
    pub recipes_label: String,
    /// This tier's recipes, in tile order.
    pub recipes: Vec<StudioRecipeView>,
    /// The badge on the default recipe, "Default".
    pub default_badge: String,
    /// "Based on Fastest, 2 changes.", once the held engine has moved from its
    /// recipe; `None` before.
    pub based_on: Option<String>,
    /// The button that goes back to the recipe's engine, "Reset".
    pub reset_label: String,
    /// The chain's heading, "Signal chain".
    pub chain_label: String,
    /// Each leg of the held engine, in call order.
    pub blocks: Vec<StudioBlockView>,
    /// The editor of the open leg.
    pub editor: StudioEditorView,
    /// The time-to-first-word meter.
    pub meter: StudioMeterView,
    /// Where the call is processed.
    pub residency: StudioResidencyView,
    /// How the last save went, until an edit or "Dismiss".
    pub notice: Option<StudioNoticeView>,
    /// "Unsaved changes" or "All changes saved".
    pub pending: String,
    /// Whether there are changes to save.
    pub dirty: bool,
    /// The save button's label, "Save voice settings".
    pub save_label: String,
    /// Whether Save works: changes, nothing on its way, and the role.
    pub can_save: bool,
    /// Whether a save is on its way, with the read after it (show a progress
    /// ring; nothing moves meanwhile).
    pub saving: bool,
}

/// One choice: what is held, and what is shown.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioChoiceView {
    /// The value sent back.
    pub value: String,
    /// The words shown.
    pub label: String,
}

/// One recipe of the tier.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioRecipeView {
    /// The recipe, to apply it by (`VoiceStudioAction::ApplyRecipe`).
    pub recipe_id: String,
    /// Its name.
    pub name: String,
    /// What it is for.
    pub description: String,
    /// Its time to first word, where it is processed, its channel and any
    /// note, joined by middle dots.
    pub facts: String,
    /// Whether it is the service's default (show the badge).
    pub is_default: bool,
    /// Whether the held engine started from it.
    pub selected: bool,
}

/// One leg of the signal chain.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioBlockView {
    /// The leg (`stt`, `turn`, `llm`, `tts` or `realtime`), to open the
    /// editor on it by (`VoiceStudioAction::SelectLeg`).
    pub leg: String,
    /// The leg's name and its model: "Ear: Deepgram Flux (English)".
    pub title: String,
    /// What it does, where it is processed, its number, its channel and any
    /// note, joined by middle dots.
    pub detail: String,
    /// Whether the editor is open on it.
    pub open: bool,
}

/// The editor of the leg the chain is open on.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioEditorView {
    /// The leg's name, or "Part to edit".
    pub title: String,
    /// The leg's pickers (vendor, model, location, then the voice), as the
    /// leg has them; none for turn-taking.
    pub pickers: Vec<StudioPickerView>,
    /// The leg's tuning, in the read's order.
    pub controls: Vec<StudioTuningView>,
    /// The disclosure's label, "Advanced".
    pub advanced_label: String,
    /// Whether any control is behind it.
    pub has_advanced: bool,
}

/// Which picker of the editor.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum StudioPicker {
    /// The vendor.
    Vendor,
    /// The model.
    Model,
    /// Where it runs.
    Location,
    /// The voice.
    Voice,
}

/// One picker of the editor.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioPickerView {
    /// Which, to send a choice by (`VoiceStudioAction::Pick`).
    pub picker: StudioPicker,
    /// Its label.
    pub label: String,
    /// What it offers. A held value the list lacks is the last option, under
    /// its own name (or "Choose a voice"); choosing it changes nothing.
    pub options: Vec<StudioChoiceView>,
    /// The value held.
    pub selected: String,
}

/// One tuning control of the open leg.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioTuningView {
    /// The key it writes, to send a change by.
    pub key: String,
    /// Its label.
    pub label: String,
    /// What it does, when the read says.
    pub description: Option<String>,
    /// Whether it is behind Advanced.
    pub advanced: bool,
    /// A heading to show above it ("Interruptions"), for the first of a group.
    pub heading: Option<String>,
    /// The control itself.
    pub control: StudioControlView,
}

/// A tuning control, as drawn.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum StudioControlView {
    /// A slider. Numbers are in thousandths ([`SLIDER_SCALE`]).
    Slider {
        /// The lowest value.
        min: i64,
        /// The highest value.
        max: i64,
        /// The step; 0 for none.
        step: i64,
        /// The value; `None` is the service's default.
        value: Option<i64>,
        /// Where it starts when first set, and what it shows while unset.
        start: i64,
        /// The value as words, in whole numbers or to two places.
        value_text: String,
        /// The "use the default" box's label, for a value that may be unset.
        default_label: Option<String>,
    },
    /// A select.
    Select {
        /// What it offers.
        options: Vec<StudioChoiceView>,
        /// What is held; empty for nothing.
        selected: String,
    },
    /// A switch.
    Switch {
        /// Whether it is on.
        on: bool,
    },
    /// Lines of text: key terms, one per line.
    Lines {
        /// The terms held, one per line.
        text: String,
    },
}

/// The time-to-first-word meter.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioMeterView {
    /// "Time to first word".
    pub heading: String,
    /// What it measures.
    pub description: String,
    /// "About 970 ms", or that nothing is measured yet.
    pub headline: String,
    /// Why the sum is "at least", when it is.
    pub note: Option<String>,
    /// Each stage in call order, with its number.
    pub stages: Vec<FactView>,
    /// Where the numbers come from.
    pub source: String,
}

/// Where the call is processed.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioResidencyView {
    /// "Where the call is processed".
    pub heading: String,
    /// Whether every leg is in the region.
    pub in_region: bool,
    /// The sentence.
    pub text: String,
    /// One line per leg that leaves it.
    pub legs_out: Vec<String>,
}

/// How a notice reads.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum StudioNoticeTone {
    /// Saved, and read back as sent.
    Success,
    /// Saved, and the read after it failed: read it again before anything
    /// else.
    Warning,
    /// Not saved, or not held by the workspace.
    Error,
}

/// How the last save went.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct StudioNoticeView {
    /// The words.
    pub text: String,
    /// How they read.
    pub tone: StudioNoticeTone,
}

/// Something the member did on the Voice section. Reading it again (and so
/// dropping what was not saved) is `UiEvent::Refresh`.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum VoiceStudioAction {
    /// Open the Voice section.
    Open,
    /// Show the recipes of a tier, applying the chosen recipe again from it.
    SelectTier {
        /// `stable` or `latest`.
        tier: String,
    },
    /// Apply one of the tier's recipes.
    ApplyRecipe {
        /// The recipe.
        recipe_id: String,
    },
    /// Go back to the engine the recipe applied.
    Reset,
    /// Open the editor on a leg of the chain.
    SelectLeg {
        /// The leg.
        leg: String,
    },
    /// A choice in one of the editor's pickers.
    Pick {
        /// Which picker.
        picker: StudioPicker,
        /// The value chosen.
        value: String,
    },
    /// A slider moved (`Some`, in thousandths), or its "use the default" box
    /// ticked (`None`).
    SetNumber {
        /// The tuning key.
        key: String,
        /// The value.
        value: Option<i64>,
    },
    /// A select's choice.
    SetChoice {
        /// The tuning key.
        key: String,
        /// The value chosen.
        value: String,
    },
    /// A switch.
    SetFlag {
        /// The tuning key.
        key: String,
        /// Whether it is on.
        on: bool,
    },
    /// Key terms as typed.
    SetLines {
        /// The tuning key.
        key: String,
        /// The text.
        text: String,
    },
    /// Save what changed.
    Save,
    /// Put away the save's notice.
    DismissNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: VoiceStudioAction) -> Vec<Event> {
    let edit = |edit| Event::VoiceStudio(VoiceStudioEvent::Edit(edit));
    vec![match action {
        VoiceStudioAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::VoiceStudio)),
        VoiceStudioAction::SelectTier { tier } => edit(StudioEdit::SelectTier(tier)),
        VoiceStudioAction::ApplyRecipe { recipe_id } => edit(StudioEdit::ApplyRecipe(recipe_id)),
        VoiceStudioAction::Reset => edit(StudioEdit::Reset),
        VoiceStudioAction::SelectLeg { leg } => {
            Event::VoiceStudio(VoiceStudioEvent::SelectLeg(leg))
        }
        VoiceStudioAction::Pick { picker, value } => edit(match picker {
            StudioPicker::Vendor => StudioEdit::Pick {
                kind: PickerKind::Vendor,
                value,
            },
            StudioPicker::Model => StudioEdit::Pick {
                kind: PickerKind::Model,
                value,
            },
            StudioPicker::Location => StudioEdit::Pick {
                kind: PickerKind::Location,
                value,
            },
            StudioPicker::Voice => StudioEdit::Voice(value),
        }),
        VoiceStudioAction::SetNumber { key, value } => edit(StudioEdit::Number {
            key,
            value: value.map(|value| value as f64 / SLIDER_SCALE),
        }),
        VoiceStudioAction::SetChoice { key, value } => edit(StudioEdit::Choice { key, value }),
        VoiceStudioAction::SetFlag { key, on } => edit(StudioEdit::Flag { key, on }),
        VoiceStudioAction::SetLines { key, text } => edit(StudioEdit::Lines { key, text }),
        VoiceStudioAction::Save => Event::VoiceStudio(VoiceStudioEvent::Save),
        VoiceStudioAction::DismissNotice => Event::VoiceStudio(VoiceStudioEvent::DismissSaveNotice),
    }]
}

/// The page of the Voice section, for a signed-in model.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::VoiceStudio {
        view: voice_studio_view(
            signed_in.voice_studio.as_ref(),
            signed_in.capabilities().can_change,
        ),
    }
}

/// The section `section`, for a member who may change it when `can_change`.
fn voice_studio_view(section: Option<&VoiceStudioSection>, can_change: bool) -> VoiceStudioView {
    let (status, studio) = match section.map(|section| (section, &section.load)) {
        None | Some((_, VoiceStudioLoad::Loading)) => (LoadStatus::Loading, None),
        Some((_, VoiceStudioLoad::Failed(failure))) => (
            LoadStatus::failed(VoiceStudioSection::FAILED_TITLE, failure),
            None,
        ),
        Some((section, VoiceStudioLoad::Ready(ready))) => (
            LoadStatus::Ready,
            Some(form_view(section, ready, can_change)),
        ),
    };
    VoiceStudioView {
        title: VOICE_STUDIO_TITLE.to_owned(),
        status,
        studio,
    }
}

fn form_view(
    section: &VoiceStudioSection,
    ready: &StudioReady,
    can_change: bool,
) -> StudioFormView {
    let studio = &ready.studio;
    let labels = &studio.labels;
    let dirty = ready.dirty();
    StudioFormView {
        description: labels.description.clone(),
        editable: section.editable() && can_change,
        can_change,
        tier_label: labels.tier_label.clone(),
        tier_description: labels.tier_description.clone(),
        tiers: vec![
            choice(TIER_STABLE, &labels.tier_stable),
            choice(TIER_LATEST, &labels.tier_latest),
        ],
        tier: ready.tier.clone(),
        recipes_label: labels.recipes_label.clone(),
        recipes: ready
            .tiles()
            .into_iter()
            .map(|recipe| recipe_view(recipe, &ready.base_recipe))
            .collect(),
        default_badge: labels.default_badge.clone(),
        based_on: section.based_on(),
        reset_label: labels.reset.clone(),
        chain_label: labels.chain_label.clone(),
        blocks: ready
            .blocks()
            .into_iter()
            .map(|block| StudioBlockView {
                detail: line(
                    [
                        block.role,
                        block.where_,
                        block.latency.text(studio),
                        block.channel_label,
                    ]
                    .into_iter()
                    .chain(block.note),
                ),
                title: format!("{}: {}", block.title, block.model),
                open: block.leg == ready.leg,
                leg: block.leg,
            })
            .collect(),
        editor: editor_view(ready),
        meter: meter_view(ready),
        residency: {
            let residency = ready.residency();
            StudioResidencyView {
                heading: labels.residency_heading.clone(),
                in_region: residency.in_region,
                text: residency.text,
                legs_out: residency.legs_out,
            }
        },
        notice: notice(&section.save, studio),
        pending: if dirty {
            &labels.unsaved
        } else {
            &labels.all_saved
        }
        .clone(),
        dirty,
        save_label: labels.save.clone(),
        can_save: section.can_save() && can_change,
        saving: section.is_saving(),
    }
}

fn choice(value: &str, label: &str) -> StudioChoiceView {
    StudioChoiceView {
        value: value.to_owned(),
        label: label.to_owned(),
    }
}

/// The parts of `parts` that say something, joined by middle dots.
fn line(parts: impl IntoIterator<Item = String>) -> String {
    parts
        .into_iter()
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>()
        .join(DOT)
}

fn recipe_view(recipe: &StudioRecipe, base: &str) -> StudioRecipeView {
    StudioRecipeView {
        recipe_id: recipe.id.clone(),
        name: recipe.name.clone(),
        description: recipe.description.clone(),
        facts: line(
            [
                recipe.time_to_first_word.text.clone(),
                recipe.residency.text.clone(),
                recipe.channel_label.clone(),
            ]
            .into_iter()
            .chain(recipe.note.clone()),
        ),
        is_default: recipe.is_default,
        selected: recipe.id == base,
    }
}

fn editor_view(ready: &StudioReady) -> StudioEditorView {
    let labels = &ready.studio.labels;
    let title = ready
        .blocks()
        .into_iter()
        .find(|block| block.leg == ready.leg)
        .map_or_else(|| labels.edit_leg.clone(), |block| block.title);
    let mut pickers: Vec<StudioPickerView> = ready
        .pickers()
        .into_iter()
        .map(|picker| {
            let selected = picker.selected.clone();
            StudioPickerView {
                picker: match picker.kind {
                    PickerKind::Vendor => StudioPicker::Vendor,
                    PickerKind::Model => StudioPicker::Model,
                    PickerKind::Location => StudioPicker::Location,
                },
                label: picker.label,
                options: listed(
                    picker.options.iter().map(option_view).collect(),
                    &selected,
                    &selected,
                ),
                selected,
            }
        })
        .collect();
    pickers.extend(ready.voice_picker().map(|voice| {
        let options = voice
            .voices
            .iter()
            .map(|(group, option)| StudioChoiceView {
                value: option.value.clone(),
                label: if group.is_empty() {
                    option.label.clone()
                } else {
                    format!("{} ({group})", option.label)
                },
            })
            .collect();
        StudioPickerView {
            picker: StudioPicker::Voice,
            options: listed(options, &voice.selected, voice.selected_label()),
            label: voice.label,
            selected: voice.selected,
        }
    }));
    let controls = tuning_views(ready);
    StudioEditorView {
        title,
        pickers,
        has_advanced: controls.iter().any(|control| control.advanced),
        controls,
        advanced_label: labels.advanced.clone(),
    }
}

fn option_view(option: &PickerOption) -> StudioChoiceView {
    StudioChoiceView {
        value: option.value.clone(),
        label: option.text(),
    }
}

/// `options`, with the held value last under `unlisted` when they lack it, so
/// a picker always shows what is held.
fn listed(
    mut options: Vec<StudioChoiceView>,
    selected: &str,
    unlisted: &str,
) -> Vec<StudioChoiceView> {
    if !options.iter().any(|option| option.value == selected) {
        options.push(choice(selected, unlisted));
    }
    options
}

fn tuning_views(ready: &StudioReady) -> Vec<StudioTuningView> {
    let mut interruptions = false;
    ready
        .controls()
        .into_iter()
        .map(|control| {
            let key = control.key().clone();
            let advanced = key.section != MAIN_SECTION;
            let first_interruption =
                advanced && !interruptions && key.key.starts_with(INTERRUPTION_PREFIX);
            interruptions |= first_interruption;
            StudioTuningView {
                heading: first_interruption.then(|| ready.studio.labels.interruptions.clone()),
                control: control_view(control),
                key: key.key,
                label: key.label,
                description: key.description,
                advanced,
            }
        })
        .collect()
}

/// `value` in thousandths.
fn thousandths(value: f64) -> i64 {
    (value * SLIDER_SCALE).round() as i64
}

fn control_view(control: TuningControl) -> StudioControlView {
    match control {
        TuningControl::Number {
            value,
            range,
            can_unset,
            ..
        } => {
            let places = if range.whole() { 0 } else { 2 };
            StudioControlView::Slider {
                min: thousandths(range.min),
                max: thousandths(range.max),
                step: range.step.map_or(0, thousandths),
                value: value.map(thousandths),
                start: thousandths(range.start),
                value_text: format!("{:.*}", places, value.unwrap_or(range.start)),
                default_label: range.use_default_label.filter(|_| can_unset),
            }
        }
        TuningControl::Choice {
            options, selected, ..
        } => StudioControlView::Select {
            options: options.iter().map(option_view).collect(),
            selected,
        },
        TuningControl::Flag { checked, .. } => StudioControlView::Switch { on: checked },
        TuningControl::Lines { terms, .. } => StudioControlView::Lines {
            text: terms.join("\n"),
        },
    }
}

fn meter_view(ready: &StudioReady) -> StudioMeterView {
    let studio = &ready.studio;
    let meter = ready.meter();
    StudioMeterView {
        heading: studio.labels.meter_heading.clone(),
        description: studio.labels.meter_description.clone(),
        headline: meter.headline_text(studio),
        note: meter.note,
        stages: meter
            .stages
            .into_iter()
            .map(|stage| FactView {
                label: stage.label,
                value: stage.value.text(studio),
            })
            .collect(),
        source: studio.latency.source_text.clone(),
    }
}

/// What the last save says, in the read's words where it has them.
fn notice(save: &StudioSaveState, studio: &VoiceStudioResponse) -> Option<StudioNoticeView> {
    let labels = &studio.labels;
    let (text, tone) = match save {
        StudioSaveState::Idle | StudioSaveState::Saving => return None,
        StudioSaveState::Saved => (labels.saved.clone(), StudioNoticeTone::Success),
        StudioSaveState::Mismatch => (labels.save_failed.clone(), StudioNoticeTone::Error),
        StudioSaveState::Failed(failure) => (
            format!("{} {}", labels.save_failed, failure.message),
            StudioNoticeTone::Error,
        ),
        StudioSaveState::SavedButStale(failure) => (
            format!("{} {}", VoiceStudioSection::SAVED_STALE, failure.message),
            StudioNoticeTone::Warning,
        ),
    };
    Some(StudioNoticeView { text, tone })
}

/// A view with nothing read yet, for the shared tests that carry one across
/// the boundary.
#[cfg(test)]
pub(crate) fn sample() -> VoiceStudioView {
    voice_studio_view(None, true)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_slider_crosses_in_thousandths() {
        assert_eq!(thousandths(0.3), 300);
        assert_eq!(thousandths(0.05), 50);
        assert_eq!(thousandths(1500.0), 1_500_000);
        let events = events(VoiceStudioAction::SetNumber {
            key: "engineMix.turn.minDelay".to_owned(),
            value: Some(350),
        });
        assert_eq!(
            events,
            [Event::VoiceStudio(VoiceStudioEvent::Edit(
                StudioEdit::Number {
                    key: "engineMix.turn.minDelay".to_owned(),
                    value: Some(0.35),
                }
            ))]
        );
    }

    #[test]
    fn a_held_value_the_list_lacks_is_shown_last() {
        let options = vec![choice("a", "A")];
        assert_eq!(listed(options.clone(), "a", "Choose"), options);
        assert_eq!(
            listed(options, "b", "Choose"),
            [choice("a", "A"), choice("b", "Choose")]
        );
    }

    #[test]
    fn the_sample_has_nothing_read() {
        let view = sample();
        assert_eq!(view.title, VOICE_STUDIO_TITLE);
        assert_eq!(view.status, LoadStatus::Loading);
        assert_eq!(view.studio, None);
    }
}
