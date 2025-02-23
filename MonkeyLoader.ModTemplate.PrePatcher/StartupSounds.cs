using MonkeyLoader;
using MonkeyLoader.Patching;
using MonkeyLoader.Resonite.Features.FrooxEngine;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StartupSounds
{
    internal sealed class StartupSounds : EarlyMonkey<StartupSounds>
    {
        public override string Id => "StartupSounds";
        public override string Name => "StartupSounds";

        protected override IEnumerable<IFeaturePatch> GetFeaturePatches()
        {
            yield return new FeaturePatch<MonkeyLoader.Resonite.Features.FrooxEngine.FrooxEngine>(PatchCompatibility.HookOnly);
        }

        protected override IEnumerable<PrePatchTarget> GetPrePatchTargets()
        {
            yield return new PrePatchTarget(Feature<MonkeyLoader.Resonite.Features.FrooxEngine.FrooxEngine>.Assembly, "FrooxEngine.Engine");
        }

        protected override bool Patch(PatchJob patchJob)
        {
            var engine = patchJob.Types.First();
            var engineCCtor = engine.GetStaticConstructor();

            var processor = engineCCtor.Body.GetILProcessor();
            processor.InsertBefore(engineCCtor.Body.Instructions.First(), processor.Create(OpCodes.Call, typeof(StartupSounds).GetMethod(nameof(PlayStartupSound))));

            patchJob.Changes = true;
            return true;
        }

        public static void PlayStartupSound()
            => Logger.Info(() => "Hello from pre-patched-in StartupSounds!");
    }
}