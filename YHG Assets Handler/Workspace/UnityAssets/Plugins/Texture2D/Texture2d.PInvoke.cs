using AssetsHandler.Core;
using System;
using System.Runtime.InteropServices;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Texture2D
{
    public static partial class PInvoke
    {
        private const string DllName = "TexToolHelper";

        static PInvoke()
        {
            DllLoader.Load("TexToolHelper");
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallback(float progress, [MarshalAs(UnmanagedType.LPStr)] string status);

        [Flags]
        public enum EncodeFlags : int
        {
            None = 0,
            InputFloat = 1,   // RGBA32F input
            SrgbMips = 2      // sRGB color-space correct mip downsampling
        }

        #region Unified Decoder
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool DecodeTextureNative(
            IntPtr inputData,
            UIntPtr dataLength,
            int width,
            int height,
            int format,
            IntPtr outputRgba,
            ProgressCallback? onProgress
        );
        #endregion

        #region Unified Encoder
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint GetEncodedSizeNative(
            int format,
            int width,
            int height,
            int mipCount
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint EncodeTextureNative(
            IntPtr inputPixels,
            int width,
            int height,
            int format,
            int quality,
            int mipCount,
            int flags,
            out int outMipCount,
            out int outHandle,
            ProgressCallback? onProgress
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool PickUpAndFree(
            IntPtr outBuf,
            uint size,
            int id
        );
        #endregion
    }
}