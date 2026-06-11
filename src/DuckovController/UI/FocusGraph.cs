using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckovController.UI
{
    internal enum NavDir { Up, Down, Left, Right }

    // 2D adjacency graph of focusable UI elements for deterministic D-pad nav.
    internal sealed class FocusGraph
    {
        // Node = GameObject (the IPointerEnterHandler's owner).
        private readonly List<GameObject> _nodes = new();
        private readonly List<Vector2> _centers = new();
        private readonly Dictionary<GameObject, int> _index = new();

        internal int Count => _nodes.Count;
        internal IReadOnlyList<GameObject> Nodes => _nodes;

        // Type-name → Type cache for whitelist GetComponent calls. Null = not found (negative cache).
        // GetComponent(Type) is faster than GetComponent(string).
        private static readonly Dictionary<string, System.Type?> _typeCache =
            new Dictionary<string, System.Type?>();

        private static System.Type? ResolveType(string name)
        {
            if (_typeCache.TryGetValue(name, out var cached)) return cached;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; } // partial/dynamic assembly — skip
                foreach (var ty in types)
                {
                    if (ty.Name == name) { _typeCache[name] = ty; return ty; }
                }
            }
            _typeCache[name] = null; // negative cache
            return null;
        }

        private static Component? TryGetComponentByName(GameObject go, string name)
        {
            var t = ResolveType(name);
            if (t != null) return go.GetComponent(t);
            return go.GetComponent(name); // fallback when type not loaded
        }

        // Cross-pane penalty: candidate distance × (1 + CrossPanePenalty) when in a different pane. Null = no pane info.
        internal System.Func<UnityEngine.GameObject, UnityEngine.GameObject, bool>? IsDifferentPane;
        internal float CrossPanePenalty;

        // Drop nodes after whitelist (e.g. chrome buttons with dedicated bindings). Null = full whitelist.
        internal System.Func<GameObject, bool>? ExcludeNode;

        internal void Build(GameObject root)
        {
            _nodes.Clear();
            _centers.Clear();
            _index.Clear();
            if (root == null) return;

            var components = root.GetComponentsInChildren<MonoBehaviour>(includeInactive: false);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                // No IPointerEnterHandler filter: StockShopItemEntry and similar don't implement it. Whitelist is sole arbiter.
                var go = c.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                if (_index.ContainsKey(go)) continue;
                var rt = c.transform as RectTransform;
                if (rt == null) continue;
                var size = rt.rect.size;
                if (size.x <= 1f || size.y <= 1f) continue;

                // Skip InventoryEntry nodes whose index is beyond the bag's
                // current capacity — these are pool-reused ghost slots that
                // have valid UI but no real backing item slot.
                var ie = go.GetComponent<Duckov.UI.InventoryEntry>();
                if (ie != null)
                {
                    var inv = ie.Master?.Target;
                    if (inv == null) continue;
                    if (ie.Index >= inv.Capacity) continue;
                }

                // Explicit exclusion: Scrollbar should never be a focus target.
                if (go.GetComponent<UnityEngine.UI.Scrollbar>() != null) continue;

                // Component whitelist: only accept nodes that are real focus targets.
                if (!IsWhitelistedNode(go)) continue;
                if (ExcludeNode != null && ExcludeNode(go)) continue;

                var screenCenter = PointerEventDispatcher.ScreenCenterOf(go);
                _index[go] = _nodes.Count;
                _nodes.Add(go);
                _centers.Add(screenCenter);
            }

            if (Log.Verbose)
            {
                int merchantCount = 0;
                foreach (var n in _nodes)
                    if (n != null && TryGetComponentByName(n, "StockShopItemEntry") != null) merchantCount++;
                Log.Debug_($"FocusGraph.Build: {_nodes.Count} nodes ({merchantCount} StockShopItemEntry).");
            }
        }

        // Shared by Build and AppendFrom so the list can't drift (AppendFrom's copy had silently gone stale).
        private static bool IsWhitelistedNode(GameObject go)
            => go.GetComponent<Duckov.UI.InventoryEntry>() != null
               || TryGetComponentByName(go, "SlotDisplay") != null
               || TryGetComponentByName(go, "InventoryFilterDisplayEntry") != null
               || TryGetComponentByName(go, "StockShopItemEntry") != null
               || TryGetComponentByName(go, "ItemShortcutButton") != null
               || TryGetComponentByName(go, "ItemShortcutEditorEntry") != null
               || TryGetComponentByName(go, "PerkEntry") != null
               || TryGetComponentByName(go, "CraftView_ListEntry") != null
               || TryGetComponentByName(go, "CraftViewFilterBtnEntry") != null
               || TryGetComponentByName(go, "QuestEntry") != null
               || TryGetComponentByName(go, "MasterKeysIndexEntry") != null
               || TryGetComponentByName(go, "NoteIndexView_Entry") != null
               || TryGetComponentByName(go, "MapSelectionEntry") != null
               || TryGetComponentByName(go, "EndowmentSelectionEntry") != null
               // DemandPanel_Entry / SupplyPanel_Entry: only their inner Button is a focus target.
               || go.GetComponent<UnityEngine.UI.Button>() != null
               // ItemDecomposeView count slider (…/CountSlider/Slider) — navigable nav-stop.
               // Scoped to that view so other views' sliders (e.g. MiniMapView.zoomSlider) never become stray nodes.
               || (go.GetComponent<UnityEngine.UI.Slider>() != null
                   && Duckov.UI.View.ActiveView != null
                   && Duckov.UI.View.ActiveView.GetType().Name == "ItemDecomposeView")
               || go.GetComponent<UnityEngine.UI.Toggle>() != null
               || go.GetComponent<TMPro.TMP_Dropdown>() != null;

        // Append nodes without clearing (e.g. ItemOperationMenu buttons onto an existing inventory graph).
        internal void AppendFrom(GameObject root)
        {
            if (root == null) return;
            var components = root.GetComponentsInChildren<MonoBehaviour>(includeInactive: false);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                if (_index.ContainsKey(go)) continue;
                var rt = c.transform as RectTransform;
                if (rt == null) continue;
                var size = rt.rect.size;
                if (size.x <= 1f || size.y <= 1f) continue;
                if (go.GetComponent<UnityEngine.UI.Scrollbar>() != null) continue;
                if (!IsWhitelistedNode(go)) continue;
                if (ExcludeNode != null && ExcludeNode(go)) continue;
                var screenCenter = PointerEventDispatcher.ScreenCenterOf(go);
                _index[go] = _nodes.Count;
                _nodes.Add(go);
                _centers.Add(screenCenter);
            }
        }

        // Best neighbor in direction: angular cost + distance heuristic.
        internal GameObject? Neighbor(GameObject? from, NavDir dir)
        {
            if (from == null || !_index.TryGetValue(from, out int fromIdx)) return Closest(default, dir, fromGo: null);
            var fromPos = _centers[fromIdx];
            return Closest(fromPos, dir, ignoreIdx: fromIdx, fromGo: from);
        }

        private GameObject? Closest(Vector2 from, NavDir dir, int ignoreIdx = -1, GameObject? fromGo = null)
        {
            if (_nodes.Count == 0) return null;
            Vector2 axis = dir switch
            {
                NavDir.Up => Vector2.up,
                NavDir.Down => Vector2.down,
                NavDir.Left => Vector2.left,
                NavDir.Right => Vector2.right,
                _ => Vector2.zero,
            };
            float bestScore = float.PositiveInfinity;
            int bestIdx = -1;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (i == ignoreIdx) continue;
                var to = _centers[i] - from;
                var dot = Vector2.Dot(to.normalized, axis);
                if (dot < 0.1f) continue; // not in cone
                var perp = to - axis * Vector2.Dot(to, axis);
                // Cost = parallel distance + heavy penalty for perpendicular drift
                var score = Mathf.Abs(Vector2.Dot(to, axis)) + 2.0f * perp.magnitude;
                var candidate = _nodes[i];
                if (fromGo != null && IsDifferentPane != null && IsDifferentPane(fromGo, candidate)
                    && CrossPanePenalty > 0f)
                {
                    score *= (1f + CrossPanePenalty);
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx >= 0 ? _nodes[bestIdx] : null;
        }

        // Topmost-leftmost node by screen position.
        internal GameObject? InitialFocus()
        {
            if (_nodes.Count == 0) return null;
            int best = 0;
            for (int i = 1; i < _nodes.Count; i++)
            {
                // higher Y first (Unity screen space: up = +y), then lower X
                if (_centers[i].y > _centers[best].y + 1f ||
                    (Mathf.Abs(_centers[i].y - _centers[best].y) <= 1f && _centers[i].x < _centers[best].x))
                {
                    best = i;
                }
            }
            return _nodes[best];
        }

        // Bottommost-leftmost node by screen position (mirror of InitialFocus; used for nav wrap-around).
        internal GameObject? LastFocus()
        {
            if (_nodes.Count == 0) return null;
            int best = 0;
            for (int i = 1; i < _nodes.Count; i++)
            {
                // lower Y first (Unity screen space: down = -y), then lower X
                if (_centers[i].y < _centers[best].y - 1f ||
                    (Mathf.Abs(_centers[i].y - _centers[best].y) <= 1f && _centers[i].x < _centers[best].x))
                {
                    best = i;
                }
            }
            return _nodes[best];
        }

        internal bool Contains(GameObject? go) => go != null && _index.ContainsKey(go);

        // Graph node nearest `screenPos` by cached screen-centre. Used by MiniMapView toolbox drift-correct.
        internal GameObject? NearestTo(Vector2 screenPos)
        {
            if (_nodes.Count == 0) return null;
            int   best  = 0;
            float bestD = (_centers[0] - screenPos).sqrMagnitude;
            for (int i = 1; i < _nodes.Count; i++)
            {
                float d = (_centers[i] - screenPos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return _nodes[best];
        }
    }
}
