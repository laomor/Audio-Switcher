// ============================================================
// Enums.cs - 全局热键的修饰键标志集合
// 功能：定义 Alt / Ctrl / Shift / Win 四个修饰键的位标志，供热键注册与界面显示使用
// 设计理念：取值刻意对齐 Win32 的 MOD_* 常量，强转成 int 就能直接喂给 RegisterHotKey
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;

namespace FortyOne.AudioSwitcher.HotKeyData
{
    // ===================== 修饰键标志 =====================
    // [Flags] 允许按位或出组合键（如 Alt|Control）；具体数值必须与 Win32 的 MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN 一致
    [Flags]
    public enum Modifiers
    {
        None = 0,     // 无修饰键，等价于只按主键
        Alt = 1,      // Alt 键，对应 MOD_ALT = 0x0001
        Control = 2,  // Ctrl 键，对应 MOD_CONTROL = 0x0002
        Shift = 4,    // Shift 键，对应 MOD_SHIFT = 0x0004
        Win = 8       // Win 键，对应 MOD_WIN = 0x0008
    }
}
