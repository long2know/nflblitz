using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public readonly struct SourceActorContactView
    {
        public bool IsSpecified { get; }
        public int? HitPartnerPlayerId { get; }
        public int PuppetPlayerId { get; }
        public long PuppetReleaseSourceTick { get; }
        public int AttackMode { get; }
        public int AttackSourceTicks { get; }
        public int BlockCount { get; }

        public SourceActorContactView(int? hitPartnerPlayerId, int puppetPlayerId, long puppetReleaseSourceTick,
            int attackMode, int attackSourceTicks, int blockCount)
        {
            if (hitPartnerPlayerId.HasValue) SourceContractGuard.Player(hitPartnerPlayerId.Value, nameof(hitPartnerPlayerId));
            SourceContractGuard.Player(puppetPlayerId, nameof(puppetPlayerId));
            if (puppetReleaseSourceTick < 0) throw new ArgumentOutOfRangeException(nameof(puppetReleaseSourceTick));
            SourceContractGuard.Range(attackMode, 0, 8, nameof(attackMode));
            SourceContractGuard.Range(attackSourceTicks, 0, int.MaxValue, nameof(attackSourceTicks));
            SourceContractGuard.Range(blockCount, 0, int.MaxValue, nameof(blockCount));
            HitPartnerPlayerId = hitPartnerPlayerId; PuppetPlayerId = puppetPlayerId;
            PuppetReleaseSourceTick = puppetReleaseSourceTick; AttackMode = attackMode;
            AttackSourceTicks = attackSourceTicks; BlockCount = blockCount; IsSpecified = true;
        }
    }

    public enum SourceContactEventKind { Collision, AnimationChanged, PuppetLinked, PuppetReleased, HelmetPopped, ProgressStopped }

    public readonly struct SourceContactEvent
    {
        public long SourceTick { get; }
        public int ProcessOrder { get; }
        public SourceContactEventKind Kind { get; }
        public int ActorPlayerId { get; }
        public int OtherPlayerId { get; }
        public int AttackMode { get; }
        public int Direction4 { get; }
        public int Direction8 { get; }
        public string ScriptSymbol { get; }
        public SourceVector3 WorldPosition { get; }

        public SourceContactEvent(long sourceTick, int processOrder, SourceContactEventKind kind,
            int actorPlayerId, int otherPlayerId, SourceVector3 worldPosition, int attackMode = 0,
            int direction4 = 0, int direction8 = 0, string scriptSymbol = null)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Range(processOrder, 0, int.MaxValue, nameof(processOrder));
            SourceContractGuard.Range((int)kind, 0, 5, nameof(kind));
            SourceContractGuard.Player(actorPlayerId, nameof(actorPlayerId));
            SourceContractGuard.Player(otherPlayerId, nameof(otherPlayerId));
            SourceContractGuard.Range(attackMode, 0, 8, nameof(attackMode));
            SourceContractGuard.Range(direction4, 0, 3, nameof(direction4));
            SourceContractGuard.Range(direction8, 0, 7, nameof(direction8));
            SourceContractGuard.Finite(worldPosition.X, nameof(worldPosition));
            SourceContractGuard.Finite(worldPosition.Y, nameof(worldPosition));
            SourceContractGuard.Finite(worldPosition.Z, nameof(worldPosition));
            if (scriptSymbol != null) SourcePresentationGuard.Identity(scriptSymbol, nameof(scriptSymbol));
            SourceTick = sourceTick; ProcessOrder = processOrder; Kind = kind;
            ActorPlayerId = actorPlayerId; OtherPlayerId = otherPlayerId; AttackMode = attackMode;
            Direction4 = direction4; Direction8 = direction8; ScriptSymbol = scriptSymbol; WorldPosition = worldPosition;
        }
    }

    // A source call that has NOT been selected. It is not a resolved/muted SoundVM dispatch.
    public sealed class SourceContactSoundCue : SourcePresentationCue
    {
        public string FunctionSymbol { get; }
        public ReadOnlyCollection<string> OriginalArguments { get; }
        public bool SelectionResolved { get { return false; } }

        public SourceContactSoundCue(SourcePresentationContext context, string functionSymbol,
            IReadOnlyList<string> originalArguments, int carrierPlayerId)
            : base(context, SourcePresentationCueKind.SoundCallback, carrierPlayerId)
        {
            SourcePresentationGuard.Identity(functionSymbol, nameof(functionSymbol));
            if (originalArguments == null) throw new ArgumentNullException(nameof(originalArguments));
            var copy = new string[originalArguments.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                SourcePresentationGuard.Identity(originalArguments[i], nameof(originalArguments));
                copy[i] = originalArguments[i];
            }
            FunctionSymbol = functionSymbol;
            OriginalArguments = Array.AsReadOnly(copy);
        }
    }
}
