using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AssetsHandler.Core
{
    public static partial class NativeInterop
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    }
}
