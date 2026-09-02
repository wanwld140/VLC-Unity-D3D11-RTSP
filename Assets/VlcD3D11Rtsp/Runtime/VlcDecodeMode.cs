namespace VlcD3D11Rtsp
{
    /// <summary>Decoder/output path requested by the application.</summary>
    public enum VlcDecodeMode
    {
        Auto = 0,
        Cpu = 1,
        Gpu = 2,
    }

    /// <summary>Video path that actually produced the current first frame.</summary>
    public enum VlcActiveVideoPath
    {
        None = 0,
        CpuMemoryBuffer = 1,
        D3D11NativeTexture = 2,
        AndroidNativeTexture = 3,
    }
}
