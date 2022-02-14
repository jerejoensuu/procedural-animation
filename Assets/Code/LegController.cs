using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LegController : MonoBehaviour {
    
    private BodyManager body;
    private Vector3 rayHitPoint;
    private Vector3 bodyOrigin;
    public GameObject limbEnd;
    public GameObject steppingArea;
    public GameObject target;
    private Vector3 targetOrigin;
    float maxDistance;
    public bool stepping = false;
    bool joinedQueue;
    public Side side;
    public int leg;
    Vector3 steppingPoint;
    Vector3 velocity;
    Vector3 prevPos;
    public Material defaultColor;
    public Material altColor;

    public enum Side{
        Right = 1,
        Left = -1
    }

    void Start() {
        body = transform.parent.GetComponent<BodyManager>();
        rayHitPoint = target.transform.position;
        bodyOrigin = transform.parent.transform.position;
        Raycast();
        targetOrigin = target.transform.position;
        prevPos = transform.position;
        velocity = Vector3.zero;
        maxDistance = body.maxDistance;
    }

    void Update() {
        CalculateVelocity();
        Raycast();
        if (!stepping) {
            target.transform.position = targetOrigin;
        }

        // When distance between the leg and stepping area grows big enough, attempt to step.
        if (GetDistance() > maxDistance && !stepping) {
            // First join the queue for legs wanting to step...
            if (!joinedQueue) {
                body.JoinQueue(transform.gameObject);
                joinedQueue = true;
            }
            // ...Then step when approprite legs are down. (More info in BodyManager)
            if (body.CanStep(transform.gameObject)) {
                stepping  = true;
                steppingPoint = GetSteppingPoint();
                StartCoroutine(Step());
            }
        }
        AlignLeg();
        ChangeColor();
    }

    /// <summary>
    /// Calculates velocity for the endpoint of the leg.
    /// </summary>
    void CalculateVelocity() {
        velocity = Vector3.Normalize(transform.position - prevPos);
        prevPos = transform.position;
    }

    /// <summary>
    /// Casts a ray to move the stepping area.
    /// </summary>
    void Raycast() {
        RaycastHit hit;
        Vector3 rayStart = steppingArea.transform.position;
        var ray = new Ray(rayStart, Vector3.down);
        Debug.DrawRay(rayStart, (Vector3.down * transform.parent.transform.position.y), Color.red);

        if (Physics.Raycast(ray, out hit, 10f, ~(1 << 9))) {
            if (hit.point != null) {
                rayHitPoint = hit.point;
            }
        }
    }

    /// <summary>
    /// Moves the leg to new position.
    /// </summary>
    IEnumerator Step() {
        Vector3 targetPoint = steppingPoint;
        // Move leg towards new position until it's close enough.
        while(Vector3.Distance(target.transform.position, targetPoint) > 0.6f) {
            target.transform.position = Vector3.MoveTowards(target.transform.position, targetPoint, 140f * Time.deltaTime);
            SineStep(targetPoint); // Create stepping motion using sine waves.
            yield return new WaitForSeconds(Time.deltaTime);
        }
        target.transform.position = targetPoint;
        targetOrigin = target.transform.position;
        stepping = joinedQueue = false;
        body.LeaveList(transform.gameObject); // Leave the list of currently moving legs.
    }

    public void ReturnToRest() { }

    /// <summary>
    /// Positions the secondary/pole target in front of the leg to point it in the correct direction.
    /// </summary>
    void AlignLeg() {
        float angle = Vector3.SignedAngle(Vector3.right,
                        new Vector3(target.transform.position.x, 0, target.transform.position.z) - new Vector3(transform.position.x, 0, transform.position.z),
                        Vector3.up);
        target.transform.eulerAngles = new Vector3(target.transform.eulerAngles.x, angle, target.transform.eulerAngles.z);
    }

    /// <summary>
    /// Gets distance between leg and the center of the stepping area.
    /// </summary>
    /// <returns> Distance between leg and the center of the stepping area </returns>
    float GetDistance() {
        Color color;
        float distance = Vector3.Distance(rayHitPoint, target.transform.position);
        Debug.DrawLine(rayHitPoint, target.transform.position, Color.yellow);

        if (distance >= maxDistance) {
            color = Color.red;
        } else {
            color = Color.green;
        }
        Debug.DrawLine(transform.position, new Vector3(transform.position.x, limbEnd.transform.position.y, transform.position.z), color);
        Debug.DrawLine(new Vector3(transform.position.x, limbEnd.transform.position.y, transform.position.z), limbEnd.transform.position, color);
        return distance;
    }

    /// <summary>
    /// Gets position for the leg to step to based on its velocity.
    /// </summary>
    /// <returns> Position for the leg to step to. </returns>
    Vector3 GetSteppingPoint() {
        Vector3 center = steppingArea.transform.position;
        float radius = maxDistance * 0.9f;
        Vector3 dir = velocity;
        float angle = Vector3.SignedAngle(Vector3.forward, dir, Vector3.up);
        Vector3 point = new Vector3(
            center.x + radius * Mathf.Sin(angle * Mathf.Deg2Rad),
            0,
            center.z + radius * Mathf.Cos(angle * Mathf.Deg2Rad) 
        );
        Vector3 origin = new Vector3(point.x, body.transform.position.y, point.z);

        RaycastHit hit;
        Physics.Raycast(origin, -body.transform.up, out hit, 10, ~(1 << 9));
        return hit.point;
    }

    /// <summary>
    /// Raises the leg in a stepping motion based on its distance to a target point.
    /// </summary>
    /// <param name="targetPoint"> The point the leg is being moved towards. </param>/
    void SineStep(Vector3 targetPoint) {
        float distance = Vector3.Distance(targetPoint, targetOrigin);
        Vector3 targetPos = new Vector3(target.transform.position.x, targetPoint.y, target.transform.position.z);
        float y = 4 * Mathf.Sin(Mathf.Deg2Rad * ((Vector3.Distance(targetPos, targetPoint) / distance) * 180));
        target.transform.position = new Vector3(target.transform.position.x, targetOrigin.y + y, target.transform.position.z);
    }

    /// <summary>
    /// Debug method for changing the color of the leg when it's stepping.
    /// </summary>
    void ChangeColor() {
        transform.GetChild(1).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material.color = joinedQueue ? Color.red : Color.black;
    }

    /// <summary>
    /// Used by BodyManager.CheckQueue to allow the leg to pass the queue system if it ends up too far from the stepping area.
    /// </summary>
    /// <returns> Distance between leg and leg target </returns>
    public float GetDistanceToTarget() {
        return Vector3.Distance(limbEnd.transform.position, target.transform.position);
    }

    /// <summary>
    /// Draws gizmos.
    /// </summary>
    void OnDrawGizmos() {
        Gizmos.color = new Color(1, 0, 0, 0.03f);
        Gizmos.DrawSphere(steppingArea.transform.position, maxDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(GetSteppingPoint(), 0.3f);
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(steppingPoint, 0.3f);
        Debug.DrawLine(rayHitPoint + (Vector3.up * 0.1f), limbEnd.transform.position + (Vector3.up * 0.1f), Color.yellow);
    }
}
