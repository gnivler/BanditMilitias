// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

using System;
using System.Collections.Generic;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Dropdown;
using MCM.Abstractions.Settings.Base;
using MCM.Abstractions.Settings.Base.Global;

namespace Bandit_Militias
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        public override IDictionary<string, Func<BaseSettings>> GetAvailablePresets()
        {
            var basePresets = base.GetAvailablePresets();
            basePresets.Add("Hard Mode", () => new Settings
            {
                Debug = false,
                CanTrain = true,
                RandomBanners = true,
                XpGift = new DropdownDefault<string>(Globals.DifficultyXpMap.Keys, 3),
                GoldReward = new DropdownDefault<string>(Globals.GoldMap.Keys, 2),
                CooldownHours = 8,
                MinPartySize = 30,
                RandomSplitChance = 0.5f,
                StrengthSplitFactor = 0,
                SizeSplitFactor = 0,
                PartyStrengthFactor = 1.25f,
                MaxPartySizeFactor = 1.25f,
                MinPartySizeToConsiderMerge = 30,
                GrowthChance = 1,
                GrowthPercent = 0.03f,
                MaxItemValue = 8000,
                LooterUpgradeFactor = 0.66f,
                MaxStrengthDeltaPercent = 100,
                UpgradeUnitsFactor = 0.33f,
                GlobalPowerFactor = 0.25f,
                MaxTrainingTier = 5
            });
            return basePresets;
        }

        public override string FormatType => "json";
        public override string FolderName => "Bandit Militias";

        [SettingPropertyBool("Train Militias", HintText = "\nBandit heroes will train their militias.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings", GroupOrder = 0)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyDropdown("Militia XP Boost", HintText = "\nHardest grants enough XP to significantly upgrade troops.  No effect if Training is disabled.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public DropdownDefault<string> XpGift { get; private set; } = new(Globals.DifficultyXpMap.Keys, 1);

        [SettingPropertyFloatingInteger("Growth Chance", 0, 1, HintText = "\nChance per day that the militia will gain more troops (0 for off).", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public float GrowthChance { get; private set; } = 0.5f;

        [SettingPropertyInteger("Growth Percent", 1, 100, HintText = "\nGrow by this percent when growth occurs.", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public float GrowthPercent { get; private set; } = 1;

        [SettingPropertyInteger("Change Cooldown", 0, 168, HintText = "\nAfter merging or splitting, a militia will not change again until this much time passes.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int CooldownHours { get; private set; } = 24;

        [SettingPropertyDropdown("Bandit Hero Gold Reward", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public DropdownDefault<string> GoldReward { get; private set; } = new(Globals.GoldMap.Keys, 1);

        [SettingPropertyInteger("Minimum Militia Size", 0, 100, HintText = "\nMilitias defeated with fewer than this many remaining troops will be dispersed.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments", GroupOrder = 2)]
        public int MinPartySize { get; private set; } = 20;

        [SettingPropertyInteger("Minimum Size To Merge", 1, 100, HintText = "\nBandit groups smaller than this will not form militias.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments")]
        public int MinPartySizeToConsiderMerge { get; private set; } = 20;

        [SettingPropertyFloatingInteger("Random Split Chance", 0, 1, HintText = "\nChance per day that any given militia will split in half when requirements are met.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments", GroupOrder = 1)]
        public float RandomSplitChance { get; private set; } = 0.25f;

        [SettingPropertyFloatingInteger("Strength Split", 0, 1, HintText = "\nMilitias won't consider splitting until this factor of the world average strength.", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float StrengthSplitFactor { get; private set; } = 0.8f;

        [SettingPropertyFloatingInteger("Size Split", 0, 1, HintText = "\nMilitias won't consider splitting until this factor of the world average party size.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float SizeSplitFactor { get; private set; } = 0.8f;

        [SettingPropertyFloatingInteger("Max Party Strength", 0, 10, HintText = "\nMilitia's won't merge into something more powerful, than this factor of the world average strength.", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float PartyStrengthFactor { get; private set; } = 0.9f;

        [SettingPropertyFloatingInteger("Max Party Size", 0, 10, HintText = "\nMilitia's won't merge into something larger, than this factor of the main party's size.", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float MaxPartySizeFactor { get; private set; } = 0.9f;

        [SettingPropertyInteger("Max Item Value", 3000, 1000000, HintText = "\nLimit the per-piece value of equipment given to the Heroes.\nMostly for when other mods give you Hero loot.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxItemValue { get; private set; } = 3000;

        [SettingPropertyFloatingInteger("Looter Conversions", 0, 1f, HintText = "\nThis factor of all looters are converted to the most-prevalent local faction's basic troops, when training occurs.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float LooterUpgradeFactor { get; private set; } = 0.25f;

        [SettingPropertyInteger("Limit Militia Engagements", 0, 100, HintText = "\nPercentage of strength difference that will make Militias ignore targets.\n100 removes the limit.", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxStrengthDeltaPercent { get; private set; } = 10;

        [SettingPropertyFloatingInteger("Upgrade Units", 0, 1f, HintText = "\nUpgrade at most this factor of any given troop type quantity when training occurs.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float UpgradeUnitsFactor { get; private set; } = 0.25f;

        [SettingPropertyFloatingInteger("Global Power", 0, 10f, HintText = "\nThe total Militias power will remain under this factor of the world total strength.", Order = 11, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public float GlobalPowerFactor { get; private set; } = 0.15f;

        [SettingPropertyInteger("Max Training Tier", 1, 6, HintText = "\nDon't train any units past this tier.", Order = 12, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyBool("Random Banners", HintText = "\nBandit Militias will have unique banners, or basic bandit clan ones.", RequireRestart = false)]
        public bool RandomBanners { get; internal set; } = true;

        private bool debug;
        [SettingPropertyBool("Debug Logging", HintText = "\nCreates output in the mod folder.", RequireRestart = false)]
        public bool Debug
        {
            get => debug;
            private set
            {
                Mod.Log("Debug " + !Debug);
                debug = !debug;
            }
        }

        private string id = "BanditMilitias";
        private string displayName = $"Bandit Militias {typeof(Settings).Assembly.GetName().Version.ToString(3)}";
        public override string Id => id;
        public override string DisplayName => displayName;
    }
}
