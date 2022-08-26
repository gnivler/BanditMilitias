using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Globals
    {
        internal static void ClearGlobals()
        {
            PartyImageMap = new();
            ItemTypes = new();
            Recruits = new();
            LootRecord = new();
            EquipmentItems = new();
            Arrows = new();
            Bolts = new();
            BanditEquipment = new();
            LastCalculated = 0;
            PartyCacheInterval = 0;
            RaidCap = 0;
            EquipmentMap = new();
            MapMobilePartyTrackerVM.Trackers.Clear();
            HeroCharacters = new();
            Mounts = new();
            Saddles = new();
            Hideouts = new();
            AllBMs = new ModBanditMilitiaPartyComponent[] { };
        }

        // merge/split criteria
        public const float MergeDistance = 2;
        public const float FindRadius = 20;
        public const float MinDistanceFromHideout = 8;

        // holders for criteria
        public static float CalculatedMaxPartySize;
        public static float CalculatedGlobalPowerLimit;
        public static float GlobalMilitiaPower;
        public static float MilitiaPowerPercent;
        public static float MilitiaPartyAveragePower;

        // dictionary maps
        public static Dictionary<MobileParty, ImageIdentifierVM> PartyImageMap = new();
        public static Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        public static Dictionary<CultureObject, List<CharacterObject>> Recruits = new();
        public static Dictionary<MapEventSide, List<EquipmentElement>> LootRecord = new();

        // object tracking
        internal static List<Hero> BanditMilitiaHeroes = new();
        internal static List<CharacterObject> BanditMilitiaCharacters = new();
        internal static List<CharacterObject> BanditMilitiaTroops = new();

        // misc
        public static readonly Random Rng = new();
        public static readonly Stopwatch T = new();
        public static Settings Settings;
        public static List<EquipmentElement> EquipmentItems = new();
        public static List<ItemObject> Arrows = new();
        public static List<ItemObject> Bolts = new();
        public static List<Equipment> BanditEquipment = new();
        public static readonly List<Banner> Banners = new();
        public static double LastCalculated;
        public static double PartyCacheInterval;
        public static int RaidCap;
        public static Dictionary<string, Equipment> EquipmentMap = new();
        private static Clan looters;
        public static Clan Looters => looters ??= Clan.BanditFactions.First(c => c.StringId == "looters");
        private static IEnumerable<Clan> synthClans;
        public static IEnumerable<Clan> SynthClans => synthClans ??= Clan.BanditFactions.Except(new[] { Looters });
        public static List<ItemObject> Mounts;
        public static List<ItemObject> Saddles;
        public static List<Settlement> Hideouts;
        internal static IEnumerable<ModBanditMilitiaPartyComponent> AllBMs;
        internal static CampaignPeriodicEventManager CampaignPeriodicEventManager;
        internal static object Ticker;

        // ReSharper disable once InconsistentNaming
        public static MapMobilePartyTrackerVM MapMobilePartyTrackerVM;

        public static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);
        public static List<CharacterObject> HeroCharacters = new();

        // ArmsDealer compatibility
        public static CultureObject BlackFlag => MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");

        public static readonly Dictionary<string, int> DifficultyXpMap = new()
        {
            { "Off", 0 },
            { "Normal", 300 },
            { "Hard", 600 },
            { "Hardest", 900 },
        };

        public static readonly Dictionary<string, int> GoldMap = new()
        {
            { "Low", 250 },
            { "Normal", 500 },
            { "Rich", 900 },
            { "Richest", 2000 },
        };
    }
}
