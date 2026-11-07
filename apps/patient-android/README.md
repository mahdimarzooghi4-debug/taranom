# Taranom patient Android preview

Installable **offline demonstration**, not the connected production app. Package
`ir.taranom.patient.preview`, version `0.1.0-preview` (1), Android 8/API 26+ with
an up-to-date Android System WebView. Target/compile API 35. Uses the existing
Taranom logo and a persistent Persian preview banner. No iOS project is created.

The current member web bundle is included in the APK, starting at `/patient-app`.
The connected home fails closed without a session; remaining preview routes retain their synthetic labels. Protected account/appointment/support
routes require the real host session and therefore report it missing. There is
no fake bearer session, OTP, identity verification, membership or clinical data.
There is deliberately no INTERNET or other sensitive Android permission, native
JavaScript bridge, file/content access, cookie acceptance, backup or WebView
inspection. Local resources are intercepted at Android's reserved app-assets
HTTPS origin; unknown/external requests are denied, API paths return 503, and
known SPA routes map to the bundled index. No remote server URL is invented.
A connected Android release needs a separate reviewed hosting/auth integration.

## Build

Prerequisites: Node 22+, existing member npm dependencies, JDK 17, Android
platform 35 revision 2, Build Tools 35.0.0, Python 3 and zip. Platform/tool ZIPs
were verified against the checksums published in Google's repository metadata.

```sh
export TARANOM_ANDROID_SDK=/absolute/path/to/android-sdk
export TARANOM_ANDROID_PREVIEW_KEYSTORE=/private/path/taranom-preview.keystore
apps/patient-android/build-preview.sh
node apps/patient-android/verify-preview.mjs
```

The optional TARANOM_ECJ_JAR fallback supports Eclipse ECJ 3.38.0 when only a JRE
is available (SHA256 97c566b120009c203a2fc8b291f4a9adbc171cf1ccb70f06f6b4e1828c00ce8e).
Keep the preview keystore outside the repository and reuse it for compatible
preview updates. The script creates a local development signer with the standard
public development password; it is never a production/store signing identity.

Output: `build/taranom-patient-android-preview.apk`. Generated packages and signing
keys are excluded from git. Deliver the APK separately; do not commit it.

Validation: member production build, Java-to-dex compilation, aapt package
metadata/no requested permissions, zip alignment, APK v2/v3 signature verification,
exact comparison between packaged assets and current web output, and Chromium
render/navigation checks for bundled home, logo, account and support. The browser
check is **not Android device/emulator testing**. Actual installation and WebView
behavior on a phone remain unverified until a device run is performed.
