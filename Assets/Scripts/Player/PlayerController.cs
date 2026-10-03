using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    // Speeds match the planted-foot speed of the in-place Mixamo clips, so the blend
    // tree (thresholds in m/s) plays each clip at the speed it was authored for.
    [SerializeField] private float walkSpeed = 1.65f;
    [SerializeField] private float runSpeed = 5.3f;
    [SerializeField] private float acceleration = 10.0f;
    [SerializeField] private float rotationSpeed = 500.0f;
    // Driven by the CinemachineBrain; its yaw defines camera-relative movement.
    Transform cameraTransform;
    private Animator playerAnimator;
    private CharacterController controller;
    Quaternion targetRotation;
    [SerializeField] LayerMask ground;
    // How far below the capsule the ground may be and still count as grounded.
    // Large enough to stay grounded when walking down stairs.
    [SerializeField] float groundCheckDistance = 0.3f;
    // Downward speed applied while grounded to keep the capsule pressed to the ground.
    [SerializeField] float groundStickSpeed = 2.0f;
    float ySpeed;
    float currentSpeed;
    InputAction moveAction;
    InputAction sprintAction;

    static readonly int SpeedParam = Animator.StringToHash("speed");

    public float CurrentSpeed => currentSpeed;
    // False while another system (e.g. ParkourController) drives the character.
    public bool HasControl { get; set; } = true;

    // Replaces player input, so AI, cutscenes or tests can drive the same movement.
    bool inputOverridden;
    Vector2 overrideMove;
    bool overrideSprint;

    public void SetInputOverride(Vector2 move, bool sprint)
    {
        inputOverridden = true;
        overrideMove = move;
        overrideSprint = sprint;
    }

    public void ClearInputOverride() => inputOverridden = false;

    // Caps the target speed, e.g. FootPlacement slowing down on stairs.
    public float SpeedLimit { get; set; } = float.PositiveInfinity;
    public bool IsSprinting { get; private set; }
    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public float GroundAngle => Vector3.Angle(GroundNormal, Vector3.up);

    void Awake()
    {
        cameraTransform = Camera.main.transform;
        targetRotation = transform.rotation;
        playerAnimator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        moveAction = InputSystem.actions.FindAction("Player/Move", throwIfNotFound: true);
        sprintAction = InputSystem.actions.FindAction("Player/Sprint", throwIfNotFound: true);
    }

    // Hands control back after another system moved the character.
    public void ResumeControl(Quaternion facing, float speed)
    {
        targetRotation = facing;
        currentSpeed = speed;
        ySpeed = -groundStickSpeed;
        HasControl = true;
    }

    void Update()
    {
        if (!HasControl) return;

        Vector2 move = Vector2.ClampMagnitude(inputOverridden ? overrideMove : moveAction.ReadValue<Vector2>(), 1.0f);
        bool sprint = inputOverridden ? overrideSprint : sprintAction.IsPressed();
        Vector3 moveInput = new Vector3(move.x, 0.0f, move.y);
        Quaternion planarRotation = Quaternion.Euler(0.0f, cameraTransform.eulerAngles.y, 0.0f);
        Vector3 moveDirection = planarRotation * moveInput.normalized;

        // Analog input scales speed; sprint switches the top speed from walk to run.
        IsSprinting = sprint;
        float targetSpeed = Mathf.Min(move.magnitude * (sprint ? runSpeed : walkSpeed), SpeedLimit);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        CheckGround();
        Vector3 velocity = moveDirection * currentSpeed;
        if (IsGrounded)
        {
            // Follow the slope so walking downhill doesn't launch the character.
            velocity = Vector3.ProjectOnPlane(velocity, GroundNormal);
            ySpeed = -groundStickSpeed;
        }
        else
        {
            ySpeed += Physics.gravity.y * Time.deltaTime;
        }
        velocity.y += ySpeed;
        controller.Move(velocity * Time.deltaTime);

        if (moveInput.sqrMagnitude > 0)
        {
            targetRotation = Quaternion.LookRotation(moveDirection);
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        playerAnimator.SetFloat(SpeedParam, currentSpeed, 0.1f, Time.deltaTime);
    }

    void CheckGround()
    {
        // Cast the capsule's bottom sphere down from higher inside the capsule: a cast that
        // starts overlapping a collider ignores it, which happens on steep or uneven ground.
        float castLift = controller.radius;
        Vector3 bottomSphere = controller.center + Vector3.down * (controller.height * 0.5f - controller.radius);
        Vector3 origin = transform.TransformPoint(bottomSphere) + Vector3.up * castLift;
        float distance = castLift + controller.skinWidth + groundCheckDistance;
        bool hit = Physics.SphereCast(origin, controller.radius * 0.95f, Vector3.down, out RaycastHit groundHit,
            distance, ground, QueryTriggerInteraction.Ignore);

        Vector3 normal = Vector3.up;
        if (hit)
        {
            // On a step or ledge edge the sphere cast reports a blended edge normal. Use the
            // face just past the contact instead, so stairs read as flat rather than too steep.
            normal = groundHit.normal;
            Vector3 outward = Vector3.ProjectOnPlane(groundHit.point - origin, Vector3.up).normalized;
            Vector3 probe = groundHit.point + outward * 0.01f + Vector3.up * 0.05f;
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit surface, 0.1f, ground, QueryTriggerInteraction.Ignore))
            {
                normal = surface.normal;
            }
        }

        // Rising (e.g. after a future jump) never counts as grounded.
        IsGrounded = hit && ySpeed <= 0.0f && Vector3.Angle(normal, Vector3.up) <= controller.slopeLimit;
        GroundNormal = normal;
    }
}
