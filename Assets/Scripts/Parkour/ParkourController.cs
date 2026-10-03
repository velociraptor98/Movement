using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Turns the scanner's view of the obstacle ahead into a procedural parkour move and plays it:
// the root follows the plan's trajectory with the CharacterController off, and the plan's IK
// targets bend the locomotion animation onto the obstacle. Press Jump to perform the move
// shown in Available.
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

    PlayerController player;
    LocomotionController locomotion;
    EnvScanner scanner;
    CharacterController controller;
    Animator animator;
    InputAction jumpAction;
    readonly ParkourContext context = new ParkourContext();
    float elapsed;

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
    }

    void Update()
    {
        if (Current == null)
        {
            Available = player.IsGrounded ? PlanMove() : null;
            if (Available != null && jumpAction.WasPressedThisFrame()) Begin(Available);
            return;
        }

        elapsed += Time.deltaTime;
        transform.position = Current.SampleRoot(elapsed);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Current.Facing, turnSpeed * Time.deltaTime);
        animator.SetFloat(SpeedParam, Current.LocomotionSpeed, 0.1f, Time.deltaTime);
        if (elapsed >= Current.Duration) End();
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
        elapsed = 0.0f;
        player.HasControl = false;
        controller.enabled = false;
    }

    void End()
    {
        transform.position = Current.SampleRoot(Current.Duration);
        controller.enabled = true;
        player.ResumeControl(Current.Facing, Current.ExitSpeed);
        Current = null;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (Current == null) return;

        foreach (LimbTarget limb in Current.Limbs)
        {
            float weight = limb.Weight(elapsed);
            if (weight <= 0.0f) continue;
            Vector3 position = limb.FollowRoot ? transform.position + Current.Facing * limb.Position : limb.Position;
            animator.SetIKPositionWeight(limb.Goal, weight);
            animator.SetIKPosition(limb.Goal, position);
        }

        Vector3 bodyOffset = Vector3.zero;
        foreach (BodyOffset offset in Current.BodyOffsets) bodyOffset += offset.Offset * offset.Weight(elapsed);
        animator.bodyPosition += bodyOffset;
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
