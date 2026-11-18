#!/usr/bin/env python3
"""並行して開いた会話どうしで、1 つしかない資源の順番を取るためのロック。

ロックは本体のチェックアウトの .claude/locks/<種類>.lock(git 管理外)に置く。
どのワークツリーから実行しても、本体の同じ場所を見る。

種類:
  unity  … 本体を開いた Unity エディタと本体のチェックアウト(Play・Unity からの保存・本体のブランチ切り替え)
  unity2 … 2 つ目の Unity エディタ(自分のワークツリーを開く。Unity は同時に 2 つまで)
  merge  … main へのマージ(main を取り込む → main に入れる → push まで)

使い方(会話のワークツリーの中で):
  python3 -I Tools/session_lock.py acquire unity --name <会話名> [--note "何をするか"]
  python3 -I Tools/session_lock.py release unity --name <会話名>
  python3 -I Tools/session_lock.py status
  python3 -I Tools/session_lock.py wait unity [--timeout 1800]   # 空くまで待つ(Bash の run_in_background で)
  python3 -I Tools/session_lock.py release unity --force         # 古いロックを消す(作者に伝えてから)

終了コード: acquire は取れたら 0、他の会話が持っていたら 2(中身を表示)。
"""
import argparse
import json
import os
import subprocess
import sys
import time

STALE_HOURS = 2


def git(*args, cwd="."):
    r = subprocess.run(["git", "-C", cwd, *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def main_root():
    common = git("rev-parse", "--path-format=absolute", "--git-common-dir")
    if not common:
        sys.exit("git のリポジトリの中で実行してください。")
    return os.path.dirname(common)


def lock_path(kind):
    d = os.path.join(main_root(), ".claude", "locks")
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, f"{kind}.lock")


def read(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return None
    except Exception:
        return {"name": "?", "broken": True, "time": os.path.getmtime(path)}


def describe(kind, info):
    age_h = (time.time() - float(info.get("time", 0))) / 3600
    wt = info.get("worktree", "")
    stale = []
    if age_h > STALE_HOURS:
        stale.append(f"{age_h:.1f} 時間前から")
    if wt and not os.path.isdir(wt):
        stale.append("持ち主のワークツリーがもう無い")
    s = (f"{kind}.lock: 会話 {info.get('name')} / ブランチ {info.get('branch')} / "
         f"{time.strftime('%m-%d %H:%M', time.localtime(float(info.get('time', 0))))} から"
         f" / {info.get('note', '')}")
    if info.get("main_branch_before"):
        s += f" / 本体の元のブランチ {info['main_branch_before']}"
    if stale:
        s += f"  [古い可能性: {'、'.join(stale)}]"
    return s


def acquire(kind, name, note):
    path = lock_path(kind)
    info = {
        "name": name,
        "branch": git("branch", "--show-current") or git("rev-parse", "--short", "HEAD"),
        "worktree": git("rev-parse", "--show-toplevel"),
        "time": time.time(),
        "note": note or "",
        "main_branch_before": git("rev-parse", "--abbrev-ref", "HEAD", cwd=main_root()),
    }
    try:
        fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    except FileExistsError:
        cur = read(path)
        if cur and cur.get("name") == name:
            print(f"すでに自分が持っている: {describe(kind, cur)}")
            return 0
        print(f"使用中: {describe(kind, cur or {})}")
        return 2
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(info, f, ensure_ascii=False)
    print(f"取った: {describe(kind, info)}")
    return 0


def release(kind, name, force):
    path = lock_path(kind)
    cur = read(path)
    if cur is None:
        print(f"{kind}.lock は無い。")
        return 0
    if not force and cur.get("name") != name:
        print(f"自分のロックではないので消さない: {describe(kind, cur)}")
        return 2
    os.remove(path)
    print(f"消した: {describe(kind, cur)}")
    return 0


def status():
    d = os.path.join(main_root(), ".claude", "locks")
    found = False
    if os.path.isdir(d):
        for fn in sorted(os.listdir(d)):
            if fn.endswith(".lock"):
                found = True
                print(describe(fn[:-5], read(os.path.join(d, fn)) or {}))
    if not found:
        print("ロックなし。")
    print(f"本体のブランチ: {git('rev-parse', '--abbrev-ref', 'HEAD', cwd=main_root())}")
    return 0


def wait(kind, timeout):
    path = lock_path(kind)
    end = time.time() + timeout
    while os.path.exists(path):
        if time.time() > end:
            print(f"{timeout} 秒待っても空かない: {describe(kind, read(path) or {})}")
            return 2
        time.sleep(15)
    print(f"{kind}.lock が空いた。acquire で取ってから使う。")
    return 0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("cmd", choices=["acquire", "release", "status", "wait"])
    p.add_argument("kind", nargs="?", choices=["unity", "unity2", "merge"])
    p.add_argument("--name", default="")
    p.add_argument("--note", default="")
    p.add_argument("--force", action="store_true")
    p.add_argument("--timeout", type=int, default=1800)
    a = p.parse_args()
    if a.cmd == "status":
        sys.exit(status())
    if not a.kind:
        sys.exit("種類(unity・unity2・merge)を指定してください。")
    if a.cmd == "acquire":
        if not a.name:
            sys.exit("--name <会話名> を付けてください。")
        sys.exit(acquire(a.kind, a.name, a.note))
    if a.cmd == "release":
        if not a.name and not a.force:
            sys.exit("--name <会話名> を付けてください(古いロックを消すなら --force)。")
        sys.exit(release(a.kind, a.name, a.force))
    sys.exit(wait(a.kind, a.timeout))


if __name__ == "__main__":
    main()
