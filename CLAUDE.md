# Original3DAction プロジェクトのルール

## 会話の進め方(並列の会話)

作者は本体のフォルダで `claude` を 3 つほど直接開き、それぞれに別のイシューを頼む。開いた会話は `.claude/settings.json` の `agent` で係(`.claude/agents/kakari.md`)として動く。決まりの要点:

- ファイルを変える作業になったら、最初の編集より前にイシュー名(短い英語)を決め、手元の main から `git worktree add -b feature/<名前> .claude/worktrees/<名前> main` でワークツリーを作り、`EnterWorktree`(`path` で既存のワークツリーに入る)で入って、その中だけを編集する。本体のチェックアウトへの Edit/Write はフックが止める。`claude --worktree`・`EnterWorktree` の新規作成・Agent の `isolation: "worktree"` は使わない(origin/main から作られ、LFS がポインタになる)。
- 本体のチェックアウトはふだん main にしておく。
- Unity エディターは開くときに名前を付ける。Unity Hub からではなく `python3 -I Tools/open_unity.py <名前> [--project <フォルダ>]` で開く(Dock のアイコンの名前が「Unity <名前>」になる)。同時に開くのは 2 つまで。開いているものは `--list` で見る。
- 1 つ目は本体を開き、本体のチェックアウトは 1 つなので、Play・Unity からの保存・本体のブランチ切り替え・本体への main のマージの間は `unity.lock`。2 つ目は自分のワークツリーを開くもので、開いて使う間は `unity2.lock`。main へのマージの間は `merge.lock`。どれも `Tools/session_lock.py` で取る。
- main へのマージは各会話が自分で行う(main を取り込む → 進捗表 → `--no-ff` で main へ → push → 片付け)。GitHub の PR は使わない。作業ブランチは main に入れるまで push しない。
- 確認は変更の大きさで選ぶ。小さな変更は `python3 -I Tools/compile_check.py`(Unity を使わないコンパイル確認)だけ。
- 窓口役(`madoguchi`)は `claude --agent madoguchi` で開いたときだけの統括役。

担当(サブエージェント)の一覧と振り方は `.claude/agents/kakari.md` の 2。

| 担当 | 役目 |
|---|---|
| `vision-keeper` | 企画番。決定事項と未決事項を見分け、古い方針や電脳斬禅に流用済みの設定を持ち込んでいないか確かめる(読むだけ) |
| `combat-designer` | 剣・回避・ガード・敵AI・武装の手触りと数値 |
| `blender-artist` | メカ・武器のモデル、リグ、アニメーション |
| `playtester` | Unity で動かして確かめる(直さない) |
| `unity-reviewer` | ロジックの大きな変更のレビュー(直さない) |
| `integrator` | マージの衝突の解決・ブランチの片付け |
| `progress-keeper` | 進捗表 `Docs/PROGRESS.md` |

## 企画の資料

- 現在の企画方針は `Docs/VISION.md` にまとめる。作者が方針を決めたり変えたりしたら、その会話で企画書も更新する(作者の言葉は「」で、Claude の提案とは分けて書く)。
- 2026-11 から企画を土台から見直している。`Docs/GameConcept.md`・`Docs/現時点仕様まとめ.md`・`Docs/WorldSetting.md` は古い方針で、仕様の根拠にしない。
- 試作(`Assets/Prototype/`)の形は、作者が試すために作ったもので、仕様として確定ではない。
- `Assets/Prototype/FiveSlot/`(5 枠版。名前空間 `PrototypeFiveSlot`)は、武装を作り直す前のプロトタイプを比較用に凍結した複製(2026-11-21)。直さない。プロトタイプへの変更は本線(`Assets/Prototype/` 直下の `Scripts/`・`Editor/`・`PrototypeStage.unity`)にだけ入れる。5 枠版は `Assets/Prototype/HuntRelease/` のコード(`HuntProto`)・プレハブ・マテリアルと `Models/` を本線と共用しているので、そこを変えたら 5 枠版もコンパイルが通ることを `compile_check.py` で確かめる。
- 企画に関わる判断は `vision-keeper` で決定事項を確かめ、方向性が決まる部分は作者に聞いてから進める。

## 速度の単位

- 企画・仕様書・検証報告・ゲーム画面では、機体や敵の移動速度、飛行速度、弾速、衝撃波の伝播速度など、速度をすべて `km/h` で表す。距離は `m`、時間は秒、連射の頻度は発/秒で表す。
- Unity の物理計算と Inspector の入力には、各値の定義単位を使う。`m/s` の値を表示する場合は、3.6 を掛けて `km/h` に換算する。

## コミットとpushのタイミング

- 実装は作業ごとの専用ワークツリーで行う。作業が終わったら、作者の作業後確認を待たずにコミット・main へのマージ・push まで進めてよい(作者の許可済み)。作者が保留や確認を指定した作業は、その指定に従う。
- 作業途中のコミットはワークツリーのブランチにローカルで積み、毎回 push しない。push するのは main へのマージ後の main だけ(作者の許可済み。force push はしない)。push 後は `git ls-remote origin main` で手元と同じことを確かめ、報告にマージのコミットを書く。
- コミットにはその作業に関係する差分だけを含める。本体に別作業の未コミット変更があっても、編集・stash・checkout・reset をしない。
- コミットメッセージはこのリポジトリの流儀で、`feat：…`・`fix：…`・`chore：…` のように全角コロンのあとに日本語で書く。

## Unity でのテスト

### テスト中だけ消音する
- 自動テスト・動作確認は、Play Mode を開始する前から一時的に消音する。
- 実行前の `EditorUtility.audioMasterMute` を控えて `true` にし、テスト終了後は成功・失敗・タイムアウト・中断を問わず、`try/finally` などで Play Mode を停止してから元に戻す。戻ったことも確かめる。
- ゲームの既定音量や OS 全体の音量は変えない。

### テスト中だけ Game ビューを Play Unfocused にする
- Play に入ると Game ビューがフォーカスされ、Unity のウィンドウが前に出てきて作者の作業を邪魔する。Play Mode を開始する前に Game ビューのプレイ時の挙動(`PlayModeView.enterPlayModeBehavior`)の元の値を控え、`PlayUnfocused` にする。終わったら消音と同じく、必ず元の値に戻して確かめる。
- テスト中に Game ビューへ `Focus()` しない。`EditorWindow.GetWindow` は既定でフォーカスするので、`GetWindow(type, false, null, false)` のように `focus` を `false` で呼ぶ。
- 切り替えのコード(`unity command --project-path <パス> script eval --code` に渡す。戻すときは `"PlayUnfocused"` を控えた値にする):

  ```csharp
  var t = System.Type.GetType("UnityEditor.PlayModeView,UnityEditor");
  var p = t.GetProperty("enterPlayModeBehavior", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
  var before = "";
  foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll(t)) { before = p.GetValue(w).ToString(); p.SetValue(w, System.Enum.Parse(p.PropertyType, "PlayUnfocused")); }
  return before;
  ```

## 作者の好み

- 画面を揺らさない(作者が 3D 酔いする)。カメラシェイクは入れないか、既定でオフにする。
