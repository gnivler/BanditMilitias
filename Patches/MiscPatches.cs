using System;
using System.Collections.Generic;
using BanditMilitias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using static BanditMilitias.Helpers.Helper;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    Nuke();
            }

            public static void Postfix()
            {
                InitMap();
            }
        }

        [HarmonyPatch(typeof(MapMobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MapMobilePartyTrackerVMCtorPatch
        {
            public static void Postfix(MapMobilePartyTrackerVM __instance) => Globals.MapMobilePartyTrackerVM = __instance;
        }

        [HarmonyPatch(typeof(SaveableCampaignTypeDefiner), "DefineContainerDefinitions")]
        public class SaveableCampaignTypeDefinerDefineContainerDefinitions
        {
            public static void Postfix(SaveableCampaignTypeDefiner __instance)
            {
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<Hero, float>) });
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<string, Equipment>) });
            }
        }

        [HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
        public static class MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        // ServeAsSoldier issue where the MobileParty isn't a quest party
        internal static void PatchSaSDeserters(ref MobileParty __result)
        {
            Traverse.Create(__result).Field<bool>("IsCurrentlyUsedByAQuest").Value = true;
        }

        // // the people, they want more bandits!
        // // quick graphing indicates BM drops the average count by about 5% so I set the default to this
        // [HarmonyPatch(typeof(BanditsCampaignBehavior), "IdealBanditPartyCount", MethodType.Getter)]
        // public static class BanditsCampaignBehaviorIdealPartyCountGet
        // {
        //     public static void Postfix(ref int __result)
        //     {
        //         __result *= Globals.Settings.idealBoostFactor;
        //     }
        // }
        //
        // [HarmonyPatch(typeof(BanditsCampaignBehavior), "SpawnBanditOrLooterPartiesAroundAHideoutOrSettlement")]
        // public static class BanditsCampaignBehaviorSpawnBanditOrLooterPartiesAroundAHideoutOrSettlement
        // {
        //     public static void Prefix(ref int numberOfBanditsWillBeSpawned)
        //     {
        //         numberOfBanditsWillBeSpawned *= Globals.Settings.idealBoostFactor;
        //     }
        //
        //     public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        //     {
        //         var codes = new List<CodeInstruction>(instructions);
        //         int target = -1;
        //         for (var index = 0; index < codes.Count; index++)
        //         {
        //             if (codes[index].opcode == OpCodes.Starg_S
        //                 && codes[index + 1].opcode == OpCodes.Ldc_I4_0
        //                 && codes[index + 2].opcode == OpCodes.Stloc_S
        //                 && codes[index + 3].opcode == OpCodes.Br
        //                 && codes[index + 4].opcode == OpCodes.Ldnull)
        //                 target = index;
        //         }
        //
        //         var stack = new List<CodeInstruction>
        //         {
        //             new(OpCodes.Ldsfld, AccessTools.Field(typeof(Globals), nameof(Globals.Settings))),
        //             new(OpCodes.Ldfld, AccessTools.Field(typeof(Settings), nameof(Settings.idealBoostFactor))),
        //             new(OpCodes.Mul)
        //         };
        //         codes.InsertRange(target, stack);
        //         return codes;
        //     }
        // }
    }
}
