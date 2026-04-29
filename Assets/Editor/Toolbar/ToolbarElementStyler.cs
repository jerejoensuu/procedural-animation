using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using UnityUtils;

namespace Editor.Toolbar
{
    public static class ToolbarElementStyler
    {
        public static void StyleElement<T>(string elementName, Action<T> styleAction) where T : VisualElement
        {
            EditorApplication.delayCall += () =>
            {
                ApplyStyle(elementName, (element) =>
                {
                    T targetElement;

                    if (element is T typedElement)
                    {
                        targetElement = typedElement;
                    }
                    else
                    {
                        targetElement = element.Query<T>().First();
                    }

                    if (targetElement != null)
                    {
                        styleAction(targetElement);
                    }
                });
            };
        }

        static void ApplyStyle(string elementName, Action<VisualElement> styleAction)
        {
            var element = FindElementByName(elementName);
            if (element != null)
            {
                styleAction(element);
            }
        }

        private static VisualElement FindElementByName(string name)
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                var root = window.rootVisualElement;
                if (root == null) continue;

                VisualElement element;
                if ((element = root.FindElementByName(name)) != null) return element;
                if ((element = root.FindElementByTooltip(name)) != null) return element;
            }

            return null;
        }
    }
}