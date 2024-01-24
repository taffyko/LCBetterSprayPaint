using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace BetterSprayPaint;
// Per-player synchronized state
public class PlayerExt : NetworkBehaviour {
    public PlayerControllerB instance => GetComponent<PlayerControllerB>();

    internal NetworkVariable<float> _paintSize = new NetworkVariable<float>(1.0f);
    internal float _localPaintSize = 1.0f;
    public float PaintSize {
        set { value = Mathf.Clamp(value, 0.1f, SessionData.instance!.maxSize.Value); _localPaintSize = value; }
        get { return instance.IsLocalPlayer() ? _localPaintSize : _paintSize.Value; }
    }
    [ServerRpc(RequireOwnership = false)]
    public void SetPaintSizeServerRpc(float size) {
        _paintSize.Value = Mathf.Clamp(size, 0.1f, SessionData.instance!.maxSize.Value);
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();
        Plugin.log.LogInfo("PlayerExt: OnNetworkSpawn");
    }

    bool loaded = false;
    public void LateUpdate() {
        if (!loaded && SessionData.instance != null) {
            loaded = true;
            PaintSize = 1.0f;
        }
    }
}