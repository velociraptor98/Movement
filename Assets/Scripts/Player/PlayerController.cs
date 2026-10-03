using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5.0f;
    // Driven by the CinemachineBrain; its yaw defines camera-relative movement.
    Transform cameraTransform;
    [SerializeField] private float rotationSpeed = 500.0f;
    private Animator playerAnimator;
    private CharacterController controller;
    Quaternion targetRotation;
    [SerializeField] LayerMask ground;
    [SerializeField] Vector3 groundOffset;
    float ySpeed;
    InputAction moveAction;

    void Awake()
    {
        cameraTransform = Camera.main.transform;
        targetRotation = transform.rotation;
        playerAnimator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        moveAction = InputSystem.actions.FindAction("Player/Move", throwIfNotFound: true);
    }

    void Update()
    {
        Vector2 move = moveAction.ReadValue<Vector2>();
        float horizontal = move.x;
        float vertical = move.y;
        Vector3 moveInput = new Vector3(horizontal, 0.0f, vertical).normalized;
        Quaternion planarRotation = Quaternion.Euler(0.0f, cameraTransform.eulerAngles.y, 0.0f);
        Vector3 moveDirection = planarRotation * moveInput;
        if (!isGround())
        {
            ySpeed += Physics.gravity.y * Time.deltaTime;
        }
        else
        {
            ySpeed = -0.5f;
        }
        Vector3 velocity = moveDirection * moveSpeed;
        velocity.y = ySpeed;
        controller.Move(velocity * Time.deltaTime);
        if (Mathf.Abs(horizontal) + Mathf.Abs(vertical) > 0)
        { 
            
            targetRotation = Quaternion.LookRotation(moveDirection);
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        // Clamp values so animations are selected correctly.
        playerAnimator.SetFloat("moveAmount", Mathf.Clamp01(Mathf.Abs(horizontal) + Mathf.Abs(vertical)),0.2f, Time.deltaTime);
    }

    bool isGround()
    {
       return Physics.CheckSphere(transform.TransformPoint(groundOffset),0.2f,ground);
    }
}
