#!/usr/bin/env python3
"""PreToolUse(Edit/Write/NotebookEdit) フック: 本体のチェックアウトへの直接の編集を止める。

並行して開いた会話は、最初の依頼でワークツリー(.claude/worktrees/<名前>)を作り、その中だけを編集する決まり
(.claude/agents/kakari.md)。ワークツリーを作る前や、ワークツリーのつもりで本体のファイルを書こうとしたら止める。

止めないもの:
- ワークツリー(.claude/worktrees/ の下)、リポジトリの外(memory・scratchpad など)、git が無視するファイル
- 窓口役(madoguchi)として動く会話
- 正当に本体を編集する会話: 本体の .claude/locks/main-edit-<session_id> があれば通す
  (Bash は止めないので、コミット・マージ・ロックの操作には影響しない)
"""
import json
import os
import subprocess
import sys


def git(cwd, *args):
    try:
        r = subprocess.run(["git", "-C", cwd, *args], capture_output=True, text=True, timeout=5)
    except Exception:
        return None
    return r


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        return
    if str(data.get("agent_type") or "") == "madoguchi":
        return
    ti = data.get("tool_input") or {}
    fp = ti.get("file_path") or ti.get("notebook_path")
    if not fp:
        return
    cwd = data.get("cwd") or os.getcwd()
    target = os.path.realpath(os.path.join(cwd, os.path.expanduser(fp)))

    root = None
    for base in (os.path.dirname(target), cwd, os.environ.get("CLAUDE_PROJECT_DIR", "")):
        d = base
        while d and not os.path.isdir(d):
            d = os.path.dirname(d)
        if not d:
            continue
        r = git(d, "rev-parse", "--path-format=absolute", "--git-common-dir")
        if r is not None and r.returncode == 0 and r.stdout.strip():
            root = os.path.dirname(os.path.realpath(r.stdout.strip()))
            break
    if not root:
        return
    if not os.path.isfile(os.path.join(root, ".claude", "agents", "kakari.md")) and \
            not os.path.isdir(os.path.join(root, ".claude", "worktrees")):
        return  # このプロジェクトの決まりではない場所

    if not target.startswith(root + os.sep):
        return  # リポジトリの外
    if target.startswith(os.path.join(root, ".claude", "worktrees") + os.sep):
        return  # ワークツリーの中
    sid = str(data.get("session_id") or "")
    marker = os.path.join(root, ".claude", "locks", f"main-edit-{sid}")
    if sid and os.path.exists(marker):
        return
    rel = os.path.relpath(target, root)
    r = git(root, "check-ignore", "-q", "--no-index", rel)
    if r is not None and r.returncode == 0:
        return  # git が無視するファイル(Screenshots/ など)

    reason = (
        f"[本体の保護] {rel} は本体のチェックアウト({root})のファイルです。"
        "この会話はイシューのワークツリーの中だけで編集する決まりです(.claude/agents/kakari.md)。"
        "まだワークツリーを作っていなければ、イシュー名を決めて "
        "`git worktree add -b feature/<名前> .claude/worktrees/<名前> main` → EnterWorktree(path) で入り、"
        f"同じファイルを {root}/.claude/worktrees/<名前>/{rel} として編集し直してください。"
        "作者の指示などで本当に本体を編集する必要があるときだけ、理由を作者に伝えてから "
        f"`mkdir -p {root}/.claude/locks && touch {marker}` で通せます(終わったら消す)。"
    )
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}, ensure_ascii=False))


if __name__ == "__main__":
    main()
