using UnityEngine;

// A parkour move that can be planned procedurally from an obstacle's measurements.
// Subclasses decide whether they apply and build the trajectory; ParkourController tries
// its actions in order and runs the first plan it gets.
public abstract class ParkourAction : ScriptableObject
{
    [SerializeField] private string displayName;
    [Tooltip("Obstacle height range (top above the character's feet) this action handles.")]
    [SerializeField] protected float minHeight;
    [SerializeField] protected float maxHeight;
    [Tooltip("Furthest the character can be from the wall when the action starts.")]
    [SerializeField] protected float maxStartDistance = 1.5f;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    public bool TryPlan(in ObstacleProfile obstacle, ParkourContext context, out ParkourPlan plan)
    {
        plan = null;
        if (obstacle.Height < minHeight || obstacle.Height > maxHeight) return false;
        if (obstacle.Distance > maxStartDistance) return false;
        plan = new ParkourPlan { Action = this, Facing = Quaternion.LookRotation(obstacle.Inward) };
        plan.AddKey(context.Position, 0.0f);
        if (BuildPlan(obstacle, context, plan)) return true;
        plan = null;
        return false;
    }

    // Fills in the plan after its first key (the character's current position), or returns
    // false if this obstacle doesn't suit the action (e.g. no room on top, nowhere to land).
    protected abstract bool BuildPlan(in ObstacleProfile obstacle, ParkourContext context, ParkourPlan plan);

    // Moves the root to `distanceFromWall` in front of the wall at `speed`, unless the character
    // is already that close. Returns the time the character is in position.
    protected static float AddApproach(ParkourPlan plan, in ObstacleProfile obstacle, ParkourContext context,
        float distanceFromWall, float speed)
    {
        if (obstacle.Distance <= distanceFromWall + 0.05f) return plan.Duration;
        Vector3 target = obstacle.WallBase - obstacle.Inward * distanceFromWall;
        return plan.AddKey(target, Vector3.Distance(context.Position, target) / Mathf.Max(speed, 0.1f));
    }

    // Point on the front edge of the top surface, in line with where the scan hit the wall.
    protected static Vector3 Edge(in ObstacleProfile obstacle) =>
        new Vector3(obstacle.WallBase.x, obstacle.LedgePoint.y, obstacle.WallBase.z);

    protected static Vector3 Right(in ObstacleProfile obstacle) => Vector3.Cross(Vector3.up, obstacle.Inward);

    // Height of the ankle (the foot IK goal) above the sole.
    protected const float AnkleHeight = 0.1f;
}

// What an action needs to know about the character while planning.
public sealed class ParkourContext
{
    public Vector3 Position;
    public float Speed;
    public float Radius;
    public float Height;
    // Geometry the character's capsule must not overlap.
    public LayerMask Solid;

    // True if the character's capsule fits standing with its feet at `feet`.
    public bool HasClearance(Vector3 feet)
    {
        const float lift = 0.05f; // ignore the surface being stood on
        Vector3 bottom = feet + Vector3.up * (Radius + lift);
        Vector3 top = feet + Vector3.up * (Height - Radius);
        return !Physics.CheckCapsule(bottom, top, Radius, Solid, QueryTriggerInteraction.Ignore);
    }
}
