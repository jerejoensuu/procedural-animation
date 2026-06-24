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
        public float preferredAngle;
        [Range(0f, 1f)] public float stiffness;
    }

    [Header("Chain")]
    [SerializeField] private Transform root;
    [SerializeField] private Transform endEffector;
    [SerializeField] private List<Transform> orderedJoints = new List<Transform>();
    [SerializeField] private bool autoBuildFromRootToEnd = true;

    [Header("Targets")]
    [SerializeField] private Transform effectorTarget;
    [SerializeField] private Transform bendPoleTarget;
    [SerializeField] private Transform restPose;
    [SerializeField] private bool matchTargetRotation = true;

    [Header("Per-Joint Settings")]
    [SerializeField] private List<JointSettings> perJointSettings = new List<JointSettings>();

    [Header("Shape Bias")]
    [Range(0f, 1f)] [SerializeField] private float solverWeight = 1f;
    [Range(0f, 1f)] [SerializeField] private float restPoseWeight = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float poleWeight = 1f;

    [Header("Foot/Contact Behavior")]
    [SerializeField] private bool useContact;
    [SerializeField] private LayerMask contactMask = ~0;
    [SerializeField] private Vector3 contactRayLocalDirection = Vector3.down;
    [SerializeField] private float contactRayDistance = 1f;
    [SerializeField] private float contactOffset = 0.02f;
    [Range(0f, 1f)] [SerializeField] private float contactWeight = 1f;

    [Header("Advanced Solver")]
    [SerializeField] private SolveTiming solveTiming = SolveTiming.LateUpdate;
    [Min(1)] [SerializeField] private int iterations = 10;
    [Min(0.00001f)] [SerializeField] private float tolerance = 0.001f;
    [SerializeField] private bool initializeOnStart = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color chainColor = Color.green;
    [SerializeField] private Color targetColor = Color.cyan;
    [SerializeField] private Color poleColor = Color.yellow;
    [SerializeField] private float gizmoSize = 0.04f;

    private readonly List<Transform> restPoseJoints = new List<Transform>();
    private Vector3[] positions;
    private Vector3[] restDirections;
    private float[] lengths;
    private Quaternion[] restLocalRotations;
    private float totalLength;
    private bool initialized;

    public Transform Root
    {
        get { return root; }
        set { root = value; initialized = false; }
    }

    public Transform EndEffector
    {
        get { return endEffector; }
        set { endEffector = value; initialized = false; }
    }

    public Transform EffectorTarget
    {
        get { return effectorTarget; }
        set { effectorTarget = value; }
    }

    public IReadOnlyList<Transform> OrderedJoints
    {
        get { return orderedJoints; }
    }

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
        iterations = Mathf.Max(1, iterations);
        tolerance = Mathf.Max(0.00001f, tolerance);
        contactRayDistance = Mathf.Max(0f, contactRayDistance);
        EnsureSettingsCount();
        initialized = false;
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
            initialized = false;
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

        if (orderedJoints.Count == 0 || orderedJoints[orderedJoints.Count - 1] != root)
        {
            orderedJoints.Clear();
            initialized = false;
            return;
        }

        orderedJoints.Reverse();
        EnsureSettingsCount();
        initialized = false;
    }

    [ContextMenu("Capture Current Rest Pose")]
    public void CaptureCurrentRestPose()
    {
        EnsureSettingsCount();
        restLocalRotations = new Quaternion[orderedJoints.Count];
        for (int i = 0; i < orderedJoints.Count; i++)
        {
            restLocalRotations[i] = orderedJoints[i] != null ? orderedJoints[i].localRotation : Quaternion.identity;
        }

        initialized = false;
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
            initialized = false;
            return;
        }

        positions = new Vector3[count];
        restDirections = new Vector3[count - 1];
        lengths = new float[count - 1];
        totalLength = 0f;

        if (restLocalRotations == null || restLocalRotations.Length != count)
        {
            restLocalRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                restLocalRotations[i] = orderedJoints[i].localRotation;
            }
        }

        BuildRestPoseLookup();

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 from = GetRestPosition(i);
            Vector3 to = GetRestPosition(i + 1);
            restDirections[i] = to - from;
            lengths[i] = restDirections[i].magnitude;
            totalLength += lengths[i];
        }

        initialized = true;
    }

    public void Solve()
    {
        if (solverWeight <= 0f || effectorTarget == null)
        {
            return;
        }

        if (!initialized || positions == null || positions.Length != orderedJoints.Count)
        {
            Initialize();
        }

        if (!initialized)
        {
            return;
        }

        int count = orderedJoints.Count;
        for (int i = 0; i < count; i++)
        {
            if (orderedJoints[i] == null)
            {
                initialized = false;
                return;
            }

            positions[i] = orderedJoints[i].position;
        }

        Vector3 targetPosition = GetTargetPosition();
        Vector3 rootPosition = positions[0];

        if (totalLength <= 0.000001f)
        {
            return;
        }

        if ((targetPosition - rootPosition).sqrMagnitude >= totalLength * totalLength)
        {
            Vector3 direction = SafeDirection(targetPosition - rootPosition, GetRestDirection(0));
            for (int i = 1; i < count; i++)
            {
                positions[i] = positions[i - 1] + direction * lengths[i - 1];
            }
        }
        else
        {
            ApplyRestPoseBias(count);

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                positions[count - 1] = targetPosition;

                for (int i = count - 2; i >= 0; i--)
                {
                    Vector3 direction = SafeDirection(positions[i] - positions[i + 1], -GetRestDirection(i));
                    positions[i] = positions[i + 1] + direction * lengths[i];
                }

                positions[0] = rootPosition;

                for (int i = 1; i < count; i++)
                {
                    Vector3 direction = SafeDirection(positions[i] - positions[i - 1], GetRestDirection(i - 1));
                    positions[i] = positions[i - 1] + direction * lengths[i - 1];
                }

                if ((positions[count - 1] - targetPosition).sqrMagnitude <= tolerance * tolerance)
                {
                    break;
                }
            }
        }

        ApplyPole(count);
        ApplySolvedPose(count);
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

        if (Physics.Raycast(effectorTarget.position, rayDirection, out RaycastHit hit, contactRayDistance, contactMask, QueryTriggerInteraction.Ignore))
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

            Vector3 desiredPosition = positions[i] + desiredDirection.normalized * lengths[i];
            positions[i + 1] = Vector3.Lerp(positions[i + 1], desiredPosition, restPoseWeight);
        }
    }

    private void ApplyPole(int count)
    {
        if (bendPoleTarget == null || poleWeight <= 0f || count < 3)
        {
            return;
        }

        Vector3 polePosition = bendPoleTarget.position;
        for (int i = 1; i < count - 1; i++)
        {
            Plane plane = new Plane(positions[i + 1] - positions[i - 1], positions[i - 1]);
            Vector3 projectedPole = plane.ClosestPointOnPlane(polePosition);
            Vector3 projectedJoint = plane.ClosestPointOnPlane(positions[i]);
            Vector3 from = projectedJoint - positions[i - 1];
            Vector3 to = projectedPole - positions[i - 1];

            if (from.sqrMagnitude <= 0.000001f || to.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            float angle = Vector3.SignedAngle(from, to, plane.normal) * poleWeight;
            positions[i] = Quaternion.AngleAxis(angle, plane.normal) * (positions[i] - positions[i - 1]) + positions[i - 1];
        }
    }

    private void ApplySolvedPose(int count)
    {
        for (int i = 0; i < count - 1; i++)
        {
            JointSettings settings = perJointSettings[i];
            if (settings.constraintType == JointConstraintType.Locked)
            {
                orderedJoints[i].localRotation = Quaternion.Slerp(orderedJoints[i].localRotation, restLocalRotations[i], solverWeight);
                continue;
            }

            Vector3 currentDirection = orderedJoints[i + 1].position - orderedJoints[i].position;
            Vector3 solvedDirection = positions[i + 1] - positions[i];
            if (currentDirection.sqrMagnitude <= 0.000001f || solvedDirection.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            float jointWeight = solverWeight * (1f - settings.stiffness);
            Quaternion targetRotation = Quaternion.FromToRotation(currentDirection, solvedDirection) * orderedJoints[i].rotation;
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
            joint.localRotation = restLocalRotations[index];
            return;
        }

        Quaternion rest = restLocalRotations[index];
        Quaternion delta = Quaternion.Inverse(rest) * joint.localRotation;
        Vector3 euler = NormalizeEuler(delta.eulerAngles);

        if (settings.constraintType == JointConstraintType.Hinge)
        {
            Vector3 axis = AxisVector(settings.axis);
            float angle = Vector3.Dot(euler, axis);
            angle = Mathf.Clamp(angle, settings.min, settings.max);
            angle = Mathf.Lerp(angle, settings.preferredAngle, settings.stiffness);
            joint.localRotation = rest * Quaternion.AngleAxis(angle, axis);
            return;
        }

        euler.x = Mathf.Clamp(euler.x, settings.min, settings.max);
        euler.y = Mathf.Clamp(euler.y, settings.min, settings.max);
        euler.z = Mathf.Clamp(euler.z, settings.min, settings.max);
        joint.localRotation = rest * Quaternion.Euler(euler);
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
        restPoseJoints.Clear();
        if (restPose == null || root == null)
        {
            return;
        }

        for (int i = 0; i < orderedJoints.Count; i++)
        {
            restPoseJoints.Add(FindRestPoseJoint(orderedJoints[i]));
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
        if (restPoseJoints.Count == orderedJoints.Count && restPoseJoints[index] != null)
        {
            return restPoseJoints[index].position;
        }

        return orderedJoints[index].position;
    }

    private Vector3 GetRestDirection(int index)
    {
        if (restPoseJoints.Count == orderedJoints.Count && restPoseJoints[index] != null && restPoseJoints[index + 1] != null)
        {
            return restPoseJoints[index + 1].position - restPoseJoints[index].position;
        }

        return restDirections[index];
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
    }
}
