
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using BetterSprayPaint.Ngo;
using System.Collections.Generic;

namespace BetterSprayPaint;

public static class Utils {
    public static string GetPath(this Transform current, Transform? relativeTo = null) {
        if (current == relativeTo)
            return "";
        if (current.parent == null)
            return "/" + current.name;
        var parentPath = current.parent.GetPath(relativeTo);
        if (parentPath == "") {
            return current.name;
        } else {
            return parentPath + "/" + current.name;
        }
    }
    
    public static IEnumerable<Transform> GetAncestors(this Transform self, bool includeSelf = true) {
        var current = self;
        if (!includeSelf)
            current = self.parent;
        while (current != null) {
            yield return current;
            current = current.parent;
        }
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