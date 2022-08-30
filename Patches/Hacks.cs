using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;

// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class Hacks
    {
        // rewrite of broken original in 1.8.0
        [HarmonyPatch(typeof(Hideout), "MapFaction", MethodType.Getter)]
        public static class HideoutMapFactionGetter
        {
            // ReSharper disable once RedundantAssignment
            public static bool Prefix(Hideout __instance, ref IFaction __result)
            {
                __result = Clan.BanditFactions.First(c => c.Culture == __instance.Settlement.Culture);
                return false;
            }
        }

        // game seems to dislike me removing parties on tick 3.9
        [HarmonyPatch(typeof(MobileParty), "GetFollowBehavior")]
        public static class MobilePartyGetFollowBehavior
        {
            public static bool Prefix(MobileParty __instance, MobileParty followedParty)
            {
                if (__instance.Army == null &&
                    followedParty is null)
                {
                    __instance.Ai.DisableForHours(1);
                    __instance.Ai.RethinkAtNextHourlyTick = true;
                    return false;
                }

                return true;
            }
        }

        // game seems to dislike me removing parties on tick 3.9
        [HarmonyPatch(typeof(MobileParty), "GetTotalStrengthWithFollowers")]
        public static class MobilePartyGetTotalStrengthWithFollowers
        {
            public static bool Prefix(MobileParty __instance, ref float __result)
            {
                if (__instance.Party.MobileParty.TargetParty == null)
                {
                    __result = __instance.Party.TotalStrength;
                    return false;
                }

                return true;
            }
        }

        // troops with missing data causing lots of NREs elsewhere
        // just a temporary patch
        public static void PurgeBadTroops()
        {
            DeferringLogger.Instance.Debug?.Log("Starting iteration of all troops in all parties, this might take a minute...");
            foreach (var mobileParty in MobileParty.All)
            {
                var rosters = new[] { mobileParty.MemberRoster, mobileParty.PrisonRoster };
                foreach (var roster in rosters)
                    while (roster.GetTroopRoster().AnyQ(t => t.Character.Name == null))
                        foreach (var troop in roster.GetTroopRoster())
                            if (troop.Character.Name == null)
                            {
                                DeferringLogger.Instance.Debug?.Log($"!!!!! Purge bad troop {troop.Character.StringId} from {mobileParty.Name}.  Prisoner? {roster.IsPrisonRoster}");
                                roster.AddToCounts(troop.Character, -troop.Number);
                                //Globals.BanditMilitiaCharacters.Remove(troop.Character);
                                Globals.Troops.Remove(troop.Character);
                                MBObjectManager.Instance.UnregisterObject(troop.Character);
                            }
            }

            foreach (var settlement in Settlement.All)
            {
                var rosters = new[] { settlement.Party.MemberRoster, settlement.Party.PrisonRoster };
                foreach (var roster in rosters)
                    while (roster.GetTroopRoster().AnyQ(t => t.Character.Name == null))
                        foreach (var troop in roster.GetTroopRoster())
                            if (troop.Character.Name == null)
                            {
                                DeferringLogger.Instance.Debug?.Log($"!!!!! Purge bad troop {troop.Character.StringId} from {settlement.Name}.  Prisoner? {roster.IsPrisonRoster}");
                                roster.AddToCounts(troop.Character, -troop.Number);
                                //Globals.BanditMilitiaCharacters.Remove(troop.Character);
                                MBObjectManager.Instance.UnregisterObject(troop.Character);
                            }
            }
        }

        // throws during nuke (apparently not in 3.9)
        // parameters are included for debugging
        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXpPatch
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null)
                    DeferringLogger.Instance.Debug?.Log(__exception);
                return null;
            }
        }
    }
}
