using System.Collections.Generic;
using System.Reflection;
using Duckov.UI.Animations;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // QuestCompletePanel is the reward-claim modal shown by QuestGiverView after a quest is turned
    // in (questGiverView.questCompletePanel.Show). It floats over the quest board but is NOT a View,
    // so it never trips the verb-map machinery. While it's up we lock the board out entirely and own
    // input here (mirroring the Split overlay): A claims the top unclaimed reward (cycling until all
    // are claimed), X claims them all (takeAllButton), B skips (skipButton). The reward list stays
    // un-navigable — only these three verbs are live, the first reward carries the golden outline.
    //
    // Reward claiming is model-driven: the per-entry RewardEntry claim buttons are inert in this
    // panel (RewardEntry.Interactable is never set, so its claimButton is hidden), so A calls
    // Reward.Claim() on the model, not a focused button. Claiming is async (RewardItem spins up a
    // UniTask and flips a transient Claiming flag), so we skip rewards mid-claim to avoid double-fire.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        private static FieldInfo? _qcpFadeField;
        private static FieldInfo? _qcpSkipField;
        private static FieldInfo? _qcpTakeAllField;
        private static PropertyInfo? _qcpTargetProp;
        private static PropertyInfo? _questRewardsProp;
        private static PropertyInfo? _rewardClaimedProp;
        private static PropertyInfo? _rewardClaimableProp;
        private static PropertyInfo? _rewardClaimingProp;
        private static MethodInfo? _rewardClaimMethod;
        private static FieldInfo? _rewardEntryTargetField;
        private static bool _qcpReflected;

        // The A/turn-in press that opened the panel must be released once before A can claim, so the
        // same press can't claim a reward the instant the modal fades in (mirrors the Split debounce).
        private bool _questRewardWasOpen;
        private bool _questRewardAArmed;
        private GameObject? _questRewardXGlyph; // X glyph attached to the takeAll (claim-all) button

        // Helper-panel prompts shown while the modal is up. B is a glyph hint only — there is no
        // focusable Skip target; it fires from the global B press handled here.
        private static readonly PromptEntry[] _questRewardPrompts =
        {
            new PromptEntry(ButtonGlyph.A, "Claim"),
            new PromptEntry(ButtonGlyph.X, "Claim All"),
            new PromptEntry(ButtonGlyph.B, "Skip"),
        };

        private static void ResolveQuestRewardFields()
        {
            if (_qcpReflected) return;
            _qcpReflected = true;
            var panelT = AccessTools.TypeByName("Duckov.Quests.UI.QuestCompletePanel");
            if (panelT != null)
            {
                _qcpFadeField    = AccessTools.Field(panelT, "mainFadeGroup");
                _qcpSkipField    = AccessTools.Field(panelT, "skipButton");
                _qcpTakeAllField = AccessTools.Field(panelT, "takeAllButton");
                _qcpTargetProp   = AccessTools.Property(panelT, "Target");
            }
            var questT = AccessTools.TypeByName("Duckov.Quests.Quest");
            if (questT != null) _questRewardsProp = AccessTools.Property(questT, "Rewards");
            var rewardT = AccessTools.TypeByName("Duckov.Quests.Reward");
            if (rewardT != null)
            {
                _rewardClaimedProp   = AccessTools.Property(rewardT, "Claimed");
                _rewardClaimableProp = AccessTools.Property(rewardT, "Claimable");
                _rewardClaimingProp  = AccessTools.Property(rewardT, "Claiming");
                _rewardClaimMethod   = AccessTools.Method(rewardT, "Claim");
            }
            var entryT = AccessTools.TypeByName("Duckov.Quests.UI.RewardEntry");
            if (entryT != null) _rewardEntryTargetField = AccessTools.Field(entryT, "target");
        }

        // The QuestCompletePanel hosted by the active QuestGiverView, only while it's faded in.
        // Cheap: a singleton-ish field read gated upstream to frames where the board is the active View.
        private MonoBehaviour? GetActiveQuestRewardPanel()
        {
            var view = Duckov.UI.View.ActiveView;
            if (view == null || view.GetType().Name != "QuestGiverView") return null;
            ResolveQuestRewardFields();
            var f = AccessTools.Field(view.GetType(), "questCompletePanel");
            if (f?.GetValue(view) is MonoBehaviour panel && panel.gameObject.activeInHierarchy)
            {
                var fg = _qcpFadeField?.GetValue(panel) as FadeGroup;
                if (fg != null && fg.IsShown) return panel;
            }
            return null;
        }

        internal bool IsQuestRewardOpen() => GetActiveQuestRewardPanel() != null;

        // Owns input while the reward modal is shown. Caller returns immediately after, so grid nav,
        // the exit glyph, and the verb router are all suppressed — the board is fully locked out.
        private void HandleQuestReward(MonoBehaviour panel)
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            ResolveQuestRewardFields();

            // Keep the cursor hidden and drop any EventSystem selection so a stray Submit can't fire
            // on a quest entry sitting behind the modal.
            Cursor.visible = false;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

            if (!_questRewardWasOpen)
            {
                _questRewardWasOpen = true;
                _questRewardAArmed = !pad.buttonSouth.isPressed; // armed only if A isn't the press that opened us
                ViewHintPanel.Override = _questRewardPrompts;
                // X glyph on the claim-all button (mirrors on-button hints elsewhere).
                if (_questRewardXGlyph == null && _qcpTakeAllField?.GetValue(panel) is Button tb)
                {
                    var img = CreateButtonGlyph(tb, ButtonGlyph.X);
                    if (img != null) _questRewardXGlyph = img.gameObject;
                }
                Log.Debug_($"QuestReward: modal shown (aHeldAtOpen={pad.buttonSouth.isPressed})");
            }
            if (!pad.buttonSouth.isPressed) _questRewardAArmed = true; // A released → claim armed

            // Default focus: the golden outline sits on the first unclaimed reward entry (visual only —
            // claiming is model-driven below). Re-pinned each frame so it advances as rewards are claimed.
            ApplyFocusOutline(FirstUnclaimedEntryGo(panel));

            // B = Skip the whole modal.
            if (pad.buttonEast.wasPressedThisFrame)
            {
                Log.Debug_("QuestReward: B → skip");
                (_qcpSkipField?.GetValue(panel) as Button)?.onClick.Invoke();
                return;
            }
            // X = Claim All.
            if (pad.buttonWest.wasPressedThisFrame)
            {
                Log.Debug_("QuestReward: X → claim all");
                (_qcpTakeAllField?.GetValue(panel) as Button)?.onClick.Invoke();
                return;
            }
            // A = claim the top unclaimed reward (cycle). Debounced against the press that opened us.
            if (_questRewardAArmed && pad.buttonSouth.wasPressedThisFrame)
            {
                var reward = FirstUnclaimedReward(panel, skipClaiming: true);
                if (reward != null)
                {
                    Log.Debug_("QuestReward: A → claim one");
                    _rewardClaimMethod?.Invoke(reward, null);
                }
            }
        }

        // Called when the modal closes (detected in Update) to drop the overlay's glyph + prompt override.
        private void EndQuestReward()
        {
            _questRewardWasOpen = false;
            _questRewardAArmed = false;
            ViewHintPanel.Override = null;
            if (_questRewardXGlyph != null) { Destroy(_questRewardXGlyph); _questRewardXGlyph = null; }
            if (_outlineOverlay != null) _outlineOverlay.Hide();
        }

        // First model reward that is unclaimed + claimable (optionally skipping ones mid-claim).
        private object? FirstUnclaimedReward(MonoBehaviour panel, bool skipClaiming)
        {
            var quest = _qcpTargetProp?.GetValue(panel);
            if (quest == null) return null;
            if (_questRewardsProp?.GetValue(quest) is not IEnumerable<object> rewards) return null;
            foreach (var r in rewards)
            {
                if (r == null) continue;
                bool claimed   = _rewardClaimedProp?.GetValue(r) as bool? ?? true;
                bool claimable = _rewardClaimableProp?.GetValue(r) as bool? ?? false;
                bool claiming  = skipClaiming && (_rewardClaimingProp?.GetValue(r) as bool? ?? false);
                if (!claimed && claimable && !claiming) return r;
            }
            return null;
        }

        // The RewardEntry GameObject whose target Reward is the first unclaimed one (for the outline).
        private GameObject? FirstUnclaimedEntryGo(MonoBehaviour panel)
        {
            var target = FirstUnclaimedReward(panel, skipClaiming: false);
            if (target == null) return null;
            var entries = panel.GetComponentsInChildren<Component>(includeInactive: false);
            foreach (var c in entries)
            {
                if (c == null || c.GetType().Name != "RewardEntry") continue;
                if (!c.gameObject.activeInHierarchy) continue;
                if (ReferenceEquals(_rewardEntryTargetField?.GetValue(c), target)) return c.gameObject;
            }
            return null;
        }
    }
}
