using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnvScanner : MonoBehaviour
{
    [SerializeField]private Vector3 offset = new Vector3(0.0f,0.4f,0.0f);
    [SerializeField] private float maxDist = 1.0f;
    [SerializeField] private float heightRay = 5.0f;
    // How far past the hit face the height ray starts, so it lands on the top surface
    // instead of grazing down the face plane.
    [SerializeField] private float heightRayInset = 0.05f;
    [SerializeField] private LayerMask layer;

    [Header("Obstacle analysis (parkour)")]
    // Obstacles deeper than this are treated as solid ground to climb onto.
    [SerializeField] private float maxDepthScan = 2.5f;
    // How far past the far side of an obstacle to look for somewhere to land.
    [SerializeField] private float landingDistance = 0.8f;
    // Surfaces that can be landed on beyond an obstacle.
    [SerializeField] private LayerMask landingLayers;

    public ObstacleDataContainer ObstacleCheck()
    {
        ObstacleDataContainer container = new ObstacleDataContainer();
        container.forwardHitFound = Physics.Raycast(transform.position + offset, transform.forward, out container.forwardHit, maxDist,layer);
        Debug.DrawRay(transform.position + offset, transform.forward * maxDist,(container.forwardHitFound) ? Color.red : Color.white);
        if (container.forwardHitFound)
        {
            Vector3 heightOrigin = container.forwardHit.point - container.forwardHit.normal * heightRayInset + Vector3.up * heightRay;
            // Obstacles taller than heightRay start the ray inside the collider and report no top.
            container.upHitFound = Physics.Raycast(heightOrigin,Vector3.down, out container.heightHit,heightRay, layer);
            if (container.upHitFound)
            {
                container.height = container.heightHit.point.y - transform.position.y;
            }
            Debug.DrawRay(heightOrigin, Vector3.down * heightRay, (container.upHitFound) ? Color.red : Color.white);
        }
        return container;
    }

    // Measures what parkour actions need beyond the per-frame scan: obstacle depth, a landing
    // spot on the far side, and the geometry of the wall and ledge. Only call this on demand.
    public bool TryAnalyze(in ObstacleDataContainer obstacle, out ObstacleProfile profile)
    {
        profile = default;
        if (!obstacle.forwardHitFound || !obstacle.upHitFound) return false;

        Vector3 inward = Vector3.ProjectOnPlane(-obstacle.forwardHit.normal, Vector3.up);
        if (inward.sqrMagnitude < 0.01f) return false; // hit a floor or ceiling, not a wall
        inward.Normalize();

        Vector3 feet = transform.position;
        Vector3 wallPoint = obstacle.forwardHit.point;
        profile.Inward = inward;
        profile.WallBase = new Vector3(wallPoint.x, feet.y, wallPoint.z);
        profile.LedgePoint = obstacle.heightHit.point;
        profile.Height = obstacle.height;
        profile.Distance = Vector3.Dot(profile.WallBase - feet, inward);

        // Depth: cast back toward the wall from beyond it, just under the top. If the ray
        // starts inside the obstacle it finds nothing, meaning the obstacle is deep.
        Vector3 farOrigin = wallPoint + inward * maxDepthScan;
        farOrigin.y = profile.LedgePoint.y - 0.05f;
        if (Physics.Raycast(farOrigin, -inward, out RaycastHit farHit, maxDepthScan, layer, QueryTriggerInteraction.Ignore)
            && farHit.collider == obstacle.forwardHit.collider)
        {
            profile.Depth = Vector3.Dot(farHit.point - wallPoint, inward);

            Vector3 landingOrigin = wallPoint + inward * (profile.Depth + landingDistance);
            landingOrigin.y = profile.LedgePoint.y + 0.5f;
            profile.HasLanding = Physics.Raycast(landingOrigin, Vector3.down, out RaycastHit landingHit,
                heightRay + 5.0f, landingLayers, QueryTriggerInteraction.Ignore);
            profile.LandingPoint = landingHit.point;
        }
        else
        {
            profile.Depth = float.PositiveInfinity;
        }
        return true;
    }
}

public struct ObstacleDataContainer
{
    public bool forwardHitFound;
    public bool upHitFound;
    public RaycastHit forwardHit;
    public RaycastHit heightHit;
    // Height of the obstacle's top above the character's feet; valid when upHitFound.
    public float height;
}

// Geometry of an obstacle in front of the character, from EnvScanner.TryAnalyze.
public struct ObstacleProfile
{
    // Horizontal direction from the character into the obstacle (the wall's inverted normal).
    public Vector3 Inward;
    // Point on the wall face at the character's foot height.
    public Vector3 WallBase;
    // Point on the top surface just behind the front edge.
    public Vector3 LedgePoint;
    // Height of the top above the character's feet.
    public float Height;
    // Horizontal distance from the character's feet to the wall.
    public float Distance;
    // Thickness along Inward; PositiveInfinity when deeper than the scan.
    public float Depth;
    // Whether there is a surface to land on beyond the obstacle (only checked for finite depth).
    public bool HasLanding;
    public Vector3 LandingPoint;
}
