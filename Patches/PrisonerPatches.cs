using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Helpers.Helper;

// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace BanditMilitias.Patches
{
    public static class PrisonerPatches
    {
        [HarmonyPatch(typeof(MapEventSide), "OnPartyDefeated")]
        public static class sadfijasdfoiajs
        {
            public static void Prefix(PartyBase defeatedParty)
            {
                foreach (var troop in defeatedParty.MemberRoster.ToFlattenedRoster().Troops.WhereQ(t => t.StringId.Contains("Bandit_Militia_Troop")))
                {
                    MBObjectManager.Instance.UnregisterObject(troop);
                }
            }
        }

        //[HarmonyPatch(typeof(MobileParty), "RemoveParty")]
        //public static class DestroyPartyActionPatch
        //{
        //    public static void Prefix(MobileParty __instance)
        //    {
        //        if (__instance.IsBM())
        //        {
        //            foreach (var troop in __instance.MemberRoster.ToFlattenedRoster().Troops.WhereQ(t => t.StringId.Contains("Bandit_Militia_Troop")))
        //            {
        //                Log($"---- Unregistering {troop.StringId} from {__instance.StringId} at RemoveParty");
        //                Log(new StackTrace());
        //                MBObjectManager.Instance.UnregisterObject(troop);
        //            }
        //        }
        //    }
        //}

        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner) return;
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);
                foreach (var party in loserBMs)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero).ToListQ();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Log($"<<< Killing {heroes[i].Character.Name} ({heroes[i].Character.StringId}) at FinishBattle");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }

                    if (party.Party.MobileParty.LeaderHero is null)
                        party.Party.MobileParty.SetCustomName(new TextObject(Globals.Settings.LeaderlessBanditMilitiaString));

                    RemoveUndersizedTracker(party.Party);
                }

                DoPowerCalculations();
            }
        }

        // prevents BM hero prisoners being taken after battle
        // upgrades all troops with any looted equipment in Postfix
        // drops Avoidance scores when BMs win
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public static class MapEventLootDefeatedPartiesPatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner) return;
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);

                foreach (var party in loserBMs)
                {
                    // Globals.LootRecord.Remove(party.Party.MapEventSide);
                    // disperse small militias
                    if (party.Party.MobileParty.MemberRoster.TotalManCount < Globals.Settings.DisperseSize)
                    {
                        Trash(party.Party.MobileParty);
                        continue;
                    }

                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero).ToListQ();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Log($"<<< Killing {heroes[i].Character.Name} at LootDefeatedParties.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }

                    //foreach (var troop in party.Party.MemberRoster.ToFlattenedRoster().Troops)
                    //{
                    //    if (troop.StringId.Contains("Bandit_Militia_Troop"))
                    //        MBObjectManager.Instance.UnregisterObject(troop);
                    //}

                    RemoveUndersizedTracker(party.Party);
                }

                DoPowerCalculations();
            }

            public static void Postfix(MapEvent __instance)
            {
                if (!__instance.HasWinner) return;
                var winnerBMs = __instance.PartiesOnSide(__instance.WinningSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).ToListQ();
                if (!winnerBMs.Any()) return;

                var loserHeroes = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .SelectQ(mep => mep.Party.Owner).Where(h => h is not null).ToListQ();
                foreach (var BM in winnerBMs)
                    DecreaseAvoidance(loserHeroes, BM);
            }
        }
    }
}
