// DEF-M1save-6: registers the destruction@v1 save section with the SaveSystem when Destruction is installed. Engine-free.
using Game.Core;
using Game.World.Destruction;

namespace Game.Composition
{
    public static class DestructionSaveInstaller
    {
        /// <summary>Registers a <see cref="DestructionSaveParticipant"/> over <paramref name="model"/>. Returns null (nothing registered) when the model or the save system is missing, i.e. Destruction is not installed.</summary>
        public static DestructionSaveParticipant Install(DamageModel model, SaveSystem saves)
        {
            if (model == null || saves == null) return null;
            var participant = new DestructionSaveParticipant(model);
            saves.Register(participant);
            return participant;
        }
    }
}
