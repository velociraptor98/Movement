using System.Collections.Generic;
using UnityEngine;

// Procedural foot placement for humanoid locomotion, per leg:
// - A planted foot is locked in place (position and height) for the whole stance, so it can't
//   slide or drift across a step edge after landing.
// - When a foot lifts off, its landing is predicted and moved onto a safe spot on the step it
//   will reach (clear of the edge behind the heel and the riser in front of the toe). During the
//   swing the foot is carried to that spot and its height moves between the two steps: rising
//   early when stepping up, dropping late when stepping down.
// - The hips follow the lower foot's ground in world space rather than the CharacterController,
//   so the capsule popping up steps or rolling over their edges doesn't bounce the body.
// - On stairs the character is slowed so the stride is close to one step per tread.
[RequireComponent(typeof(Animator), typeof(PlayerController))]
public class FootPlacement : MonoBehaviour
{
    [SerializeField] LayerMask ground;
    [Tooltip("Highest and lowest ground relative to the root that a foot will reach for.")]
    [SerializeField] float maxStepUp = 0.5f;
    [SerializeField] float maxStepDown = 0.5f;

    [Header("Feet")]
    [Tooltip("Ankle to back of the heel, and ankle to toe tip, along the foot.")]
    [SerializeField] float heelLength = 0.07f;
    [SerializeField] float toeLength = 0.18f;
    [Tooltip("A foot is planted when its animated world speed falls below this (m/s) plus a fraction of " +
             "the character's speed, and lifts when it rises above the lift threshold. Speed rather than " +
             "height, because blending toward idle flattens the swing arc.")]
    [SerializeField] float plantSpeed = 0.15f;
    [SerializeField] float plantSpeedRatio = 0.35f;
    [SerializeField] float liftSpeedRatio = 0.6f;
    [Tooltip("Animated sole height above the root above which a foot is never planted.")]
    [SerializeField] float liftHeight = 0.07f;
    [Tooltip("Furthest a landing is moved to fit on a step.")]
    [SerializeField] float maxLandingAdjust = 0.3f;
    [Tooltip("Furthest a locked foot may be held away from its animated position before it is released " +
             "(e.g. when turning on the spot).")]
    [SerializeField] float maxLockOffset = 0.45f;
    [Tooltip("How fast a swinging foot's landing spot and height may move as the prediction is refined.")]
    [SerializeField] float retargetSpeed = 1.5f;
    [SerializeField] float retargetHeightSpeed = 2.0f;
    [SerializeField] bool alignToSurface = true;

    [Header("Steps")]
    [Tooltip("Ground profile sampled ahead of the character to find step edges.")]
    [SerializeField] float profileBehind = 0.5f;
    [SerializeField] float profileAhead = 1.6f;
    [SerializeField] float profileSpacing = 0.05f;
    [Tooltip("Height change between neighbouring flat samples that counts as a step edge.")]
    [SerializeField] float minRiser = 0.04f;
    [Tooltip("Speed caps while there are steps under or just ahead of the character.")]
    [SerializeField] float stairsSpeed = 0.85f;
    [SerializeField] float stairsSprintSpeed = 1.3f;

    [Header("Smoothing")]
    [SerializeField] float pelvisSmoothTime = 0.1f;
    [SerializeField] float maxPelvisDrop = 0.45f;
    [Tooltip("How quickly foot placement fades in and out (e.g. leaving the ground).")]
    [SerializeField] float fadeSpeed = 8.0f;

    Animator animator;
    PlayerController player;
    ParkourController parkour;
    readonly Leg left = new Leg(AvatarIKGoal.LeftFoot);
    readonly Leg right = new Leg(AvatarIKGoal.RightFoot);
    // Step edges ahead, as distances along the facing direction from the root.
    readonly List<float> edges = new List<float>();
    Vector3 profileOrigin;
    Vector3 profileForward;
    float weight;
    float pelvisY;
    float pelvisVelocity;
    bool initialized;
    Vector3 lastPosition;
    Vector3 velocity;

    public float Weight => weight;
    public float PelvisOffset { get; private set; }
    public bool OnSteps { get; private set; }
    public bool LeftPlanted => left.Planted;
    public bool RightPlanted => right.Planted;
    // World positions the feet were last placed at (ankle IK targets).
    public Vector3 LeftTarget => left.Target;
    public Vector3 RightTarget => right.Target;

    void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponent<PlayerController>();
        parkour = GetComponent<ParkourController>();
        lastPosition = transform.position;
    }

    void OnDisable()
    {
        if (player != null) player.SpeedLimit = float.PositiveInfinity;
    }

    void Update()
    {
        if (Time.deltaTime > 0.0f)
        {
            velocity = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up) / Time.deltaTime;
        }
        lastPosition = transform.position;

        bool inParkour = parkour != null && parkour.InAction;
        bool active = player.IsGrounded && player.HasControl && !inParkour;
        if (inParkour) weight = 0.0f; // parkour owns the IK
        weight = Mathf.MoveTowards(weight, active ? 1.0f : 0.0f, fadeSpeed * Time.deltaTime);
        if (weight <= 0.0f) initialized = false;

        SampleProfile();
        player.SpeedLimit = OnSteps && active
            ? (player.IsSprinting ? stairsSprintSpeed : stairsSpeed)
            : float.PositiveInfinity;
    }

    // Samples ground heights along the facing direction and records step edges: neighbouring
    // flat samples whose heights differ by at least a riser. Slopes have tilted normals and
    // aren't mistaken for steps.
    void SampleProfile()
    {
        edges.Clear();
        profileOrigin = transform.position;
        profileForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        bool hasPrevious = false;
        float previousHeight = 0.0f;
        for (float d = -profileBehind; d <= profileAhead; d += profileSpacing)
        {
            bool hit = CastDown(profileOrigin + profileForward * d, out RaycastHit groundHit);
            bool flat = hit && groundHit.normal.y > 0.985f; // within ~10° of level
            if (flat && hasPrevious && Mathf.Abs(groundHit.point.y - previousHeight) >= minRiser)
            {
                edges.Add(d - profileSpacing * 0.5f);
            }
            hasPrevious = flat;
            if (flat) previousHeight = groundHit.point.y;
        }
        // Steps count if they are under the feet or within a stride ahead.
        OnSteps = edges.Exists(e => e > -profileBehind && e < 1.0f);
    }

    void OnAnimatorIK(int layerIndex)
    {
        // Locomotion IK belongs to the base layer; other IK layers (e.g. parkour poses) do their own.
        if (layerIndex != 0 || weight <= 0.0f) return;

        float rootY = transform.position.y;
        if (!initialized)
        {
            left.Reset(this);
            right.Reset(this);
            pelvisY = Mathf.Min(left.GroundY, right.GroundY);
            pelvisVelocity = 0.0f;
            initialized = true;
        }

        UpdateLeg(left);
        UpdateLeg(right);

        // Hips follow the lower foot's ground in world space, so the lower leg can always reach.
        float targetPelvis = Mathf.Max(Mathf.Min(left.GroundY, right.GroundY), rootY - maxPelvisDrop);
        pelvisY = Mathf.SmoothDamp(pelvisY, targetPelvis, ref pelvisVelocity, pelvisSmoothTime, Mathf.Infinity, Time.deltaTime);
        PelvisOffset = Mathf.Clamp(pelvisY - rootY, -maxPelvisDrop, maxStepUp) * weight;
        animator.bodyPosition += Vector3.up * PelvisOffset;

        ApplyLeg(left, rootY);
        ApplyLeg(right, rootY);
    }

    void UpdateLeg(Leg leg)
    {
        Vector3 animated = animator.GetIKPosition(leg.Goal);
        float sole = leg.Goal == AvatarIKGoal.LeftFoot ? animator.leftFeetBottomHeight : animator.rightFeetBottomHeight;
        float lift = animated.y - transform.position.y - sole;
        Vector3 moved = Vector3.ProjectOnPlane(animated - leg.PreviousAnimated, Vector3.up);
        float footSpeed = Time.deltaTime > 0.0f ? moved.magnitude / Time.deltaTime : 0.0f;
        leg.PreviousAnimated = animated;

        bool wasPlanted = leg.Planted;
        float ratio = wasPlanted ? liftSpeedRatio : plantSpeedRatio;
        leg.Planted = lift < liftHeight && footSpeed < plantSpeed + ratio * velocity.magnitude;

        if (wasPlanted && !leg.Planted) BeginSwing(leg);
        else if (!wasPlanted && leg.Planted) Land(leg, animated);

        if (leg.Planted)
        {
            // Hold the foot where it landed; release the lock if the body has turned too far from it.
            Vector3 hold = Vector3.ProjectOnPlane(leg.LockPosition - animated, Vector3.up);
            if (hold.magnitude > maxLockOffset) leg.LockPosition = animated + leg.Offset;
            else leg.Offset = hold;
            // A foot still above its step (the swing ended early) drops straight onto it.
            leg.GroundY = leg.GroundY > leg.PlantedY
                ? Mathf.MoveTowards(leg.GroundY, leg.PlantedY, 8.0f * Time.deltaTime)
                : Mathf.SmoothDamp(leg.GroundY, leg.PlantedY, ref leg.Velocity, 0.03f, Mathf.Infinity, Time.deltaTime);
        }
        else
        {
            RefineLanding(leg, false);
            // Arrive a little before the estimated end, as swings often finish early.
            float u = Mathf.Clamp01((Time.time - leg.SwingStart) / (leg.SwingDuration * 0.85f));
            leg.Offset = Vector3.Lerp(leg.FromOffset, leg.ToOffset, Mathf.SmoothStep(0.0f, 1.0f, u));
            // Up: rise early to clear the edge. Down: stay high until over the lower step.
            float shape = leg.ToY >= leg.FromY ? 1.0f - (1.0f - u) * (1.0f - u) : u * u;
            // Never below the ground actually under the foot right now, so it can't dip into
            // the step it is leaving or clip the edge of the one it is reaching for.
            float under = ProbeFoot(animated + leg.Offset, out _);
            float target = Mathf.Max(Mathf.Lerp(leg.FromY, leg.ToY, shape), under);
            leg.GroundY = target > leg.GroundY
                ? Mathf.MoveTowards(leg.GroundY, target, 4.0f * Time.deltaTime)
                : target;
        }
    }

    void BeginSwing(Leg leg)
    {
        leg.SwingStart = Time.time;
        leg.FromY = leg.GroundY;
        leg.FromOffset = leg.Offset;
        RefineLanding(leg, true);
    }

    // Predicts where the animation will put this foot down (the root carries on for the rest of
    // the swing, then the foot lands where it did relative to the root last time) and fits that
    // spot onto a step. Re-run every swing frame so the prediction converges; `snap` sets the
    // target outright instead of moving it at the retarget speeds.
    void RefineLanding(Leg leg, bool snap)
    {
        float remaining = Mathf.Max(0.0f, leg.SwingDuration - (Time.time - leg.SwingStart));
        Vector3 predicted = transform.position + velocity * remaining + transform.rotation * leg.TouchdownLocal;
        float along = Vector3.Dot(predicted - profileOrigin, profileForward);
        Vector3 offset = profileForward * Mathf.Clamp(FitOnStep(along) - along, -maxLandingAdjust, maxLandingAdjust);
        float height = ProbeFoot(predicted + offset, out leg.ToNormal);

        float dt = Time.deltaTime;
        leg.ToOffset = snap ? offset : Vector3.MoveTowards(leg.ToOffset, offset, retargetSpeed * dt);
        leg.ToY = snap ? height : Mathf.MoveTowards(leg.ToY, height, retargetHeightSpeed * dt);
    }

    void Land(Leg leg, Vector3 animated)
    {
        // Learn this leg's timing and landing spot from the animation for the next prediction.
        float swing = Time.time - leg.SwingStart;
        if (swing > 0.05f) leg.SwingDuration = Mathf.Lerp(leg.SwingDuration, swing, 0.5f);
        leg.TouchdownLocal = Quaternion.Inverse(transform.rotation) * (animated - transform.position);
        leg.TouchdownLocal.y = 0.0f;

        leg.Offset = leg.ToOffset;
        // If the prediction was off and the foot came down across an edge, settle it onto the
        // tread, then lock it there for the stance.
        Vector3 foot = animated + leg.Offset;
        float along = Vector3.Dot(foot - profileOrigin, profileForward);
        float settle = Mathf.Clamp(FitOnStep(along) - along, -heelLength - toeLength, heelLength + toeLength);
        leg.Offset += profileForward * settle;
        leg.LockPosition = animated + leg.Offset;
        leg.PlantedY = ProbeFoot(leg.LockPosition, out leg.Normal);
    }

    // Distance along the profile to put the ankle so the heel and toe fit between step edges.
    float FitOnStep(float along)
    {
        float behind = float.NegativeInfinity, ahead = float.PositiveInfinity;
        foreach (float edge in edges)
        {
            if (edge <= along) behind = Mathf.Max(behind, edge);
            else ahead = Mathf.Min(ahead, edge);
        }
        const float margin = 0.03f;
        float min = behind + heelLength + margin;
        float max = ahead - toeLength - margin;
        if (min > max) return (behind + ahead) * 0.5f; // tread shorter than the foot: centre it
        return Mathf.Clamp(along, min, max);
    }

    void ApplyLeg(Leg leg, float rootY)
    {
        Vector3 animated = animator.GetIKPosition(leg.Goal);
        // Keep the animated lift, measured from this leg's ground, and shift onto its landing spot.
        Vector3 target = animated + leg.Offset + Vector3.up * (leg.GroundY - rootY);
        animator.SetIKPositionWeight(leg.Goal, weight);
        animator.SetIKPosition(leg.Goal, target);

        if (alignToSurface)
        {
            // Tilt only planted feet; a swinging foot keeps its animated rotation.
            leg.Align = Mathf.MoveTowards(leg.Align, leg.Planted ? 1.0f : 0.0f, Time.deltaTime * 10.0f);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, leg.Normal);
            animator.SetIKRotationWeight(leg.Goal, weight * leg.Align);
            animator.SetIKRotation(leg.Goal, tilt * animator.GetIKRotation(leg.Goal));
        }
        leg.Target = target;
    }

    // Ground height for a foot with its ankle at `ankle`. On one plane (flat or a slope) that's the
    // ground under the ankle; across a step edge it's the highest of heel, ankle and toe, so the
    // foot never sinks into a step it overhangs.
    float ProbeFoot(Vector3 ankle, out Vector3 normal)
    {
        bool heel = CastDown(ankle - profileForward * heelLength, out RaycastHit heelHit);
        bool mid = CastDown(ankle, out RaycastHit midHit);
        bool toe = CastDown(ankle + profileForward * toeLength, out RaycastHit toeHit);
        if (heel && mid && toe)
        {
            float expected = Mathf.Lerp(heelHit.point.y, toeHit.point.y, heelLength / (heelLength + toeLength));
            if (Mathf.Abs(midHit.point.y - expected) < 0.01f)
            {
                normal = midHit.normal;
                return midHit.point.y;
            }
        }

        float best = float.NegativeInfinity;
        normal = Vector3.up;
        if (heel && heelHit.point.y > best) { best = heelHit.point.y; normal = heelHit.normal; }
        if (mid && midHit.point.y > best) { best = midHit.point.y; normal = midHit.normal; }
        if (toe && toeHit.point.y > best) { best = toeHit.point.y; normal = toeHit.normal; }
        return float.IsNegativeInfinity(best) ? transform.position.y : best;
    }

    bool CastDown(Vector3 point, out RaycastHit hit)
    {
        float rootY = transform.position.y;
        Vector3 origin = new Vector3(point.x, rootY + maxStepUp, point.z);
        return Physics.Raycast(origin, Vector3.down, out hit, maxStepUp + maxStepDown, ground, QueryTriggerInteraction.Ignore);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || weight <= 0.0f) return;
        Gizmos.color = Color.red;
        foreach (float edge in edges)
        {
            Vector3 p = profileOrigin + profileForward * edge;
            Gizmos.DrawLine(p + Vector3.down * 0.5f, p + Vector3.up * 0.5f);
        }
        foreach (Leg leg in new[] { left, right })
        {
            Gizmos.color = leg.Planted ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(leg.Target, 0.04f);
        }
    }

    sealed class Leg
    {
        public readonly AvatarIKGoal Goal;
        public bool Planted;
        // Smoothed world height of the ground this leg is standing on or heading for.
        public float GroundY;
        public float Velocity;
        public float PlantedY;
        public Vector3 Normal = Vector3.up;
        // Horizontal world offset from the animated foot to where it is placed.
        public Vector3 Offset;
        public float SwingStart;
        public float SwingDuration = 0.4f;
        public float FromY, ToY;
        public Vector3 FromOffset, ToOffset;
        public Vector3 ToNormal = Vector3.up;
        // Where the animation puts this foot down relative to the root, learned at each landing.
        public Vector3 TouchdownLocal = new Vector3(0.0f, 0.0f, 0.3f);
        public float Align;
        public Vector3 Target;
        public Vector3 PreviousAnimated;
        // World position a planted foot is held at.
        public Vector3 LockPosition;

        public Leg(AvatarIKGoal goal) => Goal = goal;

        public void Reset(FootPlacement owner)
        {
            Vector3 animated = owner.animator.GetIKPosition(Goal);
            PreviousAnimated = animated;
            LockPosition = animated;
            GroundY = PlantedY = owner.ProbeFoot(animated, out Normal);
            Velocity = 0.0f;
            Offset = FromOffset = ToOffset = Vector3.zero;
            Planted = true;
            Align = 0.0f;
        }
    }
}
