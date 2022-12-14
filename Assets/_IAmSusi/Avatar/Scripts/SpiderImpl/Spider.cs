/* 
 * This file is part of Unity-Procedural-IK-Wall-Walking-Spider on github.com/PhilS94
 * Copyright (C) 2020 Philipp Schofield - All Rights Reserved
 */
using System;
using Game.Avatar.SpiderImpl.Raycasting;
using UnityEngine;

namespace Game.Avatar.SpiderImpl {
    /*
     * This class represents the actual spider. It is responsible for "glueing" it to the surfaces around it. This is accomplished by
     * creating a fake gravitational force in the direction of the surface normal it is standing on. The surface normal is determined
     * by spherical raycasting downwards, as well as forwards for wall-climbing.
     * 
     * The torso of the spider will move and rotate depending on the height of the referenced legs to mimic "spinal movement".
     * 
     * The spider does not move on its own. Therefore a controller should call the provided functions walk() and turn() for
     * the desired control.
     */

    [DefaultExecutionOrder(0)] // Any controller of this spider should have default execution -1
    sealed class Spider : MonoBehaviour {
        public event Action<float> onMove;

        [SerializeField]
        Rigidbody attachedRigidbody;
        [SerializeField]
        SphereCollider attachedCollider;

        void OnValidate() {
            if (!attachedRigidbody) {
                TryGetComponent(out attachedRigidbody);
            }
            if (!attachedCollider) {
                TryGetComponent(out attachedCollider);
            }
        }

        [Header("Movement")]
        [Range(1, 1000)]
        public float walkSpeed;
        [Range(1, 1000)]
        public float runSpeed;
        [Range(1, 500)]
        public float turnSpeed;
        [Range(0.001f, 1)]
        public float walkDrag;

        [Header("Grounding")]
        [Range(1, 10)]
        public float gravityMultiplier;
        [Range(1, 10)]
        public float groundNormalAdjustSpeed;
        [Range(1, 10)]
        public float forwardNormalAdjustSpeed;
        public LayerMask walkableLayer;
        [Range(0, 1)]
        public float gravityOffDistance;

        [Header("Root Movement")]
        [Space(10)]
        public Transform root;
        public IKChain[] legs { get; private set; }

        [Space(10)]
        [Range(0, 0.003f)]
        public float rootOffsetHeight;

        [Space(10)]
        public bool legCentroidAdjustment;
        [Range(0, 100)]
        public float legCentroidSpeed;
        [Range(0, 1)]
        public float legCentroidNormalWeight;
        [Range(0, 1)]
        public float legCentroidTangentWeight;

        [Space(10)]
        public bool legNormalAdjustment;
        [Range(0, 100)]
        public float legNormalSpeed;
        [Range(0, 1)]
        public float legNormalWeight;

        public Vector3 bodyY { get; private set; }
        Vector3 bodyZ;

        [Space(10)]
        public bool breathing;
        [Range(0.01f, 20)]
        public float breathePeriod;
        [Range(0, 1)]
        public float breatheMagnitude;

        [Header("Ray Adjustments")]
        [Range(0.0f, 1.0f)]
        public float forwardRayLength;
        [Range(0.0f, 1.0f)]
        public float downRayLength;
        [Range(0.1f, 1.0f)]
        public float forwardRaySize = 0.66f;
        [Range(0.1f, 1.0f)]
        public float downRaySize = 0.9f;
        float downRayRadius;

        Vector3 currentVelocity;
        public bool isMoving { get; private set; } = true;
        bool groundCheckOn = true;

        Vector3 bodyDefaultCentroid;
        Vector3 bodyCentroid;

        public SphereCast downRay { get; private set; }
        public SphereCast forwardRay { get; private set; }
        RaycastHit hitInfo;

        public enum RayType { None, ForwardRay, DownRay };
        [Serializable]
        public struct GroundInfo {
            public bool isGrounded;
            public Vector3 groundNormal;
            public float distanceToGround;
            public RayType rayType;

            public GroundInfo(bool isGrd, Vector3 normal, float dist, RayType m_rayType) {
                isGrounded = isGrd;
                groundNormal = normal;
                distanceToGround = dist;
                rayType = m_rayType;
            }
        }
        public GroundInfo groundInfo;

        public void Awake() {
            // Debug.Log("Called Awake " + name + " on Spider");

            //Make sure the scale is uniform, since otherwise lossy scale will not be accurate.
            float x = transform.localScale.x;
            float y = transform.localScale.y;
            float z = transform.localScale.z;
            if (Mathf.Abs(x - y) > float.Epsilon || Mathf.Abs(x - z) > float.Epsilon || Mathf.Abs(y - z) > float.Epsilon) {
                Debug.LogWarning("The xyz scales of the Spider are not equal. Please make sure they are. The scale of the spider is defaulted to be the Y scale and a lot of values depend on this scale.");
            }

            attachedRigidbody = GetComponent<Rigidbody>();

            //Find all childed IKChains
            legs = GetComponentsInChildren<IKChain>();

            //Initialize the two Sphere Casts
            downRayRadius = downRaySize * getColliderRadius();
            float forwardRayRadius = forwardRaySize * getColliderRadius();
            downRay = new SphereCast(transform.position, -transform.up, downRayLength * getColliderLength(), downRayRadius, transform, transform);
            forwardRay = new SphereCast(transform.position, transform.forward, forwardRayLength * getColliderLength(), forwardRayRadius, transform, transform);

            //Initialize the bodyupLocal as the spiders transform.up parented to the body. Initialize the breathePivot as the body position parented to the spider
            bodyY = root.transform.InverseTransformDirection(transform.up);
            bodyZ = root.transform.InverseTransformDirection(transform.forward);
            bodyCentroid = root.transform.position + getScale() * rootOffsetHeight * transform.up;
            bodyDefaultCentroid = transform.InverseTransformPoint(bodyCentroid);
        }

        void FixedUpdate() {
            onMove?.Invoke(Time.deltaTime);
        }

        void OldFixedUpdate() {
            //** Ground Check **//
            groundInfo = GroundCheck();

            //** Rotation to normal **// 
            float normalAdjustSpeed = (groundInfo.rayType == RayType.ForwardRay) ? forwardNormalAdjustSpeed : groundNormalAdjustSpeed;

            var slerpNormal = Vector3.Slerp(transform.up, groundInfo.groundNormal, 0.02f * normalAdjustSpeed);
            var goalrotation = getLookRotation(Vector3.ProjectOnPlane(transform.right, slerpNormal), slerpNormal);

            //Apply the rotation to the spider
            if (Quaternion.Angle(transform.rotation, goalrotation) > Mathf.Epsilon) {
                transform.rotation = goalrotation;
            }

            // Dont apply gravity if close enough to ground
            if (groundInfo.distanceToGround > getGravityOffDistance()) {
                attachedRigidbody.AddForce(-groundInfo.groundNormal * gravityMultiplier * 0.0981f * getScale()); //Important using the groundnormal and not the lerping normal here!
            }
        }

        void Update() {
            var Y = root.TransformDirection(bodyY);

            //Doesnt work the way i want it too! On sphere i go underground. I jiggle around when i go down my centroid moves down to.(Depends on errortolerance of IKSolver)
            if (legCentroidAdjustment) {
                bodyCentroid = Vector3.Lerp(bodyCentroid, getLegsCentroid(), Time.deltaTime * legCentroidSpeed);
            } else {
                bodyCentroid = getDefaultCentroid();
            }

            root.transform.position = bodyCentroid;

            if (legNormalAdjustment) {
                var newNormal = GetLegsPlaneNormal();

                //Use Global X for  pitch
                var X = transform.right;
                float angleX = Vector3.SignedAngle(Vector3.ProjectOnPlane(Y, X), Vector3.ProjectOnPlane(newNormal, X), X);
                angleX = Mathf.LerpAngle(0, angleX, Time.deltaTime * legNormalSpeed);
                root.transform.rotation = Quaternion.AngleAxis(angleX, X) * root.transform.rotation;

                //Use Local Z for roll. With the above global X for pitch, this avoids any kind of yaw happening.
                var Z = root.TransformDirection(bodyZ);
                float angleZ = Vector3.SignedAngle(Y, Vector3.ProjectOnPlane(newNormal, Z), Z);
                angleZ = Mathf.LerpAngle(0, angleZ, Time.deltaTime * legNormalSpeed);
                root.transform.rotation = Quaternion.AngleAxis(angleZ, Z) * root.transform.rotation;
            }

            if (breathing) {
                float t = (Time.time * 2 * Mathf.PI / breathePeriod) % (2 * Mathf.PI);
                float amplitude = breatheMagnitude * getColliderRadius();
                var direction = root.TransformDirection(bodyY);

                root.transform.position = bodyCentroid + amplitude * (Mathf.Sin(t) + 1f) * direction;
            }

            // Update the moving status
            if (transform.hasChanged) {
                isMoving = true;
                transform.hasChanged = false;
            } else {
                isMoving = false;
            }
        }


        //** Movement methods**//

        void move(Vector3 direction, float speed) {

            // TODO: Make sure direction is on the XZ plane of spider! For this maybe refactor the logic from input from spidercontroller to this function.

            //Only allow direction vector to have a length of 1 or lower
            float magnitude = direction.magnitude;
            if (magnitude > 1) {
                direction = direction.normalized;
                magnitude = 1f;
            }

            // Scale the magnitude and Clamp to not move more than down ray radius (Makes sure the ground is not lost due to moving too fast)
            if (direction != Vector3.zero) {
                float directionDamp = Mathf.Pow(Mathf.Clamp(Vector3.Dot(direction / magnitude, transform.forward), 0, 1), 2);
                float distance = 0.0004f * speed * magnitude * directionDamp * getScale();
                distance = Mathf.Clamp(distance, 0, 0.99f * downRayRadius);
                direction = distance * (direction / magnitude);
            }

            //Slerp from old to new velocity using the acceleration
            currentVelocity = Vector3.Slerp(currentVelocity, direction, 1f - walkDrag);

            //Apply the resulting velocity
            transform.position += currentVelocity;
        }

        public void turn(Vector3 goalForward) {
            //Make sure goalForward is orthogonal to transform up
            goalForward = Vector3.ProjectOnPlane(goalForward, transform.up).normalized;

            if (goalForward == Vector3.zero || Vector3.Angle(goalForward, transform.forward) < Mathf.Epsilon) {
                return;
            }
            goalForward = Vector3.ProjectOnPlane(goalForward, transform.up);

            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(goalForward, transform.up), turnSpeed);
        }

        //** Movement methods for public access**//
        // It is advised to call these on a fixed update basis.

        public void walk(Vector3 direction) {
            if (direction.magnitude < Mathf.Epsilon) {
                return;
            }

            move(direction, walkSpeed);
        }

        public void run(Vector3 direction) {
            if (direction.magnitude < Mathf.Epsilon) {
                return;
            }

            move(direction, runSpeed);
        }

        //** Ground Check Method **//
        public GroundInfo GroundCheck() {
            if (groundCheckOn) {
                if (forwardRay.castRay(out hitInfo, walkableLayer)) {
                    return new GroundInfo(true, hitInfo.normal.normalized, Vector3.Distance(transform.TransformPoint(attachedCollider.center), hitInfo.point) - getColliderRadius(), RayType.ForwardRay);
                }

                if (downRay.castRay(out hitInfo, walkableLayer)) {
                    return new GroundInfo(true, hitInfo.normal.normalized, Vector3.Distance(transform.TransformPoint(attachedCollider.center), hitInfo.point) - getColliderRadius(), RayType.DownRay);
                }
            }
            return new GroundInfo(false, Vector3.up, float.PositiveInfinity, RayType.None);
        }

        //** Helper methods**//

        /*
        * Returns the rotation with specified right and up direction   
        * May have to make more error catches here. Whatif not orthogonal?
        */
        Quaternion getLookRotation(Vector3 right, Vector3 up) {
            if (up == Vector3.zero || right == Vector3.zero) {
                return Quaternion.identity;
            }
            // If vectors are parallel return identity
            float angle = Vector3.Angle(right, up);
            if (angle == 0 || angle == 180) {
                return Quaternion.identity;
            }

            var forward = Vector3.Cross(right, up);
            return Quaternion.LookRotation(forward, up);
        }

        //** Torso adjust methods for more realistic movement **//

        // Calculate the centroid (center of gravity) given by all end effector positions of the legs
        public Vector3 getLegsCentroid() {
            if (legs == null || legs.Length == 0) {
                Debug.LogError("Cant calculate leg centroid, legs not assigned.");
                return root.transform.position;
            }
            var defaultCentroid = getDefaultCentroid();
            // Calculate the centroid of legs position
            var newCentroid = Vector3.zero;
            float k = 0;
            for (int i = 0; i < legs.Length; i++) {
                newCentroid += legs[i].endEffector.position;
                k++;
            }
            newCentroid = newCentroid / k;

            // Offset the calculated centroid
            var offset = Vector3.Project(defaultCentroid - getColliderBottomPoint(), transform.up);
            newCentroid += offset;

            // Calculate the normal and tangential translation needed
            var normalPart = Vector3.Project(newCentroid - defaultCentroid, transform.up);
            var tangentPart = Vector3.ProjectOnPlane(newCentroid - defaultCentroid, transform.up);

            return defaultCentroid + Vector3.Lerp(Vector3.zero, normalPart, legCentroidNormalWeight) + Vector3.Lerp(Vector3.zero, tangentPart, legCentroidTangentWeight);
        }

        // Calculate the normal of the plane defined by leg positions, so we know how to rotate the body
        Vector3 GetLegsPlaneNormal() {

            if (legs == null) {
                Debug.LogError("Cant calculate normal, legs not assigned.");
                return transform.up;
            }

            if (legNormalWeight <= 0f) {
                return transform.up;
            }

            var newNormal = transform.up;
            Vector3 toEnd;
            Vector3 currentTangent;

            for (int i = 0; i < legs.Length; i++) {
                //normal += legWeight * legs[i].getTarget().normal;
                toEnd = legs[i].endEffector.position - transform.position;
                currentTangent = Vector3.ProjectOnPlane(toEnd, transform.up);

                if (currentTangent == Vector3.zero) {
                    continue; // Actually here we would have a 90degree rotation but there is no choice of a tangent.
                }

                newNormal = Quaternion.Lerp(Quaternion.identity, Quaternion.FromToRotation(currentTangent, toEnd), legNormalWeight) * newNormal;
            }
            return newNormal;
        }


        //** Getters **//
        public float getScale() {
            return transform.lossyScale.y;
        }

        public Vector3 getCurrentVelocityPerSecond() {
            return currentVelocity / Time.fixedDeltaTime;
        }

        public Vector3 getCurrentVelocityPerFixedFrame() {
            return currentVelocity;
        }
        public Vector3 getGroundNormal() {
            return groundInfo.groundNormal;
        }

        public float getColliderRadius() {
            return getScale() * attachedCollider.radius;
        }

        public float getNonScaledColliderRadius() {
            return attachedCollider.radius;
        }

        public float getColliderLength() {
            return getScale() * attachedCollider.radius;
        }

        public Vector3 getColliderCenter() {
            return transform.TransformPoint(attachedCollider.center);
        }

        public Vector3 getColliderBottomPoint() {
            return transform.TransformPoint(attachedCollider.center - attachedCollider.radius * new Vector3(0, 1, 0));
        }

        public Vector3 getDefaultCentroid() {
            return transform.TransformPoint(bodyDefaultCentroid);
        }

        public float getGravityOffDistance() {
            return gravityOffDistance * getColliderRadius();
        }

        //** Setters **//
        public void setGroundcheck(bool b) {
            groundCheckOn = b;
        }
    }


}