using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming  

namespace Bandit_Militias.Helpers
{
    public static class Helper
    {
        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            var mountedTroops = troopRoster.GetTroopRoster().Where(x => x.Character.IsMounted);
            var mountedTroopTypeCount = mountedTroops.Count();
            var total = 0;
            for (var i = 0; i < mountedTroopTypeCount; i++)
            {
                total += troopRoster.GetElementNumber(i);
            }

            return total;
        }

        internal static void TrySplitParty(MobileParty mobileParty)
        {
            if (GlobalMilitiaPower + mobileParty.Party.TotalStrength > CalculatedGlobalPowerLimit ||
                mobileParty.MemberRoster.TotalManCount < MinSplitSize ||
                !mobileParty.IsBM() ||
                mobileParty.IsTooBusyToMerge())
            {
                return;
            }

            var roll = Rng.NextDouble();
            if (roll <= Globals.Settings.RandomSplitChance ||
                !mobileParty.IsBM() ||
                mobileParty.Party.TotalStrength <= CalculatedMaxPartyStrength * Globals.Settings.StrengthSplitFactor * Variance ||
                mobileParty.Party.MemberRoster.TotalManCount <= CalculatedMaxPartySize * Globals.Settings.SizeSplitFactor * Variance)
            {
                return;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().Where(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster())
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Mod.Log("Bad item: " + item);
                    continue;
                    //Traverse.Create(PartyBase.MainParty).Property<ItemRoster>("ItemRoster").Value.Remove(item);
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            // toss a coin (to your Witcher)
            if (rosterElement.Number == 1)
            {
                if (Rng.Next(0, 1) == 0)
                {
                    troops1.AddToCounts(rosterElement.Character, 1);
                }
                else
                {
                    troops2.AddToCounts(rosterElement.Character, 1);
                }
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                troops1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
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
                SetMilitiaPatrol(militia1.MobileParty);
                SetMilitiaPatrol(militia2.MobileParty);
                //var settlements = Settlement.FindSettlementsAroundPosition(original.Position2D, 30).ToList();
                //militia1.MobileParty.SetMovePatrolAroundSettlement(settlements.GetRandomElement() ?? Settlement.All.GetRandomElement());
                //militia2.MobileParty.SetMovePatrolAroundSettlement(settlements.GetRandomElement() ?? Settlement.All.GetRandomElement());
                Mod.Log($"{militia1.MobileParty.StringId} <- Split -> {militia2.MobileParty.StringId}");
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
        internal static bool IsValidParty(MobileParty __instance)
        {
            if (__instance.IsBandit
                || __instance.IsBM()
                && __instance.Party.IsMobile
                && __instance.CurrentSettlement is null
                && __instance.Party.MemberRoster.TotalManCount > 0
                && !__instance.IsUsedByAQuest()
                && !verbotenParties.Contains(__instance.StringId)
                && !__instance.IsTooBusyToMerge())
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

        private static void PurgeList(string logMessage, List<MobileParty> mobileParties)
        {
            if (mobileParties.Count > 0)
            {
                Mod.Log(logMessage);
                foreach (var mobileParty in mobileParties)
                {
                    Mod.Log(">>> FLUSH " + mobileParty.Name);
                    Trash(mobileParty);
                }
            }
        }

        internal static void Trash(MobileParty mobileParty)
        {
            Mod.Log("Trashing " + mobileParty.Name);
            PartyMilitiaMap.Remove(mobileParty);
            DestroyPartyAction.Apply(null, mobileParty);
        }

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.");
            FlushBanditMilitias();
            FlushDeadBanditRemnants();
            FlushHeroes();
            FlushMilitiaCharacterObjects();
            FlushMapEvents();
            Flush();
        }

        private static void FlushBanditMilitias()
        {
            PartyMilitiaMap.Clear();
            var hasLogged = false;
            var partiesToRemove = MobileParty.All.Where(x => x.StringId.StartsWith("Bandit_Militia")).ToList();
            foreach (var mobileParty in partiesToRemove)
            {
                if (!hasLogged)
                {
                    Mod.Log($">>> FLUSH {partiesToRemove.Count} Bandit Militias");
                    hasLogged = true;
                }

                Trash(mobileParty);
            }
        }

        // still needed 1.5.10
        private static void FlushDeadBanditRemnants()
        {
            // prisoners somehow of settlements
            foreach (var settlement in Settlement.All)
            {
                var count = settlement.Parties.Sum(p => p.PrisonRoster.Count);
                if (count <= 0)
                {
                    continue;
                }

                foreach (var party in settlement.Parties)
                {
                    for (var i = 0; i < party.PrisonRoster.Count; i++)
                    {
                        try
                        {
                            var prisoner = party.PrisonRoster.GetCharacterAtIndex(i);
                            if (prisoner.StringId.EndsWith("Bandit_Militia"))
                            {
                                Mod.Log($">>> FLUSH dead bandit hero prisoner {prisoner.HeroObject.Name} at {settlement.Name}.");
                                party.PrisonRoster.AddToCounts(prisoner, -1);
                                MBObjectManager.Instance.UnregisterObject(prisoner);
                            }
                        }
                        catch (Exception ex)
                        {
                            Mod.Log(ex);
                        }
                    }
                }
            }

            // prisoners somehow of parties
            foreach (var mobileParty in MobileParty.All.Where(x => x.Party.PrisonRoster.TotalManCount > 0))
            {
                // GetCharacterAtIndex() throws, if 0...
                var count = Traverse.Create(mobileParty.Party.PrisonRoster).Field<int>("_count").Value;
                if (count < 0)
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    var prisoner = mobileParty.Party.PrisonRoster.GetCharacterAtIndex(i);
                    if (prisoner.IsHero &&
                        !prisoner.HeroObject.IsAlive)
                    {
                        mobileParty.Party.PrisonRoster.AddToCounts(prisoner, -1);
                        Mod.Log($">>> FLUSH Dead bandit hero mobile party prisoner {prisoner.HeroObject.Name}.");
                    }
                }
            }
        }

        internal static void FlushMilitiaCharacterObjects()
        {
            // leak from CampaignTickPatch, trashing parties there doesn't get rid of all remnants...
            // many hours trying to find proper solution
            // 2-4 ms
            var COs = new List<CharacterObject>();
            MBObjectManager.Instance.GetAllInstancesOfObjectType(ref COs);
            var BMs = COs.Where(c => c.HeroObject?.PartyBelongedTo is null
                                     && c.StringId.EndsWith("Bandit_Militia")).ToList();
            if (BMs.Any())
            {
                Mod.Log($">>> FLUSH {BMs.Count} BM CharacterObjects");
                BMs.Do(c => MBObjectManager.Instance.UnregisterObject(c));
                var charactersField = Traverse.Create(Campaign.Current).Field<MBReadOnlyList<CharacterObject>>("_characters");
                var tempCharacterObjectList = new List<CharacterObject>(charactersField.Value);
                tempCharacterObjectList = tempCharacterObjectList.Except(BMs).ToList();
                charactersField.Value = new MBReadOnlyList<CharacterObject>(tempCharacterObjectList);
            }
        }

        internal static void ReHome()
        {
            var tempList = PartyMilitiaMap.Values.Where(x => x?.Hero?.HomeSettlement is null).Select(x => x.Hero).ToList();
            Mod.Log($"Fixing {tempList.Count} null HomeSettlement heroes");
            tempList.Do(x => Traverse.Create(x).Field("_homeSettlement").SetValue(Hideouts.GetRandomElement()));
        }

        internal static void Flush()
        {
            FlushSettlementHeroesWithoutParty();
            FlushHideoutsOfMilitias();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadCharacterObjects();
            FlushZeroParties();
            Mod.Log(">>> FLUSHED.");
        }

        // not needed after 2.8.0
        private static void FlushSettlementHeroesWithoutParty()
        {
            // ~~fixed in 2.8.0, leaving in for now~~
            foreach (var settlement in Settlement.All.Where(x => x.HeroesWithoutParty.Count > 0))
            {
                var militiaHeroes = settlement.HeroesWithoutParty.Intersect(PartyMilitiaMap.Values.Select(x => x.Hero)).ToList();
                for (var i = 0; i < militiaHeroes.Count; i++)
                {
                    Traverse.Create(HeroesWithoutParty(settlement)).Field<List<Hero>>("_list").Value.Remove(militiaHeroes[i]);
                    Mod.Log($">>> FLUSH Removing bandit hero without party {militiaHeroes[i].Name} at {settlement}.");
                }
            }
        }

        // a bunch of hacks to dispose of leak(s) somewhere
        private static void FlushHeroes()
        {
            var heroes = Hero.FindAll(hero =>
                    (hero.PartyBelongedTo is null
                     && hero.PartyBelongedToAsPrisoner is null
                     || hero.HeroState == Hero.CharacterStates.Prisoner)
                    && Clan.BanditFactions.Contains(hero.MapFaction)
                    && hero.StringId.EndsWith("Bandit_Militia"))
                .ToList();

            for (var i = 0; i < heroes.Count; i++)
            {
                Mod.Log(">>> FLUSH prisoner, or hero with no party or settlement: " + heroes[i].Name);
                try
                {
                    heroes[i].RemoveMilitiaHero();
                }
                catch (NullReferenceException)
                {
                    // 3.0.2 throws with Heroes Must Die
                    Mod.Log("Squelching NRE at Helper.FlushHeroes");
                }
            }
        }

        private static void FlushZeroParties()
        {
            var parties = MobileParty.All.Where(m =>
                m != MobileParty.MainParty &&
                m.CurrentSettlement is null && m.MemberRoster.TotalManCount == 0).ToList();
            for (var i = 0; i < parties.Count; i++)
            {
                Mod.Log($">>> FLUSH party without a current settlement or any troops.");
                parties[i].LeaderHero.RemoveMilitiaHero();
                parties[i].RemoveParty();
            }
        }

        private static void FlushMapEvents()
        {
            var mapEvents = Traverse.Create(Campaign.Current.MapEventManager).Field("_mapEvents").GetValue<List<MapEvent>>();
            for (var index = 0; index < mapEvents.Count; index++)
            {
                var mapEvent = mapEvents[index];
                if (mapEvent.InvolvedParties.Any(p =>
                    p.MobileParty.IsBM()))
                {
                    Mod.Log(">>> FLUSH MapEvent.");
                    mapEvent.FinalizeEvent();
                }
            }
        }

        private static void FlushHideoutsOfMilitias()
        {
            foreach (var hideout in Settlement.All.Where(s => s.IsHideout()).ToList())
            {
                for (var index = 0; index < hideout.Parties.Count; index++)
                {
                    var party = hideout.Parties[index];
                    if (party.IsBM())
                    {
                        Mod.Log(">>> FLUSH Hideout.");
                        LeaveSettlementAction.ApplyForParty(party);
                        SetMilitiaPatrol(party);
                    }
                }
            }
        }

        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.FindAll(x =>
                x.Name.ToString() == "Bandit Militia" && x.PartyBelongedTo is null).ToList();
            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($">>> FLUSH  {heroes.Count} null-party heroes.");
                }

                Mod.Log(">>> FLUSH null party hero " + hero.Name);
                hero.RemoveMilitiaHero();
            }
        }

        private static void FlushBadCharacterObjects()
        {
            var badChars = CharacterObject.All.Where(x => x.HeroObject is null)
                .Where(x =>
                    x.Name is null ||
                    x.Occupation == Occupation.NotAssigned ||
                    x.Occupation == Occupation.Outlaw &&
                    x.HeroObject?.CurrentSettlement is not null)
                .Where(x =>
                    !x.StringId.Contains("template") &&
                    !x.StringId.Contains("char_creation") &&
                    !x.StringId.Contains("equipment") &&
                    !x.StringId.Contains("for_perf") &&
                    !x.StringId.Contains("dummy") &&
                    !x.StringId.Contains("npc_") &&
                    // Calradia Expanded Kingdoms
                    !x.StringId.Contains("vlandian_balestrieri_veterani") &&
                    !x.StringId.Contains("unarmed_ai"))
                .ToList();

            var hasLogged = false;
            foreach (var badChar in badChars)
            {
                if (badChar is null)
                {
                    continue;
                }

                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($">>> FLUSH  {badChars.Count} bad characters.");
                }

                Mod.Log($">>> FLUSH mock Unregistering {badChar.StringId}");
                //Traverse.Create(badChar.HeroObject?.CurrentSettlement)
                //    .Field("_heroesWithoutParty").Method("Remove", badChar.HeroObject).GetValue();
                //MBObjectManager.Instance.UnregisterObject(badChar);
            }
        }

        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in PartyMilitiaMap.Values.Where(x => x.MobileParty.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty.MobileParty + " size " + mobileParty.MobileParty.MemberRoster.TotalManCount);
                tempList.Add(mobileParty.MobileParty);
            }

            PurgeList($"FlushNeutralBanditParties Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static void FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in PartyMilitiaMap.Values.Where(x => x.MobileParty.MemberRoster.TotalManCount == 0))
            {
                tempList.Add(mobileParty.MobileParty);
            }

            PurgeList($"FlushEmptyMilitiaParties Clearing {tempList.Count} empty parties", tempList);
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
            var verbotenItems = new[]
            {
                "Sparring Targe",
                "Trash Item",
                "Torch",
                "Horse Whip",
                "Push Fork",
                "Bound Crossbow"
            };

            var all = Items.All.Where(i =>
                i.ItemType != ItemObject.ItemTypeEnum.Goods
                && i.ItemType != ItemObject.ItemTypeEnum.Horse
                && i.ItemType != ItemObject.ItemTypeEnum.HorseHarness
                && i.ItemType != ItemObject.ItemTypeEnum.Animal
                && i.ItemType != ItemObject.ItemTypeEnum.Banner
                && i.ItemType != ItemObject.ItemTypeEnum.Book
                && i.ItemType != ItemObject.ItemTypeEnum.Invalid
                && i.Value <= Globals.Settings.MaxItemValue * Variance
                && !i.Name.Contains("Crafted")
                && !i.Name.Contains("Wooden")
                && !i.Name.Contains("Practice")
                && !verbotenItems.Contains(i.Name.ToString())).ToList();
            Arrows = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows)
                .Where(x => !x.Name.Contains("Ballista")).ToList();
            Bolts = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts).ToList();
            all = all.Where(i => i.Value >= 1000 && i.Value <= Globals.Settings.MaxItemValue * Variance).ToList();
            var oneHanded = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon);
            var twoHanded = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon);
            var polearm = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Polearm);
            var thrown = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Thrown &&
                                        i.Name.ToString() != "Boulder" && i.Name.ToString() != "Fire Pot");
            var shields = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Shield);
            var bows = all.Where(i =>
                i.ItemType == ItemObject.ItemTypeEnum.Bow ||
                i.ItemType == ItemObject.ItemTypeEnum.Crossbow);
            var any = new List<ItemObject>(oneHanded.Concat(twoHanded).Concat(polearm).Concat(thrown).Concat(shields).Concat(bows).ToList());
            any.Do(i => EquipmentItems.Add(new EquipmentElement(i)));
        }

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

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (slot == 3 && !gear[3].IsEmpty)
                    {
                        break;
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow ||
                        randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
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
                            if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow)
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
                //Mod.Log("-----");
                //for (var i = 0; i < 10; i++)
                //{
                //    Mod.Log(gear[i].Item?.Name);
                //}
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
            if (force
                || LastCalculated < CampaignTime.Now.ToHours - 8)
            {
                LastCalculated = CampaignTime.Now.ToHours;
                var parties = MobileParty.All.Where(p => p.LeaderHero is not null && !p.IsBM()).ToList();
                CalculatedMaxPartySize = (float) parties.Average(p => p.Party.PartySizeLimit) * Globals.Settings.MaxPartySizeFactor * Variance;
                var totalStrength = parties.Sum(p => p.Party.TotalStrength);
                CalculatedMaxPartyStrength = totalStrength / parties.Count * Globals.Settings.PartyStrengthFactor * Variance;
                CalculatedGlobalPowerLimit = totalStrength * Variance;
                GlobalMilitiaPower = PartyMilitiaMap.Keys.Sum(p => p.Party.TotalStrength);
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
                var initialize = AccessTools.Method(typeof(MapEvent), "Initialize", new[] {typeof(PartyBase), typeof(PartyBase), typeof(MapEvent.BattleTypes)});
                initialize.Invoke(PartyBase.MainParty.MapEvent, new object[] {attacker, defender, MapEvent.BattleTypes.None});
            }
        }

        internal static bool IsBM(this MobileParty mobileParty)
        {
            return mobileParty is not null && PartyMilitiaMap.ContainsKey(mobileParty);
        }

        internal static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            FileLog.Log($"Inserting {insertSize} at {insertPoint}.");

            // in case insertPoint is near the start of the method's IL
            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // codes[266 - 5 + 0].opcode
                // codes[266 - 5 + 4].opcode
                FileLog.Log($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                FileLog.Log($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            // in case insertPoint is near the end of the method's IL
            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // 266 + 2 - 5 + 0
                // 266 + 2 - 5 + 4
                FileLog.Log($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }

        // this is a bit wasteful, if doing a list of heroes since it replaces the whole list each time
        // but it only takes 1000 ticks so whatever?
        internal static void RemoveCharacterFromReadOnlyList(CharacterObject hero)
        {
            var charactersField = Traverse.Create(Campaign.Current).Field<MBReadOnlyList<CharacterObject>>("_characters");
            charactersField.Value = new MBReadOnlyList<CharacterObject>(charactersField.Value.Except(new[] {hero}).ToList());
        }

        // anti-congregation, seems like circumstances can lead to a positive feedback loop in 3.0.3 (and a ton of BMs in one area)
        internal static void SetMilitiaPatrol(MobileParty mobileParty)
        {
            var nearbyMilitias = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, 30, m => m.IsBM()).Count();
            if (nearbyMilitias > 5)
            {
                mobileParty.SetMovePatrolAroundSettlement(Settlement.All.GetRandomElement());
            }
            else
            {
                var nearbySettlement = Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, 30)?.ToList().GetRandomElement();
                mobileParty.SetMovePatrolAroundSettlement(nearbySettlement ?? Settlement.All.GetRandomElement());
            }
        }
    }
}
