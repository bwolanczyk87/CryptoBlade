using CryptoBlade.Strategies.Sigma.Modes; // ModeDecision
using CryptoBlade.Strategies.Sigma.Regimes;
using System;
using System.Threading;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// Wspólny kontrakt dla kontrolerów reżimu (MM/MR/BO).
    /// </summary>
    public interface IModeController
    {
        /// <summary>
        /// Zwraca decyzję wejścia/zarządzania pozycją dla danego reżimu.
        /// </summary>
        ModeDecision Evaluate(FeatureSnapshot f, RegimeState state, DateTime nowUtc, CancellationToken cancel);
    }
}