using UnityEngine;

// Steps up onto an obstacle too tall for the CharacterController's step offset: the lead foot
// plants on the edge and the body rises over it without breaking stride.
[CreateAssetMenu(menuName = "Movement/Parkour/Step Up", fileName = "Step Up")]
public class StepUpAction : ParkourAction
{
    [Tooltip("Shallowest top surface that can be stood on.")]
    [SerializeField] float minTopDepth = 0.4f;
    [Tooltip("Distance from the wall where the lead foot leaves the ground.")]
    [SerializeField] float plantDistance = 0.3f;
    [Tooltip("How far onto the top the character ends up.")]
    [SerializeField] float standInset = 0.45f;
    [SerializeField] float minSpeed = 1.5f;
    [Tooltip("Base time to rise onto the top; taller steps add to it.")]
    [SerializeField] float riseTime = 0.25f;
    [SerializeField] float hipDrop = 0.05f;

    public StepUpAction()
    {
        minHeight = 0.25f;
        maxHeight = 0.75f;
        maxStartDistance = 1.5f;
    }

    protected override bool BuildPlan(in ObstacleProfile obstacle, ParkourContext context, ParkourPlan plan)
    {
        if (obstacle.Depth < minTopDepth) return false;
        Vector3 edge = Edge(obstacle);
        Vector3 stand = edge + obstacle.Inward * Mathf.Min(standInset, obstacle.Depth * 0.5f);
        if (!context.HasClearance(stand)) return false;

        float speed = Mathf.Max(context.Speed, minSpeed);
        plan.LocomotionSpeed = speed;
        plan.ExitSpeed = context.Speed;

        float plant = AddApproach(plan, obstacle, context, plantDistance, speed);
        float over = plan.AddKey(edge + obstacle.Inward * 0.15f + Vector3.up * 0.08f, riseTime + obstacle.Height * 0.3f);
        float end = plan.AddKey(stand, Vector3.Distance(edge, stand) / speed);

        plan.Limbs.Add(new LimbTarget
        {
            Goal = AvatarIKGoal.RightFoot,
            Position = edge + obstacle.Inward * 0.12f + Right(obstacle) * 0.1f + Vector3.up * AnkleHeight,
            Start = plant - 0.1f, End = over + 0.05f, Blend = 0.1f,
        });
        plan.BodyOffsets.Add(new BodyOffset { Offset = Vector3.down * hipDrop, Start = plant, End = end, Blend = 0.15f });

        // Drive the lead knee up onto the step, then hand back to the walk/run cycle.
        plan.AddPose(ParkourPose.StepUp, Mathf.Max(0.0f, plant - 0.1f), 0.12f);
        plan.AddPose(ParkourPose.None, over + 0.05f, Mathf.Max(0.1f, end - over));
        return true;
    }
}
