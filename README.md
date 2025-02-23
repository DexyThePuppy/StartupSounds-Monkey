# StartupSounds for Resonite 🎵

A MonkeyLoader mod that enhances your Resonite startup experience with custom sounds! Plays different sounds during launch, loading, and startup phases.

## Features

- **Launch Sound**: Plays when the game first starts
- **Loading Sound**: Smooth crossfade from launch to loading sound
- **Phase Sounds**: Random sounds during engine initialization phases (first 40 phases)
- **Done Sound**: Plays when loading is complete
- **Smart Sound Management**:
  - Smooth crossfading between sounds
  - Cooldown system to prevent sound spam
  - Automatic cleanup of sound resources

## Supported Audio Formats
- WAV
- FLAC
- OGG
- MP3

## Installation

1. Install [MonkeyLoader](https://github.com/MonkeyModdingTroop/MonkeyLoader)
2. Place `StartupSounds.nupkg` in your `Resonite/MonkeyLoader/Mods` folder
3. The mod will create the following sound folders, where you can add your audio files:

```
Resonite/MonkeyLoader/Mods/StartupSounds/
├── launch/   # First sound when starting Resonite
├── loading/  # Background music during the loading process
├── phase/    # Short sounds during initialization (for every phase until 40)
└── done/     # Final sound when Resonite is ready
```

## Credits
- [Original mod](https://github.com/dfgHiatus/StartupSounds) by dfgHiatus
- Ported to MonkeyLoader by Dexy
- Uses [ManagedBass](https://github.com/ManagedBass/ManagedBass) for audio playback and crossfading