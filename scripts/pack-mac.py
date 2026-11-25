import os
import zipfile

out_zip = r"D:\SquadEditor\dist\SquadEditor-macOS-arm64.zip"
bin_path = r"D:\SquadEditor\dist\v1.6.0-mac\SquadEditorCore"
icon_path = r"D:\SquadEditor\website\assets\logo.png"

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
"""

launcher = """#!/bin/bash
DIR="$(cd "$(dirname "$0")" && pwd)"
CORE="$DIR/SquadEditorCore"
/usr/bin/osascript -e "do shell script \\"'$CORE' >/dev/null 2>&1 &\\" with administrator privileges with prompt \\"SquadEditor needs administrator permission to read live Football Manager 27 memory.\\""
"""

def add_bytes(zf, arcname, data, executable=False):
    zi = zipfile.ZipInfo(arcname)
    zi.compress_type = zipfile.ZIP_DEFLATED
    zi.create_system = 3  # Unix
    zi.external_attr = (0o100755 if executable else 0o100644) << 16
    zf.writestr(zi, data)

if os.path.exists(out_zip):
    os.remove(out_zip)

with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
    add_bytes(zf, "SquadEditor.app/Contents/Info.plist", info_plist.encode("utf-8"), executable=False)
    add_bytes(zf, "SquadEditor.app/Contents/MacOS/SquadEditor", launcher.encode("utf-8"), executable=True)
    with open(bin_path, "rb") as f:
        add_bytes(zf, "SquadEditor.app/Contents/MacOS/SquadEditorCore", f.read(), executable=True)
    if os.path.exists(icon_path):
        with open(icon_path, "rb") as f:
            add_bytes(zf, "SquadEditor.app/Contents/Resources/AppIcon.png", f.read(), executable=False)

size_mb = os.path.getsize(out_zip) / (1024 * 1024)
print(f"Created {out_zip} ({size_mb:.2f} MB) with proper Unix +x permissions inside SquadEditor.app!")
