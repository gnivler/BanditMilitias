using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Patches
{
    public static class Hacks
    {
        // rewrite of broken original in 1.8.0
        [HarmonyPatch(typeof(Hideout), "MapFaction", MethodType.Getter)]
        public static class HideoutMapFactionGetter
        {
            public static bool Prefix(Hideout __instance, ref IFaction __result)
            {
                __result = Clan.BanditFactions.First(c => c.Culture == __instance.Settlement.Culture);
                return false;
            }
        }

        [HarmonyPatch(typeof(MobileParty), "GetFollowBehavior")]
        public static class MobilePartyGetFollowBehavior
        {
            public static bool Prefix(MobileParty __instance, MobileParty followedParty)
            {
                if (followedParty is null)
                {
                    __instance.SetMoveGoToPoint(__instance.Position2D);
                    __instance.Ai.RethinkAtNextHourlyTick = true;
                    return false;
                }

                return true;
            }
        }

        // troops with missing data causing lots of NREs elsewhere
        // just a temporary patch
        public static void HackPurgeAllBadTroopsFromAllParties()
        {
            Log("Starting iteration of all troops in all parties, this might take a minute...");
            foreach (var mobileParty in MobileParty.All)
            {
                var rosters = new[] { mobileParty.MemberRoster, mobileParty.PrisonRoster };
                foreach (var roster in rosters)
                {
                    while (roster.GetTroopRoster().AnyQ(t => t.Character.Name == null))
                    {
                        foreach (var troop in roster.GetTroopRoster())
                        {
                            if (troop.Character.Name == null)
                            {
                                Log($"!!!!! Purge bad troop {troop.Character.StringId} from {mobileParty.Name}.  Prisoner? {roster.IsPrisonRoster}");
                                roster.AddToCounts(troop.Character, -troop.Number);
                                Globals.BanditMilitiaCharacters.Remove(troop.Character);
                                MBObjectManager.Instance.UnregisterObject(troop.Character);
                            }
                        }
                    }
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
                    Log(__exception);
                return null;
            }
        }
    }
}
