using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Helpers.Helper.Globals;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global   
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public class MilitiaPatches
    {
        // swapped (copied) two very similar methods in assemblies, one was throwing one wasn't
       //[HarmonyPatch(typeof(NearbyBanditBaseIssueBehavior), "FindSuitableHideout")]
       //public static class NearbyBanditBaseIssueBehaviorFindSuitableHideoutPatch
       //{
       //    private const float floatMaxValue = float.MaxValue;

       //    // taken from CapturedByBountyHuntersIssue because this class' version throws
       //    private static bool Prefix(Hero issueOwner, ref Settlement __result)
       //    {
       //        foreach (var settlement in Settlement.FindAll(x => x.Hideout != null))
       //        {
       //            if (Campaign.Current.Models.MapDistanceModel.GetDistance(issueOwner.GetMapPoint(),
       //                    settlement, 55f, out var num2) &&
       //                num2 < floatMaxValue)
       //            {
       //                __result = settlement;
       //            }
       //        }

       //        return false;
       //    }
       //}

        [HarmonyPatch(typeof(PartyVisual), "AddCharacterToPartyIcon")]
        public class PartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (characterObject.Name.Equals("Bandit Militia"))
                {
                    bannerKey = Militia.FindMilitiaByParty(characterObject.HeroObject.PartyBelongedTo).Banner.Serialize();
                }
            }
        }

        [HarmonyPatch(typeof(PartyBase), "Banner", MethodType.Getter)]
        public class PartyBaseBannerPatch
        {
            private static void Postfix(PartyBase __instance, ref Banner __result)
            {
                if (Globals.Settings.RandomBanners &&
                    __instance.Name.Equals("Bandit Militia"))
                {
                    __result = Militia.FindMilitiaByParty(__instance.MobileParty)?.Banner ?? __result;
                }
            }
        }

        [HarmonyPatch(typeof(MobileParty), "DailyTick")]
        public static class MobilePartyDailyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (!IsValidParty(__instance))
                {
                    return;
                }

                // check daily each bandit party against the size factor and a random chance to split up
                TrySplitParty(__instance);
            }
        }

        // where militias try to find each other and merge
        [HarmonyPatch(typeof(MobileParty), "HourlyTick")]
        public static class MobilePartyHourlyTickPatch
        {
            private static Stopwatch t = new Stopwatch();

            private static void Postfix(MobileParty __instance)
            {
                t.Restart();
                if (!IsValidParty(__instance))
                {
                    return;
                }

                var targetParty = MobileParty.FindPartiesAroundPosition(__instance.Position2D, MergeDistance * 1.33f,
                    x => x != __instance && x.IsBandit && IsValidParty(x)).GetRandomElement()?.Party;

                // "nobody" is a valid answer
                if (targetParty == null)
                {
                    return;
                }

                // ignore units which are close together
                if (targetParty.Position2D.Distance(__instance.Position2D) < MergeDistance / 2 ||
                    Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, __instance) > MergeDistance)
                {
                    if (!IsMovingToBandit(targetParty.MobileParty, __instance) &&
                        !IsMovingToBandit(__instance, targetParty.MobileParty))

                    {
                        Mod.Log($"{__instance} Seeking target {targetParty.MobileParty}");
                        Traverse.Create(__instance).Method("SetNavigationModeParty", targetParty.MobileParty).GetValue();
                    }

                    return;
                }

                var troopCount = __instance.MemberRoster.Count + targetParty.MemberRoster.Count;
                var militiaTotalCount = troopCount;
                if (militiaTotalCount > Globals.Settings.MaxPartySize ||
                    militiaTotalCount > CalculatedMaxPartySize ||
                    __instance.Party.TotalStrength > CalculatedMaxPartyStrength ||
                    NumMountedTroops(__instance.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > troopCount / 2)
                {
                    return;
                }

                if (Settlement.FindSettlementsAroundPosition(__instance.Position2D, MinDistanceFromHideout, x => x.IsHideout()).Any())
                {
                    return;
                }

                // create a new party merged from the two
                var rosters = MergeRosters(__instance, targetParty);
                var militia = new Militia(__instance.Position2D, rosters[0], rosters[1]);
                // teleport new militias near the player
                if (testingMode)
                {
                    militia.MobileParty.Position2D = Hero.MainHero.PartyBelongedTo.Position2D +
                                                     new Vec2(MBRandom.RandomFloatRanged(-3f, 3f), MBRandom.RandomFloatRanged(-3f, 3));
                }

                militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(__instance);
                Trash(targetParty.MobileParty);
                Mod.Log($"finished ==> {t.ElapsedTicks / 10000F:F3}ms");
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public class EnterSettlementActionApplyForPartyPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty.Name.Equals("Bandit Militia"))
                {
                    Mod.Log($"Preventing {mobileParty} from entering {settlement}");
                    mobileParty.SetMovePatrolAroundSettlement(settlement);
                    return false;
                }

                return true;
            }
        }
    }
}
