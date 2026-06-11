using System;
using System.Collections.Generic;
using DuckovController.Config;
using UnityEngine;

namespace DuckovController.UI.Settings
{
    // Live binding shell on the cloned tab content root. Holds Cfg + widget-refresh
    // callbacks; tree is built once by ControllerSettingsPanelBuilder.
    internal sealed class ControllerSettingsPanel : MonoBehaviour
    {
        internal ControllerConfig? Cfg;
        internal string? SettingsPath;
        internal readonly List<Action> RefreshCallbacks = new();

        // Per-section layout records, populated by ControllerSettingsPanelBuilder.
        internal sealed class SectionLayout
        {
            public string Title = "";
            public TMPro.TextMeshProUGUI? HeaderLabel;
            public RectTransform? Header;
            public RectTransform? MasterRow;            // first row — always visible
            public readonly System.Collections.Generic.List<RectTransform> SubRows = new();
            public System.Func<ControllerConfig, bool>? IsEnabled;
        }

        internal readonly System.Collections.Generic.List<SectionLayout> Sections = new();
        internal RectTransform? ListRoot;  // inner-scroll Content, for height resizing

        private bool _subscribed;
        private bool _laidOut;
        private int _lastLayoutHash;

        private static readonly Color HeaderGold = new Color(1f, 0.84f, 0f, 1f);
        private static readonly Color HeaderRed  = new Color(0.88f, 0.23f, 0.23f, 1f);

        // Reflow: disabled sections collapse to header+master (sub-rows hidden, header red).
        // Visible rows re-stacked at standard spacing; scroll Content resized.
        internal void RelayoutSections()
        {
            if (Cfg == null || Sections.Count == 0) return;

            // Hash enabled-state only; skip restack when nothing structural changed
            // (NotifyValueChanged fires on every slider drag).
            int hash = 17;
            foreach (var s in Sections)
                hash = hash * 31 + ((s.IsEnabled == null || s.IsEnabled(Cfg)) ? 1 : 0);
            if (_laidOut && hash == _lastLayoutHash) return;
            _lastLayoutHash = hash;
            _laidOut = true;

            int visibleIndex = 0;
            void Place(RectTransform? rt)
            {
                if (rt == null) return;
                rt.gameObject.SetActive(true);
                var pos = rt.anchoredPosition;
                pos.y = ControllerSettingsPanelBuilder.RowYStart
                        - visibleIndex * ControllerSettingsPanelBuilder.RowYStep;
                rt.anchoredPosition = pos;
                visibleIndex++;
            }

            foreach (var s in Sections)
            {
                bool enabled = s.IsEnabled == null || s.IsEnabled(Cfg);
                if (s.HeaderLabel != null) s.HeaderLabel.color = enabled ? HeaderGold : HeaderRed;

                Place(s.Header);
                Place(s.MasterRow);

                foreach (var row in s.SubRows)
                {
                    if (row == null) continue;
                    if (enabled) Place(row);
                    else row.gameObject.SetActive(false);
                }
            }

            if (ListRoot != null)
                ControllerSettingsPanelBuilder.SetContentHeight(ListRoot, visibleIndex);
        }

        internal void RefreshAll()
        {
            foreach (var cb in RefreshCallbacks)
            {
                try { cb(); }
                catch (Exception e) { Log.Warn($"ControllerSettingsPanel refresh cb failed: {e.Message}"); }
            }
            RelayoutSections();
        }

        // Save Cfg to disk and raise OnRulesChanged. Called by every widget after mutation.
        internal void NotifyValueChanged()
        {
            if (Cfg == null || string.IsNullOrEmpty(SettingsPath)) return;
            try
            {
                ControllerConfigLoader.Save(Cfg, SettingsPath!);
                SettingsBridge.NotifyRulesChanged();
            }
            catch (Exception e) { Log.Error($"ControllerSettingsPanel.NotifyValueChanged: {e}"); }
            RelayoutSections();
        }

        private void OnEnable()
        {
            if (!_subscribed)
            {
                SettingsBridge.OnRulesChanged -= OnExternalChange;
                SettingsBridge.OnRulesChanged += OnExternalChange;
                _subscribed = true;
            }
            // Refresh on show: picks up JSON edits made while the tab was hidden.
            RefreshAll();
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                SettingsBridge.OnRulesChanged -= OnExternalChange;
                _subscribed = false;
            }
        }

        private void OnExternalChange(ControllerConfig newCfg)
        {
            // Hot-reload publishes a fresh instance; adopt it.
            Cfg = newCfg;
            RefreshAll();
        }
    }
}
