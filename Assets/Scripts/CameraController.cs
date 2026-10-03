using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [SerializeField] Transform followTarget;
    [SerializeField] float offsetValue = 5.0f;
    float rotationY;
    float rotationX;
    [SerializeField] float minVerticalAngle = -10.0f;
    [SerializeField] float maxVerticalAngle = 45.0f;
    [SerializeField] Vector3 charOffset;
    // Pointer delta is in pixels per frame; 0.1 matches the legacy "Mouse X/Y" axis sensitivity.
    [SerializeField] float mouseSensitivity = 0.1f;
    // Stick input is a -1..1 rate, so it is scaled to degrees per second.
    [SerializeField] float stickLookSpeed = 180.0f;
    InputAction lookAction;

    void Awake()
    {
        lookAction = InputSystem.actions.FindAction("Player/Look", throwIfNotFound: true);
    }

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }


    void Update()
    {
        Vector2 look = lookAction.ReadValue<Vector2>();
        look *= lookAction.activeControl?.device is Pointer ? mouseSensitivity : stickLookSpeed * Time.deltaTime;
        rotationY += look.x;
        rotationX += look.y;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
        Quaternion targetRotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 focusPos = followTarget.position + charOffset;
        transform.position = focusPos - targetRotation * new Vector3(0.0f, 0.0f, offsetValue);
        transform.rotation = targetRotation;
    }

    public Quaternion PlanarRotation => Quaternion.Euler(0, rotationY, 0);
}
