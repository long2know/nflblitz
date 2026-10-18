using System;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static partial class Program
{
    private static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }

    private static void InputChecks()
    {
        GameSimulation game = Create();
        object actualLibrary = Field(game, "sourceAnimations");
        object actualGeometry = Field(game, "sourceGeometry");
        Check(ReferenceEquals(real.Animations, actualLibrary)
            && ReferenceEquals(actualLibrary, Field(actualGeometry, "library")),
            "actual production factory passes ONE real272 library instance to Core and geometry");
        Check(actualGeometry is ISourceActorCameraGeometry && real.ReferencePivots.Length == 15,
            "actual production factory uses imported15-bone source camera geometry, never a default rig");
        var actualPivots = (SourceVector3[])Field(actualGeometry, "referencePivots");
        for (int i = 0; i < 15; i++)
            Check(Bits(actualPivots[i].X) == Bits(real.ReferencePivots[i].X)
                && Bits(actualPivots[i].Y) == Bits(real.ReferencePivots[i].Y)
                && Bits(actualPivots[i].Z) == Bits(real.ReferencePivots[i].Z),
                "production Contact272 geometry retains actual imported source pivot " + i);
        Check(real.SoundFunctions.PayloadSha256 == GameSimulation.SourceSoundVmDataSha256
            && real.SoundFunctions.VmBindings.Count == 6 && game.SourceTick == 0,
            "production Contact272 factory returns exact immutable SAFN-bound kernel before any source wake");
        game.Step(default(PlayerCommand));
        game.Step(default(PlayerCommand));
        Check(game.SourceTick == 1, "real production272 plus mandatory SAFN executes its first actual source wake");
        bool rejected = false;
        try { SourceTeamResourceProfile.Contact272.RequireLibrary(legacy157.Animations); }
        catch (SourceInterfaceException) { rejected = true; }
        Check(rejected, "actual production Contact272 rejects real157 rather than aliasing it by count/name");
        FullKernelInputs selected156 = FullKernelInputs.Load(inputManifest, movementLibrary: true,
            selectedMovementLibrary: true);
        Check(!selected156.IsContactProfile && selected156.Animations.SequenceNames.Count == 156
            && selected156.Animations.InventorySha256 == FullKernelInputs.MovementSelectedSha256,
            "explicit selected156 negative control keeps its old distinct identity in the new fixture");
        FullKernelInputs canonical117 = FullKernelInputs.Load(inputManifest, legacyAnimationLibrary: true);
        Check(!canonical117.IsContactProfile && canonical117.Animations.SequenceNames.Count == 117
            && canonical117.Animations.InventorySha256 == FullKernelInputs.AnimationSha256,
            "explicit canonical117 control remains actual117 rather than a272 alias");
    }
}
