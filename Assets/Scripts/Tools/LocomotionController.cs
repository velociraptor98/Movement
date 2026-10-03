using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocomotionController : MonoBehaviour
{
    EnvScanner scanner;

    // Latest scan of what is in front of the character, for traversal decisions.
    public ObstacleDataContainer CurrentObstacle { get; private set; }

    private void Awake()
    {
        scanner = GetComponent<EnvScanner>();
    }
    private void Update()
    {
        CurrentObstacle = scanner.ObstacleCheck();
    }
}
