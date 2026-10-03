using UnityEngine;
using UnityEngine.InputSystem;

// On-screen readout of locomotion state for tuning on the test course. Toggle with F1.
public class LocomotionDebugHUD : MonoBehaviour
{
    [SerializeField] PlayerController player;
    [SerializeField] bool visible = true;

    LocomotionController locomotion;
    ParkourController parkour;
    FootPlacement feet;
    GUIStyle style;

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<PlayerController>();
        if (player != null) locomotion = player.GetComponent<LocomotionController>();
        if (player != null) parkour = player.GetComponent<ParkourController>();
        if (player != null) feet = player.GetComponent<FootPlacement>();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) visible = !visible;
    }

    void OnGUI()
    {
        if (!visible || player == null) return;
        style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

        string text =
            $"<b>Locomotion</b>  (F1 to hide)\n" +
            $"Speed: {player.CurrentSpeed:F2} m/s\n" +
            $"Grounded: {player.IsGrounded}   Ground angle: {player.GroundAngle:F1}°\n";

        if (locomotion != null)
        {
            ObstacleDataContainer obstacle = locomotion.CurrentObstacle;
            text += obstacle.forwardHitFound
                ? $"Obstacle: {obstacle.forwardHit.collider.name}  {obstacle.forwardHit.distance:F2} m ahead\n" +
                  (obstacle.upHitFound ? $"Obstacle height: {obstacle.height:F2} m" : "Obstacle height: above scan range")
                : "Obstacle: none";
        }

        if (parkour != null)
        {
            text += parkour.InAction
                ? $"\nParkour: <b>{parkour.Current.Action.DisplayName}</b>"
                : $"\nParkour: {(parkour.Available != null ? $"Jump to <b>{parkour.Available.Action.DisplayName}</b>" : "-")}";
        }

        if (feet != null)
        {
            string Leg(bool planted) => planted ? "planted" : "swing";
            text += feet.Weight > 0.0f
                ? $"\nFeet: L {Leg(feet.LeftPlanted)}  R {Leg(feet.RightPlanted)}  hips {feet.PelvisOffset:+0.00;-0.00} m"
                : "\nFeet: IK off";
        }

        GUI.Box(new Rect(10, 10, 340, 150), text, style);
    }
}
