using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class IKSolver : MonoBehaviour
{
    public enum SolveTiming
    {
        Update,
        LateUpdate,
        Manual
    }

    public enum JointConstraintType
    {
        Free,
        Hinge,
        BallSocket,
        Locked
    }

    public enum ConstraintAxis
    {
        X,
        Y,
        Z,
        NegativeX,
        NegativeY,
        NegativeZ
    }

    [System.Serializable]
    public class JointSettings
    {
        public JointConstraintType constraintType = JointConstraintType.Free;
        public ConstraintAxis axis = ConstraintAxis.Z;
        public float min = -90f;
        public float max = 90f;
        public Vector2 xLimits = new Vector2(-90f, 90f);
        public Vector2 yLimits = new Vector2(-90f, 90f);
        public Vector2 zLimits = new Vector2(-90f, 90f);
        public float preferredAngle;
        [Range(0f, 1f)] public float stiffness;
    }

    [Header("Shared Settings")] [SerializeField]
    private IKSolverSettings settingsProfile;

    [SerializeField] private bool applySettingsProfileOnValidate = true;

    [Header("Chain")] [SerializeField] private Transform root;
    [SerializeField] private Transform endEffector;
    [SerializeField] private List<Transform> orderedJoints = new List<Transform>();
    [SerializeField] private bool autoBuildFromRootToEnd = true;

    [Header("Targets")] [SerializeField] private Transform effectorTarget;
    [SerializeField] private Transform bendPoleTarget;
    [SerializeField] private Transform orientationReference;
    [SerializeField] private Transform restPose;
    [SerializeField] private bool matchTargetRotation = true;
    [SerializeField] private bool keepBendPoleRelativeToOrientation = true;

    [Header("Per-Joint Settings")] [SerializeField]
    private List<JointSettings> perJointSettings = new List<JointSettings>();

    [Header("Shape Bias")] [Range(0f, 1f)] [SerializeField]
    private float solverWeight = 1f;

    [Range(0f, 1f)] [SerializeField] private float restPoseWeight = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float poleWeight = 1f;

    [Header("Foot/Contact Behavior")] [SerializeField]
    private bool useContact;

    [SerializeField] private LayerMask contactMask = ~0;
    [SerializeField] private Vector3 contactRayLocalDirection = Vector3.down;
    [SerializeField] private float contactRayDistance = 1f;
    [SerializeField] private float contactOffset = 0.02f;
    [Range(0f, 1f)] [SerializeField] private float contactWeight = 1f;

    [Header("Advanced Solver")] [SerializeField]
    private SolveTiming solveTiming = SolveTiming.LateUpdate;

    [Min(1)] [SerializeField] private int iterations = 10;
    [Min(0.00001f)] [SerializeField] private float tolerance = 0.001f;
    [SerializeField] private bool initializeOnStart = true;

    [Header("Debug")] [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color chainColor = Color.green;
    [SerializeField] private Color targetColor = Color.cyan;
    [SerializeField] private Color poleColor = Color.yellow;
    [SerializeField] private Color hingeColor = Color.magenta;
    [SerializeField] private Color ballSocketColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color lockedColor = Color.red;
    [SerializeField] private float gizmoSize = 0.04f;

    private readonly List<Transform> _restPoseJoints = new();
    private Vector3[] _positions;
    private Vector3[] _restDirections;
    private Vector3[] _restLocalDirections;
    private float[] _lengths;
    private Quaternion[] _restLocalRotations;
    private float[] _hingeAngles;
    private Quaternion _restOrientationReferenceRotation;
    private Vector3 _restBendPoleReferencePosition;
    private float _totalLength;
    private bool _initialized;

    public Transform Root
    {
        get => root;
        set
        {
            root = value;
            _initialized = false;
        }
    }

    public Transform EndEffector
    {
        get => endEffector;
        set
        {
            endEffector = value;
            _initialized = false;
        }
    }

    public Transform EffectorTarget
    {
        get => effectorTarget;
        set => effectorTarget = value;
    }

    public IKSolverSettings SettingsProfile
    {
        get => settingsProfile;
        set => settingsProfile = value;
    }

    public Transform OrientationReference
    {
        get => orientationReference;
        set
        {
            orientationReference = value;
            _initialized = false;
        }
    }

    public IReadOnlyList<Transform> OrderedJoints => orderedJoints;

    private void Reset()
    {
        endEffector = transform;
        root = transform.parent != null ? transform.parent : transform;
        AutoBuildChain();
    }

    private void Awake()
    {
        if (initializeOnStart)
        {
            Initialize();
        }
    }

    private void OnValidate()
    {
        if (settingsProfile != null && applySettingsProfileOnValidate)
        {
            ApplySettingsProfile();
        }

        iterations = Mathf.Max(1, iterations);
        tolerance = Mathf.Max(0.00001f, tolerance);
        contactRayDistance = Mathf.Max(0f, contactRayDistance);
        gizmoSize = Mathf.Max(0f, gizmoSize);
        EnsureSettingsCount();
        if (!Application.isPlaying)
        {
            _initialized = false;
        }
    }

    private void Update()
    {
        if (solveTiming == SolveTiming.Update)
        {
            Solve();
        }
    }

    private void LateUpdate()
    {
        if (solveTiming == SolveTiming.LateUpdate)
        {
            Solve();
        }
    }

    [ContextMenu("Auto Build Chain")]
    public void AutoBuildChain()
    {
        orderedJoints.Clear();

        if (root == null || endEffector == null)
        {
            _initialized = false;
            return;
        }

        Transform current = endEffector;
        while (current != null)
        {
            orderedJoints.Add(current);
            if (current == root)
            {
                break;
            }

            current = current.parent;
        }

        if (orderedJoints.Count == 0 || orderedJoints[^1] != root)
        {
            orderedJoints.Clear();
            _initialized = false;
            return;
        }

        orderedJoints.Reverse();
        EnsureSettingsCount();
        _initialized = false;
    }

    [ContextMenu("Capture Current Rest Pose")]
    public void CaptureCurrentRestPose()
    {
        EnsureSettingsCount();
        _restLocalRotations = new Quaternion[orderedJoints.Count];
        for (int i = 0; i < orderedJoints.Count; i++)
        {
            _restLocalRotations[i] = orderedJoints[i] != null ? orderedJoints[i].localRotation : Quaternion.identity;
        }

        _initialized = false;
    }

    [ContextMenu("Apply Settings Profile")]
    public void ApplySettingsProfile()
    {
        ApplySettings(settingsProfile);
    }

    public void ApplySettings(IKSolverSettings profile)
    {
        if (profile == null)
        {
            return;
        }

        settingsProfile = profile;

        perJointSettings.Clear();
        var profileJointSettings = profile.PerJointSettings;
        foreach (var jointSettings in profileJointSettings)
        {
            perJointSettings.Add(IKSolverSettings.CloneJointSettings(jointSettings));
        }

        solverWeight = profile.SolverWeight;
        restPoseWeight = profile.RestPoseWeight;
        poleWeight = profile.PoleWeight;
        useContact = profile.UseContact;
        contactMask = profile.ContactMask;
        contactRayLocalDirection = profile.ContactRayLocalDirection;
        contactRayDistance = profile.ContactRayDistance;
        contactOffset = profile.ContactOffset;
        contactWeight = profile.ContactWeight;
        solveTiming = profile.SolveTiming;
        iterations = profile.Iterations;
        tolerance = profile.Tolerance;
        initializeOnStart = profile.InitializeOnStart;
        drawGizmos = profile.DrawGizmos;
        chainColor = profile.ChainColor;
        targetColor = profile.TargetColor;
        poleColor = profile.PoleColor;
        gizmoSize = profile.GizmoSize;

        EnsureSettingsCount();
        _initialized = false;
    }

    [ContextMenu("Copy Current Settings To Profile")]
    public void CopySettingsToProfile()
    {
        CopySettingsTo(settingsProfile);
    }

    public void CopySettingsTo(IKSolverSettings profile)
    {
        if (profile == null)
        {
            return;
        }

        profile.SetSettings(
            perJointSettings,
            solverWeight,
            restPoseWeight,
            poleWeight,
            useContact,
            contactMask,
            contactRayLocalDirection,
            contactRayDistance,
            contactOffset,
            contactWeight,
            solveTiming,
            iterations,
            tolerance,
            initializeOnStart,
            drawGizmos,
            chainColor,
            targetColor,
            poleColor,
            gizmoSize);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(profile);
#endif
    }

    public void Initialize()
    {
        if (autoBuildFromRootToEnd && orderedJoints.Count == 0)
        {
            AutoBuildChain();
        }

        EnsureSettingsCount();

        int count = orderedJoints.Count;
        if (count < 2 || orderedJoints[0] == null || orderedJoints[count - 1] == null)
        {
            _initialized = false;
            return;
        }

        _positions = new Vector3[count];
        _restDirections = new Vector3[count - 1];
        _restLocalDirections = new Vector3[count - 1];
        _lengths = new float[count - 1];
        _hingeAngles = new float[count];
        _restOrientationReferenceRotation =
            orientationReference != null ? orientationReference.rotation : Quaternion.identity;
        _restBendPoleReferencePosition = orientationReference != null && bendPoleTarget != null
            ? orientationReference.InverseTransformPoint(bendPoleTarget.position)
            : Vector3.zero;
        _totalLength = 0f;

        if (_restLocalRotations == null || _restLocalRotations.Length != count)
        {
            _restLocalRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                _restLocalRotations[i] = orderedJoints[i].localRotation;
            }
        }

        BuildRestPoseLookup();

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 from = GetRestPosition(i);
            Vector3 to = GetRestPosition(i + 1);
            _restDirections[i] = to - from;
            _restLocalDirections[i] = GetStableLocalDirection(i);
            _lengths[i] = _restDirections[i].magnitude;
            _totalLength += _lengths[i];
        }

        _initialized = true;
    }

    public void Solve()
    {
        if (solverWeight <= 0f || effectorTarget == null)
        {
            return;
        }

        if (!_initialized || _positions == null || _positions.Length != orderedJoints.Count)
        {
            Initialize();
        }

        if (!_initialized)
        {
            return;
        }

        int count = orderedJoints.Count;
        for (int i = 0; i < count; i++)
        {
            if (orderedJoints[i] == null)
            {
                _initialized = false;
                return;
            }

            if (_positions != null) _positions[i] = orderedJoints[i].position;
        }

        Vector3 targetPosition = GetTargetPosition();
        if (_positions != null)
        {
            Vector3 rootPosition = _positions[0];

            if (_totalLength <= 0.000001f)
            {
                return;
            }

            if ((targetPosition - rootPosition).sqrMagnitude >= _totalLength * _totalLength)
            {
                Vector3 direction = SafeDirection(targetPosition - rootPosition, GetRestDirection(0));
                for (int i = 1; i < count; i++)
                {
                    _positions[i] = _positions[i - 1] + direction * _lengths[i - 1];
                }
            }
            else
            {
                ApplyRestPoseBias(count);

                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    _positions[count - 1] = targetPosition;

                    for (int i = count - 2; i >= 0; i--)
                    {
                        Vector3 direction = SafeDirection(_positions[i] - _positions[i + 1], -GetRestDirection(i));
                        _positions[i] = _positions[i + 1] + direction * _lengths[i];
                    }

                    _positions[0] = rootPosition;

                    for (int i = 1; i < count; i++)
                    {
                        Vector3 direction = SafeDirection(_positions[i] - _positions[i - 1], GetRestDirection(i - 1));
                        _positions[i] = _positions[i - 1] + direction * _lengths[i - 1];
                    }

                    if ((_positions[count - 1] - targetPosition).sqrMagnitude <= tolerance * tolerance)
                    {
                        break;
                    }
                }
            }
        }

        ApplyPole(count);
        ApplySolvedPose(count);
        ApplyConstrainedRefinement(count, targetPosition);
        ApplyAllConstraints(count);
    }

    private Vector3 GetTargetPosition()
    {
        Vector3 targetPosition = effectorTarget.position;

        if (!useContact || contactWeight <= 0f)
        {
            return targetPosition;
        }

        Vector3 rayDirection = effectorTarget.TransformDirection(contactRayLocalDirection.normalized);
        if (rayDirection.sqrMagnitude <= 0.0001f)
        {
            rayDirection = Vector3.down;
        }

        if (Physics.Raycast(effectorTarget.position, rayDirection, out RaycastHit hit, contactRayDistance, contactMask,
                QueryTriggerInteraction.Ignore))
        {
            Vector3 contactPosition = hit.point + hit.normal * contactOffset;
            targetPosition = Vector3.Lerp(targetPosition, contactPosition, contactWeight);
        }

        return targetPosition;
    }

    private void ApplyRestPoseBias(int count)
    {
        if (restPoseWeight <= 0f)
        {
            return;
        }

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 desiredDirection = GetRestDirection(i);
            if (desiredDirection.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Vector3 desiredPosition = _positions[i] + desiredDirection.normalized * _lengths[i];
            _positions[i + 1] = Vector3.Lerp(_positions[i + 1], desiredPosition, restPoseWeight);
        }
    }

    private void ApplyPole(int count)
    {
        if (bendPoleTarget == null || poleWeight <= 0f || count < 3)
        {
            return;
        }

        Vector3 polePosition = GetBendPolePosition();
        for (int i = 1; i < count - 1; i++)
        {
            Plane plane = new Plane(_positions[i + 1] - _positions[i - 1], _positions[i - 1]);
            Vector3 projectedPole = plane.ClosestPointOnPlane(polePosition);
            Vector3 projectedJoint = plane.ClosestPointOnPlane(_positions[i]);
            Vector3 from = projectedJoint - _positions[i - 1];
            Vector3 to = projectedPole - _positions[i - 1];

            if (from.sqrMagnitude <= 0.000001f || to.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            float angle = Vector3.SignedAngle(from, to, plane.normal) * poleWeight;
            _positions[i] = Quaternion.AngleAxis(angle, plane.normal) * (_positions[i] - _positions[i - 1]) +
                            _positions[i - 1];
        }
    }

    private void ApplySolvedPose(int count)
    {
        for (int i = 0; i < count - 1; i++)
        {
            JointSettings settings = perJointSettings[i];
            if (settings.constraintType == JointConstraintType.Locked)
            {
                orderedJoints[i].localRotation =
                    Quaternion.Slerp(orderedJoints[i].localRotation, _restLocalRotations[i], solverWeight);
                continue;
            }

            Vector3 currentDirection = orderedJoints[i + 1].position - orderedJoints[i].position;
            Vector3 solvedDirection = _positions[i + 1] - _positions[i];
            if (currentDirection.sqrMagnitude <= 0.000001f || solvedDirection.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            float jointWeight = solverWeight * (1f - settings.stiffness);
            if (settings.constraintType == JointConstraintType.Hinge)
            {
                ApplyHingeSolvedRotation(i, solvedDirection, jointWeight);
                continue;
            }

            Quaternion targetRotation = Quaternion.FromToRotation(currentDirection, solvedDirection) *
                                        orderedJoints[i].rotation;
            orderedJoints[i].rotation = Quaternion.Slerp(orderedJoints[i].rotation, targetRotation, jointWeight);
            ApplyConstraint(i);
        }

        if (matchTargetRotation && effectorTarget != null)
        {
            Transform end = orderedJoints[count - 1];
            end.rotation = Quaternion.Slerp(end.rotation, effectorTarget.rotation, solverWeight);
            ApplyConstraint(count - 1);
        }
    }

    private void ApplyConstraint(int index)
    {
        if (index < 0 || index >= perJointSettings.Count || index >= orderedJoints.Count)
        {
            return;
        }

        JointSettings settings = perJointSettings[index];
        Transform joint = orderedJoints[index];

        if (settings.constraintType == JointConstraintType.Free)
        {
            return;
        }

        if (settings.constraintType == JointConstraintType.Locked)
        {
            joint.localRotation = _restLocalRotations[index];
            return;
        }

        Quaternion rest = _restLocalRotations[index];
        Quaternion delta = Quaternion.Inverse(rest) * joint.localRotation;

        if (settings.constraintType == JointConstraintType.Hinge)
        {
            Vector3 axis = AxisVector(settings.axis);
            float angle = ExtractTwistAngle(delta, axis);
            angle = GetContinuousHingeAngle(index, angle);
            angle = ClampHingeAngle(angle, settings);
            angle = Mathf.Lerp(angle, settings.preferredAngle, settings.stiffness);
            _hingeAngles[index] = angle;
            joint.localRotation = rest * Quaternion.AngleAxis(angle, axis);
            return;
        }

        joint.localRotation = rest * ConstrainBallSocket(delta, settings);
    }

    private void ApplyHingeSolvedRotation(int index, Vector3 solvedDirection, float jointWeight)
    {
        if (jointWeight <= 0f)
        {
            ApplyConstraint(index);
            return;
        }

        JointSettings settings = perJointSettings[index];
        Transform joint = orderedJoints[index];
        Vector3 axis = AxisVector(settings.axis);
        Vector3 restDirection = _restLocalDirections[index];

        if (restDirection.sqrMagnitude <= 0.000001f || solvedDirection.sqrMagnitude <= 0.000001f)
        {
            ApplyConstraint(index);
            return;
        }

        Quaternion parentRotation = joint.parent != null ? joint.parent.rotation : Quaternion.identity;
        Vector3 localSolvedDirection = Quaternion.Inverse(parentRotation) * solvedDirection.normalized;
        Quaternion rest = _restLocalRotations[index];
        Vector3 localRestDirection = rest * restDirection.normalized;
        Vector3 hingeAxis = rest * axis;

        Vector3 projectedRest = Vector3.ProjectOnPlane(localRestDirection, hingeAxis);
        Vector3 projectedSolved = Vector3.ProjectOnPlane(localSolvedDirection, hingeAxis);
        if (projectedRest.sqrMagnitude <= 0.000001f || projectedSolved.sqrMagnitude <= 0.000001f)
        {
            ApplyConstraint(index);
            return;
        }

        float angle = GetContinuousHingeAngle(index, Vector3.SignedAngle(projectedRest, projectedSolved, hingeAxis));
        angle = ClampHingeAngle(angle, settings);
        angle = Mathf.Lerp(angle, settings.preferredAngle, settings.stiffness);
        _hingeAngles[index] = angle;

        Quaternion targetLocalRotation = rest * Quaternion.AngleAxis(angle, axis);
        joint.localRotation = Quaternion.Slerp(joint.localRotation, targetLocalRotation, jointWeight);
        ApplyConstraint(index);
    }

    private void ApplyConstrainedRefinement(int count, Vector3 targetPosition)
    {
        if (!HasConstrainedJoint(count))
        {
            return;
        }

        int constrainedIterations = Mathf.Max(1, iterations);
        for (int iteration = 0; iteration < constrainedIterations; iteration++)
        {
            for (int i = count - 2; i >= 0; i--)
            {
                JointSettings settings = perJointSettings[i];
                if (settings.constraintType == JointConstraintType.Locked)
                {
                    ApplyConstraint(i);
                    continue;
                }

                Transform joint = orderedJoints[i];
                Transform end = orderedJoints[count - 1];
                Vector3 toEnd = end.position - joint.position;
                Vector3 toTarget = targetPosition - joint.position;
                if (toEnd.sqrMagnitude <= 0.000001f || toTarget.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                float jointWeight = solverWeight * (1f - settings.stiffness);
                if (jointWeight <= 0f)
                {
                    ApplyConstraint(i);
                    continue;
                }

                if (settings.constraintType == JointConstraintType.Hinge)
                {
                    ApplyHingeCcdRotation(i, toEnd, toTarget, jointWeight);
                }
                else
                {
                    Quaternion targetRotation = Quaternion.FromToRotation(toEnd, toTarget) * joint.rotation;
                    joint.rotation = Quaternion.Slerp(joint.rotation, targetRotation, jointWeight);
                    ApplyConstraint(i);
                }
            }

            if ((orderedJoints[count - 1].position - targetPosition).sqrMagnitude <= tolerance * tolerance)
            {
                break;
            }
        }
    }

    private void ApplyAllConstraints(int count)
    {
        int jointCount = Mathf.Min(count, perJointSettings.Count);
        for (int i = 0; i < jointCount; i++)
        {
            ApplyConstraint(i);
        }
    }

    private void ApplyHingeCcdRotation(int index, Vector3 toEnd, Vector3 toTarget, float jointWeight)
    {
        JointSettings settings = perJointSettings[index];
        Transform joint = orderedJoints[index];
        Vector3 axis = AxisVector(settings.axis);
        Quaternion parentRotation = joint.parent != null ? joint.parent.rotation : Quaternion.identity;
        Quaternion rest = _restLocalRotations[index];
        Vector3 hingeAxisWorld = parentRotation * rest * axis;

        Vector3 projectedEnd = Vector3.ProjectOnPlane(toEnd, hingeAxisWorld);
        Vector3 projectedTarget = Vector3.ProjectOnPlane(toTarget, hingeAxisWorld);
        if (projectedEnd.sqrMagnitude <= 0.000001f || projectedTarget.sqrMagnitude <= 0.000001f)
        {
            ApplyConstraint(index);
            return;
        }

        float angleDelta = Vector3.SignedAngle(projectedEnd, projectedTarget, hingeAxisWorld) * jointWeight;
        float currentAngle = GetCurrentHingeAngle(index, axis);
        float angle = ClampHingeAngle(currentAngle + angleDelta, settings);
        angle = Mathf.Lerp(angle, settings.preferredAngle, settings.stiffness);
        _hingeAngles[index] = angle;
        joint.localRotation = rest * Quaternion.AngleAxis(angle, axis);
    }

    private void EnsureSettingsCount()
    {
        while (perJointSettings.Count < orderedJoints.Count)
        {
            perJointSettings.Add(new JointSettings());
        }

        while (perJointSettings.Count > orderedJoints.Count)
        {
            perJointSettings.RemoveAt(perJointSettings.Count - 1);
        }
    }

    private void BuildRestPoseLookup()
    {
        _restPoseJoints.Clear();
        if (restPose == null || root == null)
        {
            return;
        }

        foreach (var joint in orderedJoints)
        {
            _restPoseJoints.Add(FindRestPoseJoint(joint));
        }
    }

    private Transform FindRestPoseJoint(Transform joint)
    {
        if (joint == null || root == null || restPose == null)
        {
            return null;
        }

        if (joint == root)
        {
            return restPose;
        }

        string path = GetPathRelativeToRoot(joint);
        return string.IsNullOrEmpty(path) ? null : restPose.Find(path);
    }

    private string GetPathRelativeToRoot(Transform joint)
    {
        List<string> parts = new List<string>();
        Transform current = joint;
        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        if (current != root)
        {
            return string.Empty;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private Vector3 GetRestPosition(int index)
    {
        if (_restPoseJoints.Count == orderedJoints.Count && _restPoseJoints[index] != null)
        {
            return _restPoseJoints[index].position;
        }

        return orderedJoints[index].position;
    }

    private Vector3 GetStableLocalDirection(int index)
    {
        Transform joint = orderedJoints[index];
        Transform next = orderedJoints[index + 1];
        if (next.parent == joint)
        {
            return next.localPosition;
        }

        Quaternion parentRotation = joint.parent != null ? joint.parent.rotation : Quaternion.identity;
        return Quaternion.Inverse(parentRotation) * (next.position - joint.position);
    }

    private Vector3 GetRestDirection(int index)
    {
        Vector3 restDirection;
        if (_restPoseJoints.Count == orderedJoints.Count && _restPoseJoints[index] != null &&
            _restPoseJoints[index + 1] != null)
        {
            restDirection = _restPoseJoints[index + 1].position - _restPoseJoints[index].position;
        }
        else
        {
            restDirection = _restDirections[index];
        }

        if (orientationReference == null)
        {
            return restDirection;
        }

        Quaternion referenceDelta =
            orientationReference.rotation * Quaternion.Inverse(_restOrientationReferenceRotation);
        return referenceDelta * restDirection;
    }

    private Vector3 GetBendPolePosition()
    {
        if (!keepBendPoleRelativeToOrientation || orientationReference == null || bendPoleTarget == null)
        {
            return bendPoleTarget != null ? bendPoleTarget.position : Vector3.zero;
        }

        return orientationReference.TransformPoint(_restBendPoleReferencePosition);
    }

    private static Vector3 AxisVector(ConstraintAxis axis)
    {
        switch (axis)
        {
            case ConstraintAxis.X:
                return Vector3.right;
            case ConstraintAxis.Y:
                return Vector3.up;
            case ConstraintAxis.Z:
                return Vector3.forward;
            case ConstraintAxis.NegativeX:
                return Vector3.left;
            case ConstraintAxis.NegativeY:
                return Vector3.down;
            case ConstraintAxis.NegativeZ:
                return Vector3.back;
            default:
                return Vector3.forward;
        }
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        euler.x = Mathf.DeltaAngle(0f, euler.x);
        euler.y = Mathf.DeltaAngle(0f, euler.y);
        euler.z = Mathf.DeltaAngle(0f, euler.z);
        return euler;
    }

    private float GetContinuousHingeAngle(int index, float angle)
    {
        if (_hingeAngles == null || index < 0 || index >= _hingeAngles.Length)
        {
            return angle;
        }

        float reference = _hingeAngles[index];
        return reference + Mathf.DeltaAngle(reference, angle);
    }

    private float GetCurrentHingeAngle(int index, Vector3 axis)
    {
        Transform joint = orderedJoints[index];
        Quaternion delta = Quaternion.Inverse(_restLocalRotations[index]) * joint.localRotation;
        return GetContinuousHingeAngle(index, ExtractTwistAngle(delta, axis));
    }

    private bool HasConstrainedJoint(int count)
    {
        int jointCount = Mathf.Min(count - 1, perJointSettings.Count);
        for (int i = 0; i < jointCount; i++)
        {
            JointConstraintType constraintType = perJointSettings[i].constraintType;
            if (constraintType == JointConstraintType.Hinge || constraintType == JointConstraintType.BallSocket ||
                constraintType == JointConstraintType.Locked)
            {
                return true;
            }
        }

        return false;
    }

    private static Quaternion ConstrainBallSocket(Quaternion rotation, JointSettings settings)
    {
        Vector3 euler = NormalizeEuler(rotation.eulerAngles);
        euler.x = ClampAngle(euler.x, settings.xLimits);
        euler.y = ClampAngle(euler.y, settings.yLimits);
        euler.z = ClampAngle(euler.z, settings.zLimits);

        Quaternion constrained = Quaternion.Euler(euler);
        for (int i = 0; i < 2; i++)
        {
            constrained = RemoveLockedAxisTwist(constrained, Vector3.right, settings.xLimits);
            constrained = RemoveLockedAxisTwist(constrained, Vector3.up, settings.yLimits);
            constrained = RemoveLockedAxisTwist(constrained, Vector3.forward, settings.zLimits);
        }

        return constrained;
    }

    private static Quaternion RemoveLockedAxisTwist(Quaternion rotation, Vector3 axis, Vector2 limits)
    {
        if (!IsLockedLimit(limits))
        {
            return rotation;
        }

        Quaternion twist = ExtractTwist(rotation, axis);
        return rotation * Quaternion.Inverse(twist);
    }

    private static bool IsLockedLimit(Vector2 limits)
    {
        return Mathf.Abs(limits.x) <= 0.0001f && Mathf.Abs(limits.y) <= 0.0001f;
    }

    private static float ClampHingeAngle(float angle, JointSettings settings)
    {
        float min = Mathf.Min(settings.min, settings.max);
        float max = Mathf.Max(settings.min, settings.max);
        if (max - min >= 360f)
        {
            return angle;
        }

        return Mathf.Clamp(angle, min, max);
    }

    private static float ClampAngle(float angle, Vector2 limits)
    {
        float min = Mathf.Min(limits.x, limits.y);
        float max = Mathf.Max(limits.x, limits.y);
        if (max - min >= 360f)
        {
            return angle;
        }

        return Mathf.Clamp(angle, min, max);
    }

    private static float ExtractTwistAngle(Quaternion rotation, Vector3 axis)
    {
        Quaternion twist = ExtractTwist(rotation, axis);

        float angle = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(twist.x, twist.y, twist.z), axis), twist.w) *
                      Mathf.Rad2Deg;
        return Mathf.DeltaAngle(0f, angle);
    }

    private static Quaternion ExtractTwist(Quaternion rotation, Vector3 axis)
    {
        axis.Normalize();
        Vector3 vectorPart = new Vector3(rotation.x, rotation.y, rotation.z);
        Vector3 projected = Vector3.Project(vectorPart, axis);
        Quaternion twist = new Quaternion(projected.x, projected.y, projected.z, rotation.w);
        float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
        if (magnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        twist.x /= magnitude;
        twist.y /= magnitude;
        twist.z /= magnitude;
        twist.w /= magnitude;
        return twist;
    }

    private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
    {
        if (direction.sqrMagnitude > 0.000001f)
        {
            return direction.normalized;
        }

        if (fallback.sqrMagnitude > 0.000001f)
        {
            return fallback.normalized;
        }

        return Vector3.forward;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Gizmos.color = chainColor;
        for (int i = 0; i < orderedJoints.Count; i++)
        {
            if (orderedJoints[i] == null)
            {
                continue;
            }

            Gizmos.DrawSphere(orderedJoints[i].position, gizmoSize);
            if (i < orderedJoints.Count - 1 && orderedJoints[i + 1] != null)
            {
                Gizmos.DrawLine(orderedJoints[i].position, orderedJoints[i + 1].position);
            }
        }

        if (effectorTarget != null)
        {
            Gizmos.color = targetColor;
            Gizmos.DrawWireSphere(effectorTarget.position, gizmoSize * 1.5f);
        }

        if (bendPoleTarget != null)
        {
            Gizmos.color = poleColor;
            Gizmos.DrawWireSphere(bendPoleTarget.position, gizmoSize * 1.5f);
        }

        DrawConstraintGizmos();
    }

    private void DrawConstraintGizmos()
    {
        int count = Mathf.Min(orderedJoints.Count, perJointSettings.Count);
        for (int i = 0; i < count; i++)
        {
            Transform joint = orderedJoints[i];
            if (joint == null)
            {
                continue;
            }

            JointSettings settings = perJointSettings[i];
            switch (settings.constraintType)
            {
                case JointConstraintType.Hinge:
                    DrawHingeGizmo(joint, settings);
                    break;
                case JointConstraintType.BallSocket:
                    DrawBallSocketGizmo(joint, settings);
                    break;
                case JointConstraintType.Locked:
                    DrawLockedGizmo(joint);
                    break;
            }
        }
    }

    private void DrawHingeGizmo(Transform joint, JointSettings settings)
    {
        Vector3 axis = joint.TransformDirection(AxisVector(settings.axis));
        float size = gizmoSize * 4f;

        Gizmos.color = hingeColor;
        Gizmos.DrawLine(joint.position - axis * size, joint.position + axis * size);
        Gizmos.DrawWireSphere(joint.position, gizmoSize * 1.2f);
    }

    private void DrawBallSocketGizmo(Transform joint, JointSettings settings)
    {
        float size = gizmoSize * 3f;
        Gizmos.color = ballSocketColor;
        Gizmos.DrawWireSphere(joint.position, gizmoSize * 1.5f);

        DrawLimitedAxisGizmo(joint, Vector3.right, settings.xLimits, size);
        DrawLimitedAxisGizmo(joint, Vector3.up, settings.yLimits, size);
        DrawLimitedAxisGizmo(joint, Vector3.forward, settings.zLimits, size);
    }

    private void DrawLimitedAxisGizmo(Transform joint, Vector3 localAxis, Vector2 limits, float size)
    {
        Vector3 axis = joint.TransformDirection(localAxis);
        float range = Mathf.Abs(limits.y - limits.x);
        Gizmos.color = range <= 0.0001f ? lockedColor : ballSocketColor;
        Gizmos.DrawLine(joint.position, joint.position + axis * size);
    }

    private void DrawLockedGizmo(Transform joint)
    {
        float size = gizmoSize * 2.5f;
        Gizmos.color = lockedColor;
        Gizmos.DrawLine(joint.position - joint.right * size, joint.position + joint.right * size);
        Gizmos.DrawLine(joint.position - joint.up * size, joint.position + joint.up * size);
        Gizmos.DrawLine(joint.position - joint.forward * size, joint.position + joint.forward * size);
        Gizmos.DrawWireCube(joint.position, Vector3.one * gizmoSize * 1.5f);
    }
}