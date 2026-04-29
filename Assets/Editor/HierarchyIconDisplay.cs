using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    [InitializeOnLoad]
    public static class HierarchyIconDisplay
    {
        private static bool _hierarchyHasFocus;
        private static EditorWindow _hierarchyEditorWindow;

        static HierarchyIconDisplay()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGui;
            // EditorApplication.update += OnEditorUpdate;
        }

        // private static void OnEditorUpdate()
        // {
        //     bool hasHierarchyOpen = Resources
        //         .FindObjectsOfTypeAll<EditorWindow>()
        //         .Any(w => w.GetType().Name == "SceneHierarchyWindow");
        //
        //     _hierarchyHasFocus = hasHierarchyOpen
        //                          && EditorWindow.focusedWindow?.GetType().Name == "SceneHierarchyWindow";
        // }

        private static void OnHierarchyWindowItemOnGui(int instanceID, Rect selectionRect)
        {
            GameObject obj = EditorUtility.EntityIdToObject(instanceID) as GameObject;
            if (obj == null) return;

            if (PrefabUtility.IsAnyPrefabInstanceRoot(obj)) return;

            Component[] components = obj.GetComponents<Component>();
            if (components == null || components.Length == 0) return;

            Component component = components.Length > 1 ? components[1] : components[0];
            if (component == null) return;
            
            if (component is CanvasRenderer) component = components.Length > 2 ? components[2] : components[0];
            
            Type type = component.GetType();

            GUIContent content = EditorGUIUtility.ObjectContent(component, type);
            content.text = null;
            content.tooltip = type.Name; // Optional

            if (content.image == null) return;

            bool isSelected = Selection.entityIds.Contains(instanceID);
            bool isHovering = selectionRect.Contains(Event.current.mousePosition);

            Color color = UnityEditorBackgroundColor.GetColor(isSelected, isHovering, _hierarchyHasFocus);
            Rect backgroundRect = selectionRect;
            backgroundRect.width = 18.5f;
            EditorGUI.DrawRect(backgroundRect, color);

            EditorGUI.LabelField(selectionRect, content);

            if (Event.current.type == EventType.Repaint && PrefabUtility.IsAddedGameObjectOverride(obj))
            {
                var overlay = EditorGUIUtility.IconContent("PrefabOverlayAdded Icon").image;
                var oRect = new Rect(
                    selectionRect.x + 2,
                    selectionRect.y + (selectionRect.height - 16f) * .5f,
                    16f, 16f
                );
                GUI.DrawTexture(oRect, overlay, ScaleMode.ScaleToFit);
            }
        }
    }
}