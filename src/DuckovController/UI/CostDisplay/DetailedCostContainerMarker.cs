using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.CostDisplay
{
    // Per-itemsContainer cache. The vanilla container arranges its item rows with a layout
    // group (a GridLayoutGroup in the shipped game). A GameObject can't hold two LayoutGroup
    // components (DisallowMultipleComponent → AddComponent<VLG> returns null while one exists),
    // so the styler destroys the original group to add a VerticalLayoutGroup. This snapshots
    // the GridLayoutGroup's settings + the container size so the toggle-off path can rebuild an
    // identical group. (JsonUtility would be tidier but UnityEngine.JSONSerializeModule isn't
    // referenced, so we copy the fields explicitly.)
    internal sealed class DetailedCostContainerMarker : MonoBehaviour
    {
        public bool Captured;
        public bool AddedCsf;          // true if WE added the ContentSizeFitter (remove on restore)
        public Vector2 OrigSizeDelta;  // container size before our CSF grew it

        public bool WasGrid;           // original group was a GridLayoutGroup (the shipped case)
        public RectOffset GridPadding = new RectOffset();
        public Vector2 GridCellSize, GridSpacing;
        public GridLayoutGroup.Corner GridStartCorner;
        public GridLayoutGroup.Axis GridStartAxis;
        public TextAnchor GridChildAlignment;
        public GridLayoutGroup.Constraint GridConstraint;
        public int GridConstraintCount;
    }
}
