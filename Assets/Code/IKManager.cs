using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IKManager : MonoBehaviour {
    
    public JointController root;
    public JointController end;
    public GameObject target;
    public GameObject poletarget;
    float threshold = 0.05f;
    public float rate = 5f;
    public int steps = 20;

    void Update() {

        for (int i = 0; i < steps; ++i) {
            if (GetDistance(end.transform.position, target.transform.position) > threshold) {

                JointController current = root;
                while (current != null) {
                    float slope = CalculateSlope(current);
                    current.Rotate(-slope * rate);
                    current = current.GetChild();
                }

                float angle = CalculateAngle(root);
                root.Pivot(-angle * rate);

            } else if (GetDistance(end.transform.position, poletarget.transform.position) > threshold) {
                JointController current = root;
                while (current != null || current == end) {
                    float slope = CalculateSlope(current);
                    current.Rotate(-slope * rate);
                    current = current.GetChild();
                }
            }
        }

    }

    float CalculateSlope(JointController joint) {
        float deltaAngle = 0.01f;
        float distance1 = GetDistance(end.transform.position, target.transform.position);

        joint.Rotate(deltaAngle);

        float distance2 = GetDistance(end.transform.position, target.transform.position);

        joint.Rotate(-deltaAngle);

        return (distance2 - distance1) / deltaAngle;
    }

    float CalculateAngle(JointController joint) {
        float deltaAngle = 0.01f;
        float distance1 = GetDistance(end.transform.position, target.transform.position);

        joint.Pivot(deltaAngle);

        float distance2 = GetDistance(end.transform.position, target.transform.position);

        joint.Pivot(-deltaAngle);

        return (distance2 - distance1) / deltaAngle;
    }

    float GetDistance(Vector3 point1, Vector3 point2) {
        return Vector3.Distance(point1, point2);
    }

}
