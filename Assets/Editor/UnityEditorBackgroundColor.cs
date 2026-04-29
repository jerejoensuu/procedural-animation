using UnityEditor;
using UnityEngine;

namespace Editor
{
    public static class UnityEditorBackgroundColor
    {
        private static readonly Color KDefaultColor = new(0.7843f, 0.7843f, 0.7843f);
        private static readonly Color KDefaultProColor = new(0.2196f, 0.2196f, 0.2196f);

        private static readonly Color KSelectedColor = new(0.22745f, 0.447f, 0.6902f);
        private static readonly Color KSelectedProColor = new(0.1725f, 0.3647f, 0.5294f);

        private static readonly Color KSelectedUnfocusedColor = new(0.68f, 0.68f, 0.68f);
        private static readonly Color KSelectedUnfocusedProColor = new(0.3f, 0.3f, 0.3f);

        private static readonly Color KHoveredColor = new(0.698f, 0.698f, 0.698f);
        private static readonly Color KHoveredProColor = new(0.2706f, 0.2706f, 0.2706f);

        public static Color GetColor(bool isSelected, bool isHovered, bool isWindowFocused)
        {
            if (isSelected)
            {
                if (isWindowFocused)
                {
                    return EditorGUIUtility.isProSkin ? KSelectedProColor : KSelectedColor;
                }

                return EditorGUIUtility.isProSkin ? KSelectedUnfocusedProColor : KSelectedUnfocusedColor;
            }

            if (isHovered)
            {
                return EditorGUIUtility.isProSkin ? KHoveredProColor : KHoveredColor;
            }

            return EditorGUIUtility.isProSkin ? KDefaultProColor : KDefaultColor;
        }
    }
}