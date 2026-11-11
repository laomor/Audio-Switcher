// ============================================================
// NativeMemory.cs - 用 EmptyWorkingSet 收缩进程工作集
// 功能：把托盘程序已占用但暂时不用的物理页交还系统，压低常驻内存
// 设计理念：托盘程序长时间闲置后工作集虚高，一次 Win32 调用就能释放；失败也必须静默
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FortyOne.AudioSwitcher
{
    internal static class NativeMemory
    {
        // ===================== P/Invoke 声明 =====================
        // psapi.dll 的系统调用：把进程工作集里可分页的内存尽量换出  参数：进程句柄  返回：系统接受请求为 true
        // 对自己进程调用即可，无需额外权限；SetLastError 让失败时可以用 Marshal.GetLastWin32Error 追原因
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // ===================== 对外接口 =====================
        // 收缩当前进程的工作集  参数：无  返回：系统接受请求为 true；任何异常都返回 false
        // 用 Process.GetCurrentProcess 免去自己 OpenProcess 再关句柄的责任，using 会自动释放；
        // 失败不做任何提示：这只是顺手做的优化，没必要为它弹窗打扰用户
        public static bool TrimWorkingSet()
        {
            try
            {
                using (var p = Process.GetCurrentProcess())
                    return EmptyWorkingSet(p.Handle);
            }
            catch
            {
                return false;
            }
        }
    }
}
