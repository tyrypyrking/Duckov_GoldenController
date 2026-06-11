using UnityEngine;

namespace DuckovController.UI
{
    internal static class TransformHelpers
    {
        // Depth-first descendant (or self) search by name. Null-safe.
        internal static Transform? FindDescendantByName(Transform? root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDescendantByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
