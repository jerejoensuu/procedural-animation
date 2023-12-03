using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace Code
{
    public class LegController : MonoBehaviour {
    
        private BodyManager _body;
        private Vector3 _rayHitPoint;
        private Vector3 _bodyOrigin;
        public GameObject limbEnd;
        public GameObject steppingArea;
        public GameObject target;
        private Vector3 _targetOrigin;
        public bool stepping = false;
        private bool _joinedQueue;
        public Side side;
        [FormerlySerializedAs("leg")] public int legIndex;
        private Vector3 _steppingPoint;
        private Vector3 _velocity;
        private Vector3 _prevPos;
        public Material defaultColor;
        public Material altColor;

        private float MovementSpeed => _body.moveSpeed * _body.movementSpeedMultiplier;

        public enum Side{
            Right = 1,
            Left = -1
        }

        void Start() {
            _body = transform.parent.GetComponent<BodyManager>();
            _rayHitPoint = target.transform.position;
            _bodyOrigin = transform.parent.transform.position;
            Raycast();
            _targetOrigin = target.transform.position;
            _prevPos = transform.position;
            _velocity = Vector3.zero;
        }

        void Update() {
            Raycast();
            if (!stepping) {
                target.transform.position = _targetOrigin;
            }

            // When distance between the leg and stepping area grows big enough, attempt to step.
            if (GetDistance() > _body.maxDistance && !stepping) {
                // First join the queue for legs wanting to step...
                if (!_joinedQueue) {
                    _body.JoinQueue(this);
                    _joinedQueue = true;
                }
                // ...Then step when appropriate legs are down. (More info in BodyManager)
                if (_body.CanStep(this)) {
                    stepping  = true;
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
            Vector3 position = transform.position;
            _velocity = Vector3.Normalize(position - _prevPos);
            _prevPos = position;
        }

        /// <summary>
        /// Casts a ray to move the stepping area.
        /// </summary>
        void Raycast() {
            RaycastHit hit;
            Vector3 rayStart = steppingArea.transform.position;
            var ray = new Ray(rayStart, Vector3.down);
            Debug.DrawRay(rayStart, (Vector3.down * transform.parent.transform.position.y), Color.red);

            if (Physics.Raycast(ray, out hit, 10f, ~(1 << 9)))
            {
                _rayHitPoint = hit.point;
            }
        }

        /// <summary>
        /// Moves the leg to new position.
        /// </summary>
        IEnumerator Step() {
            _steppingPoint = GetSteppingPoint();
            // Move leg towards new position until it's close enough.
            while(Vector3.Distance(target.transform.position, _steppingPoint) > 0.6f) {
                _steppingPoint = GetSteppingPoint();
                target.transform.position = Vector3.MoveTowards(target.transform.position, _steppingPoint, MovementSpeed * Time.deltaTime);
                SineStep(_steppingPoint); // Create stepping motion using sine waves.
                yield return new WaitForSeconds(Time.deltaTime);
            }
            target.transform.position = _steppingPoint;
            _targetOrigin = target.transform.position;
            stepping = _joinedQueue = false;
            _body.LeaveList(this); // Leave the list of currently moving legs.
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
            float distance = Vector3.Distance(_rayHitPoint, target.transform.position);
            Debug.DrawLine(_rayHitPoint, target.transform.position, Color.yellow);

            if (distance >= _body.maxDistance) {
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
            CalculateVelocity();
            Vector3 center = steppingArea.transform.position;
            float radius = _body.maxDistance * 0.9f;
            Vector3 dir = _velocity;
            float angle = Vector3.SignedAngle(Vector3.forward, dir, Vector3.up);
            Vector3 point = new Vector3(
                center.x + radius * Mathf.Sin(angle * Mathf.Deg2Rad),
                0,
                center.z + radius * Mathf.Cos(angle * Mathf.Deg2Rad) 
            );
            Vector3 origin = new Vector3(point.x, _body.transform.position.y, point.z);

            RaycastHit hit;
            Physics.Raycast(origin, -_body.transform.up, out hit, 10, ~(1 << 9));
            return hit.point;
        }

        /// <summary>
        /// Raises the leg in a stepping motion based on its distance to a target point.
        /// </summary>
        /// <param name="targetPoint"> The point the leg is being moved towards. </param>/
        void SineStep(Vector3 targetPoint) {
            float distance = Vector3.Distance(targetPoint, _targetOrigin);
            Vector3 targetPos = new Vector3(target.transform.position.x, targetPoint.y, target.transform.position.z);
            float y = 4 * Mathf.Sin(Mathf.Deg2Rad * ((Vector3.Distance(targetPos, targetPoint) / distance) * 180));
            target.transform.position = new Vector3(target.transform.position.x, _targetOrigin.y + y, target.transform.position.z);
        }

        /// <summary>
        /// Debug method for changing the color of the leg when it's stepping.
        /// </summary>
        void ChangeColor() {
            transform.GetChild(1).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material.color = _joinedQueue ? Color.red : Color.black;
        }

        /// <summary>
        /// Used by BodyManager.CheckQueue to allow the leg to pass the queue system if it ends up too far from the stepping area.
        /// </summary>
        /// <returns> Distance between leg and leg target </returns>
        public float GetDistanceToWalkingArea() {
            return Vector3.Distance(limbEnd.transform.position, steppingArea.transform.position);
        }
        
        public bool IsLegFullyExtended() {
            return Vector3.Distance(limbEnd.transform.position, transform.position) >= 14; // TODO: Use a variable instead of a hardcoded value.
        }

        /// <summary>
        /// Draws gizmos.
        /// </summary>
        void OnDrawGizmos() {
            if (!Application.isPlaying) return;
            
            Gizmos.color = new Color(1, 0, 0, 0.1f);
            Gizmos.DrawSphere(steppingArea.transform.position, _body.maxDistance);
            
            // Gizmos.color = Color.red;
            // Gizmos.DrawSphere(GetSteppingPoint(), 0.3f);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(_steppingPoint, 0.3f);
            
            Debug.DrawLine(_rayHitPoint + (Vector3.up * 0.1f), limbEnd.transform.position + (Vector3.up * 0.1f), Color.yellow);
        }
    }
}
