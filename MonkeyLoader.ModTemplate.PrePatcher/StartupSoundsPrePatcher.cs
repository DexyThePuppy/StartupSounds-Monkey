using ManagedBass;
using MonkeyLoader;
using MonkeyLoader.Patching;
using MonkeyLoader.Resonite.Features.FrooxEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using FrooxEngine;
using PlaybackState = ManagedBass.PlaybackState;

namespace StartupSounds
{
    /// <summary>
    /// Pre-patcher that handles startup sound effects for Resonite
    /// </summary>
    internal sealed class StartupSoundsPrePatcher : EarlyMonkey<StartupSoundsPrePatcher>
    {
        //====== Constants and Static Fields ======//

        private const int FADE_TIME = 1000;
        private const int PHASE_SOUND_COOLDOWN = 300;
        private const int POLLING_INTERVAL = 20;

        private static readonly string baseSoundsPath = Path.Combine("MonkeyLoader", "Mods", "StartupSounds", "sounds");
        private static readonly string[] soundFolders = { "done", "launch", "loading", "phase" };
        private static readonly string[] supportedExtensions = { "*.wav", "*.flac", "*.ogg", "*.mp3" };
        private static readonly Random random = new Random();

        //====== Properties ======//

        public override string Id => "StartupSoundsPrePatcher";
        public override string Name => "StartupSounds";

        //====== State Fields ======//

        private static int currentStream;
        private static int loadingStream;
        private static bool bassInitialized;
        private static DateTime lastPhaseSoundTime = DateTime.MinValue;
        private volatile bool shouldContinueMonitoring = true;

        private string lastPhase = string.Empty;
        private int lastFixedPhaseIndex = -1;

        //====== Patch Configuration ======//

        protected override IEnumerable<IFeaturePatch> GetFeaturePatches()
            => new[] { new FeaturePatch<MonkeyLoader.Resonite.Features.FrooxEngine.FrooxEngine>(PatchCompatibility.HookOnly) };

        protected override IEnumerable<PrePatchTarget> GetPrePatchTargets()
            => new[] { new PrePatchTarget(Feature<MonkeyLoader.Resonite.Features.FrooxEngine.FrooxEngine>.Assembly, "FrooxEngine.Engine") };

        protected override bool Patch(PatchJob patchJob)
        {
            try
            {
                if (!InitializeBass()) return false;
                CreateSoundFolders();
                PlayLaunchSound();
                SetupEngineReadyHandler();

                patchJob.Changes = true;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(() => $"Error in StartupSounds patch: {e}");
                return false;
            }
        }

        //====== Initialization ======//

        private bool InitializeBass()
        {
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default))
            {
                Logger.Error(() => $"BASS Init failed: {Bass.LastError}");
                return false;
            }
            bassInitialized = true;
            return true;
        }

        private static void CreateSoundFolders()
        {
            Directory.CreateDirectory(baseSoundsPath);
            foreach (var folder in soundFolders)
            {
                Directory.CreateDirectory(Path.Combine(baseSoundsPath, folder));
            }
        }

        private void SetupEngineReadyHandler()
        {
            _ = Task.Run(async () =>
            {
                while (Engine.Current == null) await Task.Delay(POLLING_INTERVAL);
                var monitorTask = MonitorEnginePhases();
                Engine.Current.OnReady += async () => 
                {
                    await CrossfadeToNewSound("done");
                    // Signal to stop monitoring
                    shouldContinueMonitoring = false;
                };
            });
        }

        //====== Phase Monitoring ======//

        private async Task MonitorEnginePhases()
        {
            var progressField = typeof(Engine).GetField("<InitProgress>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (progressField == null) return;

            while (shouldContinueMonitoring)
            {
                if (Engine.Current != null)
                {
                    var currentProgress = progressField.GetValue(Engine.Current) as IEngineInitProgress;
                    var currentFixedPhaseIndex = currentProgress?.FixedPhaseIndex ?? -1;

                    // Only check for index changes and first valid index
                    if (currentFixedPhaseIndex != lastFixedPhaseIndex && 
                        currentFixedPhaseIndex >= 0 && 
                        currentFixedPhaseIndex <= 40)
                    {
                        var timeSinceLastSound = (DateTime.Now - lastPhaseSoundTime).TotalMilliseconds;
                        if (timeSinceLastSound >= PHASE_SOUND_COOLDOWN)
                        {
                            try
                            {
                                string soundFile = GetRandomAudioFile(Path.Combine(baseSoundsPath, "phase"));
                                _ = Task.Run(() => PlaySound(soundFile));
                                lastPhaseSoundTime = DateTime.Now;
                            }
                            catch (FileNotFoundException) { }
                        }

                        // Only update index
                        lastFixedPhaseIndex = currentFixedPhaseIndex;
                    }
                }

                await Task.Delay(POLLING_INTERVAL);
            }
        }

        //====== Sound System ======//

        private static int PlaySound(string soundPath, float initialVolume = 1.0f)
        {
            if (!bassInitialized) return 0;

            int stream = Bass.CreateStream(soundPath);
            if (stream == 0) return 0;

            Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, initialVolume);
            Bass.ChannelPlay(stream, true);
            currentStream = stream;

            return stream;
        }

        private void PlayLaunchSound()
        {
            try
            {
                string soundFile = GetRandomAudioFile(Path.Combine(baseSoundsPath, "launch"));
                PlaySound(soundFile);
                int launchStream = currentStream;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CrossfadeToNewSound("loading");
                        await WaitForStreamToFinish(launchStream);
                        if (loadingStream != 0)
                        {
                            Bass.ChannelSetAttribute(loadingStream, ChannelAttribute.Volume, 1.0f);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(() => $"Error transitioning from launch to loading sound: {ex}");
                    }
                });
            }
            catch (FileNotFoundException)
            {
                _ = CrossfadeToNewSound("loading");
            }
            catch (Exception ex)
            {
                Logger.Error(() => $"Error playing launch sound: {ex}");
                _ = CrossfadeToNewSound("loading");
            }
        }

        private async Task CrossfadeToNewSound(string folder)
        {
            try
            {
                string soundFile = GetRandomAudioFile(Path.Combine(baseSoundsPath, folder));
                int oldStream = currentStream;

                PlaySound(soundFile, oldStream != 0 ? 0.0f : 1.0f);
                int newStream = currentStream;

                if (folder == "loading")
                {
                    loadingStream = newStream;
                }

                if (oldStream != 0)
                {
                    await PerformCrossfade(oldStream, newStream);
                }

                if (folder == "done")
                {
                    await FadeOutLoadingSound();
                }
            }
            catch (FileNotFoundException) { }
        }

        private static async Task PerformCrossfade(int oldStream, int newStream)
        {
            Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, 0f, FADE_TIME);
            Bass.ChannelSlideAttribute(newStream, ChannelAttribute.Volume, 1f, FADE_TIME);
            await Task.Delay(FADE_TIME);
            Bass.ChannelStop(oldStream);
            Bass.StreamFree(oldStream);
        }

        private async Task FadeOutLoadingSound()
        {
            if (loadingStream != 0 && Bass.ChannelIsActive(loadingStream) == PlaybackState.Playing)
            {
                Bass.ChannelSlideAttribute(loadingStream, ChannelAttribute.Volume, 0f, FADE_TIME);
                await Task.Delay(FADE_TIME);
                Bass.ChannelStop(loadingStream);
                Bass.StreamFree(loadingStream);
                loadingStream = 0;
            }
        }

        private static async Task WaitForStreamToFinish(int stream)
        {
            while (Bass.ChannelIsActive(stream) == PlaybackState.Playing)
            {
                await Task.Delay(POLLING_INTERVAL);
            }
        }

        private static string GetRandomAudioFile(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                throw new ArgumentException("Directory path cannot be empty!", nameof(dir));
            }

            var files = supportedExtensions
                .SelectMany(ext => Directory.GetFiles(dir, ext, SearchOption.AllDirectories))
                .ToArray();

            if (files.Length == 0)
            {
                throw new FileNotFoundException($"No audio files were found in the directory: {dir}");
            }

            return files[random.Next(files.Length)];
        }
    }
}