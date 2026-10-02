# キャラの Mod Pack の作り方

VED:Recure に、新しいキャラを 1 人足す「Mod Pack」の作り方です。
数字は **対応ゲームバージョン 0.0.1878 (c9d3e1a)** で、土台のキャラを **赤悠 (1006)** にしたときのものです。

使う Mod (どれも [RusK Mod Manager](https://github.com/DonutSuZu/RusK/releases/latest) から入れられます):

| Mod | 版 | 役目 |
|---|---|---|
| Custom Character | 1.1.0 + | キャラ枠・名前・絵 |
| Custom VRM Loader | 1.3.0 + | 見た目 (VRM / PMX) |
| Custom Motion | 1.0.0 + | 動き |
| Custom Item Model | 1.2.0 + | 武器 |
| Voice Replacer | 1.1.0 + | 声 |

> 使うモデル・声・絵は、利用規約で改変・利用・配布が許可されたものだけを使ってください。
> ゲームから書き出したもの (骨格の glb・お手本の絵・ゲームの声) は配らないでください。

---

## 1. 全体の流れと、作るものの数

| 手順 | 作るもの | 数 (赤悠が土台) | 無くても動くか |
|---|---|---|---|
| 1. キャラ枠 | `character.json` | 1 | 必須 |
| 2. 見た目 | VRM か PMX のモデル | 1 | 無ければ土台のキャラの見た目 |
| 3. 絵 | PNG | 25 種類 (うちキャラの絵は 15) | 無ければ土台のキャラの絵 |
| 4. 武器 | glb か PMX | 1 | 無ければ土台のキャラの武器 |
| 5. 声 | ogg / wav / mp3 | 戦闘・画面 38 + 拠点の会話 24 = 62 | 無ければ土台のキャラの声 |
| 6. 動き | glb (Blender) | 37 動作 (うちアニメあり 34) | 無ければ土台のキャラの動き |

どれも「無ければ土台のキャラのまま」なので、**1 つずつ置き換えていけます**。
おすすめの順: キャラ枠 → 見た目 → 絵 → 武器 → 声 → 動き。

---

## 2. フォルダの形

ゲームフォルダの `RusK` の下に、こう置きます (キャラ番号 9001、名前 MyChara の例):

```
RusK\
  characters\MyChara\
    character.json            ← キャラ枠
    images\*.png              ← 差し替える絵 (images_template と同じ名前)
    images_template\          ← (自動) 土台のキャラの絵のお手本
    captures\                 ← (自動) モデルを背景透明で撮った絵
    motions.txt               ← (自動) このキャラの動作の一覧
  models\MyChara\             ← モデル (.vrm か .pmx。PMX はテクスチャのフォルダごと)
  props\MyChara\              ← 武器 (.glb か .pmx)
  motions\MyChara\*.glb       ← 動き
  voices\9001\*.ogg           ← 声 (キャラ番号のフォルダ)
  data\model\assignments.txt      ← 見た目の割り当て (1 行)
  data\itemmodel\assignments.txt  ← 武器の割り当て (1 行)
  data\motion\bindings.txt        ← 動きの割り当て (動作ごとに 1 行)
```

---

## 3. キャラ枠 (Custom Character)

`RusK\characters\MyChara\character.json`:

```json
{"id": 9001, "base": 1006, "name": {"ja": "名前", "en": "NAME", "zh": "名字"}}
```

- `id`: **9000 以上**で、ほかの Mod Pack と重ならない番号
- `base`: 土台のキャラ。動作・能力値・スキル・攻撃の判定はこのキャラを複製します

| 番号 | キャラ | 武器 | 動作の数 (アニメあり) | 攻撃の動作 |
|---|---|---|---|---|
| 1001 | 黛 | 大きな武器 (溜め攻撃あり) | 70 (61) | 16 |
| 1002 | 慶慶 | 傘 | 44 (41) | 16 |
| 1003 | 紫冬 | 銃 | 42 (35) | 10 |
| 1005 | 風禾 | — | 39 (36) | 10 |
| 1006 | 赤悠 | 刀 | 37 (34) | 10 |
| 1008 | 律真 | — | 35 (35) | 11 |
| 1009 | 霜色 | — | 36 (36) | 11 |

**初めてなら赤悠 (1006) がおすすめ**です (動作の数が少なく、素直な作り)。
ゲームを起動すると、キャラの画面・出撃前の選択・パーティに出てきます (最初から解放済み)。

---

## 4. 見た目 (Custom VRM Loader)

- `RusK\models\MyChara\` に `.vrm` か `.pmx` (テクスチャのフォルダごと) を置く
- `RusK\data\model\assignments.txt` に 1 行: `9001=MyChara\model.pmx`
- PMX は MMD の標準の骨 (上半身・腕・ひじ・足・ひざ・指) が必要。A ポーズのままで大丈夫
- 揺れ物 (髪・スカート) は PMX の剛体から自動で作ります

---

## 5. 絵 (Custom Character)

ゲームが新しいキャラの絵を読むと、土台のキャラの絵が `images_template` に書き出されます (ゲームの画面を一通り開くと全部そろいます)。
**同じ名前・同じ大きさ**の PNG を `images` に置くと差し替わります。

| 名前 | 大きさ | 中身 | 出る場所 |
|---|---|---|---|
| `blackBar_n` | 204×106 | 顔と英語の名前のカード | キャラ画面の一覧 |
| `rolechoose` | 210×100 | 顔のアイコン | パーティ・選択 |
| `character_s` | 365×1440 | 立ち絵 (カラー) | 出撃前の選択 (選んだとき) |
| `choose_n` | 365×1440 | 立ち絵 (灰色) | 出撃前の選択 |
| `BGrole` | 1200×1440 | 大きな背景の絵 (灰色・暗め) | キャラ画面 |
| `name_s` | 449×173 | 名前 (自分の言語の文字) | キャラ画面 |
| `NameBar` | 451×67 | 名前の帯 (英語) | キャラ画面 |
| `roleName_s` | 206×30 | 英語の名前 | 選択 |
| `Profile` | 888×1440 | 横顔 | リザルト画面 |
| `leftFrame` | 888×1440 | 横顔 + 背景の紙 | リザルト画面 |
| `leftName` | 888×1440 | 英語の名前を 8 段に重ねた文字 | リザルト画面 |
| `leftNameMask` | 1360×1440 | 横顔の形の切り抜き (黒) | リザルト画面 |
| `buffResuiltProfile` | 1645×1440 | 暗いバストアップ | バフの結果 |
| `dialogBox` | 256×256 | 会話の顔 | 会話 |
| `character` | 158×559 | 小さな全身 | (画面による) |
| `blackBar_h` / `bg_s` / `BGtext1` / `BGtext2` / `blackline_s` / `btn_fight` / `difficult_s` / `skillLvColor` / `SkillIconAttack` / `SkillIconP` | — | 飾り・アイコン | 土台のままでも大丈夫 |

**自動で作る方法**: 新しいキャラを操作すると、モデルを背景透明で撮影します (`captures`: 顔・バストアップ・全身・横顔。メニューの **CustomCharacter > Capture** で撮り直し)。
[tools/character/make_card_art.py](../tools/character/make_card_art.py) が、撮った絵をお手本の構図に合わせて合成し、名前の文字も描きます (上の表の上から 14 種類):

```
python tools/character/make_card_art.py "<ゲーム>\RusK\characters\MyChara" 名前 NAME
```

---

## 6. 武器 (Custom Item Model)

- `RusK\props\MyChara\` に `.glb` か `.pmx` を置く
- `RusK\data\itemmodel\assignments.txt` に 1 行: `9001:1041|MyChara\sword.pmx|0,0,0|0,180,0|1|0|0|2`
  - `9001:1041` = キャラ 9001 が装備 1041 (赤悠の刀) を持つときだけ置き換え (赤悠の見た目は変わりません)
  - 続きは `位置|回転|大きさ|発光|発光の色|発光の強さ`。ファイルを保存するとゲームに自動で反映されるので、見ながら合わせられます
- 武器は攻撃している間だけ手に出ます (立っている・走っている間はしまわれます)

---

## 7. 声 (Voice Replacer)

`RusK\voices\9001\` に、**土台のキャラの声と同じ名前**で置きます。新しいキャラが場にいて、土台のキャラ本人がいないときだけ使われます。

- 形式: `.ogg` / `.wav` / `.mp3`
- 同じ名前に `#1`・`#2` を付けて複数置くと、鳴るたびにランダムに選びます (例 `LightAttackVoice_1006_1_JP#2.ogg`)
- 名前の最後の `_JP` は日本語の声の設定のとき。中国語の声の設定では `_JP` の無い名前 (例 `LightAttackVoice_1006_1`) で鳴るので、両方の言語に対応するなら同じファイルを両方の名前で置きます

### 戦闘・画面の声: 13 種類・38 個

| 名前 (赤悠の例) | 数 | 長さ (秒) | 鳴るとき |
|---|---|---|---|
| `LightAttackVoice_1006_1_JP` 〜 `_3` | 3 | 0.4〜0.6 | 通常攻撃のかけ声 |
| `HardAttackVoice_1006_1_JP` 〜 `_3` | 3 | 0.6〜0.8 | 強い攻撃のかけ声 |
| `LightHurtVocie_1006_1_JP` 〜 `_3` | 3 | 0.6〜1.1 | 小さく攻撃を受けた |
| `HardHurtVocie_1006_1_JP` 〜 `_3` | 3 | 1.4〜2.7 | 大きく攻撃を受けた |
| `HealthLowVocie_1006_1_JP` 〜 `_3` | 3 | 3.3〜4.4 | 体力が少ない |
| `DieVocie_1006_1_JP` 〜 `_3` | 3 | 3.4〜4.5 | 倒れた |
| `FightStartVoice_1006_1_JP` 〜 `_2` | 2 | 6.6〜7.5 | 戦闘の始まり |
| `FightWellVoice_1006_1_JP` 〜 `_6` | 6 | 1.6〜4.5 | いい戦いをした |
| `BuffChooseVoice_1006_1_JP` 〜 `_3` | 3 | 2.2〜3.1 | バフを選ぶ |
| `BuffEquipVoice_1006_1_JP` 〜 `_3` | 3 | 0.7〜2.0 | バフを付けた |
| `EquipWearVocie_1006_1_JP` 〜 `_3` | 3 | 2.0〜4.3 | 装備を付けた |
| `ChooseCharVocie_1006_1_JP` 〜 `_2` | 2 | 3.3〜6.4 | 出撃前にキャラを選んだ |
| `ShowPoseVocie_1006_JP` | 1 | 11.2 | キャラ画面のポーズ |

(`Vocie` はゲームの綴りのままです)

### 拠点の会話: 24 個

`SceneChat_1006_1_JP` 〜 `SceneChat_1006_24_JP` (2.7〜9.5 秒)。拠点で話しかけたときなど。
ストーリーの声 (`StoryTimeline_*`) は新しいキャラには使われないので要りません。

**名前の探し方**: メニューの **Music > VoiceReplacer** で **LogPlayed** を ON にすると、`RusK\voices\_played.txt` に鳴った音の名前が書かれます。

---

## 8. 動き (Custom Motion)

### 動作の数: 37 (アニメあり 34)

すべての動作の名前・長さ・攻撃判定は、キャラを足すと `RusK\characters\MyChara\motions.txt` に書き出されます。
赤悠 (1006) の場合 (秒 = ゲームでの長さ、攻撃判定 = 当たる瞬間の進み具合 0〜1):

| 動作の名前 | 秒 | 攻撃判定 | ループ |
|---|---|---|---|
| `Run` | 0.51 | - | loop |
| `RunStop` | 2.50 | - |  |
| `FastRun` | 0.40 | - | loop |
| `RunTurn` | 1.20 | - |  |
| `FastRunStop` | 3.50 | - |  |
| `Defence_1` | 3.83 | - |  |
| `Defence_2` | 3.83 | - |  |
| `DefenceAccept` | 4.13 | - |  |
| `Idle` | 2.00 | - | loop |
| `RedLightCombo0` | 3.83 | 0.08 |  |
| `RedLightCombo1` | 3.50 | 0.05 |  |
| `RedLightCombo2` | 3.33 | 0.05,0.11 |  |
| `RedLightCombo3` | 5.17 | 0.06 |  |
| `RedLightCombo4` | 5.33 | 0.13 |  |
| `SpecialAttack` | 5.67 | 0.11,0.14,0.19,0.25,0.27,0.29,0.31 |  |
| `Interection` | 2.05 | - |  |
| `DashAttack` | 5.50 | 0.08,0.15,0.19,0.21 |  |
| `RedLight_HitHeavy_Middile_Official` | 4.10 | - |  |
| `RedLight_HitHeavy_Right_Official` | 4.10 | - |  |
| `RedLight_HitLeft` | 2.33 | - |  |
| `RedLight_HitMiddle` | 2.33 | - |  |
| `RedLight_HitRight_Official` | 2.33 | - |  |
| `JumpNextLevel` | 1.43 | - |  |
| `Die` | 1.60 | - |  |
| `ShowUp` | 0.00 | - |  |
| `PreShowUp` | 0.00 | - |  |
| `DashFront` | 2.67 | - |  |
| `DashBack` | 2.83 | - |  |
| `FastRun_RightFoot` | 0.00 | - |  |
| `Born` | 10.17 | - |  |
| `Parry_Left` | 2.60 | - |  |
| `Parry_Right` | 2.57 | - |  |
| `Parry_LeftDown` | 2.57 | - |  |
| `Parry_RightDown` | 2.57 | - |  |
| `SpecialAttack_QTE_RedLight` | 4.00 | 0.26 |  |
| `NormalAttack_QTE_RedLight` | 3.83 | 0.31,0.34,0.36 |  |
| `DashAttack_QTE_RedLight` | 4.00 | 0.26,0.27,0.29 |  |

### 置き換える優先度

| 優先度 | 動作 | 数 |
|---|---|---|
| 1 (よく見る) | `Idle`・`Run`・`FastRun`・通常攻撃 `RedLightCombo0`〜`4` | 8 |
| 2 (戦闘で目立つ) | `DashAttack`・`SpecialAttack`・`DashFront`・`DashBack`・`RunStop`・`FastRunStop`・`RunTurn` | 7 |
| 3 (あれば) | 防御 3・パリィ 4・被弾 5・`Die`・`Born`・QTE 3・`JumpNextLevel`・`Interection` | 19 |

### 作り方

1. **Model > ModelLab** の「骨格と動作を glb で書き出す (参考用)」で、土台のキャラの骨格と全部の動作を書き出す (`RusK\data\model\rig_キャラ名_motions.glb`。時間割も入っています。**配らないでください**)
2. Blender でその骨格に動きを付けて、glb で書き出す (手順・道具: [tools/blender](../tools/blender))
3. `RusK\motions\MyChara\` に置き、`RusK\data\motion\bindings.txt` に動作ごとに 1 行:
   `9001/RedLightCombo0=MyChara\Slash1.glb#Slash1@0.30` (`@` の後ろ = 自分の動きの当たる瞬間の秒)
4. ファイルを保存するとゲームに自動で反映されます

**時間割のルール** (攻撃):
- 一番速く振る瞬間 = 攻撃判定 (上の表の「攻撃判定」の進み具合)。Custom Motion がそこまでを伸び縮みさせて合わせます
- 次の段への受付 (赤悠の通常攻撃は 0.20〜0.27) までに、振り抜いた姿勢で止まる
- 残りは、押さなかったときに立ち姿勢へ戻る余韻

---

## 9. 配るとき

配るのは、自分で作った・配布が許可されたものだけです:

- `characters\MyChara\character.json` と `images\`
- `models\MyChara\`・`props\MyChara\`・`motions\MyChara\`・`voices\9001\`
- 割り当ての行 (見た目・武器・動き) を README に書いておく

配らないもの: `images_template`・`captures` (ゲームの絵・ゲームのモデルの撮影)、`rig_*.glb` (ゲームの骨格・モデル)、ゲームの声。
