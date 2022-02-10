using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LegController : MonoBehaviour {
    
    private BodyManager body;
    private Vector3 rayHitPoint;
    private Vector3 rayPos;
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
    Vector3 velocity; // think of a better name
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
        SetRayPos();
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

        CalculateAntipedalPoint();
        if (GetDistance() > maxDistance && !stepping) {
            if (!joinedQueue) {
                body.JoinQueue(transform.gameObject);
                joinedQueue = true;
            }
            if (body.CanStep(transform.gameObject)) {
                stepping  = true;
                steppingPoint = GetSteppingPoint();
                StartCoroutine(Step());
            }
        }
        AlignLeg();
        ChangeColor();
    }

    void SetRayPos() {
        //rayPos = transform.position + (Vector3.RotateTowards(Vector3.forward, new Vector3((float)side, 0), 100, 100));
        rayPos = steppingArea.transform.position + (Vector3.up * transform.parent.transform.position.y);
        //rayPos = Vector3.zero;
    }

    void CalculateVelocity() {
        velocity = Vector3.Normalize(transform.position - prevPos);
        prevPos = transform.position;
    }

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

    IEnumerator Step() {
        Vector3 targetPoint = steppingPoint;
        while(Vector3.Distance(target.transform.position, targetPoint) > 0.6f) {
            // Vector3 offset = Vector3.RotateTowards(Vector3.forward);
            target.transform.position = Vector3.MoveTowards(target.transform.position, targetPoint, 140f * Time.deltaTime);
            SineStep(targetPoint);
            yield return new WaitForSeconds(Time.deltaTime);
        }
        target.transform.position = targetPoint;
        targetOrigin = target.transform.position;
        stepping = joinedQueue = false;
        body.LeaveList(transform.gameObject);
    }

    public void ReturnToRest() { }

    void AlignLeg() {
        float angle = Vector3.SignedAngle(Vector3.right,
                        new Vector3(target.transform.position.x, 0, target.transform.position.z) - new Vector3(transform.position.x, 0, transform.position.z),
                        Vector3.up);
        target.transform.eulerAngles = new Vector3(target.transform.eulerAngles.x, angle, target.transform.eulerAngles.z);
    }

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

    Vector3 GetSteppingPoint() {
        RaycastHit hit;
        Vector3 origin = new Vector3(CalculateAntipedalPoint().x, body.transform.position.y, CalculateAntipedalPoint().z);
        Physics.Raycast(origin, -body.transform.up, out hit, 10, ~(1 << 9));
        return hit.point;
    }

    Vector3 CalculateAntipedalPoint() {
        Vector3 center = steppingArea.transform.position;
        float radius = maxDistance * 0.9f;
        Vector3 dir = -velocity;
        float initDegree = Vector3.SignedAngle(Vector3.forward, dir, Vector3.up);
        float oppositeAngle = initDegree + 180;

        Vector3 antipedalPoint = new Vector3(
            center.x + radius * Mathf.Sin(oppositeAngle * Mathf.Deg2Rad),
            0,
            center.z + radius * Mathf.Cos(oppositeAngle * Mathf.Deg2Rad) 
        );

        return antipedalPoint;
    }

    void SineStep(Vector3 targetPoint) {
        float distance = Vector3.Distance(targetPoint, targetOrigin);
        Vector3 targetPos = new Vector3(target.transform.position.x, targetPoint.y, target.transform.position.z);
        float y = 4 * Mathf.Sin(Mathf.Deg2Rad * ((Vector3.Distance(targetPos, targetPoint) / distance) * 180));
        target.transform.position = new Vector3(target.transform.position.x, targetOrigin.y + y, target.transform.position.z);
    }

    void ChangeColor() {
        transform.GetChild(1).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material.color = joinedQueue ? Color.red : Color.black;
    }

    public float GetDistanceToTarget() {
        return Vector3.Distance(limbEnd.transform.position, target.transform.position);
    }

    void OnDrawGizmos() {
        Gizmos.color = new Color(1, 0, 0, 0.03f);
        Gizmos.DrawSphere(steppingArea.transform.position, maxDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(CalculateAntipedalPoint(), 0.3f);
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(steppingPoint, 0.3f);
        Debug.DrawLine(rayHitPoint + (Vector3.up * 0.1f), limbEnd.transform.position + (Vector3.up * 0.1f), Color.yellow);
    }
}
