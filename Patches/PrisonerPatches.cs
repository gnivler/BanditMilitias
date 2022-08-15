using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Helpers.Helper;

// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace BanditMilitias.Patches
{
    public static class PrisonerPatches
    {
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
            public static void Prefix(MapEvent __instance, object lootCollector)
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
