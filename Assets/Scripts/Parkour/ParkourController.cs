using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Turns the scanner's view of the obstacle ahead into a procedural parkour move and plays it:
// the root follows the plan's trajectory with the CharacterController off, the "Parkour" animator
// layer blends the whole body through the plan's key poses, and the plan's IK targets put the
// hands and feet exactly on the obstacle while the head and chest look where they're going.
// Press Jump to perform the move shown in Available.
[RequireComponent(typeof(PlayerController), typeof(LocomotionController), typeof(EnvScanner))]
public class ParkourController : MonoBehaviour
{
    [Tooltip("Tried in order; the first action that can plan a move for the obstacle is used.")]
    [SerializeField] List<ParkourAction> actions = new List<ParkourAction>();
    [Tooltip("Largest angle between the character's facing and the wall for a move to start.")]
    [SerializeField] float maxApproachAngle = 50.0f;
    [SerializeField] float turnSpeed = 720.0f;
    [Tooltip("Geometry the character must have room to stand clear of when a move ends.")]
    [SerializeField] LayerMask solid;

    [Header("Animation")]
    [Tooltip("Animator layer holding the pose blend tree; its IK pass applies the move's IK.")]
    [SerializeField] string poseLayer = "Parkour";
    [Tooltip("Seconds to blend from the move's last pose back into locomotion after it ends.")]
    [SerializeField] float exitBlend = 0.2f;
    [SerializeField] float lookBodyWeight = 0.3f;
    [SerializeField] float lookHeadWeight = 0.9f;
    [Tooltip("How far elbows and knees are pushed out to the side / forward while their hand or foot is placed.")]
    [SerializeField] float elbowOut = 0.25f;
    [SerializeField] float kneeForward = 0.3f;
    [Tooltip("Normalised walk/run cycle time when each foot strikes (furthest forward), so locomotion " +
             "resumes in step with the foot a move lands on. Both clips strike at about the same phase.")]
    [SerializeField] float rightFootStrike = 0.72f;
    [SerializeField] float leftFootStrike = 0.22f;

    [Header("Secondary motion")]
    [Tooltip("The body hangs off the move's trajectory on a damped spring, so it lags takeoffs, " +
             "compresses into landings and leans with changes in speed.")]
    [SerializeField] float springFrequency = 2.5f;
    [SerializeField] [Range(0, 1)] float springDamping = 0.45f;
    [Tooltip("Hip drop per m/s² of upward acceleration, before the spring.")]
    [SerializeField] float dipGain = 1.0f;
    [Tooltip("Forward lean (degrees) per m/s² of deceleration, before the spring.")]
    [SerializeField] float leanGain = 150.0f;
    [SerializeField] float maxDip = 0.08f;
    [SerializeField] float maxLean = 12.0f;

    PlayerController player;
    LocomotionController locomotion;
    EnvScanner scanner;
    CharacterController controller;
    Animator animator;
    InputAction jumpAction;
    readonly ParkourContext context = new ParkourContext();
    float elapsed;
    int poseLayerIndex = -1;
    int[] poseParams;
    readonly float[] poseWeights = new float[System.Enum.GetValues(typeof(ParkourPose)).Length];
    float layerWeight;
    float exitFade;
    // The move that just ended, while its animation blends back out.
    ParkourPlan exiting;
    int motionTimeParam;
    Vector3 lastPosition;
    Vector3 lastVelocity;
    float dip, dipVelocity, lean, leanVelocity;

    static readonly int SpeedParam = Animator.StringToHash("speed");

    // The move Jump would perform right now, if any.
    public ParkourPlan Available { get; private set; }
    // The move being performed, if any.
    public ParkourPlan Current { get; private set; }
    public bool InAction => Current != null;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        locomotion = GetComponent<LocomotionController>();
        scanner = GetComponent<EnvScanner>();
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        jumpAction = InputSystem.actions.FindAction("Player/Jump", throwIfNotFound: true);

        poseLayerIndex = animator.GetLayerIndex(poseLayer);
        if (poseLayerIndex < 0) Debug.LogWarning($"[Parkour] Animator has no '{poseLayer}' layer; moves will play without poses.", this);
        string[] names = System.Enum.GetNames(typeof(ParkourPose));
        poseParams = new int[names.Length];
        for (int i = 0; i < names.Length; i++) poseParams[i] = Animator.StringToHash("Pose" + names[i]);
        motionTimeParam = Animator.StringToHash("ParkourMotionTime");
    }

    void Update()
    {
        if (Current == null)
        {
            // Blend the last move's animation back out into locomotion, letting a motion clip finish.
            if (exitFade > 0.0f)
            {
                elapsed += Time.deltaTime;
                exitFade = Mathf.Max(0.0f, exitFade - Time.deltaTime / Mathf.Max(exitBlend, 0.01f));
                if (exiting?.Motion != null) animator.SetFloat(motionTimeParam, exiting.MotionTime(elapsed));
                ApplyPoses(layerWeight * exitFade);
                UpdateSprings();
                if (exitFade <= 0.0f) exiting = null;
            }
            Available = player.IsGrounded ? PlanMove() : null;
            if (Available != null && jumpAction.WasPressedThisFrame()) Begin(Available);
            return;
        }

        elapsed += Time.deltaTime;
        transform.position = Current.SampleRoot(elapsed);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Current.Facing, turnSpeed * Time.deltaTime);
        animator.SetFloat(SpeedParam, Current.LocomotionSpeed, 0.1f, Time.deltaTime);

        if (Current.Motion != null)
        {
            layerWeight = Mathf.SmoothStep(0.0f, 1.0f, elapsed / Mathf.Max(Current.MotionBlendIn, 0.01f));
            animator.SetFloat(motionTimeParam, Current.MotionTime(elapsed));
        }
        else
        {
            Current.EvaluatePoses(elapsed, poseWeights);
            layerWeight = 1.0f - poseWeights[(int)ParkourPose.None];
        }
        ApplyPoses(layerWeight);
        UpdateSprings();
        if (elapsed >= Current.Duration) End();
    }

    // The body as a damped spring hanging off the root: it is pushed opposite the root's
    // acceleration (lagging a takeoff, compressing into a landing, leaning into a slow-down).
    void UpdateSprings()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.0f) return;
        Vector3 velocity = (transform.position - lastPosition) / dt;
        Vector3 acceleration = Vector3.ClampMagnitude((velocity - lastVelocity) / dt, 40.0f);
        lastPosition = transform.position;
        lastVelocity = velocity;

        Vector3 forward = transform.forward;
        float omega = 2.0f * Mathf.PI * springFrequency;
        Spring(ref dip, ref dipVelocity, -acceleration.y * dipGain, omega, dt);
        Spring(ref lean, ref leanVelocity, -Vector3.Dot(acceleration, forward) * leanGain, omega, dt);
        dip = Mathf.Clamp(dip, -maxDip, maxDip);
        lean = Mathf.Clamp(lean, -maxLean, maxLean);
    }

    void Spring(ref float x, ref float v, float force, float omega, float dt)
    {
        v += (force - omega * omega * x - 2.0f * springDamping * omega * v) * dt;
        x += v * dt;
    }

    // Sets the pose blend tree's weights (normalised to sum to 1 so the override layer never
    // scales the pose toward zero) and the layer's overall weight.
    void ApplyPoses(float weight)
    {
        if (poseLayerIndex < 0) return;
        float total = layerWeight > 0.0001f ? layerWeight : 1.0f;
        for (int i = 1; i < poseWeights.Length; i++) animator.SetFloat(poseParams[i], poseWeights[i] / total);
        animator.SetLayerWeight(poseLayerIndex, weight);
    }

    public ParkourPlan PlanMove()
    {
        ObstacleDataContainer obstacle = locomotion.CurrentObstacle;
        if (!obstacle.forwardHitFound) return null;
        Vector3 toWall = Vector3.ProjectOnPlane(-obstacle.forwardHit.normal, Vector3.up);
        if (Vector3.Angle(transform.forward, toWall) > maxApproachAngle) return null;
        if (!scanner.TryAnalyze(obstacle, out ObstacleProfile profile)) return null;

        context.Position = transform.position;
        context.Speed = player.CurrentSpeed;
        context.Radius = controller.radius;
        context.Height = controller.height;
        context.Solid = solid;
        foreach (ParkourAction action in actions)
        {
            if (action != null && action.TryPlan(profile, context, out ParkourPlan plan)) return plan;
        }
        return null;
    }

    void Begin(ParkourPlan plan)
    {
        Current = plan;
        Available = null;
        exiting = null;
        elapsed = 0.0f;
        player.HasControl = false;
        controller.enabled = false;

        lastPosition = transform.position;
        lastVelocity = transform.forward * player.CurrentSpeed;
        dip = dipVelocity = lean = leanVelocity = 0.0f;
        if (poseLayerIndex >= 0)
        {
            animator.Play(plan.Motion ?? "Poses", poseLayerIndex, 0.0f);
            if (plan.Motion != null) animator.SetFloat(motionTimeParam, plan.MotionTime(0.0f));
        }
    }

    void End()
    {
        transform.position = Current.SampleRoot(Current.Duration);
        controller.enabled = true;
        player.ResumeControl(Current.Facing, Current.ExitSpeed);

        // Restart the walk/run cycle on the foot the move landed on, so the first stride follows on.
        if (Current.ExitOnRightFoot.HasValue && Current.ExitSpeed > 0.1f)
        {
            int locomotionState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            animator.Play(locomotionState, 0, Current.ExitOnRightFoot.Value ? rightFootStrike : leftFootStrike);
            animator.SetFloat(SpeedParam, Current.ExitSpeed);
        }
        exitFade = layerWeight > 0.0f ? 1.0f : 0.0f;
        exiting = Current;
        Current = null;
    }

    void OnAnimatorIK(int layerIndex)
    {
        // Solve on the pose layer so the IK lands on top of the move's poses.
        if (layerIndex != Mathf.Max(poseLayerIndex, 0)) return;
        if (Current == null && exiting == null) return;

        // Secondary motion, through the move and its blend back out.
        animator.bodyPosition += Vector3.up * dip;
        animator.bodyRotation = Quaternion.AngleAxis(lean, transform.right) * animator.bodyRotation;
        if (Current == null) return;
        Vector3 forward = Current.Facing * Vector3.forward;

        foreach (LimbTarget limb in Current.Limbs)
        {
            float weight = limb.Weight(elapsed);
            if (weight <= 0.0f) continue;
            Vector3 position = limb.FollowRoot ? transform.position + Current.Facing * limb.Position : limb.Position;
            animator.SetIKPositionWeight(limb.Goal, weight);
            animator.SetIKPosition(limb.Goal, position);
            SetHint(limb.Goal, position, forward, weight);
        }

        // Turn the head and chest toward the strongest look target.
        float lookWeight = 0.0f;
        Vector3 lookAt = Vector3.zero;
        foreach (LookTarget look in Current.Looks)
        {
            float weight = look.Weight(elapsed);
            if (weight > lookWeight) { lookWeight = weight; lookAt = look.Position; }
        }
        if (lookWeight > 0.0f)
        {
            animator.SetLookAtWeight(lookWeight, lookBodyWeight, lookHeadWeight, 0.0f, 0.5f);
            animator.SetLookAtPosition(lookAt);
        }

        Vector3 bodyOffset = Vector3.zero;
        foreach (BodyOffset offset in Current.BodyOffsets) bodyOffset += offset.Offset * offset.Weight(elapsed);
        animator.bodyPosition += bodyOffset;
    }

    // Elbows bend out to the side and slightly back; knees bend forward toward the obstacle.
    void SetHint(AvatarIKGoal goal, Vector3 target, Vector3 forward, float weight)
    {
        AvatarIKHint hint;
        HumanBodyBones upper;
        Vector3 bend;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        switch (goal)
        {
            case AvatarIKGoal.LeftHand: hint = AvatarIKHint.LeftElbow; upper = HumanBodyBones.LeftUpperArm; bend = -side * elbowOut - forward * 0.1f; break;
            case AvatarIKGoal.RightHand: hint = AvatarIKHint.RightElbow; upper = HumanBodyBones.RightUpperArm; bend = side * elbowOut - forward * 0.1f; break;
            case AvatarIKGoal.LeftFoot: hint = AvatarIKHint.LeftKnee; upper = HumanBodyBones.LeftUpperLeg; bend = forward * kneeForward; break;
            default: hint = AvatarIKHint.RightKnee; upper = HumanBodyBones.RightUpperLeg; bend = forward * kneeForward; break;
        }
        Transform root = animator.GetBoneTransform(upper);
        if (root == null) return;
        animator.SetIKHintPositionWeight(hint, weight);
        animator.SetIKHintPosition(hint, Vector3.Lerp(root.position, target, 0.5f) + bend);
    }

    void OnDrawGizmosSelected()
    {
        ParkourPlan plan = Current ?? Available;
        if (plan == null) return;

        Gizmos.color = Color.cyan;
        Vector3 previous = plan.SampleRoot(0.0f);
        for (int i = 1; i <= 40; i++)
        {
            Vector3 point = plan.SampleRoot(plan.Duration * i / 40.0f);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
        Gizmos.color = Color.yellow;
        foreach (LimbTarget limb in plan.Limbs)
        {
            if (!limb.FollowRoot) Gizmos.DrawWireSphere(limb.Position, 0.05f);
        }
    }
}
