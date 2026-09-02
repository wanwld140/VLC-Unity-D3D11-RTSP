# Architecture

## Windows GPU path

1. Managed code selects `NativeGpu` immediately before constructing a player.
2. `libvlc_unity_media_player_new` creates the VLC-Unity renderer and registers
   LibVLC 4 `libvlc_video_set_output_callbacks` for D3D11.
3. LibVLC renders into native D3D11 textures owned by the bridge.
4. `libvlc_unity_get_texture` returns the current `ID3D11Texture2D` pointer.
5. Unity wraps that pointer with `Texture2D.CreateExternalTexture` and only calls
   `UpdateExternalTexture` if the pointer changes.

No managed pixel buffer, `ReadPixels`, `LoadRawTextureData`, or `Apply` call is
present in the GPU update path.

## Android GPU path

1. `VLCAndroidInitialization` sends the render-device initialization event to
   `libVLCUnityPlugin.so` before playback.
2. LibVLCSharp creates the media player from the ARM64 `libvlc.so` runtime.
3. Vulkan uses `libvlc_unity_set_unity_texture_vulkan`; OpenGL ES uses the
   native texture pointer returned by the VLC-Unity bridge.
4. Unity wraps or binds the native `Texture2D`, then performs a GPU
   `Graphics.Blit` into the `RawImage` display `RenderTexture`.

`AndroidNativeTexture` proves the Unity output path, not the decoder selected
inside LibVLC. Android hardware decode remains unconfirmed until device logs or
platform decoder evidence are captured.

## CPU path

On Windows the native constructor consumes `CpuCallbacks`; on Android the
standard LibVLC media player is configured with the same callbacks. Managed
RV32 callbacks write to an unmanaged buffer on the decoder thread. Unity copies
only completed frames on the main thread and uploads them to an ARGB32 texture.

## Auto and recovery

Auto begins with the platform-native texture path. Failure to create a valid
D3D11/Android renderer, a LibVLC playback error before the first frame, or a
first-frame timeout causes a single documented fallback to CPU. Subsequent
failures rebuild the chosen session with exponential backoff. A manual restart
or application resume resets Auto to its preferred GPU-first behavior.

Every rebuild releases the media player, performs the platform renderer cleanup,
disposes callback buffers, destroys Unity textures, and then constructs a fresh
media session. This is intentional for RTSP recovery: the old socket and decoder
state are never trusted after suspend or a stall.
