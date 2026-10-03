# How to use RusK (setup manual)

[日本語](Manual.md) · [English](Manual.en.md) · [简体中文](Manual.zh.md)

RusK is a tool for adding mods to VED:Recure. With the **RusK Mod Manager** app you can install, update and remove everything in one place.

- Supported game version: **0.0.1878 (c9d3e1a)** (shown at the bottom left of the title screen)
- Requirements: Windows 10 / 11, VED:Recure on Steam

---

## 1. Install (first time)

1. Download **`RusK-Mod-Manager.exe`** from the [release page](https://github.com/DonutSuZu/RusK/releases/latest) (you can put it anywhere)
2. Double-click to start it
   - If "Windows protected your PC" appears, press **"More info" → "Run anyway"** (it appears because the app is not signed)
3. On the "Welcome to RusK" screen, press **"Install RusK"**
   - The game folder is found automatically through Steam. If not, use "Choose the game folder..." and pick the folder that contains `ved.exe`
   - The mod framework (BepInEx) is installed too if it is missing
4. Pick the mods you want in the list on the left and press **"Install"** (you can add or remove them any time)
5. Press **"Play"**

> **The first launch takes a while** (the screen may stay black for a few minutes). It is only preparing the mods, so please wait without closing it. From the second launch on it starts normally.

---

## 2. In the game

| Action | Keyboard | Gamepad |
|---|---|---|
| Open / close the RusK menu | **Insert** | **LS + RS** (press both sticks) |
| Move the selection | ↑ ↓ | D-pad ↑ ↓ |
| Open / turn a feature ON or OFF | → / Enter | A |
| Back | ← / Backspace | B (closes the menu in the leftmost column) |
| Change a setting's value | ← → in the settings column | D-pad ← → |

- The menu appears at the top left in 3 columns: "category (Party, Visual, Music, ...) → feature (module) → settings"
- Turn a feature ON to use it. Settings are saved automatically
- To use the mouse, switch **Visual > Menu > GUI** to ClickGUI
- The menu key can be changed in **Visual > Menu > MenuKey** (gamepad: **PadMenu**)

---

## 3. Mods

| Mod | What it does | Files go in (under `RusK\` in the game folder) |
|---|---|---|
| RusK UI | Button hints, attack warnings, ... | — |
| EXTREME Difficulty | EXTREME difficulty | — |
| Party | Fight as 3 and switch | — |
| Chain Attack | Chain attacks (needs Party) | — |
| Party Op.2 | Endfield-style battle (needs Party) | — |
| Party Formation | ZZZ-style formation screen (needs Party) | — |
| Camera View | Switch the camera view | — |
| Music Manager | Your own battle music | `music\` |
| Voice Replacer | Replace voices and sound effects | `voices\` |
| Effect Tuner | Effect colors, size and replacement | `effects\` |
| Custom VRM Loader | Character looks as VRM / PMX (MMD) | `models\` |
| Custom Item Model | Weapons and accessories as glb / PMX | `props\` |
| Custom Motion | Your own motions (Blender, VRMA) | `motions\` |
| Custom Character | New characters (character Mod Packs) | `characters\` |

---

## 4. Where to put files

### Open the game folder

The easiest way is **"Open the game folder"** in the Mod Manager.
To find it yourself: right-click the game in your Steam library → "Manage" → "Browse local files".
The folder is named **`Ved疗愈所`** (a Chinese name). Example: `C:\Program Files (x86)\Steam\steamapps\common\Ved疗愈所`

### Folder layout

Installing RusK creates a **`RusK`** folder in the game folder. **Put all your files inside this `RusK` folder.**

```
Ved疗愈所\                 ← the game folder (contains ved.exe)
  ved.exe
  BepInEx\                 ← the mod framework (no need to touch)
  RusK\
    mods\                  ← the mods themselves (.dll). The Mod Manager handles this
    characters\            ← character Mod Packs
    models\                ← VRM / PMX models
    props\                 ← weapon and accessory models
    motions\               ← your own motions
    voices\                ← voices and sound effects
    music\                 ← battle music
    effects\               ← your own effects
    configs\  data\        ← settings (created automatically, no need to touch)
```

If a folder is missing, you can create it yourself (it is also created once the mod runs). **"OpenFolder"** in each mod's menu or **"Open folder"** in its window opens the right folder.

### Per mod

| Mod | Where | What | Example |
|---|---|---|---|
| Custom Character | `RusK\characters\<name>\` | the whole Pack folder (with `character.json` inside) | `RusK\characters\MyChara\character.json` |
| Custom VRM Loader | `RusK\models\` | `.vrm`, or `.pmx` **together with its texture folder** | `RusK\models\MyModel\model.pmx` and `RusK\models\MyModel\Textures\` |
| Custom Item Model | `RusK\props\` | `.glb`, or `.pmx` with its texture folder | `RusK\props\sword.glb` |
| Custom Motion | `RusK\motions\` | `.glb` / `.vrma` | `RusK\motions\Wave.glb` |
| Voice Replacer | `RusK\voices\` | `.ogg` / `.wav` / `.mp3` (**file name = the game's sound name**) | `RusK\voices\LightAttackVoice_1006_1_JP.ogg` |
| Music Manager | `RusK\music\` (boss battles: `RusK\music\boss\`) | `.ogg` / `.wav` / `.mp3` | `RusK\music\my_song.ogg` |
| Effect Tuner | `RusK\effects\` | `.bundle` (made with Unity) | `RusK\effects\my_effect.bundle` |

After placing files:
- **VRM, PMX, weapons**: in each mod's window, choose which character or equipment to use them for (character Mod Packs attach automatically)
- **Voices**: turn on **LogPlayed** in Voice Replacer and play; the game's sound names are written to `RusK\voices\_played.txt`
- **If nothing shows up**: check that the files are **inside** `RusK\` (not directly in the game folder) and that the file extension is right

---

## 5. Install a character Mod Pack

A character Mod Pack is **just one folder**.

1. In the Mod Manager, install **Custom Character** (required). If the Pack has looks, a weapon, motions or voices, also install **Custom VRM Loader, Custom Item Model, Custom Motion, Voice Replacer**
2. Put the Pack folder (with `character.json` inside) into `RusK\characters\`
   ```
   RusK\characters\<Pack name>\character.json
   ```
3. Start the game; the new character appears on the character screen and the sortie selection (unlocked from the start)

- New characters are not stored in your save. Removing the Pack or the mods does not break your save
- How to make one: [docs/ModPack.md](ModPack.md) (Japanese)
- To make your own, **RusK Pack Creator** (`RusK-Pack-Creator.exe`, on the [release page](https://github.com/DonutSuZu/RusK/releases)) is the easiest way: fill in the fields and press "Put into the game"

---

## 6. Update / remove

- **Update**: the Mod Manager checks for updates when it starts. **"Update all"** updates everything (close the game first)
- **Stop a mod**: select it in the list and press **"Disable"** (keeps it) or **"Remove"**
- **Remove everything**: **"Uninstall RusK..."**. VRM, PMX, audio and other files are kept. You are asked whether to delete the settings too
- If the game still behaves oddly, Steam's "Verify integrity of game files" restores it

---

## 7. Troubleshooting

| Problem | What to check |
|---|---|
| The menu does not open | Is "RusK v…" shown at the bottom right of the title screen? If not, run "Install RusK" again in the Mod Manager |
| The game does not start / crashes | Is the game version (bottom left of the title screen) **0.0.1878**? After a game update, RusK may not work until RusK is updated |
| Loading never finishes | The first launch takes a few minutes. If it still does not finish, look at `BepInEx\LogOutput.log` in the game folder |
| "Couldn't get the latest information" in the Mod Manager | Your internet connection. If GitHub is busy, wait a bit and press "Check for updates again" |
| The new character does not appear | Is `RusK\characters\<name>\character.json` in the right place? Is Custom Character enabled? |
| Blocked by antivirus | The mod framework (BepInEx) is sometimes falsely detected. Add the game folder to the exclusions |

**When reporting a problem**, please include:
- **`BepInEx\LogOutput.log`** from the game folder (after closing the game)
- What you did and what happened

Report here: [GitHub Issues](https://github.com/DonutSuZu/RusK/issues)
