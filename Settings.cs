// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

namespace Bandit_Militias
{
    public class Settings
    {
        public bool Debug = false;
        public bool CanTrain = true;
        public bool RandomBanners = true;
        public string XpGift = "NORMAL";
        public string GoldReward = "NORMAL";
        public float CooldownHours = 24;
        public int MinPartySize = 20;
        public int MinPartySizeToConsiderMerge = 1;
        public float RandomSplitChance = 0.25f;
        public float StrengthSplitFactor = 0.8f;
        public float SizeSplitFactor = 0.8f;
        public float PartyStrengthFactor = 0.9f;
        public float MaxPartySizeFactor = 0.9f;
        public float GrowthChance = 0.5f;
        public float GrowthFactor = 0.01f;
        public int MaxItemValue = 3000;
        public float LooterUpgradeFactor = 0.25f;
        public int MaxStrengthDeltaPercent = 10;
        public float UpgradeUnitsFactor = 0.25f;
        public float GlobalPowerFactor = 0.15f;
        public int MaxTrainingTier = 4;
    }
}
