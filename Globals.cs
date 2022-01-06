using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace Bandit_Militias
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal static class Globals
    {
        // merge/split criteria
        internal const float MergeDistance = 2;
        internal static float FindRadius = 7;
        internal static float MinDistanceFromHideout = 8;
        internal static int MinSplitSize = 0;

        // holders for criteria
        internal static float CalculatedMaxPartyStrength;
        internal static float CalculatedMaxPartySize;
        internal static float CalculatedGlobalPowerLimit;
        internal static float GlobalMilitiaPower;
        internal static float MilitiaPowerPercent;

        // misc
        internal static readonly Random Rng = new();
        internal static readonly Dictionary<MobileParty, Militia> PartyMilitiaMap = new();
        internal static readonly Dictionary<MobileParty, ImageIdentifierVM> PartyImageMap = new();
        internal static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        internal static readonly List<EquipmentElement> EquipmentItems = new();
        internal static List<Settlement> Hideouts = new();
        internal static Settings Settings;
        internal static List<ItemObject> Arrows = new();
        internal static List<ItemObject> Bolts = new();
        internal static readonly Stopwatch T = new();
        internal static readonly List<Equipment> BanditEquipment = new();
        internal static IEnumerable<CharacterObject> Recruits;
        internal static readonly List<Banner> Banners = new();
        internal static double LastCalculated;
        internal static float Variance => MBRandom.RandomFloatRanged(0.8f, 1.2f);
        // ArmsDealer compatibility
        internal static CultureObject BlackFlag => MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");
        
        // ReSharper disable once InconsistentNaming
        internal static MobilePartyTrackerVM MobilePartyTrackerVM;

        // FieldRefs
        internal static readonly AccessTools.FieldRef<Settlement, MBReadOnlyList<Hero>> HeroesWithoutParty =
            AccessTools.FieldRefAccess<Settlement, MBReadOnlyList<Hero>>("<HeroesWithoutParty>k__BackingField");

        internal static readonly Dictionary<string, int> DifficultyXpMap = new()
        {
            {"Off", 0},
            {"Normal", 300},
            {"Hard", 600},
            {"Hardest", 900},
        };

        internal static readonly Dictionary<string, int> GoldMap = new()
        {
            {"Low", 250},
            {"Normal", 500},
            {"Rich", 900},
            {"Richest", 2000},
        };

    }
}
