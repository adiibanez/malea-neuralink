# Unity CI/CD Setup Guide

This document describes how to configure GitHub Actions to build the Unity project.

## Prerequisites

- Unity Hub installed locally
- Unity 6 (6000.x) installed
- Docker installed (for license activation)
- GitHub repository with admin access

## Required GitHub Secrets

The build workflow requires the following secrets configured in your repository:

| Secret | Description |
|--------|-------------|
| `UNITY_LICENSE` | Contents of your Unity license file (`.ulf`) |
| `UNITY_EMAIL` | Unity account email address |
| `UNITY_PASSWORD` | Unity account password |

## Setup Steps

### 1. Generate a License Activation File

Run the following command to generate a `.alf` (activation license file):

```bash
docker run -it --rm \
  -v "$(pwd)/license":/output \
  unityci/editor:ubuntu-6000.0.33f1-base-3 \
  /bin/bash -c "unity-editor -batchmode -createManualActivationFile -logFile /dev/stdout && mv Unity_v*.alf /output/"

docker run -it --rm \
  -v "$(pwd)/license":/output \
  unityci/editor \
  /bin/bash -c "unity-editor -batchmode -createManualActivationFile -logFile /dev/stdout && mv Unity_v*.alf /output/"



```

This creates a `Unity_v6000.x.alf` file in a `license/` directory.

### 2. Activate the License

1. Go to [Unity Manual Activation](https://license.unity.com/manual)
2. Upload the `.alf` file generated in step 1
3. Follow the prompts to activate (select Personal or Pro based on your license)
4. Download the resulting `.ulf` license file

### 3. Configure GitHub Secrets

1. Navigate to your GitHub repository
2. Go to **Settings** > **Secrets and variables** > **Actions**
3. Click **New repository secret** and add:

**UNITY_LICENSE**
- Copy the entire contents of the `.ulf` file
- Paste as the secret value

**UNITY_EMAIL**
- Your Unity account email address

**UNITY_PASSWORD**
- Your Unity account password

### 4. Verify the Setup

Create a tag to trigger the build and release:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Then:
1. Go to the **Actions** tab in your repository
2. Check that the "Build Unity macOS" workflow runs successfully
3. Once complete, find the release under **Releases** with the macOS build attached

## Creating a Release

The workflow triggers only on version tags. To create a new release:

```bash
# Create and push a tag
git tag v1.0.0
git push origin v1.0.0
```

This will:
1. Build the Unity project for macOS
2. Create a GitHub Release named after the tag
3. Attach `neura-steer-macos-v1.0.0.zip` to the release
4. Auto-generate release notes from commits since the last tag

### Tag Naming Convention

Use semantic versioning with a `v` prefix:
- `v1.0.0` - Major release
- `v1.1.0` - Minor release (new features)
- `v1.0.1` - Patch release (bug fixes)
- `v1.0.0-beta.1` - Pre-release

## Troubleshooting

### License Expired

Unity licenses expire periodically. If builds fail with license errors:
1. Repeat steps 1-3 to generate and upload a new license
2. Update the `UNITY_LICENSE` secret with the new `.ulf` contents

### Unity Version Mismatch

If the build fails due to version mismatch:
1. Check the Unity version in `steer-chair/ProjectSettings/ProjectVersion.txt`
2. Ensure the Docker image tag matches (e.g., `6000.0.33f1`)

### Cache Issues

If builds fail unexpectedly after project changes:
1. Go to **Actions** > **Caches** in your repository
2. Delete the `Library-StandaloneOSX-*` cache entries
3. Re-run the workflow

## Additional Resources

- [GameCI Documentation](https://game.ci/docs)
- [Unity Manual Activation](https://license.unity.com/manual)
- [game-ci/unity-builder Action](https://github.com/game-ci/unity-builder)
