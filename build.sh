#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "Building MyVoice..."
xcodebuild -project MyVoice.xcodeproj -scheme MyVoice -configuration Debug build 2>&1 | tail -3

APP_SRC=$(find ~/Library/Developer/Xcode/DerivedData/MyVoice-*/Build/Products/Debug -name "MyVoice.app" -maxdepth 1 -type d | head -1)

if [ -z "$APP_SRC" ]; then
    echo "ERROR: MyVoice.app not found in DerivedData"
    exit 1
fi

echo "Deploying to /Applications..."
pkill -x MyVoice 2>/dev/null || true
sleep 1
rm -rf /Applications/MyVoice.app
cp -R "$APP_SRC" /Applications/MyVoice.app

codesign -v /Applications/MyVoice.app 2>&1 && echo "Signature: OK" || echo "Signature: FAILED"
echo "Done. Launch from /Applications/MyVoice.app"
