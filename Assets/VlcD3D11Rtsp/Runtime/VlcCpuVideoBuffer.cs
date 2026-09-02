#if (UNITY_ANDROID && !UNITY_EDITOR) || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LibVLCSharp;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// CPU fallback path. LibVLC writes RV32 frames on its decoder thread;
    /// Unity copies only complete frames on the main thread.
    /// </summary>
    internal sealed class VlcCpuVideoBuffer : IDisposable
    {
        private readonly SemaphoreSlim bufferGate = new SemaphoreSlim(1, 1);
        private readonly byte[] rv32Chroma = Encoding.ASCII.GetBytes("RV32");

        private IntPtr nativeBuffer;
        private byte[] managedPixels;
        private uint width;
        private uint height;
        private uint pitch;
        private int displayedFrameVersion;
        private int copiedFrameVersion;
        private int decoderOwnsGate;
        private int formatCallbackCount;
        private int lockCallbackCount;
        private int unlockCallbackCount;
        private int displayCallbackCount;
        private bool disposed;

        internal MediaPlayer.LibVLCVideoLockCb LockCallback => OnVideoLock;
        internal MediaPlayer.LibVLCVideoUnlockCb UnlockCallback => OnVideoUnlock;
        internal MediaPlayer.LibVLCVideoDisplayCb DisplayCallback => OnVideoDisplay;
        internal MediaPlayer.LibVLCVideoFormatCb FormatCallback => OnVideoFormat;
        internal string LastError { get; private set; }
        internal string Diagnostics =>
            "format=" + Volatile.Read(ref formatCallbackCount) +
            ", lock=" + Volatile.Read(ref lockCallbackCount) +
            ", unlock=" + Volatile.Read(ref unlockCallbackCount) +
            ", display=" + Volatile.Read(ref displayCallbackCount);

        private uint OnVideoFormat(
            ref IntPtr opaque,
            IntPtr chroma,
            ref uint requestedWidth,
            ref uint requestedHeight,
            ref uint pitches,
            ref uint lines)
        {
            Interlocked.Increment(ref formatCallbackCount);
            if (disposed || requestedWidth == 0 || requestedHeight == 0) return 0;

            try
            {
                uint rowBytes = checked(requestedWidth * 4u);
                uint alignedPitch = checked((rowBytes + 31u) & ~31u);
                long nativeByteCount = checked((long)alignedPitch * requestedHeight);
                if (nativeByteCount > int.MaxValue)
                {
                    LastError = "CPU video frame is too large to allocate.";
                    return 0;
                }

                bufferGate.Wait();
                try
                {
                    ReleaseNativeBufferWithoutLock();
                    nativeBuffer = Marshal.AllocHGlobal((int)nativeByteCount);
                    width = requestedWidth;
                    height = requestedHeight;
                    pitch = alignedPitch;
                    managedPixels = new byte[checked((int)(requestedWidth * requestedHeight * 4u))];
                    Interlocked.Exchange(ref displayedFrameVersion, 0);
                    copiedFrameVersion = 0;
                    LastError = null;
                }
                finally
                {
                    bufferGate.Release();
                }

                Marshal.Copy(rv32Chroma, 0, chroma, rv32Chroma.Length);
                pitches = alignedPitch;
                lines = requestedHeight;
                return 1;
            }
            catch (Exception exception)
            {
                // Never throw through a native callback boundary.
                LastError = "CPU video buffer initialization failed: " +
                            exception.GetType().Name + ".";
                return 0;
            }
        }

        private IntPtr OnVideoLock(IntPtr opaque, IntPtr planes)
        {
            Interlocked.Increment(ref lockCallbackCount);
            if (disposed) return IntPtr.Zero;

            bufferGate.Wait();
            if (disposed || nativeBuffer == IntPtr.Zero)
            {
                bufferGate.Release();
                return IntPtr.Zero;
            }

            Interlocked.Exchange(ref decoderOwnsGate, 1);
            Marshal.WriteIntPtr(planes, nativeBuffer);
            return nativeBuffer;
        }

        private void OnVideoUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            Interlocked.Increment(ref unlockCallbackCount);
            if (Interlocked.Exchange(ref decoderOwnsGate, 0) == 1)
                bufferGate.Release();
        }

        private void OnVideoDisplay(IntPtr opaque, IntPtr picture)
        {
            Interlocked.Increment(ref displayCallbackCount);
            Interlocked.Increment(ref displayedFrameVersion);
        }

        internal bool TryCopyLatestFrame(
            out byte[] pixels,
            out int frameWidth,
            out int frameHeight)
        {
            pixels = null;
            frameWidth = 0;
            frameHeight = 0;

            int availableVersion = Volatile.Read(ref displayedFrameVersion);
            if (disposed || availableVersion == copiedFrameVersion ||
                !bufferGate.Wait(0)) return false;

            try
            {
                availableVersion = Volatile.Read(ref displayedFrameVersion);
                if (nativeBuffer == IntPtr.Zero || managedPixels == null ||
                    width == 0 || height == 0 ||
                    availableVersion == copiedFrameVersion) return false;

                int rowBytes = checked((int)width * 4);
                int sourcePitch = checked((int)pitch);
                int rows = checked((int)height);

                if (sourcePitch == rowBytes)
                {
                    Marshal.Copy(nativeBuffer, managedPixels, 0,
                        checked(rowBytes * rows));
                }
                else
                {
                    for (int row = 0; row < rows; row++)
                    {
                        Marshal.Copy(
                            IntPtr.Add(nativeBuffer, checked(row * sourcePitch)),
                            managedPixels,
                            checked(row * rowBytes),
                            rowBytes);
                    }
                }

                // This pinned LibVLC 4 build returns RV32 as ARGB bytes. The
                // first byte is not guaranteed, so force it opaque.
                for (int index = 0; index < managedPixels.Length; index += 4)
                    managedPixels[index] = byte.MaxValue;

                copiedFrameVersion = availableVersion;
                pixels = managedPixels;
                frameWidth = checked((int)width);
                frameHeight = checked((int)height);
                return true;
            }
            finally
            {
                bufferGate.Release();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            bufferGate.Wait();
            try
            {
                ReleaseNativeBufferWithoutLock();
            }
            finally
            {
                bufferGate.Release();
                bufferGate.Dispose();
            }
        }

        private void ReleaseNativeBufferWithoutLock()
        {
            if (nativeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(nativeBuffer);
                nativeBuffer = IntPtr.Zero;
            }

            managedPixels = null;
            width = 0;
            height = 0;
            pitch = 0;
        }
    }
}
#endif
