using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField]CameraController cameraController;
    [SerializeField] private float rotationSpeed = 500.0f;
    private Animator playerAnimator;
    Quaternion targetRotation;
    void Awake()
    {
        cameraController = Camera.main.GetComponent<CameraController>();
        targetRotation = transform.rotation;
        playerAnimator = GetComponent<Animator>();
    }
    void Update()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        Vector3 moveInput = new Vector3(horizontal, 0.0f, vertical).normalized;
        Vector3 moveDirection = cameraController.PlanarRotation * moveInput;
        if (Mathf.Abs(horizontal) + Mathf.Abs(vertical) > 0)
        {
            transform.position += moveDirection * moveSpeed * Time.deltaTime;
            targetRotation = Quaternion.LookRotation(moveDirection);
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        // Clamp values so animations are selected correctly.
        playerAnimator.SetFloat("moveAmount", Mathf.Clamp01(Mathf.Abs(horizontal) + Mathf.Abs(vertical)),0.2f, Time.deltaTime);
    }
}
