# StartupSounds for Resonite

A MonkeyLoader mod that plays custom sounds when Resonite starts up! 🎵

## Features
- Plays custom sounds during different game phases
- Supports multiple audio formats:
  - WAV
  - FLAC
  - OGG
  - MP3

## Installation
1. Install [MonkeyLoader](https://github.com/MonkeyModdingTroop/MonkeyLoader)
2. Place the `StartupSounds.nupkg` in your `Resonite/MonkeyLoader/Mods` folder
3. The mod will create these folders automatically:
   ```
   Resonite/MonkeyLoader/Mods/StartupSounds/sounds/
   ├── done/         # Sounds for when loading is complete
   ├── launch/       # Sounds played at game launch
   ├── loading/      # Sounds during loading
   └── phase/        # Sounds for phase transitions
   ```
4. Add your audio files to the appropriate folders

## Usage
Place your sound files in the appropriate folders:
- `launch/` - Sounds that play when the game starts
- `loading/` - Sounds during the loading process
- `phase/` - Sounds for different loading phases
- `done/` - Sounds when loading is complete

The mod will randomly select and play sounds from these folders at the appropriate times!

## Credits
- Original mod by dfgHiatus
- Ported to MonkeyLoader by MonkeModding
- Uses [ManagedBass](https://github.com/ManagedBass/ManagedBass) for audio playback 