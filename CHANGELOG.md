# Changelog

## 0.1.0 — 2026-08-31

- Added selectable CPU, GPU, and Auto decoding/output modes.
- Added a versioned Windows x64 D3D11 native texture bridge based on VLC-Unity.
- Added D3D11VA evidence reporting without conflating output and decode paths.
- Added RTSP session rebuild on resume, focus recovery, first-frame timeout,
  frame stall, and LibVLC errors.
- Added pinned dependency setup, reproducible native builds, Windows Demo/Smoke
  builds, editor self-tests, and GitHub Actions native CI.
