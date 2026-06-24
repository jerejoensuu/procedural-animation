using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(IKSolver))]
public class IKSolverEditor : UnityEditor.Editor
{
    private SerializedProperty root;
    private SerializedProperty endEffector;
    private SerializedProperty orderedJoints;
    private SerializedProperty autoBuildFromRootToEnd;
    private SerializedProperty effectorTarget;
    private SerializedProperty bendPoleTarget;
    private SerializedProperty restPose;
    private SerializedProperty matchTargetRotation;
    private SerializedProperty perJointSettings;
    private SerializedProperty solverWeight;
    private SerializedProperty restPoseWeight;
    private SerializedProperty poleWeight;
    private SerializedProperty useContact;
    private SerializedProperty contactMask;
    private SerializedProperty contactRayLocalDirection;
    private SerializedProperty contactRayDistance;
    private SerializedProperty contactOffset;
    private SerializedProperty contactWeight;
    private SerializedProperty solveTiming;
    private SerializedProperty iterations;
    private SerializedProperty tolerance;
    private SerializedProperty initializeOnStart;
    private SerializedProperty drawGizmos;
    private SerializedProperty chainColor;
    private SerializedProperty targetColor;
    private SerializedProperty poleColor;
    private SerializedProperty gizmoSize;

    private ReorderableList jointsList;

    private void OnEnable()
    {
        root = serializedObject.FindProperty("root");
        endEffector = serializedObject.FindProperty("endEffector");
        orderedJoints = serializedObject.FindProperty("orderedJoints");
        autoBuildFromRootToEnd = serializedObject.FindProperty("autoBuildFromRootToEnd");
        effectorTarget = serializedObject.FindProperty("effectorTarget");
        bendPoleTarget = serializedObject.FindProperty("bendPoleTarget");
        restPose = serializedObject.FindProperty("restPose");
        matchTargetRotation = serializedObject.FindProperty("matchTargetRotation");
        perJointSettings = serializedObject.FindProperty("perJointSettings");
        solverWeight = serializedObject.FindProperty("solverWeight");
        restPoseWeight = serializedObject.FindProperty("restPoseWeight");
        poleWeight = serializedObject.FindProperty("poleWeight");
        useContact = serializedObject.FindProperty("useContact");
        contactMask = serializedObject.FindProperty("contactMask");
        contactRayLocalDirection = serializedObject.FindProperty("contactRayLocalDirection");
        contactRayDistance = serializedObject.FindProperty("contactRayDistance");
        contactOffset = serializedObject.FindProperty("contactOffset");
        contactWeight = serializedObject.FindProperty("contactWeight");
        solveTiming = serializedObject.FindProperty("solveTiming");
        iterations = serializedObject.FindProperty("iterations");
        tolerance = serializedObject.FindProperty("tolerance");
        initializeOnStart = serializedObject.FindProperty("initializeOnStart");
        drawGizmos = serializedObject.FindProperty("drawGizmos");
        chainColor = serializedObject.FindProperty("chainColor");
        targetColor = serializedObject.FindProperty("targetColor");
        poleColor = serializedObject.FindProperty("poleColor");
        gizmoSize = serializedObject.FindProperty("gizmoSize");

        jointsList = new ReorderableList(serializedObject, orderedJoints, true, true, true, true);
        jointsList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Ordered Joints");
        jointsList.elementHeight = EditorGUIUtility.singleLineHeight + 4f;
        jointsList.drawElementCallback = DrawJointElement;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawChain();
        DrawTargets();
        DrawJointSettings();
        DrawShapeBias();
        DrawContact();
        DrawAdvanced();
        DrawDebug();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawChain()
    {
        EditorGUILayout.LabelField("IK Limb", EditorStyles.boldLabel);
        EditorGUILayout.Space(3f);

        EditorGUILayout.LabelField("Chain", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(root);
        EditorGUILayout.PropertyField(endEffector);
        EditorGUILayout.PropertyField(autoBuildFromRootToEnd);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Auto Build From Root To End"))
            {
                serializedObject.ApplyModifiedProperties();
                foreach (Object currentTarget in targets)
                {
                    Undo.RecordObject(currentTarget, "Auto Build IK Chain");
                    ((IKSolver)currentTarget).AutoBuildChain();
                    EditorUtility.SetDirty(currentTarget);
                }
                serializedObject.Update();
            }

            if (GUILayout.Button("Capture Rest Pose"))
            {
                serializedObject.ApplyModifiedProperties();
                foreach (Object currentTarget in targets)
                {
                    Undo.RecordObject(currentTarget, "Capture IK Rest Pose");
                    ((IKSolver)currentTarget).CaptureCurrentRestPose();
                    EditorUtility.SetDirty(currentTarget);
                }
                serializedObject.Update();
            }
        }

        jointsList.DoLayoutList();
        EditorGUILayout.Space(6f);
    }

    private void DrawTargets()
    {
        EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(effectorTarget);
        EditorGUILayout.PropertyField(bendPoleTarget, new GUIContent("Bend/Pole Target"));
        EditorGUILayout.PropertyField(restPose);
        EditorGUILayout.PropertyField(matchTargetRotation);
        EditorGUILayout.Space(6f);
    }

    private void DrawJointSettings()
    {
        EditorGUILayout.LabelField("Per-Joint Settings", EditorStyles.boldLabel);

        int count = Mathf.Min(orderedJoints.arraySize, perJointSettings.arraySize);
        if (count == 0)
        {
            EditorGUILayout.HelpBox("Add joints manually or use Auto Build From Root To End.", MessageType.Info);
            EditorGUILayout.Space(6f);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            SerializedProperty joint = orderedJoints.GetArrayElementAtIndex(i);
            SerializedProperty settings = perJointSettings.GetArrayElementAtIndex(i);
            string label = joint.objectReferenceValue != null ? joint.objectReferenceValue.name : "Joint " + i;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("constraintType"));
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("axis"));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative("min"));
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative("max"));
                }

                EditorGUILayout.PropertyField(settings.FindPropertyRelative("preferredAngle"));
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("stiffness"));
            }
        }

        EditorGUILayout.Space(6f);
    }

    private void DrawShapeBias()
    {
        EditorGUILayout.LabelField("Shape Bias", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(solverWeight);
        EditorGUILayout.PropertyField(restPoseWeight);
        EditorGUILayout.PropertyField(poleWeight);
        EditorGUILayout.Space(6f);
    }

    private void DrawContact()
    {
        EditorGUILayout.LabelField("Foot/Contact Behavior Optional", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(useContact);
        if (useContact.boolValue)
        {
            EditorGUILayout.PropertyField(contactMask);
            EditorGUILayout.PropertyField(contactRayLocalDirection);
            EditorGUILayout.PropertyField(contactRayDistance);
            EditorGUILayout.PropertyField(contactOffset);
            EditorGUILayout.PropertyField(contactWeight);
        }

        EditorGUILayout.Space(6f);
    }

    private void DrawAdvanced()
    {
        EditorGUILayout.LabelField("Advanced Solver", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(solveTiming);
        EditorGUILayout.PropertyField(iterations);
        EditorGUILayout.PropertyField(tolerance);
        EditorGUILayout.PropertyField(initializeOnStart);
        EditorGUILayout.Space(6f);
    }

    private void DrawDebug()
    {
        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(drawGizmos);
        if (drawGizmos.boolValue)
        {
            EditorGUILayout.PropertyField(chainColor);
            EditorGUILayout.PropertyField(targetColor);
            EditorGUILayout.PropertyField(poleColor);
            EditorGUILayout.PropertyField(gizmoSize);
        }
    }

    private void DrawJointElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty element = orderedJoints.GetArrayElementAtIndex(index);
        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(rect, element, new GUIContent("Joint " + index));
    }
}
