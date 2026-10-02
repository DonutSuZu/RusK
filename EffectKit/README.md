# RusK EffectKit

Effect Tuner の「差し替え」で使う **自作エフェクト** を作るための Unity プロジェクトです。
作ったエフェクトは AssetBundle (`.bundle`) に書き出して、ゲームの `RusK\effects` に入れると、
Effect Tuner の差し替え先の一覧の「自作」タブに出てきます。

[English](#english) / [中文](#中文)

## 必要なもの
- **Unity 2022.3.7f1** (ゲームと同じ版。Unity Hub の「インストール」→ アーカイブから入れる)
  - 別の版で書き出したバンドルは、ゲームで読み込めないことがあります
- ゲームに RusK と Effect Tuner が入っていること

## 使い方
1. Unity Hub の「プロジェクト」→「追加」で、この `EffectKit` フォルダを開く (初回はパッケージの読み込みに数分かかります)
2. 最初の 1 回だけ、メニューの **RusK > ゲームの RusK フォルダを選ぶ** で、ゲームのフォルダ (`ved.exe` のあるところ) を選ぶ
3. `Assets/Effects` の中にフォルダを作り、その中にエフェクトのプレハブを置く
   - フォルダ 1 つ = バンドル 1 つ (`フォルダ名.bundle`)。人に配るときは、自分の名前のフォルダにまとめると分かりやすいです
   - プレハブ 1 つ = エフェクト 1 つ。ゲームではプレハブの名前で出ます (★ 付き)
   - 見本: `Assets/Effects/Sample/RusK_Sample_Burst` (メニューの **RusK > 見本のエフェクトを作る** で作り直せます)
4. メニューの **RusK > エフェクトを書き出す** を押す → `Build` に書き出して、ゲームの `RusK\effects` にもコピーします
5. ゲームで Effect Tuner を開き、変えたいエフェクトの「差し替え」→「選ぶ」→「自作」タブから選ぶ
   - ゲームを起動したまま書き出し直したときは、「自作」タブの「読み込み直す」を押す

## 作るときの決まり
- **使えるのは Unity の部品だけ** です (Particle System・Trail Renderer・Line Renderer・Mesh Renderer・Light・Animator など)。
  自分で書いたスクリプト (C#) はゲームでは動きません (書き出すときに警告が出ます)
- マテリアルは **URP のシェーダー** を使ってください (`Universal Render Pipeline/Particles/Unlit` など)。
  `Legacy Shaders/...` や `Standard` などの古いシェーダーは、ゲームではピンク色になります。Shader Graph で作ったものは使えます
- エフェクトは元のエフェクトの位置・向きに、元の子として出ます。再生は「出るたびに頭から」なので、
  Particle System は **Looping をオフ**、Play On Awake をオンにしておくのがおすすめです
- 大きさの目安: キャラの背は 1.6〜1.8 くらいです。Particle System の Scaling Mode を **Hierarchy** にしておくと、
  Effect Tuner の「大きさ」が効きます
- 元のエフェクトが消えると、差し替えたエフェクトも一緒に消えます (長く残したいときは、元も長いエフェクトを選んでください)
- Effect Tuner の色の設定 (色相・明るさなど) は、自作エフェクトにもかかります

## 困ったとき
- 「自作」タブに出ない: `RusK\effects` に `.bundle` があるか、Unity の版が 2022.3.7f1 か確認して、「読み込み直す」を押す。
  BepInEx のログ (`BepInEx\LogOutput.log`) に `Effect:` から始まる行が出ていないか見る
- ピンク色になる: マテリアルのシェーダーを URP のものに変える
- 何も出ない: Particle System の Looping・Play On Awake・Duration を確認する

コマンドラインから書き出すこともできます:
```
Unity.exe -batchmode -projectPath EffectKit -executeMethod RusK.EffectKit.RuskEffectKit.BuildFromCommandLine -ruskFolder "〈ゲームのフォルダ〉\RusK" -quit
```

---

## English
A Unity project for making **custom effects** for Effect Tuner's "Replace".
Build them into AssetBundles (`.bundle`), put them in the game's `RusK\effects` folder, and they show up in the "Custom" tab of the replacement list.

**You need** Unity **2022.3.7f1** (same version as the game; install it from the Unity Hub archive), and RusK + Effect Tuner in the game.

1. In Unity Hub, Projects → Add, and open this `EffectKit` folder (the first import takes a few minutes)
2. Once: **RusK > Set Game Folder** and pick the game folder (where `ved.exe` is)
3. Make a folder in `Assets/Effects` and put effect prefabs in it. One folder = one bundle, one prefab = one effect (shown by its prefab name with a ★).
   A sample is in `Assets/Effects/Sample` (**RusK > Create Sample** recreates it)
4. **RusK > Build Effects** builds into `Build` and copies the bundles to the game's `RusK\effects`
5. In game, open Effect Tuner, then "Replace" → "Pick" → "Custom" tab. If the game was running while you rebuilt, press "Reload"

Rules:
- **Only Unity's own components work** (Particle System, Trail/Line/Mesh Renderer, Light, Animator, ...). Your own C# scripts will not run
- Use **URP shaders** for materials (e.g. `Universal Render Pipeline/Particles/Unlit`, or Shader Graph). Legacy shaders render pink
- The effect is spawned as a child of the original effect and restarted every time it appears: turn **Looping off** and Play On Awake on.
  Use Scaling Mode **Hierarchy** so Effect Tuner's "Size" works. Characters are about 1.6–1.8 units tall
- It disappears together with the original effect. Effect Tuner's color settings also apply to it

## 中文
用于制作 Effect Tuner "替换" 功能所用 **自制特效** 的 Unity 项目。
导出为 AssetBundle (`.bundle`) 并放入游戏的 `RusK\effects` 文件夹后, 会显示在替换列表的 "自制" 标签中。

**需要** Unity **2022.3.7f1** (与游戏相同的版本, 从 Unity Hub 的存档安装), 以及游戏中已安装 RusK 和 Effect Tuner。

1. 在 Unity Hub 中 "项目" → "添加", 打开此 `EffectKit` 文件夹 (首次导入需要几分钟)
2. 仅首次: **RusK > ゲームの RusK フォルダを選ぶ (Set Game Folder)**, 选择游戏文件夹 (`ved.exe` 所在处)
3. 在 `Assets/Effects` 中创建文件夹并放入特效预制体。一个文件夹 = 一个包, 一个预制体 = 一个特效 (以预制体名称加 ★ 显示)。
   示例在 `Assets/Effects/Sample` (**RusK > Create Sample** 可重新生成)
4. **RusK > Build Effects** 导出到 `Build`, 并复制到游戏的 `RusK\effects`
5. 在游戏中打开 Effect Tuner, "替换" → "选择" → "自制" 标签。若游戏运行中重新导出, 请按 "重新读取"

规则:
- **只能使用 Unity 自带的组件** (Particle System、Trail/Line/Mesh Renderer、Light、Animator 等)。自己写的 C# 脚本不会运行
- 材质请使用 **URP 着色器** (如 `Universal Render Pipeline/Particles/Unlit` 或 Shader Graph)。旧版着色器会显示为粉色
- 特效作为原特效的子物体生成, 每次出现时从头播放: 请关闭 **Looping**, 打开 Play On Awake。
  Scaling Mode 设为 **Hierarchy** 后 Effect Tuner 的 "大小" 才会生效。角色身高约 1.6–1.8
- 原特效消失时它也会一起消失。Effect Tuner 的颜色设置同样适用
