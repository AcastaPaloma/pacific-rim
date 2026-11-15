// Engine-free composition hook of the pack layer (MODULARITY rule 5): registers the member-to-coordinator directory and the pack@1 brain factory
// only when ai.pack is on. T-113 follow-up (registration in Composition).
using System.Collections.Generic;
using Game.Core;

namespace Game.AI.Pack
{
    /// <summary>
    /// Routes a member's directive request to the coordinator of its pack. The host (or the emergence/formation code) assigns members to a
    /// coordinator when a pack forms and removes them when it splits or the member dies. Not thread-safe; used on the simulation thread.
    /// </summary>
    public sealed class PackDirectory : IPackDirectiveSource
    {
        private readonly Dictionary<ulong, PackCoordinator> _byMember = new Dictionary<ulong, PackCoordinator>();

        public int MemberCount => _byMember.Count;

        public void Assign(EntityId member, PackCoordinator coordinator)
        {
            if (!member.IsValid || coordinator == null) return;
            _byMember[member.Value] = coordinator;
        }

        public bool Remove(EntityId member) => _byMember.Remove(member.Value);

        public void Clear() => _byMember.Clear();

        public PackCoordinator CoordinatorOf(EntityId member) => _byMember.TryGetValue(member.Value, out var c) ? c : null;

        public bool TryGetDirective(EntityId id, out PackDirective directive)
        {
            if (_byMember.TryGetValue(id.Value, out var c)) return c.TryGetDirective(id, out directive);
            directive = default; return false;
        }
    }

    public static class PackModule
    {
        /// <summary>
        /// Returns the factory it registered, or null when ai.pack is off or <paramref name="inner"/> is null (nothing registered). Registers
        /// <see cref="PackDirectory"/> and <see cref="PackBrainFactory"/> under their own types; the plain <see cref="IKaijuBrainFactory"/>
        /// slot stays with the scripted brain, so a host picks a brain by BrainId.
        /// </summary>
        public static PackBrainFactory Install(IServiceRegistry registry, IFeatureFlags flags, IKaijuBrainFactory inner, PackDirectory directory = null, PackConfig? config = null)
        {
            if (registry == null || flags == null || inner == null || !flags.IsOn(PackFlags.Pack)) return null;
            directory = directory ?? new PackDirectory();
            var factory = new PackBrainFactory(inner, directory, config ?? PackConfig.Default());
            registry.Register(directory);
            registry.Register(factory);
            return factory;
        }
    }
}
