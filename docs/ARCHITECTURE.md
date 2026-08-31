# Architecture

## GPU path

1. Managed code selects `NativeGpu` immediately before constructing a player.
2. `libvlc_unity_media_player_new` creates the VLC-Unity renderer and registers
   LibVLC 4 `libvlc_video_set_output_callbacks` for D3D11.
3. LibVLC renders into native D3D11 textures owned by the bridge.
4. `libvlc_unity_get_texture` returns the current `ID3D11Texture2D` pointer.
5. Unity wraps that pointer with `Texture2D.CreateExternalTexture` and only calls
   `UpdateExternalTexture` if the pointer changes.

No managed pixel buffer, `ReadPixels`, `LoadRawTextureData`, or `Apply` call is
present in the GPU update path.

## CPU path

The same native constructor consumes `CpuCallbacks` and creates a normal LibVLC
media player without an output renderer. Managed RV32 callbacks write to an
unmanaged buffer on the decoder thread. Unity copies only completed frames on
the main thread and uploads them to an ARGB32 texture.

## Auto and recovery

Auto begins with the GPU path. Failure to create a valid D3D11 renderer, a
LibVLC playback error before the first frame, or a first-frame timeout causes a
single documented fallback to CPU. Subsequent failures rebuild the chosen
session with exponential backoff. A manual restart or application resume resets
Auto to its preferred GPU-first behavior.

Every rebuild releases the media player, queues renderer retirement work on the
Unity render thread, disposes callback buffers, destroys the Unity texture, and
then constructs a fresh media session. This is intentional for RTSP recovery:
the old socket and decoder state are never trusted after suspend or a stall.
