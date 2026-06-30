using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Settings
{
    // Builds the Controller Mod settings tab by cloning live OptionsUIEntry_Dropdown
    // rows so entries inherit game theming (font, shadow, layout, TMP_Dropdown nav).
    // Game binding components (OptionsUIEntry_*, OptionsProvider*) are stripped to
    // prevent writes to vanilla PlayerPrefs. Each row gets a ControllerSettingsRowMarker
    // for X-button reset (OptionsPanelTabCycler).
    internal static partial class ControllerSettingsPanelBuilder
    {
        // Components to destroy on cloned rows — they bind into vanilla settings
        // (PlayerPrefs, FMOD buses, localization). Everything else stays for styling.
        private static readonly string[] _stripComponentPrefixes =
        {
            "OptionsUIEntry_",       // OptionsUIEntry_Dropdown / _Toggle / _Slider
            "OptionsProvider",       // OptionsProviderBase + subclasses
            "Duckov.Options.UI",     // any other namespace member
            "BusVolume",             // FMOD bus volume bindings
            "UI_Bus_Slider",            // FMOD bus slider on UI_BusVolume_* rows: re-sets value+slider pos in OnEnable
            "SodaCraft.Localizations.", // TextLocalizor/ImageLocalizor/etc: re-stamp localized key in OnEnable
        };

        // Set by BuildInto; read by section Build() calls via row-primitive methods.
        internal static GameObject? ActiveDropdownTemplate;

        // Section registry — add new sections here.
        private static readonly ISettingsSection[] _sections =
        {
            new Sections.SmartTakeSection(),
            new Sections.SmartHealSection(),
            new Sections.AimAssistSection(),
            new Sections.CostDisplaySection(),
            new Sections.HapticsSection(),
        };

        internal static ControllerSettingsPanel BuildInto(
            RectTransform root, Config.ControllerConfig cfg, string settingsPath,
            GameObject? dropdownRowTemplate,
            GameObject? sliderRowTemplate = null)
        {
            ActiveDropdownTemplate = dropdownRowTemplate;
            var panel = root.gameObject.AddComponent<ControllerSettingsPanel>();
            panel.Cfg = cfg;
            panel.SettingsPath = settingsPath;

            // Fill the host container the way native tabs do: mirror the live Common-tab
            // content RectTransform (stretch anchors) instead of baking main-menu numbers.
            // The pause-menu OptionsPanel container is larger; stretch anchors adapt to both
            // hosts automatically (they're container-relative), and copying the live sibling
            // sidesteps the CanvasScaler/Deck-units pitfall that baking numbers would hit.
            StretchRootToHost(root);

            // LayoutElement: with stretch anchors the parent rect drives size, so a fixed
            // min/preferred lock would fight it. Keep an (unlocked) LayoutElement only to
            // survive a layout-group-controlled ancestor collapsing height to 0; flexible
            // weights let the parent rect win.
            var rootLe = root.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            rootLe.minWidth = -1f;
            rootLe.minHeight = -1f;
            rootLe.preferredWidth = -1f;
            rootLe.preferredHeight = -1f;
            rootLe.flexibleWidth = 1f;
            rootLe.flexibleHeight = 1f;

            // Drop layout components from root; rows use absolute positioning inside inner ScrollRect.
            var oldVlg = root.GetComponent<VerticalLayoutGroup>();
            if (oldVlg != null) UnityEngine.Object.DestroyImmediate(oldVlg);
            var oldCsf = root.GetComponent<ContentSizeFitter>();
            if (oldCsf != null) UnityEngine.Object.DestroyImmediate(oldCsf);

            Log.Info($"BuildInto: root after stretch — "
                + $"anchors=({root.anchorMin.x:F2},{root.anchorMin.y:F2})-({root.anchorMax.x:F2},{root.anchorMax.y:F2}) "
                + $"pivot=({root.pivot.x:F2},{root.pivot.y:F2}) "
                + $"size=({root.rect.width:F0},{root.rect.height:F0}) "
                + $"parent={root.parent?.name ?? "<null>"} active={root.gameObject.activeInHierarchy}");

            // Inner ScrollRect stretches to fill root; rows scroll independently of the game's outer ScrollView.
            var listRoot = CreateInnerScroll(root);

            // Reset row-index counter for this content root.
            _rowIndexByParent[listRoot.GetInstanceID()] = 0;

            Log.Info($"BuildInto: listRoot (inner scroll Content) — "
                + $"size=({listRoot.sizeDelta.x:F0},{listRoot.sizeDelta.y:F0}) "
                + $"anchors=({listRoot.anchorMin.x:F2},{listRoot.anchorMin.y:F2})-({listRoot.anchorMax.x:F2},{listRoot.anchorMax.y:F2}) "
                + $"pivot=({listRoot.pivot.x:F2},{listRoot.pivot.y:F2})");

            if (dropdownRowTemplate == null)
            {
                Log.Warn("ControllerSettingsPanelBuilder: no dropdown row template — falling back to plain label.");
                AddPlainText(listRoot, "Could not locate vanilla settings row template.");
                return panel;
            }

            panel.ListRoot = listRoot;
            foreach (var section in _sections)
            {
                var label = AddSectionHeader(listRoot, dropdownRowTemplate, section.Title, out var headerRt);

                var record = new ControllerSettingsPanel.SectionLayout
                {
                    Title = section.Title,
                    HeaderLabel = label,
                    Header = headerRt,
                    IsEnabled = section.IsEnabled,
                };

                var collected = new System.Collections.Generic.List<RectTransform>();
                _collectRowsInto = collected;
                section.Build(panel, listRoot, sliderRowTemplate);
                _collectRowsInto = null;

                if (collected.Count == 0)
                    Log.Warn($"ControllerSettings: section '{section.Title}' added no rows — no master control, cannot collapse/expand.");

                if (collected.Count > 0)
                {
                    record.MasterRow = collected[0];
                    for (int i = 1; i < collected.Count; i++) record.SubRows.Add(collected[i]);
                }
                panel.Sections.Add(record);
            }

            // Size scroll Content to fit all rows. Rebuild first so the freshly-created
            // stretch scroll/viewport resolve their rects (viewport height drives the
            // Content floor); without this the viewport reads 0 and the floor falls back.
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            int rowCount = _rowIndexByParent.TryGetValue(listRoot.GetInstanceID(), out int n) ? n : 0;
            SetContentHeight(listRoot, rowCount);

            panel.RelayoutSections();

            Log.Info($"BuildInto DONE: rowCount={rowCount} "
                + $"listRoot.size=({listRoot.sizeDelta.x:F0},{listRoot.sizeDelta.y:F0}) "
                + $"listRoot.childCount={listRoot.childCount} "
                + $"root.children={root.childCount}");

            // Log first few children for sanity.
            for (int i = 0; i < Mathf.Min(listRoot.childCount, 3); i++)
            {
                var c = listRoot.GetChild(i) as RectTransform;
                if (c == null) continue;
                var img = c.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                Log.Info($"  row[{i}] {c.name}: pos=({c.anchoredPosition.x:F0},{c.anchoredPosition.y:F0}) "
                    + $"size=({c.sizeDelta.x:F0},{c.sizeDelta.y:F0}) "
                    + $"active={c.gameObject.activeInHierarchy} children={c.childCount} "
                    + $"firstLabel='{(img != null ? img.text : "<null>")}' ");
            }

            return panel;
        }
    }
}
