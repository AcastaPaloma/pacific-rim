using Game.Core;

namespace Game.AI
{
    /// <summary>Flag names of the kaiju brain (T-039). Everything brain-related is off unless <see cref="Scripted"/> is on.</summary>
    public static class BrainFlags
    {
        public const string Scripted = "kaiju.scriptedBrain";
    }

    /// <summary>Makes one brain per kaiju. Registered by a brain module only while its flag is on.</summary>
    public interface IKaijuBrainFactory
    {
        string BrainId { get; }
        IKaijuBrain Create();
    }
}
