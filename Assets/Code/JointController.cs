using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JointController : MonoBehaviour {

    JointController child;

    void Start() {
        if (transform.childCount > 1) {
            child = transform.GetChild(1).GetComponent<JointController>();
        }
    }

    public JointController GetChild() {
        return child;
    }
    
    public void Rotate(float angle) {
        transform.Rotate(Vector3.down * angle);
    }

    public void Pivot(float angle) {
        transform.Rotate(Vector3.forward * angle);
    }

}
