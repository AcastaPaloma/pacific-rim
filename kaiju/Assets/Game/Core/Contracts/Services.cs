using System;

namespace Game.Core
{
    /// <summary>
    /// Explicit service registry (contract v1): register an instance under a contract type, resolve by that type.
    /// No reflection, no auto-wiring, no static instance. Disposing the registry disposes IDisposable services
    /// once each, in reverse registration order.
    /// </summary>
    public interface IServiceRegistry
    {
        /// <exception cref="DuplicateServiceException">The contract already has an implementation.</exception>
        void Register<TContract>(TContract implementation) where TContract : class;
        /// <exception cref="ServiceNotRegisteredException">Nothing is registered for the contract.</exception>
        TContract Resolve<TContract>() where TContract : class;
        bool TryResolve<TContract>(out TContract implementation) where TContract : class;
        bool IsRegistered<TContract>() where TContract : class;
    }

    public sealed class ServiceNotRegisteredException : Exception
    {
        public ServiceNotRegisteredException(Type contract)
            : base("No service registered for contract " + contract.FullName + ".") { Contract = contract; }
        public Type Contract { get; }
    }

    public sealed class DuplicateServiceException : Exception
    {
        public DuplicateServiceException(Type contract, Type existing, Type attempted)
            : base("Contract " + contract.FullName + " is already registered to " + existing.FullName +
                   "; refusing second registration of " + attempted.FullName + ".")
        { Contract = contract; }
        public Type Contract { get; }
    }
}
