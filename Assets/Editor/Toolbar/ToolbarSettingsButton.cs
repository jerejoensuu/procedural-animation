using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Editor.Toolbar
{
    public class ToolbarSettingsButton
    {
        [MainToolbarElement("Project/Open Project Settings", defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement ProjectSettingsButton()
        {
            var icon = EditorGUIUtility.IconContent("SettingsIcon").image as Texture2D;
            var content = new MainToolbarContent(icon);
            return new MainToolbarButton(content, () => SettingsService.OpenProjectSettings());
        }
    }
}