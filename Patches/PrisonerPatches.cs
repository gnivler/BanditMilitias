using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;

// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace Bandit_Militias.Patches
{
    public static class PrisonerPatches
    {
        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                if (__instance.DefeatedSide is BattleSideEnum.None)
                {
                    return;
                }

                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent
                                || !string.IsNullOrEmpty(p.Party?.MobileParty?.LeaderHero?.CharacterObject?.StringId)
                                && p.Party.MobileParty.LeaderHero.CharacterObject.StringId.Contains("Bandit_Militia"));
                foreach (var party in loserBMs)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero).ToListQ();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($">>> Killing {heroes[i].Character.Name} ({heroes[i].Character.StringId}) at FinishBattle.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }

                    if (party.Party.MobileParty.LeaderHero is null)
                    {
                        party.Party.MobileParty.SetCustomName(new TextObject("Leaderless Bandit Militia"));
                    }

                    Helper.RemoveUndersizedTracker(party.Party);
                }

                Helper.DoPowerCalculations();
            }
        }

        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public static class MapEventLootDefeatedPartiesPatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent
                                || !string.IsNullOrEmpty(p.Party?.MobileParty?.LeaderHero?.CharacterObject?.StringId)
                                && p.Party.MobileParty.LeaderHero.CharacterObject.StringId.Contains("Bandit_Militia"));
                foreach (var party in loserBMs)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero).ToListQ();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($">>> Killing {heroes[i].Character.Name} at LootDefeatedParties.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }
                }

                Helper.DoPowerCalculations();
            }
        }
    }
}
