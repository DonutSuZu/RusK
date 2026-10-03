# RusK の使い方 (セットアップ・マニュアル)

[日本語](Manual.md) · [English](Manual.en.md) · [简体中文](Manual.zh.md)

RusK は、VED:Recure に Mod を入れるための道具です。**RusK Mod Manager** というアプリで、入れる・更新する・外すをまとめてできます。

- 対応しているゲームの版: **0.0.1878 (c9d3e1a)** (タイトル画面の左下に出ています)
- 必要なもの: Windows 10 / 11、Steam 版の VED:Recure

---

## 1. 入れる (はじめての人)

1. [リリースのページ](https://github.com/DonutSuZu/RusK/releases/latest) から **`RusK-Mod-Manager.exe`** をダウンロードする (好きな場所に置いて大丈夫)
2. ダブルクリックで起動する
   - 「Windows によって PC が保護されました」と出たら、**「詳細情報」→「実行」** を押す (署名していないアプリなので出ます)
3. 「RusK へようこそ」の画面で **「RusK を入れる」** を押す
   - ゲームのフォルダは Steam から自動で探します。見つからないときは「ゲームのフォルダを選ぶ...」で `ved.exe` があるフォルダを選ぶ
   - Mod を動かす土台 (BepInEx) が無ければ、一緒に入れます
4. 左の一覧から使いたい Mod を選び、**「インストール」** を押す (あとから何度でも入れたり外したりできます)
5. **「ゲームを起動」** を押す

> **はじめての起動は時間がかかります** (数分、画面が黒いままのことがあります)。Mod を動かす準備をしているだけなので、閉じずに待ってください。2 回目からは普通に起動します。

---

## 2. ゲームの中で使う

| 操作 | キーボード | ゲームパッド |
|---|---|---|
| RusK のメニューを開く / 閉じる | **Insert** | **LS + RS** (両方のスティックを押し込む) |
| 項目を選ぶ | ↑ ↓ | 十字キー ↑ ↓ |
| 開く・機能の ON / OFF | → / Enter | A |
| 戻る | ← / Backspace | B (いちばん左で押すと閉じる) |
| 設定の値を変える | 設定の列で ← → | 十字キー ← → |

- メニューは画面の左上に「カテゴリ (Party・Visual・Music など) → 機能 (モジュール) → 設定」の 3 列で出ます
- 機能を ON にすると動きます。設定は自動で保存されます
- マウスで操作したいときは、**Visual > Menu > GUI** を ClickGUI に切り替えます
- メニューを開くキーは **Visual > Menu > MenuKey** (パッドは **PadMenu**) で変えられます

---

## 3. Mod の一覧

| Mod | できること | 置くもの (ゲームフォルダの `RusK\` の下) |
|---|---|---|
| RusK UI | ボタンの案内・攻撃の予兆の表示など | — |
| EXTREME Difficulty | 難易度 EXTREME | — |
| Party | 3 人で戦う・切り替え | — |
| Chain Attack | 連携攻撃 (Party が必要) | — |
| Party Op.2 | エンドフィールド風の戦い方 (Party が必要) | — |
| Party Formation | ゼンゼロ風の編成画面 (Party が必要) | — |
| Camera View | 視点の切り替え | — |
| Music Manager | 戦闘の BGM を好きな曲に | `music\` |
| Voice Replacer | ボイス・効果音の差し替え | `voices\` |
| Effect Tuner | エフェクトの色・大きさ・差し替え | `effects\` |
| Custom VRM Loader | キャラの見た目を VRM / PMX (MMD) に | `models\` |
| Custom Item Model | 武器・装飾品の見た目を glb / PMX に | `props\` |
| Custom Motion | 自作の動き (Blender・VRMA) | `motions\` |
| Custom Character | 新しいキャラ (キャラの Mod Pack) | `characters\` |

---

## 4. ファイルを置く場所

### ゲームのフォルダを開く

Mod Manager の **「ゲームのフォルダを開く」** を押すのがいちばん確実です。
手で探すときは、Steam のライブラリでゲームを右クリック →「管理」→「ローカルファイルを閲覧」。
フォルダの名前は **`Ved疗愈所`** (中国語の名前) です。例: `C:\Program Files (x86)\Steam\steamapps\common\Ved疗愈所`

### フォルダの形

RusK を入れると、ゲームのフォルダに **`RusK`** フォルダができます。**素材はすべて、この `RusK` の中**に置きます。

```
Ved疗愈所\                 ← ゲームのフォルダ (ved.exe がある)
  ved.exe
  BepInEx\                 ← Mod を動かす土台 (触らなくて大丈夫)
  RusK\
    mods\                  ← Mod の本体 (.dll)。Mod Manager が入れるので、ふつうは触らない
    characters\            ← キャラの Mod Pack
    models\                ← VRM / PMX のモデル
    props\                 ← 武器・装飾品のモデル
    motions\               ← 自作の動き
    voices\                ← ボイス・効果音
    music\                 ← 戦闘の BGM
    effects\               ← 自作エフェクト
    configs\  data\        ← 設定 (自動で作られる。触らなくて大丈夫)
```

フォルダが無ければ、自分で作って大丈夫です (Mod を一度動かすと自動でできます)。各 Mod のメニューの **「OpenFolder」** や、画面の **「フォルダを開く」** でも、そのフォルダを開けます。

### Mod ごとの置き方

| Mod | 置く場所 | 置くもの | 例 |
|---|---|---|---|
| Custom Character | `RusK\characters\〈名前〉\` | Pack のフォルダごと (中に `character.json`) | `RusK\characters\MyChara\character.json` |
| Custom VRM Loader | `RusK\models\` | `.vrm`、または `.pmx` を**テクスチャのフォルダごと** | `RusK\models\MyModel\model.pmx` と `RusK\models\MyModel\Textures\` |
| Custom Item Model | `RusK\props\` | `.glb`、または `.pmx` をテクスチャのフォルダごと | `RusK\props\sword.glb` |
| Custom Motion | `RusK\motions\` | `.glb` / `.vrma` | `RusK\motions\Wave.glb` |
| Voice Replacer | `RusK\voices\` | `.ogg` / `.wav` / `.mp3` (**ファイル名 = ゲームの音の名前**) | `RusK\voices\LightAttackVoice_1006_1_JP.ogg` |
| Music Manager | `RusK\music\` (ボス戦は `RusK\music\boss\`) | `.ogg` / `.wav` / `.mp3` | `RusK\music\my_song.ogg` |
| Effect Tuner | `RusK\effects\` | `.bundle` (Unity で作ったもの) | `RusK\effects\my_effect.bundle` |

置いた後は:
- **VRM・PMX・武器**: メニューの各 Mod の画面で、どのキャラ・どの装備に使うかを選ぶ (キャラの Mod Pack なら選ばなくても自動で付きます)
- **ボイス**: ゲームの音の名前は、Voice Replacer の **LogPlayed** を ON にして遊ぶと `RusK\voices\_played.txt` に書かれます
- **うまく出ないとき**: ファイルを置いた場所が `RusK\` の**中**か (ゲームのフォルダの直下ではない)、拡張子が合っているかを確かめてください

---

## 5. キャラの Mod Pack を入れる

キャラの Mod Pack は、**フォルダを 1 つ置くだけ**です。

1. Mod Manager で、次の Mod を入れる: **Custom Character** (必須)。見た目・武器・動き・声が入っている Pack なら **Custom VRM Loader・Custom Item Model・Custom Motion・Voice Replacer** も
2. Pack のフォルダ (中に `character.json` がある) を、`RusK\characters\` に置く
   ```
   RusK\characters\〈Pack の名前〉\character.json
   ```
3. ゲームを起動すると、キャラの画面・出撃前の選択に新しいキャラが出ます (最初から使えます)

- 新しいキャラはセーブには残りません。Pack や Mod を外しても、セーブは壊れません
- 作り方: [docs/ModPack.md](ModPack.md)

---

## 6. 更新する・外す

- **更新**: Mod Manager を起動すると、更新を自動で確かめます。**「すべて更新」** でまとめて更新 (ゲームは閉じておく)
- **Mod を止める**: 一覧で Mod を選び **「無効にする」** (消さずに止める) / **「削除」**
- **全部外す**: **「RusK をアンインストール...」**。VRM・PMX・音声などの素材は残ります。設定も消すか聞かれます
- それでもゲームがおかしいときは、Steam の「ゲームファイルの整合性を確認」で元に戻せます

---

## 7. うまくいかないとき

| 困ったこと | 確かめること |
|---|---|
| メニューが開かない | タイトル画面の右下に「RusK v…」と出ているか。出ていなければ、Mod Manager で「RusK を入れる」をやり直す |
| ゲームが起動しない・すぐ落ちる | ゲームの版がタイトル画面の左下と同じ **0.0.1878** か (ゲームが更新されると、RusK の更新まで動かないことがあります) |
| 起動が終わらない | はじめての起動は数分かかります。それでも終わらなければ、ゲームのフォルダの `BepInEx\LogOutput.log` を見てください |
| Mod Manager が「最新の情報を取得できませんでした」 | インターネットの接続。GitHub が混んでいるときは、少し待って「最新の情報を取得し直す」 |
| 新しいキャラが出ない | `RusK\characters\〈名前〉\character.json` の場所が正しいか。Custom Character が有効か |
| ウイルス対策ソフトに止められる | Mod を動かす仕組み (BepInEx) が誤って検出されることがあります。ゲームのフォルダを除外に入れてください |

**報告するとき**は、次を添えてもらえると直しやすいです:
- ゲームのフォルダの **`BepInEx\LogOutput.log`** (ゲームを閉じた後のもの)
- 何をしたらどうなったか

報告先: [GitHub の Issues](https://github.com/DonutSuZu/RusK/issues)
