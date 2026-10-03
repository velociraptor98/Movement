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
    // Key poses the body blends through, in time order. ParkourPose.None blends back to locomotion.
    public readonly List<PoseKey> Poses = new List<PoseKey>();
    // Where the head and chest turn to look, over time windows.
    public readonly List<LookTarget> Looks = new List<LookTarget>();

    // Optional animated clip for the whole move: the name of a state in the Parkour layer, played
    // instead of the pose keys, with its playback time warped onto the plan by MotionMarkers.
    public string Motion;
    // (plan seconds, motion normalised time) pairs in order; plan time may run past Duration into
    // the exit blend so the clip finishes as the move hands back to locomotion.
    public readonly List<Vector2> MotionMarkers = new List<Vector2>();
    public float MotionBlendIn = 0.12f;
    // Which foot the move lands on, so the walk/run cycle can resume in step. Null leaves it alone.
    public bool? ExitOnRightFoot;

    public float MotionTime(float time)
    {
        if (MotionMarkers.Count == 0) return 0.0f;
        if (time <= MotionMarkers[0].x) return MotionMarkers[0].y;
        for (int i = 1; i < MotionMarkers.Count; i++)
        {
            Vector2 a = MotionMarkers[i - 1], b = MotionMarkers[i];
            if (time <= b.x) return Mathf.Lerp(a.y, b.y, b.x > a.x ? (time - a.x) / (b.x - a.x) : 1.0f);
        }
        return MotionMarkers[MotionMarkers.Count - 1].y;
    }

    // Crossfades into a pose starting at `time`, over `blend` seconds.
    public void AddPose(ParkourPose pose, float time, float blend) =>
        Poses.Add(new PoseKey { Pose = pose, Time = time, Blend = blend });

    // Writes each pose's weight at `time` into `weights` (indexed by ParkourPose; they sum to 1,
    // with None meaning plain locomotion). Each key crossfades from whatever was blended before it.
    public void EvaluatePoses(float time, float[] weights)
    {
        System.Array.Clear(weights, 0, weights.Length);
        weights[(int)ParkourPose.None] = 1.0f;
        foreach (PoseKey key in Poses)
        {
            if (time <= key.Time) break;
            float blend = key.Blend > 0.0f ? Mathf.SmoothStep(0.0f, 1.0f, (time - key.Time) / key.Blend) : 1.0f;
            for (int i = 0; i < weights.Length; i++) weights[i] *= 1.0f - blend;
            weights[(int)key.Pose] += blend;
        }
    }

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

public struct PoseKey
{
    public ParkourPose Pose;
    public float Time;
    public float Blend;
}

// A point the head and chest turn toward over a window.
public struct LookTarget
{
    public Vector3 Position;
    public float Start;
    public float End;
    public float Blend;

    public float Weight(float time) => LimbTarget.WindowWeight(time, Start, End, Blend);
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
