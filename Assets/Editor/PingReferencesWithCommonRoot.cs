using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PingReferencesWithCommonRoot
{
    private const string MENU_PATH = "GameObject/Ping references with common root";

    [MenuItem(MENU_PATH, false, 0)]
    private static void Execute()
    {
        var selectedGameobjects = Selection.gameObjects;
        if (selectedGameobjects == null || selectedGameobjects.Length == 0) return;

        // Require same top-most parent
        var sharedRoot = selectedGameobjects[0].transform.root;
        for (int i = 1; i < selectedGameobjects.Length; i++)
        {
            if (selectedGameobjects[i].transform.root != sharedRoot)
            {
                EditorUtility.DisplayDialog(
                    "Ping references",
                    "Selected objects do not share the same root parent. Select objects under the same root and try again.",
                    "OK");
                return;
            }
        }

        // Targets: selected GOs + Transforms + all Components on them
        var targets = new HashSet<UnityEngine.Object>();
        foreach (var go in selectedGameobjects)
        {
            if (!go) continue;
            targets.Add(go);
            var goTransform = go.transform;
            if (goTransform) targets.Add(goTransform);

            var comps = ListPool<Component>.Get();
            go.GetComponents(comps);
            foreach (var component in comps)
            {
                if (component) targets.Add(component);
            }

            ListPool<Component>.Release(comps);
        }

        // Exclude only the selected objects themselves (parents/children allowed if they truly reference)
        var excludeSelf = new HashSet<GameObject>(selectedGameobjects);

        // Scan subtree under the shared root
        var hitsByGo = new Dictionary<GameObject, List<RefHit>>(128);
        ScanRecursive(sharedRoot.gameObject, targets, excludeSelf, hitsByGo);

        if (hitsByGo.Count == 0)
        {
            EditorUtility.DisplayDialog("Ping references", "No referencers found under the common root.", "OK");
            return;
        }

        if (hitsByGo.Count == 1)
        {
            // Ping the single result; no window
            foreach (var (key, list) in hitsByGo)
            {
                if (key) EditorGUIUtility.PingObject(key);

                var sb = new StringBuilder(256);
                sb.AppendLine("[Ping references] 1 object referencing the selection:");
                sb.AppendLine("- " + BuildHierarchyPath(key ? key.transform : null));
                for (int i = 0; i < list.Count; i++)
                {
                    var (ct, pp) = (list[i].Component ? list[i].Component.GetType().Name : "<MissingComponent>",
                        list[i].PropertyPath);
                    sb.AppendLine($"    • {ct}.{pp}");
                }

                Debug.Log(sb.ToString());
                break;
            }

            return;
        }

        // >1: open the window
        var window = PingReferencesWithCommonRootWindow.ShowWindow(sharedRoot.name);
        window.SetResults(hitsByGo);
    }

    [MenuItem(MENU_PATH, true)]
    private static bool Validate() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

    private static void ScanRecursive(
        GameObject go,
        HashSet<UnityEngine.Object> targets,
        HashSet<GameObject> excludeSelf,
        Dictionary<GameObject, List<RefHit>> hitsByGo)
    {
        if (!go) return;

        // We still traverse excluded nodes; we just don't count them as referees
        bool countThisGo = !excludeSelf.Contains(go);

        var comps = ListPool<Component>.Get();
        go.GetComponents(comps);

        foreach (var component in comps)
        {
            if (!component) continue; // Missing script slot
            if (component is Transform) continue; // Ignore Transform/RectTransform structural links

            var so = new SerializedObject(component);
            var it = so.GetIterator();

            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;

                var obj = it.objectReferenceValue;
                if (obj == null || !targets.Contains(obj)) continue;

                if (countThisGo)
                {
                    if (!hitsByGo.TryGetValue(go, out var list))
                    {
                        list = new List<RefHit>(2);
                        hitsByGo.Add(go, list);
                    }

                    list.Add(new RefHit(component, it.propertyPath));
                }
            }
        }

        ListPool<Component>.Release(comps);

        // Recurse children
        var transform = go.transform;
        for (int count = 0, cc = transform.childCount; count < cc; count++)
        {
            ScanRecursive(transform.GetChild(count).gameObject, targets, excludeSelf, hitsByGo);
        }
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (!transform) return "<null>";
        var stack = ListPool<Transform>.Get();
        while (transform)
        {
            stack.Add(transform);
            transform = transform.parent;
        }

        var sb = new StringBuilder(64);
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            sb.Append(stack[i].name);
            if (i > 0) sb.Append('/');
        }

        ListPool<Transform>.Release(stack);
        return sb.ToString();
    }

    // Lightweight GC-friendly list pool
    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new();
        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>(16);

        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }

    // One recorded serialized reference hit (component + serialized property path)
    private struct RefHit
    {
        public readonly Component Component;
        public readonly string PropertyPath;

        public RefHit(Component component, string path)
        {
            Component = component;
            PropertyPath = path;
        }
    }

    // IMGUI window that shows results with Ping/Select (opens only if >1 result)
    private class PingReferencesWithCommonRootWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _filter = "";
        private string _rootName = "<root>";
        private readonly List<Row> _rows = new(256);

        private struct Row
        {
            public GameObject Go;
            public string Path;
            public List<(string compType, string propPath)> Details;
        }

        public static PingReferencesWithCommonRootWindow ShowWindow(string rootName)
        {
            var w = GetWindow<PingReferencesWithCommonRootWindow>("Ping References");
            w._rootName = rootName;
            w.minSize = new Vector2(520, 300);
            return w;
        }

        public void SetResults(Dictionary<GameObject, List<RefHit>> hitsByGo)
        {
            _rows.Clear();
            foreach (var (go, value) in hitsByGo)
            {
                var details = new List<(string, string)>(value.Count);
                foreach (var hit in value)
                {
                    var typeName = hit.Component ? hit.Component.GetType().Name : "<MissingComponent>";
                    details.Add((typeName, hit.PropertyPath));
                }

                _rows.Add(new Row
                {
                    Go = go,
                    Path = BuildHierarchyPath(go ? go.transform : null),
                    Details = details
                });
            }

            _rows.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));

            // Auto-ping first visible for convenience
            if (_rows.Count > 0 && _rows[0].Go)
                EditorGUIUtility.PingObject(_rows[0].Go);

            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"Root: {_rootName}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                GUILayout.Label("Filter", GUILayout.Width(36));
                var newFilter = GUILayout.TextField(_filter, EditorStyles.toolbarTextField, GUILayout.MinWidth(120));
                if (newFilter != _filter)
                {
                    _filter = newFilter;
                    Repaint();
                }

                if (GUILayout.Button("Ping all", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    PingAll();

                if (GUILayout.Button("Select all", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    SelectAllVisible();
            }

            if (_rows.Count == 0)
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox("No referencers found under the common root.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var row in _rows)
            {
                if (!row.Go) continue;
                if (!PassesFilter(row)) continue;

                EditorGUILayout.BeginVertical("box");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(row.Path, EditorStyles.boldLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                        EditorGUIUtility.PingObject(row.Go);
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                        Selection.activeObject = row.Go;
                }

                if (row.Details is { Count: > 0 })
                {
                    var sb = new StringBuilder(128);
                    for (int i = 0; i < row.Details.Count; i++)
                    {
                        var (ct, pp) = row.Details[i];
                        sb.Append("• ").Append(ct).Append('.').Append(pp);
                        if (i < row.Details.Count - 1) sb.Append('\n');
                    }

                    EditorGUILayout.HelpBox(sb.ToString(), MessageType.None);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private bool PassesFilter(in Row row)
        {
            if (string.IsNullOrEmpty(_filter)) return true;
            var filterString = _filter.ToLowerInvariant();
            if (row.Path.ToLowerInvariant().Contains(filterString)) return true;
            if (row.Go && row.Go.name.ToLowerInvariant().Contains(filterString)) return true;

            if (row.Details != null)
            {
                foreach (var (compType, propPath) in row.Details)
                {
                    if (compType.ToLowerInvariant().Contains(filterString) ||
                        propPath.ToLowerInvariant().Contains(filterString))
                        return true;
                }
            }

            return false;
        }

        private void PingAll()
        {
            foreach (var row in _rows)
            {
                if (!row.Go) continue;
                if (!PassesFilter(row)) continue;
                EditorGUIUtility.PingObject(row.Go);
            }
        }

        private void SelectAllVisible()
        {
            var list = new List<UnityEngine.Object>(64);
            foreach (var row in _rows)
            {
                if (!row.Go) continue;
                if (!PassesFilter(row)) continue;
                list.Add(row.Go);
            }

            if (list.Count > 0)
                Selection.objects = list.ToArray();
        }
    }
}