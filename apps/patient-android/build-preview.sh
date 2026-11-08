#!/usr/bin/env bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/../.." && pwd)"
: "${TARANOM_ANDROID_SDK:?Point TARANOM_ANDROID_SDK to the Android SDK root}"
tools="$TARANOM_ANDROID_SDK/build-tools/35.0.0"
platform="$TARANOM_ANDROID_SDK/platforms/android-35/android.jar"
output="$project_root/apps/patient-android/build"
mkdir -p "$output/classes" "$output/dex" "$output/assets/web"
npm run build --prefix "$project_root/apps/member-web"
# Stage only the current web output; prevent stale bundle files from entering the APK.
python3 - "$project_root/apps/member-web/dist" "$output/assets/web" <<'PY'
import shutil,sys
shutil.rmtree(sys.argv[2])
shutil.copytree(sys.argv[1],sys.argv[2])
PY
"$tools/aapt" package -f -M "$project_root/apps/patient-android/AndroidManifest.xml" -S "$project_root/apps/patient-android/res" -A "$output/assets" -I "$platform" -F "$output/resources.apk"
if command -v javac >/dev/null; then
 javac --release 8 -encoding UTF-8 -classpath "$platform" -d "$output/classes" "$project_root/apps/patient-android/src/ir/taranom/patient/preview/MainActivity.java"
else
 : "${TARANOM_ECJ_JAR:?Install JDK 17 or set TARANOM_ECJ_JAR to Eclipse ECJ 3.38.0}"
 java -jar "$TARANOM_ECJ_JAR" -source 8 -target 8 -encoding UTF-8 -bootclasspath "$platform" -classpath "$platform" -d "$output/classes" "$project_root/apps/patient-android/src/ir/taranom/patient/preview/MainActivity.java"
fi
find "$output/classes" -name '*.class' -print0 | xargs -0 "$tools/d8" --lib "$platform" --min-api 26 --output "$output/dex"
cp "$output/resources.apk" "$output/unsigned.apk"
(cd "$output/dex" && zip -q -u "$output/unsigned.apk" classes.dex)
"$tools/zipalign" -f -p 4 "$output/unsigned.apk" "$output/aligned.apk"
# Preview-only signer stored outside git; never use this for a production release.
: "${TARANOM_ANDROID_PREVIEW_KEYSTORE:?Set an external preview signing keystore path}"
if [[ ! -f "$TARANOM_ANDROID_PREVIEW_KEYSTORE" ]]; then
 keytool -genkeypair -keystore "$TARANOM_ANDROID_PREVIEW_KEYSTORE" -storepass android -keypass android -alias preview -keyalg RSA -keysize 2048 -validity 3650 -dname 'CN=Taranom Preview, O=Taranom, C=IR'
fi
"$tools/apksigner" sign --ks "$TARANOM_ANDROID_PREVIEW_KEYSTORE" --ks-pass pass:android --key-pass pass:android --ks-key-alias preview --out "$output/taranom-patient-android-preview.apk" "$output/aligned.apk"
"$tools/apksigner" verify --verbose "$output/taranom-patient-android-preview.apk"
"$tools/zipalign" -c -v 4 "$output/taranom-patient-android-preview.apk"
