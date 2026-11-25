import os
import zipfile

pub_dir = r"D:\SquadEditor\dist\avalonia-osx-arm64"
out_zip = r"D:\SquadEditor\dist\SquadEditor-macOS-arm64.zip"
icon_candidates = [
    r"D:\SquadEditor\website\assets\logo.png",
    r"D:\SquadEditor\src\SquadEditor.App\Assets\logo.png",
]
icon_path = next((p for p in icon_candidates if os.path.exists(p)), icon_candidates[0])

info_plist = """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SquadEditor</string>
  <key>CFBundleDisplayName</key><string>SquadEditor</string>
  <key>CFBundleIdentifier</key><string>dev.squadeditor.mac</string>
  <key>CFBundleVersion</key><string>1.6.0</string>
  <key>CFBundleShortVersionString</key><string>1.6.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>SquadEditor</string>
  <key>CFBundleIconFile</key><string>AppIcon.png</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
""".replace("\r\n", "\n")

launcher = """#!/bin/bash
DIR="$(cd "$(dirname "$0")" && pwd)"
CORE="$DIR/SquadEditorCore"
USER_HOME="$HOME"
/usr/bin/osascript -e "do shell script \\"SQUADEDITOR_USER_HOME='$USER_HOME' '$CORE' >/dev/null 2>&1 &\\" with administrator privileges with prompt \\"SquadEditor needs administrator permission to read live Football Manager 27 memory.\\""
""".replace("\r\n", "\n")


def add_bytes(zf, arcname, data, executable=False):
    zi = zipfile.ZipInfo(arcname)
    zi.compress_type = zipfile.ZIP_DEFLATED
    zi.create_system = 3  # Unix
    zi.external_attr = (0o100755 if executable else 0o100644) << 16
    zf.writestr(zi, data)


if not os.path.isdir(pub_dir):
    raise FileNotFoundError(f"Publish directory not found: {pub_dir}")

if os.path.exists(out_zip):
    os.remove(out_zip)

file_count = 0
with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
    add_bytes(zf, "SquadEditor.app/Contents/Info.plist", info_plist.encode("utf-8"), executable=False)
    add_bytes(zf, "SquadEditor.app/Contents/MacOS/SquadEditor", launcher.encode("utf-8"), executable=True)
    file_count += 2

    core_found = False
    for root, _, files in os.walk(pub_dir):
        for name in sorted(files):
            full_path = os.path.join(root, name)
            rel = os.path.relpath(full_path, pub_dir).replace("\\", "/")
            if rel == "SquadEditor":
                arcname = "SquadEditor.app/Contents/MacOS/SquadEditorCore"
                executable = True
                core_found = True
            else:
                arcname = f"SquadEditor.app/Contents/MacOS/{rel}"
                executable = rel == "createdump" or rel.endswith(".dylib") or rel.endswith(".so")
            with open(full_path, "rb") as f:
                add_bytes(zf, arcname, f.read(), executable=executable)
            file_count += 1

    if not core_found:
        raise FileNotFoundError(f"Main executable 'SquadEditor' not found in {pub_dir}")

    if os.path.exists(icon_path):
        with open(icon_path, "rb") as f:
            add_bytes(zf, "SquadEditor.app/Contents/Resources/AppIcon.png", f.read(), executable=False)
        file_count += 1

size_mb = os.path.getsize(out_zip) / (1024 * 1024)
print(f"Created {out_zip} ({size_mb:.2f} MB, {file_count} entries) with proper Unix +x permissions inside SquadEditor.app!")
