<p align="center"><img src="icon.png" width="120" alt="RusK"></p>

# RusK — Mod Loader (VED:Recure)

BepInEx 6 (IL2CPP) の上で動く、着脱可能な Mod ローダー。
ゲーム内メニュー (Insert キー) から Mod の機能を ON/OFF したり、Mod を読み込み・取り外し・再読み込みしたりできる。

- TabGUI / ClickGUI (Horion 風)、ArrayList・通知・ウォーターマーク
- Mod の着脱 (AssemblyLoadContext)、アクショントリガー、Config プロファイル
- Flex Window (Mod 用のドラッグ・リサイズできるウィンドウ)
- 同梱 Mod: **RusK UI** (ボタン HUD・攻撃予兆・キー追加) / **EXTREME Difficulty** (難易度 EXTREME の解放と敵の強化) / **Music Manager** (戦闘 BGM の置き換え) / **Voice Replacer** (ボイス・効果音の置き換え) / **Camera View** (視点の切り替え) / **Effect Tuner** (エフェクトの色・大きさ・差し替え・自作エフェクト) / **Party** (アクティブ3人・切り替えパリィ・パッシブバフの共有) / **Chain Attack** (連携攻撃。Party が必要) / **Party Op.2** (エンドフィールド風のバトルスタイル。Party が必要) / **Party Formation** (ゼンゼロ風の編成画面。Party が必要) / **Custom Model** (キャラの見た目を VRM / PMX に) / **Custom Item Model** (武器・装飾品の見た目を glb / PMX に) / **Custom Motion** (Blender・VRMA・Mixamo の動きをキャラの動作に) / **Custom Character** (新しいキャラ枠)
- **RusK Check**: ゲームの更新で壊れた Mod を教える診断機能

**ゲーム**: [VED:Recure (Steam)](https://store.steampowered.com/app/3255500/Ved/)
<img width="2559" height="1439" alt="スクリーンショット 2026-09-28 143100" src="https://github.com/user-attachments/assets/fc1d804d-1a30-4ef0-8e89-dce345236e2c" />

<img width="2559" height="1439" alt="スクリーンショット 2026-09-28 133951" src="https://github.com/user-attachments/assets/4d212f47-ec62-4f92-b872-2479d8ff2fe1" />
<img width="2559" height="1439" alt="スクリーンショット 2026-09-28 134006" src="https://github.com/user-attachments/assets/8c5b5815-bbac-444b-9181-ef8d9f84f38c" />
<img width="2559" height="1439" alt="スクリーンショット 2026-09-28 143127" src="https://github.com/user-attachments/assets/6d459b0f-1843-423b-b61d-34582da4b3ad" />
<img width="2559" height="1439" alt="スクリーンショット 2026-09-28 143135" src="https://github.com/user-attachments/assets/558d6ad4-841f-498c-a79c-dd3b4c886368" />
<img width="2559" height="1439" alt="スクリーンショット 2026-09-28 143139" src="https://github.com/user-attachments/assets/7828f779-6900-49a0-81b5-01cdc945e14e" />

**対応ゲームバージョン: 0.0.1878 (c9d3e1a)** — ゲーム画面の左下に出る `Version 0.0.1878_c9d3e1a` と同じか確認してください。
ゲームが更新されたときは、メニューの Mods > Check で動かなくなった Mod を確認できます。

## キャラの Mod Pack を作る

新しいキャラ (見た目・絵・武器・声・動き) を足す Mod Pack の作り方と、必要なファイルの種類・数: [docs/ModPack.md](docs/ModPack.md)

## インストール (利用者向け)
1. [最新のリリース](https://github.com/DonutSuZu/RusK/releases/latest) から `RusK-Mod-Manager.exe` をダウンロード (好きな場所に置いてよい)
2. 起動すると、ゲームフォルダを Steam から自動で探し、GitHub から最新の情報を取得する (表示は日本語 / English / 中文)
   - 「RusK を入れる」で RusK 本体を入れる。BepInEx 6 (IL2CPP 版) が無ければ、動作確認済みの公式 be.788 も一緒に入れる (SHA256 で確認)
   - 一覧から Mod を選んで「インストール」。必要な Mod (例: Chain Attack → Party) も一緒に入る
   - 起動するたびに RusK 本体・Mod・Mod Manager 自身の更新を確かめる。「すべて更新」でまとめて更新できる
   - Mod の DLL をウィンドウにドラッグ＆ドロップすると追加できる。有効 / 無効の切り替え・削除もここで
3. 「ゲームを起動」(Steam から起動) → **Insert** キーでメニューを開く

※ 署名していない exe なので、初回は SmartScreen の警告が出ることがある (「詳細情報」→「実行」)

## ライセンス
[LGPL-2.1](LICENSE)

---

以下は開発者向けの説明。

## 構成
```
RusK/
├─ RusK.API/            Mod が参照する公開API (IRuskMod, Module, Setting, Render)
├─ RusK.Core/           ローダー本体 (BepInExプラグイン)。Mod管理・GUI・Config
├─ RusK.Mods.Ui/        同梱Mod: ボタン HUD・攻撃予兆・キー追加 (バランスに影響しない補助)
├─ RusK.Mods.Extreme/   同梱Mod: 難易度 EXTREME
├─ RusK.Mods.Music/     同梱Mod: 戦闘 BGM の置き換え
├─ RusK.Mods.Voice/     同梱Mod: ボイス・効果音の置き換え (ゲームの AudioPlayer に割り込む)
├─ RusK.Mods.Camera/    同梱Mod: 視点の切り替え
├─ RusK.Mods.Effect/    同梱Mod: エフェクトの色・明るさ・大きさ (GameUtil.LoadEffect に割り込む)
├─ RusK.Mods.Party/     同梱Mod: アクティブ3人 (ほかの Mod 用の入口 PartyBridge)
├─ RusK.Mods.Chain/     同梱Mod: 連携攻撃 (Party の PartyBridge をリフレクションで使う)
├─ RusK.Mods.Op2/       同梱Mod: Party Op.2 (エンドフィールド風のバトルスタイル。Party から呼ばれる)
├─ RusK.Mods.Formation/ 同梱Mod: Party Formation (ゼンゼロ風の編成画面。PartyBridge 版 3 を使う)
├─ RusK.Mods.Model/     同梱Mod: キャラの見た目を VRM / PMX に (glb・PMX の読み込み・動きの写し・揺れ物・表情)
├─ RusK.Mods.ItemModel/ 同梱Mod: 武器・装飾品の見た目を glb に (glb の読み込みは RusK.Mods.Model/Vrm をソースごとリンク)
├─ RusK.Mods.Motion/    同梱Mod: glb / vrma のアニメーションをキャラの動作に (Glb.cs を RusK.Mods.Model/Vrm からリンク)
├─ RusK.Mods.Shared/    ゲーム用 Mod で共有するソース (キャラの表示名など。各 csproj に Compile Include でリンク)
├─ EffectKit/           Effect Tuner の自作エフェクトを作る Unity 2022.3.7f1 のプロジェクト (AssetBundle に書き出す)
├─ RusK.Manager/        RusK Mod Manager (ランチャー。本体・Mod の導入と更新、ゲームの起動)
├─ catalog.json         公開中の Mod の一覧とお知らせ (Mod Manager が main から取得する)
├─ RusK.sln
└─ build.bat            全体をビルドしてゲームへ配置

配置先 (ゲーム側):
  BepInEx/plugins/RusK/   RusK.Core.dll, RusK.API.dll   ← ローダー本体
  RusK/mods/              RuskUi.dll など              ← 着脱するMod
  RusK/configs/           <profile>.json                  ← 設定の保存先
  RusK/data/<modid>/      各Modの作業フォルダ
```

## RusK Mod Manager (ランチャー) と配布物を作る
`release.bat` をダブルクリック → `dist\RusK-Mod-Manager.exe` (配る exe) と `dist\RusK-Core.zip` (本体) ができる。
- `RusK.Manager` は .NET Framework 4.8 の WinForms アプリ（Windows 10/11 は追加インストール不要）
- 起動するたびに `catalog.json` (main ブランチ) と GitHub のリリースの一覧を取得して、本体・Mod・Mod Manager 自身の更新を確かめる。
  取得できないときは exe に埋め込んだビルド時の `catalog.json` を使う
- Mod の情報 (名前・版・説明・動作確認したゲーム) は、DLL を読み込まずにメタデータから `[RuskMod]` 属性と埋め込みの言語ファイルを読む (`ModInfoReader.cs`)
- 無効にした Mod は `RusK\mods\disabled\` に移す (RusK は `RusK\mods` の直下の DLL だけを読み込む)
- Mod Manager 自身の更新: 新しい exe を `.new` に落とし、今の exe を `.old` に名前を変えて入れ替え、新しい方を起動する (次の起動で `.old` を消す)
- 利用者向けの説明は `RusK.Manager\Docs\README.txt`（`RusK-Core.zip` に入り、ゲームフォルダの `RusK\README.txt` になる）

バージョンを上げるときに変える場所:
本体は `RusK.Core\Rusk.cs` の `Version` と `RusK.Core.csproj` の `<Version>`、Mod Manager は `RusK.Manager.csproj` の `<Version>`、各 Mod は `.csproj` の `<Version>` と `[RuskMod]` の版

対応ゲームバージョンが変わったら: `catalog.json` の `gameVersion` / このファイルと `Docs\README.txt`（各 Mod の `GameVersion` は、その Mod を直したときだけ上げればよい。Check は版の違いだけでは注意を出さない）

Mod を増やしたら / お知らせを出したら: `catalog.json` に足して main に push するだけ (Mod Manager の更新はいらない)。
DLL のリリースが無い Mod は一覧に出ないので、先に push しても大丈夫

## リリースの分け方
- Mod Manager: タグ `manager-vX.Y.Z`「RusK Mod Manager vX.Y.Z」に `RusK-Mod-Manager.exe`。**これを Latest にする** (利用者が落とすのはこれだけ)
- 本体: タグ `vX.Y.Z`「RusK vX.Y.Z」に `RusK-Core.zip` (`--latest=false`)
- Mod: タグ `<Mod の ID>-vX.Y.Z` (例: `model-v1.2.5`「Custom VRM Loader Mod v1.2.5」) に DLL (例: `RuskModel.dll`) (`--latest=false`)
- Mod Manager はリリースを新しい順に見て、ファイルの名前ごとに、それがある最初のリリースを最新とする (版はタグの `-v` の後ろ)。
  本体や Mod だけ更新したいときは、そのリリースを出すだけでよい

## 言語ファイル
- 各プロジェクトの `lang/translations.tsv` (元の文 / 英語 / 中国語) に訳を書き、`python tools/langgen.py <プロジェクト>` で
  `lang/ja.json`・`en.json`・`zh.json` を作る (訳の無い文は一覧に出て、tsv の最後に空欄で足される)
- json は DLL に埋め込まれる (`RusK.props`)。表示する文は `L.T("...")` で包む (モジュール名・説明・設定は本体が自動で訳す)

## ビルド
`build.bat` をダブルクリック。または:
```
dotnet build RusK.sln -c Release
```

## 操作
**Insert** でメニュー開閉（Visual > Menu > MenuKey で変更可）。メニューは2種類あり、Visual > Menu > GUI で切り替える。

### TabGUI（既定・キーボード操作）
画面左上に「カテゴリ → 項目 → 設定」の3列で出る。
| キー | 動作 |
|---|---|
| ↑ ↓ | 選択 |
| → / Enter | 開く・モジュール ON/OFF・コマンド実行 |
| ← / Backspace | 戻る |
| 設定の列で ← → | 値を変更（長押しでリピート） |
| 設定の列で Enter | キー割り当て開始・ボタン実行 |

### ClickGUI（マウス操作）
| 操作 | 動作 |
|---|---|
| 左クリック | ON/OFF・実行・設定を開く |
| 右クリック | 設定の開閉 / 値を戻す方向へ変更 |
| Shift+左クリック | モジュールのトグルキー割り当て |
| 見出しをドラッグ / 右クリック | パネル移動 / 折りたたみ |

### キー割り当て（共通）
「Bind」「Key」行を選んで Enter / クリック → 次に押したキーを割り当てる。
Ctrl / Shift / Alt を押しながらなら組み合わせキーになる。**Esc** でキャンセル、**Delete** で解除。
ゲームパッドのボタンも割り当てられる (表示は `Pad A` など)。

### ゲームパッド
| 操作 | 動作 |
|---|---|
| **LS + RS** (両方のスティックの押し込み) | メニュー開閉（Visual > Menu > PadMenu で View + Menu / Off に変更可） |
| 十字キー | 選択・値の変更 |
| A / B | 決定 / 戻る（いちばん左の列で B を押すと閉じる） |

- パッドで開いたメニューは、GUI の設定にかかわらず TabGUI で出す。開いている間はゲームの操作を止める (`InputController.SetLockInput`。RusK 本体はゲームの DLL を参照しないのでリフレクションで呼ぶ: `GameInputLock.cs`)
- パッドのボタンは `KeyCode.JoystickButton0`〜`15` で表す (`PadButton`: 0〜9 は Unity の昔の JoystickButton の並び、10〜15 は LT / RT / 十字キー)。
  `NewInput` がつながっているすべてのゲームパッド (`Gamepad.s_Gamepads`) から読むので、Mod は `RuskInput.WasPressed(Hotkey)` のままでパッドにも対応する
- Xbox の名前で表示する。PlayStation のパッドは A=✕ B=○ X=□ Y=△ LB=L1 RB=R1 LT=L2 RT=R2 View=Create Menu=Options

## メニューの中身
| カテゴリ | 内容 |
|---|---|
| Combat / Player / Visual … | 各 Mod のモジュール。先頭の「Bind」行でトグルキー、「ShowInList」で ArrayList に出すかを設定 |
| Visual > **Menu** | GUI の種類、MenuKey、透明度、カスタムカラー、ライティング（Static / Rainbow / Breathing / Wave）と速さ、ArrayList の表示とアニメ速度、ウォーターマーク、通知 |
| **Triggers** | アクショントリガーの編集。「+ Add Trigger」で追加 → Action（←→で選択）、Key、Mode（Press: 押すたび / Hold: 押している間）、Delete |
| **Mods** | Mod の Reload / Unload、未読み込み DLL の Load、Config プロファイル（切替・Save・Reload・New）、Rescan |

HUD: 右上 ArrayList（有効モジュール、ゆっくりスライド）、右下 ウォーターマーク（非表示可）と通知、
ボタン HUD（ゼンゼロと同じ並び: 攻撃・回避・スキル・ガード、ガードの上に追加攻撃。ゲージはボタンの中に液体がたまる見た目。
追加攻撃 = QTEAttack は、ガード・回避反撃・スキルの直後など撃てるときに光って READY! を出す）。

## 同梱 Mod の使い方
### Party（アクティブ3人）
- 拠点で **Party > Party > SelectMembers** から仲間を 2 人選ぶ（リーダーは拠点で選んだキャラ。戦闘中は編成を固定）
- 戦闘ステージに入ると仲間が控えに用意され、**C で次 / Z で前**のキャラに交代（クールタイム 3 秒）
- **切り替えパリィ**: 切り替えた瞬間を「ガードを押した」扱いにする。攻撃の直前に切り替えると、出てきたキャラがゲーム本来のガードでパリィする
- 操作中のキャラが倒れたら、生きている仲間に自動で交代（全員倒れたらゲームオーバー）
- 画面左にパーティ HUD（顔・HP・必殺技ゲージ・交代キー）。位置と大きさは設定で変更できる
- **パッシブバフの共有** (ShareBuffs): 操作中のキャラが得た / 失ったパッシブバフを控えにも付ける / 外す (キャラ専用・装備のバフは除く)
- 切り替えの後、敵の AI (Behavior Designer の BehaviorTree の変数) が前のキャラを指していたら今のキャラに付け替える (立ち尽くす敵の対策)
- **DevTools** (開発者向け、既定 OFF): ON にしてゲームを再起動すると Party Lab (調査用ウィンドウ・追加攻撃の試し撃ち・敵の AI の変数のログ) が出る
- ほかの Mod 用の入口 `PartyBridge` (public static): メンバー・切り替え・顔アイコン・切り替えキー・無敵 (BlockHits)・キーの横取り (SuppressSwitchKeys)・HUD の描き足し (HudExtras)。
  Mod は別々の AssemblyLoadContext で読み込まれて互いの DLL を参照できないので、使う側はリフレクションで探す (Chain の `PartyLink.cs`)

### Chain Attack（連携攻撃、Party が必要）
- 敵に 1 ヒットで 1P (パーティ HUD の下に CHAIN ゲージ、必要な数の 2 倍で頭打ち)。ステージ移動・連携の発動・30 秒攻撃なしで 0 に戻る
- **必要なポイント (PointsNeeded: 300 / 600 / 900 / 1500、既定 300) がたまった状態で追加攻撃を当てる**と時間がほぼ止まり、画面下に選択ゲージ (5 秒)。**C / Z** で次のキャラを選ぶと、敵の目の前に出て追加攻撃を撃つ
- 編成の人数だけ最後まで繋がる (狙った敵が倒れたら近くの別の敵へ、外れても次へ、途中の時間切れは左のキャラを自動で選ぶ)。連携中は攻撃を受けない
- 最初の選択で **X** (SkipKey) を押すか時間切れにすると、連携せずに 1 回分をストックする (最大 1。次に追加攻撃を当てると使う)
- 追加攻撃は、キャラの動作の一覧から `NormalAttack_QTE_*` を名前で探して直接出す (`PlayerController.QTEAttack()` は今のコンボの続きしか出せない)
- 追加攻撃のヒットは、操作キャラの動作を毎フレーム見張り「QTE の動作中か、終わって 0.4 秒以内」のヒットで判定する (`EnemyController.GetHit` の戻り値は当てにならない)
- Party Op.2 のエンドフィールドスタイルのときは止まる

### Party Op.2（エンドフィールド風のバトルスタイル、Party が必要）
- Party の **BattleStyle** を「エンドフィールド」にすると有効 (Op.2 はメニューに項目を作らない。Party が `EndfieldLink` で `RusK.Mods.Op2.Op2Entry.Attach` を呼ぶ)
- 戦闘ステージでは 3 人全員をフィールドに置き、操作していないキャラはオート (追いかける・攻撃・ついていく・離れたらワープ)。オートのキャラは無敵
- **C / Z / F1〜F3** でその場で切り替え (キャラは動かさず、操作・カメラ・HUD・敵の狙いだけ移す)。切り替えパリィも効く
- 1〜3 と F1〜F3 のキーは Party の設定 (FieldSkill1Key〜 / FieldSwitch1Key〜) で変えられる (ゲームパッドのボタンも可)。Op.2 は `PartyBridge.FieldSkillKey / FieldSwitchKey` (版 4) で読む
- **共有 EP**: 1 ヒットで 1、バフなどで増えたゲームのエネルギーも足す。最大はフィールドのキャラのエネルギーの最大値でいちばん大きいもの
- **1〜3 短押し**: そのキャラが EP 100 で特殊攻撃 (操作は切り替えない)。**長押し (0.4 秒)**: キャラごとの必殺ゲージ (そのキャラのヒットで 1、50 で満タン) で必殺技 (追加攻撃の動作)
- E キーの特殊攻撃・Q キーの追加攻撃は封印し、ゲームのエネルギーは EP の割合に同期。RusK UI のボタン HUD は隠す
- 仕組み: キャラのコンポーネントは `GameUtil.m_curPlayer` がそのキャラのときしか動かないので、置いたキャラの `Update` などの間だけ
  `m_curPlayer`・`GameSave.lastCrtId`・`GameUtil.m_playerEquipCur` (装備。差し替えないとリーダーの装備でダメージを計算する) を差し替える (`FieldSwap.cs`)

### Party Formation（ゼンゼロ風の編成画面、Party が必要）
- メニューの **Formation > PartyFormation** (アクション `FormationOpen`) で開く。リーダー + 仲間 2 人の斜めのカード (顔・テーマカラー・役割)
- 仲間のカードをクリックするとキャラの一覧が出て選べる。下のボタンでバトルスタイル (ゼンゼロ / エンドフィールド) を切り替える
- 編成は Party の `PartyBridge` (版 3: `Companions` / `SetCompanion` / `SetBattleStyle` など) で読み書きする

### Voice Replacer（ボイス・効果音の置き換え）
- `RusK\voices` に、ゲームの音声と**同じ名前**の ogg / wav / mp3 を置くと、鳴る瞬間に差し替える (サブフォルダは自由。キャラごとに分けるなど)
- `名前#1.ogg`・`名前#2.ogg` のように `#` の後ろを変えて複数置くと、鳴るたびにランダムに選ぶ
- 名前の調べ方: **Music > VoiceReplacer** の **LogPlayed** を ON にすると、鳴った音の名前と種類 (Voice / SFX / UI など) を `RusK\voices\_played.txt` に書き出す
- 割り込む場所: `AudioPlayer.CreateSFX` (2 つ)・`PlayPersistentVoice`・`PlayUICharacterVoice` (前) と `ResolveVoiceClip` (後)。
  ボイスはどれも `AudioPlayer` を通る (戦闘 `PlayVoiceFunc`、吹き出し `PlayBubbleVoice`、会話 `ResolveVoiceClip`)。
  差し替えた音声では、ゲームの言語の選び直し (`PlayPersistentVoice` の resolveLanguage) をしない
- 音声は起動時に全部読み込んでおく (`HideFlags.DontUnloadUnusedAsset` でシーンの切り替えでも消さない)。Volume で置き換えた音の音量を変えられる

### Effect Tuner（エフェクトの色・大きさ）
- **Visual > EffectTuner > OpenWindow** で設定画面を開く。対象 (全体 / キャラ / 敵 / 最近出たエフェクト) を選んで「設定を作る」
- 変えられるもの: 色相 (色付きの部分の色を変える)・白い部分にも色をつける量・彩度・明るさ・不透明度・大きさ・非表示・**差し替え** (ゲームの別のエフェクトに)。細かい対象の設定が優先 (エフェクト → キャラ / 敵 → 全体)
- 設定は `RusK\data\effect\rules.json` に保存。次にそのエフェクトが出たときから効く
- 仕組み: エフェクトはどれも `GameUtil.LoadEffect` (3 つ) を通るので、その後に設定をかける。持ち主は呼んだ関数で決める
  (`PlayerController.CreateEffectOnTrans` / `SetPerfectDefence` / `CreateGroundTrail` = そのキャラ、`EnemyController.GetHitCallback` = 攻撃したキャラ、`EnemyController.CreateEffect` など = 敵)
- ゲームのエフェクト (`Resources/VFX`、約 400 個) はほぼ全部 ParticleSystem で、シェーダーは色のプロパティを持たない。
  色はパーティクルの `startColor` / `colorOverLifetime` で付いているので、そこの色相・彩度・明るさを変える (テクスチャ自体の色は変わらない)
- エフェクトはプールで使い回されるので、最初に見たときの元の値を覚えて毎回そこから計算する (同じ設定がかかっていれば何もしない)
- 差し替え (`EffectReplacer`): 元のエフェクトの表示を消し、差し替え先 (`Resources/VFX` のプレハブ) を元の子 (`RusK:名前`) として出す。
  元 1 つにつき 1 つ作って使い回し、出るたびに頭から再生する。差し替え先のゲームのスクリプト (時間で隠す・プールに戻すなど) は止める
- 自作エフェクト: `RusK\effects` の `*.bundle` (AssetBundle) の中のプレハブも差し替え先になる (名前は `custom/プレハブ名`、一覧では ★ 付き)。
  作り方は [EffectKit/README.md](EffectKit/README.md) (Unity 2022.3.7f1 のプロジェクト。メニューの RusK > エフェクトを書き出す で、書き出してゲームにコピーする)
  - バンドルは `AssetBundle.LoadFromMemory` で開く (ファイルを掴まないので、ゲームを起動したまま書き出し直して「読み込み直す」で反映できる)
  - 使えるのは Unity の部品だけ (自作スクリプトは IL2CPP のゲームには無いので動かない)。シェーダーはバンドルに入るので、URP 14 のものならそのまま描ける
  - 差し替え先のバンドルが無いときは、元のエフェクトを隠さない

### Custom Model（VRM）
- `RusK\models` に `.vrm` (VRM 0.x / 1.0) を置き、**Visual > CustomModel** でキャラごとに選ぶ
- ゲームのキャラ (骨格・アニメーション・当たり判定) はそのまま動かし、見た目だけを VRM にする
  - 動き: ゲームの骨の回転を、基準の姿勢 (T ポーズ) からの差分として毎フレーム VRM に写す
    (Unity の AvatarBuilder / HumanPoseHandler は IL2CPP 経由だとクラッシュするので使っていない)
  - 材質: ゲームのトゥーンシェーダー (Custom/ToonLit_Crt) で、ゲームのキャラと同じ陰影・色調で描く。
    透明部分はキャラ用の切り抜き (`_USEALPHACLIPPING_ON` + `_CharacterAlphaClipMap`、白い所が消える地図なので透明度を反転して渡す)。
    両面表示の材質は裏向きの面をメッシュに足し (Outline パスは止める)、影は裏向きの面の無い「影だけ」のメッシュで落とす
  - 揺れ物 (SpringBone) に対応。武器は VRM の手の位置に合わせる
  - 表情: ゲームの顔のブレンドシェイプ (Mouth_Shout = 口パク、Mouth_Smile02、Eyes_Closed、Pupil_* など) を VRM の表情 (A / Fun / Blink / LookUp など) に写す。ゲームの顔がまばたきしないときは自動のまばたき
  - 操作キャラだけでなく、タイトル画面・キャラクター画面・装備画面の見せるためのモデル (`CharacterShowController`) にも付ける。
    どのキャラかは `InitialSetting(id)` で受け取り、動きは `CinemachineBrain.LateUpdate` の後で写す (タイトル画面では CameraController が動かない)。
    元の体を描かないと Animator がアニメーションを止めるので、付けている間は `AnimatorCullingMode.AlwaysAnimate` にする
- 装飾品 (WeaponHolder_1～4: 頭・肩・腰・背中) は部位ごとに隠せる (`accessories.txt`、描画だけ止める)
- 設定は `RusK\data\model\assignments.txt`。ステージ移動などでキャラが作り直されても付け直す
- **Model > ModelLab** はデバッグ用 (モデルの作りの書き出し・キャラ同士の見た目の入れ替え・切り抜き方式の比較)

### Custom Model（PMX）
- `RusK\models` に PMX (MMD のモデル、2.0 / 2.1) を**テクスチャのフォルダごと**置くと、VRM と同じ一覧に出る。PMD は読めない (PMX エディタで変換する)
- `Vrm/Pmx/PmxLoader` が PMX を VRM と同じ形 (`VrmModel`) に組み立てるので、付ける・動きを写す・影・切り抜きは VRM と同じ仕組み
  - 座標: PMX も Unity も左手系。MMD のモデルは -Z を向いているので Y 軸で 180 度回す (x, z を反転)。1 = 8cm
  - 骨: MMD の標準の名前 (上半身・左腕・左ひじ・左足D・左親指０ など、全角の数字も可) で人型の骨に対応させる。
    「腰」が無ければ下半身を腰にして上半身をその子にする (腰を動かしたときに上半身も付いてくるように)
  - 基準の姿勢: MMD は A ポーズなので、骨を複製して腕を水平にした T ポーズを `VrmModel.RestOverride` に入れる (骨そのものは A ポーズのまま)
  - 付与 (足D・肩C・腕捩1 など、ほかの骨の回転を写す骨) は `VrmModel.AfterPose` で毎フレーム計算する。人型の骨とその上の骨は対象外
  - 物理: 「物理」の剛体の骨を VRM の揺れ物 (SpringBone) に、「ボーン追従」の剛体を当たり判定にする (当たらないグループの設定も使う)
  - 材質: 色 = 環境色 + 拡散色 × 0.6 (MMD の標準のライト)。透明度のあるテクスチャは切り抜き、不透明度 0 の材質 (材質モーフで出すもの) は描かない。
    スフィア・トゥーンは使わない。テクスチャは PNG・JPG (Unity)、BMP・TGA (`ImageDecoder`)。DDS は読めない
  - 表情: まばたき・ウィンク・あいうえお・笑い・にこり・怒り・困る・びっくり (グループモーフは中の頂点モーフに分ける)。使うモーフだけブレンドシェイプにする

### Custom Item Model（武器・装飾品）
- `RusK\props` に `.glb` か `.pmx` (テクスチャのフォルダごと) を置き、**Visual > CustomItemModel** で装備を選んでから選ぶ。位置・回転・大きさ・発光を調整できる
- キャラ専用: `assignments.txt` の行の頭を `キャラの番号:装備の番号` にすると、そのキャラが持つときだけ置き換える。ファイルを書き換えると自動で読み直す
- 材質の元は元の武器のトゥーンシェーダーの材質だけを使う (武器を出す瞬間は溶けて現れる演出の材質に差し替わっている)
- ゲームの武器・装飾品は、どれも装備 ID ごとの `Equip_<ID>` で、差し込み口 `WeaponHolder_0～4` に付く
  (武器は WeaponController 付きで、しまうとゲームの置き場に戻る。装飾品は WeaponHolder.m_equipGb から探す)。
  `Equip_<ID>` の下に glb を付け、元の見た目は `forceRenderingOff` で隠す
- 発光: glb の emissive (KHR_materials_emissive_strength 対応) と、画面の「光らせる」(色・強さ)
- 設定は `RusK\data\itemmodel\assignments.txt`

### Custom Motion（動き）
- `RusK\motions` に glb (Blender などで作ったアニメーション) か vrma (VRM のアニメーション) を置き、**Visual > CustomMotion > OpenWindow**
  - 動きを押すと、その場で再生 (操作キャラと画面に見せるキャラ)。止めるとフェードで戻る
  - 「このキャラの動作」から動作 (Idle・攻撃など) を選んでから動きを押すと割り当て (`RusK\data\motion\bindings.txt`)。その動作のときに自動で再生
- 作り方は [tools/blender/README.md](tools/blender/README.md)。ゲームの骨格は **Model > ModelLab** の「骨格を glb で書き出す」(バインドポーズの骨格・メッシュ・テクスチャ)。
  ゲームのメッシュは読み取り禁止なので、GPU の頂点バッファを `GraphicsBuffer.InternalGetData` で読み戻す (GetData はゲームから削られている)
- 仕組み:
  - ゲームの骨格で作った動き: 骨を名前 (パス) で対応させ、glb の骨が基準の姿勢から回った分 (根元の空間) をゲームの骨のバインドポーズに掛ける。
    上書きするのはキーの値が動いている骨だけで、子はゲームの動きのまま付いていく (一部だけの動きも自然)
  - ほかの骨格 (VRMA の対応表・Mixamo の `mixamorig:*`・VRoid の `J_Bip_*`): 人型の骨の役割で対応させ、両方を腕を水平にした T ポーズにそろえて写す。
    背骨の数が違うときは一番先の背骨どうし。腰は上下だけ写す (前後左右の踏み込みはゲームがキャラごと動かす)
  - お供の骨 (`UpArmTwist` / `ForeTwist` などのねじれの骨、骨の線のすぐ近くの兄弟の枝の骨) も、付いていく骨と同じだけ回す (袖が伸びないように)
  - 上書きはアニメーションの後・Custom Model が VRM に写す前 (`CameraController` / `CinemachineBrain` の LateUpdate、優先度 First)。始めと終わりは 0.25 秒で混ぜる
  - 割り当て: ループする動作は自分の時計。1 回きりの動作は `ActionAnimController.GetCurAnimNormalizedTime` に合わせ、
    攻撃はゲームの攻撃判定の瞬間 (`MotionState.animEvent` の最初の `AttackBoxOn` の進み具合) に動きの当たる瞬間を合わせる
    (それまでは伸び縮み、その後は実際の速さ)。当たる瞬間は自動 (手が腰から見て一番速く動く時間) か、画面のスライダーで決める
- ゲームの動作のデータ: `PlayerController.m_motionMgr` (`MotionManager`) の `motions` (`MotionState`: `bindAnimClip` は Animancer の `ClipTransition`、
  `attackBoxes`・`animEvent`・`comboName`・`playSpeed` など)。再生は Animancer (`ActionAnimController.m_anim`)、切り替えは `MotionController.ChangeMotion`

### Custom Character（新しいキャラ枠）
- `RusK\characters\<フォルダ>\character.json` (`{"id": 9001, "base": 1006, "name": {"ja": ..., "en": ..., "zh": ...}}`)。id は 9000 以上
- 仕組み:
  - キャラの一覧 (`CharacterContainer.characters`) に、土台の `MotionManager` を複製して id・名前を変えたものを足す (毎フレーム確かめる)
  - セーブ (`GameSave`) には解放・装備・スキルをメモリの中だけ足し、保存 (`SaveGame` / `GameSaveBackUp`) の間だけ抜く
    (`GetGameSave` / `GetCharacterContainer` を Harmony で書き換えると、別のスレッドから呼ばれて落ちるので触らない)
  - `Resources.Load` の `ActionSettingRes/MotionList_<id>` は複製を、ほかの `_<id>` は土台のものを渡す。`PlayerController.SetData` の前に `m_id` を直す。
    `ResourceManager.CharacterIdToTag` にも足す (無いとリーダーで出撃したときに読み込みが終わらない)
  - キャラを並べる画面 (`WindowCharacterShow`) のカードは数が決まっているので、土台のカードを複製して `SetBindId` で足す
  - 名前は `GameUtil.GetLocale("ActorName_<id>")` を差し替え
- 絵: ゲームが `〈種類〉_<id>` の絵を読むとき、土台の絵を表示の枠 (rect) 全体で `images_template` に書き出し、`images\〈種類〉.png` があれば差し替える。
  撮影 (`captures`) と合成は [tools/character/make_card_art.py](tools/character/make_card_art.py)
- 動作の一覧はキャラのフォルダの `motions.txt` (名前・クリップ・秒・攻撃判定)。開発用の指示ファイル `RusK\data\character\dev.txt` (switch / motion / bones / capture)

### Camera View（視点）
- **Visual > CameraView** の View で「近い肩越し / 真後ろ / 一人称 / カスタム」を選ぶ
- 一人称の目の高さはキャラごとに覚える。アクション `CycleView` をトリガーに割り当てると、キーで視点を順に切り替えられる

## RusK Check（Mod の診断）
メニューの **Mods > Check** で開く。ゲームの更新で壊れた Mod や、エラーが出ている Mod を教える。
- 読み込み時に Mod の全メソッドを JIT コンパイルさせ、**消えた / 形が変わったゲームの関数・型**を検出する
- `[HarmonyPatch]` のパッチ先が今のゲームに実在するかを確かめる
- モジュールの実行中のエラーや、OnLoad の失敗を Mod ごとに記録する
- ゲームの版 (`build_info.txt`) が前回から変わったら起動時に知らせる (上の検査で問題が無ければ「問題なし」として知らせる)
- Mod が動作確認した版と今のゲームの版が違うだけでは注意にしない。**上の検査で本当に壊れているものが見つかった Mod だけ**が ✗ になる
  (ゲームの更新のたびに全部の Mod を出し直さなくて済む。ただし、関数の形は同じで中身の動きだけが変わった場合は検出できない)

Mod 作者は、動作確認したゲームの版を書いておくと、Check の詳細に「確認済みゲーム」として表示される:
```csharp
[RuskMod("mymod", "My Mod", "1.0.0", GameVersion = "0.0.1873")]
```

## Flex Window（Mod 用ウィンドウ）
`RuskWindow` を継承して `Draw` に中身を書き、`Context.RegisterWindow(window)` で登録、`window.Visible = true` で開く。
ドラッグ・リサイズ・✕・スクロール・位置の保存・カーソル表示・クリックの貫通防止は RusK がやる。
```csharp
class MyWindow : RuskWindow
{
    float _hp = 100;
    public MyWindow() : base("main", "My Window", 400, 300) { }
    public override void Draw(WindowGui gui)
    {
        gui.Header("ステータス");
        _hp = gui.Number("hp", "HP", _hp, 10f);        // − 入力欄 +
        gui.BeginRow(1, 1);                              // 横並び
        if (gui.Button("回復")) { /* ... */ }
        if (gui.Button("閉じる")) Visible = false;
    }
}
```
部品: `Header` `Label` `Button` `Tabs` `Toggle` `Selectable` `Stepper`(◀▶) `Number`(直接入力可) `Slider` `BeginRow/EndRow` `Space` `Separator`。
色は `RuskStyle`（メニューのテーマ・ライティングと同じ色）を使う。

## Mod 同士の連携（RuskShared）
`RusK.API.RuskShared` は、Mod 同士で値をやり取りする共有の置き場。名前を付けて、どの Mod からも読み書きできる。
相手の Mod が入っていなくても動くように、読む側は既定値を渡す。名前は「Mod の ID.内容」の形にする。
```csharp
RuskShared.Set("camera.hideBody", true);                 // 書く (Camera View: 一人称で体を隠している)
bool hide = RuskShared.Get("camera.hideBody", false);    // 読む (Custom Model: VRM も隠す)
RuskShared.Changed += (key, value) => { /* 変わったとき */ };
```

## アクショントリガー
Mod は `Context.RegisterAction("名前", () => { ... }, "説明")` で 1 回きりの処理を登録できる。
登録したアクションと、各モジュールの ON/OFF がトリガーの発動先として選べる。
トリガーは Config プロファイルに保存される。

## 新しい Mod の作り方
1. `RusK.Mods.Extreme` をコピーして新プロジェクトにする（参照は `RusK.API` のみ）
2. `IRuskMod` を実装したクラスに `[RuskMod("id","名前","1.0.0")]` を付ける
3. `OnLoad` で `Context.RegisterModule(new YourModule())`
4. `Module` を継承して機能を書く:
   - `OnUpdate()` … 毎フレーム（有効な間）
   - `OnGUI()` … HUD 描画（`Render` を使う）
   - `OnEnable()/OnDisable()` … 切替時。Harmony パッチは `Context.Harmony.PatchAll(typeof(...))`
   - `AddSetting(new FloatSetting(...))` … ClickGUI に出る設定
   - モジュールを別の Mod に移したときは、コンストラクタで `MovedFrom("前のmodid:Name")` を呼ぶと、前の設定を引き継ぐ
   - `RuskInput.WasPressed(KeyCode.C)` / `RuskInput.WasPressed(hotkeySetting.Value)` … キー入力（メニュー操作中は自動で無視される）
5. ビルドして `RusK/mods/` に置く（`DeployDir` で自動配置）

## 着脱の仕組み（ハードコードしない設計）
- 各 Mod は独立した `AssemblyLoadContext` に読み込まれる
- DLL はメモリから読むのでゲーム起動中でも上書きできる（Reload 可能）
- アンロード時、RusK が自動で: モジュール登録解除 → `OnUnload()` → `Context.Harmony.UnpatchSelf()`
- **重要**: Mod 側では独自の MonoBehaviour を作らないこと。IL2CPP に登録した型は
  後から解放できないため。Update/描画は RusK が Module 経由で配る
- `Loader/CollectibleMods = true` にするとアセンブリ本体もメモリから解放を試みる（実験的）

## 設定の保存
- モジュールの ON/OFF・キー割当・設定値・パネル位置・テーマを `configs/<profile>.json` に保存
- メニューを閉じたとき、ゲーム終了時に自動保存
- アンロード中の Mod の設定も保持され、再ロードで復元される

## 見た目のカスタマイズ（今後の拡張ポイント）
色・サイズは `RusK.Core/UI/Theme.cs` に集約。`configs/<profile>.json` の `Theme` で
アクセント色・レインボー・不透明度・各HUDの表示切替ができる。
「カスタムライティング」やスキンを足すときはここを起点にする。
