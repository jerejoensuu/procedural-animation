using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class BodyManager : MonoBehaviour {

    List<GameObject> queue = new List<GameObject>();
    List<GameObject> currentlyMoving = new List<GameObject>();
    List<GameObject> toRemove = new List<GameObject>();
    Inputs inputs;
    Vector3 movementDir;
    int rotatingDir;
    bool moving;
    bool rotating;
    public float moveSpeed = 10;
    public float turnSpeed = 15;
    int restTimer = 2;
    public float maxDistance = 6;

    void Awake() {
        inputs = new Inputs();
        inputs.Enable();
        inputs.Player.Move.performed += ReadMovement;
        inputs.Player.Move.canceled += ReadMovement;
        inputs.Player.Turn.performed += ReadRotation;
        inputs.Player.Turn.canceled += ReadRotation;

        //StartCoroutine(restTimer());
    }
    
    void Update() {
        ApplyMovement();
        CheckQueue();
    }

    void ApplyMovement() {
        if (moving) transform.Translate(movementDir * moveSpeed * Time.deltaTime);
        if (rotating) transform.RotateAround(transform.position, Vector3.up, turnSpeed * rotatingDir * Time.deltaTime);
    }

    void ReadMovement(InputAction.CallbackContext context) {
        moving = context.performed;
        movementDir = new Vector3(context.ReadValue<Vector2>().x, 0, context.ReadValue<Vector2>().y);
    }

    void ReadRotation(InputAction.CallbackContext context) {
        rotating = context.performed;
        rotatingDir = (int) context.ReadValue<Vector2>().x;
    }

    public bool CanStep(GameObject leg) {
        return currentlyMoving.Contains(leg) ? true : false;
    }

    void CheckQueue() {
        LegController qlCrl;
        LegController lCrl;
        bool freeToStep;
        foreach (GameObject queuedLeg in queue) {
            qlCrl = queuedLeg.GetComponent<LegController>();
            freeToStep = true;
            foreach (GameObject leg in currentlyMoving) {
                lCrl = leg.GetComponent<LegController>();
                if (qlCrl.leg % 2 == lCrl.leg % 2 || qlCrl.side == lCrl.side) {
                    freeToStep = false;
                }
            }
            if (freeToStep || qlCrl.GetDistanceToTarget() > 1) {
                currentlyMoving.Add(queuedLeg);
                toRemove.Add(queuedLeg);
            }
        }

        foreach (GameObject leg in toRemove) {
            queue.Remove(leg);
        }
        toRemove.Clear();
        
    }

    public void JoinQueue(GameObject leg) {
        queue.Add(leg);
    }

    public void LeaveList(GameObject leg) {
        currentlyMoving.Remove(leg);
    }

    IEnumerator RestTimer() {
        yield return new WaitForSeconds(1);
        if (moving) { restTimer--; } else { restTimer = 0; }
        if (restTimer <= 0) {
            foreach (Transform leg in transform) {
                leg.GetComponent<LegController>().ReturnToRest();
            }
        }
    }
}
