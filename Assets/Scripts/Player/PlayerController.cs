using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField]CameraController cameraController;
    [SerializeField] private float rotationSpeed = 500.0f;
    private Animator playerAnimator;
    private CharacterController controller;
    Quaternion targetRotation;
    [SerializeField] LayerMask ground;
    [SerializeField] Vector3 groundOffset;
    float ySpeed;

    void Awake()
    {
        cameraController = Camera.main.GetComponent<CameraController>();
        targetRotation = transform.rotation;
        playerAnimator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        Vector3 moveInput = new Vector3(horizontal, 0.0f, vertical).normalized;
        Vector3 moveDirection = cameraController.PlanarRotation * moveInput;
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
