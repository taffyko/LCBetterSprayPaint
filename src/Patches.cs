using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using System;
using UnityEngine.Rendering.HighDefinition;
using GameNetcodeStuff;
using Unity.Netcode;

namespace BetterSprayPaint;

[HarmonyPatch]
internal class Patches {
    static SessionData? sessionData => SessionData.instance;
    public static bool InfiniteTank => sessionData?.infiniteTank.Value ?? Plugin.InfiniteTank;
    public static bool AllowErasing => sessionData?.allowErasing.Value ?? Plugin.AllowErasing;
    public static bool ShakingNotNeeded => sessionData?.shakingNotNeeded.Value ?? Plugin.ShakingNotNeeded;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(StartOfRound), "EndOfGame")]
    public static void EndOfGame() {
        // Defensive cleanup for (https://github.com/taffyko/LCBetterSprayPaint/issues/4)
        foreach (var decal in SprayPaintItem.sprayPaintDecals) {
            if (decal != null && !decal.activeInHierarchy) {
                UnityEngine.Object.Destroy(decal);
            }
        }
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SprayPaintItem), "LateUpdate")]
    public static void LateUpdate(SprayPaintItem __instance, ref float ___sprayCanTank, ref float ___sprayCanShakeMeter, ref AudioSource ___sprayAudio, bool ___isSpraying) {
        var c = __instance.Ext();
        // Spray more, forever, faster
        if (InfiniteTank) ___sprayCanTank = 1f;
        __instance.maxSprayPaintDecals = Plugin.MaxSprayPaintDecals;
        __instance.sprayIntervalSpeed = 0.01f * c.PaintSize;
        if (ShakingNotNeeded) ___sprayCanShakeMeter = 1f;
        ___sprayAudio.volume = Plugin.Volume;

        // Properly synchronize shake meter
        if (c.HeldByLocalPlayer && c.ShakeMeter.Value != ___sprayCanShakeMeter) {
            c.UpdateShakeMeterServerRpc(___sprayCanShakeMeter);
        } else {
            ___sprayCanShakeMeter = c.ShakeMeter.Value;
        }

        // Properly synchronize holding player for late-joining clients
        if (c.HeldByLocalPlayer && c.PlayerHeldBy.Value.NetworkObjectId != __instance.playerHeldBy.NetworkObjectId) {
            c.SetPlayerHeldByServerRpc(new NetworkObjectReference(__instance.playerHeldBy.NetworkObject));
        } else {
            if (c.PlayerHeldBy.Value.TryGet(out var networkObject)) {
                __instance.playerHeldBy = networkObject.GetComponent<PlayerControllerB>();
            }
        }

        if (c.HeldByLocalPlayer) {
            var p = __instance.playerHeldBy.Ext();
            if (p != null) {
                // Synchronize size now
                if (p._localPaintSize != p._paintSize.Value) {
                    p.SetPaintSizeServerRpc(c.PaintSize);
                }
            }

            c.IsErasing = Plugin.inputActions.SprayPaintEraseModifier!.IsPressed() || Plugin.inputActions.SprayPaintErase!.IsPressed();
        }


        if (___isSpraying && (___sprayCanTank <= 0f || ___sprayCanShakeMeter <= 0f))
        {
            c.UpdateParticles();
            // needed to synchronize the effect for other players when the can runs out in the middle of spraying
            __instance.StopSpraying();
            PlayCanEmptyEffect(__instance, ___sprayCanTank <= 0f);
        }

        if (__instance.playerHeldBy != null) {
            // Cancel can shake animation early
            if (Plugin.ShorterShakeAnimation) {
                var anim = __instance.playerHeldBy.playerBodyAnimator;
                foreach (var clipInfo in anim.GetCurrentAnimatorClipInfo(2)) {
                    if (clipInfo.clip.name == "ShakeItem") {
                        var stateInfo = anim.GetCurrentAnimatorStateInfo(2);
                        if (stateInfo.normalizedTime > 0.1f) {
                            anim.Play("HoldOneHandedItem");
                        }
                    }
                }
            }
        }
    }

    public static void EraseSprayPaintAtPoint(SprayPaintItem __instance, Vector3 pos) {
        foreach (GameObject decal in SprayPaintItem.sprayPaintDecals) {
            if (decal != null && Vector3.Distance(decal.transform.position, pos) < Mathf.Max(0.15f, 0.5f * __instance.Ext().PaintSize)) {
                decal.SetActive(false);
            }
        }
    }
    public static bool EraseSprayPaintLocal(SprayPaintItem __instance, Vector3 sprayPos, Vector3 sprayRot, out RaycastHit sprayHit) {
        Ray ray = new Ray(sprayPos, sprayRot);
        if (RaycastSkipPlayer(ray, out sprayHit, sessionData!.range.Value, __instance.Ext().sprayPaintMask, QueryTriggerInteraction.Collide, __instance)) {
            EraseSprayPaintAtPoint(__instance, sprayHit.point);
            return true;
        } else {
            return false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SprayPaintItem), "TrySpraying")]
    public static bool TrySpraying(SprayPaintItem __instance, ref bool __result, ref RaycastHit ___sprayHit, ref float ___sprayCanShakeMeter) {
        var c = __instance.Ext();

        var sprayPos = GameNetworkManager.Instance.localPlayerController.gameplayCamera.transform.position;
        var sprayRot = GameNetworkManager.Instance.localPlayerController.gameplayCamera.transform.forward;

        c.UpdateParticles();

        if (AllowErasing && c.IsErasing) {
            // "Erase" mode
            if (EraseSprayPaintLocal(__instance, sprayPos, sprayRot, out var sprayHit)) {
                __result = true;
                c.EraseServerRpc(sprayHit.point);
            }
            return false;
        } else {
            // "Normal" mode
            if (AddSprayPaintLocal(__instance, sprayPos, sprayRot)) {
                __result = true;
                c.SprayServerRpc(sprayPos, sprayRot);
            }
            return false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SprayPaintItem), "ItemActivate")]
    public static void ItemActivate(SprayPaintItem __instance) {
        __instance.Ext().UpdateParticles();
    }

    // In the base game, a lot of raycasts fail because they hit the player rigidbody. This fixes that.
    public static bool RaycastSkipPlayer(Ray ray, out RaycastHit sprayHit, float _distance, int layerMask, QueryTriggerInteraction _queryTriggerInteraction, SprayPaintItem __instance) {
        var playerRigidbody = __instance.playerHeldBy?.playerRigidbody;
        if (playerRigidbody == null) {
            Plugin.log?.LogWarning("Player rigidbody is null");
        }
        bool result = false;
        RaycastHit sprayHitOut = default;
        float hitDistance = sessionData!.range.Value + 1f;
        foreach (var hit in Physics.RaycastAll(ray, sessionData!.range.Value, layerMask, QueryTriggerInteraction.Ignore)) {
            if (playerRigidbody == null || hit.rigidbody != playerRigidbody) {
                if (hit.distance < hitDistance) {
                    hitDistance = hit.distance;
                    sprayHitOut = hit;
                    result = true;
                }
            }
        }
        sprayHit = sprayHitOut;
        return result;
    }

    static MethodInfo physicsRaycast = typeof(Physics).GetMethod(nameof(Physics.Raycast), new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
    static MethodInfo raycastSkipPlayer = typeof(Patches).GetMethod(nameof(RaycastSkipPlayer));

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(SprayPaintItem), "AddSprayPaintLocal")]
    private static IEnumerable<CodeInstruction> transpiler_AddSprayPaintLocal(IEnumerable<CodeInstruction> instructions) {
        var foundMinNextDecalDistance = false;
        var foundRaycastCall = false;
        foreach (var instruction in instructions) {
            if (!foundMinNextDecalDistance && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.175f) {
                foundMinNextDecalDistance = true;
                // Reduce the minimum movement needed from the last position before you are allowed to spray a new decal
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0.001f);
            } else if (!foundRaycastCall && instruction.opcode == OpCodes.Call && instruction.operand == (object)physicsRaycast) {
                foundRaycastCall = true;
                // Replace the call to Physics.Raycast
                yield return new CodeInstruction(OpCodes.Ldarg_0); // pass instance as extra parameter
                yield return new CodeInstruction(OpCodes.Call, raycastSkipPlayer);
            }
            else {
                yield return instruction;
            }
        }
    }

    public static float TankCapacity() {
        if (InfiniteTank) {
            return 25f;
        } else {
            return sessionData?.tankCapacity.Value ?? Plugin.TankCapacity;
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(SprayPaintItem), "LateUpdate")]
    private static IEnumerable<CodeInstruction> transpiler_LateUpdate(IEnumerable<CodeInstruction> instructions) {
        var foundTankCapacityDivisor = false;
        foreach (var instruction in instructions) {
            if (!foundTankCapacityDivisor && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 25f) {
                // Override the rate at which the spray tank depletes during use
                foundTankCapacityDivisor = true;
                yield return new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod(nameof(TankCapacity)));
            } else {
                yield return instruction;
            }
        }
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(PlayerControllerB), "CanUseItem")]
    public static bool CanUseItem(object instance) { throw new NotImplementedException("stub"); }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(SprayPaintItem), "AddSprayPaintLocal")]
    public static bool _AddSprayPaintLocal(object instance, Vector3 sprayPos, Vector3 sprayRot) {
        IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            var foundMinNextDecalDistance = false;
            var foundRaycastCall = false;
            foreach (var instruction in instructions) {
                if (!foundMinNextDecalDistance && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.175f) {
                    foundMinNextDecalDistance = true;
                    // Reduce the minimum movement needed from the last position before you are allowed to spray a new decal
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 0.001f);
                } else if (!foundRaycastCall && instruction.opcode == OpCodes.Call && instruction.operand == (object)physicsRaycast) {
                    foundRaycastCall = true;
                    // Replace the call to Physics.Raycast
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // pass instance as extra parameter
                    yield return new CodeInstruction(OpCodes.Call, raycastSkipPlayer);
                }
                else {
                    yield return instruction;
                }
            }
        }
        _ = Transpiler(null!);
        return default;
    }


    [HarmonyReversePatch]
    [HarmonyPatch(typeof(SprayPaintItem), "PlayCanEmptyEffect")]
    public static void PlayCanEmptyEffect(object instance, bool isEmpty) { throw new NotImplementedException("stub"); }

    public static bool AddSprayPaintLocal(SprayPaintItem instance, Vector3 sprayPos, Vector3 sprayRot) {
        var result = _AddSprayPaintLocal(instance, sprayPos, sprayRot);
        if (result && SprayPaintItem.sprayPaintDecals.Count > SprayPaintItem.sprayPaintDecalsIndex) {
            var gameObject = SprayPaintItem.sprayPaintDecals[SprayPaintItem.sprayPaintDecalsIndex];

            // Use the raycast normal to orient the decal so that decals are no longer distorted when spraying at an angle
            var sprayHit = Traverse.Create(instance).Field<RaycastHit>("sprayHit").Value;
            gameObject.transform.forward = -sprayHit.normal;

            // Fix for spray paint failing to clear (https://github.com/taffyko/LCBetterSprayPaint/issues/4)
            // The game uses GrabbableObject.inElevator to decide whether spray paint should be parented to HangarShip (persistent) or MapPropsContainer (temporary)
            // This value fails to update properly if the player leaves the ship while the spray paint is in a non-active slot.
            // - This parenting implementation doesn't have that issue.
            if (sprayHit.collider.gameObject.layer == 11 || sprayHit.collider.gameObject.layer == 8 || sprayHit.collider.gameObject.layer == 0)
            {
                if (sprayHit.collider.transform.IsChildOf(StartOfRound.Instance.elevatorTransform) || RoundManager.Instance.mapPropsContainer == null) {
                    gameObject.transform.SetParent(StartOfRound.Instance.elevatorTransform, true);
                } else {
                    gameObject.transform.SetParent(RoundManager.Instance.mapPropsContainer.transform, true);
                }
            }

            #if DEBUG
            gameObject.name = $"SprayDecal_{SprayPaintItem.sprayPaintDecalsIndex}";
            Plugin.log.LogInfo($"{gameObject.name} hit: {sprayHit.collider.gameObject.transform.GetPath()}");
            Plugin.log.LogInfo($"{gameObject.name} added to {gameObject.transform.parent.name} at{gameObject.transform.position}");
            #endif

            // Spray paint had a netcode issue where some decals don't show up on remote clients,
            // because their DecalProjector.enabled is set to false. Make sure it's set to true whenever a decal is added.
            var projector = gameObject.GetComponent<DecalProjector>();
            projector.enabled = true;

            var c = instance.Ext();

            projector.drawDistance = Plugin.DrawDistance;

            projector.material = c.DecalMaterialForColor(c.CurrentColor);

            projector.scaleMode = DecalScaleMode.InheritFromHierarchy;
            gameObject.transform.localScale = new Vector3(
                c.PaintSize,
                c.PaintSize,
                1.0f
            );
        }
        return result;
    }

    public static float _shakeRestoreAmount() {
        return sessionData?.shakeEfficiency.Value ?? Plugin.ShakeEfficiency;
    }
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(SprayPaintItem), "ItemInteractLeftRight")]
    private static IEnumerable<CodeInstruction> transpiler_ItemInteractLeftRight(IEnumerable<CodeInstruction> instructions) {
        var foundShakeRestoreAmount = false;
        foreach (var instruction in instructions) {
            if (!foundShakeRestoreAmount && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.15f) {
                // Make shaking restore double the normal amount on the "shake meter"
                foundShakeRestoreAmount = true;
                yield return new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod(nameof(_shakeRestoreAmount)));
            } else {
                yield return instruction;
            }
        }
    }
}