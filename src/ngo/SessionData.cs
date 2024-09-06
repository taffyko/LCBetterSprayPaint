using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint.Ngo;

// Global synchronized lobby state
public class SessionData : NetworkBehaviour {
    internal static GameObject prefab = new GameObject(nameof(BetterSprayPaint.Ngo.SessionData));
    internal static SessionData? instance = null;

    internal NetworkVariable<bool> allowColorChange = new();
    public static bool AllowColorChange => instance?.allowColorChange?.Value ?? Plugin.AllowColorChange;
    internal NetworkVariable<bool> allowErasing = new();
    public static bool AllowErasing => instance?.allowErasing?.Value ?? Plugin.AllowErasing;
    internal NetworkVariable<bool> infiniteTank = new();
    public static bool InfiniteTank => instance?.infiniteTank?.Value ?? Plugin.InfiniteTank;
    internal NetworkVariable<float> tankCapacity = new();
    public static float TankCapacity => instance?.tankCapacity?.Value ?? Plugin.TankCapacity;
    internal NetworkVariable<float> shakeEfficiency = new();
    public static float ShakeEfficiency => instance?.shakeEfficiency?.Value ?? Plugin.ShakeEfficiency;
    internal NetworkVariable<bool> shakingNotNeeded = new();
    public static bool ShakingNotNeeded => instance?.shakingNotNeeded?.Value ?? Plugin.ShakingNotNeeded;
    internal NetworkVariable<float> range = new();
    public static float Range => instance?.range?.Value ?? Plugin.Range;
    internal NetworkVariable<float> maxSize = new();
    public static float MaxSize => instance?.maxSize?.Value ?? Plugin.MaxSize;
    internal NetworkVariable<bool> clientsCanPaintShip = new();
    public static bool ClientsCanPaintShip => instance?.clientsCanPaintShip?.Value ?? Plugin.ClientsCanPaintShip;

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
}