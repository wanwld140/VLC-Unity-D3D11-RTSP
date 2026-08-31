# Contributing

Use Unity `2021.3.28f1c1` on Windows x64 with the Direct3D 11 graphics API.

1. Run `scripts/setup-dependencies.ps1`.
2. Run `scripts/build-native.ps1`.
3. Open the project once or run `scripts/build-unity-player.ps1`.
4. Run `scripts/run-editor-tests.ps1` and `scripts/verify-repository.ps1` before
   submitting a change.

Keep RTSP credentials out of scenes, logs, reports, commits, and issues. Use the
`VLC_RTSP_TEST_URL` environment variable for acceptance tests. Changes to the
native ABI must increment `libvlc_unity_bridge_api_version` and update the
managed expected version in the same commit.
