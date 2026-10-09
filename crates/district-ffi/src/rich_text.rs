//! Rich text: the safe subset of Markdown District HQ answers in, as blocks of
//! styled runs that C# draws as it reads them.
//!
//! An answer is written by a model on the service, so none of it is markup to
//! the app. Parsing reads the raw text for its structure, and every piece of
//! text it finds becomes the text of a run: C# builds a `RichTextBlock` from the
//! runs and never parses or escapes anything. A marker that does not pair up
//! is text like any other, and so is raw HTML.
//!
//! The subset is the Linux app's (`markdown.rs`): paragraphs, emphasis, bulleted
//! and numbered lists, inline code and code blocks, headings, block quotes (as
//! their text) and links. A link is a link only when it goes to a web page
//! ([`is_web_link`]); any other link is shown as its words. The run carries the
//! address, which C# hands back through `HqAction::OpenLink`, and the core
//! checks it again before it opens anything. Tables and images are shown as the
//! text they are.

use district_core::is_web_link;
use serde::Serialize;

/// How deep emphasis may nest before the rest is shown as text.
const MAX_DEPTH: usize = 6;

/// The deepest a list item is indented.
const MAX_LIST_DEPTH: usize = 3;

/// Text written in the subset, as blocks.
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RichTextView {
    /// The blocks, in reading order.
    pub blocks: Vec<RichBlock>,
}

/// One block of rich text. Consecutive list items sit on lines of their own
/// with no gap between them; every other block has a gap before it.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Enum)]
pub enum RichBlock {
    /// A paragraph. A line break inside it is a `\n` in a run's text.
    Paragraph {
        /// Its text.
        runs: Vec<RichRun>,
    },
    /// A heading, of any level: shown as a strong line.
    Heading {
        /// Its text.
        runs: Vec<RichRun>,
    },
    /// An item of a bulleted or numbered list.
    ListItem {
        /// The marker as it reads: a bullet, or the item's number and a full
        /// stop.
        marker: String,
        /// How far it is indented, 0 to 3.
        depth: u32,
        /// Its text.
        runs: Vec<RichRun>,
    },
    /// A code block, shown as it is in a fixed-width font.
    Code {
        /// Its lines, joined by `\n`.
        text: String,
    },
}

/// A stretch of text in one style.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, uniffi::Record)]
pub struct RichRun {
    /// The text, shown exactly as it is.
    pub text: String,
    /// Strong emphasis.
    pub bold: bool,
    /// Emphasis.
    pub italic: bool,
    /// Inline code, in a fixed-width font.
    pub code: bool,
    /// The web page the run links to, when it is a link: always `https://` or
    /// `http://` ([`is_web_link`]). Opening it goes back through the core.
    pub link: Option<String>,
}

/// `text`, Markdown, as rich text.
pub fn parse(text: &str) -> RichTextView {
    let mut blocks: Vec<RichBlock> = Vec::new();
    let mut paragraph: Vec<&str> = Vec::new();
    let mut code: Option<Vec<&str>> = None;
    for line in text.lines() {
        let trimmed = line.trim_start();
        let fence = trimmed.starts_with("```") || trimmed.starts_with("~~~");
        if let Some(lines) = code.as_mut() {
            if fence {
                blocks.push(RichBlock::Code {
                    text: lines.join("\n"),
                });
                code = None;
            } else {
                lines.push(line);
            }
            continue;
        }
        let rule = is_rule(trimmed);
        let item = list_item(trimmed);
        let heading = heading(trimmed);
        let breaks = fence || rule || trimmed.is_empty() || item.is_some() || heading.is_some();
        if breaks {
            flush(&mut paragraph, &mut blocks);
        }
        if fence {
            code = Some(Vec::new());
        } else if rule {
            // A rule reads as the break between paragraphs it is.
        } else if let Some(heading) = heading {
            blocks.push(RichBlock::Heading {
                runs: inline(heading),
            });
        } else if let Some((marker, rest)) = item {
            let depth = ((line.len() - trimmed.len()) / 2).min(MAX_LIST_DEPTH);
            blocks.push(RichBlock::ListItem {
                marker,
                depth: u32::try_from(depth).unwrap_or(0),
                runs: inline(rest),
            });
        } else if !breaks {
            let quoted = trimmed.strip_prefix('>').map(str::trim_start);
            paragraph.push(quoted.unwrap_or(trimmed).trim_end());
        }
    }
    if let Some(lines) = code {
        blocks.push(RichBlock::Code {
            text: lines.join("\n"),
        });
    }
    flush(&mut paragraph, &mut blocks);
    RichTextView { blocks }
}

/// The paragraph gathered so far, as a block, if there is one.
fn flush(paragraph: &mut Vec<&str>, blocks: &mut Vec<RichBlock>) {
    if !paragraph.is_empty() {
        blocks.push(RichBlock::Paragraph {
            runs: inline(&paragraph.join("\n")),
        });
        paragraph.clear();
    }
}

/// A heading's text, for a line of one to six `#`s and a space.
fn heading(line: &str) -> Option<&str> {
    let hashes = line.chars().take_while(|&c| c == '#').count();
    let rest = line[hashes..].strip_prefix(' ')?;
    (1..=6)
        .contains(&hashes)
        .then(|| rest.trim().trim_end_matches('#').trim_end())
}

/// A line of three or more dashes, stars or underscores, spaces allowed
/// between them: a rule.
fn is_rule(line: &str) -> bool {
    let marks: String = line.chars().filter(|c| !c.is_whitespace()).collect();
    marks.len() >= 3
        && ['-', '*', '_']
            .iter()
            .any(|&mark| marks.chars().all(|c| c == mark))
}

/// A list item's marker as it reads (a bullet, or its number as written) and
/// its text: `- `, `* ` or `+ `, or digits and `.` or `)`, then a space.
fn list_item(line: &str) -> Option<(String, &str)> {
    if let Some(rest) = ["- ", "* ", "+ "]
        .iter()
        .find_map(|marker| line.strip_prefix(marker))
    {
        return Some(("\u{2022}".to_owned(), rest.trim()));
    }
    let digits = line.chars().take_while(char::is_ascii_digit).count();
    let rest = line[digits..]
        .strip_prefix(". ")
        .or_else(|| line[digits..].strip_prefix(") "))?;
    (1..=9)
        .contains(&digits)
        .then(|| (format!("{}.", &line[..digits]), rest.trim()))
}

/// The style a run is written in.
#[derive(Clone, Copy, Debug, Default)]
struct Style<'a> {
    bold: bool,
    italic: bool,
    link: Option<&'a str>,
}

/// Runs being written, each new one joined to the last when they look the same.
#[derive(Default)]
struct Runs(Vec<RichRun>);

impl Runs {
    /// Adds `text`, which is never empty: every caller has found at least one
    /// character.
    fn push(&mut self, text: &str, style: Style<'_>, code: bool) {
        let link = style.link.map(str::to_owned);
        if let Some(last) = self.0.last_mut()
            && last.bold == style.bold
            && last.italic == style.italic
            && last.code == code
            && last.link == link
        {
            last.text.push_str(text);
            return;
        }
        self.0.push(RichRun {
            text: text.to_owned(),
            bold: style.bold,
            italic: style.italic,
            code,
            link,
        });
    }
}

/// A paragraph's text as runs, links included.
fn inline(text: &str) -> Vec<RichRun> {
    let mut runs = Runs::default();
    render(text, true, 0, Style::default(), &mut runs);
    runs.0
}

/// Writes `text` in `style`: emphasis, code, and links when `links` (never
/// inside a link's own words). Everything else is text.
fn render<'a>(text: &'a str, links: bool, depth: usize, style: Style<'a>, runs: &mut Runs) {
    let mut before: Option<char> = None;
    let mut rest = text;
    while let Some(next) = rest.chars().next() {
        let found = if depth < MAX_DEPTH {
            span(rest, before, links, depth, style, runs)
        } else {
            None
        };
        let used = found.unwrap_or_else(|| {
            runs.push(&rest[..next.len_utf8()], style, false);
            next.len_utf8()
        });
        before = rest[..used].chars().last();
        rest = &rest[used..];
    }
}

/// Writes the span at the start of `rest` and answers how many bytes of it the
/// span takes, or writes nothing and answers `None` when nothing starts there.
/// `before` is the character before it, which decides whether `_` and a bare
/// link can start.
fn span<'a>(
    rest: &'a str,
    before: Option<char>,
    links: bool,
    depth: usize,
    style: Style<'a>,
    runs: &mut Runs,
) -> Option<usize> {
    let word_start = before.is_none_or(|c| !c.is_alphanumeric());
    let first = rest.chars().next()?;
    match first {
        '\\' => {
            let escaped = rest[1..]
                .chars()
                .next()
                .filter(char::is_ascii_punctuation)?;
            runs.push(&rest[1..=escaped.len_utf8()], style, false);
            Some(1 + escaped.len_utf8())
        }
        '`' => {
            let (code, used) = code_span(rest)?;
            runs.push(code, style, true);
            Some(used)
        }
        '*' | '_' if first == '*' || word_start => emphasis(rest, first, links, depth, style, runs),
        '[' if links => link(rest, depth, style, runs),
        '<' if links => {
            let end = rest.find('>')?;
            let url = &rest[1..end];
            is_web_link(url).then(|| {
                runs.push(url, linked(style, url), false);
                end + 1
            })
        }
        'h' | 'H' if links && word_start => {
            let url = bare_link(rest)?;
            runs.push(url, linked(style, url), false);
            Some(url.len())
        }
        _ => None,
    }
}

/// `style`, linking to `url`.
fn linked<'a>(style: Style<'a>, url: &'a str) -> Style<'a> {
    Style {
        link: Some(url),
        ..style
    }
}

/// Inline code: a run of backticks, the code, and a run as long. The code and
/// how many bytes the span takes.
fn code_span(rest: &str) -> Option<(&str, usize)> {
    let ticks = rest.chars().take_while(|&c| c == '`').count();
    let fence = &rest[..ticks];
    let close = rest[ticks..].find(fence)? + ticks;
    let code = rest[ticks..close].trim();
    (!code.is_empty()).then_some((code, close + ticks))
}

/// Strong emphasis (`**`, `__`) or emphasis (`*`, `_`), when its closing
/// marker follows and nothing but text is between.
fn emphasis<'a>(
    rest: &'a str,
    mark: char,
    links: bool,
    depth: usize,
    style: Style<'a>,
    runs: &mut Runs,
) -> Option<usize> {
    let doubled: String = [mark, mark].iter().collect();
    let strong = rest.starts_with(&doubled);
    let open = if strong { 2 } else { 1 };
    let body = &rest[open..];
    let close = closing(body, mark, open)?;
    let inner = &body[..close];
    let after = body[close + open..].chars().next();
    let flanked = !inner.starts_with(char::is_whitespace)
        && !inner.ends_with(char::is_whitespace)
        && (mark == '*' || after.is_none_or(|c| !c.is_alphanumeric()));
    flanked.then(|| {
        let style = Style {
            bold: style.bold || strong,
            italic: style.italic || !strong,
            ..style
        };
        render(inner, links, depth + 1, style, runs);
        open + close + open
    })
}

/// Where in `body` the marker closing an emphasis of `width` `mark`s is: the
/// next pair for strong emphasis, the next single one (not one of a pair) for
/// emphasis. Code spans are stepped over, since their markers are text.
fn closing(body: &str, mark: char, width: usize) -> Option<usize> {
    let mut at = 0;
    while at < body.len() {
        let here = &body[at..];
        let run = here.chars().take_while(|&c| c == mark).count();
        // A run closes from its end: `***` closes strong emphasis after an
        // emphasis inside it, and a pair inside emphasis is strong emphasis.
        let closes = if width == 2 {
            run >= 2
        } else {
            run == 1 || run >= 3
        };
        if here.starts_with('`') {
            at += code_span(here).map_or(1, |(_, used)| used);
        } else if run == 0 {
            at += here.chars().next().map_or(1, char::len_utf8);
        } else if at > 0 && closes {
            return Some(at + run - width);
        } else {
            at += run;
        }
    }
    None
}

/// `[words](address)`: a link to a web page, or its words alone for anything
/// else.
fn link<'a>(rest: &'a str, depth: usize, style: Style<'a>, runs: &mut Runs) -> Option<usize> {
    let middle = rest.find("](")?;
    let words = &rest[1..middle];
    if words.contains(['[', '\n']) {
        return None;
    }
    let start = middle + 2;
    let mut open = 0usize;
    let mut end = None;
    for (index, c) in rest[start..].char_indices() {
        match c {
            '(' => open += 1,
            ')' if open == 0 => {
                end = Some(start + index);
                break;
            }
            ')' => open -= 1,
            '\n' => return None,
            _ => {}
        }
    }
    let end = end?;
    // A title after the address is left out.
    let url = rest[start..end].split_whitespace().next().unwrap_or("");
    let style = if is_web_link(url) {
        linked(style, url)
    } else {
        style
    };
    render(words, false, depth + 1, style, runs);
    Some(end + 1)
}

/// A bare web address in the text, up to the space after it, without the
/// punctuation that ends the sentence around it.
fn bare_link(rest: &str) -> Option<&str> {
    let lower = rest.get(..8)?.to_ascii_lowercase();
    if !lower.starts_with("https://") && !lower.starts_with("http://") {
        return None;
    }
    let end = rest
        .find(|c: char| c.is_whitespace() || c == '<')
        .unwrap_or(rest.len());
    let mut url = &rest[..end];
    loop {
        let trimmed = url.trim_end_matches(['.', ',', ';', ':', '!', '?', '\'', '"']);
        let unbalanced = trimmed.ends_with(')') && !trimmed.contains('(');
        let trimmed = if unbalanced {
            &trimmed[..trimmed.len() - 1]
        } else {
            trimmed
        };
        if trimmed == url {
            break;
        }
        url = trimmed;
    }
    is_web_link(url).then_some(url)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A run of `text` with no style.
    fn t(text: &str) -> RichRun {
        RichRun {
            text: text.to_owned(),
            bold: false,
            italic: false,
            code: false,
            link: None,
        }
    }

    fn b(text: &str) -> RichRun {
        RichRun {
            bold: true,
            ..t(text)
        }
    }

    fn i(text: &str) -> RichRun {
        RichRun {
            italic: true,
            ..t(text)
        }
    }

    fn bi(text: &str) -> RichRun {
        RichRun {
            bold: true,
            italic: true,
            ..t(text)
        }
    }

    fn c(text: &str) -> RichRun {
        RichRun {
            code: true,
            ..t(text)
        }
    }

    fn l(text: &str, url: &str) -> RichRun {
        RichRun {
            link: Some(url.to_owned()),
            ..t(text)
        }
    }

    fn p(runs: Vec<RichRun>) -> RichBlock {
        RichBlock::Paragraph { runs }
    }

    /// The runs of `text`, which must be one paragraph.
    fn runs(text: &str) -> Vec<RichRun> {
        match parse(text).blocks.as_slice() {
            [RichBlock::Paragraph { runs }] => runs.clone(),
            other => panic!("{text:?} is not one paragraph: {other:?}"),
        }
    }

    /// The text `text` shows, without its styles, one block to a line.
    fn shown(text: &str) -> String {
        let joined =
            |runs: &[RichRun]| -> String { runs.iter().map(|run| run.text.as_str()).collect() };
        let lines: Vec<String> = parse(text)
            .blocks
            .iter()
            .map(|block| match block {
                RichBlock::Paragraph { runs } | RichBlock::Heading { runs } => joined(runs),
                RichBlock::ListItem { marker, runs, .. } => format!("{marker} {}", joined(runs)),
                RichBlock::Code { text } => text.clone(),
            })
            .collect();
        lines.join("\n")
    }

    #[test]
    fn raw_html_and_entities_stay_text() {
        let hostile = "<b>bold</b> & <span foreground=\"red\">red</span> \
            <a href=\"file:///etc/passwd\">x</a> &amp; '\"";
        assert_eq!(runs(hostile), [t(hostile)]);
        assert_eq!(
            runs("<script>alert(1)</script>"),
            [t("<script>alert(1)</script>")]
        );
        assert_eq!(
            runs("**<i>**"),
            [b("<i>")],
            "markup inside emphasis is its text"
        );
        assert_eq!(runs("`</tt><b>`"), [c("</tt><b>")]);
        assert_eq!(
            parse("```\n</tt><b>\n```").blocks,
            [RichBlock::Code {
                text: "</tt><b>".to_owned()
            }]
        );
        assert_eq!(runs("\\<b>"), [t("<b>")], "an escaped < is text");
    }

    #[test]
    fn paragraphs_lines_and_headings() {
        assert_eq!(
            parse("One.\nTwo.\n\n\nThree.").blocks,
            [p(vec![t("One.\nTwo.")]), p(vec![t("Three.")])]
        );
        assert_eq!(
            parse("## Your calls ##\nToday.").blocks,
            [
                RichBlock::Heading {
                    runs: vec![t("Your calls")]
                },
                p(vec![t("Today.")])
            ]
        );
        assert_eq!(runs("#hashtag"), [t("#hashtag")], "no space, no heading");
        assert_eq!(runs("####### seven"), [t("####### seven")]);
        assert_eq!(runs("> Quoted.\nOn."), [t("Quoted.\nOn.")]);
        assert_eq!(
            parse("Above.\n---\nBelow.").blocks,
            [p(vec![t("Above.")]), p(vec![t("Below.")])]
        );
        assert_eq!(parse("* * *").blocks, []);
        assert_eq!(runs("--"), [t("--")]);
        assert_eq!(parse("").blocks, []);
        assert_eq!(shown(""), "");
    }

    #[test]
    fn lists_keep_their_markers_and_their_depth() {
        let item = |marker: &str, depth: u32, text: &str| RichBlock::ListItem {
            marker: marker.to_owned(),
            depth,
            runs: vec![t(text)],
        };
        assert_eq!(
            parse("Calls:\n- 12 answered\n* 3 missed\n  + 2 today\n\nDone.").blocks,
            [
                p(vec![t("Calls:")]),
                item("\u{2022}", 0, "12 answered"),
                item("\u{2022}", 0, "3 missed"),
                item("\u{2022}", 1, "2 today"),
                p(vec![t("Done.")]),
            ]
        );
        assert_eq!(
            parse("1. First\n2) Second\n10. Tenth").blocks,
            [
                item("1.", 0, "First"),
                item("2.", 0, "Second"),
                item("10.", 0, "Tenth")
            ]
        );
        // Nested past the deepest indent: held at the deepest.
        assert_eq!(
            parse("- a\n  - b\n    - c\n      - d\n                - e").blocks,
            [
                item("\u{2022}", 0, "a"),
                item("\u{2022}", 1, "b"),
                item("\u{2022}", 2, "c"),
                item("\u{2022}", 3, "d"),
                item("\u{2022}", 3, "e"),
            ]
        );
        assert_eq!(
            parse("2026. A year").blocks,
            [item("2026.", 0, "A year")],
            "reads the same as the text"
        );
        assert_eq!(runs("1234567890. Too long"), [t("1234567890. Too long")]);
        assert_eq!(runs("-not an item"), [t("-not an item")]);
        assert_eq!(
            parse("- **Booked**: 3").blocks,
            [RichBlock::ListItem {
                marker: "\u{2022}".to_owned(),
                depth: 0,
                runs: vec![b("Booked"), t(": 3")],
            }]
        );
        assert_eq!(
            shown("Calls:\n- 12 answered\n1. **First**\n```\ncode\n```"),
            "Calls:\n\u{2022} 12 answered\n1. First\ncode"
        );
    }

    #[test]
    fn emphasis_pairs_up_or_stays_text() {
        assert_eq!(
            runs("**Booked** and *confirmed*."),
            [b("Booked"), t(" and "), i("confirmed"), t(".")]
        );
        assert_eq!(runs("__strong__ _soft_"), [b("strong"), t(" "), i("soft")]);
        assert_eq!(
            runs("**bold *and italic***"),
            [b("bold "), bi("and italic")]
        );
        assert_eq!(
            runs("*one **two** three*"),
            [i("one "), bi("two"), i(" three")]
        );
        assert_eq!(runs("snake_case_name"), [t("snake_case_name")]);
        assert_eq!(runs("2 * 3 * 4"), [t("2 * 3 * 4")]);
        assert_eq!(runs("**unclosed"), [t("**unclosed")]);
        assert_eq!(runs("*unclosed _too"), [t("*unclosed _too")]);
        assert_eq!(runs("*"), [t("*")]);
        assert_eq!(runs("_a_b"), [t("_a_b")], "closed inside a word");
        assert_eq!(
            runs("*`a*b`*"),
            [RichRun {
                italic: true,
                ..c("a*b")
            }]
        );
        assert_eq!(
            runs("*a __b _c **d** c_ b__ a*"),
            [i("a "), bi("b c d c b"), i(" a")]
        );
        // Past the deepest nesting, the rest is text.
        let mut deep = Runs::default();
        render("**a** `b`", true, MAX_DEPTH, Style::default(), &mut deep);
        assert_eq!(deep.0, [t("**a** `b`")]);
        assert_eq!(
            shown("*_*_*_*_*_*_*_*x*_*_*_*_*_*_*_*")
                .chars()
                .filter(|&c| c == 'x')
                .count(),
            1
        );
    }

    #[test]
    fn code_is_shown_as_it_is() {
        assert_eq!(runs("Run `a & b`."), [t("Run "), c("a & b"), t(".")]);
        assert_eq!(runs("``a ` b``"), [c("a ` b")]);
        assert_eq!(runs("`` `"), [t("`` `")]);
        assert_eq!(runs("``"), [t("``")]);
        assert_eq!(
            parse("Before.\n```text\n  *kept* <as>\n```\nAfter.").blocks,
            [
                p(vec![t("Before.")]),
                RichBlock::Code {
                    text: "  *kept* <as>".to_owned()
                },
                p(vec![t("After.")]),
            ]
        );
        assert_eq!(
            parse("~~~\nopen").blocks,
            [RichBlock::Code {
                text: "open".to_owned()
            }],
            "an unclosed fence"
        );
        assert_eq!(runs("\\*not emphasis\\*"), [t("*not emphasis*")]);
        assert_eq!(runs("a \\ b \\n"), [t("a \\ b \\n")]);
    }

    #[test]
    fn only_a_web_page_becomes_a_link() {
        let dashboard = "https://www.distronode.com/dashboard";
        assert_eq!(
            runs("See [the **dashboard**](https://www.distronode.com/dashboard \"Title\")."),
            [
                t("See "),
                l("the ", dashboard),
                RichRun {
                    bold: true,
                    ..l("dashboard", dashboard)
                },
                t(".")
            ]
        );
        assert_eq!(
            runs("[a (wiki)](https://example.com/A_(b)) end"),
            [l("a (wiki)", "https://example.com/A_(b)"), t(" end")]
        );
        assert_eq!(runs("[files](file:///etc/passwd)"), [t("files")]);
        assert_eq!(runs("[x](javascript:alert(1))"), [t("x")]);
        assert_eq!(runs("[x](JavaScript:alert(1))"), [t("x")]);
        assert_eq!(runs("[x](data:text/html,hi)"), [t("x")]);
        assert_eq!(runs("<javascript:alert(1)>"), [t("<javascript:alert(1)>")]);
        assert_eq!(runs("javascript:alert(1)"), [t("javascript:alert(1)")]);
        // What is not a link's markdown is text, with any address in it a link.
        let bare = || l("https://example.com", "https://example.com");
        assert_eq!(
            runs("[unclosed](https://example.com"),
            [t("[unclosed]("), bare()]
        );
        assert_eq!(
            runs("[a\nb](https://example.com)"),
            [t("[a\nb]("), bare(), t(")")]
        );
        assert_eq!(
            runs("[a](https://example.com/\nb)"),
            [
                t("[a]("),
                l("https://example.com/", "https://example.com/"),
                t("\nb)")
            ]
        );
        assert_eq!(
            runs("[a [b]](https://example.com)"),
            [t("[a "), l("b]", "https://example.com")]
        );
        assert_eq!(runs("[no link] here"), [t("[no link] here")]);
        assert_eq!(
            runs("Go to https://example.com/a?b=1&c=2."),
            [
                t("Go to "),
                l(
                    "https://example.com/a?b=1&c=2",
                    "https://example.com/a?b=1&c=2"
                ),
                t(".")
            ]
        );
        assert_eq!(
            runs("(see HTTP://example.com/x)"),
            [
                t("(see "),
                l("HTTP://example.com/x", "HTTP://example.com/x"),
                t(")")
            ]
        );
        assert_eq!(runs("xhttps://example.com"), [t("xhttps://example.com")]);
        assert_eq!(runs("https:// and http"), [t("https:// and http")]);
        assert_eq!(runs("hello"), [t("hello")]);
        assert_eq!(
            runs("<https://example.com> <mailto:a@example.com>"),
            [bare(), t(" <mailto:a@example.com>")]
        );
        assert_eq!(runs("a <b"), [t("a <b")]);
        // No link inside a link's words.
        assert_eq!(
            runs("[https://a.example](https://b.example)"),
            [l("https://a.example", "https://b.example")]
        );
        // Every link in any answer is a web page.
        for text in [
            "[a](https://x.example) [b](ftp://x.example) <https://y.example> http://z.example",
            "[a](http://) [b](https:///x) [c](https://x .example)",
        ] {
            for block in parse(text).blocks {
                if let RichBlock::Paragraph { runs } = block {
                    for run in runs {
                        if let Some(url) = run.link {
                            assert!(is_web_link(&url), "{url}");
                        }
                    }
                }
            }
        }
    }
}
