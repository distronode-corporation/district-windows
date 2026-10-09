//! Which calls go where: the routing rules, as District AI for Linux's routing
//! page shows them (`district-app/src/pages/routing.rs`).
//!
//! Each stored rule is a card of its own, edited one key at a time with the web
//! console's choices; a stored value those choices do not list is shown as it
//! is stored, and choosing it again sends nothing. A rule's engine is shown and
//! not changed here. The save replaces every rule, so the core asks first.

use district_core::{
    Capabilities, Event, Model, Route, RoutingRulesEvent, RoutingRulesSection, SignedIn,
    WorkspaceSection,
};
use district_model::{
    ROUTING_FIELDS, ROUTING_OPERATORS, ROUTING_VOICES, RoutingRule, RoutingRuleField,
};
use serde::Serialize;

use super::call_handling::{QuestionView, SaveView, config_status};
use crate::screen::ScreenView;
use crate::views::{EmptyView, LoadStatus};

/// Whether this version has the area's screens. The packet that builds them
/// sets it; until then [`crate::nav::built`] says no for its routes.
pub(crate) const BUILT: bool = true;

/// The section's heading: the hub row's title, which opens it.
pub const ROUTING_TITLE: &str = "Call routing rules";
/// What the section says under its heading.
pub const ROUTING_INTRO: &str = "Which callers get which voice and instruction. Rules are \
    checked in this order, and saving replaces every rule stored.";
/// What a picker shows for a stored value that is empty.
pub const NOT_CHOSEN: &str = "Not chosen";

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

/// The line under a rule: what it matches, and the voice it gives.
pub(crate) fn rule_line(rule: &RoutingRule) -> String {
    let field = rule.get(RoutingRuleField::Field);
    let voice = rule.get(RoutingRuleField::Voice);
    let condition = if field.is_empty() {
        "No condition set here".to_owned()
    } else {
        format!(
            "{} {} \"{}\"",
            key_words(field),
            rule.get(RoutingRuleField::Operator),
            rule.get(RoutingRuleField::Value)
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
    (!kept.is_empty()).then(|| {
        format!(
            "Also stored: {}. Kept as they are when the rules are saved.",
            kept.join(", ")
        )
    })
}

/// A rule's engine, in words: the persona's own when none is set.
pub(crate) fn engine_words(rule: &RoutingRule) -> String {
    match rule.get(RoutingRuleField::Model) {
        "" => "The persona's own".to_owned(),
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

/// One choice of a picker.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct ChoiceView {
    /// The value it sends; `None` for a stored value the choices do not list,
    /// shown as stored, which sends nothing.
    pub value: Option<String>,
    /// What it shows.
    pub label: String,
}

/// A picker of one of a rule's keys.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct PickerView {
    /// Its label.
    pub label: String,
    /// Its choices, in order: the web console's, then the stored value when
    /// they do not list it.
    pub choices: Vec<ChoiceView>,
    /// The position of the choice the rule holds.
    pub selected: u32,
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
    pub field: PickerView,
    /// How ("Compared by").
    pub operator: PickerView,
    /// What it is compared with.
    pub value: String,
    /// The voice ("Voice").
    pub voice: PickerView,
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
    /// The heading, [`ROUTING_TITLE`].
    pub title: String,
    /// What it says under the heading, [`ROUTING_INTRO`].
    pub intro: String,
    /// Where the read stands. No list is offered before the settings are
    /// read: a list not built from them could only save over them.
    pub status: LoadStatus,
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
    /// How the last save ended.
    pub save: SaveView,
    /// The core's question before the save, while it asks.
    pub confirming: Option<QuestionView>,
}

impl Default for RoutingView {
    /// The section before anything is read.
    fn default() -> Self {
        Self {
            title: ROUTING_TITLE.to_owned(),
            intro: ROUTING_INTRO.to_owned(),
            status: LoadStatus::Loading,
            unmodellable: None,
            rules: Vec::new(),
            empty: None,
            can_edit: false,
            can_save: false,
            save: SaveView::default(),
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
/// the rule's `stored` value chosen, listed last when they do not have it.
fn picker(label: &str, values: &[&str], words: fn(&str) -> String, stored: &str) -> PickerView {
    let mut choices: Vec<ChoiceView> = values
        .iter()
        .map(|value| ChoiceView {
            value: Some((*value).to_owned()),
            label: words(value),
        })
        .collect();
    let selected = match values.iter().position(|value| *value == stored) {
        Some(at) => at,
        None => {
            choices.push(ChoiceView {
                value: None,
                label: if stored.trim().is_empty() {
                    NOT_CHOSEN.to_owned()
                } else {
                    stored.to_owned()
                },
            });
            values.len()
        }
    };
    PickerView {
        label: label.to_owned(),
        choices,
        selected: u32::try_from(selected).unwrap_or(u32::MAX),
    }
}

fn rule_view(index: usize, rule: &RoutingRule) -> RoutingRuleView {
    RoutingRuleView {
        index: u32::try_from(index).unwrap_or(u32::MAX),
        heading: format!("Rule {}", index + 1),
        summary: rule_line(rule),
        field: picker(
            "Caller detail",
            ROUTING_FIELDS,
            key_words,
            rule.get(RoutingRuleField::Field),
        ),
        operator: picker(
            "Compared by",
            ROUTING_OPERATORS,
            key_words,
            rule.get(RoutingRuleField::Operator),
        ),
        value: rule.get(RoutingRuleField::Value).to_owned(),
        voice: picker(
            "Voice",
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
        status: config_status(&section.config, &section.save),
        confirming: section.confirmation().map(|confirm| QuestionView {
            title: confirm.title().to_owned(),
            body: confirm.body(),
            action: confirm.action().to_owned(),
            destructive: confirm.rules == 0,
        }),
        ..RoutingView::default()
    };
    if view.status != LoadStatus::Ready {
        return view;
    }
    if section.unmodellable() {
        view.unmodellable = Some(EmptyView::new(
            RoutingRulesSection::UNMODELLABLE_TITLE,
            RoutingRulesSection::UNMODELLABLE_BODY,
        ));
        return view;
    }
    let rules = section.rules();
    view.rules = rules
        .iter()
        .enumerate()
        .map(|(index, rule)| rule_view(index, rule))
        .collect();
    view.empty = rules.is_empty().then(|| {
        EmptyView::new(
            RoutingRulesSection::EMPTY_TITLE,
            RoutingRulesSection::EMPTY_BODY,
        )
    });
    view.can_edit = capabilities.can_change && section.editable();
    view.can_save = capabilities.can_change && section.can_save();
    view.save = SaveView::of(&section.save);
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
    fn a_stored_value_the_choices_lack_is_listed_last_and_sends_nothing() {
        let listed = picker("Voice", ROUTING_VOICES, str::to_owned, "Fenrir");
        assert_eq!(listed.selected, 1);
        assert_eq!(listed.choices.len(), ROUTING_VOICES.len());
        let unlisted = picker("Voice", ROUTING_VOICES, str::to_owned, "Orpheus");
        assert_eq!(unlisted.selected as usize, ROUTING_VOICES.len());
        assert_eq!(
            unlisted.choices.last(),
            Some(&ChoiceView {
                value: None,
                label: "Orpheus".to_owned()
            })
        );
        let blank = picker("Voice", ROUTING_VOICES, str::to_owned, " ");
        assert_eq!(blank.choices.last().unwrap().label, NOT_CHOSEN);
    }
}
