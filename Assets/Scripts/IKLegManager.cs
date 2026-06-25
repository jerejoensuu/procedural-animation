using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
        [HideInInspector] public Vector3 stepHorizontalPosition;
        [HideInInspector] public float stepElevation;
        [HideInInspector] public float stepProgress;
        [HideInInspector] public bool isStepping;
    }

    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference rotateAction;

    [Header("Body")]
    [SerializeField] private Transform centerOfMass;
    [SerializeField] private Transform movementReference;
    [SerializeField] private bool autoCaptureHomeOffsets = true;
    [SerializeField] private bool autoCreateMissingTargets = true;
    [SerializeField] private bool keepPlantedTargetsInWorld = true;

    [Header("Body Orientation")]
    [SerializeField] private bool orientBodyToLegEnds = true;
    [SerializeField] private float bodyOrientationSmoothing = 12f;
    [SerializeField] private float minimumBodyOrientationFootSpread = 0.01f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 180f;

    [Header("Legs")]
    [SerializeField] private IKSolverSettings solverSettingsProfile;
    [SerializeField] private int copySettingsFromLegIndex;
    [SerializeField] private List<ManagedLeg> legs = new List<ManagedLeg>();

    [Header("Step")]
    [SerializeField] private float stepHeight = 0.35f;
    [SerializeField] private float maxReach = 1.25f;
    [SerializeField] private float stepLength = 0.6f;
    [SerializeField] private float maxElevationError = 0.15f;
    [SerializeField] private float stepDuration = 0.18f;
    [SerializeField] private AnimationCurve stepHeightCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0f));

    [Header("Gait")]
    [SerializeField] private bool requireGroupComplete = true;

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
    [SerializeField] private float angularPredictionTime = 0.15f;
    [SerializeField] private float maxFootTwistAngle = 25f;
    [SerializeField] private float minimumTurnSpeedForPrediction = 5f;

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
    private float currentTurnDegreesPerSecond;
    private int activeStepGroup;
    private bool initialized;
    private Rigidbody bodyRigidbody;

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
        bodyRigidbody = Body.GetComponent<Rigidbody>();
        Initialize();
    }

    private void OnEnable()
    {
        moveAction?.action?.Enable();
        rotateAction?.action?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.action?.Disable();
        rotateAction?.action?.Disable();
    }

    private void OnValidate()
    {
        stepHeight = Mathf.Max(0f, stepHeight);
        maxReach = Mathf.Max(0.01f, maxReach);
        stepLength = Mathf.Max(0f, stepLength);
        maxElevationError = Mathf.Max(0f, maxElevationError);
        stepDuration = Mathf.Max(0.01f, stepDuration);
        centerOfMassLeadTime = Mathf.Max(0f, centerOfMassLeadTime);
        maxSupportCenterError = Mathf.Max(0.01f, maxSupportCenterError);
        groundProbeHeight = Mathf.Max(0f, groundProbeHeight);
        groundProbeDistance = Mathf.Max(0f, groundProbeDistance);
        velocitySmoothing = Mathf.Max(0f, velocitySmoothing);
        velocityPredictionWeight = Mathf.Max(0f, velocityPredictionWeight);
        angularPredictionTime = Mathf.Max(0f, angularPredictionTime);
        maxFootTwistAngle = Mathf.Max(0f, maxFootTwistAngle);
        minimumTurnSpeedForPrediction = Mathf.Max(0f, minimumTurnSpeedForPrediction);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        bodyOrientationSmoothing = Mathf.Max(0f, bodyOrientationSmoothing);
        minimumBodyOrientationFootSpread = Mathf.Max(0.0001f, minimumBodyOrientationFootSpread);
        copySettingsFromLegIndex = Mathf.Max(0, copySettingsFromLegIndex);
    }

    private void FixedUpdate()
    {
        RotateBody();
        MoveBody();
    }

    private void RotateBody()
    {
        float rotateInput = ReadRotateInput();
        currentTurnDegreesPerSecond = rotateInput * turnSpeed;
        if (Mathf.Abs(rotateInput) < 0.0001f)
        {
            return;
        }

        Transform body = Body;
        Quaternion deltaRotation = Quaternion.AngleAxis(currentTurnDegreesPerSecond * Time.fixedDeltaTime, body.up);
        Quaternion rotation = deltaRotation * body.rotation;

        if (bodyRigidbody != null)
        {
            bodyRigidbody.MoveRotation(rotation);
        }
        else
        {
            body.rotation = rotation;
        }
    }

    private float ReadRotateInput()
    {
        if (rotateAction == null || rotateAction.action == null)
        {
            return 0f;
        }

        InputAction action = rotateAction.action;
        if (action.activeControl is Vector2Control || action.expectedControlType == "Vector2" || action.expectedControlType == "Stick")
        {
            return Mathf.Clamp(action.ReadValue<Vector2>().x, -1f, 1f);
        }

        return Mathf.Clamp(action.ReadValue<float>(), -1f, 1f);
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
        UpdateBodyOrientation();
        TryStartSteps();
    }

    private void MoveBody()
    {
        if (moveAction == null || moveAction.action == null)
        {
            return;
        }

        Vector2 moveInput = moveAction.action.ReadValue<Vector2>();
        if (moveInput.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 right = Body.right;
        Vector3 forward = Body.forward;
        Vector3 moveDirection = right * moveInput.x + forward * moveInput.y;
        if (moveDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 displacement = moveDirection.normalized * (Mathf.Clamp01(moveInput.magnitude) * moveSpeed * Time.fixedDeltaTime);

        if (bodyRigidbody != null)
        {
            bodyRigidbody.MovePosition(bodyRigidbody.position + displacement);
        }
        else
        {
            Body.position += displacement;
        }
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

            if (solverSettingsProfile != null && leg.solver != null)
            {
                leg.solver.ApplySettings(solverSettingsProfile);
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

            if (leg.solver != null && leg.solver.OrientationReference == null)
            {
                leg.solver.OrientationReference = Body;
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
            leg.stepHorizontalPosition = GetHorizontalPosition(startPosition);
            leg.stepElevation = GetElevation(startPosition);
            leg.stepProgress = 1f;
            leg.isStepping = false;

            if (autoCaptureHomeOffsets && leg.homeAnchor == null)
            {
                leg.localHomeOffset = Body.InverseTransformPoint(startPosition);
            }
        }

        activeStepGroup = GetFirstStepGroup();
        initialized = true;
    }

    [ContextMenu("Apply Solver Settings To Legs")]
    public void ApplySolverSettingsToLegs()
    {
        if (solverSettingsProfile == null)
        {
            return;
        }

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (leg == null || leg.solver == null)
            {
                continue;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RecordObject(leg.solver, "Apply IK Solver Settings");
            }
#endif

            leg.solver.ApplySettings(solverSettingsProfile);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(leg.solver);
            }
#endif
        }
    }

    [ContextMenu("Copy Leg Solver Settings To Profile")]
    public void CopyLegSolverSettingsToProfile()
    {
        if (solverSettingsProfile == null || legs.Count == 0)
        {
            return;
        }

        int index = Mathf.Clamp(copySettingsFromLegIndex, 0, legs.Count - 1);
        ManagedLeg leg = legs[index];
        if (leg == null || leg.solver == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.RecordObject(solverSettingsProfile, "Copy IK Solver Settings To Profile");
        }
#endif

        solverSettingsProfile.CopyFromSolver(leg.solver);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(solverSettingsProfile);
        }
#endif
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

    private void UpdateBodyOrientation()
    {
        if (!orientBodyToLegEnds || !TryGetLegEndAverageNormal(out Vector3 targetUp))
        {
            return;
        }

        Transform body = Body;
        Vector3 forward = Vector3.ProjectOnPlane(body.forward, targetUp);
        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = Vector3.ProjectOnPlane(body.up, targetUp);
        }

        if (forward.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, targetUp);
        float blend = bodyOrientationSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-bodyOrientationSmoothing * Time.deltaTime);
        Quaternion rotation = Quaternion.Slerp(body.rotation, targetRotation, blend);

        if (bodyRigidbody != null)
        {
            bodyRigidbody.MoveRotation(rotation);
        }
        else
        {
            body.rotation = rotation;
        }
    }

    private bool TryGetLegEndAverageNormal(out Vector3 averageNormal)
    {
        const float sideThreshold = 0.0001f;

        Vector3 leftSum = Vector3.zero;
        Vector3 rightSum = Vector3.zero;
        Vector3 backSum = Vector3.zero;
        Vector3 frontSum = Vector3.zero;
        int leftCount = 0;
        int rightCount = 0;
        int backCount = 0;
        int frontCount = 0;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg))
            {
                continue;
            }

            Vector3 endPosition = GetCurrentLegEndPosition(leg);
            Vector3 bodyOffset = GetLegBodyOffset(leg);

            if (bodyOffset.x < -sideThreshold)
            {
                leftSum += endPosition;
                leftCount++;
            }
            else if (bodyOffset.x > sideThreshold)
            {
                rightSum += endPosition;
                rightCount++;
            }

            if (bodyOffset.z < -sideThreshold)
            {
                backSum += endPosition;
                backCount++;
            }
            else if (bodyOffset.z > sideThreshold)
            {
                frontSum += endPosition;
                frontCount++;
            }
        }

        bool hasRightAxis = leftCount > 0 && rightCount > 0;
        bool hasForwardAxis = backCount > 0 && frontCount > 0;

        if (!hasRightAxis && !hasForwardAxis)
        {
            averageNormal = -GetGravityDirection();
            return false;
        }

        Transform body = Body;
        Vector3 rightAxis = hasRightAxis ? rightSum / rightCount - leftSum / leftCount : Vector3.ProjectOnPlane(body.right, -GetGravityDirection());
        Vector3 forwardAxis = hasForwardAxis ? frontSum / frontCount - backSum / backCount : Vector3.ProjectOnPlane(body.forward, -GetGravityDirection());

        if (rightAxis.sqrMagnitude <= minimumBodyOrientationFootSpread * minimumBodyOrientationFootSpread ||
            forwardAxis.sqrMagnitude <= minimumBodyOrientationFootSpread * minimumBodyOrientationFootSpread)
        {
            averageNormal = -GetGravityDirection();
            return false;
        }

        Vector3 normal = Vector3.Cross(forwardAxis, rightAxis);

        if (normal.sqrMagnitude <= minimumBodyOrientationFootSpread * minimumBodyOrientationFootSpread)
        {
            averageNormal = -GetGravityDirection();
            return false;
        }

        averageNormal = normal.normalized;
        Vector3 gravityUp = -GetGravityDirection();
        if (Vector3.Dot(averageNormal, gravityUp) < 0f)
        {
            averageNormal = -averageNormal;
        }

        return true;
    }

    private Vector3 GetLegBodyOffset(ManagedLeg leg)
    {
        if (leg.localHomeOffset.sqrMagnitude > 0.000001f)
        {
            return leg.localHomeOffset;
        }

        return Body.InverseTransformPoint(GetHomePosition(leg));
    }

    private Vector3 GetCurrentLegEndPosition(ManagedLeg leg)
    {
        if (leg.isStepping)
        {
            return leg.target.position;
        }

        return leg.plantedPosition;
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

            Vector3 liveLanding = GetDesiredFootPosition(leg);
            Vector3 liveLandingHorizontal = GetHorizontalPosition(liveLanding);
            float liveLandingElevation = GetElevation(liveLanding);
            float remainingSwingTime = Mathf.Max((1f - leg.stepProgress) * stepDuration, Time.deltaTime);
            Vector3 requiredVelocity = (liveLandingHorizontal - leg.stepHorizontalPosition) / remainingSwingTime;
            float requiredElevationVelocity = (liveLandingElevation - leg.stepElevation) / remainingSwingTime;

            leg.stepEndPosition = liveLanding;
            leg.stepProgress = Mathf.Clamp01(leg.stepProgress + Time.deltaTime / stepDuration);
            leg.stepHorizontalPosition += requiredVelocity * Time.deltaTime;
            leg.stepElevation += requiredElevationVelocity * Time.deltaTime;

            if (leg.stepProgress >= 1f)
            {
                leg.stepHorizontalPosition = liveLandingHorizontal;
                leg.stepElevation = liveLandingElevation;
            }

            float height = stepHeightCurve != null ? stepHeightCurve.Evaluate(leg.stepProgress) * stepHeight : Mathf.Sin(leg.stepProgress * Mathf.PI) * stepHeight;
            Vector3 position = BuildPositionFromHorizontalAndElevation(leg.stepHorizontalPosition, leg.stepElevation) - GetGravityDirection() * height;

            leg.target.position = position;

            if (leg.stepProgress >= 1f)
            {
                leg.target.position = liveLanding;
                leg.plantedPosition = liveLanding;
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

        float supportError = GetPlanarDistance(supportCenter, desiredSupportCenter);
        bool anyLegPastMaxReach = IsAnyLegPastMaxReach();
        bool anyLegPastMaxElevation = IsAnyLegPastMaxElevation();
        bool anyLegPastMaxTwist = IsAnyLegPastMaxTwist();
        bool isMoving = IsMovingOnGroundPlane();
        bool isTurning = IsTurning();
        float effectiveSupportError = Mathf.Max(maxSupportCenterError, stepLength);
        bool supportNeedsStep = (isMoving || allowIdleBalanceSteps) && supportError >= effectiveSupportError;
        bool rotationNeedsStep = isTurning && anyLegPastMaxTwist;

        if (!supportNeedsStep && !rotationNeedsStep && !anyLegPastMaxReach && !anyLegPastMaxElevation)
        {
            return;
        }

        int groupToStep = supportNeedsStep ? FindBestSupportGroup() : rotationNeedsStep ? FindMostTwistedGroup() : anyLegPastMaxReach ? FindMostOverreachedGroup() : FindMostElevatedGroup();
        if (!HasUsableLegInGroup(groupToStep))
        {
            return;
        }

        bool startedAny = StartStepGroup(groupToStep);

        if (startedAny)
        {
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
        leg.stepHorizontalPosition = GetHorizontalPosition(leg.stepStartPosition);
        leg.stepElevation = GetElevation(leg.stepStartPosition);
        leg.stepProgress = 0f;
        leg.isStepping = true;
    }

    private Vector3 GetDesiredFootPosition(ManagedLeg leg)
    {
        Vector3 home = GetPredictedHomePosition(leg);
        Vector3 predictedOffset = GetPlanarVelocityDirection() * stepLength * Mathf.Max(1f, velocityPredictionWeight);
        Vector3 desired = home + predictedOffset;

        if (TryProjectToGround(desired, out Vector3 groundedPosition))
        {
            desired = groundedPosition;
        }

        return desired;
    }

    private Vector3 GetPredictedHomePosition(ManagedLeg leg)
    {
        Vector3 home = GetHomePosition(leg);
        if (!IsTurning())
        {
            return home;
        }

        Vector3 pivot = Center.position;
        Quaternion predictedRotation = Quaternion.AngleAxis(currentTurnDegreesPerSecond * angularPredictionTime, Body.up);
        return pivot + predictedRotation * (home - pivot);
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

    private float GetElevationError(ManagedLeg leg, Vector3 desiredPosition)
    {
        return Mathf.Abs(GetElevation(leg.plantedPosition) - GetElevation(desiredPosition));
    }

    private bool IsPastMaxElevation(ManagedLeg leg, Vector3 desiredPosition)
    {
        return maxElevationError > 0f && GetElevationError(leg, desiredPosition) > maxElevationError;
    }

    private float GetTwistAngle(ManagedLeg leg)
    {
        Vector3 axis = Body.up;
        Vector3 pivot = Center.position;
        Vector3 plantedOffset = Vector3.ProjectOnPlane(leg.plantedPosition - pivot, axis);
        Vector3 homeOffset = Vector3.ProjectOnPlane(GetHomePosition(leg) - pivot, axis);

        if (plantedOffset.sqrMagnitude <= 0.000001f || homeOffset.sqrMagnitude <= 0.000001f)
        {
            return 0f;
        }

        return Mathf.Abs(Vector3.SignedAngle(plantedOffset, homeOffset, axis));
    }

    private bool IsPastMaxTwist(ManagedLeg leg)
    {
        return maxFootTwistAngle > 0f && GetTwistAngle(leg) > maxFootTwistAngle;
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

    private bool IsAnyLegPastMaxElevation()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            if (IsPastMaxElevation(leg, GetDesiredFootPosition(leg)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAnyLegPastMaxTwist()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            if (IsPastMaxTwist(leg))
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

    private int FindMostElevatedGroup()
    {
        bool found = false;
        int bestGroup = activeStepGroup;
        float bestElevationError = 0f;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            float elevationError = GetElevationError(leg, GetDesiredFootPosition(leg));
            if (!found || elevationError > bestElevationError)
            {
                bestElevationError = elevationError;
                bestGroup = leg.stepGroup;
                found = true;
            }
        }

        return found ? bestGroup : activeStepGroup;
    }

    private int FindMostTwistedGroup()
    {
        bool found = false;
        int bestGroup = activeStepGroup;
        float bestTwist = 0f;

        for (int i = 0; i < legs.Count; i++)
        {
            ManagedLeg leg = legs[i];
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            float twist = GetTwistAngle(leg);
            if (!found || twist > bestTwist)
            {
                bestTwist = twist;
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

    private bool IsTurning()
    {
        return Mathf.Abs(currentTurnDegreesPerSecond) >= minimumTurnSpeedForPrediction;
    }

    private float GetPlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 up = -GetGravityDirection();
        return Vector3.Distance(Vector3.ProjectOnPlane(a, up), Vector3.ProjectOnPlane(b, up));
    }

    private Vector3 GetHorizontalPosition(Vector3 position)
    {
        return Vector3.ProjectOnPlane(position, -GetGravityDirection());
    }

    private Vector3 BuildPositionFromHorizontalAndElevation(Vector3 horizontalPosition, float elevation)
    {
        Vector3 up = -GetGravityDirection();
        return horizontalPosition + up * elevation;
    }

    private float GetElevation(Vector3 position)
    {
        return Vector3.Dot(position, -GetGravityDirection());
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
