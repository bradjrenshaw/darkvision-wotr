"""Build the Wrath Access release artifacts.

Builds everything a GitHub release needs into release/ (the same layout as Say the Spire 2 and
Eliza Access):
  release/changes-latest.md        the release notes (copied from changes-latest.md at the repo root;
                                   also embedded in loadstone-release.json)
  release/assets/                  every uploadable asset:
  WrathAccess.zip          — the mod payload the installer downloads/extracts:
                              WrathAccess/ (the native-mod folder) + game/ (prism.dll, loadstar.dll)
  WrathAccessInstaller.exe  — the standalone installer: Loadstone (external/loadstone) built with the
                              single-mod profile installer/loadstone-profile.json baked in
  loadstone-release.json    — the release manifest Loadstone installs from: the install steps frozen
                              from loadstone-packages/wrath-access.json, the zip's SHA-256, and the
                              installer's hash + Loadstone version (so installers self-update)

usage: python scripts/release.py, then publish with the command it prints:
  gh release create vX.Y.Z --title "Wrath Access X.Y.Z" --notes-file release/changes-latest.md release/assets/*
(the tag is Version in OwlcatModificationManifest.json; Loadstone compares release tags by semver).
The installer build needs Visual Studio (C++ workload), CMake and Rust; the first build also
compiles a static wxWidgets once (cached under %LOCALAPPDATA%/loadstone).

The build runs at below-normal priority with cargo capped at CARGO_BUILD_JOBS=2 (unless already
set): the dev machine's CPU has crashed under sustained all-core compiles.
"""

import glob
import json
import os
import shutil
import subprocess
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RELEASE = os.path.join(REPO, "release")
ASSETS = os.path.join(RELEASE, "assets")
NOTES = os.path.join(REPO, "changes-latest.md")
LOADSTONE = os.path.join(REPO, "external", "loadstone")
STAGE = os.path.join(RELEASE, "stage")


def run(args, cwd=None):
    print(">", " ".join(args))
    subprocess.check_call(args, cwd=cwd or REPO)


def stage_payload():
    mod = os.path.join(STAGE, "WrathAccess")
    asm = os.path.join(mod, "Assemblies")
    game = os.path.join(STAGE, "game")
    os.makedirs(asm)
    os.makedirs(game)

    shutil.copy2(os.path.join(REPO, "OwlcatModificationManifest.json"), mod)
    shutil.copy2(os.path.join(REPO, "OwlcatModificationSettings.json"), mod)
    shutil.copy2(os.path.join(REPO, "bin", "Release", "WrathAccess.dll"), asm)
    # The hot-reloadable module (host/module split): byte-loaded by the host from Module\, not
    # Assemblies\. A payload without it boots the host with nothing to load — mod silent.
    module_dir = os.path.join(mod, "Module")
    os.makedirs(module_dir)
    shutil.copy2(os.path.join(REPO, "deploy", "Module", "WrathAccess.Module.dll"), module_dir)
    naudio = glob.glob(os.path.join(os.path.expanduser("~"), ".nuget", "packages",
                                    "naudio", "*", "lib", "net35", "NAudio.dll"))
    if not naudio:
        raise SystemExit("NAudio.dll not found in the NuGet cache — run a build first.")
    shutil.copy2(sorted(naudio)[-1], asm)
    shutil.copytree(os.path.join(REPO, "assets"), os.path.join(mod, "assets"))
    # Bundled documentation (built into deploy/docs by build_docs.ps1) ships inside the mod folder,
    # so Help > Read documentation opens it offline. Warn rather than fail if it hasn't been built.
    docs = os.path.join(REPO, "deploy", "docs")
    if os.path.isdir(docs):
        shutil.copytree(docs, os.path.join(mod, "docs"))
    else:
        print("WARNING: deploy/docs not found — run build_docs.ps1 to bundle the docs.")
    shutil.copy2(os.path.join(REPO, "vendor", "prism.dll"), game)
    shutil.copy2(os.path.join(REPO, "vendor", "loadstar.dll"), game)  # the low-latency audio backend


def make_zip():
    out = os.path.join(ASSETS, "WrathAccess.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _dirs, files in os.walk(STAGE):
            for f in files:
                full = os.path.join(root, f)
                z.write(full, os.path.relpath(full, STAGE))
    return out


def be_gentle():
    # Below-normal priority (child processes inherit it) and two cargo jobs: see the docstring.
    if os.name == "nt":
        import ctypes
        BELOW_NORMAL_PRIORITY_CLASS = 0x4000
        k32 = ctypes.windll.kernel32
        k32.SetPriorityClass(k32.GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS)
    os.environ.setdefault("CARGO_BUILD_JOBS", "2")


def main():
    if not os.path.isfile(NOTES):
        raise SystemExit("changes-latest.md not found at the repo root - write this release's notes first.")
    be_gentle()
    if os.path.isdir(RELEASE):
        shutil.rmtree(RELEASE)
    os.makedirs(ASSETS)
    shutil.copy2(NOTES, RELEASE)

    run(["dotnet", "build", "-c", "Release"])
    stage_payload()
    zip_path = make_zip()
    shutil.rmtree(STAGE)

    if not os.path.isfile(os.path.join(LOADSTONE, "Cargo.toml")):
        run(["git", "submodule", "update", "--init", "external/loadstone"])

    # The branded installer: Loadstone's own Windows build (static wxWidgets + static C runtime, so
    # no Visual C++ Redistributable needed) with our single-mod profile embedded.
    run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
         os.path.join(LOADSTONE, "scripts", "build_windows_app.ps1"),
         "-ProfilePath", os.path.join(REPO, "installer", "loadstone-profile.json"),
         "-Name", "WrathAccessInstaller", "-Out", ASSETS])

    with open(os.path.join(REPO, "OwlcatModificationManifest.json"), encoding="utf-8-sig") as f:
        version = json.load(f)["Version"]

    # The release manifest: validates loadstone-packages/wrath-access.json and hashes the artifacts.
    run(["cargo", "build", "--release", "-p", "loadstone-cli"], cwd=LOADSTONE)
    package = [os.path.join(LOADSTONE, "target", "release", "loadstone.exe"), "package",
               "--manifest", os.path.join(REPO, "loadstone-packages", "wrath-access.json"),
               "--version", "v" + version,
               "--artifact", zip_path,
               "--installer", os.path.join(ASSETS, "WrathAccessInstaller.exe"),
               "--notes-file", os.path.join(RELEASE, "changes-latest.md"),
               "--out", os.path.join(ASSETS, "loadstone-release.json")]
    run(package)

    print("\nDone. Release folder: release/ (notes: changes-latest.md, assets: assets/)")
    print('Publish with: gh release create v%s --title "Wrath Access %s" '
          "--notes-file release/changes-latest.md release/assets/*" % (version, version))


if __name__ == "__main__":
    main()
