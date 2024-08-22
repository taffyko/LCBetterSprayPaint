using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint.Ngo;

// Global synchronized lobby state
public class SessionData : NetworkBehaviour {
    internal static GameObject prefab = new GameObject(nameof(BetterSprayPaint.Ngo.SessionData));
    internal static SessionData? instance = null;

    NetworkVariable<bool> allowColorChange = new();
    public static bool AllowColorChange => instance?.allowColorChange?.Value ?? Plugin.AllowColorChange;
    NetworkVariable<bool> allowErasing = new();
    public static bool AllowErasing => instance?.allowErasing?.Value ?? Plugin.AllowErasing;
    NetworkVariable<bool> infiniteTank = new();
    public static bool InfiniteTank => instance?.infiniteTank?.Value ?? Plugin.InfiniteTank;
    NetworkVariable<float> tankCapacity = new();
    public static float TankCapacity => instance?.tankCapacity?.Value ?? Plugin.TankCapacity;
    NetworkVariable<float> shakeEfficiency = new();
    public static float ShakeEfficiency => instance?.shakeEfficiency?.Value ?? Plugin.ShakeEfficiency;
    NetworkVariable<bool> shakingNotNeeded = new();
    public static bool ShakingNotNeeded => instance?.shakingNotNeeded?.Value ?? Plugin.ShakingNotNeeded;
    NetworkVariable<float> range = new();
    public static float Range => instance?.range?.Value ?? Plugin.Range;
    NetworkVariable<float> maxSize = new();
    public static float MaxSize => instance?.maxSize?.Value ?? Plugin.MaxSize;

    public override void OnNetworkSpawn() {
        instance = this.GetComponent<SessionData>();
    }
    public override void OnDestroy() {
        instance = null;
    }

    internal static void ServerSpawn() {
        if (NetworkManager.Singleton.IsServer && instance == null) {
            var go = Instantiate(prefab)!;
            go.GetComponent<NetworkObject>().Spawn();
        }
    }
    internal static void Despawn(bool _) {
        Destroy(instance?.gameObject);
    }

    public void Update() {
        if (IsServer) {
            allowErasing.Value = Plugin.AllowErasing;
            allowColorChange.Value = Plugin.AllowColorChange;
            infiniteTank.Value = Plugin.InfiniteTank;
            tankCapacity.Value = Plugin.TankCapacity;
            shakeEfficiency.Value = Plugin.ShakeEfficiency;
            shakingNotNeeded.Value = Plugin.ShakingNotNeeded;
            range.Value = Plugin.Range;
            maxSize.Value = Plugin.MaxSize;
        }
    }
}