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
        [HideInInspector] public float stepPredictionScale = 1f;
        [HideInInspector] public float stepProgress;
        [HideInInspector] public bool isStepping;
    }

    [Header("Input")] [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference rotateAction;

    [Header("Body")] [SerializeField] private Transform centerOfMass;
    [SerializeField] private Transform movementReference;
    [SerializeField] private bool autoCaptureHomeOffsets = true;
    [SerializeField] private bool autoCreateMissingTargets = true;
    [SerializeField] private bool keepPlantedTargetsInWorld = true;

    [Header("Body Height")] [SerializeField]
    private bool maintainBodyHeight = true;

    [SerializeField] private bool autoCaptureBodyHeight = true;
    [SerializeField] private float bodyHeight = 1f;
    [SerializeField] private float bodyHeightSmoothing = 12f;
    [SerializeField] private float maxBodyHeightCorrectionSpeed = 4f;

    [Header("Body Orientation")] [SerializeField]
    private bool orientBodyToLegEnds = true;

    [SerializeField] private float bodyOrientationSmoothing = 12f;
    [SerializeField] private float minimumBodyOrientationFootSpread = 0.01f;

    [Header("Movement")] [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float moveAcceleration = 30f;
    [SerializeField] private float moveDeceleration = 40f;
    [SerializeField] private float idleSupportCenterPull = 8f;
    [SerializeField] private float idleSupportCenterDeadZone = 0.05f;
    [SerializeField] private float maxIdleSupportCenterCorrection = 0.25f;
    [SerializeField] private float maxIdleSettleSpeed = 2f;
    [SerializeField] private float turnSpeed = 180f;

    [Header("Legs")] [SerializeField] private IKSolverSettings solverSettingsProfile;
    [SerializeField] private int copySettingsFromLegIndex;
    [SerializeField] private List<ManagedLeg> legs = new List<ManagedLeg>();

    [Header("Step")] [SerializeField] private float stepHeight = 0.35f;
    [SerializeField] private float maxReach = 1.25f;
    [SerializeField] private float stepLength = 0.6f;
    [SerializeField] private float maxElevationError = 0.15f;
    [SerializeField] private float stepDuration = 0.18f;

    [SerializeField] private AnimationCurve stepHeightCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0f));

    [Header("Gait")] [SerializeField] private bool requireGroupComplete = true;

    [Header("Balance")] [SerializeField] private bool predictSupportCenter;
    [SerializeField] private float centerOfMassLeadTime = 0.15f;
    [SerializeField] private float maxSupportCenterError = 0.35f;
    [SerializeField] private bool allowIdleBalanceSteps;

    [Header("Grounding")] [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundProbeHeight = 1.5f;
    [SerializeField] private float groundProbeDistance = 3f;
    [SerializeField] private float groundOffset = 0.02f;
    [SerializeField] private Vector3 gravityDirection = Vector3.down;

    [Header("Prediction")] [SerializeField]
    private float footPlacementLeadTime = 0.1f;

    [SerializeField] private float velocitySmoothing = 12f;

    [SerializeField] private float velocityPredictionWeight = 1f;
    [SerializeField] private float minimumMoveSpeedForPrediction = 0.05f;
    [SerializeField] private float angularPredictionTime = 0.15f;
    [SerializeField] private float maxFootTwistAngle = 25f;
    [SerializeField] private float minimumTurnSpeedForPrediction = 5f;

    [Header("Debug")] [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color plantedColor = Color.green;
    [SerializeField] private Color desiredColor = Color.cyan;
    [SerializeField] private Color steppingColor = Color.yellow;
    [SerializeField] private Color centerOfMassColor = Color.magenta;
    [SerializeField] private Color supportCenterColor = Color.white;
    [SerializeField] private float gizmoSize = 0.06f;

    private Vector3 _previousCenterOfMass;
    private Vector3 _bodyVelocity;
    private Vector3 _lastMoveDirection;
    private float _idleSupportCenterCorrectionRemaining;
    private float _gaitDistanceAccumulator;
    private float _currentTurnDegreesPerSecond;
    private bool _hasMoveInputThisFrame;
    private bool _startupHalfStepPending;
    private bool _wasLocomotionGaitActive;
    private int _activeStepGroup;
    private bool _initialized;
    private Rigidbody _bodyRigidbody;
    private Vector3? _pendingBodyPosition;

    public Vector3 CurrentCenterOfMass { get; private set; }

    public Vector3 SmoothedVelocity { get; private set; }

    public Vector3 SupportCenter { get; private set; }

    public Vector3 DesiredSupportCenter { get; private set; }

    public IReadOnlyList<ManagedLeg> Legs => legs;

    private Transform Body => movementReference != null ? movementReference : transform;

    private Transform Center => centerOfMass != null ? centerOfMass : transform;

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
        _bodyRigidbody = Body.GetComponent<Rigidbody>();
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
        footPlacementLeadTime = Mathf.Max(0f, footPlacementLeadTime);
        velocitySmoothing = Mathf.Max(0f, velocitySmoothing);
        velocityPredictionWeight = Mathf.Max(0f, velocityPredictionWeight);
        angularPredictionTime = Mathf.Max(0f, angularPredictionTime);
        maxFootTwistAngle = Mathf.Max(0f, maxFootTwistAngle);
        minimumTurnSpeedForPrediction = Mathf.Max(0f, minimumTurnSpeedForPrediction);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        moveAcceleration = Mathf.Max(0f, moveAcceleration);
        moveDeceleration = Mathf.Max(0f, moveDeceleration);
        idleSupportCenterPull = Mathf.Max(0f, idleSupportCenterPull);
        idleSupportCenterDeadZone = Mathf.Max(0f, idleSupportCenterDeadZone);
        maxIdleSupportCenterCorrection = Mathf.Max(0f, maxIdleSupportCenterCorrection);
        maxIdleSettleSpeed = Mathf.Max(0f, maxIdleSettleSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        bodyHeight = Mathf.Max(0f, bodyHeight);
        bodyHeightSmoothing = Mathf.Max(0f, bodyHeightSmoothing);
        maxBodyHeightCorrectionSpeed = Mathf.Max(0f, maxBodyHeightCorrectionSpeed);
        bodyOrientationSmoothing = Mathf.Max(0f, bodyOrientationSmoothing);
        minimumBodyOrientationFootSpread = Mathf.Max(0.0001f, minimumBodyOrientationFootSpread);
        copySettingsFromLegIndex = Mathf.Max(0, copySettingsFromLegIndex);
    }

    private void FixedUpdate()
    {
        _pendingBodyPosition = _bodyRigidbody != null ? _bodyRigidbody.position : Body.position;
        RotateBody();
        MoveBody();
        MaintainBodyHeight();
        _pendingBodyPosition = null;
    }

    private void RotateBody()
    {
        float rotateInput = ReadRotateInput();
        _currentTurnDegreesPerSecond = rotateInput * turnSpeed;
        if (Mathf.Abs(rotateInput) < 0.0001f)
        {
            return;
        }

        Transform body = Body;
        Quaternion deltaRotation = Quaternion.AngleAxis(_currentTurnDegreesPerSecond * Time.fixedDeltaTime, body.up);
        Quaternion rotation = deltaRotation * body.rotation;

        if (_bodyRigidbody != null)
        {
            _bodyRigidbody.MoveRotation(rotation);
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
        if (action.activeControl is Vector2Control || action.expectedControlType == "Vector2" ||
            action.expectedControlType == "Stick")
        {
            return Mathf.Clamp(action.ReadValue<Vector2>().x, -1f, 1f);
        }

        return Mathf.Clamp(action.ReadValue<float>(), -1f, 1f);
    }

    private void LateUpdate()
    {
        if (!_initialized)
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
        float deltaTime = Time.fixedDeltaTime;
        Vector3 desiredVelocity = GetDesiredBodyVelocity(out bool hasMoveInput);
        _hasMoveInputThisFrame = hasMoveInput;
        float acceleration = hasMoveInput ? moveAcceleration : moveDeceleration;
        _bodyVelocity = Vector3.MoveTowards(_bodyVelocity, desiredVelocity, acceleration * deltaTime);
        _bodyVelocity = Vector3.ProjectOnPlane(_bodyVelocity, GetMovementPlaneNormal());

        if (hasMoveInput)
        {
            _idleSupportCenterCorrectionRemaining = maxIdleSupportCenterCorrection;
        }
        else if (_bodyVelocity.sqrMagnitude > 0.000001f && _idleSupportCenterCorrectionRemaining <= 0f)
        {
            _bodyVelocity = Vector3.MoveTowards(_bodyVelocity, Vector3.zero, moveDeceleration * deltaTime);
        }

        if (_bodyVelocity.sqrMagnitude < 0.000001f)
        {
            _bodyVelocity = Vector3.zero;
            return;
        }

        Vector3 displacement = _bodyVelocity * deltaTime;
        if (!hasMoveInput)
        {
            float correctionDistance = displacement.magnitude;
            if (correctionDistance > _idleSupportCenterCorrectionRemaining)
            {
                displacement = displacement.normalized * _idleSupportCenterCorrectionRemaining;
                _bodyVelocity = deltaTime > 0f ? displacement / deltaTime : Vector3.zero;
                _idleSupportCenterCorrectionRemaining = 0f;
            }
            else
            {
                _idleSupportCenterCorrectionRemaining -= correctionDistance;
            }
        }

        MoveBodyBy(displacement);
    }

    private Vector3 GetDesiredBodyVelocity(out bool hasMoveInput)
    {
        hasMoveInput = false;

        if (moveAction == null || moveAction.action == null)
        {
            return GetIdleSettleVelocity();
        }

        Vector2 moveInput = moveAction.action.ReadValue<Vector2>();
        if (moveInput.sqrMagnitude < 0.0001f)
        {
            return GetIdleSettleVelocity();
        }

        Vector3 right = Body.right;
        Vector3 forward = Body.forward;
        Vector3 moveDirection = right * moveInput.x + forward * moveInput.y;
        moveDirection = Vector3.ProjectOnPlane(moveDirection, GetMovementPlaneNormal());
        if (moveDirection.sqrMagnitude < 0.0001f)
        {
            return GetIdleSettleVelocity();
        }

        hasMoveInput = true;
        _lastMoveDirection = moveDirection.normalized;
        return _lastMoveDirection * (Mathf.Clamp01(moveInput.magnitude) * moveSpeed);
    }

    private Vector3 GetIdleSettleVelocity()
    {
        if (_lastMoveDirection.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 moveDirection = Vector3.ProjectOnPlane(_lastMoveDirection, GetMovementPlaneNormal());
        if (moveDirection.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        moveDirection.Normalize();

        Vector3 toSupportCenter = Vector3.ProjectOnPlane(SupportCenter - CurrentCenterOfMass, GetMovementPlaneNormal());
        float distance = Vector3.Dot(toSupportCenter, moveDirection);
        float absDistance = Mathf.Abs(distance);
        if (absDistance <= idleSupportCenterDeadZone)
        {
            return Vector3.zero;
        }

        float speed = Mathf.Min(absDistance * idleSupportCenterPull, maxIdleSettleSpeed);
        if (speed <= 0f)
        {
            return Vector3.zero;
        }

        return moveDirection * (Mathf.Sign(distance) * speed);
    }

    private Vector3 GetMovementPlaneNormal()
    {
        Vector3 normal = Body.up;
        if (normal.sqrMagnitude <= 0.0001f)
        {
            normal = -GetGravityDirection();
        }

        return normal.normalized;
    }

    private void MaintainBodyHeight()
    {
        if (!maintainBodyHeight)
        {
            return;
        }

        if (autoCaptureBodyHeight && bodyHeight <= 0.0001f)
        {
            CaptureCurrentBodyHeight();
        }

        Vector3 centerPosition = GetCurrentCenterPosition();
        if (!TryGetBodyHeightReference(centerPosition, out Vector3 referencePosition))
        {
            return;
        }

        Vector3 up = -GetGravityDirection();
        float currentHeight = Vector3.Dot(centerPosition - referencePosition, up);
        float error = bodyHeight - currentHeight;
        if (Mathf.Abs(error) <= 0.0001f)
        {
            return;
        }

        float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float blend = bodyHeightSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-bodyHeightSmoothing * deltaTime);
        float correction = error * blend;
        if (maxBodyHeightCorrectionSpeed > 0f)
        {
            correction = Mathf.Clamp(correction, -maxBodyHeightCorrectionSpeed * deltaTime,
                maxBodyHeightCorrectionSpeed * deltaTime);
        }

        MoveBodyBy(up * correction);
    }

    [ContextMenu("Capture Current Body Height")]
    public void CaptureCurrentBodyHeight()
    {
        Vector3 centerPosition = GetCurrentCenterPosition();
        if (!TryGetBodyHeightReference(centerPosition, out Vector3 referencePosition))
        {
            return;
        }

        bodyHeight = Mathf.Max(0f, Vector3.Dot(centerPosition - referencePosition, -GetGravityDirection()));
    }

    private bool TryGetBodyHeightReference(Vector3 centerPosition, out Vector3 referencePosition)
    {
        if (TryGetSupportGroundReference(centerPosition, out referencePosition))
        {
            return true;
        }

        if (TryGetGroundPoint(centerPosition, out referencePosition))
        {
            return true;
        }

        referencePosition = Vector3.zero;
        return false;
    }

    private bool TryGetSupportGroundReference(Vector3 centerPosition, out Vector3 referencePosition)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var leg in legs)
        {
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            sum += leg.plantedPosition;
            count++;
        }

        if (count <= 0)
        {
            foreach (var leg in legs)
            {
                if (!IsLegUsable(leg))
                {
                    continue;
                }

                sum += leg.isStepping ? leg.stepStartPosition : leg.plantedPosition;
                count++;
            }
        }

        if (count <= 0)
        {
            referencePosition = Vector3.zero;
            return false;
        }

        Vector3 up = -GetGravityDirection();
        referencePosition = ProjectPointToPlane(centerPosition, sum / count, up);
        return true;
    }

    private Vector3 GetCurrentCenterPosition()
    {
        if (_bodyRigidbody == null || !_pendingBodyPosition.HasValue)
        {
            return Center.position;
        }

        return Center.position + (_pendingBodyPosition.Value - Body.position);
    }

    private void MoveBodyBy(Vector3 displacement)
    {
        if (displacement.sqrMagnitude <= 0.0000000001f)
        {
            return;
        }

        if (_bodyRigidbody != null)
        {
            Vector3 position = _pendingBodyPosition ?? _bodyRigidbody.position;
            position += displacement;
            _pendingBodyPosition = position;
            _bodyRigidbody.MovePosition(position);
        }
        else
        {
            Body.position += displacement;
        }
    }

    [ContextMenu("Initialize Legs")]
    public void Initialize()
    {
        CurrentCenterOfMass = Center.position;
        _previousCenterOfMass = CurrentCenterOfMass;
        SmoothedVelocity = Vector3.zero;
        SupportCenter = CurrentCenterOfMass;
        DesiredSupportCenter = CurrentCenterOfMass;
        _bodyVelocity = Vector3.zero;
        _lastMoveDirection = Vector3.zero;
        _idleSupportCenterCorrectionRemaining = maxIdleSupportCenterCorrection;
        _gaitDistanceAccumulator = 0f;
        _hasMoveInputThisFrame = false;
        _startupHalfStepPending = false;
        _wasLocomotionGaitActive = false;

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
            leg.stepPredictionScale = 1f;
            leg.stepProgress = 1f;
            leg.isStepping = false;

            if (autoCaptureHomeOffsets && leg.homeAnchor == null)
            {
                leg.localHomeOffset = Body.InverseTransformPoint(startPosition);
            }
        }

        if (autoCaptureBodyHeight)
        {
            CaptureCurrentBodyHeight();
        }

        _activeStepGroup = GetFirstStepGroup();
        _initialized = true;
    }

    [ContextMenu("Apply Solver Settings To Legs")]
    public void ApplySolverSettingsToLegs()
    {
        if (solverSettingsProfile == null)
        {
            return;
        }

        foreach (var leg in legs)
        {
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
        _previousCenterOfMass = CurrentCenterOfMass;
        CurrentCenterOfMass = Center.position;

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 velocity = (CurrentCenterOfMass - _previousCenterOfMass) / deltaTime;
        float blend = velocitySmoothing <= 0f ? 1f : 1f - Mathf.Exp(-velocitySmoothing * deltaTime);
        SmoothedVelocity = Vector3.Lerp(SmoothedVelocity, velocity, blend);
    }

    private void UpdateSupportCenters()
    {
        Vector3 gravity = GetGravityDirection();
        Vector3 up = -gravity;
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var leg in legs)
        {
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            sum += leg.plantedPosition;
            count++;
        }

        SupportCenter = count > 0 ? sum / count : CurrentCenterOfMass;

        Vector3 desiredCenter = CurrentCenterOfMass;
        if (predictSupportCenter)
        {
            desiredCenter += Vector3.ProjectOnPlane(SmoothedVelocity, up) * centerOfMassLeadTime;
        }

        DesiredSupportCenter = ProjectPointToPlane(desiredCenter, SupportCenter, up);
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

        if (_bodyRigidbody != null)
        {
            _bodyRigidbody.MoveRotation(rotation);
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

        foreach (var leg in legs)
        {
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
        Vector3 rightAxis = hasRightAxis
            ? rightSum / rightCount - leftSum / leftCount
            : Vector3.ProjectOnPlane(body.right, -GetGravityDirection());
        Vector3 forwardAxis = hasForwardAxis
            ? frontSum / frontCount - backSum / backCount
            : Vector3.ProjectOnPlane(body.forward, -GetGravityDirection());

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
        foreach (var leg in legs)
        {
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

            Vector3 liveLanding = _hasMoveInputThisFrame
                ? GetDesiredFootPosition(leg, true, leg.stepPredictionScale)
                : leg.stepEndPosition;
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

            float height = stepHeightCurve?.Evaluate(leg.stepProgress) * stepHeight ??
                           Mathf.Sin(leg.stepProgress * Mathf.PI) * stepHeight;
            Vector3 position = BuildPositionFromHorizontalAndElevation(leg.stepHorizontalPosition, leg.stepElevation) -
                               GetGravityDirection() * height;

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
        bool locomotionGaitActive = IsLocomotionGaitActive();
        bool locomotionStepDue = UpdateLocomotionGait(locomotionGaitActive);

        if (requireGroupComplete && AnyLegStepping())
        {
            return;
        }

        if (locomotionGaitActive)
        {
            if (!locomotionStepDue)
            {
                return;
            }

            int scheduledGroup = GetScheduledStepGroup();
            bool startedScheduledGroup = StartStepGroup(scheduledGroup, GetLocomotionStepPredictionScale());
            if (startedScheduledGroup)
            {
                ConsumeLocomotionGaitStep();
                _activeStepGroup = GetNextStepGroup(scheduledGroup);
            }

            return;
        }

        float supportError = GetPlanarDistance(SupportCenter, DesiredSupportCenter);
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

        int groupToStep = supportNeedsStep ? FindBestSupportGroup() :
            rotationNeedsStep ? FindMostTwistedGroup() :
            anyLegPastMaxReach ? FindMostOverreachedGroup() : FindMostElevatedGroup();
        if (!HasUsableLegInGroup(groupToStep))
        {
            return;
        }

        bool startedAny = StartStepGroup(groupToStep);

        if (startedAny)
        {
            _activeStepGroup = GetNextStepGroup(groupToStep);
        }
    }

    private bool IsLocomotionGaitActive()
    {
        return _hasMoveInputThisFrame || IsMovingOnGroundPlane();
    }

    private bool UpdateLocomotionGait(bool locomotionGaitActive)
    {
        if (!locomotionGaitActive)
        {
            _gaitDistanceAccumulator = 0f;
            _startupHalfStepPending = false;
            _wasLocomotionGaitActive = false;
            return false;
        }

        float stepDistance = GetLocomotionGroupStepDistance();
        if (!_wasLocomotionGaitActive)
        {
            _gaitDistanceAccumulator = stepDistance;
            _startupHalfStepPending = true;
            _wasLocomotionGaitActive = true;
            return true;
        }

        _gaitDistanceAccumulator += GetPlanarDistance(_previousCenterOfMass, CurrentCenterOfMass);
        return _gaitDistanceAccumulator >= stepDistance;
    }

    private void ConsumeLocomotionGaitStep()
    {
        _gaitDistanceAccumulator = Mathf.Max(0f, _gaitDistanceAccumulator - GetLocomotionGroupStepDistance());
        _startupHalfStepPending = false;
    }

    private float GetLocomotionGroupStepDistance()
    {
        return Mathf.Max(0.01f, stepLength > 0.0001f ? stepLength : maxSupportCenterError);
    }

    private int GetScheduledStepGroup()
    {
        if (HasUsableLegInGroup(_activeStepGroup))
        {
            return _activeStepGroup;
        }

        int nextGroup = GetNextStepGroup(_activeStepGroup);
        return HasUsableLegInGroup(nextGroup) ? nextGroup : FindBestSupportGroup();
    }

    private float GetLocomotionStepPredictionScale()
    {
        return _startupHalfStepPending ? 0.5f : 1f;
    }

    private bool StartStepGroup(int stepGroup)
    {
        return StartStepGroup(stepGroup, 1f);
    }

    private bool StartStepGroup(int stepGroup, float predictionScale)
    {
        bool startedAny = false;

        foreach (var leg in legs)
        {
            if (!IsLegUsable(leg) || leg.isStepping || leg.stepGroup != stepGroup)
            {
                continue;
            }

            Vector3 desiredPosition = GetDesiredFootPosition(leg, _hasMoveInputThisFrame, predictionScale);
            StartStep(leg, desiredPosition, predictionScale);
            startedAny = true;
        }

        return startedAny;
    }

    private void StartStep(ManagedLeg leg, Vector3 desiredPosition, float predictionScale)
    {
        leg.stepStartPosition = leg.plantedPosition;
        leg.target.position = leg.stepStartPosition;
        leg.stepEndPosition = desiredPosition;
        leg.stepHorizontalPosition = GetHorizontalPosition(leg.stepStartPosition);
        leg.stepElevation = GetElevation(leg.stepStartPosition);
        leg.stepPredictionScale = Mathf.Max(0f, predictionScale);
        leg.stepProgress = 0f;
        leg.isStepping = true;
    }

    private Vector3 GetDesiredFootPosition(ManagedLeg leg)
    {
        return GetDesiredFootPosition(leg, _hasMoveInputThisFrame);
    }

    private Vector3 GetDesiredFootPosition(ManagedLeg leg, bool useVelocityPrediction)
    {
        return GetDesiredFootPosition(leg, useVelocityPrediction, 1f);
    }

    private Vector3 GetDesiredFootPosition(ManagedLeg leg, bool useVelocityPrediction, float predictionScale)
    {
        Vector3 home = GetPredictedHomePosition(leg);
        Vector3 predictedOffset = useVelocityPrediction
            ? GetFootPlacementPredictionOffset(predictionScale)
            : Vector3.zero;
        Vector3 desired = home + predictedOffset;

        if (TryProjectToGround(desired, out Vector3 groundedPosition))
        {
            desired = groundedPosition;
        }

        return desired;
    }

    private Vector3 GetFootPlacementPredictionOffset(float predictionScale)
    {
        if (footPlacementLeadTime <= 0f || velocityPredictionWeight <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 up = -GetGravityDirection();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(SmoothedVelocity, up);
        if (planarVelocity.magnitude < minimumMoveSpeedForPrediction)
        {
            planarVelocity = Vector3.ProjectOnPlane(_bodyVelocity, up);
        }

        if (planarVelocity.magnitude < minimumMoveSpeedForPrediction)
        {
            return Vector3.zero;
        }

        return planarVelocity * (footPlacementLeadTime * velocityPredictionWeight * Mathf.Max(0f, predictionScale));
    }

    private Vector3 GetPredictedHomePosition(ManagedLeg leg)
    {
        Vector3 home = GetHomePosition(leg);
        if (!IsTurning())
        {
            return home;
        }

        Vector3 pivot = Center.position;
        Quaternion predictedRotation =
            Quaternion.AngleAxis(_currentTurnDegreesPerSecond * angularPredictionTime, Body.up);
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

    private bool IsAnyLegPastMaxReach()
    {
        foreach (var leg in legs)
        {
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
        foreach (var leg in legs)
        {
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
        foreach (var leg in legs)
        {
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
        foreach (var leg in legs)
        {
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

        foreach (var leg in legs)
        {
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

        foreach (var leg in legs)
        {
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
        int bestGroup = _activeStepGroup;
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
            float score = GetPlanarDistance(simulatedCenter, DesiredSupportCenter);
            if (!found || score < bestScore)
            {
                bestScore = score;
                bestGroup = group;
                found = true;
            }
        }

        return found ? bestGroup : _activeStepGroup;
    }

    private int FindMostOverreachedGroup()
    {
        bool found = false;
        int bestGroup = _activeStepGroup;
        float bestReachError = 0f;

        foreach (var leg in legs)
        {
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

        return found ? bestGroup : _activeStepGroup;
    }

    private int FindMostElevatedGroup()
    {
        bool found = false;
        int bestGroup = _activeStepGroup;
        float bestElevationError = 0f;

        foreach (var leg in legs)
        {
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

        return found ? bestGroup : _activeStepGroup;
    }

    private int FindMostTwistedGroup()
    {
        bool found = false;
        int bestGroup = _activeStepGroup;
        float bestTwist = 0f;

        foreach (var leg in legs)
        {
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

        return found ? bestGroup : _activeStepGroup;
    }

    private Vector3 GetSimulatedSupportCenterAfterGroupStep(int stepGroup)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var leg in legs)
        {
            if (!IsLegUsable(leg) || leg.isStepping)
            {
                continue;
            }

            sum += leg.stepGroup == stepGroup ? GetDesiredFootPosition(leg) : leg.plantedPosition;
            count++;
        }

        return count > 0 ? sum / count : SupportCenter;
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
        foreach (var leg in legs)
        {
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
        Vector3 planarVelocity = Vector3.ProjectOnPlane(SmoothedVelocity, up);
        return planarVelocity.magnitude >= minimumMoveSpeedForPrediction;
    }

    private bool IsTurning()
    {
        return Mathf.Abs(_currentTurnDegreesPerSecond) >= minimumTurnSpeedForPrediction;
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
        if (TryGetGroundPoint(position, out Vector3 groundPoint))
        {
            groundedPosition = groundPoint - GetGravityDirection() * groundOffset;
            return true;
        }

        groundedPosition = position;
        return false;
    }

    private bool TryGetGroundPoint(Vector3 position, out Vector3 groundPoint)
    {
        Vector3 gravity = GetGravityDirection();
        Vector3 origin = position - gravity * groundProbeHeight;
        float distance = groundProbeHeight + groundProbeDistance;

        if (Physics.Raycast(origin, gravity, out RaycastHit hit, distance, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundPoint = hit.point;
            return true;
        }

        groundPoint = position;
        return false;
    }

    private Vector3 GetPlanarVelocityDirection()
    {
        Vector3 up = -GetGravityDirection();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(SmoothedVelocity, up);
        if (planarVelocity.magnitude >= minimumMoveSpeedForPrediction)
        {
            return planarVelocity.normalized;
        }

        if (_hasMoveInputThisFrame)
        {
            Vector3 moveDirection = Vector3.ProjectOnPlane(_lastMoveDirection, up);
            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                return moveDirection.normalized;
            }
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

    private static bool IsLegUsable(ManagedLeg leg)
    {
        return leg is { enabled: true } && leg.target != null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Vector3 center = Application.isPlaying ? CurrentCenterOfMass : Center.position;
        Gizmos.color = centerOfMassColor;
        Gizmos.DrawWireSphere(center, gizmoSize * 1.5f);

        if (Application.isPlaying)
        {
            Gizmos.color = supportCenterColor;
            Gizmos.DrawSphere(SupportCenter, gizmoSize);
            Gizmos.DrawLine(SupportCenter, DesiredSupportCenter);

            Gizmos.color = desiredColor;
            Gizmos.DrawWireSphere(DesiredSupportCenter, gizmoSize * 1.25f);
        }

        foreach (var leg in legs)
        {
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
