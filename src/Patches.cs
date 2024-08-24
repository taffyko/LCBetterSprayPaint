using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using System;
using UnityEngine.Rendering.HighDefinition;
using GameNetcodeStuff;
using Unity.Netcode;
using BetterSprayPaint.Ngo;
using System.Linq;

namespace BetterSprayPaint;

[HarmonyPatch]
internal class Patches {

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
        if (__instance.isWeedKillerSprayBottle) { return; }
        __instance.Ext();
        var c = __instance.NetExt();
        if (c == null) { return; }
        // Spray more, forever, faster
        if (SessionData.InfiniteTank) ___sprayCanTank = 1f;
        __instance.maxSprayPaintDecals = Plugin.MaxSprayPaintDecals;
        __instance.sprayIntervalSpeed = 0.01f * c.PaintSize;
        if (SessionData.ShakingNotNeeded) ___sprayCanShakeMeter = 1f;
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
        return;
    }

    public static void EraseSprayPaintAtPoint(SprayPaintItem __instance, Vector3 pos) {
        foreach (GameObject decal in SprayPaintItem.sprayPaintDecals) {
            if (decal != null && Vector3.Distance(decal.transform.position, pos) < Mathf.Max(0.15f, 0.5f * __instance.NetExt().PaintSize)) {
                decal.SetActive(false);
            }
        }
    }
    public static bool EraseSprayPaintLocal(SprayPaintItem __instance, Vector3 sprayPos, Vector3 sprayRot, out RaycastHit sprayHit) {
        Ray ray = new Ray(sprayPos, sprayRot);
        if (RaycastCustom(ray, out sprayHit, SessionData.Range, __instance.NetExt().sprayPaintMask, QueryTriggerInteraction.Collide, __instance)) {
            EraseSprayPaintAtPoint(__instance, sprayHit.point);
            return true;
        } else {
            return false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SprayPaintItem), "TrySpraying")]
    public static bool TrySpraying(SprayPaintItem __instance, ref bool __result, ref RaycastHit ___sprayHit, ref float ___sprayCanShakeMeter) {
        if (__instance.isWeedKillerSprayBottle) { return true; }
        var c = __instance.NetExt();

        var sprayPos = GameNetworkManager.Instance.localPlayerController.gameplayCamera.transform.position;
        var sprayRot = GameNetworkManager.Instance.localPlayerController.gameplayCamera.transform.forward;

        c.UpdateParticles();

        if (SessionData.AllowErasing && c.IsErasing) {
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
        if (__instance.isWeedKillerSprayBottle) { return; }
        __instance.NetExt().UpdateParticles();
    }
    
    public static HashSet<string> CompanyCruiserColliderBlacklist = new HashSet<string> {
        "Meshes/DoorLeftContainer/Door/DoorTrigger",
        "CollisionTriggers/Cube",
        "PushTrigger",
        "Triggers/ItemDropRegion",
        "Triggers/BackPhysicsRegion",
        "Triggers/LeftShelfPlacementCollider/bounds",
        "Triggers/RightShelfPlacementCollider/bounds",
        "VehicleBounds",
        "InsideTruckNavBounds",
    };

    // In the base game, a lot of raycasts fail because they hit the player rigidbody.
    // (particularly when spraying the floor while moving backwards)
    // This fixes that.
    public static bool RaycastCustom(Ray ray, out RaycastHit sprayHit, float _distance, int layerMask, QueryTriggerInteraction queryTriggerInteraction, SprayPaintItem __instance) {
        var playerObject = __instance.playerHeldBy?.gameObject;
        if (playerObject == null) {
            Plugin.log?.LogWarning("Player GameObject is null");
        }
        bool result = false;
        RaycastHit sprayHitOut = default;
        float hitDistance = SessionData.Range + 1f;
        int companyCruiserLayers = 1 << 9 | 1 << 30;
        foreach (var hit in Physics.RaycastAll(ray, SessionData.Range, layerMask | companyCruiserLayers, queryTriggerInteraction)) {
            var layer = 1 << hit.collider.gameObject.layer;
            if (playerObject != null && hit.transform.IsChildOf(playerObject.transform)) { continue; }
            if ((layer & layerMask) == 0) {
                // Make an exception for certain colliders on the Company Cruiser
                if ((layer & companyCruiserLayers) == 0) { continue; }
                var cruiser = hit.collider.gameObject.transform.GetAncestors().FirstOrDefault((go) => go.name.StartsWith("CompanyCruiser"));
                if (cruiser == null) { continue; }
                var path = hit.collider.gameObject.transform.GetPath(relativeTo: cruiser);
                if (CompanyCruiserColliderBlacklist.Contains(path)) { continue; }
            }

            if (hit.collider.isTrigger) {
                if (hit.collider.name.Contains("Trigger")) { continue; }
            }

            if (hit.distance < hitDistance) {
                hitDistance = hit.distance;
                sprayHitOut = hit;
                result = true;
            }
        }
        sprayHit = sprayHitOut;
        return result;
    }

    static MethodInfo physicsRaycast = typeof(Physics).GetMethod(nameof(Physics.Raycast), new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
    static MethodInfo raycastCustom = typeof(Patches).GetMethod(nameof(RaycastCustom));

    
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(SprayPaintItem), "AddSprayPaintLocal")]
    public static bool original_AddSprayPaintLocal(object instance, Vector3 sprayPos, Vector3 sprayRot) { throw new NotImplementedException("stub"); }
    
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(SprayPaintItem), "AddSprayPaintLocal")]
    private static IEnumerable<CodeInstruction> transpiler_AddSprayPaintLocal(IEnumerable<CodeInstruction> instructions) {
        var foundMinNextDecalDistance = false;
        var foundRaycastCall = false;
        var addedWeedKillerSkip = false;
        foreach (var instruction in instructions) {
            if (!addedWeedKillerSkip) {
                addedWeedKillerSkip = true;
                foreach (var instr in _transpiler_AddWeedKillerSkip(instruction, typeof(Patches).GetMethod(nameof(original_AddSprayPaintLocal)))) { yield return instr; }
            } else if (!foundMinNextDecalDistance && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.175f) {
                foundMinNextDecalDistance = true;
                // Reduce the minimum movement needed from the last position before you are allowed to spray a new decal
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0.001f);
            } else if (!foundRaycastCall && instruction.opcode == OpCodes.Call && instruction.operand == (object)physicsRaycast) {
                foundRaycastCall = true;
                // Replace the call to Physics.Raycast
                yield return new CodeInstruction(OpCodes.Ldarg_0); // pass instance as extra parameter
                yield return new CodeInstruction(OpCodes.Call, raycastCustom);
            } else {
                yield return instruction;
            }
        }
    }

    public static float TankCapacity() {
        if (SessionData.InfiniteTank) {
            return 25f;
        } else {
            return SessionData.TankCapacity;
        }
    }

    private static IEnumerable<CodeInstruction> _transpiler_AddWeedKillerSkip(CodeInstruction instruction, MethodInfo original) {
        var label = new System.Reflection.Emit.Label();
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Ldfld, typeof(SprayPaintItem).GetField("isWeedKillerSprayBottle"));
        yield return new CodeInstruction(OpCodes.Brfalse_S, label);
        yield return new CodeInstruction(OpCodes.Jmp, original);
        var instr = instruction.WithLabels(label);
        yield return instr;
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(SprayPaintItem), "LateUpdate")]
    public static void original_LateUpdate(object instance) { throw new NotImplementedException("stub"); }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(SprayPaintItem), "LateUpdate")]
    private static IEnumerable<CodeInstruction> transpiler_LateUpdate(IEnumerable<CodeInstruction> instructions) {
        var foundTankCapacityDivisor = false;
        var addedWeedKillerSkip = false;
        foreach (var instruction in instructions) {
            if (!addedWeedKillerSkip) {
                addedWeedKillerSkip = true;
                foreach (var instr in _transpiler_AddWeedKillerSkip(instruction, typeof(Patches).GetMethod(nameof(original_LateUpdate)))) { yield return instr; }
            } else if (!foundTankCapacityDivisor && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 25f) {
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
                    yield return new CodeInstruction(OpCodes.Call, raycastCustom);
                } else {
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
    
    public static void PositionSprayPaint(SprayPaintItem instance, GameObject gameObject, RaycastHit sprayHit, bool setColor = true) {
        // Use the raycast normal to orient the decal so that decals are no longer distorted when spraying at an angle
        gameObject.transform.forward = -sprayHit.normal;
        gameObject.transform.position = sprayHit.point;

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

        // Spray paint had a netcode issue where some decals don't show up on remote clients,
        // because their DecalProjector.enabled is set to false. Make sure it's set to true whenever a decal is added.
        var projector = gameObject.GetComponent<DecalProjector>();
        projector.enabled = true;

        var c = instance.NetExt();

        projector.drawDistance = Plugin.DrawDistance;

        if (setColor) { projector.material = c.DecalMaterialForColor(c.CurrentColor); }

        projector.scaleMode = DecalScaleMode.InheritFromHierarchy;
        gameObject.transform.localScale = new Vector3(1f, 1f, 1f);
        var parentScale = gameObject.transform.lossyScale;
        gameObject.transform.localScale = new Vector3(
            (1f/parentScale.x) * c.PaintSize,
            (1f/parentScale.y) * c.PaintSize,
            1.0f
        );
    }

    public static bool AddSprayPaintLocal(SprayPaintItem instance, Vector3 sprayPos, Vector3 sprayRot) {
        if ((SprayPaintItem.sprayPaintDecalsIndex - SprayPaintItem.sprayPaintDecals.Count) > 1) {
            // defensive
            SprayPaintItem.sprayPaintDecals.AddRange(new GameObject[(SprayPaintItem.sprayPaintDecalsIndex - 2) - SprayPaintItem.sprayPaintDecals.Count]);
        }
        var result = _AddSprayPaintLocal(instance, sprayPos, sprayRot);
        if (result && SprayPaintItem.sprayPaintDecals.Count > SprayPaintItem.sprayPaintDecalsIndex) {
            var gameObject = SprayPaintItem.sprayPaintDecals[SprayPaintItem.sprayPaintDecalsIndex];

            var sprayHit = Traverse.Create(instance).Field<RaycastHit>("sprayHit").Value;
            PositionSprayPaint(instance, gameObject, sprayHit);

            #if DEBUG
            gameObject.name = $"SprayDecal_{SprayPaintItem.sprayPaintDecalsIndex}";
            Plugin.log.LogInfo($"{gameObject.name} hit collider: {sprayHit.collider.gameObject.transform.GetPath()} (isTrigger: {sprayHit.collider.isTrigger}), tag: {sprayHit.collider.tag}, layer: {sprayHit.collider.gameObject.layer}");
            Plugin.log.LogInfo($"{gameObject.name} added to {gameObject.transform.parent.name} at {gameObject.transform.position}");
            #endif

        }
        return result;
    }

    

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(SprayPaintItem), "ItemInteractLeftRight")]
    public static void original_ItemInteractLeftRight(object instance, bool right) { throw new NotImplementedException("stub"); }
    
    public static float _shakeRestoreAmount() {
        return SessionData.ShakeEfficiency;
    }
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(SprayPaintItem), "ItemInteractLeftRight")]
    private static IEnumerable<CodeInstruction> transpiler_ItemInteractLeftRight(IEnumerable<CodeInstruction> instructions) {
        var foundShakeRestoreAmount = false;
        var addedWeedKillerSkip = false;
        foreach (var instruction in instructions) {
            if (!addedWeedKillerSkip) {
                addedWeedKillerSkip = true;
                foreach (var instr in _transpiler_AddWeedKillerSkip(instruction, typeof(Patches).GetMethod(nameof(original_ItemInteractLeftRight)))) { yield return instr; }
            } else if (!foundShakeRestoreAmount && instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.15f) {
                // Make shaking restore double the normal amount on the "shake meter"
                foundShakeRestoreAmount = true;
                yield return new CodeInstruction(OpCodes.Call, typeof(Patches).GetMethod(nameof(_shakeRestoreAmount)));
            } else {
                yield return instruction;
            }
        }
    }
}