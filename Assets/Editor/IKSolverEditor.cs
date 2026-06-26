using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(IKSolver))]
public class IKSolverEditor : UnityEditor.Editor
{
    private SerializedProperty settingsProfile;
    private SerializedProperty applySettingsProfileOnValidate;
    private SerializedProperty root;
    private SerializedProperty endEffector;
    private SerializedProperty orderedJoints;
    private SerializedProperty autoBuildFromRootToEnd;
    private SerializedProperty effectorTarget;
    private SerializedProperty bendPoleTarget;
    private SerializedProperty orientationReference;
    private SerializedProperty restPose;
    private SerializedProperty matchTargetRotation;
    private SerializedProperty keepBendPoleRelativeToOrientation;
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
    private SerializedProperty hingeColor;
    private SerializedProperty ballSocketColor;
    private SerializedProperty lockedColor;
    private SerializedProperty gizmoSize;

    private ReorderableList jointsList;

    private void OnEnable()
    {
        settingsProfile = serializedObject.FindProperty("settingsProfile");
        applySettingsProfileOnValidate = serializedObject.FindProperty("applySettingsProfileOnValidate");
        root = serializedObject.FindProperty("root");
        endEffector = serializedObject.FindProperty("endEffector");
        orderedJoints = serializedObject.FindProperty("orderedJoints");
        autoBuildFromRootToEnd = serializedObject.FindProperty("autoBuildFromRootToEnd");
        effectorTarget = serializedObject.FindProperty("effectorTarget");
        bendPoleTarget = serializedObject.FindProperty("bendPoleTarget");
        orientationReference = serializedObject.FindProperty("orientationReference");
        restPose = serializedObject.FindProperty("restPose");
        matchTargetRotation = serializedObject.FindProperty("matchTargetRotation");
        keepBendPoleRelativeToOrientation = serializedObject.FindProperty("keepBendPoleRelativeToOrientation");
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
        hingeColor = serializedObject.FindProperty("hingeColor");
        ballSocketColor = serializedObject.FindProperty("ballSocketColor");
        lockedColor = serializedObject.FindProperty("lockedColor");
        gizmoSize = serializedObject.FindProperty("gizmoSize");

        jointsList = new ReorderableList(serializedObject, orderedJoints, true, true, true, true);
        jointsList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Ordered Joints");
        jointsList.elementHeight = EditorGUIUtility.singleLineHeight + 4f;
        jointsList.drawElementCallback = DrawJointElement;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSharedSettings();
        DrawChain();
        DrawTargets();
        DrawJointSettings();
        DrawShapeBias();
        DrawContact();
        DrawAdvanced();
        DrawDebug();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSharedSettings()
    {
        EditorGUILayout.LabelField("Shared Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(settingsProfile);
        EditorGUILayout.PropertyField(applySettingsProfileOnValidate);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(settingsProfile.objectReferenceValue == null))
            {
                if (GUILayout.Button("Apply Profile"))
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (Object currentTarget in targets)
                    {
                        Undo.RecordObject(currentTarget, "Apply IK Solver Settings Profile");
                        ((IKSolver)currentTarget).ApplySettingsProfile();
                        EditorUtility.SetDirty(currentTarget);
                    }
                    serializedObject.Update();
                }

                if (GUILayout.Button("Copy To Profile"))
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (Object currentTarget in targets)
                    {
                        IKSolver solver = (IKSolver)currentTarget;
                        if (solver.SettingsProfile == null)
                        {
                            continue;
                        }

                        Undo.RecordObject(solver.SettingsProfile, "Copy IK Solver Settings To Profile");
                        solver.CopySettingsToProfile();
                    }
                    serializedObject.Update();
                }
            }
        }

        EditorGUILayout.Space(6f);
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
        EditorGUILayout.PropertyField(orientationReference);
        EditorGUILayout.PropertyField(restPose);
        EditorGUILayout.PropertyField(matchTargetRotation);
        EditorGUILayout.PropertyField(keepBendPoleRelativeToOrientation);
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
                SerializedProperty constraintType = settings.FindPropertyRelative("constraintType");
                EditorGUILayout.PropertyField(constraintType);

                IKSolver.JointConstraintType selectedType = (IKSolver.JointConstraintType)constraintType.enumValueIndex;
                if (selectedType == IKSolver.JointConstraintType.Hinge)
                {
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative("axis"));

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(settings.FindPropertyRelative("min"));
                        EditorGUILayout.PropertyField(settings.FindPropertyRelative("max"));
                    }

                    EditorGUILayout.PropertyField(settings.FindPropertyRelative("preferredAngle"));
                }
                else if (selectedType == IKSolver.JointConstraintType.BallSocket)
                {
                    DrawLimit(settings.FindPropertyRelative("xLimits"), "X Limits");
                    DrawLimit(settings.FindPropertyRelative("yLimits"), "Y Limits");
                    DrawLimit(settings.FindPropertyRelative("zLimits"), "Z Limits");
                }
                else if (selectedType == IKSolver.JointConstraintType.Free)
                {
                    EditorGUILayout.HelpBox("No rotation limits are applied.", MessageType.None);
                }
                else if (selectedType == IKSolver.JointConstraintType.Locked)
                {
                    EditorGUILayout.HelpBox("Joint rotation is held at the captured rest pose.", MessageType.None);
                }

                EditorGUILayout.PropertyField(settings.FindPropertyRelative("stiffness"));
            }
        }

        EditorGUILayout.Space(6f);
    }

    private static void DrawLimit(SerializedProperty property, string label)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            SerializedProperty min = property.FindPropertyRelative("x");
            SerializedProperty max = property.FindPropertyRelative("y");
            min.floatValue = EditorGUILayout.FloatField(min.floatValue);
            max.floatValue = EditorGUILayout.FloatField(max.floatValue);
        }
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
            EditorGUILayout.PropertyField(hingeColor);
            EditorGUILayout.PropertyField(ballSocketColor);
            EditorGUILayout.PropertyField(lockedColor);
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
