using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Generates Assets/Scenes/TestCourse.unity: a set of zones for exercising the procedural
// movement system (stride matching, slopes, stairs, uneven ground, obstacle scanning, drops).
// The scene is fully regenerated on each build, so change the course here rather than by hand.
public static class TestCourseBuilder
{
    const string ScenePath = "Assets/Scenes/TestCourse.unity";
    const string MaterialFolder = "Assets/Materials/TestCourse";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string CameraPrefabPath = "Assets/Prefabs/FreeLook Camera.prefab";
    const string GroundLayerName = "Ground";
    const string ObstacleLayerName = "Obstacles";

    static int groundLayer;
    static int obstacleLayer;
    static Material groundMat, walkableMat, obstacleMat, markerMat, dropMat;
    static Font labelFont;

    [MenuItem("Tools/Movement/Build Test Course")]
    static void BuildFromMenu()
    {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Build Test Course",
                $"{ScenePath} will be regenerated and any manual edits to it lost.", "Rebuild", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void Build()
    {
        groundLayer = EnsureLayer(GroundLayerName);
        obstacleLayer = EnsureLayer(ObstacleLayerName);
        CreateMaterials();
        labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50, -30, 0);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(10, 1, 10); // 100 m x 100 m
        Finish(floor, groundMat, groundLayer);

        BuildSpeedStrip(new Vector3(0, 0, 4));
        BuildSlopes(new Vector3(-12, 0, 8));
        BuildObstacles(new Vector3(10, 0, 10));
        BuildStairs(new Vector3(-24, 0, -10));
        BuildUnevenGround(new Vector3(16, 0, -20));
        BuildDrops(new Vector3(0, 0, -6));
        BuildParkourRun(new Vector3(28, 0, 18));

        var player = SpawnPlayer();
        SpawnCamera(player.transform);
        new GameObject("Debug HUD").AddComponent<LocomotionDebugHUD>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Debug.Log($"[TestCourseBuilder] Built {ScenePath}");
    }

    // Flat 50 m run with a marker every 5 m, for checking speed and foot sliding.
    static void BuildSpeedStrip(Vector3 start)
    {
        var root = Group("Speed Strip", start);
        Label(root, "SPEED / STRIDE\nmarkers every 5 m", new Vector3(0, 2.5f, -1));
        for (int i = 0; i <= 10; i++)
        {
            var marker = Box(root, $"Marker {i * 5}m", new Vector3(0, 0.005f, i * 5), new Vector3(4, 0.01f, 0.1f), markerMat, groundLayer);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            Label(root, $"{i * 5} m", new Vector3(2.6f, 0.4f, i * 5), 0.06f);
        }
    }

    // Ramps from gentle to steeper than the CharacterController slope limit (45°).
    static void BuildSlopes(Vector3 origin)
    {
        var root = Group("Slopes", origin);
        const float height = 1.5f, width = 3, topLength = 3;
        float[] angles = { 10, 20, 30, 40, 50 };
        for (int i = 0; i < angles.Length; i++)
        {
            var ramp = Group($"Slope {angles[i]}°", Vector3.left * (i * 4), root);
            float run = height / Mathf.Tan(angles[i] * Mathf.Deg2Rad);
            Ramp(ramp, "Up", 0, run, height, width, angles[i]);
            Box(ramp, "Top", new Vector3(0, height * 0.5f, run + topLength * 0.5f), new Vector3(width, height, topLength), walkableMat, groundLayer);
            Ramp(ramp, "Down", run + topLength, run, height, width, -angles[i]);
            Label(ramp, $"{angles[i]}°", new Vector3(0, height + 1.5f, run + topLength * 0.5f));
        }
        Label(root, "SLOPES\n(limit 45°)", new Vector3(-8, 4.5f, 0));
    }

    // A row of obstacles at step, vault and climb heights, plus cases the
    // single forward ray (0.2 m up) is expected to miss.
    static void BuildObstacles(Vector3 origin)
    {
        var root = Group("Obstacles", origin);
        (string name, Vector3 size)[] obstacles =
        {
            ("Below scan ray 0.15", new Vector3(2, 0.15f, 1)),
            ("Step 0.3", new Vector3(2, 0.3f, 1)),
            ("Low 0.6", new Vector3(2, 0.6f, 1)),
            ("Vault wall 1.0", new Vector3(2, 1.0f, 0.2f)),
            ("Vault block 1.0", new Vector3(2, 1.0f, 2)),
            ("Climb 1.5", new Vector3(2, 1.5f, 2)),
            ("Climb 2.0", new Vector3(2, 2.0f, 2)),
            ("Climb 2.7", new Vector3(2, 2.7f, 2)),
            ("Wall 4.0", new Vector3(2, 4.0f, 2)),
            ("Thin pole", new Vector3(0.1f, 1.5f, 0.1f)),
        };
        for (int i = 0; i < obstacles.Length; i++)
        {
            var (name, size) = obstacles[i];
            var position = new Vector3(i * 3.5f, size.y * 0.5f, size.z * 0.5f);
            Box(root, name, position, size, obstacleMat, obstacleLayer);
            Label(root, name.Replace(' ', '\n'), new Vector3(i * 3.5f, size.y + 0.8f, 0));
        }
        Label(root, "OBSTACLES", new Vector3(obstacles.Length * 1.75f, 6, 0));
    }

    // Stair flights with risers up to and beyond the CharacterController step offset (0.3 m).
    static void BuildStairs(Vector3 origin)
    {
        var root = Group("Stairs", origin);
        const float height = 1.2f, tread = 0.35f, width = 2.5f, landing = 2;
        float[] risers = { 0.1f, 0.15f, 0.2f, 0.3f, 0.4f };
        for (int i = 0; i < risers.Length; i++)
        {
            var flight = Group($"Riser {risers[i]}", Vector3.left * (i * 3.5f), root);
            int steps = Mathf.RoundToInt(height / risers[i]);
            float up = steps * tread;
            for (int s = 0; s < steps; s++)
            {
                float h = (s + 1) * risers[i];
                Box(flight, $"Up {s}", new Vector3(0, h * 0.5f, s * tread + tread * 0.5f), new Vector3(width, h, tread), walkableMat, groundLayer);
                Box(flight, $"Down {s}", new Vector3(0, h * 0.5f, up * 2 + landing - s * tread - tread * 0.5f), new Vector3(width, h, tread), walkableMat, groundLayer);
            }
            float top = steps * risers[i];
            Box(flight, "Landing", new Vector3(0, top * 0.5f, up + landing * 0.5f), new Vector3(width, top, landing), walkableMat, groundLayer);
            Label(flight, $"{risers[i] * 100:0} cm", new Vector3(0, top + 1.2f, up + landing * 0.5f));
        }
        Label(root, "STAIRS\n(step offset 30 cm)", new Vector3(-7, 3.5f, -1));
    }

    // Seeded field of low, tilted slabs for foot placement and pelvis adjustment.
    static void BuildUnevenGround(Vector3 origin)
    {
        var root = Group("Uneven Ground", origin);
        var random = new System.Random(1234);
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        for (int i = 0; i < 70; i++)
        {
            var slab = Box(root, $"Slab {i}", new Vector3(Range(-5, 5), 0, Range(0, 16)),
                new Vector3(Range(0.4f, 1.4f), Range(0.05f, 0.25f), Range(0.4f, 1.4f)), walkableMat, groundLayer);
            slab.transform.localRotation = Quaternion.Euler(Range(-8, 8), Range(0, 360), Range(-8, 8));
        }
        Label(root, "UNEVEN GROUND", new Vector3(0, 2.5f, -1));
    }

    // Ramp up to a 4 m deck, then drop-offs of 0.5, 1, 2 and 4 m for fall and landing tests.
    static void BuildDrops(Vector3 origin)
    {
        var root = Group("Drops", origin);
        const float deckHeight = 4, rampAngle = 20, width = 3;
        float run = deckHeight / Mathf.Tan(rampAngle * Mathf.Deg2Rad);
        var ramp = Group("Ramp", Vector3.zero, root);
        ramp.localRotation = Quaternion.Euler(0, 180, 0); // climbs toward -Z, away from spawn
        Ramp(ramp, "Up", 0, run, deckHeight, width, rampAngle);

        var deck = new Vector3(0, deckHeight, -run - 2);
        Box(root, "Deck 4.0", deck + new Vector3(0, -deckHeight * 0.5f, 0), new Vector3(4, deckHeight, 4), dropMat, groundLayer);
        Label(root, "DROPS\n4 m to floor →", deck + new Vector3(0, 1.8f, 0));

        // Stepping down westward: 0.5, 1 and 2 m drops, then 0.5 m to the floor.
        float[] heights = { 3.5f, 2.5f, 0.5f };
        float previous = deckHeight;
        for (int i = 0; i < heights.Length; i++)
        {
            var center = new Vector3(-4 * (i + 1), heights[i] * 0.5f, deck.z);
            Box(root, $"Deck {heights[i]}", center, new Vector3(4, heights[i], 4), dropMat, groundLayer);
            Label(root, $"drop {previous - heights[i]:0.0} m", new Vector3(center.x + 2, previous + 1, deck.z));
            previous = heights[i];
        }
    }

    // A lane of parkour obstacles with run-up space between them, for chaining moves at speed,
    // plus a block under a low ceiling where climbing must be refused (no room to stand).
    static void BuildParkourRun(Vector3 origin)
    {
        var root = Group("Parkour Run", origin);
        (string name, float height, float depth, float gap)[] stations =
        {
            ("Step 0.5", 0.5f, 1.5f, 0),
            ("Vault 1.0", 1.0f, 0.2f, 4),
            ("Vault 0.8 deep", 0.8f, 0.6f, 4),
            ("Mantle 1.2", 1.2f, 2.0f, 4),
            ("Climb 2.4", 2.4f, 2.0f, 5),
        };
        float z = 0;
        foreach (var (name, height, depth, gap) in stations)
        {
            z += gap;
            Box(root, name, new Vector3(0, height * 0.5f, z + depth * 0.5f), new Vector3(3, height, depth), obstacleMat, obstacleLayer);
            Label(root, name, new Vector3(-2.2f, height + 0.6f, z));
            z += depth;
        }
        Label(root, "PARKOUR RUN\n(Jump to act)", new Vector3(0, 3.5f, -1.5f));

        var ceiling = Group("Low Ceiling", new Vector3(6, 0, 4), root);
        Box(ceiling, "Block 1.5", new Vector3(0, 0.75f, 1), new Vector3(3, 1.5f, 2), obstacleMat, obstacleLayer);
        Box(ceiling, "Roof", new Vector3(0, 2.7f, 1.5f), new Vector3(3, 0.2f, 3), walkableMat, groundLayer);
        Label(ceiling, "Climb blocked:\n1.1 m headroom", new Vector3(0, 3.4f, 0));
    }

    static GameObject SpawnPlayer()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return player;
    }

    static void SpawnCamera(Transform target)
    {
        var mainCamera = new GameObject("Main Camera") { tag = "MainCamera" };
        mainCamera.AddComponent<Camera>();
        mainCamera.AddComponent<AudioListener>();
        mainCamera.AddComponent<CinemachineBrain>();
        mainCamera.transform.position = new Vector3(0, 3, -5);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraPrefabPath);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.GetComponent<CinemachineCamera>().Target.TrackingTarget = target;
    }

    // A slab whose top surface rises from the floor at startZ by `height` over `run` metres.
    // A negative angle descends instead, starting at `height`.
    static void Ramp(Transform parent, string name, float startZ, float run, float height, float width, float angle)
    {
        const float thickness = 0.5f;
        float slopeLength = Mathf.Sqrt(run * run + height * height);
        var rotation = Quaternion.Euler(-angle, 0, 0);
        var topMiddle = new Vector3(0, height * 0.5f, startZ + run * 0.5f);
        var ramp = Box(parent, name, topMiddle - rotation * Vector3.up * (thickness * 0.5f),
            new Vector3(width, thickness, slopeLength), walkableMat, groundLayer);
        ramp.transform.localRotation = rotation;
    }

    static Transform Group(string name, Vector3 localPosition, Transform parent = null)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.localPosition = localPosition;
        return group;
    }

    static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, int layer)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        Finish(box, material, layer);
        return box;
    }

    static void Finish(GameObject go, Material material, int layer)
    {
        go.layer = layer;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
    }

    static void Label(Transform parent, string text, Vector3 localPosition, float size = 0.1f)
    {
        var label = new GameObject($"Label {text.Split('\n')[0]}");
        label.transform.SetParent(parent, false);
        label.transform.localPosition = localPosition;
        // Text faces -Z, toward the spawn point.
        label.transform.rotation = Quaternion.identity;
        var mesh = label.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = labelFont;
        mesh.fontSize = 64;
        mesh.characterSize = size;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = Color.black;
        label.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material;
    }

    static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "TestCourse");

        groundMat = LoadOrCreateMaterial("Ground", Color.white, m =>
        {
            // 1 m checkers on the 100 m floor make speed and foot sliding easy to judge.
            var checker = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Checkers.mat");
            if (checker != null) m.SetTexture("_BaseMap", checker.GetTexture("_BaseMap"));
            m.SetTextureScale("_BaseMap", new Vector2(50, 50));
        });
        walkableMat = LoadOrCreateMaterial("Walkable", new Color(0.62f, 0.66f, 0.72f));
        obstacleMat = LoadOrCreateMaterial("Obstacle", new Color(0.95f, 0.55f, 0.2f));
        markerMat = LoadOrCreateMaterial("Marker", new Color(0.15f, 0.15f, 0.15f));
        dropMat = LoadOrCreateMaterial("Drop", new Color(0.35f, 0.55f, 0.9f));
    }

    static Material LoadOrCreateMaterial(string name, Color color, System.Action<Material> configure = null)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        configure?.Invoke(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing != -1) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 6; i < layers.arraySize; i++) // 0-5 are reserved by Unity
        {
            var layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue)) continue;
            layer.stringValue = name;
            tagManager.ApplyModifiedProperties();
            return i;
        }
        throw new System.InvalidOperationException($"No free user layer for '{name}'.");
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
