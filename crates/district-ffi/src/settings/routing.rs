//! Which calls go where: the routing rules, as District AI for Linux's routing
//! page shows them (`district-app/src/pages/routing.rs`).
//!
//! Each stored rule is a card of its own, edited one key at a time with the web
//! console's choices; a stored value those choices do not list is shown as it
//! is stored (the settings kit's picker). A rule's engine is shown and
//! not changed here. The save replaces every rule, so the core asks first.

use district_core::{
    Capabilities, Event, Model, Route, RoutingRulesEvent, RoutingRulesSection, SignedIn,
    WorkspaceSection,
};
use district_model::{
    ROUTING_FIELDS, ROUTING_OPERATORS, ROUTING_VOICES, RoutingRule, RoutingRuleField,
};
use serde::Serialize;

use super::call_handling::QuestionView;
use super::{
    ChoiceView, PickerView, SaveNoticeView, SectionStatus, save_notice, section_status, unlisted,
};
use crate::l10n::{cs, l, s};
use crate::screen::ScreenView;
use crate::views::EmptyView;

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The keys of a rule the builder reads and writes. Any other key a stored
/// rule holds goes back as it came.
const BUILDER_KEYS: [&str; 7] = [
    "id",
    "field",
    "operator",
    "value",
    "voice",
    "model",
    "instruction",
];

/// A stored key as words: `estimatedValue` reads "Estimated value".
pub(crate) fn key_words(key: &str) -> String {
    let mut words = String::new();
    for (at, c) in key.char_indices() {
        if at == 0 {
            words.extend(c.to_uppercase());
        } else if c.is_uppercase() {
            words.push(' ');
            words.extend(c.to_lowercase());
        } else {
            words.push(c);
        }
    }
    words
}

/// A caller detail a rule compares, as the picker and the rule's line show
/// it: the catalog's label (the web portal's) for a detail the builder
/// offers, the stored key as words for any other.
pub(crate) fn field_words(field: &str) -> String {
    let label = match field {
        "industry" => s::routing_field_industry(l()),
        "estimatedValue" => s::routing_field_estimated_value(l()),
        "callerType" => s::routing_field_caller_type(l()),
        "lineType" => s::routing_field_line_type(l()),
        "isDecisionMaker" => s::routing_field_is_decision_maker(l()),
        "seniority" => s::routing_field_seniority(l()),
        other => return key_words(other),
    };
    label.to_owned()
}

/// A comparison, as the picker shows it: the catalog's label (the web
/// portal's) for one the builder offers, the stored word as words for any
/// other.
pub(crate) fn operator_words(operator: &str) -> String {
    known_operator(operator).map_or_else(|| key_words(operator), str::to_owned)
}

/// The catalog's label for a comparison the builder offers.
fn known_operator(operator: &str) -> Option<&'static str> {
    match operator {
        "contains" => Some(s::routing_operator_contains(l())),
        "equals" => Some(s::routing_operator_equals(l())),
        _ => None,
    }
}

/// The line under a rule: what it matches, and the voice it gives.
pub(crate) fn rule_line(rule: &RoutingRule) -> String {
    let field = rule.get(RoutingRuleField::Field);
    let voice = rule.get(RoutingRuleField::Voice);
    let condition = if field.is_empty() {
        s::routing_no_condition(l()).to_owned()
    } else {
        // Mid-sentence, a comparison the builder offers is its label in lower
        // case (`contains`, `contient`); any other is shown as stored.
        let operator = rule.get(RoutingRuleField::Operator);
        let operator =
            known_operator(operator).map_or_else(|| operator.to_owned(), str::to_lowercase);
        s::routing_condition(
            l(),
            &field_words(field),
            &operator,
            rule.get(RoutingRuleField::Value),
        )
    };
    if voice.is_empty() {
        condition
    } else {
        format!("{condition} \u{b7} {voice}")
    }
}

/// What a rule stores beyond the builder's keys, which is kept as it is.
pub(crate) fn kept_keys(rule: &RoutingRule) -> Option<String> {
    let mut kept: Vec<String> = rule
        .as_json()
        .keys()
        .filter(|key| !BUILDER_KEYS.contains(&key.as_str()))
        .map(|key| key_words(key))
        .collect();
    kept.sort();
    (!kept.is_empty()).then(|| s::routing_kept_keys(l(), &kept.join(", ")))
}

/// A rule's engine, in words: the persona's own when none is set.
pub(crate) fn engine_words(rule: &RoutingRule) -> String {
    match rule.get(RoutingRuleField::Model) {
        "" => s::routing_engine_default(l()).to_owned(),
        model => model.to_owned(),
    }
}

/// A field of a rule the member edits here. The engine is not one: it is
/// shown and kept.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, uniffi::Enum)]
pub enum RoutingField {
    /// What about the caller is compared.
    Field,
    /// How.
    Operator,
    /// What it is compared with.
    Value,
    /// The voice for a matching caller.
    Voice,
    /// What the receptionist is told for a matching caller.
    Instruction,
}

impl RoutingField {
    fn field(self) -> RoutingRuleField {
        match self {
            Self::Field => RoutingRuleField::Field,
            Self::Operator => RoutingRuleField::Operator,
            Self::Value => RoutingRuleField::Value,
            Self::Voice => RoutingRuleField::Voice,
            Self::Instruction => RoutingRuleField::Instruction,
        }
    }
}

/// One of a rule's pickers, under its label.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RulePickerView {
    /// Its label ("Caller detail", "Compared by", "Voice").
    pub label: String,
    /// The web console's choices, with the rule's value chosen; a stored
    /// value they do not list is shown as stored.
    pub picker: PickerView,
}

/// One rule, as its card shows it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RoutingRuleView {
    /// Its position, which its edits and its removal name.
    pub index: u32,
    /// "Rule 1".
    pub heading: String,
    /// What it matches, and the voice it gives.
    pub summary: String,
    /// What about the caller is compared ("Caller detail").
    pub field: RulePickerView,
    /// How ("Compared by").
    pub operator: RulePickerView,
    /// What it is compared with.
    pub value: String,
    /// The voice ("Voice").
    pub voice: RulePickerView,
    /// What the receptionist is told.
    pub instruction: String,
    /// The engine, in words: shown, and not changed here.
    pub engine: String,
    /// What the rule stores beyond the builder's keys, kept as it is.
    pub kept: Option<String>,
}

/// The routing rules section.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RoutingView {
    /// The heading, `routing.title`.
    pub title: String,
    /// What it says under the heading, `routing.intro`.
    pub intro: String,
    /// Where the page stands. No list is offered before the settings are
    /// read: a list not built from them could only save over them.
    pub status: SectionStatus,
    /// Rules stored in a shape this app cannot change whole: said in place of
    /// the list, with no control. Not a failure, and a retry cannot help.
    pub unmodellable: Option<EmptyView>,
    /// The rules, in the order they are checked: the edited list, else the
    /// stored one.
    pub rules: Vec<RoutingRuleView>,
    /// What to say when there is no rule.
    pub empty: Option<EmptyView>,
    /// Whether the rules can be changed: read, editable here, the member may,
    /// and no save on its way.
    pub can_edit: bool,
    /// Whether "Save" works: the list differs from the stored one.
    pub can_save: bool,
    /// Whether a save is on its way (show a progress ring).
    pub saving: bool,
    /// How the last save ended, until dismissed or the list changes.
    pub notice: Option<SaveNoticeView>,
    /// The core's question before the save, while it asks.
    pub confirming: Option<QuestionView>,
}

impl Default for RoutingView {
    /// The section before anything is read.
    fn default() -> Self {
        Self {
            title: s::routing_title(l()).to_owned(),
            intro: s::routing_intro(l()).to_owned(),
            status: SectionStatus::Loading,
            unmodellable: None,
            rules: Vec::new(),
            empty: None,
            can_edit: false,
            can_save: false,
            saving: false,
            notice: None,
            confirming: None,
        }
    }
}

/// Something the member did on the routing rules section.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum RoutingAction {
    /// Open the routing rules section.
    Open,
    /// Add a rule with the web console's defaults. Nothing is saved yet.
    Add,
    /// Change one field of one rule, keeping its other keys.
    Edit {
        /// The rule's position.
        index: u32,
        /// Which field.
        field: RoutingField,
        /// The new value.
        value: String,
    },
    /// Take one rule off the list. Nothing is saved yet.
    Remove {
        /// The rule's position.
        index: u32,
    },
    /// "Save": the core asks first.
    Save,
    /// Answer the question yes.
    ConfirmSave,
    /// Answer it no.
    CancelSave,
    /// Put the save's notice away.
    DismissNotice,
}

/// The core events `action` is, in the order the core is to hear them.
pub(crate) fn events(action: RoutingAction) -> Vec<Event> {
    vec![match action {
        RoutingAction::Open => Event::Navigate(Route::Workspace(WorkspaceSection::Routing)),
        RoutingAction::Add => Event::RoutingRules(RoutingRulesEvent::Add),
        RoutingAction::Edit {
            index,
            field,
            value,
        } => Event::RoutingRules(RoutingRulesEvent::Edit {
            index: index as usize,
            field: field.field(),
            value,
        }),
        RoutingAction::Remove { index } => {
            Event::RoutingRules(RoutingRulesEvent::Remove(index as usize))
        }
        RoutingAction::Save => Event::RoutingRules(RoutingRulesEvent::Save),
        RoutingAction::ConfirmSave => Event::RoutingRules(RoutingRulesEvent::ConfirmSave),
        RoutingAction::CancelSave => Event::RoutingRules(RoutingRulesEvent::CancelSave),
        RoutingAction::DismissNotice => Event::RoutingRules(RoutingRulesEvent::DismissNotice),
    }]
}

/// A picker labelled `label` over `values` (each shown as `words` says), with
/// the rule's `stored` value chosen.
fn picker(label: &str, values: &[&str], words: fn(&str) -> String, stored: &str) -> RulePickerView {
    RulePickerView {
        label: label.to_owned(),
        picker: PickerView {
            choices: values
                .iter()
                .map(|value| ChoiceView {
                    value: (*value).to_owned(),
                    label: words(value),
                })
                .collect(),
            selected: stored.to_owned(),
            selected_label: if values.contains(&stored) {
                words(stored)
            } else {
                unlisted(stored)
            },
        },
    }
}

fn rule_view(index: usize, rule: &RoutingRule) -> RoutingRuleView {
    RoutingRuleView {
        index: u32::try_from(index).unwrap_or(u32::MAX),
        heading: s::routing_rule_heading(l(), i64::try_from(index + 1).unwrap_or(i64::MAX)),
        summary: rule_line(rule),
        field: picker(
            s::routing_picker_field(l()),
            ROUTING_FIELDS,
            field_words,
            rule.get(RoutingRuleField::Field),
        ),
        operator: picker(
            s::routing_picker_operator(l()),
            ROUTING_OPERATORS,
            operator_words,
            rule.get(RoutingRuleField::Operator),
        ),
        value: rule.get(RoutingRuleField::Value).to_owned(),
        voice: picker(
            s::routing_picker_voice(l()),
            ROUTING_VOICES,
            str::to_owned,
            rule.get(RoutingRuleField::Voice),
        ),
        instruction: rule.get(RoutingRuleField::Instruction).to_owned(),
        engine: engine_words(rule),
        kept: kept_keys(rule),
    }
}

/// The section as the core holds it, for a member with `capabilities`.
pub(crate) fn routing_view(
    section: &RoutingRulesSection,
    capabilities: &Capabilities,
) -> RoutingView {
    let mut view = RoutingView {
        status: section_status(&section.config, &section.save),
        confirming: section.confirmation().map(|confirm| QuestionView {
            title: confirm.title().to_owned(),
            body: confirm.body(),
            action: confirm.action().to_owned(),
            destructive: confirm.rules == 0,
        }),
        ..RoutingView::default()
    };
    if view.status != SectionStatus::Ready {
        return view;
    }
    if section.unmodellable() {
        view.unmodellable = Some(EmptyView::new(
            cs::routing_unmodellable_title(l()),
            cs::routing_unmodellable_body(l()),
        ));
        return view;
    }
    let rules = section.rules();
    view.rules = rules
        .iter()
        .enumerate()
        .map(|(index, rule)| rule_view(index, rule))
        .collect();
    view.empty = rules
        .is_empty()
        .then(|| EmptyView::new(cs::routing_empty_title(l()), cs::routing_empty_body(l())));
    view.can_edit = capabilities.can_change && section.editable();
    view.can_save = capabilities.can_change && section.can_save();
    view.saving = section.save.is_busy();
    view.notice = save_notice(&section.save);
    view
}

/// The page of the routing rules section, for a signed-in model. Until the
/// core has opened the section, it is being read.
pub(crate) fn screen(_model: &Model, signed_in: &SignedIn) -> ScreenView {
    ScreenView::Routing {
        view: signed_in
            .routing_rules
            .as_ref()
            .map_or_else(RoutingView::default, |section| {
                routing_view(section, &signed_in.capabilities())
            }),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_rule_reads_as_what_it_matches_and_what_it_keeps() {
        assert_eq!(key_words("estimatedValue"), "Estimated value");
        assert_eq!(key_words("isDecisionMaker"), "Is decision maker");
        assert_eq!(key_words(""), "");
        let rule = RoutingRule::new("rule-1");
        assert_eq!(rule_line(&rule), "Industry contains \"\" \u{b7} Puck");
        assert_eq!(kept_keys(&rule), None);
        assert_eq!(engine_words(&rule), "The persona's own");
        let tuned = rule
            .with(RoutingRuleField::Model, "deepgram-pipeline")
            .with(RoutingRuleField::Voice, "")
            .with(RoutingRuleField::Field, "");
        assert_eq!(engine_words(&tuned), "deepgram-pipeline");
        assert_eq!(rule_line(&tuned), "No condition set here");
    }

    #[test]
    fn the_builders_details_and_comparisons_read_as_the_portal_labels_them() {
        use district_l10n::{Locale, with_locale};

        let english: Vec<String> = ROUTING_FIELDS.iter().map(|f| field_words(f)).collect();
        assert_eq!(
            english,
            [
                "Industry",
                "Estimated value",
                "Caller type (business/consumer)",
                "Line type (mobile/landline/voip)",
                "Decision maker (true/false)",
                "Seniority (e.g. vp, c-suite)",
            ]
        );
        let operators: Vec<String> = ROUTING_OPERATORS
            .iter()
            .map(|o| operator_words(o))
            .collect();
        assert_eq!(operators, ["Contains", "Equals"]);
        // A key the builder does not offer reads as words, as stored.
        assert_eq!(field_words("annualRevenue"), "Annual revenue");
        assert_eq!(operator_words("greaterThan"), "Greater than");
        let rule = RoutingRule::new("rule-1")
            .with(RoutingRuleField::Field, "estimatedValue")
            .with(RoutingRuleField::Operator, "equals")
            .with(RoutingRuleField::Value, "High");
        assert_eq!(
            rule_line(&rule),
            "Estimated value equals \"High\" \u{b7} Puck"
        );
        let other = rule.clone().with(RoutingRuleField::Operator, "greaterThan");
        assert_eq!(
            rule_line(&other),
            "Estimated value greaterThan \"High\" \u{b7} Puck"
        );

        with_locale(Locale::FrCa, || {
            let french: Vec<String> = ROUTING_FIELDS.iter().map(|f| field_words(f)).collect();
            assert_eq!(
                french,
                [
                    "Secteur d'activité",
                    "Valeur estimée",
                    "Type d'appelant (business/consumer)",
                    "Type de ligne (mobile/landline/voip)",
                    "Décideur (true/false)",
                    "Niveau hiérarchique (p. ex. vp, c-suite)",
                ]
            );
            assert_eq!(operator_words("contains"), "Contient");
            assert_eq!(operator_words("equals"), "Égal à");
            assert_eq!(
                rule_line(&rule),
                "Valeur estimée égal à «\u{a0}High\u{a0}» \u{b7} Puck"
            );
            let picked = picker("Champ", ROUTING_FIELDS, field_words, "industry");
            assert_eq!(picked.picker.selected_label, "Secteur d'activité");
        });
    }

    #[test]
    fn a_stored_value_the_choices_lack_is_shown_as_stored() {
        let listed = picker(
            "Caller detail",
            ROUTING_FIELDS,
            field_words,
            "estimatedValue",
        );
        assert_eq!(listed.picker.selected_label, "Estimated value");
        assert_eq!(listed.picker.choices.len(), ROUTING_FIELDS.len());
        let other = picker("Voice", ROUTING_VOICES, str::to_owned, "Orpheus");
        assert_eq!(other.picker.selected, "Orpheus");
        assert_eq!(other.picker.selected_label, "Orpheus");
        let blank = picker("Voice", ROUTING_VOICES, str::to_owned, " ");
        assert_eq!(blank.picker.selected_label, s::settings_not_chosen(l()));
    }
}
