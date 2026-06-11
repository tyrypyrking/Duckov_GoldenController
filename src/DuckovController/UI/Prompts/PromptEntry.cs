using DuckovController.UI.Common;

namespace DuckovController.UI.Prompts
{
    internal readonly struct PromptEntry
    {
        public readonly ButtonGlyph Glyph;
        // Optional paired glyph for a single "same cycle" row (e.g. LT+RT for
        // the pane cycle). Null = a normal single-glyph row.
        public readonly ButtonGlyph? Glyph2;
        public readonly string Label;

        public PromptEntry(ButtonGlyph glyph, string label)
        {
            Glyph = glyph;
            Glyph2 = null;
            Label = label;
        }

        public PromptEntry(ButtonGlyph glyph, ButtonGlyph glyph2, string label)
        {
            Glyph = glyph;
            Glyph2 = glyph2;
            Label = label;
        }
    }
}
