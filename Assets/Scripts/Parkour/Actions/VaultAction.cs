using UnityEngine;

// Speed vault over a thin obstacle: the left hand plants on the top, the body rolls over it while
// the legs swing through on the right, and the character lands on the right foot and runs on,
// keeping its speed. Animated by the "SpeedVault" motion clip, time-warped onto the trajectory.
[CreateAssetMenu(menuName = "Movement/Parkour/Vault", fileName = "Vault")]
public class VaultAction : ParkourAction
{
    [Tooltip("Thickest obstacle that can be vaulted.")]
    [SerializeField] float maxDepth = 1.2f;
    [Tooltip("Distance from the wall where the character leaves the ground.")]
    [SerializeField] float takeoffDistance = 0.8f;
    [Tooltip("Height of the feet above the top at the peak of the vault.")]
    [SerializeField] float clearance = 0.15f;
    [SerializeField] float minSpeed = 3.0f;
    [Tooltip("Furthest the landing may be below / above the takeoff.")]
    [SerializeField] float maxDrop = 1.5f;
    [SerializeField] float maxRise = 0.3f;
    [Tooltip("Parkour-layer state with the vault's motion clip; empty falls back to the key poses.")]
    [SerializeField] string motion = "SpeedVault";
    [Tooltip("Seconds after landing the motion clip keeps playing while blending back into the run.")]
    [SerializeField] float recoverTime = 0.2f;

    public VaultAction()
    {
        minHeight = 0.5f;
        maxHeight = 1.3f;
        maxStartDistance = 1.5f;
    }

    protected override bool BuildPlan(in ObstacleProfile obstacle, ParkourContext context, ParkourPlan plan)
    {
        if (obstacle.Depth > maxDepth || !obstacle.HasLanding) return false;
        float landingHeight = obstacle.LandingPoint.y - context.Position.y;
        if (landingHeight < -maxDrop || landingHeight > maxRise) return false;
        if (!context.HasClearance(obstacle.LandingPoint)) return false;

        float speed = Mathf.Max(context.Speed, minSpeed);
        plan.LocomotionSpeed = speed;
        plan.ExitSpeed = context.Speed;

        Vector3 edge = Edge(obstacle);
        Vector3 apex = edge + obstacle.Inward * (obstacle.Depth * 0.5f) + Vector3.up * clearance;
        float takeoff = AddApproach(plan, obstacle, context, takeoffDistance, speed);
        Vector3 takeoffPosition = plan.KeyPosition(plan.KeyCount - 1);
        float peak = plan.AddKey(apex, Vector3.Distance(takeoffPosition, apex) / speed + 0.05f);
        float land = plan.AddKey(obstacle.LandingPoint, Vector3.Distance(apex, obstacle.LandingPoint) / speed + 0.05f);

        // The left hand plants on the top just left of centre and holds until the body is over it.
        Vector3 handPlant = edge + obstacle.Inward * Mathf.Min(obstacle.Depth * 0.5f, 0.25f) + Vector3.up * 0.05f;
        Vector3 right = Right(obstacle);
        plan.Limbs.Add(new LimbTarget
        {
            Goal = AvatarIKGoal.LeftHand, Position = handPlant - right * 0.12f,
            Start = takeoff - 0.06f, End = Mathf.Lerp(peak, land, 0.3f), Blend = 0.08f,
        });

        if (!string.IsNullOrEmpty(motion))
        {
            // Line the clip's authored events up with this vault's: 0.25 hand plant, 0.55 over the top,
            // 0.85 landing, then the run-out while the move blends back into locomotion.
            plan.Motion = motion;
            plan.MotionMarkers.Add(new Vector2(0.0f, Mathf.Max(0.0f, 0.25f - takeoff * 0.8f)));
            plan.MotionMarkers.Add(new Vector2(takeoff, 0.25f));
            plan.MotionMarkers.Add(new Vector2(peak, 0.55f));
            plan.MotionMarkers.Add(new Vector2(land, 0.85f));
            plan.MotionMarkers.Add(new Vector2(land + recoverTime, 1.0f));
            plan.ExitOnRightFoot = true;
        }
        else
        {
            // Key-pose fallback: lean in and reach, tuck over, absorb the landing.
            plan.AddPose(ParkourPose.VaultPlant, Mathf.Max(0.0f, takeoff - 0.2f), 0.15f);
            plan.AddPose(ParkourPose.VaultTuck, Mathf.Lerp(takeoff, peak, 0.5f), 0.15f);
            plan.AddPose(ParkourPose.VaultLand, Mathf.Lerp(peak, land, 0.5f), 0.15f);
        }
        // Eyes on the hand plant until the hands are down, then down the line to the landing. (Looking
        // back at the plant once the body is over it would pull the torso upright.)
        plan.Looks.Add(new LookTarget { Position = handPlant + obstacle.Inward * 0.3f, Start = -1.0f, End = takeoff + 0.05f, Blend = 0.1f });
        Vector3 ahead = obstacle.LandingPoint + obstacle.Inward * 1.5f + Vector3.up * 0.6f;
        plan.Looks.Add(new LookTarget { Position = ahead, Start = takeoff - 0.05f, End = land + 1.0f, Blend = 0.15f });
        return true;
    }
}
