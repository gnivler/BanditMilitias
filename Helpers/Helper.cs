using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BanditMilitias.Patches;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Globals;

// ReSharper disable MemberCanBePrivate.Global 
// ReSharper disable InconsistentNaming  

namespace BanditMilitias.Helpers
{
    public static class Helper
    {
        public static List<ItemObject> Mounts;
        public static List<ItemObject> Saddles;

        private const float ReductionFactor = 0.8f;

        //private static List<CultureObject> AllowedCultures;
        private static IEnumerable<ModBanditMilitiaPartyComponent> AllBMs;

        public static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
            AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

        private static readonly AccessTools.FieldRef<NameGenerator, TextObject[]> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, TextObject[]>("_gangLeaderNames");

        public static readonly AccessTools.FieldRef<Hero, Settlement> _homeSettlement = AccessTools.FieldRefAccess<Hero, Settlement>("_homeSettlement");

        private static readonly AccessTools.StructFieldRef<EquipmentElement, ItemModifier> ItemModifier =
            AccessTools.StructFieldRefAccess<EquipmentElement, ItemModifier>("<ItemModifier>k__BackingField");

        public static readonly AccessTools.FieldRef<PartyBase, ItemRoster> ItemRoster =
            AccessTools.FieldRefAccess<PartyBase, ItemRoster>("<ItemRoster>k__BackingField");

        public static bool Log(object input)
        {
            if (Globals.Settings is null
                || Globals.Settings?.Debug is false)
            {
                return false;
            }

            using var sw = new StreamWriter(SubModule.logFilename, true);
            sw.WriteLine($"[{DateTime.Now.ToLongTimeString()}] {(string.IsNullOrEmpty(input?.ToString()) ? "IsNullOrEmpty" : input)}");
            return true;
        }

        public static bool TrySplitParty(MobileParty mobileParty)
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
            //Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().Where(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                //Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster())
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Log("Bad item: " + item.EquipmentElement);
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
                    party1.AddToCounts(party1.GetCharacterAtIndex(Rng.Next(1, party1.Count)), 1);
                }

                while (party2.TotalManCount < Globals.Settings.MinPartySize)
                {
                    party2.AddToCounts(party2.GetCharacterAtIndex(Rng.Next(1, party2.Count)), 1);
                }

                if (original.ActualClan is null) Debugger.Break();

                var bm1 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(original.ActualClan), m => m.ActualClan = original.ActualClan);
                var bm2 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(original.ActualClan), m => m.ActualClan = original.ActualClan);
                var rosters1 = new[]
                {
                    party1,
                    prisoners1
                };
                var rosters2 = new[]
                {
                    party2,
                    prisoners2
                };
                InitMilitia(bm1, rosters1, original.Position2D);
                InitMilitia(bm2, rosters2, original.Position2D);
                bm1.BM().Avoidance = original.BM().Avoidance;
                bm2.BM().Avoidance = original.BM().Avoidance;
                Log($">>> {bm1.Name} <- Split {original.Name} Split -> {bm2.Name}");
                ItemRoster(bm1.Party) = inventory1;
                ItemRoster(bm2.Party) = inventory2;
                bm1.Party.Visuals.SetMapIconAsDirty();
                bm2.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
                DoPowerCalculations();
            }
            catch (Exception ex)
            {
                Log(ex);
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

        public static bool IsAvailableBanditParty(MobileParty __instance)
        {
            return __instance.IsBandit
                   && __instance.CurrentSettlement is null
                   && __instance.MapEvent is null
                   && __instance.Party.MemberRoster.TotalManCount > 0
                   && !__instance.IsTooBusyToMerge()
                   // unfortunately this also means "tracked" BMs too but their distance, so doesn't matter?
                   && !__instance.IsUsedByAQuest()
                   && !verbotenParties.Contains(__instance.StringId);
        }

        public static TroopRoster[] MergeRosters(MobileParty sourceParty, PartyBase targetParty)
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

        public static void Trash(MobileParty mobileParty)
        {
            mobileParty.LeaderHero?.RemoveMilitiaHero();

            if (mobileParty.ActualClan is not null)
            {
                DestroyPartyAction.Apply(null, mobileParty);
            }
        }

        public static void Nuke()
        {
            FlushBanditMilitias();
            MilitiaBehavior.FlushMilitiaCharacterObjects();
            FlushPrisoners();
            FlushMapEvents();
            RemoveBMHeroesFromClanLeaderships();
            // written to deal with escapee-BM heroes clogging up the logs
            FlushObjectManager();
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

        public static void RemoveHeroesWithoutParty()
        {
            var heroes = Hero.AllAliveHeroes.WhereQ(h =>
                h.PartyBelongedTo is null && h.CharacterObject.StringId.Contains("Bandit_Militia")).ToListQ();
            for (var index = 0; index < heroes.Count; index++)
            {
                // firing 3.7.0...
                //Debugger.Break();
                var hero = heroes[index];
                Log($">>> NULL PARTY FOR {hero.Name} - settlement: {hero.CurrentSettlement} - RemoveMilitiaHero ({hero.IsPrisoner})");
                hero.RemoveMilitiaHero();
                //Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            }
        }

        private static void FlushBanditMilitias()
        {
            var hasLogged = false;
            var partiesToRemove = MobileParty.All.WhereQ(m => m.PartyComponent is ModBanditMilitiaPartyComponent).ToListQ();
            foreach (var mobileParty in partiesToRemove)
            {
                if (!hasLogged)
                {
                    Log($">>> FLUSH {partiesToRemove.Count} Bandit Militias");
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
                            //Debugger.Break();
                            Log($">>> FLUSH BM hero prisoner {prisoner.HeroObject?.Name} at {settlement.Name}.");
                            settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                            prisoner.HeroObject.RemoveMilitiaHero();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                    }
                }
            }

            var leftovers = Hero.AllAliveHeroes.WhereQ(h => h.StringId.Contains("Bandit_Militia")).ToListQ();
            //if (leftovers.Any()) Debugger.Break();
            for (var index = 0; index < leftovers.Count; index++)
            {
                var hero = leftovers[index];
                Log("Removing leftover hero " + hero);
                hero.RemoveMilitiaHero();
            }

            PartyImageMap.Clear();
        }


        public static void FlushPrisoners()
        {
            // stupid overkill, 0-sequences
            var prisoners = Hero.AllAliveHeroes.WhereQ(h =>
                h.CharacterObject.StringId.Contains("Bandit_Militia") && h.IsPrisoner).ToListQ();
            prisoners = prisoners.Concat(Hero.DeadOrDisabledHeroes.WhereQ(h => h.CharacterObject.StringId.Contains("Bandit_Militia") && h.IsPrisoner)).ToListQ();
            prisoners = prisoners.Concat(MobileParty.MainParty.PrisonRoster.GetTroopRoster().WhereQ(e => e.Character.StringId.Contains("Bandit_Militia")).Select(e => e.Character.HeroObject)).ToListQ();
            for (var index = 0; index < prisoners.Count; index++)
            {
                Debugger.Break();
                var prisoner = prisoners[index];
                //Log($"{new string('=', 80)}");
                //Log($">>> PRISONER {prisoner.Name,-20}: {prisoner.IsPrisoner} ({prisoner.PartyBelongedToAsPrisoner is not null})");
                prisoner.RemoveMilitiaHero();
                //Log($"{new string('=', 80)}");
            }
        }

        public static void RemoveBMHeroesFromClanLeaderships()
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
                    Log(">>> FLUSH MapEvent.");
                    mapEvent.FinalizeEvent();
                }
            }
        }

        // Bob's Bandit Militia vs Ross' Bandit Militia
        public static string Possess(string input)
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

        public static void PopulateItems()
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

            all.RemoveAll(i => verbotenItems.Contains(i.Name.ToString()));
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

        // builds a set of 4 weapons that won't include more than 1 bow or shield, nor any lack of ammo
        public static Equipment BuildViableEquipmentSet()
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
                Log(ex);
            }

            //Log($"GEAR ==> {T.ElapsedTicks / 10000F:F3}ms");
            return gear.Clone();
        }

        // game world measurement
        public static void DoPowerCalculations(bool force = false)
        {
            if (force || LastCalculated < CampaignTime.Now.ToHours - 8)
            {
                var parties = MobileParty.All.Where(p => p.LeaderHero is not null && !p.IsBM()).ToListQ();
                var medianSize = (float)parties.OrderBy(p => p.MemberRoster.TotalManCount)
                    .ElementAt(parties.CountQ() / 2).MemberRoster.TotalManCount;
                Globals.CalculatedMaxPartySize = Math.Max(medianSize * Variance, Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * Variance);
                //Globals.CalculatedMaxPartySize = Math.Max(Globals.CalculatedMaxPartySize, Globals.Settings.MinPartySize);
                Globals.LastCalculated = CampaignTime.Now.ToHours;
                Globals.CalculatedGlobalPowerLimit = parties.Sum(p => p.Party.TotalStrength) * Variance;
                Globals.GlobalMilitiaPower = GetCachedBMs(true).SumQ(m => m.Party.TotalStrength);
                Globals.MilitiaPowerPercent = Globals.GlobalMilitiaPower / Globals.CalculatedGlobalPowerLimit * 100;
                Globals.MilitiaPartyAveragePower = Globals.GlobalMilitiaPower / GetCachedBMs().CountQ();
            }
        }

        // leveraged to make looters convert into troop types from nearby cultures
        public static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 100;
            var settlements = Settlement.FindSettlementsAroundPosition(position, arbitraryDistance, s => !s.IsHideout);
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
            return result ?? MBObjectManager.Instance.GetObject<CultureObject>("empire");
        }

        public static void ConvertLootersToKingdomCultureRecruits(TroopRoster troopRoster, CultureObject culture, int numberToUpgrade)
        {
            var recruit = Recruits[culture][Rng.Next(0, Recruits[culture].Count)];
            troopRoster.AddToCounts(recruit, numberToUpgrade);
        }

        public static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            Log($"Inserting {insertSize} at {insertPoint}.");

            // in case insertPoint is near the start of the method's IL
            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // codes[266 - 5 + 0].opcode
                // codes[266 - 5 + 4].opcode
                Log($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                Log($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            // in case insertPoint is near the end of the method's IL
            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // 266 + 2 - 5 + 0
                // 266 + 2 - 5 + 4
                Log($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }

        public static void RunLateManualPatches()
        {
            var original = AccessTools.Method(typeof(DefaultPartySpeedCalculatingModel), "CalculatePureSpeed");
            var postfix = AccessTools.Method(
                typeof(MilitiaPatches.CalculatePureSpeed),
                nameof(MilitiaPatches.CalculatePureSpeed.Postfix));
            SubModule.harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        public static void RemoveUndersizedTracker(PartyBase party)
        {
            if (party.MemberRoster.TotalManCount < Globals.Settings.TrackedSizeMinimum)
            {
                var tracker = Globals.MobilePartyTrackerVM?.Trackers?.FirstOrDefault(t => t.TrackedParty == party.MobileParty);
                if (tracker is not null)
                {
                    Globals.MobilePartyTrackerVM.Trackers.Remove(tracker);
                }
            }
        }

        public static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().Where(x => x.Character.Equipment[10].Item is not null).Sum(e => e.Number);
        }

        public static Hero CreateHero(Clan clan)
        {
            //AllowedCultures ??= new()
            //{
            //    MBObjectManager.Instance.GetObject<CultureObject>("looters"),
            //    MBObjectManager.Instance.GetObject<CultureObject>("mountain_bandits"),
            //    MBObjectManager.Instance.GetObject<CultureObject>("forest_bandits"),
            //    MBObjectManager.Instance.GetObject<CultureObject>("desert_bandits"),
            //    MBObjectManager.Instance.GetObject<CultureObject>("steppe_bandits"),
            //    MBObjectManager.Instance.GetObject<CultureObject>("sea_bandits")
            //};

            var hero = HeroCreator.CreateHeroAtOccupation(Occupation.Bandit, Hideouts.GetRandomElementInefficiently());
            hero.Clan = clan;
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
                hero.HeroDeveloper.AddPerk(DefaultPerks.Leadership.VeteransRespect);
                hero.HeroDeveloper.AddSkillXp(DefaultSkills.Leadership, 150);
            }

            return hero;
        }


        internal static void MurderMounts(TroopRoster troopRoster)
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

        public static void ConfigureMilitia(MobileParty mobileParty)
        {
            mobileParty.LeaderHero.Gold = Convert.ToInt32(mobileParty.Party.TotalStrength * GoldMap[Globals.Settings.GoldReward.SelectedValue]);
            mobileParty.MemberRoster.AddToCounts(mobileParty.BM().Leader.CharacterObject, 1, false, 0, 0, true, 0);
            IsBandit(mobileParty) = true;
            if (PartyImageMap.ContainsKey(mobileParty))
            {
                PartyImageMap[mobileParty] = new ImageIdentifierVM(mobileParty.BM().Banner);
            }
            else
            {
                PartyImageMap.Add(mobileParty, new ImageIdentifierVM(mobileParty.BM().Banner));
            }

            if (mobileParty.ActualClan.Leader is null)
            {
                mobileParty.ActualClan.SetLeader(mobileParty.BM().Leader);
            }

            if (Rng.Next(0, 2) == 0)
            {
                var mount = Mounts.GetRandomElement();
                mobileParty.BM().Leader.BattleEquipment[10] = new EquipmentElement(mount);
                if (mount.HorseComponent.Monster.MonsterUsage == "camel")
                {
                    mobileParty.BM().Leader.BattleEquipment[11] = new EquipmentElement(Saddles.Where(saddle =>
                        saddle.Name.ToString().ToLower().Contains("camel")).ToList().GetRandomElement());
                }
                else
                {
                    mobileParty.BM().Leader.BattleEquipment[11] = new EquipmentElement(Saddles.Where(saddle =>
                        !saddle.Name.ToString().ToLower().Contains("camel")).ToList().GetRandomElement());
                }
            }

            mobileParty.SetCustomName(mobileParty.BM().Name);
            mobileParty.LeaderHero.StringId += "Bandit_Militia";
            var tracker = Globals.MobilePartyTrackerVM.Trackers.FirstOrDefault(t => t.TrackedParty == mobileParty);
            if (Globals.Settings.Trackers
                && tracker is null
                && mobileParty.MemberRoster.TotalManCount >= Globals.Settings.TrackedSizeMinimum)
            {
                tracker = new MobilePartyTrackItemVM(mobileParty, MapScreen.Instance.MapCamera, null);
                Globals.MobilePartyTrackerVM.Trackers.Add(tracker);
            }
        }

        public static void TrainMilitia(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty.MemberRoster.Count == 0)
                {
                    Log("Trying to configure militia with no troops, trashing");
                    Trash(mobileParty);
                    return;
                }

                if (!Globals.Settings.CanTrain ||
                    MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent)
                {
                    return;
                }

                int iterations = default;
                switch (Globals.Settings.XpGift.SelectedValue)
                {
                    case "Off":
                        break;
                    case "Normal":
                        iterations = 1;
                        break;
                    case "Hard":
                        iterations = 2;
                        break;
                    case "Hardest":
                        iterations = 4;
                        break;
                }

                int number, numberToUpgrade;
                if (Globals.Settings.LooterUpgradePercent > 0)
                {
                    // upgrade any looters first, then go back over and iterate further upgrades
                    var allLooters = mobileParty.MemberRoster.GetTroopRoster().Where(e => e.Character == MilitiaBehavior.Looters.BasicTroop).ToList();
                    if (allLooters.Any())
                    {
                        var culture = GetMostPrevalentFromNearbySettlements(mobileParty.Position2D);
                        foreach (var looter in allLooters)
                        {
                            number = mobileParty.MemberRoster.GetElementCopyAtIndex(mobileParty.MemberRoster.FindIndexOfTroop(looter.Character)).Number;
                            numberToUpgrade = Convert.ToInt32(number * Globals.Settings.LooterUpgradePercent / 100);
                            if (numberToUpgrade == 0)
                            {
                                continue;
                            }

                            var roster = mobileParty.MemberRoster;
                            roster.RemoveTroop(looter.Character, numberToUpgrade);
                            ConvertLootersToKingdomCultureRecruits(roster, culture, numberToUpgrade);
                        }
                    }
                }

                var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (var i = 0; i < iterations && GlobalMilitiaPower <= Globals.Settings.GlobalPowerPercent; i++)
                {
                    var validTroops = mobileParty.MemberRoster.GetTroopRoster().Where(x =>
                        x.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !x.Character.IsHero
                        && troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, x.Character));
                    var troopToTrain = validTroops.ToList().GetRandomElement();
                    number = troopToTrain.Number;
                    if (number < 1)
                    {
                        continue;
                    }

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsPercent / 100 * number * Rng.NextDouble());
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32(Rng.Next(minNumberToUpgrade, Convert.ToInt32((number + 1) / 2f)));
                    Log($"{mobileParty.LeaderHero.Name} is upgrading up to {numberToUpgrade} of {number} \"{troopToTrain.Character.Name}\".");
                    var xpGain = numberToUpgrade * DifficultyXpMap[Globals.Settings.XpGift.SelectedValue];
                    mobileParty.MemberRoster.AddXpToTroop(xpGain, troopToTrain.Character);
                    Campaign.Current._partyUpgrader.UpgradeReadyTroops(mobileParty.Party);
                    if (Globals.Settings.TestingMode)
                    {
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        mobileParty.Position2D = party.Position2D;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Bandit Militias is failing to configure parties!  Exception: " + ex);
                Trash(mobileParty);
            }
        }

        public static void ConfigureLeader(Hero hero)
        {
            if (hero.HomeSettlement is null)
            {
                _homeSettlement(hero) = hero.BornSettlement;
            }

            var random = Rng.Next(0, GangLeaderNames(NameGenerator.Current).Length);
            var originalStringId = hero.CharacterObject.StringId;
            hero.CharacterObject.StringId = hero.CharacterObject.StringId.Replace("Bandit_Militia", "");
            NameGenerator.Current.AddName(
                (uint)Traverse.Create(NameGenerator.Current)
                    .Method("CreateNameCode", hero.CharacterObject, GangLeaderNames(NameGenerator.Current), random)
                    .GetValue());
            hero.CharacterObject.StringId = originalStringId;
            var textObject = GangLeaderNames(NameGenerator.Current)[random].CopyTextObject();
            StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject);
            hero.SetName(new TextObject($"{textObject}"), hero.FirstName);
        }

        public static IEnumerable<ModBanditMilitiaPartyComponent> GetCachedBMs(bool forceRefresh = false)
        {
            if (forceRefresh || PartyCacheInterval < CampaignTime.Now.ToHours - 1)
            {
                PartyCacheInterval = CampaignTime.Now.ToHours;
                AllBMs = MobileParty.All.WhereQ(m => m.PartyComponent is ModBanditMilitiaPartyComponent)
                    .SelectQ(m => m.PartyComponent as ModBanditMilitiaPartyComponent).ToListQ();
            }

            return AllBMs;
        }

        public static void InitMilitia(MobileParty militia, TroopRoster[] rosters, Vec2 position)
        {
            var index = Globals.MobilePartyTrackerVM.Trackers.FindIndexQ(t => t.TrackedParty == militia);
            if (index >= 0)
            {
                Globals.MobilePartyTrackerVM.Trackers.RemoveAt(index);
            }

            militia.InitializeMobilePartyAtPosition(rosters[0], rosters[1], position);
            IsBandit(militia) = true;
            ConfigureMilitia(militia);
            TrainMilitia(militia);
        }

        // too slow
        //private static void LogMilitiaFormed(MobileParty mobileParty)
        //{
        //    try
        //    {
        //        var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
        //        var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
        //        LogLog($"{$"New Bandit Militia led by {mobileParty.LeaderHero.Name}",-70} | {troopString,10} | {strengthString,12} | >>> {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100}%");
        //    }
        //    catch (Exception ex)
        //    {
        //        LogLog(ex);
        //    }
        //}
    }
}
