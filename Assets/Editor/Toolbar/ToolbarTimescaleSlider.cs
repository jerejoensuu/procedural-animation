using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Toolbar
{
    public class ToolbarTimescaleSlider
    {
        private const float MIN_TIME_SCALE = 0f;
        private const float MAX_TIME_SCALE = 5f;

        [MainToolbarElement("Timescale/Slider", defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement TimeScaleSlider()
        {
            var content = new MainToolbarContent("Time Scale");
            var slider = new MainToolbarSlider(content, Time.timeScale, MIN_TIME_SCALE, MAX_TIME_SCALE,
                OnSliderValueChanged)
            {
                populateContextMenu = menu =>
                {
                    menu.AppendAction("Reset", _ =>
                    {
                        Time.timeScale = 1f;
                        MainToolbar.Refresh("Timescale/Slider");
                    });
                }
            };

            ToolbarElementStyler.StyleElement<VisualElement>("Timescale/Slider", element =>
            {
                element.style.paddingLeft = 20;
            });

            return slider;
        }

        private static void OnSliderValueChanged(float newValue)
        {
            Time.timeScale = newValue;
        }

        [MainToolbarElement("Timescale/Reset", defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement TimeScaleResetButton()
        {
            var icon = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
            var content = new MainToolbarContent(icon);
            var button = new MainToolbarButton(content, () =>
            {
                Time.timeScale = 1f;
                MainToolbar.Refresh("Timescale/Slider");
            });

            ToolbarElementStyler.StyleElement<EditorToolbarButton>("Timescale/Reset", element =>
            {
                element.style.paddingLeft = 0;
                element.style.paddingRight = 0;
                element.style.marginLeft = 0;
                element.style.marginRight = 0;
                element.style.minWidth = 20f;
                element.style.maxWidth = 20f;

                var image = element.Q<Image>("icon");
                if (image != null)
                {
                    image.style.width = 12f;
                    image.style.height = 12f;
                }
            });

            return button;
        }
    }
}