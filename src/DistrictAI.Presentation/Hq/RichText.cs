using DistrictAI.Core.Ffi;

namespace DistrictAI.ViewModels.Hq;

/// <summary>What kind of paragraph a <see cref="RichParagraph"/> is drawn as.</summary>
public enum RichParagraphKind
{
    /// <summary>Body text.</summary>
    Body,

    /// <summary>A heading: a strong line.</summary>
    Heading,

    /// <summary>A list item, its marker first, indented by <see cref="RichParagraph.Indent"/>.</summary>
    ListItem,

    /// <summary>A code block, in a fixed-width font.</summary>
    Code,
}

/// <summary>
/// One piece of a paragraph: text in one style, or a line break. A link's text
/// carries the address, which goes back to the core when it is clicked.
/// </summary>
/// <param name="Text">The text, exactly as it is shown; empty for a line break.</param>
/// <param name="Bold">Strong emphasis.</param>
/// <param name="Italic">Emphasis.</param>
/// <param name="Code">Inline code, in a fixed-width font.</param>
/// <param name="Link">The web page it links to, or null.</param>
/// <param name="IsLineBreak">A line break inside the paragraph.</param>
public sealed record RichInline(string Text, bool Bold, bool Italic, bool Code, string? Link, bool IsLineBreak)
{
    /// <summary>A line break.</summary>
    public static readonly RichInline LineBreak = new(string.Empty, false, false, false, null, true);
}

/// <summary>One paragraph of an answer, ready to become a XAML <c>Paragraph</c> one inline at a time.</summary>
/// <param name="Kind">How it is drawn.</param>
/// <param name="Indent">A list item's depth, 0 to 3; 0 for everything else.</param>
/// <param name="SpaceBefore">Whether a gap goes before it: every paragraph but the first, except a list item straight after another.</param>
/// <param name="Inlines">Its pieces, in order.</param>
public sealed record RichParagraph(RichParagraphKind Kind, int Indent, bool SpaceBefore, IReadOnlyList<RichInline> Inlines);

/// <summary>
/// The core's rich text as paragraphs of inlines. The core has already parsed
/// the Markdown and decided every link; this only splits lines and places the
/// list markers, so the page builds its <c>RichTextBlock</c> without reading
/// any markup.
/// </summary>
public static class RichText
{
    /// <summary>The paragraphs of <paramref name="text"/>.</summary>
    public static IReadOnlyList<RichParagraph> Layout(RichTextView text)
    {
        var paragraphs = new List<RichParagraph>(text.Blocks.Length);
        RichBlock? before = null;
        foreach (var block in text.Blocks)
        {
            var spaceBefore = before is not null && !(before is RichBlock.ListItem && block is RichBlock.ListItem);
            paragraphs.Add(block switch
            {
                RichBlock.Paragraph paragraph => new RichParagraph(RichParagraphKind.Body, 0, spaceBefore, Inlines(paragraph.Runs)),
                RichBlock.Heading heading => new RichParagraph(RichParagraphKind.Heading, 0, spaceBefore, Inlines(heading.Runs)),
                RichBlock.ListItem item => new RichParagraph(
                    RichParagraphKind.ListItem,
                    (int)Math.Min(item.Depth, 3u),
                    spaceBefore,
                    [new RichInline(item.Marker + " ", false, false, false, null, false), .. Inlines(item.Runs)]),
                RichBlock.Code code => new RichParagraph(
                    RichParagraphKind.Code,
                    0,
                    spaceBefore,
                    Lines(code.Text, line => new RichInline(line, false, false, true, null, false))),
                // A block a later core adds: its text is not known here, so nothing is drawn for it.
                _ => new RichParagraph(RichParagraphKind.Body, 0, spaceBefore, []),
            });
            before = block;
        }
        return paragraphs;
    }

    /// <summary>The text of <paramref name="paragraphs"/> without its styles, one paragraph to a line: what a screen reader is given as its name.</summary>
    public static string Plain(IReadOnlyList<RichParagraph> paragraphs) =>
        string.Join(
            "\n",
            paragraphs.Select(paragraph => string.Concat(paragraph.Inlines.Select(inline => inline.IsLineBreak ? "\n" : inline.Text))));

    private static List<RichInline> Inlines(RichRun[] runs)
    {
        var inlines = new List<RichInline>();
        foreach (var run in runs)
        {
            inlines.AddRange(Lines(run.Text, line => new RichInline(line, run.Bold, run.Italic, run.Code, run.Link, false)));
        }
        return inlines;
    }

    /// <summary><paramref name="text"/> split at each line break, each line made by <paramref name="make"/>, with a break between them.</summary>
    private static List<RichInline> Lines(string text, Func<string, RichInline> make)
    {
        var inlines = new List<RichInline>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                inlines.Add(RichInline.LineBreak);
            }
            if (lines[i].Length > 0)
            {
                inlines.Add(make(lines[i]));
            }
        }
        return inlines;
    }
}
