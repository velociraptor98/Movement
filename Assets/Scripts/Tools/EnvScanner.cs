using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnvScanner : MonoBehaviour
{
    [SerializeField]private Vector3 offset = new Vector3(0.0f,0.4f,0.0f);
    [SerializeField] private float maxDist = 1.0f;
    [SerializeField] private float heightRay = 5.0f;
    [SerializeField] private LayerMask layer;
    public ObstacleDataContainer ObstacleCheck()
    {
        ObstacleDataContainer container = new ObstacleDataContainer();
        container.forwardHitFound = Physics.Raycast(transform.position + offset, transform.forward, out container.forwardHit, maxDist,layer);
        Debug.DrawRay(transform.position + offset, transform.forward * maxDist,(container.forwardHitFound) ? Color.red : Color.white);
        if (container.forwardHitFound)
        {
            Vector3 heightOrigin = container.forwardHit.point + Vector3.up * heightRay;
            Physics.Raycast(heightOrigin,Vector3.down, out container.heightHit,heightRay, layer);
            Debug.DrawRay(heightOrigin, Vector3.down * maxDist, (container.upHitFound) ? Color.red : Color.white);
        }
        return container;
    }
}

public struct ObstacleDataContainer
{
    public bool forwardHitFound;
    public bool upHitFound;
    public RaycastHit forwardHit;
    public RaycastHit heightHit;
}