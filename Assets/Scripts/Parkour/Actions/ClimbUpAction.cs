using UnityEngine;

// Climbs onto an obstacle: the body reaches up, grabs the ledge, pulls up and mantles over onto a
// knee before standing. Ledges above `reachHeight` add a jump to a hang (feet braced on the wall)
// before the pull-up.
[CreateAssetMenu(menuName = "Movement/Parkour/Climb Up", fileName = "Climb Up")]
public class ClimbUpAction : ParkourAction
{
    [Tooltip("Shallowest top surface that can be stood on.")]
    [SerializeField] float minTopDepth = 0.5f;
    [Tooltip("Distance of the feet from the wall while climbing.")]
    [SerializeField] float wallDistance = 0.35f;
    [Tooltip("Hand height above the feet with the arms raised. Ledges higher than this need a jump.")]
    [SerializeField] float reachHeight = 2.0f;
    [SerializeField] float approachSpeed = 2.0f;
    [SerializeField] float jumpTime = 0.35f;
    [Tooltip("Pause while hanging before the pull-up.")]
    [SerializeField] float hangTime = 0.25f;
    [SerializeField] float pullTime = 0.8f;
    [Tooltip("How far onto the top the character ends up.")]
    [SerializeField] float standInset = 0.45f;
    [Tooltip("Extra hip drop during the pull, on top of the poses.")]
    [SerializeField] float pullCrouch = 0.0f;

    public ClimbUpAction()
    {
        minHeight = 0.75f;
        maxHeight = 2.8f;
        maxStartDistance = 1.2f;
    }

    protected override bool BuildPlan(in ObstacleProfile obstacle, ParkourContext context, ParkourPlan plan)
    {
        if (obstacle.Depth < minTopDepth) return false;
        Vector3 edge = Edge(obstacle);
        Vector3 stand = edge + obstacle.Inward * Mathf.Min(standInset, obstacle.Depth * 0.5f);
        if (!context.HasClearance(stand)) return false;

        plan.LocomotionSpeed = 0.0f;
        plan.ExitSpeed = 0.0f;

        float atWall = AddApproach(plan, obstacle, context, wallDistance, approachSpeed);
        Vector3 wallFeet = plan.KeyPosition(plan.KeyCount - 1);

        float hangFeetHeight = edge.y - reachHeight;
        bool hangs = hangFeetHeight > context.Position.y + 0.05f;
        float hung = atWall;
        if (hangs)
        {
            Vector3 hang = new Vector3(wallFeet.x, hangFeetHeight, wallFeet.z);
            hung = plan.AddKey(hang, jumpTime);
            if (hangTime > 0.0f) hung = plan.AddKey(hang, hangTime);
        }
        float overEdge = plan.AddKey(edge + obstacle.Inward * 0.15f + Vector3.up * 0.1f, pullTime * 0.7f);
        float end = plan.AddKey(stand, pullTime * 0.3f);

        Vector3 right = Right(obstacle);
        Vector3 grip = edge + obstacle.Inward * 0.05f + Vector3.up * 0.05f;
        foreach (var (goal, side) in new[] { (AvatarIKGoal.LeftHand, -1.0f), (AvatarIKGoal.RightHand, 1.0f) })
        {
            plan.Limbs.Add(new LimbTarget
            {
                Goal = goal, Position = grip + right * (0.2f * side),
                Start = atWall - 0.1f, End = overEdge + 0.1f, Blend = 0.12f,
            });
        }
        if (hangs)
        {
            // Feet brace against the wall face while hanging and through the start of the pull.
            Vector3 wall = obstacle.WallBase - obstacle.Inward * 0.12f;
            foreach (var (goal, side, rise) in new[] { (AvatarIKGoal.LeftFoot, -1.0f, 0.35f), (AvatarIKGoal.RightFoot, 1.0f, 0.55f) })
            {
                plan.Limbs.Add(new LimbTarget
                {
                    Goal = goal,
                    Position = new Vector3(wall.x, hangFeetHeight + rise, wall.z) + right * (0.12f * side),
                    Start = atWall + jumpTime * 0.5f, End = overEdge - pullTime * 0.3f, Blend = 0.1f,
                });
            }
        }
        if (pullCrouch > 0.0f)
        {
            plan.BodyOffsets.Add(new BodyOffset { Offset = Vector3.down * pullCrouch, Start = hung, End = end, Blend = 0.2f });
        }

        // Reach up, hang if the ledge is high, pull, mantle onto a knee, then crouch on top.
        plan.AddPose(ParkourPose.ClimbReach, Mathf.Max(0.0f, atWall - 0.25f), 0.2f);
        if (hangs) plan.AddPose(ParkourPose.ClimbHang, atWall + jumpTime * 0.6f, 0.15f);
        plan.AddPose(ParkourPose.ClimbPull, hung, 0.2f);
        plan.AddPose(ParkourPose.ClimbMantle, Mathf.Lerp(hung, overEdge, 0.6f), 0.2f);
        plan.AddPose(ParkourPose.Crouch, overEdge, 0.2f);
        // Eyes on the ledge until over it, then on the way ahead.
        plan.Looks.Add(new LookTarget { Position = grip, Start = -1.0f, End = overEdge, Blend = 0.2f });
        plan.Looks.Add(new LookTarget { Position = stand + obstacle.Inward * 3.0f + Vector3.up * 1.5f, Start = overEdge - 0.1f, End = end + 1.0f, Blend = 0.2f });
        return true;
    }
}
