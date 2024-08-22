
using GameNetcodeStuff;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using BetterSprayPaint.Ngo;

namespace BetterSprayPaint;

public static class Utils {
    public static string GetPath(this Transform current) {
        if (current.parent == null)
            return "/" + current.name;
        return current.parent.GetPath() + "/" + current.name;
    }

    public static bool IsLocalPlayer(this PlayerControllerB? player) {
        return player != null && player == StartOfRound.Instance?.localPlayerController;
    }

    public static SprayPaintItemNetExt NetExt(this SprayPaintItem instance) {
        var c = instance.GetComponent<SprayPaintItemNetExt>();
        if (c == null) { Plugin.log.LogError("SprayPaintItem.Ext() is null"); }
        return c!;
    }

    public static SprayPaintItemExt Ext(this SprayPaintItem instance) {
        var c = instance.GetComponent<SprayPaintItemExt>();
        if (c == null) { c = instance.gameObject.AddComponent<SprayPaintItemExt>(); }
        return c;
    }

    public static PlayerNetExt? NetExt(this PlayerControllerB? instance) {
        if (instance != null && instance.TryGetComponent<PlayerNetExt>(out var playerExt)) {
            return playerExt;
        }
        return null;
    }

    public static void SyncWithNetworkObject(this NetworkBehaviour networkBehaviour, NetworkObject? networkObject) {
        networkObject = networkObject ?? networkBehaviour.NetworkObject;
        if (!networkObject.ChildNetworkBehaviours.Contains(networkBehaviour))
            networkObject.ChildNetworkBehaviours.Add(networkBehaviour);
        networkBehaviour.UpdateNetworkProperties();
    }
}