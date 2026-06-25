using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IKSolverSettings))]
public class IKSolverSettingsEditor : UnityEditor.Editor
{
    private IKSolver sourceSolver;

    public override void OnInspectorGUI()
    {
        DrawCopyFromSolver();
        EditorGUILayout.Space(8f);
        DrawDefaultInspector();
    }

    private void DrawCopyFromSolver()
    {
        EditorGUILayout.LabelField("Copy From Existing Leg", EditorStyles.boldLabel);
        sourceSolver = (IKSolver)EditorGUILayout.ObjectField("Source Solver", sourceSolver, typeof(IKSolver), true);

        using (new EditorGUI.DisabledScope(sourceSolver == null))
        {
            if (GUILayout.Button("Copy Solver Options To This Asset"))
            {
                IKSolverSettings settings = (IKSolverSettings)target;
                Undo.RecordObject(settings, "Copy IK Solver Settings From Solver");
                settings.CopyFromSolver(sourceSolver);
                EditorUtility.SetDirty(settings);
                serializedObject.Update();
            }
        }

        EditorGUILayout.HelpBox(
            "Copies tuning values and the per-joint settings list. Chain references, targets, poles, and rest pose references stay on the leg solver.",
            MessageType.Info);
    }
}
