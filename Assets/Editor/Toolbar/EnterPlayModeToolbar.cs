using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Toolbar
{
    /// <summary>
    /// Main toolbar dropdown for switching Enter Play Mode presets.
    /// Supports both click-to-open and mouse-wheel cycling while hovered.
    /// </summary>
    public static class EnterPlayModeToolbar
    {
        private const string ToolbarPath = "PlayMode/Preset";

        private static readonly string[] Labels =
        {
            "Default",
            "No Domain",
            "No Scene",
            "Fast"
        };

        [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement CreateElement()
        {
            var currentPreset = EnterPlayModeMenu.GetCurrentPreset();
            var currentLabel = Labels[PresetToIndex(currentPreset)];

            var content = new MainToolbarContent(
                text: $"Play Mode: {currentLabel}",
                image: null,
                tooltip: "Choose the Enter Play Mode preset.");

            var dropdown = new MainToolbarDropdown(content, ShowPresetMenu);

            ToolbarElementStyler.StyleElement<VisualElement>(ToolbarPath, RegisterScrollCycling);
            
            ToolbarElementStyler.StyleElement<VisualElement>(ToolbarPath, element =>
            {
                element.style.paddingRight = 20;
            });

            return dropdown;
        }

        private static void ShowPresetMenu(Rect rect)
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            var menu = new GenericMenu();
            var currentPreset = EnterPlayModeMenu.GetCurrentPreset();
            var currentIndex = PresetToIndex(currentPreset);

            for (int i = 0; i < Labels.Length; i++)
            {
                int index = i;
                bool isCurrent = index == currentIndex;

                menu.AddItem(
                    new GUIContent(Labels[index]),
                    isCurrent,
                    () => ApplyIndex(index));
            }

            menu.DropDown(rect);
        }

        private static void RegisterScrollCycling(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            // Prevent duplicate registration when the toolbar is refreshed/rebuilt.
            element.UnregisterCallback<WheelEvent>(OnWheelScrolledPreset);
            element.RegisterCallback<WheelEvent>(OnWheelScrolledPreset);
        }

        private static void OnWheelScrolledPreset(WheelEvent evt)
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            float deltaY = evt.delta.y;
            if (Mathf.Approximately(deltaY, 0f))
            {
                return;
            }

            var currentPreset = EnterPlayModeMenu.GetCurrentPreset();
            int currentIndex = PresetToIndex(currentPreset);

            int direction = deltaY > 0f ? 1 : -1;
            int targetIndex = Mathf.Clamp(currentIndex + direction, 0, Labels.Length - 1);

            if (targetIndex == currentIndex)
            {
                return;
            }

            ApplyIndex(targetIndex);
            evt.StopPropagation();
        }

        private static int PresetToIndex(EnterPlayModeMenu.Preset preset)
        {
            switch (preset)
            {
                case EnterPlayModeMenu.Preset.DefaultReloads:
                    return 0;

                case EnterPlayModeMenu.Preset.DisableDomainReload:
                    return 1;

                case EnterPlayModeMenu.Preset.DisableSceneReload:
                    return 2;

                case EnterPlayModeMenu.Preset.FastNoReloads:
                    return 3;

                default:
                    return 0;
            }
        }

        private static void ApplyIndex(int index)
        {
            switch (index)
            {
                case 0:
                    EnterPlayModeMenu.ApplyPreset(EnterPlayModeMenu.Preset.DefaultReloads);
                    break;

                case 1:
                    EnterPlayModeMenu.ApplyPreset(EnterPlayModeMenu.Preset.DisableDomainReload);
                    break;

                case 2:
                    EnterPlayModeMenu.ApplyPreset(EnterPlayModeMenu.Preset.DisableSceneReload);
                    break;

                case 3:
                    EnterPlayModeMenu.ApplyPreset(EnterPlayModeMenu.Preset.FastNoReloads);
                    break;

                default:
                    return;
            }

            MainToolbar.Refresh(ToolbarPath);
        }
    }
}