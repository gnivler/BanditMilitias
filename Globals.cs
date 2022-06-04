using System;
using System.Collections.Generic;
using System.Diagnostics;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Globals
    {
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
        public static readonly Dictionary<MobileParty, ImageIdentifierVM> PartyImageMap = new();
        public static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        public static readonly Dictionary<CultureObject, List<CharacterObject>> Recruits = new();

        // misc
        public static readonly Random Rng = new();
        public static readonly Stopwatch T = new();
        public static Settings Settings;
        public static readonly List<EquipmentElement> EquipmentItems = new();
        public static List<ItemObject> Arrows = new();
        public static List<ItemObject> Bolts = new();
        public static readonly List<Equipment> BanditEquipment = new();
        public static readonly List<Banner> Banners = new();
        public static double LastCalculated;
        public static double PartyCacheInterval;
        public static int RaidCap;
        //public static Dictionary<MapEventSide, List<EquipmentElement>> LootRecord = new();

        // ReSharper disable once InconsistentNaming
        public static MobilePartyTrackerVM MobilePartyTrackerVM;

        public static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);

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
