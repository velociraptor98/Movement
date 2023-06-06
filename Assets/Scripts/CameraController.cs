using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] Transform followTarget;
    [SerializeField] float offsetValue = 5.0f;
    float rotationY;
    float rotationX;
    [SerializeField] float minVerticalAngle = -30.0f;
    [SerializeField] float maxVerticalAngle = 45.0f;
    [SerializeField] Vector3 charOffset;
    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

  
    void Update()
    {
        rotationY += Input.GetAxis("Mouse X");
        rotationX += Input.GetAxis("Mouse Y");
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
        Quaternion targetRotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 focusPos = followTarget.position + charOffset;
        transform.position = focusPos - targetRotation * new Vector3(0.0f, 0.0f, offsetValue);
        transform.rotation = targetRotation;
    }
}
