using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class IKLegManager : MonoBehaviour
{
    [System.Serializable]
    public class ManagedLeg
    {
        public string name;
        public IKSolver solver;
        public Transform target;
        public Transform homeAnchor;
        public int stepGroup;
        public Vector3 localHomeOffset;
        public bool enabled = true;

        [HideInInspector] public Vector3 plantedPosition;
        [HideInInspector] public Vector3 stepStartPosition;
        [HideInInspector] public Vector3 stepEndPosition;
        [HideInInspector] public float stepProgress;
        [HideInInspector] public bool isStepping;
    }

    [Header("Body")]
    [SerializeField] private Transform centerOfMass;
    [SerializeField] private Transform movementReference;
    [SerializeField] private bool autoCaptureHomeOffsets = true;
    [SerializeField] private bool autoCreateMissingTargets = true;
    [SerializeField] private bool keepPlantedTargetsInWorld = true;

    [Header("Legs")]
    [SerializeField] private List<ManagedLeg> legs = new List<ManagedLeg>();

    [Header("Step")]
    [SerializeField] private float stepHeight = 0.35f;
    [SerializeField] private float maxReach = 1.25f;
    [SerializeField] private float stepLength = 0.6f;
    [SerializeField] private float stepDuration = 0.18f;
    [SerializeField] private AnimationCurve stepCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve stepHeightCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0f));
    [SerializeField] private float minTimeBetweenSteps = 0.05f;
    [SerializeField] private bool allowMultipleLegsPerGroup = true;

    [Header("Grounding")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundProbeHeight = 1.5f;
    [SerializeField] private float groundProbeDistance = 3f;
    [SerializeField] private float groundOffset = 0.02f;
    [SerializeField] private Vector3 gravityDirection = Vector3.down;

    [Header("Prediction")]
    [SerializeField] private float velocitySmoothing = 12f;
    [SerializeField] private float velocityPredictionWeight = 1f;
    [SerializeField] private float minimumMoveSpeedForPrediction = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color plantedColor = Color.green;
    [SerializeField] private Color desiredColor = Color.cyan;
    [SerializeField] private Color steppingColor = Color.yellow;
    [SerializeField] private Color centerOfMassColor = Color.magenta;
    [SerializeField] private float gizmoSize = 0.06f;

    private Vector3 currentCenterOfMass;
    private Vector3 previousCenterOfMass;
    private Vector3 smoothedVelocity;
    private int activeStepGroup;
    private float lastStepStartTime;
    private bool initialized;

    public Vector3 CurrentCenterOfMass
    {
        get { return currentCenterOfMass; }
    }

    public Vector3 SmoothedVelocity
    {
        get { return smoothedVelocity; }
    }

    public IReadOnlyList<ManagedLeg> Legs
    {
        get { return legs; }
    }

    private Transform Body
    {
        get { return movementReference != null ? movementReference : transform; }
    }

    private Transform Center
    {
        get { return centerOfMass != null ? centerOfMass : transform; }
    }

    private void Reset()
    {
        centerOfMass = transform;
        movementReference = transform;
        stepHeightCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnValidate()
    {
        stepHeight = Mathf.Max(0f, stepHeight);
        maxReach = Mathf.Max(0.01f, maxReach);
        stepLength = Mathf.Max(0f, stepLength);
        stepDuration = Mathf.Max(0.01f, stepDuration);
        minTimeBetweenSteps = Mathf.Max(0f, minTimeBetweenSteps);
        groundProbeHeight = Mathf.Max(0f, groundProbeHeight);
        groundProbeDistance = Mathf.Max(0f, groundProbeDistance);
        velocitySmoothing = Mathf.Max(0f, velocitySmoothing);
        velocityPredictionWeight = Mathf.Max(0f, velocityPredictionWeight);
    }

    private void LateUpdate()
    {
        if (!initialized)
        {
            Initialize();
        }

        UpdateCenterOfMass();
        UpdateSteppingLegs();
        TryStartSteps();
    }

    [ContextMenu("Initialize Legs")]
    public void Initialize()
    {
        currentCenterOfMass = Center.position;
        previousCenterOfMass = currentCenterOfMass;
        smoothedVelocity = Vector3.zero;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (leg == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(leg.name))
            {
                leg.name = "Leg " + i;
            }

            if (leg.target == null && leg.solver != null)
            {
                leg.target = leg.solver.EffectorTarget;
            }

            if (leg.target == null && autoCreateMissingTargets)
            {
                GameObject targetObject = new GameObject(leg.name + " IK Target");
                leg.target = targetObject.transform;

                if (leg.solver != null)
                {
                    leg.solver.EffectorTarget = leg.target;
                }
            }

            if (leg.solver != null && leg.target != null && leg.solver.EffectorTarget == null)
            {
                leg.solver.EffectorTarget = leg.target;
            }

            Vector3 startPosition = leg.target != null ? leg.target.position : GetHomePosition(leg);
            if (TryProjectToGround(startPosition, out Vector3 groundedPosition))
            {
                startPosition = groundedPosition;
            }

            if (leg.target != null)
            {
                leg.target.position = startPosition;
            }

            leg.plantedPosition = startPosition;
            leg.stepStartPosition = startPosition;
            leg.stepEndPosition = startPosition;
            leg.stepProgress = 1f;
            leg.isStepping = false;

            if (autoCaptureHomeOffsets && leg.homeAnchor == null)
            {
                leg.localHomeOffset = Body.InverseTransformPoint(startPosition);
            }
        }

        activeStepGroup = 0;
        lastStepStartTime = -minTimeBetweenSteps;
        initialized = true;
    }

    private void UpdateCenterOfMass()
    {
        previousCenterOfMass = currentCenterOfMass;
        currentCenterOfMass = Center.position;

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 velocity = (currentCenterOfMass - previousCenterOfMass) / deltaTime;
        float blend = velocitySmoothing <= 0f ? 1f : 1f - Mathf.Exp(-velocitySmoothing * deltaTime);
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, blend);
    }

    private void UpdateSteppingLegs()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg))
            {
                continue;
            }

            if (!leg.isStepping)
            {
                if (keepPlantedTargetsInWorld)
                {
                    leg.target.position = leg.plantedPosition;
                }

                continue;
            }

            leg.stepProgress = Mathf.Clamp01(leg.stepProgress + Time.deltaTime / stepDuration);
            float t = stepCurve != null ? stepCurve.Evaluate(leg.stepProgress) : leg.stepProgress;
            float height = stepHeightCurve != null ? stepHeightCurve.Evaluate(leg.stepProgress) * stepHeight : Mathf.Sin(leg.stepProgress * Mathf.PI) * stepHeight;
            Vector3 position = Vector3.Lerp(leg.stepStartPosition, leg.stepEndPosition, t) - GetGravityDirection() * height;

            leg.target.position = position;

            if (leg.stepProgress >= 1f)
            {
                leg.target.position = leg.stepEndPosition;
                leg.plantedPosition = leg.stepEndPosition;
                leg.isStepping = false;
            }
        }
    }

    private void TryStartSteps()
    {
        if (Time.time - lastStepStartTime < minTimeBetweenSteps)
        {
            return;
        }

        if (AnyLegSteppingOutsideGroup(activeStepGroup))
        {
            return;
        }

        bool startedAny = false;
        int bestIndex = -1;
        float bestError = 0f;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping || Mathf.Abs(leg.stepGroup) % 2 != activeStepGroup)
            {
                continue;
            }

            Vector3 desiredPosition = GetDesiredFootPosition(leg);
            float error = GetStepError(leg, desiredPosition);
            if (error <= stepLength && !IsPastMaxReach(leg, desiredPosition))
            {
                continue;
            }

            if (allowMultipleLegsPerGroup)
            {
                StartStep(leg, desiredPosition);
                startedAny = true;
            }
            else if (error > bestError)
            {
                bestError = error;
                bestIndex = i;
            }
        }

        if (!allowMultipleLegsPerGroup && bestIndex >= 0)
        {
            StartStep(legs[bestIndex], GetDesiredFootPosition(legs[bestIndex]));
            startedAny = true;
        }

        if (startedAny)
        {
            lastStepStartTime = Time.time;
            return;
        }

        if (!AnyLegSteppingInGroup(activeStepGroup))
        {
            activeStepGroup = 1 - activeStepGroup;
        }
    }

    private void StartStep(ManagedLeg leg, Vector3 desiredPosition)
    {
        leg.stepStartPosition = leg.plantedPosition;
        leg.target.position = leg.stepStartPosition;
        leg.stepEndPosition = desiredPosition;
        leg.stepProgress = 0f;
        leg.isStepping = true;
    }

    private Vector3 GetDesiredFootPosition(ManagedLeg leg)
    {
        Vector3 home = GetHomePosition(leg);
        Vector3 predictedOffset = GetPlanarVelocityDirection() * stepLength * velocityPredictionWeight;
        Vector3 desired = home + predictedOffset;

        if (TryProjectToGround(desired, out Vector3 groundedPosition))
        {
            desired = groundedPosition;
        }

        return desired;
    }

    private Vector3 GetHomePosition(ManagedLeg leg)
    {
        if (leg.homeAnchor != null)
        {
            return leg.homeAnchor.position;
        }

        return Body.TransformPoint(leg.localHomeOffset);
    }

    private float GetStepError(ManagedLeg leg, Vector3 desiredPosition)
    {
        Vector3 up = -GetGravityDirection();
        Vector3 current = Vector3.ProjectOnPlane(leg.plantedPosition, up);
        Vector3 desired = Vector3.ProjectOnPlane(desiredPosition, up);
        return Vector3.Distance(current, desired);
    }

    private bool IsPastMaxReach(ManagedLeg leg, Vector3 desiredPosition)
    {
        Vector3 home = GetHomePosition(leg);
        Vector3 up = -GetGravityDirection();
        Vector3 planted = Vector3.ProjectOnPlane(leg.plantedPosition, up);
        Vector3 homePlanar = Vector3.ProjectOnPlane(home, up);
        Vector3 desiredPlanar = Vector3.ProjectOnPlane(desiredPosition, up);
        return Vector3.Distance(planted, homePlanar) > maxReach || Vector3.Distance(planted, desiredPlanar) > maxReach;
    }

    private bool TryProjectToGround(Vector3 position, out Vector3 groundedPosition)
    {
        Vector3 gravity = GetGravityDirection();
        Vector3 origin = position - gravity * groundProbeHeight;
        float distance = groundProbeHeight + groundProbeDistance;

        if (Physics.Raycast(origin, gravity, out RaycastHit hit, distance, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundedPosition = hit.point - gravity * groundOffset;
            return true;
        }

        groundedPosition = position;
        return false;
    }

    private Vector3 GetPlanarVelocityDirection()
    {
        Vector3 up = -GetGravityDirection();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(smoothedVelocity, up);
        if (planarVelocity.magnitude >= minimumMoveSpeedForPrediction)
        {
            return planarVelocity.normalized;
        }

        return Vector3.zero;
    }

    private Vector3 GetGravityDirection()
    {
        if (gravityDirection.sqrMagnitude <= 0.000001f)
        {
            return Vector3.down;
        }

        return gravityDirection.normalized;
    }

    private bool IsLegUsable(ManagedLeg leg)
    {
        return leg != null && leg.enabled && leg.target != null;
    }

    private bool AnyLegSteppingInGroup(int group)
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (IsLegUsable(leg) && leg.isStepping && Mathf.Abs(leg.stepGroup) % 2 == group)
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyLegSteppingOutsideGroup(int group)
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (IsLegUsable(leg) && leg.isStepping && Mathf.Abs(leg.stepGroup) % 2 != group)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Vector3 center = Application.isPlaying ? currentCenterOfMass : Center.position;
        Gizmos.color = centerOfMassColor;
        Gizmos.DrawWireSphere(center, gizmoSize * 1.5f);

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (leg == null)
            {
                continue;
            }

            Vector3 desired = GetDesiredFootPosition(leg);
            Gizmos.color = desiredColor;
            Gizmos.DrawWireSphere(desired, gizmoSize);

            if (leg.target != null)
            {
                Gizmos.color = leg.isStepping ? steppingColor : plantedColor;
                Gizmos.DrawSphere(leg.target.position, gizmoSize);
                Gizmos.DrawLine(GetHomePosition(leg), leg.target.position);
            }
        }
    }
}
