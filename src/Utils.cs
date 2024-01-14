
using GameNetcodeStuff;
using UnityEngine;

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

    public static SprayPaintItemExt Ext(this SprayPaintItem instance) {
        return instance.GetComponent<SprayPaintItemExt>();
    }

    public static PlayerExt? Ext(this PlayerControllerB? instance) {
        return instance?.GetComponent<PlayerExt>();
    }
}