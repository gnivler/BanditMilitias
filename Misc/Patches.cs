using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.VillageBehaviors;
using static Bandit_Militias.Helper;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global  
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Misc
{
    public class Patches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class CampaignOnInitializePatch
        {
            private static void Postfix()
            {
                Mod.Log("MapScreen.OnInitialize", LogLevel.Debug);
                var militias = MobileParty.All.Where(x => x != null && x.Name.Equals("Bandit Militia")).ToList();
                Mod.Log($"Militias: {militias.Count}", LogLevel.Debug);
                Flush();
                CalcMergeCriteria();
            }
        }

        [HarmonyPatch(typeof(FactionManager), "IsAtWarAgainstFaction")]
        public static class FactionManagerIsAtWarAgainstFactionPatch
        {
            // 1.4.2b vanilla code not optimized and checks against own faction
            private static bool Prefix(IFaction faction1, IFaction faction2, ref bool __result)
            {
                if (faction1 == faction2)
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FactionManager), "IsAlliedWithFaction")]
        public static class FactionManagerIsAlliedWithFactionPatch
        {
            // 1.4.2b vanilla code not optimized and checks against own faction  
            private static bool Prefix(IFaction faction1, IFaction faction2, ref bool __result)
            {
                if (faction1 == faction2)
                {
                    __result = true;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Campaign), "HourlyTick")]
        public static class CampaignHourlyTickPatch
        {
            private static int hoursPassed;

            private static void Postfix()
            {
                if (hoursPassed == 23)
                {
                    CalcMergeCriteria();
                    hoursPassed = 0;
                }

                hoursPassed++;
            }
        }

        // todo check if necessary after 1.4.2b is updated
        // looks like a vanilla bug
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "AddVillagersToParty")]
        public class VillagerCampaignBehaviorAddVillagersToPartyPatch
        {
            private static bool Prefix(MobileParty villagerParty)
            {
                if (villagerParty.Leader == null)
                {
                    Mod.Log("Aborting call to AddVillagersToParty because there is no leader", LogLevel.Warning);
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(DestroyPartyAction), "Apply")]
        public class DestroyPartyActionApplyPatch
        {
            private static bool Prefix(PartyBase destroyerParty, MobileParty destroyedParty)
            {
                if (destroyedParty == null)
                {
                    Mod.Log($"destroyedParty is null, abort DestroyPartyAction.Apply", LogLevel.Debug);
                    return false;
                }

                if (destroyerParty == null)
                {
                    Mod.Log($"destroyerParty is null, abort DestroyPartyAction.Apply", LogLevel.Debug);
                    return false;
                }

                return true;
            }
        }

        // just disperse small militias
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Postfix(MapEventSide __instance, PartyBase party)
            {
                if (party.Name.ToString() != "Bandit Militia")
                {
                    return;
                }

                if (party.PrisonRoster.Contains(Hero.MainHero.CharacterObject))
                {
                    return;
                }

                if (party.MemberRoster.TotalHealthyCount < Globals.Settings.MinPartySize &&
                    party.MemberRoster.TotalHealthyCount > 0 &&
                    party.PrisonRoster.Count < Globals.Settings.MinPartySize &&
                    __instance.Casualties > party.MemberRoster.Count / 2)
                {
                    Mod.Log($"Dispersing militia of {party.MemberRoster.TotalHealthyCount}+{party.MemberRoster.TotalWounded}w+{party.PrisonRoster.Count}p", LogLevel.Debug);
                    Trash(party.MobileParty);
                }
                else if (party.MemberRoster.Count >= Globals.Settings.MinPartySize &&
                         party.LeaderHero == null)
                {
                    var militias = Militia.All.Where(x => x.MobileParty == party.MobileParty);
                    foreach (var militia in militias)
                    {
                        Mod.Log("Reconfiguring", LogLevel.Debug);
                        militia.Configure();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BanditsCampaignBehavior), "CheckForSpawningBanditBoss")]
        public class BanditsCampaignBehaviorCheckForSpawningBanditBossPatch
        {
            private static bool Prefix(MobileParty mobileParty) => mobileParty != null;
        }
    }
}
