# MultiplayerRestart - Slay the Spire 2 Co-op Restart Mod

[中文文档](README_zh.md)

Quickly restart a co-op run in multiplayer without manually returning to the main menu.

## Features

- **Hotkey Restart**: Press `Ctrl+Shift+R` to trigger a restart
- **Pause Menu Button**: Automatically injects a "Restart Run" button into the pause menu
- **Host-Only Control**: Only the host can initiate a restart
- **Confirmation Dialog**: Shows a confirmation popup before restarting to prevent accidental triggers
- **Multi-Strategy Execution**: Automatically selects the best method to restart (Game API -> Return to Main Menu -> Force Disconnect)

## Compatibility

- **Game Version**: v0.103.2
- **Dependencies**: None (BaseLib not required)
- **Multiplayer**: All players in the lobby must have this mod installed

## Requirements

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Slay the Spire 2 installed

## Build

### 1. Configure Game Path

The mod automatically detects the game install path from the Steam registry. If detection fails, edit `Directory.Build.props`:

```xml
<Sts2Path>C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2</Sts2Path>
```

Or manually copy the following DLLs to the `references/` directory:
- `sts2.dll`
- `0Harmony.dll`
- `GodotSharp.dll`

These files are located in the `data_sts2_windows_x86_64/` folder under the game directory.

### 2. Compile

```bash
dotnet build -c Release
```

### 3. Install

After building, files are automatically copied to the game's `mods/MultiplayerRestart/` directory.

If auto-copy fails, manually copy these files to `<game_dir>/mods/MultiplayerRestart/`:
- `bin/Release/MultiplayerRestart.dll`
- `MultiplayerRestart.json`

### 4. Enable the Mod

1. Launch the game
2. A mod activation prompt will appear on first launch
3. Confirm and restart the game

## Usage

### Hotkey
Press `Ctrl+Shift+R` in-game to trigger a restart.

### Pause Menu
If the mod successfully detects the pause menu, a "Restart Run" button will be added automatically.

### Restart Flow
1. The host presses the hotkey or clicks the button
2. A confirmation dialog appears
3. After confirmation, the current run is abandoned
4. All players return to the main menu
5. The host can immediately create a new multiplayer lobby

## Multiplayer Testing

Use launch arguments for local multiplayer testing:

**Host:**
```
--force-steam=off --fastmp host --clientId 1
```

**Client:**
```
--force-steam=off --fastmp join --clientId 1000
```

## Troubleshooting

### Pause Menu Button Not Appearing
This is expected. The mod uses runtime discovery to find the pause menu class. If the class name in your game version isn't in the candidate list, the button won't appear. Use the `Ctrl+Shift+R` hotkey instead.

### Not Returning to Main Menu After Restart
The mod uses reflection to find the game's abandon/return methods. If they can't be found, it falls back to force-disconnecting. Check the game log for messages with the `[MultiplayerRestart]` prefix for details.

### Custom Game API Paths
If you've decompiled `sts2.dll` and found the correct class/method names, update the candidate lists in `src/Utils/GameApi.cs`.

## Project Structure

```
src/
├── ModEntry.cs              # Mod entry point
├── RestartManager.cs        # Restart flow manager
├── InputHandler.cs          # Keyboard hotkey handler
├── Patches/
│   ├── PauseMenuPatch.cs    # Pause menu injection
│   └── RunLifecyclePatch.cs # Run lifecycle tracking
└── Utils/
    └── GameApi.cs           # Game API reflection discovery
MultiplayerRestart.csproj    # Project configuration
MultiplayerRestart.json      # Mod manifest
Directory.Build.props        # Local build configuration
Sts2PathDiscovery.props      # STS2 path auto-detection
```

## License

MIT License
