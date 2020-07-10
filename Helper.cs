using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public static class Helper
    {
        public static class Globals
        {
            // dev
            internal static bool testingMode;
            internal static LogLevel logging = LogLevel.Disabled;

            // how far to look
            internal const float SearchRadius = 25;

            // how close before merging
            internal const float MergeDistance = 3.5f;
            internal const float MinDistanceFromHideout = 15;

            // thresholds for splitting
            internal const float StrengthSplitFactor = 0.8f;
            internal const float SizeSplitFactor = 0.8f;
            internal const float RandomSplitChance = 0.25f;

            // adjusts size and strengths
            internal const float PartyStrengthFactor = 0.8f;
            internal const float MaxPartySizeFactor = 0.8f;

            // holders for criteria
            internal static float HeroPartyStrength;
            internal static float MaxPartyStrength;
            internal static double MaxPartySize;

            // misc
            internal static readonly Random Rng = new Random();
            internal static List<MapEvent> MapEvents;

            // temporary unfuckery
            internal static readonly string[] replacementData =
            {
                "infantry_heavyinfantry_level1_template_skills",
                "infantry_heavyinfantry_level6_template_skills",
                "infantry_heavyinfantry_level11_template_skills",
                "infantry_heavyinfantry_level16_template_skills",
                "infantry_heavyinfantry_level21_template_skills",
                "infantry_heavyinfantry_level26_template_skills",
                "infantry_heavyinfantry_level31_template_skills",
                "infantry_heavyinfantry_highestlevel_template_skills",
                "cavalry_lightcavalry_heavycavalry_level1_template_skills",
                "cavalry_lightcavalry_heavycavalry_level6_template_skills",
                "cavalry_lightcavalry_heavycavalry_level11_template_skills",
                "cavalry_lightcavalry_heavycavalry_level16_template_skills",
                "cavalry_lightcavalry_heavycavalry_level21_template_skills",
                "cavalry_lightcavalry_heavycavalry_level26_template_skills",
                "cavalry_lightcavalry_heavycavalry_level31_template_skills",
                "cavalry_lightcavalry_heavycavalry_highestlevel_template_skills",
                "ranged_skirmisher_level1_template_skills",
                "ranged_skirmisher_level6_template_skills",
                "ranged_skirmisher_level11_template_skills",
                "ranged_skirmisher_level16_template_skills",
                "ranged_skirmisher_level21_template_skills",
                "ranged_skirmisher_level26_template_skills",
                "ranged_skirmisher_level31_template_skills",
                "ranged_skirmisher_highestlevel_template_skills",
                "horsearcher_level1_template_skills",
                "horsearcher_level6_template_skills",
                "horsearcher_level11_template_skills",
                "horsearcher_level16_template_skills",
                "horsearcher_level21_template_skills",
                "horsearcher_level26_template_skills",
                "horsearcher_level31_template_skills",
                "horsearcher_highestlevel_template_skills",
                "custombattle_commander_1_template_skills",
                "custombattle_commander_2_template_skills",
                "custombattle_commander_3_template_skills",
                "custombattle_commander_4_template_skills",
                "custombattle_commander_5_template_skills",
                "custombattle_commander_6_template_skills",
                "custombattle_commander_7_template_skills",
                "npc_wanderer_equipment_template_battania",
                "npc_armed_wanderer_equipment_template_battania",
                "npc_wanderer_equipment_template_empire",
                "npc_armed_wanderer_equipment_template_empire",
                "npc_wanderer_equipment_template_aserai",
                "npc_armed_wanderer_equipment_template_aserai",
                "npc_armed_wanderer_equipment_template_khuzait",
                "npc_companion_equipment_template_khuzait",
                "npc_wanderer_equipment_template_vlandia",
                "npc_armed_wanderer_equipment_template_vlandia",
                "npc_companion_equipment_template_vlandia",
                "npc_companion_equipment_template_battania",
                "npc_wanderer_equipment_template_sturgia",
                "npc_armed_wanderer_equipment_template_sturgia",
                "npc_wanderer_equipment_template_khuzait",
                "empire_lord_lady_battle_equipment",
                "khuzait_lord_lady_battle_equipment",
                "battania_lord_lady_battle_equipment",
                "sturgia_lord_lady_battle_equipment",
                "vlandia_lord_lady_battle_equipment",
                "aserai_lord_lady_battle_equipment",
                "aserai_lord_lady_battle_equipment_heavy",
                "battania_lord_lady_battle_equipment_heavy",
                "empire_lord_lady_battle_equipment_heavy",
                "khuzait_lord_lady_battle_equipment_heavy",
                "sturgia_lord_lady_battle_equipment_heavy",
                "vlandia_lord_lady_battle_equipment_heavy",
                "aserai_unarmored_noble_equipment",
                "battania_unarmored_noble_equipment",
                "empire_unarmored_noble_equipment",
                "khuzait_unarmored_noble_equipment",
                "sturgia_unarmored_noble_equipment",
                "vlandia_unarmored_noble_equipment",
                "npc_companion_equipment_template_sturgia",
                "npc_companion_equipment_template_aserai",
                "civillian_template_gangster_tier1_empire",
                "civillian_template_gangster_tier2_empire",
                "civillian_template_gangster_tier3_empire",
                "aserai_troop_civilian_template_t1",
                "aserai_troop_civilian_template_t2",
                "aserai_troop_civilian_template_t3",
                "battania_troop_civilian_template_t1",
                "battania_troop_civilian_template_t2",
                "battania_troop_civilian_template_t3",
                "empire_troop_civilian_template_t1",
                "empire_troop_civilian_template_t2",
                "empire_troop_civilian_template_t3",
                "khuzait_troop_civilian_template_t1",
                "khuzait_troop_civilian_template_t2",
                "khuzait_troop_civilian_template_t3",
                "sturgia_troop_civilian_template_t1",
                "sturgia_troop_civilian_template_t2",
                "sturgia_troop_civilian_template_t3",
                "vlandia_troop_civilian_template_t1",
                "vlandia_troop_civilian_template_t2",
                "vlandia_troop_civilian_template_t3",
                "npc_companion_equipment_template_empire",
                "player_char_creation_empire_10_m",
                "player_char_creation_empire_10_f",
                "player_char_creation_vlandia_10_m",
                "player_char_creation_vlandia_10_f",
                "player_char_creation_battania_10_m",
                "player_char_creation_battania_10_f",
                "player_char_creation_khuzait_10_m",
                "player_char_creation_khuzait_10_f",
                "player_char_creation_empire_9_m",
                "player_char_creation_empire_9_f",
                "player_char_creation_khuzait_9_m",
                "player_char_creation_khuzait_9_f",
                "player_char_creation_aserai_9_m",
                "player_char_creation_aserai_9_f",
                "player_char_creation_vlandia_9_m",
                "player_char_creation_vlandia_9_f",
                "player_char_creation_battania_9_m",
                "player_char_creation_battania_9_f",
                "player_char_creation_sturgia_9_m",
                "player_char_creation_sturgia_9_f",
                "player_char_creation_battania_8_m",
                "player_char_creation_battania_8_f",
                "player_char_creation_aserai_1_m",
                "player_char_creation_aserai_1_f",
                "player_char_creation_aserai_2_m",
                "player_char_creation_aserai_2_f",
                "player_char_creation_aserai_3_m",
                "player_char_creation_aserai_3_f",
                "player_char_creation_aserai_4_m",
                "player_char_creation_aserai_4_f",
                "player_char_creation_aserai_5_m",
                "player_char_creation_aserai_5_f",
                "player_char_creation_aserai_6_m",
                "player_char_creation_aserai_6_f",
                "player_char_creation_battania_1_m",
                "player_char_creation_battania_1_f",
                "player_char_creation_battania_2_m",
                "player_char_creation_battania_2_f",
                "player_char_creation_battania_3_m",
                "player_char_creation_battania_3_f",
                "player_char_creation_battania_4_m",
                "player_char_creation_battania_4_f",
                "player_char_creation_battania_5_m",
                "player_char_creation_battania_5_f",
                "player_char_creation_battania_6_m",
                "player_char_creation_battania_6_f",
                "player_char_creation_empire_1_m",
                "player_char_creation_empire_1_f",
                "player_char_creation_empire_2_m",
                "player_char_creation_empire_2_f",
                "player_char_creation_empire_3_m",
                "player_char_creation_empire_3_f",
                "player_char_creation_empire_4_m",
                "player_char_creation_empire_4_f",
                "player_char_creation_empire_5_m",
                "player_char_creation_empire_5_f",
                "player_char_creation_empire_6_m",
                "player_char_creation_empire_6_f",
                "player_char_creation_khuzait_1_m",
                "player_char_creation_khuzait_1_f",
                "player_char_creation_khuzait_2_m",
                "player_char_creation_khuzait_2_f",
                "player_char_creation_khuzait_3_m",
                "player_char_creation_khuzait_3_f",
                "player_char_creation_khuzait_4_m",
                "player_char_creation_khuzait_4_f",
                "player_char_creation_khuzait_5_m",
                "player_char_creation_khuzait_5_f",
                "player_char_creation_khuzait_6_m",
                "player_char_creation_khuzait_6_f",
                "player_char_creation_vlandia_1_m",
                "player_char_creation_vlandia_1_f",
                "player_char_creation_vlandia_2_m",
                "player_char_creation_vlandia_2_f",
                "player_char_creation_vlandia_3_m",
                "player_char_creation_vlandia_3_f",
                "player_char_creation_vlandia_4_m",
                "player_char_creation_vlandia_4_f",
                "player_char_creation_vlandia_5_m",
                "player_char_creation_vlandia_5_f",
                "player_char_creation_vlandia_6_m",
                "player_char_creation_vlandia_6_f",
                "player_char_creation_sturgia_1_m",
                "player_char_creation_sturgia_1_f",
                "player_char_creation_sturgia_2_m",
                "player_char_creation_sturgia_2_f",
                "player_char_creation_sturgia_3_m",
                "player_char_creation_sturgia_3_f",
                "player_char_creation_sturgia_4_m",
                "player_char_creation_sturgia_4_f",
                "player_char_creation_sturgia_5_m",
                "player_char_creation_sturgia_5_f",
                "player_char_creation_sturgia_6_m",
                "player_char_creation_sturgia_6_f",
                "brother_char_creation_sturgia",
                "brother_char_creation_aserai",
                "brother_char_creation_empire",
                "brother_char_creation_vlandia",
                "brother_char_creation_khuzait",
                "brother_char_creation_battania",
                "tournament_template_aserai_one_participant_set_v1",
                "tournament_template_aserai_two_participant_set_v1",
                "tournament_template_aserai_two_participant_set_v2",
                "tournament_template_aserai_two_participant_set_v3",
                "tournament_template_aserai_four_participant_set_v1",
                "tournament_template_aserai_four_participant_set_v2",
                "tournament_template_aserai_four_participant_set_v3",
                "tournament_template_aserai_four_participant_set_v4",
                "tournament_template_battania_one_participant_set_v1",
                "tournament_template_battania_one_participant_set_v2",
                "tournament_template_battania_two_participant_set_v1",
                "tournament_template_battania_two_participant_set_v2",
                "tournament_template_battania_two_participant_set_v3",
                "tournament_template_battania_two_participant_set_v4",
                "tournament_template_battania_two_participant_set_v5",
                "tournament_template_battania_four_participant_set_v1",
                "tournament_template_battania_four_participant_set_v2",
                "tournament_template_battania_four_participant_set_v3",
                "tournament_template_empire_one_participant_set_v1",
                "tournament_template_empire_two_participant_set_v1",
                "tournament_template_empire_two_participant_set_v2",
                "tournament_template_empire_two_participant_set_v3",
                "tournament_template_empire_four_participant_set_v1",
                "tournament_template_empire_four_participant_set_v2",
                "tournament_template_empire_four_participant_set_v3",
                "tournament_template_khuzait_one_participant_set_v1",
                "tournament_template_khuzait_one_participant_set_v2",
                "tournament_template_khuzait_two_participant_set_v1",
                "tournament_template_khuzait_two_participant_set_v2",
                "tournament_template_khuzait_two_participant_set_v3",
                "tournament_template_khuzait_four_participant_set_v1",
                "tournament_template_khuzait_four_participant_set_v2",
                "tournament_template_khuzait_four_participant_set_v3",
                "tournament_template_vlandia_one_participant_set_v1",
                "tournament_template_vlandia_one_participant_set_v2",
                "tournament_template_vlandia_one_participant_set_v3",
                "tournament_template_vlandia_two_participant_set_v1",
                "tournament_template_vlandia_two_participant_set_v2",
                "tournament_template_vlandia_two_participant_set_v3",
                "tournament_template_vlandia_two_participant_set_v4",
                "tournament_template_vlandia_four_participant_set_v1",
                "tournament_template_vlandia_four_participant_set_v2",
                "tournament_template_vlandia_four_participant_set_v3",
                "tournament_template_vlandia_four_participant_set_v4",
                "tournament_template_sturgia_one_participant_set_v1",
                "tournament_template_sturgia_one_participant_set_v2",
                "tournament_template_sturgia_two_participant_set_v1",
                "tournament_template_sturgia_two_participant_set_v2",
                "tournament_template_sturgia_two_participant_set_v3",
                "tournament_template_sturgia_four_participant_set_v1",
                "tournament_template_sturgia_four_participant_set_v2",
                "tournament_template_sturgia_four_participant_set_v3",
                "npc_disguised_hero_template",
                "unarmed_ai",
                "unarmed_ai_2",
                "main_hero_for_perf",
                "crazy_man_for_perf",
                "champion_fighter_for_perf",
                "player_char_creation_gamescom_1_m",
                "player_char_creation_gamescom_2_m",
                "player_char_creation_gamescom_3_f",
                "dummy_no_armor",
                "dummy_light_armor",
                "dummy_medium_armor",
                "dummy_heavy_armor",
                "npc_wanderer_equipment_empire",
                "npc_wanderer_equipment_vlandia",
                "npc_wanderer_equipment_battania",
                "npc_wanderer_equipment_sturgia",
                "npc_wanderer_equipment_aserai",
                "npc_poor_wanderer_khuzait",
                "npc_artisan_equipment_empire",
                "npc_artisan_equipment_aserai",
                "npc_artisan_equipment_khuzait",
                "npc_artisan_equipment_battania",
                "npc_artisan_equipment_sturgia",
                "npc_artisan_equipment_vlandia",
                "npc_gang_leader_equipment_empire",
                "npc_gang_leader_equipment_aserai",
                "npc_gang_leader_equipment_khuzait",
                "npc_gang_leader_equipment_battania",
                "npc_gang_leader_equipment_sturgia",
                "npc_gang_leader_equipment_vlandia",
                "npc_preacher_equipment_empire",
                "npc_preacher_equipment_aserai",
                "npc_preacher_equipment_khuzait",
                "npc_preacher_equipment_battania",
                "npc_preacher_equipment_sturgia",
                "npc_preacher_equipment_vlandia",
                "npc_merchant_equipment_empire",
                "npc_merchant_equipment_aserai",
                "npc_merchant_equipment_khuzait",
                "npc_merchant_equipment_battania",
                "npc_merchant_equipment_sturgia",
                "npc_merchant_equipment_vlandia",
                "npc_gentry_equipment_empire",
                "npc_gentry_equipment_vlandia",
                "tournament_template_battania_two_participant_set_v5__ft_female",
                "tournament_template_battania_four_participant_set_v3__ft_female",
                "tournament_template_vlandia_four_participant_set_v3__ft_female",
                "tournament_template_vlandia_four_participant_set_v4__ft_female",
                "tournament_template_aserai_two_participant_set_v3__ft_female",
                "tournament_template_aserai_four_participant_set_v4__ft_female",
                "tournament_template_khuzait_one_participant_set_v2__ft_female",
                "tournament_template_vlandia_one_participant_set_v3__ft_female",
                "tournament_template_vlandia_two_participant_set_v4__ft_female",
                "tournament_template_khuzait_two_participant_set_v2__ft_female",
                "tournament_template_khuzait_four_participant_set_v2__ft_female",
                "tournament_template_khuzait_four_participant_set_v3__ft_female"
            };
        }

        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.Troops.Where(x => x.IsMounted)
                .Sum(troopRoster.GetTroopCount);
        }

        private static float Variance => MBRandom.RandomFloatRanged(0.5f, 1.5f);

        internal static void CalcMergeCriteria()
        {
            // first campaign init hasn't populated this apparently
            var parties = MobileParty.All.Where(
                x => x.LeaderHero != null && !x.Name.Equals("Bandit Militia")).ToList();
            if (parties.Any())
            {
                HeroPartyStrength = parties.Select(x => x.Party.TotalStrength).Average();
                // reduce strength
                MaxPartyStrength = HeroPartyStrength * PartyStrengthFactor * Variance;
                // maximum size grows over time as clans level up
                MaxPartySize = Math.Round(MobileParty.All
                    .Where(x => x.LeaderHero != null && !x.IsBandit).Select(x => x.Party.PartySizeLimit).Average());
                MaxPartySize *= MaxPartySizeFactor * Variance;
                Mod.Log($"Daily calculations => size: {MaxPartySize:0} strength: {MaxPartyStrength:0}", LogLevel.Debug);
            }
        }

        internal static void TrySplitUpParty(MobileParty __instance)
        {
            if (__instance.Party.MemberRoster.TotalManCount < 50)
            {
                return;
            }

            if (__instance.IsTooBusyToMerge())
            {
                return;
            }

            if (!__instance.Name.Equals("Bandit Militia"))
            {
                return;
            }

            var roll = Rng.NextDouble();
            if (__instance.MemberRoster.TotalManCount == 0 ||
                roll > RandomSplitChance ||
                !__instance.Name.Equals("Bandit Militia") ||
                __instance.Party.TotalStrength <= MaxPartyStrength * StrengthSplitFactor * Variance ||
                __instance.Party.MemberRoster.TotalManCount <= MaxPartySize * SizeSplitFactor * Variance)
            {
                return;
            }

            var party1 = new TroopRoster();
            var party2 = new TroopRoster();
            var prisoners1 = new TroopRoster();
            var prisoners2 = new TroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total", LogLevel.Debug);
                foreach (var rosterElement in original.MemberRoster)
                {
                    SplitRosters(troops1, troops2, rosterElement);
                }

                if (original.PrisonRoster.TotalManCount > 0)
                {
                    Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total", LogLevel.Debug);
                    foreach (var rosterElement in original.PrisonRoster)
                    {
                        SplitRosters(prisoners1, prisoners2, rosterElement);
                    }
                }

                foreach (var item in original.ItemRoster)
                {
                    var half = Math.Max(1, item.Amount / 2);
                    inventory1.AddToCounts(item.EquipmentElement, half);
                    var remainder = item.Amount % 2;
                    if (half > 2)
                    {
                        inventory2.AddToCounts(item.EquipmentElement, half + remainder);
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            var half = Math.Max(1, rosterElement.Number / 2);
            troops1.AddToCounts(rosterElement.Character, half);
            var remainder = rosterElement.Number % 2;
            // 1-3 will have a half of 1.  4 would be 2, and worth adding to second roster
            if (half > 2)
            {
                troops2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                var militia1 = new Militia(original.Position2D, party1, prisoners1);
                var militia2 = new Militia(original.Position2D, party2, prisoners2);
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                Mod.Log($"{militia1.MobileParty.MapFaction.Name} <<< Split >>> {militia2.MobileParty.MapFaction.Name}", LogLevel.Info);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (!__instance.Party.IsMobile ||
                __instance.CurrentSettlement != null ||
                __instance.Party.MemberRoster.TotalManCount == 0 ||
                __instance.IsCurrentlyUsedByAQuest ||
                __instance.IsTooBusyToMerge())
            {
                return false;
            }

            return __instance.IsBandit && !__instance.IsBanditBossParty;
        }

        // dumps all bandit heroes (shouldn't be more than 2 though...)
        internal static TroopRoster[] MergeRosters(MobileParty __instance, PartyBase targetParty)
        {
            var troopRoster = new TroopRoster();
            var faction = __instance.MapFaction;
            foreach (var troopRosterElement in __instance.MemberRoster
                .Where(x => x.Character?.HeroObject == null &&
                            Clan.BanditFactions.Contains(faction)))
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            foreach (var troopRosterElement in targetParty.MemberRoster
                .Where(x => x.Character?.HeroObject == null &&
                            Clan.BanditFactions.Contains(faction)))
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            var prisonerRoster = new TroopRoster();
            foreach (var troopRosterElement in __instance.PrisonRoster)
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            foreach (var troopRosterElement in targetParty.PrisonRoster)
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            return new[]
            {
                troopRoster,
                prisonerRoster
            };
        }

        internal static void FinalizeBadMapEvents()
        {
            if (MapEvents == null || MapEvents.Count == 0)
            {
                return;
            }

            foreach (var mapEvent in MapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
            {
                // bug added IsFinished, does it work?  I don't want it to clear parties that the game would be dealing with
                if (mapEvent.AttackerSide.TroopCount == 0 ||
                    mapEvent.DefenderSide.TroopCount == 0 &&
                    mapEvent.IsFinished)
                {
                    Mod.Log($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}", LogLevel.Info);
                    mapEvent.FinalizeEvent();
                }
            }
        }

        private static readonly AccessTools.FieldRef<IssueManager, Dictionary<Hero, IssueBase>> issuesRef =
            AccessTools.FieldRefAccess<IssueManager, Dictionary<Hero, IssueBase>>("_issues");

        internal static void PurgeNullRefDescriptionIssues(bool logOnlyMode = false)
        {
            var purgeList = new List<Hero>();
            foreach (var issue in Campaign.Current.IssueManager.Issues)
            {
                try
                {
                    var _ = issue.Value.Description;
                }
                catch (NullReferenceException)
                {
                    purgeList.Add(issue.Key);
                }
            }

            foreach (var heroKey in purgeList)
            {
                if (logOnlyMode)
                {
                    Mod.Log(heroKey.Issue, LogLevel.Debug);
                }
                else
                {
                    var issues = issuesRef(Campaign.Current.IssueManager);
                    Mod.Log($"Removing {heroKey} from IssueBase._issues as the Description is throwing NRE", LogLevel.Debug);
                    Debug.PrintError("Bandit Militias is removing {heroKey} from IssueBase._issues as the Description is throwing NRE");
                    issues.Remove(heroKey);
                }
            }
        }

        private static void PurgeList(string logMessage, List<MobileParty> mobileParties)
        {
            if (mobileParties.Count > 0)
            {
                Mod.Log(logMessage, LogLevel.Debug);
                foreach (var mobileParty in mobileParties)
                {
                    mobileParty.RemoveParty();
                    mobileParty.LeaderHero?.KillHero();
                    mobileParty.Party.Visuals.SetMapIconAsDirty();
                }
            }

            mobileParties.Clear();
        }

        // todo move to Militia
        internal static void LogMilitiaFormed(MobileParty mobileParty)
        {
            var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
            var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
            Mod.Log($"{"New Bandit Militia",-40} | {troopString,10} | {strengthString,10} |", LogLevel.Info);
        }

        private static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            return mobileParty.TargetParty != null ||
                   mobileParty.ShortTermTargetParty != null ||
                   mobileParty.ShortTermBehavior == AiBehavior.EngageParty ||
                   mobileParty.DefaultBehavior == AiBehavior.EngageParty ||
                   mobileParty.ShortTermBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.AssaultSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.AssaultSettlement;
        }

        internal static void Trash(MobileParty mobileParty)
        {
            if (mobileParty.LeaderHero != null)
            {
                Militia.FindMilitiaByParty(mobileParty)?.Remove();
                mobileParty.LeaderHero.KillHero();
            }

            mobileParty.RemoveParty();
        }

        // 0-2 milliseconds
        internal static void KillHero(this Hero hero)
        {
            try
            {
                hero.ChangeState(Hero.CharacterStates.Dead);
                hero.HeroDeveloper.ClearUnspentPoints();
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled")
                    .Invoke(CampaignEventDispatcher.Instance, new object[] {hero, hero, KillCharacterAction.KillCharacterActionDetail.None, false});
                Traverse.Create(hero.CurrentSettlement).Field("_heroesWithoutParty").Method("Remove", hero).GetValue();
                Traverse.Create(hero.Clan).Field("_heroes").Method("Remove", hero).GetValue();
                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
                MBObjectManager.Instance.UnregisterObject(hero);
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        internal static bool IsAlone(this MobileParty mobileParty) => MobileParty.FindPartiesAroundPosition(
            mobileParty.Position2D, MergeDistance, x => x.IsBandit).Count(IsValidParty) == 2;

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.", LogLevel.Info);
            InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
            FlushBanditMilitias();
            Flush();
        }

        private static void FlushBanditMilitias()
        {
            Militia.All.Clear();
            var tempList = MobileParty.All
                .Where(x => x.Name.Equals("Bandit Militia")).ToList();
            var hasLogged = false;
            foreach (var mobileParty in tempList)
            {
                if (!hasLogged)
                {
                    Mod.Log($"Clearing {tempList.Count} Bandit Militias", LogLevel.Info);
                    hasLogged = true;
                }

                Trash(mobileParty);
            }
        }

        internal static void Flush()
        {
            FixHomelessHeroes();
            FixBadSettlements();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadIssues();
            FlushBadHeroes();
            FlushBadCharacterObjects();
            FlushBadBehaviors();
            FinalizeBadMapEvents();
        }

        private static void FlushBadBehaviors()
        {
            var behaviors = (IDictionary) Traverse.Create(
                    Campaign.Current.CampaignBehaviorManager.GetBehavior<DynamicBodyCampaignBehavior>())
                .Field("_heroBehaviorsDictionary").GetValue();
            var heroes = new List<Hero>();
            foreach (var hero in behaviors.Keys)
            {
                if (!Hero.All.Contains(hero))
                {
                    heroes.Add((Hero) hero);
                }
            }

            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {heroes.Count} hero behaviors without heroes.", LogLevel.Debug);
                }

                behaviors.Remove(hero);
            }
        }

        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.All.Where(
                x => Clan.BanditFactions.Contains(x.MapFaction) && x.PartyBelongedTo == null).ToList();
            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {heroes.Count} null-party heroes.", LogLevel.Debug);
                }

                hero.KillHero();
            }
        }

        private static void FixHomelessHeroes()
        {
            var homelessHeroes = Hero.All.Where(x => x.HomeSettlement == null);
            foreach (var hero in homelessHeroes)
            {
                Traverse.Create(hero).Property("HomeSettlement").SetValue(hero.BornSettlement);
            }
        }

        private static void FlushBadIssues()
        {
            var badIssues = Campaign.Current.IssueManager.Issues
                .Where(x => Clan.BanditFactions.Contains(x.Key.MapFaction)).ToList();
            var hasLogged = false;
            foreach (var issue in badIssues)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {badIssues.Count} bad-issue heroes.", LogLevel.Info);
                }

                issue.Key.KillHero();
            }
        }

        private static void FixBadSettlements()
        {
            var badSettlements = Settlement.All
                .Where(x => x.IsHideout() && x.OwnerClan == null).ToList();

            var hasLogged = false;
            foreach (var settlement in badSettlements)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {badSettlements.Count} bad settlements.", LogLevel.Info);
                }

                settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
            }
        }

        private static void FlushBadHeroes()
        {
            try
            {
                var badHeroes = Hero.All.Where(
                    x => !x.IsNotable && x.PartyBelongedTo == null && x.CurrentSettlement != null &&
                         x.MapFaction == CampaignData.NeutralFaction && x.EncyclopediaLink.Contains("CharacterObject")).ToList();
                var hasLogged = false;
                foreach (var hero in badHeroes)
                {
                    if (!hasLogged)
                    {
                        hasLogged = true;
                        Mod.Log($"Clearing {badHeroes.Count} bad heroes.", LogLevel.Debug);
                    }

                    if (Hero.All.Contains(hero))
                    {
                        hero.KillHero();
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        // todo FieldRef
        //private static readonly AccessTools.FieldRef<Settlement, List<Hero>> heroesWithoutPartyRef =
        //    AccessTools.FieldRefAccess<Settlement, List<Hero>>("_heroesWithoutParty");

        private static void FlushBadCharacterObjects()
        {
            var badChars = CharacterObject.All.Where(
                    x => x.Name == null ||
                         x.Occupation == Occupation.NotAssigned ||
                         x.Occupation == Occupation.Outlaw &&
                         x.HeroObject?.CurrentSettlement != null)
                .Where(x => !x.StringId.Contains("template") &&
                            !x.StringId.Contains("char_creation") &&
                            !x.StringId.Contains("equipment") &&
                            !x.StringId.Contains("for_perf") &&
                            !x.StringId.Contains("dummy") &&
                            !x.StringId.Contains("npc_") &&
                            !x.StringId.Contains("unarmed_ai")).ToList();

            var hasLogged = false;
            foreach (var badChar in badChars)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {badChars.Count()} bad characters.", LogLevel.Debug);
                }

                Traverse.Create(badChar?.HeroObject?.CurrentSettlement).Field("_heroesWithoutParty").Method("Remove", badChar?.HeroObject).GetValue();
                MBObjectManager.Instance.UnregisterObject(badChar);
            }
        }

        internal static void ReInjectCharacterObjectData()
        {
            var objectTypeRecords = Traverse.Create(MBObjectManager.Instance).Field("ObjectTypeRecords").GetValue<IList>();
            foreach (var record in objectTypeRecords)
            {
                if (record.GetType().FullName.Contains("CharacterObject"))
                {
                    var getObject = record.GetType().GetMethods(AccessTools.all).FirstOrDefault(x => x.Name.Contains("GetObject"));
                    //var createPresumedObject = record.GetType().GetMethods(AccessTools.all).FirstOrDefault(x => x.Name.Contains("CreatePresumedObject"));
                    var registerObject = record.GetType().GetMethods(AccessTools.all).FirstOrDefault(x => x.Name.Contains("RegisterObject"));

                    foreach (var characterObject in replacementData)
                    {
                        try
                        {
                            var existingObject = getObject?.Invoke(record, new object[] {characterObject});
                            if (existingObject == null)
                            {
                                Mod.Log($"Injecting {characterObject}", LogLevel.Debug);
                                registerObject?.Invoke(record, BindingFlags.Default, null, new object[] {characterObject}, CultureInfo.InvariantCulture);
                                // this worked - the above probably works?  neither appears necessary! but boy was it fun
                                //createPresumedObject?.Invoke(record, BindingFlags.Default, null, new object[] {characterObject}, CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                Mod.Log($"Not injecting {((CharacterObject) existingObject).StringId}", LogLevel.Debug);
                            }
                        }
                        catch (Exception ex)
                        {
                            Mod.Log(ex, LogLevel.Debug);
                        }
                    }
                }
            }
        }

        internal static void HourlyFlush()
        {
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadIssues();
            FlushBadHeroes();
            // todo may not be needed anymore
            FinalizeBadMapEvents();
        }

        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All
                .Where(x => x.Name.Equals("Bandit Militia") &&
                            x.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount, LogLevel.Debug);
                tempList.Add(mobileParty);
            }

            PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static void FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All
                .Where(x => x.MemberRoster.TotalManCount == 0 && x.Name.Equals("Bandit Militia")))
            {
                tempList.Add(mobileParty);
            }

            PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} empty parties", tempList);
        }

        internal static Equipment MurderLordsForEquipment(Hero hero, bool randomizeWornEquipment)
        {
            try
            {
                int i = default;
                var equipment = new Equipment[3];
                while (i < 3)
                {
                    var sacrificialLamb = HeroCreator.CreateHeroAtOccupation(Occupation.Lord);
                    if (sacrificialLamb?.BattleEquipment?.Horse != null)
                    {
                        equipment[i++] = sacrificialLamb.BattleEquipment;
                    }

                    sacrificialLamb.KillHero();
                }

                if (!randomizeWornEquipment)
                {
                    return equipment[0];
                }

                var gear = new Equipment();
                for (var j = 0; j < 12; j++)
                {
                    gear[j] = equipment[Rng.Next(0, 3)][j];
                }


                // get rid of any mount
                gear[10] = new EquipmentElement();
                if (hero != null)
                {
                    EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, gear);
                }

                return gear.Clone();
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }

            return null;
        }
    }
}
