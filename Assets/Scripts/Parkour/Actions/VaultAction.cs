using UnityEngine;

// Vaults over a thin obstacle: hands plant on the top, the feet tuck up over it and the
// character lands on the far side, keeping its speed.
[CreateAssetMenu(menuName = "Movement/Parkour/Vault", fileName = "Vault")]
public class VaultAction : ParkourAction
{
    [Tooltip("Thickest obstacle that can be vaulted.")]
    [SerializeField] float maxDepth = 1.2f;
    [Tooltip("Distance from the wall where the character leaves the ground.")]
    [SerializeField] float takeoffDistance = 0.8f;
    [Tooltip("Height of the feet above the top at the peak of the vault.")]
    [SerializeField] float clearance = 0.35f;
    [SerializeField] float minSpeed = 3.0f;
    [Tooltip("Furthest the landing may be below / above the takeoff.")]
    [SerializeField] float maxDrop = 1.5f;
    [SerializeField] float maxRise = 0.3f;
    [Tooltip("How high the feet tuck under the hips while passing over.")]
    [SerializeField] float tuckHeight = 0.35f;

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

        // Both hands push off the top, just behind the front edge.
        Vector3 handPlant = edge + obstacle.Inward * Mathf.Min(obstacle.Depth * 0.5f, 0.25f) + Vector3.up * 0.05f;
        Vector3 right = Right(obstacle);
        foreach (var (goal, side) in new[] { (AvatarIKGoal.LeftHand, -1.0f), (AvatarIKGoal.RightHand, 1.0f) })
        {
            plan.Limbs.Add(new LimbTarget
            {
                Goal = goal, Position = handPlant + right * (0.2f * side),
                Start = takeoff - 0.05f, End = peak + 0.08f, Blend = 0.08f,
            });
        }
        // Feet tuck up under the hips, following the root over the obstacle.
        foreach (var (goal, side) in new[] { (AvatarIKGoal.LeftFoot, -1.0f), (AvatarIKGoal.RightFoot, 1.0f) })
        {
            plan.Limbs.Add(new LimbTarget
            {
                Goal = goal, FollowRoot = true,
                Position = new Vector3(0.15f * side, tuckHeight + AnkleHeight, 0.1f),
                Start = takeoff, End = land - 0.05f, Blend = 0.1f,
            });
        }
        return true;
    }
}
