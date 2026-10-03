# RusK 使用方法 (安装手册)

[日本語](Manual.md) · [English](Manual.en.md) · [简体中文](Manual.zh.md)

RusK 是为 VED:Recure 添加 Mod 的工具。使用 **RusK Mod Manager** 应用, 可以统一安装、更新和卸载。

- 支持的游戏版本: **0.0.1878 (c9d3e1a)** (显示在标题画面左下角)
- 需要: Windows 10 / 11、Steam 版 VED:Recure

---

## 1. 安装 (首次)

1. 从 [发布页面](https://github.com/DonutSuZu/RusK/releases/latest) 下载 **`RusK-Mod-Manager.exe`** (放在哪里都可以)
2. 双击启动
   - 如果出现"Windows 已保护你的电脑", 请点击 **"更多信息" → "仍要运行"** (因为应用没有签名才会出现)
3. 在"欢迎使用 RusK"画面点击 **"安装 RusK"**
   - 会通过 Steam 自动查找游戏文件夹。找不到时, 用"选择游戏文件夹..."选择有 `ved.exe` 的文件夹
   - 如果没有 Mod 运行框架 (BepInEx), 会一起安装
4. 在左侧列表中选择想用的 Mod, 点击 **"安装"** (之后可以随时添加或移除)
5. 点击 **"启动游戏"**

> **首次启动需要一些时间** (画面可能会黑屏几分钟)。这只是在准备 Mod, 请不要关闭, 耐心等待。第二次以后会正常启动。

---

## 2. 在游戏中使用

| 操作 | 键盘 | 手柄 |
|---|---|---|
| 打开 / 关闭 RusK 菜单 | **Insert** | **LS + RS** (同时按下两个摇杆) |
| 移动选择 | ↑ ↓ | 十字键 ↑ ↓ |
| 打开 / 开关功能 | → / Enter | A |
| 返回 | ← / Backspace | B (在最左列按下会关闭菜单) |
| 修改设置值 | 在设置列按 ← → | 十字键 ← → |

- 菜单显示在画面左上角, 分为 3 列: "分类 (Party、Visual、Music 等) → 功能 (模块) → 设置"
- 打开功能 (ON) 即可使用。设置会自动保存
- 想用鼠标操作时, 把 **Visual > Menu > GUI** 切换为 ClickGUI
- 打开菜单的按键可在 **Visual > Menu > MenuKey** (手柄为 **PadMenu**) 中修改

---

## 3. Mod 一览

| Mod | 功能 | 放置文件 (游戏文件夹的 `RusK\` 下) |
|---|---|---|
| RusK UI | 按键提示、攻击预兆显示等 | — |
| EXTREME Difficulty | EXTREME 难度 | — |
| Party | 3 人战斗、切换 | — |
| Chain Attack | 连携攻击 (需要 Party) | — |
| Party Op.2 | 终末地风格的战斗 (需要 Party) | — |
| Party Formation | 绝区零风格的编队画面 (需要 Party) | — |
| Camera View | 切换视角 | — |
| Music Manager | 把战斗 BGM 换成喜欢的曲子 | `music\` |
| Voice Replacer | 替换语音和音效 | `voices\` |
| Effect Tuner | 特效的颜色、大小、替换 | `effects\` |
| Custom VRM Loader | 角色外观换成 VRM / PMX (MMD) | `models\` |
| Custom Item Model | 武器、饰品换成 glb / PMX | `props\` |
| Custom Motion | 自制动作 (Blender、VRMA) | `motions\` |
| Custom Character | 新角色 (角色 Mod Pack) | `characters\` |

---

## 4. 文件放在哪里

### 打开游戏文件夹

最可靠的方法是点击 Mod Manager 的 **"打开游戏文件夹"**。
手动查找时: 在 Steam 库中右键点击游戏 → "管理" → "浏览本地文件"。
文件夹名为 **`Ved疗愈所`**。例: `C:\Program Files (x86)\Steam\steamapps\common\Ved疗愈所`

### 文件夹结构

安装 RusK 后, 游戏文件夹中会出现 **`RusK`** 文件夹。**所有素材都放在这个 `RusK` 里面。**

```
Ved疗愈所\                 ← 游戏文件夹 (有 ved.exe)
  ved.exe
  BepInEx\                 ← Mod 运行框架 (无需改动)
  RusK\
    mods\                  ← Mod 本体 (.dll)。由 Mod Manager 管理, 通常无需改动
    characters\            ← 角色 Mod Pack
    models\                ← VRM / PMX 模型
    props\                 ← 武器、饰品模型
    motions\               ← 自制动作
    voices\                ← 语音、音效
    music\                 ← 战斗 BGM
    effects\               ← 自制特效
    configs\  data\        ← 设置 (自动生成, 无需改动)
```

没有的文件夹可以自己创建 (运行一次 Mod 后也会自动生成)。各 Mod 菜单中的 **"OpenFolder"** 或窗口中的 **"打开文件夹"** 也能打开对应的文件夹。

### 各 Mod 的放法

| Mod | 位置 | 放置内容 | 例 |
|---|---|---|---|
| Custom Character | `RusK\characters\<名字>\` | 整个 Pack 文件夹 (里面有 `character.json`) | `RusK\characters\MyChara\character.json` |
| Custom VRM Loader | `RusK\models\` | `.vrm`, 或 `.pmx` **连同贴图文件夹** | `RusK\models\MyModel\model.pmx` 和 `RusK\models\MyModel\Textures\` |
| Custom Item Model | `RusK\props\` | `.glb`, 或 `.pmx` 连同贴图文件夹 | `RusK\props\sword.glb` |
| Custom Motion | `RusK\motions\` | `.glb` / `.vrma` | `RusK\motions\Wave.glb` |
| Voice Replacer | `RusK\voices\` | `.ogg` / `.wav` / `.mp3` (**文件名 = 游戏中的声音名**) | `RusK\voices\LightAttackVoice_1006_1_JP.ogg` |
| Music Manager | `RusK\music\` (Boss 战: `RusK\music\boss\`) | `.ogg` / `.wav` / `.mp3` | `RusK\music\my_song.ogg` |
| Effect Tuner | `RusK\effects\` | `.bundle` (用 Unity 制作) | `RusK\effects\my_effect.bundle` |

放好之后:
- **VRM、PMX、武器**: 在各 Mod 的窗口中选择用于哪个角色、哪件装备 (角色 Mod Pack 会自动附加, 无需选择)
- **语音**: 在 Voice Replacer 中打开 **LogPlayed** 后游玩, 游戏中的声音名会写入 `RusK\voices\_played.txt`
- **没有生效时**: 请确认文件放在 `RusK\` **里面** (而不是游戏文件夹的根目录), 以及扩展名是否正确

---

## 5. 安装角色 Mod Pack

角色 Mod Pack **只需放入一个文件夹**。

1. 在 Mod Manager 中安装 **Custom Character** (必需)。如果 Pack 包含外观、武器、动作或语音, 也请安装 **Custom VRM Loader、Custom Item Model、Custom Motion、Voice Replacer**
2. 把 Pack 文件夹 (里面有 `character.json`) 放入 `RusK\characters\`
   ```
   RusK\characters\<Pack 名>\character.json
   ```
3. 启动游戏后, 新角色会出现在角色画面和出击前的选择中 (一开始即可使用)

- 新角色不会保存在存档中。卸载 Pack 或 Mod 也不会损坏存档
- 制作方法: [docs/ModPack.md](ModPack.md) (日文)
- 自己制作时推荐使用 **RusK Pack Creator** (`RusK-Pack-Creator.exe`, 见 [发布页面](https://github.com/DonutSuZu/RusK/releases)): 填好各项后点击"放入游戏"即可生成 Pack

---

## 6. 更新 / 卸载

- **更新**: Mod Manager 启动时会自动检查更新。点击 **"全部更新"** 统一更新 (请先关闭游戏)
- **停用 Mod**: 在列表中选择 Mod, 点击 **"禁用"** (保留文件) 或 **"删除"**
- **全部卸载**: **"卸载 RusK..."**。VRM、PMX、音频等素材会保留。会询问是否同时删除设置
- 如果游戏仍然异常, 可以用 Steam 的"验证游戏文件的完整性"恢复

---

## 7. 遇到问题时

| 问题 | 确认事项 |
|---|---|
| 菜单打不开 | 标题画面右下角是否显示"RusK v…"。没有的话, 在 Mod Manager 中重新"安装 RusK" |
| 游戏无法启动 / 闪退 | 游戏版本 (标题画面左下角) 是否为 **0.0.1878** (游戏更新后, 在 RusK 更新前可能无法运行) |
| 读取一直不结束 | 首次启动需要几分钟。如果仍然不结束, 请查看游戏文件夹中的 `BepInEx\LogOutput.log` |
| Mod Manager 显示"无法获取最新信息" | 检查网络连接。GitHub 繁忙时, 稍等后点击"重新获取最新信息" |
| 新角色没有出现 | `RusK\characters\<名字>\character.json` 的位置是否正确。Custom Character 是否已启用 |
| 被杀毒软件拦截 | Mod 运行框架 (BepInEx) 有时会被误报。请把游戏文件夹加入排除列表 |

**报告问题时**, 请附上:
- 游戏文件夹中的 **`BepInEx\LogOutput.log`** (关闭游戏后的文件)
- 做了什么、发生了什么

报告地址: [GitHub Issues](https://github.com/DonutSuZu/RusK/issues)
