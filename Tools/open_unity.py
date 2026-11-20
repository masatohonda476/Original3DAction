#!/usr/bin/env python3
"""Unity エディターに名前を付けて開く(作者の決まり: 開くときに命名する。同時に 2 つまで)。

Dock のアイコンにカーソルを合わせたときの名前は、アプリのファイル名から来る。
そこで Unity.app を「Unity <名前>.app」として同じフォルダに APFS で複製し(容量はほぼ増えない)、
そのアプリからプロジェクトを開く。Unity Hub から開くと「Unity」のままになるので、Hub は使わない。

使い方:
  python3 -I Tools/open_unity.py 作業用                  # このワークツリー(または本体)を「Unity 作業用」で開く
  python3 -I Tools/open_unity.py デバッグ用 --project <フォルダ>
  python3 -I Tools/open_unity.py --list                  # 開いている Unity と名前
  python3 -I Tools/open_unity.py 作業用 --dry-run        # 開かずに、何をするかだけ出す

  python3 -I Tools/open_unity.py 作業用 --scene Assets/…/X.unity   # 初めて開くときのシーンを指定

初めて開くフォルダ(作ったばかりのワークツリー)は「最後に開いていたシーン」の記録がなく、Unity が名前なしの
空のシーンを開く(作者「また空です」)。そこで、記録がないときだけ、開く前にステージのシーン(DEFAULT_SCENE。
--scene で替えられる)を記録に書いておく。記録があるフォルダは、前に開いていたシーンのまま開く。

止めるとき(終了コード 2): Unity がすでに 2 つ開いている / 同じプロジェクトが開いている / 同じ名前が使われている。
"""
import argparse
import os
import re
import subprocess
import sys

MAX_EDITORS = 2
HUB_EDITORS = "/Applications/Unity/Hub/Editor"
# 初めて開くフォルダで開くシーン(いま試しているステージ)
DEFAULT_SCENE = "Assets/Prototype/PrototypeStage.unity"
# Unity が「最後に開いていたシーン」を覚えるファイル(起動のときに読んで開き直す)
LAST_SCENES = os.path.join("Library", "LastSceneManagerSetup.txt")


def first_scene(project, scene):
    """初めて開くフォルダで開くシーンを返す。記録がもうある・シーンのファイルがないなら ""(何もしない)。"""
    if os.path.isfile(os.path.join(project, LAST_SCENES)):
        return ""
    scene = (scene or DEFAULT_SCENE).strip().replace(os.sep, "/")
    return scene if os.path.isfile(os.path.join(project, scene)) else ""


def write_first_scene(project, scene):
    """Unity が起動のときに scene を開くよう、「最後に開いていたシーン」の記録を書く(Unity が書くのと同じ形)。"""
    path = os.path.join(project, LAST_SCENES)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(f"sceneSetups:\n- path: {scene}\n  isLoaded: 1\n  isActive: 1\n  isSubScene: 0\n")


def git_root(path):
    r = subprocess.run(["git", "-C", path, "rev-parse", "--show-toplevel"],
                       capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def unity_version(project):
    p = os.path.join(project, "ProjectSettings", "ProjectVersion.txt")
    with open(p, encoding="utf-8") as f:
        m = re.search(r"^m_EditorVersion:\s*(\S+)", f.read(), re.M)
    if not m:
        sys.exit(f"{p} から Unity の版が読めません。")
    return m.group(1)


def running_editors():
    """開いている Unity エディター(取り込み用の裏の Unity は除く)を [(名前, プロジェクト, pid)] で返す。"""
    out = subprocess.run(["ps", "-axww", "-o", "pid=,command="],
                         capture_output=True, text=True).stdout
    found = []
    for line in out.splitlines():
        pid, _, cmd = line.strip().partition(" ")
        # Unity Hub(…/MacOS/Unity Hub)は除き、エディター本体(…/MacOS/Unity のあとに引数)だけ数える
        m = re.search(r"/Editor/[^/]+/(Unity[^/]*)\.app/Contents/MacOS/Unity(\s+-|$)", cmd)
        if not m or re.search(r"-batch[mM]ode|AssetImportWorker", cmd):
            continue
        name = m.group(1)
        name = name[len("Unity "):] if name.startswith("Unity ") else "(名前なし)"
        pm = re.search(r"-project[pP]ath\s+(.+?)(\s+-\w|$)", cmd)
        proj = os.path.realpath(pm.group(1)) if pm else "?"
        found.append((name, proj, pid))
    return found


def main():
    ap = argparse.ArgumentParser(description="Unity エディターに名前を付けて開く(同時に 2 つまで)")
    ap.add_argument("name", nargs="?", help="Dock に出す名前(例: 作業用、デバッグ用)")
    ap.add_argument("--project", help="開くプロジェクトのフォルダ(省くとこのワークツリー/本体)")
    ap.add_argument("--list", action="store_true", help="開いている Unity と名前を出す")
    ap.add_argument("--dry-run", action="store_true", help="開かずに、何をするかだけ出す")
    ap.add_argument("--scene", help=f"初めて開くフォルダで開くシーン(省くと {DEFAULT_SCENE})。前に開いたことのあるフォルダでは使わない")
    a = ap.parse_args()

    editors = running_editors()
    if a.list or not a.name:
        if not editors:
            print("開いている Unity はありません。")
        for name, proj, pid in editors:
            print(f"Unity {name}\t{proj}\t(pid {pid})")
        if not a.name and not a.list:
            print("\n名前を付けて開く: python3 -I Tools/open_unity.py <名前> [--project <フォルダ>]")
        return 0

    name = a.name.strip()
    if not name or "/" in name or ":" in name or len(name) > 20:
        sys.exit("名前は 1〜20 文字で、/ と : は使えません。")

    project = os.path.realpath(a.project or git_root(os.getcwd()) or os.getcwd())
    if not os.path.isfile(os.path.join(project, "ProjectSettings", "ProjectVersion.txt")):
        sys.exit(f"Unity のプロジェクトではありません: {project}")

    for ename, proj, pid in editors:
        if proj == project:
            print(f"このプロジェクトは「Unity {ename}」(pid {pid})で開いています: {project}")
            return 2
        if ename == name:
            print(f"「Unity {name}」はもう使われています({proj})。別の名前にしてください。")
            return 2
    if len(editors) >= MAX_EDITORS:
        print(f"Unity はすでに {len(editors)} つ開いています(同時に {MAX_EDITORS} つまで)。どれかを閉じてから開いてください。")
        for ename, proj, pid in editors:
            print(f"  Unity {ename}\t{proj}")
        return 2

    ver = unity_version(project)
    vdir = os.path.join(HUB_EDITORS, ver)
    src = os.path.join(vdir, "Unity.app")
    if not os.path.isdir(src):
        sys.exit(f"Unity {ver} が見つかりません: {src}(Unity Hub で入れてください)")
    # 複製は元と同じフォルダに置く(PlaybackEngines など、隣のフォルダを Unity が使うため)
    app = os.path.join(vdir, f"Unity {name}.app")

    scene = first_scene(project, a.scene)
    if a.scene and not scene:
        print(f"--scene は使いません(前に開いたことのあるフォルダか、シーンが見つかりません): {a.scene}")

    if a.dry_run:
        print(("作る: " if not os.path.isdir(app) else "使う: ") + app)
        print(f"開く: {project}")
        print(f"シーン: {scene}(初めて開くフォルダ)" if scene else "シーン: 前に開いていたまま")
        return 0

    if not os.path.isdir(app):
        print(f"「Unity {name}.app」を作ります(APFS の複製なので容量はほぼ増えません)…")
        subprocess.run(["cp", "-cR", src, app], check=True)
    if scene:
        write_first_scene(project, scene)
        print(f"初めて開くフォルダなので、シーン {scene} を開くようにしました。")
    subprocess.run(["open", "-n", "-a", app, "--args", "-projectPath", project], check=True)
    print(f"「Unity {name}」で開きました: {project}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
