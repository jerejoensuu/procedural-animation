using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Code
{
    public class BodyManager : MonoBehaviour {

        private List<LegController> _queue = new();
        private List<LegController> _currentlyMoving = new();
        private List<LegController> _toRemove = new();
        private Inputs _inputs;
        private Vector3 _movementDir;
        private int _rotatingDir;
        private bool _moving;
        private bool _rotating;
        public float moveSpeed = 10;
        public float turnSpeed = 15;
        private int _restTimer = 2;
        public float maxDistance = 6;
        public float movementSpeedMultiplier = 4;
        
        public bool blockMovementIfLegsAreHyperExtended = false;

        void Awake() {
            QualitySettings.vSyncCount = 0;

            _inputs = new Inputs();
            _inputs.Enable();
            _inputs.Player.Move.performed += ReadMovement;
            _inputs.Player.Move.canceled += ReadMovement;
            _inputs.Player.Turn.performed += ReadRotation;
            _inputs.Player.Turn.canceled += ReadRotation;
            _inputs.Player.Reset.performed += ResetScene;

            //StartCoroutine(restTimer());
        }
    
        void Update() {
            ApplyMovement();
            CheckQueue();
        }

        /// <summary>
        /// Apply movement based on player input.
        /// </summary>
        void ApplyMovement()
        {
            float speed = moveSpeed;
            if (blockMovementIfLegsAreHyperExtended && _queue.Any(leg => leg.IsLegFullyExtended()))
            {
                speed *= 0.01f;
            }
            
            if (_moving) transform.Translate(_movementDir * (speed * Time.deltaTime));
            if (_rotating) transform.RotateAround(transform.position, Vector3.up, turnSpeed * _rotatingDir * Time.deltaTime);
        }

        /// <summary>
        /// Read player movement input.
        /// </summary>
        /// <param name="context"> InputAction event </param>
        void ReadMovement(InputAction.CallbackContext context) {
            _moving = context.performed;
            _movementDir = new Vector3(context.ReadValue<Vector2>().x, 0, context.ReadValue<Vector2>().y);
        }

        /// <summary>
        /// Read player rotation input.
        /// </summary>
        /// <param name="context"> InputAction event </param>
        void ReadRotation(InputAction.CallbackContext context) {
            _rotating = context.performed;
            _rotatingDir = (int) context.ReadValue<Vector2>().x;
        }

        /// <summary>
        /// Check if a leg is found in the currentlyMoving list to see if it should be allowed to step.
        /// </summary>
        /// <param name="leg"> Leg GameObject calling the method. </param>
        /// <returns> Boolean returned as answer. </returns>
        public bool CanStep(LegController leg) {
            return _currentlyMoving.Contains(leg);
        }

        /// <summary>
        /// Checks if a leg can be moved from the waiting queue to the list of currently moving legs.
        /// A leg is moved to the list if all legs of the same parity are not moving.
        /// </summary>
        private void CheckQueue() {
            foreach (LegController queuedLeg in _queue) {
                bool freeToStep = true;
                foreach (LegController leg in _currentlyMoving) {
                    // Check if opposing leg is moving
                    if (queuedLeg.side != leg.side && queuedLeg.legIndex % 2 == leg.legIndex % 2) {
                        freeToStep = false;
                    }
                    // Check if neighboring leg is moving
                    if (queuedLeg.side == leg.side && Mathf.Abs(queuedLeg.legIndex - leg.legIndex) == 1) {
                        freeToStep = false;
                    }
                }
                // Ignore previous if leg is hyper-extended
                if (freeToStep || queuedLeg.IsLegFullyExtended()) {
                    _currentlyMoving.Add(queuedLeg);
                    _toRemove.Add(queuedLeg);
                }
            }

            foreach (LegController leg in _toRemove) {
                _queue.Remove(leg);
            }
            _toRemove.Clear();
        }

        /// <summary>
        /// Adds a leg to the queue of legs waiting to be moved.
        /// </summary>
        /// <param name="leg"> Leg waiting to be moved. </param>
        public void JoinQueue(LegController leg) {
            _queue.Add(leg);
        }

        /// <summary>
        /// Removes leg from list of currently moving legs.
        /// </summary>
        /// <param name="leg"> Leg to be removed. </param>
        public void LeaveList(LegController leg) {
            _currentlyMoving.Remove(leg);
        }

        /// <summary>
        /// To be implemented...
        /// </summary>
        IEnumerator RestTimer() {
            yield return new WaitForSeconds(1);
            if (_moving) { _restTimer--; } else { _restTimer = 0; }
            if (_restTimer <= 0) {
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
            _inputs.Disable();
            _inputs.Player.Move.performed -= ReadMovement;
            _inputs.Player.Move.canceled -= ReadMovement;
            _inputs.Player.Turn.performed -= ReadRotation;
            _inputs.Player.Turn.canceled -= ReadRotation;
            _inputs.Player.Reset.performed -= ResetScene;
        }
    }
}
