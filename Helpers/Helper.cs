using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Patches;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming  

namespace Bandit_Militias.Helpers
{
    public class Helper
    {
        internal static List<ItemObject> Mounts;
        internal static List<ItemObject> Saddles;
        private const float ReductionFactor = 0.8f;
        private static Clan looters;
        private static IEnumerable<Clan> synthClans;
        private static Clan Looters => looters ??= Clan.BanditFactions.First(c => c.StringId == "looters");
        private static IEnumerable<Clan> SynthClans => synthClans ??= Clan.BanditFactions.Except(new[] { Looters });

        internal static bool TrySplitParty(MobileParty mobileParty)
        {
            const float splitDivisor = 2;
            const float removedHero = 1;
            if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                || mobileParty.Party.MemberRoster.TotalManCount / splitDivisor - removedHero < Globals.Settings.MinPartySize
                || !mobileParty.IsBM()
                || mobileParty.IsTooBusyToMerge())
            {
                return false;
            }

            var roll = Rng.Next(0, 101);
            if (roll > Globals.Settings.RandomSplitChance
                || mobileParty.Party.MemberRoster.TotalManCount > Math.Max(1, CalculatedMaxPartySize * ReductionFactor))
            {
                return false;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);

            return true;
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            //Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().Where(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                //Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster())
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Mod.Log("Bad item: " + item.EquipmentElement);
                    continue;
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }
        }

        private static void SplitRosters(TroopRoster roster1, TroopRoster roster2, TroopRosterElement rosterElement)
        {
            // toss a coin (to your Witcher)
            if (rosterElement.Number == 1)
            {
                if (Rng.Next(0, 2) == 0)
                {
                    roster1.AddToCounts(rosterElement.Character, 1);
                }
                else
                {
                    roster2.AddToCounts(rosterElement.Character, 1);
                }
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                roster1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
                roster2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                while (party1.TotalManCount < Globals.Settings.MinPartySize)
                {
                    // using 1, not 0 because 0 is the BM hero
                    party1.AddToCounts(party1.GetCharacterAtIndex(Rng.Next(1, party1.Count + 1)), 1);
                }

                // pretty sure this is never true ...
                while (party2.TotalManCount < Globals.Settings.MinPartySize)
                {
                    party2.AddToCounts(party2.GetCharacterAtIndex(Rng.Next(1, party1.Count + 1)), 1);
                }

                var militia1 = new Militia(original.Position2D, party1, prisoners1);
                var militia2 = new Militia(original.Position2D, party2, prisoners2);
                Mod.Log($">>> {militia1.MobileParty.Name} <- Split {original.Name} Split -> {militia2.MobileParty.Name}");
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
                DoPowerCalculations();
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        private static readonly List<string> verbotenParties = new()
        {
            "ebdi_deserters_party",
            "caravan_ambush_quest",
            "arzagos_banner_piece_quest_raider_party",
            "istiana_banner_piece_quest_raider_party",
            "rescue_family_quest_raider_party",
            "destroy_raiders_conspiracy_quest",
            "radagos_raider_party",
            "locate_and_rescue_traveller_quest_raider_party",
            "company_of_trouble",
            "villagers_of_landlord_needs_access_to_village_common_quest",
            //Calradia Expanded Kingdoms in 3.0.2
            "manhunter"
        };

        // todo more robust solution
        internal static bool IsAvailableBanditParty(MobileParty __instance)
        {
            if (__instance.IsBandit
                || __instance.IsBM()
                && __instance.Party.IsMobile
                && __instance.CurrentSettlement is null
                && __instance.Party.MemberRoster.TotalManCount > 0
                && !__instance.IsTooBusyToMerge()
                && !__instance.IsUsedByAQuest()
                && !verbotenParties.Contains(__instance.StringId))
            {
                return true;
            }

            return false;
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, PartyBase targetParty)
        {
            var troopRoster = TroopRoster.CreateDummyTroopRoster();
            var prisonerRoster = TroopRoster.CreateDummyTroopRoster();
            var rosters = new List<TroopRoster>
            {
                sourceParty.MemberRoster,
                targetParty.MemberRoster
            };

            var prisoners = new List<TroopRoster>
            {
                sourceParty.PrisonRoster,
                targetParty.PrisonRoster
            };

            // dumps all bandit heroes (shouldn't be more than 2 though...)
            foreach (var roster in rosters)
            {
                foreach (var element in roster.GetTroopRoster().Where(e => e.Character?.HeroObject is null))
                {
                    troopRoster.AddToCounts(element.Character, element.Number,
                        woundedCount: element.WoundedNumber, xpChange: element.Xp);
                }
            }

            foreach (var roster in prisoners)
            {
                foreach (var element in roster.GetTroopRoster().Where(e => e.Character?.HeroObject is null))
                {
                    prisonerRoster.AddToCounts(element.Character, element.Number,
                        woundedCount: element.WoundedNumber, xpChange: element.Xp);
                }
            }

            return new[]
            {
                troopRoster,
                prisonerRoster
            };
        }

        internal static void Trash(MobileParty mobileParty)
        {
            PartyMilitiaMap.Remove(mobileParty);
            mobileParty.LeaderHero?.RemoveMilitiaHero();
            if (mobileParty.ActualClan is not null)
            {
                DestroyPartyAction.Apply(null, mobileParty);
            }
        }

        internal static void Nuke()
        {
            FlushBanditMilitias();
            FlushObjectManager();
            FlushMilitiaCharacterObjects();
            FlushPrisoners();
            FlushMapEvents();
            RemoveBMHeroesFromClanLeaderships();
            // TODO remove this temporary fix
            RemoveHeroesWithoutParty();
        }

        private static void FlushObjectManager()
        {
            List<LogEntry> remove = new();
            remove.AddRange(Campaign.Current.LogEntryHistory.GameActionLogs.WhereQ(l => l is TakePrisonerLogEntry entry && entry.Prisoner.StringId.Contains("Bandit_Militia")));
            remove.AddRange(Campaign.Current.LogEntryHistory.GameActionLogs.WhereQ(l => l is EndCaptivityLogEntry entry && entry.Prisoner.StringId.Contains("Bandit_Militia")));
            Traverse.Create(Campaign.Current.LogEntryHistory).Field<List<LogEntry>>("_logs").Value = Campaign.Current.LogEntryHistory.GameActionLogs.Except(remove).ToListQ();
            var characterObjectsRecord = ((IList)Traverse.Create(MBObjectManager.Instance).Field("ObjectTypeRecords").GetValue())[12];
            Traverse.Create(characterObjectsRecord).Method("ReInitialize").GetValue();
        }

        internal static void RemoveHeroesWithoutParty()
        {
            var heroes = Hero.AllAliveHeroes.WhereQ(h =>
                h.PartyBelongedTo is null && h.CharacterObject.StringId.Contains("Bandit_Militia")).ToListQ();
            for (var index = 0; index < heroes.Count; index++)
            {
                var hero = heroes[index];
                Mod.Log($">>> NULL PARTY FOR {hero.Name} - settlement: {hero.CurrentSettlement} - RemoveMilitiaHero");
                hero.RemoveMilitiaHero();
                //Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            }
        }

        private static void FlushBanditMilitias()
        {
            PartyMilitiaMap.Clear();
            var hasLogged = false;
            var partiesToRemove = MobileParty.All.Where(m => m.StringId.Contains("Bandit_Militia"))
                .Concat(MobileParty.All.WhereQ(m => m.PartyComponent is ModBanditMilitiaPartyComponent)).Distinct().ToListQ();
            foreach (var mobileParty in partiesToRemove)
            {
                if (!hasLogged)
                {
                    Mod.Log($">>> FLUSH {partiesToRemove.Count} Bandit Militias parties");
                    hasLogged = true;
                }

                Trash(mobileParty);
            }

            // still needed post 1.7?
            // prisoners somehow of settlements
            foreach (var settlement in Settlement.All)
            {
                var count = settlement.Party.PrisonRoster.Count;
                if (count <= 0)
                {
                    continue;
                }

                for (var i = 0; i < settlement.Party.PrisonRoster.Count; i++)
                {
                    try
                    {
                        var prisoner = settlement.Party.PrisonRoster.GetCharacterAtIndex(i);
                        if (prisoner.StringId.Contains("Bandit_Militia"))
                        {
                            Mod.Log($">>> FLUSH BM hero prisoner {prisoner.HeroObject.Name} at {settlement.Name}.");
                            settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                            prisoner.HeroObject.RemoveMilitiaHero();
                        }
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }
            }

            var leftovers = Hero.AllAliveHeroes.WhereQ(h => h.StringId.Contains("Bandit_Militia")).ToListQ();
            for (var index = 0; index < leftovers.Count; index++)
            {
                var hero = leftovers[index];
                Mod.Log("Removing leftover hero " + hero);
                hero.RemoveMilitiaHero();
            }
        }

        // TODO verify if needed post-1.7
        internal static void FlushMilitiaCharacterObjects()
        {
            // still rarely happening with 1.7 and 3.3.7
            // leak from CampaignTickPatch, trashing parties there doesn't get rid of all remnants...
            // many hours trying to find proper solution
            // 2-4 ms
            var COs = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>();
            var BMs = COs.WhereQ(c => c.HeroObject?.PartyBelongedTo is null
                                      && c.StringId.EndsWith("Bandit_Militia")
                                      && c.HeroObject is not null
                                      && !c.HeroObject.IsFactionLeader).ToList();
            if (BMs.Any())
            {
                Mod.Log($">>> FLUSH {BMs.Count} BM CharacterObjects");
                Mod.Log(new StackTrace());
                BMs.Do(c => MBObjectManager.Instance.UnregisterObject(c));
                var charactersField = Traverse.Create(Campaign.Current).Field<MBReadOnlyList<CharacterObject>>("_characters");
                var tempCharacterObjectList = new List<CharacterObject>(charactersField.Value);
                tempCharacterObjectList = tempCharacterObjectList.Except(BMs).ToListQ();
                charactersField.Value = new MBReadOnlyList<CharacterObject>(tempCharacterObjectList);
            }

            //Mod.Log("");
            //Mod.Log($"{new string('=', 80)}\nBMs: {PartyMilitiaMap.Count,-4} Power: {GlobalMilitiaPower} / Power Limit: {CalculatedGlobalPowerLimit} = {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100:f2}% (limit {Globals.Settings.GlobalPowerPercent}%)");
            //Mod.Log("");
        }

        internal static void FlushPrisoners()
        {
            // stupid overkill, 0-sequences
            var prisoners = Hero.AllAliveHeroes.WhereQ(h =>
                h.CharacterObject.StringId.Contains("Bandit_Militia") && h.IsPrisoner).ToListQ();
            prisoners = prisoners.Concat(Hero.DeadOrDisabledHeroes.WhereQ(h => h.CharacterObject.StringId.Contains("Bandit_Militia") && h.IsPrisoner)).ToListQ();
            prisoners = prisoners.Concat(MobileParty.MainParty.PrisonRoster.GetTroopRoster().WhereQ(e => e.Character.StringId.Contains("Bandit_Militia")).Select(e => e.Character.HeroObject)).ToListQ();
            for (var index = 0; index < prisoners.Count; index++)
            {
                var prisoner = prisoners[index];
                Mod.Log($"{new string('=', 80)}");
                Mod.Log($">>> PRISONER {prisoner.Name,-20}: {prisoner.IsPrisoner} ({prisoner.PartyBelongedToAsPrisoner is not null})");
                prisoner.RemoveMilitiaHero();
                Mod.Log($"{new string('=', 80)}");
            }
        }

        internal static void ReHome()
        {
            var tempList = PartyMilitiaMap.Values.Where(x => x?.Hero?.HomeSettlement is null).Select(x => x.Hero).ToList();
            Mod.Log($"Fixing {tempList.Count} null HomeSettlement heroes");
            tempList.Do(x => Traverse.Create(x).Field("_homeSettlement").SetValue(Hideouts.GetRandomElement()));
        }


        internal static void RemoveBMHeroesFromClanLeaderships()
        {
            foreach (var clan in Clan.BanditFactions)
            {
                if (clan.Leader?.StringId is not null &&
                    clan.Leader.StringId.EndsWith("Bandit_Militia"))
                {
                    clan.SetLeader(null);
                }
            }
        }

        private static void FlushMapEvents()
        {
            var mapEvents = Traverse.Create(Campaign.Current.MapEventManager).Field("_mapEvents").GetValue<List<MapEvent>>();
            for (var index = 0; index < mapEvents.Count; index++)
            {
                var mapEvent = mapEvents[index];
                if (mapEvent.InvolvedParties.Any(p =>
                        p.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent
                        || p.LeaderHero?.CharacterObject is not null
                        && p.LeaderHero.CharacterObject.StringId.Contains("Bandit_Militia")))
                {
                    Mod.Log(">>> FLUSH MapEvent.");
                    mapEvent.FinalizeEvent();
                }
            }
        }

        // Bob's Bandit Militia vs Ross' Bandit Militia
        internal static string Possess(string input)
        {
            // game tries to redraw the PartyNamePlateVM after combat with multiple militias
            // and crashes because mobileParty.Party.LeaderHero?.FirstName.ToString() is null
            if (input is null)
            {
                return null;
            }

            var lastChar = input[input.Length - 1];
            return $"{input}{(lastChar == 's' ? "'" : "'s")}";
        }

        internal static void PopulateItems()
        {
            var verbotenItems = new List<string>
            {
                "Sparring",
                "Trash Item",
                "Torch",
                "Horse Whip",
                "Push Fork",
                "Bound Crossbow",
                "Hoe",
                "Scythe",
                "Stone",
                "Crafted",
                "Wooden",
                "Practice",
                "Ballista",
                "Boulder",
                "Fire Pot",
                "Banner",
                "Grapeshot"
            };

            var verbotenSaddles = new List<string>
            {
                "celtic_frost",
                "saddle_of_aeneas",
                "fortunas_choice",
                "aseran_village_harness"
            };

            Mounts = Items.All.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Horse).Where(i => !i.StringId.Contains("unmountable")).ToList();
            Saddles = Items.All.Where(i => i.ItemType == ItemObject.ItemTypeEnum.HorseHarness
                                           && !i.StringId.Contains("mule")
                                           && !verbotenSaddles.Contains(i.StringId)).ToList();
            var moddedWithCivilized = AppDomain.CurrentDomain.GetAssemblies().AnyQ(a => a.FullName.Contains("Civilized"));
            var all = Items.All.Where(i =>
                !i.IsCraftedByPlayer
                && i.ItemType is not ItemObject.ItemTypeEnum.Goods
                && i.ItemType is not ItemObject.ItemTypeEnum.Horse
                && i.ItemType is not ItemObject.ItemTypeEnum.HorseHarness
                && i.ItemType is not ItemObject.ItemTypeEnum.Animal
                && i.ItemType is not ItemObject.ItemTypeEnum.Banner
                && i.ItemType is not ItemObject.ItemTypeEnum.Book
                && i.ItemType is not ItemObject.ItemTypeEnum.Invalid
                && i.ItemCategory.StringId != "garment").ToList();
            if (!moddedWithCivilized)
            {
                all = Items.All.Where(i => !i.IsCivilian).ToList();
            }

            for (var index = 0; index < all.Count; index++)
            {
                var item = all[index];
                foreach (var word in verbotenItems)
                {
                    if (item.Name.Contains(word))
                    {
                        //Mod.Log("Removing " + item.Name);
                        all.Remove(item);
                        index--;
                        break;
                    }
                }
            }

            Arrows = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows).ToList();
            Bolts = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts).ToList();
            var oneHanded = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon);
            var twoHanded = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon);
            var polearm = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Polearm);
            var thrown = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Thrown);
            var shields = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Shield);
            var bows = all.Where(i => i.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow);
            var any = new List<ItemObject>(oneHanded.Concat(twoHanded).Concat(polearm).Concat(thrown).Concat(shields).Concat(bows).ToList());
            any.Do(i => EquipmentItems.Add(new EquipmentElement(i)));
        }

        private static readonly AccessTools.StructFieldRef<EquipmentElement, ItemModifier> ItemModifier =
            AccessTools.StructFieldRefAccess<EquipmentElement, ItemModifier>("<ItemModifier>k__BackingField");

        // builds a set of 4 weapons that won't include more than 1 bow or shield, nor any lack of ammo
        internal static Equipment BuildViableEquipmentSet()
        {
            //T.Restart();
            var gear = new Equipment();
            var haveShield = false;
            var haveBow = false;
            try
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    EquipmentElement randomElement = default;
                    switch (slot)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            randomElement = EquipmentItems.Where(x =>
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    if (randomElement.Item.HasArmorComponent)
                    {
                        ItemModifier(ref randomElement) = randomElement.Item.ArmorComponent.ItemModifierGroup?.ItemModifiers.GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);
                    }

                    if (randomElement.Item.HasWeaponComponent)
                    {
                        ItemModifier(ref randomElement) = randomElement.Item.WeaponComponent.ItemModifierGroup?.ItemModifiers.GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);
                    }

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (slot == 3 && !gear[3].IsEmpty)
                    {
                        break;
                    }

                    if (randomElement.Item.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow)
                    {
                        if (slot < 3)
                        {
                            // try again, try harder
                            if (haveBow)
                            {
                                slot--;
                                continue;
                            }

                            haveBow = true;
                            gear[slot] = randomElement;
                            if (randomElement.Item.ItemType is ItemObject.ItemTypeEnum.Bow)
                            {
                                gear[3] = new EquipmentElement(Arrows.ToList()[Rng.Next(0, Arrows.Count)]);
                            }
                            else if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                            {
                                gear[3] = new EquipmentElement(Bolts.ToList()[Rng.Next(0, Bolts.Count)]);
                            }

                            continue;
                        }

                        randomElement = EquipmentItems.Where(x =>
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        // try again, try harder
                        if (haveShield)
                        {
                            slot--;
                            continue;
                        }

                        haveShield = true;
                    }

                    gear[slot] = randomElement;
                }

                gear[5] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].GetRandomElement());
                gear[6] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].GetRandomElement());
                gear[7] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.LegArmor].GetRandomElement());
                gear[8] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HandArmor].GetRandomElement());
                gear[9] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.Cape].GetRandomElement());
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }

            //Mod.Log($"GEAR ==> {T.ElapsedTicks / 10000F:F3}ms");
            return gear.Clone();
        }

        // game world measurement
        internal static void DoPowerCalculations(bool force = false)
        {
            if (force || LastCalculated < CampaignTime.Now.ToHours - 8)
            {
                var parties = MobileParty.All.Where(p => p.LeaderHero is not null && !p.IsBM()).ToListQ();
                var medianSize = (float)parties.OrderBy(p => p.MemberRoster.TotalManCount)
                    .ElementAt(parties.CountQ() / 2).MemberRoster.TotalManCount;
                CalculatedMaxPartySize = Math.Min(medianSize * Variance, Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * 1.5f);
                if (CalculatedMaxPartySize <= MobileParty.MainParty.MemberRoster.TotalManCount)
                {
                    CalculatedMaxPartySize *= 1 + CalculatedMaxPartySize / MobileParty.MainParty.MemberRoster.TotalManCount;
                }

                CalculatedMaxPartySize = Math.Max(CalculatedMaxPartySize, Globals.Settings.MinPartySize);
                LastCalculated = CampaignTime.Now.ToHours;
                CalculatedGlobalPowerLimit = parties.Sum(p => p.Party.TotalStrength) * Variance;
                GlobalMilitiaPower = PartyMilitiaMap.Keys.Sum(p => p.Party.TotalStrength);
                MilitiaPowerPercent = GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100;
            }
        }

        // leveraged to make looters convert into troop types from nearby cultures
        public static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 20;
            var settlements = Settlement.FindSettlementsAroundPosition(position, arbitraryDistance);
            var map = new Dictionary<CultureObject, int>();
            foreach (var settlement in settlements)
            {
                if (map.ContainsKey(settlement.Culture))
                {
                    map[settlement.Culture]++;
                }
                else
                {
                    map.Add(settlement.Culture, 1);
                }
            }

            if (BlackFlag is not null)
            {
                map.Remove(BlackFlag);
            }

            var highest = map.Where(x =>
                x.Value == map.Values.Max()).Select(x => x.Key);
            var result = highest.ToList().GetRandomElement();
            return result;
        }

        public static void ConvertLootersToKingdomCultureRecruits(ref TroopRoster troopRoster, CultureObject culture, int numberToUpgrade)
        {
            var recruit = Recruits.Where(x =>
                    x.Culture == Clan.All.FirstOrDefault(k => k.Culture == culture)?.Culture)
                .ToList().GetRandomElement() ?? Recruits.ToList().GetRandomElement();

            troopRoster.AddToCounts(recruit, numberToUpgrade);
        }

        // 1.5.9 this condition leads to a null ref in DoWait() so we hack it
        internal static void FixMapEventFuckery()
        {
            if (PlayerEncounter.Battle is not null &&
                PartyBase.MainParty.MapEvent is null)
            {
                var sides = Traverse.Create(PlayerEncounter.Battle).Field<MapEventSide[]>("_sides").Value;
                var playerSide = sides.FirstOrDefault(x => x.LeaderParty.LeaderHero == Hero.MainHero);
                var attacker = sides.FirstOrDefault(x => x.MissionSide == BattleSideEnum.Attacker)?.LeaderParty;
                var defender = sides.FirstOrDefault(x => x.MissionSide == BattleSideEnum.Defender)?.LeaderParty;
                PartyBase.MainParty.MapEventSide = playerSide;
                var initialize = AccessTools.Method(typeof(MapEvent), "Initialize", new[] { typeof(PartyBase), typeof(PartyBase), typeof(MapEvent.BattleTypes) });
                initialize.Invoke(PartyBase.MainParty.MapEvent, new object[] { attacker, defender, MapEvent.BattleTypes.None });
            }
        }

        internal static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            Mod.Log($"Inserting {insertSize} at {insertPoint}.");

            // in case insertPoint is near the start of the method's IL
            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // codes[266 - 5 + 0].opcode
                // codes[266 - 5 + 4].opcode
                Mod.Log($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                Mod.Log($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            // in case insertPoint is near the end of the method's IL
            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // 266 + 2 - 5 + 0
                // 266 + 2 - 5 + 4
                Mod.Log($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }

        private static readonly AccessTools.FieldRef<MobileParty, bool> aiBehaviorResetNeeded =
            AccessTools.FieldRefAccess<MobileParty, bool>("_aiBehaviorResetNeeded");

        internal static void SetMilitiaPatrol(MobileParty mobileParty)
        {
            var settlement = Settlement.All.GetRandomElement();
            mobileParty.SetMovePatrolAroundPoint(settlement.GatePosition);
            aiBehaviorResetNeeded(mobileParty) = false;
        }

        internal static void RunLateManualPatches()
        {
            // this patch prevents nasty problems with saving at an encounter dialog (might be getting old as of 3.3.1)
            // have to patch late because of static constructors (type initialization exception)
            Mod.harmony.Patch(
                AccessTools.Method(typeof(EncounterGameMenuBehavior), "game_menu_encounter_on_init"),
                new HarmonyMethod(AccessTools.Method(typeof(Helper), nameof(FixMapEventFuckery))));
            var original = AccessTools.Method(typeof(DefaultPartySpeedCalculatingModel), "CalculatePureSpeed");
            var postfix = AccessTools.Method(
                typeof(MilitiaPatches.DefaultPartySpeedCalculatingModelCalculatePureSpeedPatch),
                nameof(MilitiaPatches.DefaultPartySpeedCalculatingModelCalculatePureSpeedPatch.Postfix));
            Mod.harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        internal static void RemoveUndersizedTracker(PartyBase party)
        {
            if (party.MemberRoster.TotalManCount < Globals.Settings.TrackedSizeMinimum)
            {
                var tracker = MobilePartyTrackerVM?.Trackers?.FirstOrDefault(t => t.TrackedParty == party.MobileParty);
                if (tracker is not null)
                {
                    MobilePartyTrackerVM.Trackers.Remove(tracker);
                }
            }
        }

        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().Where(x => x.Character.Equipment[10].Item is not null).Sum(e => e.Number);
        }

        private static List<CultureObject> AllowedCultures;
        private static List<Settlement> AllowedSettlements;

        internal static Hero CreateHero()
        {
            AllowedCultures ??= new()
            {
                MBObjectManager.Instance.GetObject<CultureObject>("looters"),
                MBObjectManager.Instance.GetObject<CultureObject>("mountain_bandits"),
                MBObjectManager.Instance.GetObject<CultureObject>("forest_bandits"),
                MBObjectManager.Instance.GetObject<CultureObject>("desert_bandits"),
                MBObjectManager.Instance.GetObject<CultureObject>("steppe_bandits"),
                MBObjectManager.Instance.GetObject<CultureObject>("sea_bandits")
            };

            AllowedSettlements ??= Hideouts.WhereQ(s => AllowedCultures.Contains(s.Culture)).ToListQ();
            var hero = HeroCreator.CreateHeroAtOccupation(Occupation.Bandit, AllowedSettlements.GetRandomElement());
            hero.StringId += "Bandit_Militia";
            hero.CharacterObject.StringId += "Bandit_Militia";
            if (Rng.Next(0, 2) == 0)
            {
                hero.UpdatePlayerGender(true);
                hero.FirstName.SetTextVariable("FEMALE", 1);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, hero.FirstName);
            }

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, BanditEquipment.GetRandomElement());
            if (Globals.Settings.CanTrain)
            {
                Traverse.Create(hero).Method("SetSkillValueInternal", DefaultSkills.Leadership, 150).GetValue();
                Traverse.Create(hero).Method("SetPerkValueInternal", DefaultPerks.Leadership.VeteransRespect, true).GetValue();
            }

            return hero;
        }

        internal static void SynthesizeBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
            {
                return;
            }

            for (var i = 0;
                 MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                 && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                 i++)
            {
                if (Rng.Next(0, 101) > Globals.Settings.SpawnChance)
                {
                    continue;
                }

                var settlement = Settlement.All.Where(s => !s.IsVisible).GetRandomElementInefficiently();
                var nearbyBandits = MobileParty.FindPartiesAroundPosition(settlement.Position2D, 100).WhereQ(m => m.IsBandit);
                var cultureMap = new Dictionary<Clan, int>();
                {
                    foreach (var party in nearbyBandits)
                    {
                        if (cultureMap.TryGetValue(party.ActualClan, out _))
                        {
                            cultureMap[party.ActualClan]++;
                        }

                        cultureMap[party.ActualClan] = 1;
                    }
                }

                var clan = SynthClans.FirstOrDefaultQ(c => c == cultureMap.OrderByDescending(x => x.Value).First().Key) ?? Looters;
                var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                var max = Convert.ToInt32(CalculatedMaxPartySize);
                var roster = TroopRoster.CreateDummyTroopRoster();
                var size = Convert.ToInt32(Rng.Next(min, max + 1) / 2f);
                roster.AddToCounts(clan.BasicTroop, size);
                roster.AddToCounts(Looters.BasicTroop, size);
                MurderMounts(roster);


                var militia = new Militia(settlement.GatePosition, roster, TroopRoster.CreateDummyTroopRoster());
                // teleport new militias near the player
                if (Globals.Settings.TestingMode)
                {
                    // in case a prisoner
                    var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                    militia.MobileParty.Position2D = party.Position2D;
                }

                DoPowerCalculations();
            }
        }

        internal static void TryGrowing(MobileParty mobileParty)
        {
            if (Globals.Settings.GrowthPercent > 0
                && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                && mobileParty.IsBM()
                && mobileParty.MapEvent is null
                && IsAvailableBanditParty(mobileParty)
                && Rng.NextDouble() <= Globals.Settings.GrowthChance / 100f)
            {
                var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible)
                    .ToListQ();
                if (eligibleToGrow.Any())
                {
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                    // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                    // thanks Erythion!
                    var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                    growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                    growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                    Mod.Log($"Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                    {
                        var troop = eligibleToGrow.GetRandomElement().Character;
                        if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        {
                            mobileParty.MemberRoster.AddToCounts(troop, 1);
                        }
                    }

                    MurderMounts(mobileParty.MemberRoster);
                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Mod.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Mod.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        private static void MurderMounts(TroopRoster troopRoster)
        {
            var numMounted = NumMountedTroops(troopRoster);
            var mountedTroops = troopRoster.ToFlattenedRoster().Troops.WhereQ(t => t.IsMounted && !t.IsHero).ToListQ();
            mountedTroops.Shuffle();
            // remove horses past 50% of the BM
            if (numMounted > troopRoster.TotalManCount / 2)
            {
                foreach (var troop in mountedTroops)
                {
                    if (NumMountedTroops(troopRoster) > troopRoster.TotalManCount / 2)
                    {
                        troop.Equipment[10] = new EquipmentElement();
                        troop.Equipment[11] = new EquipmentElement();
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }
}
