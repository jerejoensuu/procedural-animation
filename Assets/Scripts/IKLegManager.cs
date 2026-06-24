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

    [Header("Gait")]
    [SerializeField] private float minGroupInterval = 0.12f;
    [SerializeField] private float maxGroupInterval = 0.7f;
    [SerializeField] private bool requireGroupComplete = true;
    [SerializeField] private bool useCadenceFallback;

    [Header("Balance")]
    [SerializeField] private float centerOfMassLeadTime = 0.15f;
    [SerializeField] private float maxSupportCenterError = 0.35f;
    [SerializeField] private bool allowIdleBalanceSteps;

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
    [SerializeField] private Color supportCenterColor = Color.white;
    [SerializeField] private float gizmoSize = 0.06f;

    private Vector3 currentCenterOfMass;
    private Vector3 previousCenterOfMass;
    private Vector3 smoothedVelocity;
    private Vector3 supportCenter;
    private Vector3 desiredSupportCenter;
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

    public Vector3 SupportCenter
    {
        get { return supportCenter; }
    }

    public Vector3 DesiredSupportCenter
    {
        get { return desiredSupportCenter; }
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
        minGroupInterval = Mathf.Max(minTimeBetweenSteps, minGroupInterval);
        maxGroupInterval = Mathf.Max(minGroupInterval, maxGroupInterval);
        centerOfMassLeadTime = Mathf.Max(0f, centerOfMassLeadTime);
        maxSupportCenterError = Mathf.Max(0.01f, maxSupportCenterError);
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
        UpdateSupportCenters();
        TryStartSteps();
    }

    [ContextMenu("Initialize Legs")]
    public void Initialize()
    {
        currentCenterOfMass = Center.position;
        previousCenterOfMass = currentCenterOfMass;
        smoothedVelocity = Vector3.zero;
        supportCenter = currentCenterOfMass;
        desiredSupportCenter = currentCenterOfMass;

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
                leg.target.position = GetLegEndPosition(leg);

                if (leg.solver != null)
                {
                    leg.solver.EffectorTarget = leg.target;
                }
            }

            if (leg.solver != null && leg.target != null && leg.solver.EffectorTarget == null)
            {
                leg.solver.EffectorTarget = leg.target;
            }

            Vector3 startPosition = leg.target != null ? leg.target.position : GetLegEndPosition(leg);
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

        activeStepGroup = GetFirstStepGroup();
        lastStepStartTime = -maxGroupInterval;
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

    private void UpdateSupportCenters()
    {
        Vector3 gravity = GetGravityDirection();
        Vector3 up = -gravity;
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            sum += leg.plantedPosition;
            count++;
        }

        supportCenter = count > 0 ? sum / count : currentCenterOfMass;

        Vector3 predictedCenter = currentCenterOfMass + Vector3.ProjectOnPlane(smoothedVelocity, up) * centerOfMassLeadTime;
        desiredSupportCenter = ProjectPointToPlane(predictedCenter, supportCenter, up);
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
        if (requireGroupComplete && AnyLegStepping())
        {
            return;
        }

        float timeSinceLastStep = Time.time - lastStepStartTime;
        if (timeSinceLastStep < minGroupInterval)
        {
            return;
        }

        float supportError = GetPlanarDistance(supportCenter, desiredSupportCenter);
        bool anyLegPastMaxReach = IsAnyLegPastMaxReach();
        bool isMoving = IsMovingOnGroundPlane();
        float effectiveSupportError = Mathf.Max(maxSupportCenterError, stepLength);
        bool supportNeedsStep = (isMoving || allowIdleBalanceSteps) && supportError >= effectiveSupportError;
        bool cadenceExpired = useCadenceFallback && isMoving && timeSinceLastStep >= maxGroupInterval && supportError >= effectiveSupportError;

        if (!supportNeedsStep && !anyLegPastMaxReach && !cadenceExpired)
        {
            return;
        }

        int groupToStep = supportNeedsStep || cadenceExpired ? FindBestSupportGroup() : FindMostOverreachedGroup();
        if (!HasUsableLegInGroup(groupToStep))
        {
            return;
        }

        bool startedAny = StartStepGroup(groupToStep);

        if (startedAny)
        {
            lastStepStartTime = Time.time;
            activeStepGroup = GetNextStepGroup(groupToStep);
        }
    }

    private bool StartStepGroup(int stepGroup)
    {
        bool startedAny = false;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping || leg.stepGroup != stepGroup)
            {
                continue;
            }

            Vector3 desiredPosition = GetDesiredFootPosition(leg);
            StartStep(leg, desiredPosition);
            startedAny = true;
        }

        return startedAny;
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
        Vector3 predictedOffset = GetPlanarVelocityDirection() * stepLength * Mathf.Max(1f, velocityPredictionWeight);
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

    private Vector3 GetLegEndPosition(ManagedLeg leg)
    {
        if (leg != null && leg.solver != null && leg.solver.EndEffector != null)
        {
            return leg.solver.EndEffector.position;
        }

        if (leg != null && leg.target != null)
        {
            return leg.target.position;
        }

        return GetHomePosition(leg);
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

    private float GetReachError(ManagedLeg leg, Vector3 desiredPosition)
    {
        Vector3 home = GetHomePosition(leg);
        Vector3 up = -GetGravityDirection();
        Vector3 planted = Vector3.ProjectOnPlane(leg.plantedPosition, up);
        Vector3 homePlanar = Vector3.ProjectOnPlane(home, up);
        Vector3 desiredPlanar = Vector3.ProjectOnPlane(desiredPosition, up);
        float homeReach = Vector3.Distance(planted, homePlanar) - maxReach;
        float desiredReach = Vector3.Distance(planted, desiredPlanar) - maxReach;
        return Mathf.Max(homeReach, desiredReach, 0f);
    }

    private bool IsAnyLegInGroupPastMaxReach(int stepGroup)
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping || leg.stepGroup != stepGroup)
            {
                continue;
            }

            if (IsPastMaxReach(leg, GetDesiredFootPosition(leg)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAnyLegPastMaxReach()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            if (IsPastMaxReach(leg, GetDesiredFootPosition(leg)))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyLegStepping()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (IsLegUsable(leg) && leg.isStepping)
            {
                return true;
            }
        }

        return false;
    }

    private int GetFirstStepGroup()
    {
        bool found = false;
        int firstGroup = 0;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg))
            {
                continue;
            }

            if (!found || leg.stepGroup < firstGroup)
            {
                firstGroup = leg.stepGroup;
                found = true;
            }
        }

        return firstGroup;
    }

    private int GetNextStepGroup(int currentGroup)
    {
        bool foundHigher = false;
        int nextHigher = currentGroup;
        bool foundAny = false;
        int firstGroup = currentGroup;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg))
            {
                continue;
            }

            if (!foundAny || leg.stepGroup < firstGroup)
            {
                firstGroup = leg.stepGroup;
                foundAny = true;
            }

            if (leg.stepGroup > currentGroup && (!foundHigher || leg.stepGroup < nextHigher))
            {
                nextHigher = leg.stepGroup;
                foundHigher = true;
            }
        }

        if (foundHigher)
        {
            return nextHigher;
        }

        return foundAny ? firstGroup : currentGroup;
    }

    private int FindBestSupportGroup()
    {
        bool found = false;
        int bestGroup = activeStepGroup;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            int group = leg.stepGroup;
            if (HasScoredGroupBefore(group, i))
            {
                continue;
            }

            Vector3 simulatedCenter = GetSimulatedSupportCenterAfterGroupStep(group);
            float score = GetPlanarDistance(simulatedCenter, desiredSupportCenter);
            if (!found || score < bestScore)
            {
                bestScore = score;
                bestGroup = group;
                found = true;
            }
        }

        return found ? bestGroup : activeStepGroup;
    }

    private int FindMostOverreachedGroup()
    {
        bool found = false;
        int bestGroup = activeStepGroup;
        float bestReachError = 0f;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            float reachError = GetReachError(leg, GetDesiredFootPosition(leg));
            if (!found || reachError > bestReachError)
            {
                bestReachError = reachError;
                bestGroup = leg.stepGroup;
                found = true;
            }
        }

        return found ? bestGroup : activeStepGroup;
    }

    private Vector3 GetSimulatedSupportCenterAfterGroupStep(int stepGroup)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            sum += leg.stepGroup == stepGroup ? GetDesiredFootPosition(leg) : leg.plantedPosition;
            count++;
        }

        return count > 0 ? sum / count : supportCenter;
    }

    private bool HasScoredGroupBefore(int stepGroup, int beforeIndex)
    {
        for (int i = 0; i < beforeIndex; i++)
        {
            ManagedLeg leg = legs[i];
            if (IsLegUsable(leg) && !leg.isStepping && leg.stepGroup == stepGroup)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasUsableLegInGroup(int stepGroup)
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (IsLegUsable(leg) && !leg.isStepping && leg.stepGroup == stepGroup)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsMovingOnGroundPlane()
    {
        Vector3 up = -GetGravityDirection();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(smoothedVelocity, up);
        return planarVelocity.magnitude >= minimumMoveSpeedForPrediction;
    }

    private float GetPlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 up = -GetGravityDirection();
        return Vector3.Distance(Vector3.ProjectOnPlane(a, up), Vector3.ProjectOnPlane(b, up));
    }

    private static Vector3 ProjectPointToPlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        return point - planeNormal * Vector3.Dot(point - planePoint, planeNormal);
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

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Vector3 center = Application.isPlaying ? currentCenterOfMass : Center.position;
        Gizmos.color = centerOfMassColor;
        Gizmos.DrawWireSphere(center, gizmoSize * 1.5f);

        if (Application.isPlaying)
        {
            Gizmos.color = supportCenterColor;
            Gizmos.DrawSphere(supportCenter, gizmoSize);
            Gizmos.DrawLine(supportCenter, desiredSupportCenter);

            Gizmos.color = desiredColor;
            Gizmos.DrawWireSphere(desiredSupportCenter, gizmoSize * 1.25f);
        }

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
