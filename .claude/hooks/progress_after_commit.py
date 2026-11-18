#!/usr/bin/env python3
"""PostToolUse(Bash) フック: git commit が通った直後に、まとめ役(progress-keeper)を呼ぶよう Claude に伝える。

- コミットが本当にできたか(HEAD が今さっき作られたか)で判定する。失敗したコミットでは何もしない。
- Docs/PROGRESS.md だけのコミット(進捗表の更新そのもの)では呼ばない(無限に回らないように)。
- 作業ブランチ(main 以外)のコミットでは呼ばない。並行して開いた会話が同じ表を書き換えて衝突しないよう、
  作業ブランチの進捗表は main へのマージの手順の中で 1 回だけ更新する(.claude/agents/kakari.md)。
"""
import json
import subprocess
import sys
import time


def git(cwd, *args):
    r = subprocess.run(["git", "-C", cwd, *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        return
    cmd = str((data.get("tool_input") or {}).get("command") or "")
    if "git" not in cmd or "commit" not in cmd:
        return
    cwd = data.get("cwd") or "."
    head_time = git(cwd, "log", "-1", "--format=%ct")
    if not head_time or time.time() - int(head_time) > 120:
        return
    files = git(cwd, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").splitlines()
    if files and all(f == "Docs/PROGRESS.md" for f in files):
        return
    branch = git(cwd, "branch", "--show-current")
    if branch != "main":
        return
    top = git(cwd, "rev-parse", "--show-toplevel")
    head = git(cwd, "log", "-1", "--format=%h %s")
    msg = (
        f"[進捗フック] コミットが入りました: {head} (ブランチ {branch}, 場所 {top})。"
        "Agent ツールで subagent_type \"progress-keeper\" を run_in_background で起動し、"
        f"Docs/PROGRESS.md の更新を任せてください。プロンプトには、このコミットの場所 {top}、"
        f"会話ログのパス {data.get('transcript_path', '')}、いま取り組んでいる作業の一言を入れる。"
        "結果は待たずに作業を続け、完了通知が来たら変更点を一言だけ報告する。"
        "あなたがサブエージェントで Agent ツールを持たない場合は、この案内は無視してよい。"
    )
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": msg}},
                     ensure_ascii=False))


if __name__ == "__main__":
    main()
