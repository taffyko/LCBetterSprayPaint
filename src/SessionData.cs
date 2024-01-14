using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint;

// Global synchronized lobby state
public class SessionData : NetworkBehaviour {
    internal static GameObject prefab = new GameObject(nameof(BetterSprayPaint.SessionData));
    internal static SessionData? instance = null;

    public NetworkVariable<bool> allowColorChange = new();
    public NetworkVariable<bool> allowErasing = new();
    public NetworkVariable<bool> infiniteTank = new();
    public NetworkVariable<float> tankCapacity = new();
    public NetworkVariable<float> shakeEfficiency = new();
    public NetworkVariable<bool> shakingNotNeeded = new();
    public NetworkVariable<float> range = new();
    public NetworkVariable<float> maxSize = new();

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