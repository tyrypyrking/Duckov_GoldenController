using System;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI
{
    // View & chrome classification: supported views, no-focus views, cursor-driven
    // views, and per-view chrome-button predicates excluded from D-pad nav.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        // Quest chrome: tab strip (RB/LB), sort (Y), exit (B) — all have dedicated bindings.
        private static readonly System.Collections.Generic.HashSet<string> _questChromeNames =
            new System.Collections.Generic.HashSet<string>
            {
                "Btn_Avaliable", "Btn_Active", "Btn_History", "Btn_Sort", "ExitButton",
            };

        private static bool IsQuestChromeButton(GameObject go)
            => go != null && _questChromeNames.Contains(go.name);

        // Covers "ExitButton" and "ExitButton_1" (MiniMapView).
        private static bool IsViewExitButton(GameObject go)
            => go != null && go.name.StartsWith("ExitButton");

        // EndowmentSelectionPanel: Confirm=X, exit=B.
        private static bool IsEndowmentChrome(GameObject go)
            => go != null && (go.name.StartsWith("ExitButton") || go.name == "Confirm");

        // ItemRepairView: A selects/repairs, X repairs all, B exits (RepairViewVerbMap).
        private static bool IsRepairChrome(GameObject go)
            => go != null && (go.name.StartsWith("ExitButton")
                || go.name == "RepairButton"
                || go.name == "RepairAllButton");

        // ItemDecomposeView: A selects, X decomposes (decompose GO named "Button"), B exits (ItemDecomposeViewVerbMap).
        private static bool IsDecomposeChrome(GameObject go)
            => go != null && (go.name.StartsWith("ExitButton")
                || go.name == "Button");

        // StorageDock: NextPage/PrevPage=LB/RB, exit=B.
        private static bool IsStorageDockChrome(GameObject go)
            => go != null && (go.name.StartsWith("ExitButton")
                || go.name == "NextPage"
                || go.name == "PrevPage");

        // CraftView: section tabs=RB/LB, craft=X (BtnContainer), exit=B.
        private static bool IsCraftChrome(GameObject go)
            => go != null && (go.name.StartsWith("ExitButton")
                || go.name.StartsWith("FilterEntry")
                || go.name == "BtnContainer");

        // ATMView: exits named "Btn_Exit" (not "ExitButton") — B handles back, active one hosts glyph.
        private static bool IsAtmChrome(GameObject go)
            => go != null && go.name == "Btn_Exit";

        private bool IsSupportedView(View v)
        {
            if (v == null) return false;
            if (v is LootView) return true;
            // Type-by-name to avoid hard assembly dependencies (Economy.UI, Crafting.UI, etc.)
            var n = v.GetType().Name;
            if (n == "StockShopView") return true;
            if (n == "FormulasRegisterView") return true;
            if (n == "MasterKeysRegisterView") return true;
            if (n == "BitcoinMinerView") return true;
            if (n == "BlackMarketView") return true;
            if (n == "CraftView") return true;
            // ItemRepairView: A selects/repairs, X repairs all, B exits, LT/RT jump panes (RepairViewVerbMap).
            if (n == "ItemRepairView") return true;
            // ItemDecomposeView: A selects, X decomposes, dpad count slider, B exits, LT/RT jump panes (ItemDecomposeViewVerbMap).
            if (n == "ItemDecomposeView") return true;
            // ShowcaseView: trader-family transfer (DefaultViewVerbMap).
            if (n == "ShowcaseView") return true;
            if (n == "QuestView") return true;
            if (n == "QuestGiverView") return true;
            if (n == "PerkTreeView") return true;
            if (n == "MasterKeysView") return true;
            if (n == "NoteIndexView") return true;
            // SleepView: dpad drives time slider, A=Sleep, B=Exit (SleepViewVerbMap).
            if (n == "SleepView") return true;
            // PlayerStatsView: read-only; supported only so B-exit routes through the router (see IsNoFocusView).
            if (n == "PlayerStatsView") return true;
            // MiniMapView: D-pad navigates marker toolbox; LS pans, RS=cursor, LT/RT zoom, X marks, B exits (MiniMapViewVerbMap). StickAsDpad gated off.
            if (n == "MiniMapView") return true;
            // BuilderView: D-pad navigates catalog, A places; LS pans natively, RS=build cursor, LT/RT zoom (BuilderViewVerbMap). StickAsDpad gated off.
            if (n == "BuilderView") return true;
            // MapSelectionView: teleporter destination grid; A confirms, B exits (MapSelectionViewVerbMap).
            if (n == "MapSelectionView") return true;
            // EndowmentSelectionPanel: talent card row; A selects, X confirms, B exits (EndowmentSelectionPanelVerbMap).
            if (n == "EndowmentSelectionPanel") return true;
            // GamingConsoleView: trader-family transfer (DefaultViewVerbMap).
            if (n == "GamingConsoleView") return true;
            // ATMView: Select pane then digit-keypad; ATMViewVerbMap handles pane-aware B, X=Backspace, Y=Max.
            if (n == "ATMView") return true;
            // StorageDock: paged claim-card list; A claims, LB/RB pages, B exits (StorageDockViewVerbMap).
            if (n == "StorageDock") return true;
            return false;
        }

        // Claimed but non-navigable: no chevron, dpad suppressed (see Update); only B-exit routes through the router.
        private static bool IsNoFocusView(View v)
        {
            if (v == null) return false;
            var n = v.GetType().Name;
            return n == "PlayerStatsView";
        }

        // BuilderView drives Mouse.current itself (RS build cursor) — suppress the usual off-slot cursor park.
        private static bool IsCursorDrivenView(View? v)
            => v != null && v.GetType().Name == "BuilderView";
    }
}
