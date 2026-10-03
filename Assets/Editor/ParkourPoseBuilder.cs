using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Generates the parkour key poses as humanoid AnimationClips (muscle space, so they work on any
// humanoid rig) and wires them into a "Parkour" layer of the player's AnimatorController: an
// override layer with its IK pass on, holding a Direct blend tree with one weight parameter per
// pose ("Pose" + ParkourPose name). ParkourController drives the weights and the layer weight.
//
// Each pose starts from the idle clip's first frame and overrides the muscles listed below.
// Muscle directions, checked on the Y Bot:
//   Spine / Chest Front-Back   + bends forward       Head / Neck Nod Down-Up  + looks up
//   Upper Leg Front-Back       - swings leg forward  Lower Leg Stretch        - bends the knee
//   Arm Down-Up                + raises the arm      Arm Front-Back           - brings arm forward
//   Forearm Stretch            - bends the elbow     Shoulder Down-Up         + shrugs
// "Height" is the body centre height (RootT.y; idle is 0.962) and "Pitch" tilts the whole body
// forward in degrees. The root is where the feet/contact point travels, so airborne poses (tuck,
// hang) sit lower relative to it.
//
// Moves can also be full animated clips ("motions"): a list of timed key poses written with smooth
// tangents into one clip, with each body part's keys shifted in time (arms lead, legs and head follow)
// so the parts overlap instead of all hitting each pose at once. Each motion gets a state in the
// Parkour layer whose playback is driven by the "ParkourMotionTime" parameter, so ParkourController
// can time-warp the clip onto the move's trajectory.
//
// To use authored clips instead (e.g. Mixamo vault/climb frames), replace a child motion of the
// "Poses" blend tree in the Parkour layer; rebuilding here would put the generated clip back.
public static class ParkourPoseBuilder
{
    const string ControllerPath = "Assets/Models/Animations/PlayerAnimController.controller";
    const string BaseClipPath = "Assets/Models/Animations/Breathing Idle.fbx";
    const string ClipFolder = "Assets/Animations/ParkourPoses";
    const string LayerName = "Parkour";
    const string StateName = "Poses";

    static readonly Dictionary<ParkourPose, string> Poses = new Dictionary<ParkourPose, string>
    {
        // Leaning in at takeoff, both arms reaching down to the top, lead knee lifting.
        [ParkourPose.VaultPlant] = "Height=0.88; Pitch=20; Spine Front-Back=0.5; Chest Front-Back=0.3; Head Nod Down-Up=0.5; Neck Nod Down-Up=0.2; " +
                                   "Both Arm Front-Back=-0.6; Both Arm Down-Up=-0.4; Both Forearm Stretch=1; " +
                                   "Left Upper Leg Front-Back=-0.4; Left Lower Leg Stretch=-0.4; Right Upper Leg Front-Back=0.3; Right Lower Leg Stretch=0.9",
        // Over the top: weight on straight arms, legs tucked up under the hips, eyes forward.
        [ParkourPose.VaultTuck] = "Height=0.34; Pitch=35; Spine Front-Back=0.6; Chest Front-Back=0.4; Head Nod Down-Up=0.5; Neck Nod Down-Up=0.3; " +
                                  "Both Arm Front-Back=-0.5; Both Arm Down-Up=-0.65; Both Forearm Stretch=1; Both Shoulder Down-Up=0.3; " +
                                  "Both Upper Leg Front-Back=-0.9; Both Lower Leg Stretch=-1",
        // Absorbing the landing in a staggered crouch, arms forward for balance.
        [ParkourPose.VaultLand] = "Height=0.86; Spine Front-Back=0.3; Chest Front-Back=0.2; Head Nod Down-Up=0.2; " +
                                  "Both Arm Front-Back=-0.5; Both Arm Down-Up=-0.1; Both Forearm Stretch=0.6; " +
                                  "Left Upper Leg Front-Back=-0.4; Left Lower Leg Stretch=-0.4; Right Upper Leg Front-Back=-0.15; Right Lower Leg Stretch=-0.3",
        // At the wall: both arms up toward the ledge, looking up, a knee loading for the jump.
        [ParkourPose.ClimbReach] = "Height=0.98; Spine Front-Back=-0.1; Head Nod Down-Up=0.5; Neck Nod Down-Up=0.3; " +
                                   "Both Arm Down-Up=0.9; Both Arm Front-Back=-0.6; Both Forearm Stretch=0.9; Both Shoulder Down-Up=0.5; " +
                                   "Left Upper Leg Front-Back=-0.4; Left Lower Leg Stretch=-0.5",
        // Hanging from the ledge on straight arms, shoulders up, knees bent to brace on the wall.
        [ParkourPose.ClimbHang] = "Height=1.0; Spine Front-Back=0.1; Head Nod Down-Up=0.4; " +
                                  "Both Arm Down-Up=1; Both Arm Front-Back=-0.6; Both Forearm Stretch=1; Both Shoulder Down-Up=0.7; " +
                                  "Both Upper Leg Front-Back=-0.4; Both Lower Leg Stretch=-0.6",
        // Pulling up: elbows bending, chest coming over the ledge, one knee driving up.
        [ParkourPose.ClimbPull] = "Height=0.9; Spine Front-Back=0.35; Chest Front-Back=0.2; Head Nod Down-Up=0.2; " +
                                  "Both Arm Down-Up=0.2; Both Arm Front-Back=-0.5; Both Forearm Stretch=-0.6; Both Shoulder Down-Up=0.2; " +
                                  "Left Upper Leg Front-Back=-0.9; Left Lower Leg Stretch=-1; Right Upper Leg Front-Back=0.1; Right Lower Leg Stretch=-0.3",
        // Mantling: pressing down on straight arms over the ledge, knee up onto the top.
        [ParkourPose.ClimbMantle] = "Height=0.6; Spine Front-Back=0.6; Chest Front-Back=0.3; Head Nod Down-Up=0.4; " +
                                    "Both Arm Down-Up=-0.5; Both Arm Front-Back=-0.2; Both Forearm Stretch=1; " +
                                    "Left Upper Leg Front-Back=-1; Left Lower Leg Stretch=-1; Right Upper Leg Front-Back=0.2; Right Lower Leg Stretch=-0.2",
        // Crouched on top, about to stand.
        [ParkourPose.Crouch] = "Height=0.6; Spine Front-Back=0.35; Chest Front-Back=0.2; Head Nod Down-Up=0.2; " +
                               "Both Arm Front-Back=-0.3; Both Arm Down-Up=-0.5; Both Forearm Stretch=0.6; " +
                               "Both Upper Leg Front-Back=-0.75; Both Lower Leg Stretch=-0.9",
        // Lead knee driving up onto a step, opposite arm forward.
        [ParkourPose.StepUp] = "Height=0.92; Spine Front-Back=0.25; Chest Front-Back=0.1; " +
                               "Right Upper Leg Front-Back=-0.7; Right Lower Leg Stretch=-0.7; Left Upper Leg Front-Back=0.1; " +
                               "Left Arm Front-Back=-0.4; Right Arm Front-Back=0.3",
    };

    public const string MotionTimeParameter = "ParkourMotionTime";

    // Normalised time markers the motions are authored against; actions map their own events onto
    // these (see VaultAction). Speed vault: 0.25 hand plant / takeoff, 0.55 over the top, 0.85 landing.
    static readonly Dictionary<string, (float time, string spec)[]> Motions = new Dictionary<string, (float, string)[]>
    {
        // Speed vault: left hand plants, the body rolls over it and the legs swing through on the
        // right, landing on the right foot and running out on the left.
        ["SpeedVault"] = new[]
        {
            // Last running stride before the obstacle.
            (0.00f, "Height=0.94; Spine Front-Back=0.2; Head Nod Down-Up=0.15; Left Arm Front-Back=0.3; Right Arm Front-Back=-0.4; Both Forearm Stretch=-0.4; " +
                    "Left Upper Leg Front-Back=-0.5; Left Lower Leg Stretch=-0.6; Right Upper Leg Front-Back=0.2; Right Lower Leg Stretch=-0.2"),
            // Loading into the takeoff: dropping the hips, arms drawn back.
            (0.14f, "Height=0.86; Spine Front-Back=0.35; Head Nod Down-Up=0.3; Both Arm Front-Back=0.2; Both Forearm Stretch=0.2; " +
                    "Right Upper Leg Front-Back=-0.2; Right Lower Leg Stretch=-0.5; Left Upper Leg Front-Back=0.2; Left Lower Leg Stretch=-0.4"),
            // Hand plant: left hand onto the top, right arm reaching, right knee driving, left leg pushing off.
            (0.25f, "Height=0.88; Pitch=20; Roll=5; Spine Front-Back=0.5; Chest Front-Back=0.3; Head Nod Down-Up=0.4; " +
                    "Left Arm Front-Back=-0.7; Left Arm Down-Up=-0.3; Left Forearm Stretch=1; Right Arm Front-Back=-0.6; Right Arm Down-Up=0; Right Forearm Stretch=0.7; " +
                    "Right Upper Leg Front-Back=-0.8; Right Lower Leg Stretch=-0.8; Left Upper Leg Front-Back=0.35; Left Lower Leg Stretch=0.9"),
            // Hips rising over the planted hand, legs lifting to the right side.
            (0.40f, "Height=0.42; Pitch=30; Roll=24; Yaw=-14; Spine Front-Back=0.6; Chest Front-Back=0.35; Spine Twist Left-Right=0.2; Spine Left-Right=-0.2; Head Nod Down-Up=0.45; " +
                    "Left Arm Down-Up=-0.55; Left Arm Front-Back=-0.4; Left Forearm Stretch=1; Left Shoulder Down-Up=0.4; Right Arm Down-Up=0.1; Right Arm Front-Back=-0.3; Right Forearm Stretch=0.6; " +
                    "Right Upper Leg Front-Back=-0.8; Right Upper Leg In-Out=0.4; Right Lower Leg Stretch=-0.6; Left Upper Leg Front-Back=-0.6; Left Upper Leg In-Out=0.2; Left Lower Leg Stretch=-1"),
            // Over the top: rolled onto the left hand, lead (right) leg extended out to the side, left leg tucked, free arm out for balance.
            (0.55f, "Height=0.36; Pitch=30; Roll=32; Yaw=-20; Spine Front-Back=0.55; Chest Front-Back=0.35; Spine Twist Left-Right=0.3; Spine Left-Right=-0.3; Chest Left-Right=-0.15; Head Nod Down-Up=0.5; " +
                    "Left Arm Down-Up=-0.65; Left Arm Front-Back=-0.35; Left Forearm Stretch=1; Left Shoulder Down-Up=0.5; Right Arm Down-Up=0.25; Right Arm Front-Back=-0.2; Right Forearm Stretch=0.5; " +
                    "Right Upper Leg Front-Back=-0.65; Right Upper Leg In-Out=0.5; Right Lower Leg Stretch=-0.25; Left Upper Leg Front-Back=-0.75; Left Upper Leg In-Out=0.15; Left Lower Leg Stretch=-0.95"),
            // Clearing: pushing off the hand, lead leg reaching down for the ground, torso coming upright.
            (0.70f, "Height=0.6; Pitch=15; Roll=14; Yaw=-6; Spine Front-Back=0.4; Spine Twist Left-Right=0.15; Spine Left-Right=-0.1; Head Nod Down-Up=0.3; " +
                    "Left Arm Down-Up=-0.4; Left Arm Front-Back=0.1; Left Forearm Stretch=0.9; Right Arm Down-Up=-0.2; Right Arm Front-Back=-0.5; Right Forearm Stretch=0.6; " +
                    "Right Upper Leg Front-Back=-0.5; Right Upper Leg In-Out=0.2; Right Lower Leg Stretch=-0.2; Left Upper Leg Front-Back=-0.4; Left Lower Leg Stretch=-0.8"),
            // Landing on the right foot, knee absorbing, arms countering the legs, left leg swinging through.
            (0.85f, "Height=0.88; Pitch=5; Spine Front-Back=0.35; Head Nod Down-Up=0.2; " +
                    "Left Arm Front-Back=-0.5; Left Arm Down-Up=-0.2; Left Forearm Stretch=0.5; Right Arm Front-Back=0.3; Right Arm Down-Up=-0.4; Right Forearm Stretch=0.5; " +
                    "Right Upper Leg Front-Back=-0.35; Right Lower Leg Stretch=-0.4; Left Upper Leg Front-Back=-0.05; Left Lower Leg Stretch=-0.7"),
            // Running out: left leg stepping through, arms back in the run swing.
            (1.00f, "Height=0.93; Spine Front-Back=0.22; Head Nod Down-Up=0.15; Left Arm Front-Back=0.3; Right Arm Front-Back=-0.4; Both Forearm Stretch=-0.4; " +
                    "Left Upper Leg Front-Back=-0.6; Left Lower Leg Stretch=-0.5; Right Upper Leg Front-Back=0.25; Right Lower Leg Stretch=-0.3"),
        },
    };

    // How far each body part's keys are shifted (in normalised time): arms lead, legs and head
    // follow, so a move flows through the body rather than snapping into each key pose at once.
    static float GroupOffset(string property)
    {
        if (property.Contains("Arm") || property.Contains("Shoulder") || property.Contains("Hand")) return -0.04f;
        if (property.Contains("Leg") || property.Contains("Foot") || property.Contains("Toes")) return 0.04f;
        if (property.Contains("Head") || property.Contains("Neck")) return 0.03f;
        return 0.0f;
    }

    // Humanoid IK-goal curves (e.g. LeftFootT.x) would contradict the edited muscles; leave them out.
    static readonly Regex IKGoalCurve = new Regex(@"^(Left|Right)(Foot|Hand)[TQ]\.");

    [MenuItem("Tools/Movement/Build Parkour Poses")]
    public static void Build()
    {
        var basePose = ReadBasePose();
        var clips = new Dictionary<ParkourPose, AnimationClip>();
        foreach (var pair in Poses) clips[pair.Key] = WriteClip(pair.Key, ApplySpec(basePose, pair.Value, pair.Key.ToString()));
        var motions = new Dictionary<string, AnimationClip>();
        foreach (var pair in Motions) motions[pair.Key] = WriteMotion(pair.Key, pair.Value, basePose);
        WireController(clips, motions);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ParkourPoseBuilder] Built {clips.Count} poses and {motions.Count} motions into the '{LayerName}' layer of {ControllerPath}");
    }

    static Dictionary<string, float> ReadBasePose()
    {
        AnimationClip idle = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(BaseClipPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__")) idle = clip;
        }
        if (idle == null) throw new System.InvalidOperationException($"No clip in {BaseClipPath}");

        var values = new Dictionary<string, float>();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(idle))
        {
            if (binding.type != typeof(Animator) || binding.path != "" || IKGoalCurve.IsMatch(binding.propertyName)) continue;
            values[binding.propertyName] = AnimationUtility.GetEditorCurve(idle, binding).Evaluate(0.0f);
        }
        return values;
    }

    static Dictionary<string, float> ApplySpec(Dictionary<string, float> basePose, string spec, string label)
    {
        var values = new Dictionary<string, float>(basePose)
        {
            ["RootT.x"] = 0.0f, ["RootT.z"] = 0.0f,
            ["RootQ.x"] = 0.0f, ["RootQ.y"] = 0.0f, ["RootQ.z"] = 0.0f, ["RootQ.w"] = 1.0f,
        };
        float pitch = 0.0f, yaw = 0.0f, roll = 0.0f;
        foreach (string entry in spec.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            string[] parts = entry.Split('=');
            string name = parts[0].Trim();
            float value = float.Parse(parts[1], CultureInfo.InvariantCulture);
            if (name == "Height") { values["RootT.y"] = value; continue; }
            if (name == "Pitch") { pitch = value; continue; }
            if (name == "Yaw") { yaw = value; continue; }
            if (name == "Roll") { roll = value; continue; }
            string[] muscles = name.StartsWith("Both ")
                ? new[] { "Left " + name.Substring(5), "Right " + name.Substring(5) }
                : new[] { name };
            foreach (string muscle in muscles)
            {
                if (System.Array.IndexOf(HumanTrait.MuscleName, muscle) < 0)
                    throw new System.ArgumentException($"{label}: unknown muscle '{muscle}'");
                values[muscle] = value;
            }
        }
        // Whole-body orientation: + pitch leans forward, + yaw turns right, + roll tilts left.
        Quaternion q = Quaternion.Euler(pitch, yaw, roll);
        values["RootQ.x"] = q.x; values["RootQ.y"] = q.y; values["RootQ.z"] = q.z; values["RootQ.w"] = q.w;
        return values;
    }

    static AnimationClip WriteClip(ParkourPose pose, Dictionary<string, float> values)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(ClipFolder)) AssetDatabase.CreateFolder("Assets/Animations", "ParkourPoses");

        string path = $"{ClipFolder}/{pose}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = pose.ToString() };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        foreach (var pair in values)
        {
            // A held pose: the same value at both ends of a short clip.
            var curve = AnimationCurve.Constant(0.0f, 0.5f, pair.Value);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        BakeBodyIntoPose(ref settings);
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationClip WriteMotion(string name, (float time, string spec)[] keys, Dictionary<string, float> basePose)
    {
        string path = $"{ClipFolder}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();

        var poses = new List<(float time, Dictionary<string, float> values)>();
        foreach (var (time, spec) in keys) poses.Add((time, ApplySpec(basePose, spec, $"{name}@{time}")));

        foreach (string property in poses[0].values.Keys)
        {
            // Shift this body part's keys, hold its first/last value out to the clip ends.
            float offset = GroupOffset(property);
            var frames = new SortedDictionary<float, float>();
            foreach (var (time, values) in poses) frames[Mathf.Clamp01(time + offset)] = values[property];
            var first = poses[0].values[property];
            var last = poses[poses.Count - 1].values[property];
            if (!frames.ContainsKey(0.0f)) frames[0.0f] = first;
            if (!frames.ContainsKey(1.0f)) frames[1.0f] = last;

            var curve = new AnimationCurve();
            foreach (var frame in frames) curve.AddKey(frame.Key, frame.Value);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        BakeBodyIntoPose(ref settings);
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // Keep the authored body height, lean and turn in the pose. Otherwise the humanoid treats the
    // body's motion over the clip as root motion and strips it out, so the hips never drop into a
    // tuck (and with root motion off, nothing moves the root instead).
    static void BakeBodyIntoPose(ref AnimationClipSettings settings)
    {
        settings.loopBlendPositionY = true;
        settings.keepOriginalPositionY = true;
        settings.loopBlendPositionXZ = true;
        settings.keepOriginalPositionXZ = true;
        settings.loopBlendOrientation = true;
        settings.keepOriginalOrientation = true;
    }

    static void WireController(Dictionary<ParkourPose, AnimationClip> clips, Dictionary<string, AnimationClip> motions)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        foreach (ParkourPose pose in clips.Keys)
        {
            string parameter = "Pose" + pose;
            if (System.Array.Exists(controller.parameters, p => p.name == parameter)) continue;
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
        }

        int layerIndex = System.Array.FindIndex(controller.layers, l => l.name == LayerName);
        if (layerIndex < 0)
        {
            controller.AddLayer(LayerName);
            layerIndex = controller.layers.Length - 1;
        }

        AnimatorControllerLayer[] layers = controller.layers;
        layers[layerIndex].defaultWeight = 0.0f; // ParkourController raises it during moves
        layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[layerIndex].iKPass = true;
        layers[0].iKPass = true;
        AnimatorStateMachine machine = layers[layerIndex].stateMachine;
        controller.layers = layers;

        AnimatorState state = System.Array.Find(machine.states, s => s.state.name == StateName).state;
        if (state == null) state = machine.AddState(StateName);
        machine.defaultState = state;

        if (!(state.motion is BlendTree tree))
        {
            tree = new BlendTree { name = StateName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            state.motion = tree;
        }
        tree.blendType = BlendTreeType.Direct;
        var children = new List<ChildMotion>();
        foreach (var pair in clips)
        {
            children.Add(new ChildMotion { motion = pair.Value, directBlendParameter = "Pose" + pair.Key, timeScale = 1.0f });
        }
        tree.children = children.ToArray();
        EditorUtility.SetDirty(tree);

        // One state per motion, its playback position driven by a parameter.
        if (!System.Array.Exists(controller.parameters, p => p.name == MotionTimeParameter))
            controller.AddParameter(MotionTimeParameter, AnimatorControllerParameterType.Float);
        foreach (var pair in motions)
        {
            AnimatorState motionState = System.Array.Find(machine.states, s => s.state.name == pair.Key).state;
            if (motionState == null) motionState = machine.AddState(pair.Key);
            motionState.motion = pair.Value;
            motionState.timeParameterActive = true;
            motionState.timeParameter = MotionTimeParameter;
            motionState.writeDefaultValues = true;
        }
        EditorUtility.SetDirty(controller);
    }
}
