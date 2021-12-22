using TaleWorlds.SaveSystem;

namespace Bandit_Militias
{
    // ReSharper disable once UnusedType.Global
    // class is loaded by the game automatically
    public class MilitiaSaveableTypeDefiner  : SaveableTypeDefiner
    {
        public MilitiaSaveableTypeDefiner() : base(42069)
        {
        }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(ModBanditMilitiaPartyComponent), 42069);
        }
    }
}
