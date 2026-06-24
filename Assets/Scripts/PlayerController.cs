using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")] [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;

    [Header("Surface Movement")]
    [SerializeField] private LayerMask surfaceMask = ~0;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float surfaceOffset = 0.5f;
    [SerializeField] private float surfaceSnapSpeed = 8f;
    [SerializeField] private float initialSurfaceProbeDistance = 2f;
    [SerializeField] private float localSearchRadius = 0.75f;
    [SerializeField] private float maxSurfaceAngleChange = 140f;
    [SerializeField] private float rotationSpeed = 14f;

    [Header("Hook Probe")]
    [SerializeField] private float hookForwardDistance = 1.1f;
    [SerializeField] private float hookNormalClearance = 0.75f;
    [SerializeField] private float hookReturnDistance = 2.25f;
    [SerializeField] private float hookSideOffset = 0.45f;
    [SerializeField] private float transitionSurfaceBias = 2f;
    [SerializeField] private bool drawHook = true;
    [SerializeField] private Color hookRayColor = Color.cyan;
    [SerializeField] private Color hookHitColor = Color.green;
    [SerializeField] private Color hookMissColor = Color.red;
    [SerializeField] private bool drawSurfacePlanes = true;
    [SerializeField] private float surfacePlaneSize = 0.45f;
    [SerializeField] private Color surfacePlaneColor = new Color(0f, 1f, 0.7f, 0.35f);
    [SerializeField] private Color currentSurfacePlaneColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Visual Surface Blending")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float visualAlignDegreesPerSecond = 540f;
    [SerializeField] private float visualProbeRadius = 0.65f;
    [SerializeField] private float visualProbeForwardOffset = 0.35f;
    [SerializeField] private float visualProbeDistance = 1.4f;
    [SerializeField] private bool drawVisualProbes = true;
    [SerializeField] private Color visualProbeColor = Color.yellow;

    [Header("Look")] [SerializeField] private Transform cameraAnchor;
    [SerializeField] private float mouseLookSensitivity = 0.1f;
    [SerializeField] private float stickLookSensitivity = 180f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private Rigidbody rb;
    private Collider[] ownColliders;
    private float pitch;
    private SurfaceHit currentSurface;
    private Vector3 surfaceVelocity;
    private bool hasSurface;
    private float yawInput;
    private Vector3 visualSurfaceNormal;
    private bool hasVisualSurfaceNormal;
    private Quaternion visualBaseLocalRotation;
    private Quaternion visualBaseWorldRotation;
    private bool hasVisualBaseRotation;

    private struct SurfaceHit
    {
        public Vector3 Position;
        public Vector3 Normal;
    }

    private struct HookRay
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float Distance;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ownColliders = GetComponentsInChildren<Collider>();

        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        CacheVisualBaseRotation();
    }

    private void OnEnable()
    {
        moveAction?.action?.Enable();
        lookAction?.action?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.action?.Disable();
        lookAction?.action?.Disable();
    }

    private void Update()
    {
        RotateCameraAnchor();
    }

    private void FixedUpdate()
    {
        if (!hasSurface && !FindInitialSurface(out currentSurface))
        {
            surfaceVelocity = Vector3.zero;
            return;
        }

        hasSurface = true;

        Vector3 desiredDirection = ReadDesiredMoveDirection();
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            surfaceVelocity = Vector3.zero;
            AlignToSurface(currentSurface.Normal);
            AlignVisualToSurroundingSurfaces();
            SnapToCurrentSurface();
            return;
        }

        Vector3 surfaceDirection = Vector3.ProjectOnPlane(desiredDirection, currentSurface.Normal);
        if (surfaceDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        surfaceDirection.Normalize();

        if (TryFindNextSurface(surfaceDirection, out SurfaceHit nextSurface))
        {
            surfaceVelocity = surfaceDirection * moveSpeed;
            currentSurface = MoveAlongSurface(nextSurface, surfaceVelocity);
            AlignToSurface(nextSurface.Normal);
            AlignVisualToSurroundingSurfaces();
        }
        else if (TryLocalSurfaceSearch(out SurfaceHit nearbySurface))
        {
            currentSurface = nearbySurface;
            surfaceVelocity = Vector3.zero;
            SnapToCurrentSurface();
            AlignToSurface(currentSurface.Normal);
            AlignVisualToSurroundingSurfaces();
        }
        else
        {
            surfaceVelocity = Vector3.zero;
        }
    }

    private void RotateCameraAnchor()
    {
        if (lookAction == null || lookAction.action == null)
        {
            return;
        }

        Vector2 lookInput = lookAction.action.ReadValue<Vector2>();
        if (lookInput == Vector2.zero)
        {
            return;
        }

        bool pointerInput = lookAction.action.activeControl?.device is Pointer;
        float sensitivity = pointerInput ? mouseLookSensitivity : stickLookSensitivity * Time.deltaTime;

        yawInput += lookInput.x * sensitivity;

        if (cameraAnchor != null)
        {
            pitch = Mathf.Clamp(pitch - lookInput.y * sensitivity, minPitch, maxPitch);
            cameraAnchor.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private Vector3 ReadDesiredMoveDirection()
    {
        if (moveAction == null || moveAction.action == null)
        {
            return Vector3.zero;
        }

        Vector2 moveInput = moveAction.action.ReadValue<Vector2>();
        if (moveInput.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 right = Vector3.ProjectOnPlane(transform.right, currentSurface.Normal).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, currentSurface.Normal).normalized;

        return (right * moveInput.x + forward * moveInput.y).normalized;
    }

    private bool FindInitialSurface(out SurfaceHit surface)
    {
        Vector3 origin = rb.position + transform.up * surfaceOffset;

        if (TryRaycast(origin, -transform.up, initialSurfaceProbeDistance + surfaceOffset, out RaycastHit hit) ||
            TryRaycast(origin, transform.up, initialSurfaceProbeDistance, out hit))
        {
            surface = CreateSurfaceHit(hit);
            return true;
        }

        if (TryLocalSurfaceSearch(out surface))
        {
            return true;
        }

        surface = default;
        return false;
    }

    private bool TryFindNextSurface(Vector3 forward, out SurfaceHit surface)
    {
        float bestScore = float.PositiveInfinity;
        RaycastHit bestHit = default;
        bool found = false;
        HookRay[] hookRays = BuildHookRays(forward);

        for (int i = 0; i < hookRays.Length; i++)
        {
            HookRay ray = hookRays[i];
            CastHookRay(ray.Origin, ray.Direction, ray.Distance, forward, ref found, ref bestHit, ref bestScore);
        }

        if (found)
        {
            surface = CreateSurfaceHit(bestHit);
            return true;
        }

        surface = default;
        return false;
    }

    private void CastHookRay(Vector3 origin, Vector3 direction, float distance, Vector3 forward, ref bool found, ref RaycastHit bestHit, ref float bestScore)
    {
        if (!TryRaycast(origin, direction, distance, out RaycastHit hit) || !IsValidSurface(hit))
        {
            return;
        }

        Vector3 toHit = hit.point - currentSurface.Position;
        float forwardScore = Mathf.Max(0f, Vector3.Dot(toHit, forward));
        float distanceScore = toHit.sqrMagnitude;
        float normalScore = Vector3.Angle(currentSurface.Normal, hit.normal) * 0.01f;
        float transitionScore = Vector3.Angle(currentSurface.Normal, hit.normal) > 20f ? transitionSurfaceBias : 0f;
        float score = distanceScore - forwardScore + normalScore - transitionScore;

        if (score < bestScore)
        {
            bestScore = score;
            bestHit = hit;
            found = true;
        }
    }

    private bool TryLocalSurfaceSearch(out SurfaceHit surface)
    {
        Vector3 origin = rb.position;
        Vector3[] directions =
        {
            -transform.up,
            transform.up,
            transform.forward,
            -transform.forward,
            transform.right,
            -transform.right
        };

        for (int i = 0; i < directions.Length; i++)
        {
            if (TryRaycast(origin, directions[i], localSearchRadius + surfaceOffset, out RaycastHit hit) && IsValidSurface(hit))
            {
                surface = CreateSurfaceHit(hit);
                return true;
            }
        }

        surface = default;
        return false;
    }

    private bool IsValidSurface(RaycastHit hit)
    {
        if (!hasSurface)
        {
            return true;
        }

        return Vector3.Angle(currentSurface.Normal, hit.normal) <= maxSurfaceAngleChange;
    }

    private bool TryRaycast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, surfaceMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsOwnCollider(hits[i].collider))
            {
                hit = hits[i];
                return true;
            }
        }

        hit = default;
        return false;
    }

    private bool IsOwnCollider(Collider hitCollider)
    {
        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (ownColliders[i] == hitCollider)
            {
                return true;
            }
        }

        return false;
    }

    private SurfaceHit CreateSurfaceHit(RaycastHit hit)
    {
        return new SurfaceHit
        {
            Position = hit.point,
            Normal = hit.normal.normalized
        };
    }

    private SurfaceHit MoveAlongSurface(SurfaceHit probeSurface, Vector3 velocity)
    {
        Vector3 tangentialVelocity = Vector3.ProjectOnPlane(velocity, probeSurface.Normal);
        if (tangentialVelocity.sqrMagnitude < 0.0001f)
        {
            Vector3 transitionDirection = Vector3.ProjectOnPlane(currentSurface.Normal, probeSurface.Normal);
            if (transitionDirection.sqrMagnitude > 0.0001f)
            {
                tangentialVelocity = transitionDirection.normalized * velocity.magnitude;
            }
        }

        Vector3 movementPosition = rb.position + tangentialVelocity * Time.fixedDeltaTime;
        Vector3 targetPosition = probeSurface.Position + probeSurface.Normal * surfaceOffset;

        float normalError = Vector3.Dot(targetPosition - movementPosition, probeSurface.Normal);
        float maxCorrection = surfaceSnapSpeed * Time.fixedDeltaTime;
        float correction = Mathf.MoveTowards(0f, normalError, maxCorrection);
        Vector3 nextPosition = movementPosition + probeSurface.Normal * correction;

        rb.MovePosition(nextPosition);

        return new SurfaceHit
        {
            Position = nextPosition - probeSurface.Normal * surfaceOffset,
            Normal = probeSurface.Normal
        };
    }

    private void SnapToCurrentSurface()
    {
        Vector3 targetPosition = currentSurface.Position + currentSurface.Normal * surfaceOffset;
        Vector3 nextPosition = Vector3.MoveTowards(rb.position, targetPosition, surfaceSnapSpeed * Time.fixedDeltaTime);
        rb.MovePosition(nextPosition);

        currentSurface.Position = nextPosition - currentSurface.Normal * surfaceOffset;
    }

    private void AlignToSurface(Vector3 surfaceNormal)
    {
        Quaternion upRotation = Quaternion.FromToRotation(transform.up, surfaceNormal) * rb.rotation;
        Quaternion yawRotation = Quaternion.AngleAxis(yawInput, surfaceNormal);
        Quaternion targetRotation = yawRotation * upRotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        yawInput = 0f;
    }

    private void AlignVisualToSurroundingSurfaces()
    {
        if (visualRoot == null)
        {
            return;
        }

        CacheVisualBaseRotation();

        Vector3 blendedNormal = SampleSurroundingSurfaceNormal();
        if (!hasVisualSurfaceNormal)
        {
            visualSurfaceNormal = blendedNormal;
            hasVisualSurfaceNormal = true;
        }
        else
        {
            float maxRadiansDelta = visualAlignDegreesPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime;
            visualSurfaceNormal = Vector3.RotateTowards(visualSurfaceNormal, blendedNormal, maxRadiansDelta, 0f);
        }

        if (visualRoot.parent != null)
        {
            Vector3 localNormal = visualRoot.parent.InverseTransformDirection(visualSurfaceNormal);
            Vector3 baseUp = visualBaseLocalRotation * Vector3.up;
            visualRoot.localRotation = Quaternion.FromToRotation(baseUp, localNormal) * visualBaseLocalRotation;
        }
        else
        {
            Vector3 baseUp = visualBaseWorldRotation * Vector3.up;
            visualRoot.rotation = Quaternion.FromToRotation(baseUp, visualSurfaceNormal) * visualBaseWorldRotation;
        }
    }

    private void CacheVisualBaseRotation()
    {
        if (hasVisualBaseRotation || visualRoot == null)
        {
            return;
        }

        visualBaseLocalRotation = visualRoot.localRotation;
        visualBaseWorldRotation = visualRoot.rotation;
        hasVisualBaseRotation = true;
    }

    private Vector3 SampleSurroundingSurfaceNormal()
    {
        Vector3 normal = currentSurface.Normal;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(transform.up, normal);
        }

        forward.Normalize();
        Vector3 side = Vector3.Cross(normal, forward).normalized;
        Vector3 origin = rb.position + forward * visualProbeForwardOffset;
        Vector3 normalSum = normal;

        CastVisualProbe(origin, -normal, ref normalSum);
        CastVisualProbe(origin + side * visualProbeRadius, -normal, ref normalSum);
        CastVisualProbe(origin - side * visualProbeRadius, -normal, ref normalSum);
        CastVisualProbe(origin + forward * visualProbeRadius, -normal, ref normalSum);
        CastVisualProbe(origin - forward * visualProbeRadius, -normal, ref normalSum);
        CastVisualProbe(origin + side * visualProbeRadius, (-normal - side).normalized, ref normalSum);
        CastVisualProbe(origin - side * visualProbeRadius, (-normal + side).normalized, ref normalSum);
        CastVisualProbe(origin + forward * visualProbeRadius, (-normal + forward).normalized, ref normalSum);
        CastVisualProbe(origin + forward * visualProbeRadius, (-normal - forward).normalized, ref normalSum);

        return normalSum.normalized;
    }

    private void CastVisualProbe(Vector3 origin, Vector3 direction, ref Vector3 normalSum)
    {
        if (TryRaycast(origin, direction, visualProbeDistance, out RaycastHit hit) && IsValidSurface(hit))
        {
            normalSum += hit.normal;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !hasSurface)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, currentSurface.Normal);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(transform.up, currentSurface.Normal);
        }

        if (drawSurfacePlanes)
        {
            DrawSurfacePlane(currentSurface.Position, currentSurface.Normal, currentSurfacePlaneColor);
        }

        if (drawHook || drawSurfacePlanes)
        {
            DrawHookPattern(forward.normalized);
        }

        if (drawVisualProbes || drawSurfacePlanes)
        {
            DrawVisualProbes(forward.normalized);
        }
    }

    private void DrawHookPattern(Vector3 forward)
    {
        HookRay[] hookRays = BuildHookRays(forward);
        for (int i = 0; i < hookRays.Length; i++)
        {
            HookRay ray = hookRays[i];
            DrawHookRay(ray.Origin, ray.Direction, ray.Distance);
        }
    }

    private HookRay[] BuildHookRays(Vector3 forward)
    {
        Vector3 normal = currentSurface.Normal;
        Vector3 side = Vector3.Cross(normal, forward).normalized;

        HookRay[] rays = new HookRay[9];
        int index = 0;
        AddHookShape(Vector3.zero, forward, normal, ref rays, ref index);
        AddHookShape(side * hookSideOffset, forward, normal, ref rays, ref index);
        AddHookShape(-side * hookSideOffset, forward, normal, ref rays, ref index);
        return rays;
    }

    private void AddHookShape(Vector3 lateralOffset, Vector3 forward, Vector3 normal, ref HookRay[] rays, ref int index)
    {
        Vector3 start = currentSurface.Position + normal * surfaceOffset + lateralOffset;
        Vector3 liftDirection = (forward + normal).normalized;
        Vector3 liftedPoint = start + liftDirection * hookNormalClearance;
        Vector3 forwardPoint = liftedPoint + forward * hookForwardDistance;
        Vector3 returnDirection = (-normal - forward).normalized;

        rays[index++] = new HookRay
        {
            Origin = start,
            Direction = liftDirection,
            Distance = hookNormalClearance
        };

        rays[index++] = new HookRay
        {
            Origin = liftedPoint,
            Direction = forward,
            Distance = hookForwardDistance
        };

        rays[index++] = new HookRay
        {
            Origin = forwardPoint,
            Direction = returnDirection,
            Distance = hookReturnDistance
        };
    }

    private void DrawHookRay(Vector3 origin, Vector3 direction, float distance)
    {
        if (drawHook)
        {
            Gizmos.color = hookRayColor;
            Gizmos.DrawRay(origin, direction * distance);
        }

        if (TryRaycast(origin, direction, distance, out RaycastHit hit) && IsValidSurface(hit))
        {
            if (drawHook)
            {
                Gizmos.color = hookHitColor;
                Gizmos.DrawSphere(hit.point, 0.06f);
                Gizmos.DrawRay(hit.point, hit.normal * 0.25f);
            }

            if (drawSurfacePlanes)
            {
                DrawSurfacePlane(hit.point, hit.normal, surfacePlaneColor);
            }
        }
        else if (drawHook)
        {
            Gizmos.color = hookMissColor;
            Gizmos.DrawSphere(origin + direction.normalized * distance, 0.035f);
        }
    }

    private void DrawVisualProbes(Vector3 forward)
    {
        Vector3 normal = currentSurface.Normal;
        Vector3 side = Vector3.Cross(normal, forward).normalized;
        Vector3 origin = rb.position + forward * visualProbeForwardOffset;

        Gizmos.color = visualProbeColor;
        DrawVisualProbe(origin, -normal);
        DrawVisualProbe(origin + side * visualProbeRadius, -normal);
        DrawVisualProbe(origin - side * visualProbeRadius, -normal);
        DrawVisualProbe(origin + forward * visualProbeRadius, -normal);
        DrawVisualProbe(origin - forward * visualProbeRadius, -normal);
        DrawVisualProbe(origin + side * visualProbeRadius, (-normal - side).normalized);
        DrawVisualProbe(origin - side * visualProbeRadius, (-normal + side).normalized);
        DrawVisualProbe(origin + forward * visualProbeRadius, (-normal + forward).normalized);
        DrawVisualProbe(origin + forward * visualProbeRadius, (-normal - forward).normalized);
    }

    private void DrawVisualProbe(Vector3 origin, Vector3 direction)
    {
        if (drawVisualProbes)
        {
            Gizmos.color = visualProbeColor;
            Gizmos.DrawRay(origin, direction * visualProbeDistance);
        }

        if (TryRaycast(origin, direction, visualProbeDistance, out RaycastHit hit) && IsValidSurface(hit) && drawSurfacePlanes)
        {
            DrawSurfacePlane(hit.point, hit.normal, surfacePlaneColor);
        }
    }

    private void DrawSurfacePlane(Vector3 position, Vector3 normal, Color color)
    {
        Vector3 tangent = Vector3.Cross(normal, transform.forward);
        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = Vector3.Cross(normal, transform.right);
        }

        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            position + normal * 0.01f,
            Quaternion.LookRotation(bitangent, normal),
            new Vector3(surfacePlaneSize, 0.01f, surfacePlaneSize));

        Gizmos.color = color;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a + 0.35f));
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = previousMatrix;

        Gizmos.color = new Color(color.r, color.g, color.b, 1f);
        Gizmos.DrawRay(position, normal * (surfacePlaneSize * 0.5f));
    }
}
