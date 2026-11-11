// ============================================================
// NativeMethods.cs - 热键相关 Win32 API 的 P/Invoke 声明
// 功能：集中声明原子表与全局热键的四个原生函数，供热键消息窗口调用
// 设计理念：原生签名集中一处便于核对参数含义；SetLastError 打开后失败原因可回读，而不是只看 bool 返回值
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Runtime.InteropServices;

namespace FortyOne.AudioSwitcher.HotKeyData
{
    internal static class NativeMethods
    {
        // ===================== 系统 DLL 名称 =====================
        private const string KERNEL32 = "kernel32";  // 原子表（GlobalAddAtom 等）所在 DLL
        private const string USER32 = "user32";      // 热键（RegisterHotKey 等）所在 DLL

        // ===================== 原子表接口 =====================
        // 把一个字符串登记进系统全局原子表  参数：lpString 原子名称（本项目用 GUID 保证唯一）  返回：原子号（16 位，0 表示失败）
        // CharSet.Auto 让运行库自动选 A/W 版本；SetLastError 让失败时能用 Marshal.GetLastWin32Error 取错误码
        [DllImport(KERNEL32, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern short GlobalAddAtom(string lpString);

        // 释放一个原子号  参数：nAtom 要释放的原子号  返回：恒为 0，不能用返回值判断成败
        // 与 GlobalAddAtom 必须成对调用，否则原子号会一直占着系统原子表；确需判断失败可在调用前后配合 SetLastError / GetLastError
        [DllImport(KERNEL32)]
        public static extern short GlobalDeleteAtom(short nAtom);

        // ===================== 全局热键接口 =====================
        // 注册一个系统级热键  参数：hWnd 接收 WM_HOTKEY 的窗口；id 热键标识；fsModifiers 修饰键位组合；vk 主键虚拟键码  返回：true 表示成功
        // id 用原子号充当，按下时 WM_HOTKEY 的 wParam 会原样带回这个 id
        [DllImport(USER32, SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        // 注销已注册的热键  参数：hWnd 注册时用的窗口；id 注册时用的标识  返回：true 表示成功
        // 注册失败后系统并未占用该热键，此时调用返回 false 属于正常情况，调用方可以忽略返回值
        [DllImport(USER32, SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    }
}
