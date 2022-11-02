using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;

// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable ConvertToAutoProperty
// ReSharper disable InconsistentNaming    
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
// ReSharper disable PropertyCanBeMadeInitOnly.Local
// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

namespace BanditMilitias
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        // public override IEnumerable<ISettingsPreset> GetBuiltInPresets()
        // {
        //     var basePresets = base.GetBuiltInPresets().ToListQ();
        //     var hardMode = new JsonSettingsPreset("Hard Mode", "Hard Mode","Hard Mode",null, () =>
        //     {
        //         Debug = false;
        //         CanTrain = true;
        //         return this;
        //     basePresets.Add(hardMode);
        //     return basePresets;
        // }
        // var hardMode = new JsonSettingsPreset("Hard Mode", "Hard Mode","Hard Mode",null, () =>
        // {
        //     Debug = false;
        //     CanTrain = true;
        //     RandomBanners = true;
        //     XpGift = new Dropdown<string>(Globals.DifficultyXpMap.Keys, 3);
        //     GoldReward = new Dropdown<string>(Globals.GoldMap.Keys, 2);
        //     CooldownHours = 8;
        //     DisperseSize = 30;
        //     RandomSplitChance = 50;
        //     MergeableSize = 10;
        //     GrowthChance = 100;
        //     GrowthPercent = 3;
        //     MilitiaSpawn = true;
        //     SpawnChance = 50;
        //     MaxItemValue = 10000;
        //     LooterUpgradePercent = 66;
        //     UpgradeUnitsPercent = 33;
        //     GlobalPowerPercent = 33;
        //     MaxTrainingTier = 5;
        //     return this;
        // }); 

        public override string FormatType => "json";
        public override string FolderName => "BanditMilitias";

        [SettingPropertyBool("{=BMTrain}Train Militias", HintText = "{=BMTrainDesc}Bandit heroes will train their militias.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings", GroupOrder = 0)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyInteger("{=BMDailyTrain}Daily Training Chance", 0, 100, HintText = "{=BMDailyTrainDesc}Each day they might train further.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings", GroupOrder = 2)]
        public float TrainingChance { get; private set; } = 10;

        [SettingPropertyDropdown("{=BMXpBoost}Militia XP Boost", HintText = "{=BMXpBoostDesc}Hardest grants enough XP to significantly upgrade troops.  Off grants no bonus XP.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public Dropdown<string> XpGift { get; internal set; } = new(Globals.DifficultyXpMap.Keys.SelectQ(k => k.ToString()), 1);

        [SettingPropertyInteger("{=BMGrowChance}Growth Chance Percent", 0, 100, HintText = "{=BMGrowChanceDesc}Chance per day that the militia will gain more troops (0 for off).", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int GrowthChance { get; private set; } = 50;

        [SettingPropertyInteger("{=BMGrowPercent}Growth Percent", 0, 100, HintText = "{=BMGrowPercentDesc}Grow each troop type by this percent.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int GrowthPercent { get; private set; } = 1;

        [SettingPropertyBool("{=BMIgnore}Ignore Villagers/Caravans", HintText = "{=BMIgnoreDesc}They won't be attacked by BMs.", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public bool IgnoreVillagersCaravans { get; private set; } = false;

        [SettingPropertyBool("{=BMSpawn}BM Spawn", HintText = "{=BMSpawnDesc}New BM will form spontaneously as well as by merging together normally.", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public bool MilitiaSpawn { get; private set; } = true;

        [SettingPropertyInteger("{=BMSpawnChance}Spawn Chance Percent", 1, 100, HintText = "{=BMSpawnChanceDesc}BM will spawn hourly at this likelihood.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int SpawnChance { get; private set; } = 30;

        [SettingPropertyInteger("{=BMCooldown}Change Cooldown", 0, 168, HintText = "{=BMCooldownDesc}BM won't merge or split a second time until this many hours go by.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int CooldownHours { get; private set; } = 24;

        [SettingPropertyDropdown("{=BMGoldReward}Bandit Hero Gold Reward", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public Dropdown<string> GoldReward { get; internal set; } = new(Globals.GoldMap.Keys.SelectQ(k => k.ToString()), 1);

        [SettingPropertyInteger("{=BMDisperse}Disperse Militia Size", 10, 100, HintText = "{=BMDisperseDesc}Militias defeated with fewer than this many remaining troops will be dispersed.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("{=BMSizeAdjustments}Size Adjustments", GroupOrder = 2)]
        public int DisperseSize { get; private set; } = 20;

        [SettingPropertyInteger("{=BMMinSize}Minimum Size", 1, 100, HintText = "{=BMMinSizeDesc}No BMs smaller than this will form.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("{=BMSizeAdjustments}Size Adjustments")]
        public int MinPartySize { get; private set; } = 20;

        [SettingPropertyInteger("{=BMMergeSize}Mergeable party size", 1, 100, HintText = "{=BMMergeSizeDesc}Small looter and bandit parties won't merge.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("{=BMSizeAdjustments}Size Adjustments")]
        public int MergeableSize { get; private set; } = 10;

        [SettingPropertyInteger("{=BMSplit}Random Split Chance", 0, 100, HintText = "{=BMSplitDesc}How likely BM is to split when large enough.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments", GroupOrder = 1)]
        public int RandomSplitChance { get; private set; } = 10;

        [SettingPropertyInteger("{=BMMaxValue}Max Item Value", 1000, 1000000, HintText = "{=BMMaxValueDesc}Limit the per-piece value of equipment given to the Heroes.  Mostly for when other mods give you Hero loot.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int MaxItemValue { get; private set; } = 5000;

        [SettingPropertyInteger("{=BMLooter}Looter Conversions", 0, 100, HintText = "How many looters get made into better units when training.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int LooterUpgradePercent { get; private set; } = 15;

        [SettingPropertyInteger("{=BMUpgrade}Upgrade Units", 0, 100, HintText = "{=BMUpgradeDesc}Upgrade (at most) this percentage of troops when training occurs.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("{=BMPower}Global Power", 0, 1000, HintText = "{=BMPowerDesc}Major setting.  Setting higher means more, bigger BMs.", Order = 11, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int GlobalPowerPercent { get; private set; } = 15;

        [SettingPropertyInteger("{=BMTier}Max Training Tier", 1, 6, HintText = "{=BMTierDesc}BM won't train any units past this tier.", Order = 13, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyInteger("{=BMWeaker}Ignore Weaker Parties", 0, 100, HintText = "{=BMWeakerDesc}10 means any party 10% weaker will be ignored.  100 attacks without restriction.", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int MaxStrengthDeltaPercent { get; private set; } = 10;

        [SettingPropertyBool("{=BMPillage}Allow Pillaging", HintText = "{=BMPillageDesc}Allow PILLAGING!.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public bool AllowPillaging { get; private set; } = true;

        [SettingPropertyText("{=BMStringSetting}Bandit Militia String", Order = 0, HintText = "{=BMStringSettingDesc}What to name a Bandit Militia.", RequireRestart = false)]
        public string BanditMilitiaString { get; set; } = Globals.BanditMilitiaString.ToString();

        [SettingPropertyText("{=BMLeaderlessStringSetting}Leaderless Bandit Militia String", Order = 1, HintText = "{=BMLeaderlessStringSettingDesc}What to name a Bandit Militia with no leader.", RequireRestart = false)]
        public string LeaderlessBanditMilitiaString { get; set; } = Globals.LeaderlessBanditMilitiaString.ToString();

        [SettingPropertyBool("{=BMMarkers}Militia Map Markers", HintText = "{=BMMarkersDesc}Have omniscient view of BMs.", Order = 2, RequireRestart = false)]
        public bool Trackers { get; private set; } = false;

        [SettingPropertyInteger("{=BMTrackSize}Minimum BM Size To Track", 1, 500, HintText = "{=BMTrackSizeDesc}Any smaller BMs won't be tracked.", Order = 3, RequireRestart = false)]
        public int TrackedSizeMinimum { get; private set; } = 50;

        [SettingPropertyBool("{=BMBanners}Random Banners", HintText = "{=BMBannersDesc}BMs will have unique banners, or basic bandit clan ones.", Order = 4, RequireRestart = false)]
        public bool RandomBanners { get; set; } = true;

        [SettingPropertyBool("{=BMRaidNotices}Village raid notices", HintText = "{=BMRaidNoticesDesc}When your fiefs are raided you'll see a banner message.", Order = 5, RequireRestart = false)]
        public bool ShowRaids { get; set; } = true;

        [SettingPropertyBool("{=BMDebug}Debug Logging", HintText = "{=BMDebugDesc}Creates logfile output in the mod folder.", Order = 6, RequireRestart = false)]
        public bool Debug { get; private set; }

        [SettingPropertyBool("{=BMTesting}Testing Mode", HintText = "{=BMTestingDesc}Teleports BMs to you.", Order = 7, RequireRestart = false)]
        public bool TestingMode { get; internal set; }

        private const string id = "BanditMilitias";
        private string displayName = $"BanditMilitias {typeof(Settings).Assembly.GetName().Version.ToString(3)}";

        public override string Id => id;
        public override string DisplayName => displayName;
    }
}
