using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "IK Solver Settings", menuName = "IK/Solver Settings")]
public class IKSolverSettings : ScriptableObject
{
    [Header("Per-Joint Settings")] [SerializeField]
    private List<IKSolver.JointSettings> perJointSettings = new();

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
    private IKSolver.SolveTiming solveTiming = IKSolver.SolveTiming.LateUpdate;

    [Min(1)] [SerializeField] private int iterations = 10;
    [Min(0.00001f)] [SerializeField] private float tolerance = 0.001f;
    [SerializeField] private bool initializeOnStart = true;

    [Header("Debug")] [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color chainColor = Color.green;
    [SerializeField] private Color targetColor = Color.cyan;
    [SerializeField] private Color poleColor = Color.yellow;
    [SerializeField] private float gizmoSize = 0.04f;

    public IReadOnlyList<IKSolver.JointSettings> PerJointSettings => perJointSettings;

    public float SolverWeight => solverWeight;

    public float RestPoseWeight => restPoseWeight;

    public float PoleWeight => poleWeight;

    public bool UseContact => useContact;

    public LayerMask ContactMask => contactMask;

    public Vector3 ContactRayLocalDirection => contactRayLocalDirection;

    public float ContactRayDistance => contactRayDistance;

    public float ContactOffset => contactOffset;

    public float ContactWeight => contactWeight;

    public IKSolver.SolveTiming SolveTiming => solveTiming;

    public int Iterations => iterations;

    public float Tolerance => tolerance;

    public bool InitializeOnStart => initializeOnStart;

    public bool DrawGizmos => drawGizmos;

    public Color ChainColor => chainColor;

    public Color TargetColor => targetColor;

    public Color PoleColor => poleColor;

    public float GizmoSize => gizmoSize;

    private void OnValidate()
    {
        iterations = Mathf.Max(1, iterations);
        tolerance = Mathf.Max(0.00001f, tolerance);
        contactRayDistance = Mathf.Max(0f, contactRayDistance);
        gizmoSize = Mathf.Max(0f, gizmoSize);
        EnsureJointSettingsNotNull();
    }

    public void CopyFromSolver(IKSolver solver)
    {
        if (solver == null)
        {
            return;
        }

        solver.CopySettingsTo(this);
    }

    public void SetSettings(
        IReadOnlyList<IKSolver.JointSettings> sourcePerJointSettings,
        float sourceSolverWeight,
        float sourceRestPoseWeight,
        float sourcePoleWeight,
        bool sourceUseContact,
        LayerMask sourceContactMask,
        Vector3 sourceContactRayLocalDirection,
        float sourceContactRayDistance,
        float sourceContactOffset,
        float sourceContactWeight,
        IKSolver.SolveTiming sourceSolveTiming,
        int sourceIterations,
        float sourceTolerance,
        bool sourceInitializeOnStart,
        bool sourceDrawGizmos,
        Color sourceChainColor,
        Color sourceTargetColor,
        Color sourcePoleColor,
        float sourceGizmoSize)
    {
        perJointSettings.Clear();
        if (sourcePerJointSettings != null)
        {
            foreach (var jointSettings in sourcePerJointSettings)
            {
                perJointSettings.Add(CloneJointSettings(jointSettings));
            }
        }

        solverWeight = sourceSolverWeight;
        restPoseWeight = sourceRestPoseWeight;
        poleWeight = sourcePoleWeight;
        useContact = sourceUseContact;
        contactMask = sourceContactMask;
        contactRayLocalDirection = sourceContactRayLocalDirection;
        contactRayDistance = sourceContactRayDistance;
        contactOffset = sourceContactOffset;
        contactWeight = sourceContactWeight;
        solveTiming = sourceSolveTiming;
        iterations = sourceIterations;
        tolerance = sourceTolerance;
        initializeOnStart = sourceInitializeOnStart;
        drawGizmos = sourceDrawGizmos;
        chainColor = sourceChainColor;
        targetColor = sourceTargetColor;
        poleColor = sourcePoleColor;
        gizmoSize = sourceGizmoSize;

        OnValidate();
    }

    public static IKSolver.JointSettings CloneJointSettings(IKSolver.JointSettings source)
    {
        IKSolver.JointSettings clone = new IKSolver.JointSettings();
        if (source == null)
        {
            return clone;
        }

        clone.constraintType = source.constraintType;
        clone.axis = source.axis;
        clone.min = source.min;
        clone.max = source.max;
        clone.xLimits = source.xLimits;
        clone.yLimits = source.yLimits;
        clone.zLimits = source.zLimits;
        clone.preferredAngle = source.preferredAngle;
        clone.stiffness = source.stiffness;
        return clone;
    }

    private void EnsureJointSettingsNotNull()
    {
        for (int i = 0; i < perJointSettings.Count; i++)
        {
            perJointSettings[i] ??= new IKSolver.JointSettings();
        }
    }
}