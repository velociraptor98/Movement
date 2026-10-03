using System.Collections.Generic;
using UnityEngine;

// A procedural parkour move: a root trajectory plus IK targets, all on a timeline in seconds.
// Built by a ParkourAction from an ObstacleProfile and played back by ParkourController.
public sealed class ParkourPlan
{
    public ParkourAction Action;
    // Character facing for the whole move.
    public Quaternion Facing;
    // Value fed to the animator's speed parameter while the move plays.
    public float LocomotionSpeed;
    // Horizontal speed handed back to PlayerController when the move ends.
    public float ExitSpeed;

    readonly List<Vector3> positions = new List<Vector3>();
    readonly List<float> times = new List<float>();
    public readonly List<LimbTarget> Limbs = new List<LimbTarget>();
    public readonly List<BodyOffset> BodyOffsets = new List<BodyOffset>();

    public float Duration => times.Count > 0 ? times[times.Count - 1] : 0.0f;
    public int KeyCount => positions.Count;
    public Vector3 KeyPosition(int index) => positions[index];
    public float KeyTime(int index) => times[index];

    // Appends a root position reached `seconds` after the previous key (the first key is t = 0).
    // Returns the key's time on the plan's timeline.
    public float AddKey(Vector3 position, float seconds)
    {
        float time = times.Count == 0 ? 0.0f : Duration + Mathf.Max(0.0f, seconds);
        positions.Add(position);
        times.Add(time);
        return time;
    }

    // Root position at `time`, Catmull-Rom interpolated through the keys.
    public Vector3 SampleRoot(float time)
    {
        if (positions.Count == 1 || time <= 0.0f) return positions[0];
        int last = positions.Count - 1;
        if (time >= times[last]) return positions[last];

        int i = 0;
        while (times[i + 1] < time) i++;
        float span = times[i + 1] - times[i];
        float u = span > 0.0f ? (time - times[i]) / span : 1.0f;

        Vector3 p0 = positions[Mathf.Max(i - 1, 0)];
        Vector3 p1 = positions[i];
        Vector3 p2 = positions[i + 1];
        Vector3 p3 = positions[Mathf.Min(i + 2, last)];
        return 0.5f * (2.0f * p1 + (p2 - p0) * u + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * u * u
                       + (3.0f * p1 - p0 - 3.0f * p2 + p3) * u * u * u);
    }
}

// An IK goal held over a time window, blending in and out at the edges.
public struct LimbTarget
{
    public AvatarIKGoal Goal;
    // World position, or an offset in the plan's facing frame when FollowRoot is set.
    public Vector3 Position;
    public bool FollowRoot;
    public float Start;
    public float End;
    public float Blend;

    public float Weight(float time) => WindowWeight(time, Start, End, Blend);

    internal static float WindowWeight(float time, float start, float end, float blend)
    {
        if (time <= start || time >= end) return 0.0f;
        if (blend <= 0.0f) return 1.0f;
        return Mathf.SmoothStep(0.0f, 1.0f, Mathf.Min(time - start, end - time) / blend);
    }
}

// Offset applied to the hips (Animator.bodyPosition) over a window, e.g. a tuck or pull-up crouch.
public struct BodyOffset
{
    public Vector3 Offset;
    public float Start;
    public float End;
    public float Blend;

    public float Weight(float time) => LimbTarget.WindowWeight(time, Start, End, Blend);
}
