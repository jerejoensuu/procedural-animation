using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        QualitySettings.vSyncCount = 0;

        inputs = new Inputs();
        inputs.Enable();
        inputs.Player.Move.performed += ReadMovement;
        inputs.Player.Move.canceled += ReadMovement;
        inputs.Player.Turn.performed += ReadRotation;
        inputs.Player.Turn.canceled += ReadRotation;
        inputs.Player.Reset.performed += ResetScene;

        //StartCoroutine(restTimer());
    }
    
    void Update() {
        ApplyMovement();
        CheckQueue();
    }

    /// <summary>
    /// Apply movement based on player input.
    /// </summary>
    void ApplyMovement() {
        if (moving) transform.Translate(movementDir * moveSpeed * Time.deltaTime);
        if (rotating) transform.RotateAround(transform.position, Vector3.up, turnSpeed * rotatingDir * Time.deltaTime);
    }

    /// <summary>
    /// Read player movement input.
    /// </summary>
    /// <param name="context"> InputAction event </param>
    void ReadMovement(InputAction.CallbackContext context) {
        moving = context.performed;
        movementDir = new Vector3(context.ReadValue<Vector2>().x, 0, context.ReadValue<Vector2>().y);
    }

    /// <summary>
    /// Read player rotation input.
    /// </summary>
    /// <param name="context"> InputAction event </param>
    void ReadRotation(InputAction.CallbackContext context) {
        rotating = context.performed;
        rotatingDir = (int) context.ReadValue<Vector2>().x;
    }

    /// <summary>
    /// Check if a leg is found in the currentlyMoving list to see if it should be allowed to step.
    /// </summary>
    /// <param name="leg"> Leg GameObject calling the method. </param>
    /// <returns> Boolean returned as answer. </returns>
    public bool CanStep(GameObject leg) {
        return currentlyMoving.Contains(leg) ? true : false;
    }

    /// <summary>
    /// Checks if a leg can be moved from the waiting queue to the list of currently moving legs.
    /// A leg is moved to the list if all legs of the same parity are not moving.
    /// </summary>
    void CheckQueue() {
        LegController qlCrl;
        LegController lCrl;
        bool freeToStep;
        foreach (GameObject queuedLeg in queue) {
            qlCrl = queuedLeg.GetComponent<LegController>();
            freeToStep = true;
            foreach (GameObject leg in currentlyMoving) {
                lCrl = leg.GetComponent<LegController>();
                if (qlCrl.leg % 2 == lCrl.leg % 2) {
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

    /// <summary>
    /// Adds a leg to the queue of legs waiting to be moved.
    /// </summary>
    /// <param name="leg"> Leg waiting to be moved. </param>
    public void JoinQueue(GameObject leg) {
        queue.Add(leg);
    }

    /// <summary>
    /// Removes leg from list of currently moving legs.
    /// </summary>
    /// <param name="leg"> Leg to be removed. </param>
    public void LeaveList(GameObject leg) {
        currentlyMoving.Remove(leg);
    }

    /// <summary>
    /// To be implemented...
    /// </summary>
    IEnumerator RestTimer() {
        yield return new WaitForSeconds(1);
        if (moving) { restTimer--; } else { restTimer = 0; }
        if (restTimer <= 0) {
            foreach (Transform leg in transform) {
                leg.GetComponent<LegController>().ReturnToRest();
            }
        }
    }

    /// <summary>
    /// Reloads the scene.
    /// </summary>
    /// <param name="context"> InputAction event </param>
    void ResetScene(InputAction.CallbackContext context) {
        SceneManager.LoadScene(0);
    }

    void OnDisable() {
        inputs.Disable();
        inputs.Player.Move.performed -= ReadMovement;
        inputs.Player.Move.canceled -= ReadMovement;
        inputs.Player.Turn.performed -= ReadRotation;
        inputs.Player.Turn.canceled -= ReadRotation;
        inputs.Player.Reset.performed -= ResetScene;
    }
}
