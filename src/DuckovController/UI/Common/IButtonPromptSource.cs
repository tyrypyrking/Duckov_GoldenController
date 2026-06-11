using System;
using System.Collections.Generic;
using DuckovController.UI.Prompts;

namespace DuckovController.UI.Common
{
    internal interface IButtonPromptSource
    {
        event Action OnPromptsChanged;
        IReadOnlyList<PromptEntry> CurrentPrompts { get; }

        // True when the active view's prompts should render as a horizontal strip
        // rather than a vertical stack (mirrors the current verb map's flag).
        bool PromptsHorizontal { get; }
    }
}
