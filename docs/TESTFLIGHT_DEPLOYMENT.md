# TestFlight Deployment Guide

## Overview

This guide covers distributing NeuraSteer to testers via Apple TestFlight on macOS. The app communicates with hardware via USB serial port, which requires specific sandbox entitlements.

## Prerequisites

- Apple Developer Program membership ($99/year)
- Xcode command line tools installed
- Access to [Apple Developer Portal](https://developer.apple.com/account) and [App Store Connect](https://appstoreconnect.apple.com)

## One-Time Setup

### 1. Register App ID

1. Go to https://developer.apple.com/account > Certificates, Identifiers & Profiles
2. Identifiers > + > App IDs
3. Bundle ID: `com.gammaludic.neurasteer` (Explicit)
4. No capabilities need to be checked
5. Continue > Register

### 2. Create Signing Certificates

You need two certificates:

**Mac App Distribution** (signs the .app):
1. Certificates > + > Mac App Distribution
2. Open Keychain Access > Certificate Assistant > Request a Certificate from a CA
3. Save CSR to disk, upload to portal
4. Download `.cer`, double-click to install

**Mac Installer Distribution** (signs the .pkg):
1. Certificates > + > Mac Installer Distribution
2. Same CSR process
3. Download `.cer`, double-click to install

Verify installation:
```bash
security find-identity -v -p macappstore
# Should show:
#   "3rd Party Mac Developer Application: Gammaludic (7D9Z44N844)"
#   "3rd Party Mac Developer Installer: Gammaludic (7D9Z44N844)"
```

### 3. Create Provisioning Profile

1. Profiles > + > Mac App Store Connect
2. Select App ID: `com.gammaludic.neurasteer`
3. Select Mac App Distribution certificate
4. Name it (e.g., "NeuraSteer Mac App Store")
5. Download the `.provisionprofile`

### 4. Set Up App Store Connect

1. Go to https://appstoreconnect.apple.com
2. My Apps > + > New App
   - Platform: macOS
   - Name: NeuraSteer
   - Bundle ID: `com.gammaludic.neurasteer`
   - SKU: `neurasteer`
3. TestFlight tab > Internal Testing > + > create group, add tester emails (up to 100)

### 5. Create API Key (for CI uploads)

1. Users and Access > Integrations > App Store Connect API
2. \+ > Name: "CI Upload", Role: "App Manager"
3. Download the `.p8` key file (only downloadable once!)
4. Note the Key ID and Issuer ID

## Manual Build & Upload

### Build in Unity

1. File > Build Profiles > macOS
2. Architecture: Apple Silicon + Intel (Universal)
3. Build to `build/`

### Sign and Package

```bash
# Set your variables
APP="build/neura-steer.app"
ENT="steer-chair/neura-steer.entitlements"
PROFILE="path/to/downloaded.provisionprofile"
APP_CERT="3rd Party Mac Developer Application: Gammaludic (7D9Z44N844)"
PKG_CERT="3rd Party Mac Developer Installer: Gammaludic (7D9Z44N844)"

# Embed provisioning profile
cp "$PROFILE" "$APP/Contents/embedded.provisionprofile"

# Sign nested code first
find "$APP/Contents/Frameworks" -name "*.dylib" -exec \
  codesign --force --sign "$APP_CERT" --entitlements "$ENT" \
  --timestamp --options runtime {} \;

find "$APP/Contents/Plugins" -name "*.bundle" -exec \
  codesign --force --sign "$APP_CERT" --entitlements "$ENT" \
  --timestamp --options runtime {} \;

# Sign main app
codesign --force --deep --sign "$APP_CERT" \
  --entitlements "$ENT" --timestamp --options runtime "$APP"

# Verify
codesign --verify --verbose=4 "$APP"
codesign --display --entitlements :- "$APP"  # confirm sandbox entries

# Package as .pkg (required for TestFlight upload)
productbuild --component "$APP" /Applications \
  --sign "$PKG_CERT" "neura-steer.pkg"
```

### Upload

**Option A — Transporter app** (easiest):
1. Download Transporter from Mac App Store
2. Sign in with your Apple ID
3. Drag `neura-steer.pkg` into Transporter
4. Click Deliver

**Option B — CLI:**
```bash
xcrun altool --upload-app --file "neura-steer.pkg" --type macos \
  --apiKey YOUR_KEY_ID --apiIssuer YOUR_ISSUER_ID
```

### Enable for Testing

1. Wait 5-30 minutes for processing in App Store Connect
2. Go to TestFlight tab, click the new build
3. Click "Missing Compliance" > answer "No" (no custom encryption)
4. Add build to your Internal Testing group
5. Testers receive an email to install via the TestFlight app on macOS

## CI Automation

The `.github/workflows/build-testflight.yml` workflow automates the entire process.

### GitHub Secrets Required

| Secret | How to get it |
|--------|---------------|
| `UNITY_LICENSE` | Existing — Unity license activation |
| `UNITY_EMAIL` | Existing — Unity account email |
| `UNITY_PASSWORD` | Existing — Unity account password |
| `APPLE_CERTIFICATE_P12` | Export both certs from Keychain Access as .p12, then `base64 -i certs.p12` |
| `APPLE_CERTIFICATE_PASSWORD` | Password you set during .p12 export |
| `APPLE_PROVISIONING_PROFILE` | `base64 -i profile.provisionprofile` |
| `APP_STORE_CONNECT_API_KEY_ID` | From App Store Connect > Integrations |
| `APP_STORE_CONNECT_API_ISSUER_ID` | From App Store Connect > Integrations |
| `APP_STORE_CONNECT_API_PRIVATE_KEY` | `base64 -i AuthKey_XXXX.p8` |

### Exporting Certificates as .p12

1. Open Keychain Access
2. Select both "3rd Party Mac Developer Application" and "3rd Party Mac Developer Installer" certificates
3. File > Export Items > save as `certs.p12` with a strong password
4. Base64 encode: `base64 -i certs.p12 | pbcopy`
5. Paste into GitHub secret `APPLE_CERTIFICATE_P12`

### Triggering a Build

```bash
# Tag and push to trigger the workflow
git tag tf-v0.1.0
git push origin tf-v0.1.0
```

The workflow will:
1. Build the Unity project on a macOS runner
2. Compute build number from git history
3. Import certificates into a temporary keychain
4. Sign the app with entitlements
5. Package as `.pkg`
6. Upload to App Store Connect via `xcrun altool`
7. Clean up the temporary keychain

### Build Numbers

Build numbers are auto-computed as `git rev-list --count HEAD`, ensuring each upload has a unique, incrementing build number. No manual tracking needed.

## Entitlements

The entitlements file (`steer-chair/neura-steer.entitlements`) enables:

| Entitlement | Purpose |
|------------|---------|
| `com.apple.security.app-sandbox` | Required for TestFlight/App Store |
| `com.apple.security.device.usb` | USB device access |
| `com.apple.security.device.serial` | Serial device access |
| `temporary-exception.files.absolute-path.read-write` `/dev/` | POSIX-level access to `/dev/cu.usbmodem*` |
| `cs.allow-unsigned-executable-memory` | Required for Unity IL2CPP runtime |
| `cs.disable-library-validation` | Required for Unity plugin loading |

## Log Files

### Audit Log

The app writes a CSV audit log of all serial commands, state changes, relay activations, and safety events. Location:

**Normal (unsigned) build:**
```
~/Library/Application Support/Gammaludic/neura-steer/AuditLogs/audit_<timestamp>.csv
```

**Sandboxed (TestFlight) build:**
```
~/Library/Containers/com.gammaludic.neurasteer/Data/Library/Application Support/Gammaludic/neura-steer/AuditLogs/audit_<timestamp>.csv
```

CSV columns: `Timestamp, Category, Message, Command, Success`

Categories: `SerialCommand`, `Override`, `Connection`, `StateChange`, `Relay`, `Macro`, `Application`, `Safety`

A new file is created per session. No additional entitlement is needed — `Application.persistentDataPath` is inside the sandbox container.

### Unity Player Log

**Normal build:**
```
~/Library/Logs/Gammaludic/neura-steer/Player.log
```

**Sandboxed (TestFlight) build:**
```
~/Library/Containers/com.gammaludic.neurasteer/Data/Library/Logs/Gammaludic/neura-steer/Player.log
```

## Troubleshooting

### App crashes on launch after signing
- Ensure the Hardened Runtime entitlements (`allow-unsigned-executable-memory`, `disable-library-validation`) are present
- Check Console.app for crash details

### Serial port not working under sandbox
- Verify entitlements are embedded: `codesign --display --entitlements :- path/to/app`
- Look for the `/dev/` temporary exception in the output

### Upload rejected by App Store Connect
- Ensure bundle ID matches exactly: `com.gammaludic.neurasteer`
- Ensure build number is higher than any previously uploaded build
- Check that the provisioning profile matches the signing certificate and bundle ID

### "Missing Compliance" won't go away
- Click the build in TestFlight > answer the encryption question
- NeuraSteer does not use custom encryption, so answer "No"
