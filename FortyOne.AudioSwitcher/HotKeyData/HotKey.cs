// ============================================================
// HotKey.cs - 单条全局热键：配置数据、消息窗口与注册生命周期
// 功能：保存热键的键码/修饰符/目标设备 GUID，并借一个隐藏消息窗口把它注册成系统级全局热键
// 设计理念：热键不挂在可见窗体上，而由 HWND_MESSAGE 消息窗口承接 WM_HOTKEY，主窗体全程不显示也能响应
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher.HotKeyData
{
    public class HotKey
    {
        // ===================== 状态与构造 =====================
        public bool IsRegistered;  // true 表示已注册，或组合键为空无需注册；false 表示注册失败

        // 构造一个尚未绑定组合键的热键对象  参数：无  返回：无（修饰键与主键都置为 None）
        // 先给合法默认值，设置界面新建条目时就不必再判断字段是否还停留在初始垃圾值
        public HotKey()
        {
            Modifiers = Modifiers.None;
            Key = Keys.None;
        }

        // ===================== 绑定设备与显示文本 =====================
        // 该热键切换的目标音频设备 GUID；循环切换热键不绑真实设备，用全 F 的哨兵 GUID 表示
        public Guid DeviceId { get; set; }

        // 判断是否为「循环切换设备」热键  参数：无  返回：true 表示绑定的正是循环切换哨兵 GUID
        public bool IsCycleHotKey
        {
            get { return DeviceId == HotKeyManager.CycleHotkeyDeviceId; }
        }

        // 组合键的可读文本  参数：无  返回：形如 "Alt+Ctrl+F5" 的显示字符串，供界面与失败提示使用
        // 修饰键固定按 Alt→Ctrl→Shift→Win 的顺序拼接，保证同一条热键每次显示都一致
        public string HotKeyString
        {
            get
            {
                // === 依次拼接修饰键前缀 ===
                var keystring = "";
                if ((Modifiers & Modifiers.Alt) > 0)
                    keystring += "Alt+";
                if ((Modifiers & Modifiers.Control) > 0)
                    keystring += "Ctrl+";
                if ((Modifiers & Modifiers.Shift) > 0)
                    keystring += "Shift+";
                if ((Modifiers & Modifiers.Win) > 0)
                    keystring += "Win+";
                // 最后拼主键名：Keys 的枚举名与键帽刻印基本一致（如 F5、D1），可以直接给人看
                keystring += Key.ToString();

                return keystring;
            }
        }

        // ===================== 消息窗口 =====================
        // 承接 WM_HOTKEY 的隐藏窗口；懒创建，注销或注册失败时置回 null，表示当前没有任何原生注册
        private HotKeyNativeWindow HotKeyWindow { get; set; }

        // ===================== 组合键配置 =====================
        // 修饰键位组合：Alt/Ctrl/Shift/Win 可按位叠加，转 int 后即 RegisterHotKey 的 fsModifiers
        public Modifiers Modifiers { get; set; }

        // 主键键码：Keys 的数值就是 Win32 虚拟键码；Keys.None 表示未绑定，注册时跳过系统调用
        public Keys Key { get; set; }

        // ===================== 对外事件 =====================
        // 热键被按下时触发；由消息窗口的窗口过程经 OnHotKey 转发，主程序订阅后执行设备切换
        public event EventHandler HotKeyPressed;

        // ===================== 注册与注销 =====================
        // 按当前配置注册为全局热键  参数：无  返回：true 表示注册成功或无需注册，false 表示注册失败
        // 失败时窗口会被释放并置 IsRegistered 为 false，调用方只看返回值即可
        public bool RegisterHotkey()
        {
            // === 懒创建消息窗口 ===
            if (HotKeyWindow == null)
                HotKeyWindow = new HotKeyNativeWindow(this);

            try
            {
                if (Key != Keys.None)
                {
                    HotKeyWindow.RegisterHotkey();
                }
                else
                {
                    // 主键为 None 说明用户清空了组合键：不注册，直接释放窗口，避免留下空壳句柄
                    ReleaseHotKeyWindow();
                }

                // 走到这里说明没抛异常：要么已成功注册，要么组合键已清空无需注册，都算处理完成
                IsRegistered = true;
            }
            catch
            {
                // 注册抛异常（最常见是组合键被别的程序占用）：彻底释放窗口并标记失败
                ReleaseHotKeyWindow();

                IsRegistered = false;
            }

            return IsRegistered;
        }

        // 销毁消息窗口并断开引用  参数：无  返回：无
        // DestroyHandle 内部会先注销热键，所以这里只负责句柄与引用，不必再单独调 UnregisterHotKey
        private void ReleaseHotKeyWindow()
        {
            if (HotKeyWindow != null && HotKeyWindow.Handle != IntPtr.Zero)
                HotKeyWindow.DestroyHandle();
            HotKeyWindow = null;
        }

        // 注销热键并释放消息窗口  参数：无  返回：无
        // 先注销再销毁句柄，避免句柄失效时系统仍向它投递 WM_HOTKEY；重复调用是安全的
        public void UnregisterHotkey()
        {
            if (HotKeyWindow != null)
            {
                HotKeyWindow.UnregisterHotkey();
                ReleaseHotKeyWindow();
            }

            IsRegistered = false;
        }

        // ===================== 事件触发 =====================
        // 触发 HotKeyPressed 事件  参数：无  返回：无
        // 做成 virtual 便于派生类在事件前插入自定义逻辑；先判空是因为没人订阅时它会是 null
        protected virtual void OnHotKey()
        {
            if (HotKeyPressed != null)
                HotKeyPressed(this, new EventArgs());
        }

        // ===================== 内嵌消息窗口 =====================
        // 只收发窗口消息、从不显示的窗口  说明：继承 NativeWindow 才能拿到原始 WndProc，也不会牵动 WinForms 的消息泵
        private class HotKeyNativeWindow : NativeWindow
        {
            // ===================== Win32 常量 =====================
            // WM_HOTKEY = 0x312：热键被按下时系统投递给目标窗口的消息号
            private const int WM_HOTKEY = 0x312;
            // HWND_MESSAGE = -3：系统约定的「仅消息」父窗口句柄，窗口不可见、不进任务栏，但仍能接收消息
            private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

            // 包装给定的 HotKey 逻辑对象  参数：owner 热键本体  返回：无
            // 构造时只记归属，真正的窗口句柄留到 RegisterHotkey 里按需创建
            public HotKeyNativeWindow(HotKey owner)
            {
                Owner = owner;
            }

            // ===================== 窗口状态 =====================
            private HotKey Owner { get; set; }    // 事件最终要回传给哪个 HotKey
            private short HotKeyID { get; set; }  // GlobalAddAtom 分配的原子号（0 表示尚未分配）

            // 终结器：被 GC 回收前兜底注销  参数：无  返回：无
            // 用 try/catch 吞掉异常，因为终结器里抛出的异常会直接终止进程
            ~HotKeyNativeWindow()
            {
                try
                {
                    UnregisterHotkey();
                }
                catch
                {
                }
            }

            // 销毁窗口句柄  参数：无  返回：无
            // 重写是为了先注销热键再让句柄消失；否则系统会向一个已失效的句柄投递 WM_HOTKEY
            public override void DestroyHandle()
            {
                UnregisterHotkey();
                base.DestroyHandle();
            }

            // 释放窗口句柄（不销毁）  参数：无  返回：无
            // 与 DestroyHandle 同理，两条释放路径都要经过注销，保证原子号不会泄漏
            public override void ReleaseHandle()
            {
                UnregisterHotkey();
                base.ReleaseHandle();
            }

            // 创建窗口句柄并完成系统注册  参数：无  返回：无（失败抛 Win32Exception，带回原生错误码）
            public void RegisterHotkey()
            {
                if (HandleCreated() && Owner.Key != Keys.None)
                {
                    // === 惰性分配热键 ID ===
                    // 用原子号充当 ID：GlobalAddAtom 返回 16 位原子号，全系统唯一；按下时随 WM_HOTKEY 的 wParam 原样带回。
                    // 原子号是无符号值，这里用 short 保存，高位值会读成负数；实测这种负数 id 仍能正常注册。
                    if (HotKeyID == 0)
                        HotKeyID = NativeMethods.GlobalAddAtom(Guid.NewGuid().ToString("N"));

                    if (!NativeMethods.RegisterHotKey(Handle, HotKeyID, (int) Owner.Modifiers, (int) Owner.Key))
                    {
                        // 取回原生错误码（1409 表示该热键已被占用）再抛，让上层能提示具体原因
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
            }

            // 注销热键并归还原子号  参数：无  返回：无
            // 原子号必须显式 GlobalDeleteAtom，否则系统原子表里的引用会一直挂着
            public void UnregisterHotkey()
            {
                if (Handle != IntPtr.Zero && HotKeyID != 0)
                {
                    NativeMethods.UnregisterHotKey(Handle, HotKeyID);
                    NativeMethods.GlobalDeleteAtom(HotKeyID);
                    HotKeyID = 0;
                }
            }

            // 确保窗口句柄已创建  参数：无  返回：true 表示句柄可用
            // 窗口本身永远不显示，所有样式清零后挂到 HWND_MESSAGE 下，得到一个只收消息的哑窗口
            private bool HandleCreated()
            {
                if (Handle == IntPtr.Zero)
                {
                    // === 构造「仅消息」窗口参数 ===
                    var createParams = new CreateParams();
                    createParams.Caption = Guid.NewGuid().ToString("N");
                    createParams.Style = 0;
                    createParams.ExStyle = 0;
                    createParams.ClassStyle = 0;
                    // === 挂到 HWND_MESSAGE 之下 ===
                    // HWND_MESSAGE(-3) 是系统约定的「仅消息」父句柄：窗口不显示、不进任务栏，
                    // 却仍能收到 WM_HOTKEY，这是托盘类程序后台接收热键的标准做法，也是这里不建 Form 的原因
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        createParams.Parent = HWND_MESSAGE;
                    }
                    CreateHandle(createParams);
                }
                return (Handle != IntPtr.Zero);
            }

            // 窗口过程：截获热键消息  参数：m 系统投递的消息  返回：无
            // 只认 WM_HOTKEY（0x312），把它翻成 HotKeyPressed 事件；之后仍交给基类，保证消息链完整
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    Owner.OnHotKey();
                }

                base.WndProc(ref m);
            }
        }
    }
}
