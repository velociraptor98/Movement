using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocomotionController : MonoBehaviour
{
    EnvScanner scanner;
    private void Awake()
    {
        scanner = GetComponent<EnvScanner>();
    }
    private void Update()
    {
        scanner.ObstacleCheck();
    }
}
