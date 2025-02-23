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
    internal sealed class StartupSoundsPrePatcher : EarlyMonkey<StartupSoundsPrePatcher>
    {
        public override string Id => "StartupSoundsPrePatcher";
        public override string Name => "StartupSounds";
        private static int currentStream;
        private static int loadingStream;
        private static bool bassInitialized;
        private static readonly string baseSoundsPath = Path.Combine("Mods", "StartupSounds", "sounds");
        private static readonly string[] soundFolders = new[] { "done", "launch", "loading", "phase" };
        private const int FADE_TIME = 2000; // 2 seconds fade
        private const int PHASE_SOUND_COOLDOWN = 200; // Minimum time between phase sounds in ms
        private string lastPhase = "";
        private string lastSubphase = "";
        private static DateTime lastPhaseSoundTime = DateTime.MinValue;

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
            try
            {
                Logger.Info(() => "Initializing StartupSounds...");
                // Initialize BASS here, before the engine even starts
                if (!Bass.Init(-1, 44100, DeviceInitFlags.Default))
                {
                    Logger.Error(() => $"BASS Init failed: {Bass.LastError}");
                    return false;
                }
                bassInitialized = true;
                Logger.Info(() => "BASS initialized successfully");

                // Create base directory and all subfolders
                Directory.CreateDirectory(baseSoundsPath);
                foreach (var folder in soundFolders)
                {
                    string path = Path.Combine(baseSoundsPath, folder);
                    Directory.CreateDirectory(path);
                    Logger.Info(() => $"Created sound folder: {path}");
                }

                // Play launch sound first
                Logger.Info(() => "Playing launch sound...");
                PlayLaunchSound();

                // Start a task to wait for the engine to be created and hook into its ready event
                Logger.Info(() => "Setting up engine ready handler...");
                _ = Task.Run(async () =>
                {
                    Logger.Info(() => "Waiting for Engine.Current...");
                    while (Engine.Current == null)
                    {
                        await Task.Delay(100);
                    }

                    // Start monitoring engine phases
                    _ = MonitorEnginePhases();

                    Logger.Info(() => "Engine.Current available, hooking OnReady event");
                    Engine.Current.OnReady += () =>
                    {
                        Logger.Info(() => "Engine ready event triggered!");
                        _ = CrossfadeToNewSound("done");
                    };
                });

                patchJob.Changes = true;
                Logger.Info(() => "StartupSounds initialization complete!");
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(() => $"Error in StartupSounds patch: {e}");
                return false;
            }
        }

        private async Task MonitorEnginePhases()
        {
            Logger.Info(() => "Starting phase monitoring...");
            
            // Give the engine a moment to start initializing
            await Task.Delay(500);
            
            var phaseField = typeof(Engine).GetField("<InitPhase>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            var subphaseField = typeof(Engine).GetField("<InitSubphase>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            var progressField = typeof(Engine).GetField("<InitProgress>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (phaseField == null || subphaseField == null || progressField == null)
            {
                Logger.Error(() => "Could not find one or more required fields!");
                return;
            }
            
            Logger.Info(() => $"Found fields: Phase={phaseField.Name}, Subphase={subphaseField.Name}, Progress={progressField.Name}");

            int lastFixedPhaseIndex = -1;

            while (!Engine.Current.IsReady)
            {
                try
                {
                    var currentPhase = (string)phaseField.GetValue(Engine.Current);
                    var currentSubphase = (string)subphaseField.GetValue(Engine.Current);
                    var currentProgress = progressField.GetValue(Engine.Current) as IEngineInitProgress;
                    
                    var currentFixedPhaseIndex = currentProgress?.FixedPhaseIndex ?? -1;
                    
                    Logger.Info(() => $"Current state - Phase: '{currentPhase ?? "null"}', Subphase: '{currentSubphase ?? "null"}', Progress: {currentProgress?.GetType().Name ?? "null"}, FixedPhaseIndex: {currentFixedPhaseIndex}");

                    // Check for phase changes or fixed phase index changes
                    if ((currentPhase != lastPhase || currentSubphase != lastSubphase || currentFixedPhaseIndex != lastFixedPhaseIndex) && 
                        (currentPhase != null || currentSubphase != null || currentFixedPhaseIndex >= 0))
                    {
                        Logger.Info(() => $"State changed - Phase: '{currentPhase ?? "null"}' (was: '{lastPhase ?? "null"}')");
                        Logger.Info(() => $"State changed - Subphase: '{currentSubphase ?? "null"}' (was: '{lastSubphase ?? "null"}')");
                        Logger.Info(() => $"State changed - FixedPhaseIndex: {currentFixedPhaseIndex} (was: {lastFixedPhaseIndex})");

                        // If phase changed or fixed phase index changed, play a phase sound
                        if ((currentPhase != lastPhase || currentFixedPhaseIndex != lastFixedPhaseIndex) && currentFixedPhaseIndex <= 40)
                        {
                            // Check if enough time has passed since the last phase sound
                            var timeSinceLastSound = DateTime.Now - lastPhaseSoundTime;
                            if (timeSinceLastSound.TotalMilliseconds < PHASE_SOUND_COOLDOWN)
                            {
                                Logger.Info(() => $"Skipping phase sound - cooldown active ({timeSinceLastSound.TotalMilliseconds:F0}ms < {PHASE_SOUND_COOLDOWN}ms)");
                            }
                            else
                            {
                                var phaseDescription = !string.IsNullOrEmpty(currentPhase) ? currentPhase : 
                                                     currentFixedPhaseIndex >= 0 ? $"Phase {currentFixedPhaseIndex}" : "Unknown Phase";
                                
                                Logger.Info(() => $"Phase changed to {phaseDescription}, playing phase sound...");
                                try
                                {
                                    string phaseFolder = Path.Combine(baseSoundsPath, "phase");
                                    string soundFile = GetRandomAudioFile(phaseFolder);
                                    _ = Task.Run(async () =>
                                    {
                                        PlaySound(soundFile);
                                        Logger.Info(() => $"Phase change sound playing on stream {currentStream}");
                                    });
                                    lastPhaseSoundTime = DateTime.Now;
                                }
                                catch (FileNotFoundException)
                                {
                                    Logger.Info(() => $"No sounds found in phase folder - add some .wav, .mp3, .ogg or .flac files to hear phase change sounds!");
                                }
                            }
                        }
                        else if (currentFixedPhaseIndex > 40)
                        {
                            Logger.Info(() => $"Skipping sound for high phase index: {currentFixedPhaseIndex}");
                        }

                        lastPhase = currentPhase;
                        lastSubphase = currentSubphase;
                        lastFixedPhaseIndex = currentFixedPhaseIndex;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(() => $"Error monitoring phases: {ex}");
                }

                await Task.Delay(100);
            }
            
            Logger.Info(() => "Engine is ready, stopping phase monitoring.");
        }

        private void PlayLaunchSound()
        {
            try
            {
                string launchFolder = Path.Combine(baseSoundsPath, "launch");
                Logger.Info(() => $"Looking for launch sounds in: {launchFolder}");
                string soundFile = GetRandomAudioFile(launchFolder);
                Logger.Info(() => $"Selected launch sound: {Path.GetFileName(soundFile)}");

                // Start playing launch sound immediately
                PlaySound(soundFile);
                int launchStream = currentStream;
                Logger.Info(() => $"Launch sound playing on stream {launchStream}");

                // Start loading sound after a short delay
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Start loading sound at 0 volume
                        Logger.Info(() => "Starting loading sound...");
                        await CrossfadeToNewSound("loading");

                        // Wait for launch sound to finish, then bring up loading sound volume
                        while (Bass.ChannelIsActive(launchStream) == PlaybackState.Playing)
                        {
                            await Task.Delay(100);
                        }
                        Logger.Info(() => "Launch sound finished playing");

                        // Ensure loading sound is at full volume
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
                Logger.Info(() => $"No sounds found in launch folder - add some .wav, .mp3, .ogg or .flac files to hear launch sounds!");
                // If no launch sound, try to play loading sound directly
                _ = CrossfadeToNewSound("loading");
            }
            catch (Exception ex)
            {
                Logger.Error(() => $"Error playing launch sound: {ex}");
                // If launch sound fails, try to play loading sound directly
                _ = CrossfadeToNewSound("loading");
            }
        }

        private async Task CrossfadeToNewSound(string folder)
        {
            try
            {
                string soundFolder = Path.Combine(baseSoundsPath, folder);
                Logger.Info(() => $"Looking for sounds in: {soundFolder}");
                string soundFile = GetRandomAudioFile(soundFolder);
                Logger.Info(() => $"Selected sound for {folder}: {Path.GetFileName(soundFile)}");

                // Store the old stream
                int oldStream = currentStream;

                // Start the new sound at 0 volume if there was a previous sound
                PlaySound(soundFile, oldStream != 0 ? 0.0f : 1.0f);
                int newStream = currentStream;

                // Keep track of loading stream separately
                if (folder == "loading")
                {
                    loadingStream = newStream;
                }

                Logger.Info(() => $"New sound playing on stream {newStream} for {folder}");

                // Only crossfade if there was a previous sound
                if (oldStream != 0)
                {
                    Logger.Info(() => $"Starting crossfade from stream {oldStream} to {newStream}");
                    Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, 0f, FADE_TIME);
                    Bass.ChannelSlideAttribute(newStream, ChannelAttribute.Volume, 1f, FADE_TIME);

                    // Wait for fade to complete then cleanup old stream
                    await Task.Delay(FADE_TIME);
                    Bass.ChannelStop(oldStream);
                    Bass.StreamFree(oldStream);
                    Logger.Info(() => $"Crossfade complete, cleaned up stream {oldStream}");
                }

                // If this is the "done" sound, fade out the loading sound if it's still playing
                if (folder == "done" && loadingStream != 0 && Bass.ChannelIsActive(loadingStream) == PlaybackState.Playing)
                {
                    Logger.Info(() => $"Engine ready, fading out loading sound on stream {loadingStream}");
                    Bass.ChannelSlideAttribute(loadingStream, ChannelAttribute.Volume, 0f, FADE_TIME);
                    await Task.Delay(FADE_TIME);
                    Bass.ChannelStop(loadingStream);
                    Bass.StreamFree(loadingStream);
                    loadingStream = 0;
                    Logger.Info(() => "Loading sound faded out and cleaned up");
                }
            }
            catch (FileNotFoundException)
            {
                Logger.Info(() => $"No sounds found in {folder} folder - add some .wav, .mp3, .ogg or .flac files to hear {folder} sounds!");
            }
        }

        private static int PlaySound(string soundPath, float initialVolume = 1.0f)
        {
            if (!bassInitialized)
            {
                Logger.Error(() => "Attempted to play sound but BASS is not initialized!");
                return 0;
            }

            Logger.Info(() => $"Creating stream for sound: {Path.GetFileName(soundPath)}");
            int stream = Bass.CreateStream(soundPath);
            if (stream == 0)
            {
                Logger.Error(() => $"Failed to create audio stream: {Bass.LastError}");
                return 0;
            }

            Logger.Info(() => $"Setting initial volume to {initialVolume} on stream {stream}");
            Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, initialVolume);
            Bass.ChannelPlay(stream, true);
            Logger.Info(() => $"Started playback on stream {stream}");

            currentStream = stream;
            Logger.Info(() => $"Set as current stream: {stream}");

            return stream;
        }

        private static string GetRandomAudioFile(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                throw new ArgumentException("Directory path cannot be empty!", nameof(dir));
            }

            Logger.Info(() => $"Scanning for audio files in: {dir}");
            string[] files = Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(dir, "*.flac", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(dir, "*.ogg", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(dir, "*.mp3", SearchOption.AllDirectories))
                .ToArray();

            Logger.Info(() => $"Found {files.Length} audio files");

            if (files.Length == 0)
            {
                throw new FileNotFoundException($"No audio files were found in the directory: {dir}");
            }

            string selected = files[new Random().Next(0, files.Length)];
            Logger.Info(() => $"Selected file: {Path.GetFileName(selected)}");
            return selected;
        }
    }
} 