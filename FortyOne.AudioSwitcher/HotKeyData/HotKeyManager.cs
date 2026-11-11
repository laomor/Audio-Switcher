// ============================================================
// HotKeyManager.cs - 全局热键的注册、持久化与去重
// 功能：从设置读回热键配置并注册、把变更写回设置文件，再把热键事件向上转发给主程序
// 设计理念：静态类集中托管所有热键，保证同一时刻只有一份注册；对外只保留「循环切换设备」这一条
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using FortyOne.AudioSwitcher.Configuration;

namespace FortyOne.AudioSwitcher.HotKeyData
{
    public static class HotKeyManager
    {
        // ===================== 常量与状态 =====================
        // 循环切换热键的哨兵设备 GUID：全 F 不可能与真实设备冲突，用来标记「这条热键切的是循环」而非某个设备
        public static readonly Guid CycleHotkeyDeviceId = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");

        // 全部已注册热键（本项目只会有循环切换这一条），是 SaveHotKeys 序列化时的数据来源
        private static readonly List<HotKey> _hotkeys = new List<HotKey>();
        // 供设置界面绑定的列表，只放 IsCycleHotKey 为真的项，界面因此不会冒出多余热键
        public static BindingList<HotKey> HotKeys = new BindingList<HotKey>();

        // 注册失败的热键显示名（如 "Alt+Ctrl+F5"）：注册失败时写入，交给界面提示用户换键；
        // 每次重新加载或重建失败列表时都会先清空，否则旧的失败记录会一直挂在界面上
        public static readonly List<string> RegistrationFailures = new List<string>();

        // 静态构造函数：先加载配置并注册，再刷新界面列表  参数：无  返回：无
        // 顺序不能颠倒：RefreshHotkeys 读的是 _hotkeys，不先 LoadHotKeys 列表会是空的
        static HotKeyManager()
        {
            LoadHotKeys();
            RefreshHotkeys();
        }

        // ===================== 对外事件 =====================
        // 任一热键被按下时触发，主程序订阅后执行音频设备切换
        public static event EventHandler HotKeyPressed;

        // ===================== 内部工具 =====================
        // 注销并清空所有热键  参数：无  返回：无
        // 重载配置前必须走这一步，否则旧热键仍被系统占用，同键位的新配置注册时必然冲突
        private static void UnregisterAllHotKeys()
        {
            foreach (var hk in _hotkeys)
                hk.UnregisterHotkey();
            _hotkeys.Clear();
        }

        // ===================== 加载与保存 =====================
        // 从设置里读回热键配置并逐条注册  参数：无  返回：无
        // 配置格式为「[键码,修饰符,设备GUID]」重复出现；单条解析失败只跳过该条，不影响其余热键
        public static void LoadHotKeys()
        {
            try
            {
                // === 清空上一次的失败记录 ===
                RegistrationFailures.Clear();

                // === 无配置：回到「一条热键都没注册」的干净状态 ===
                var hotkeydata = Program.Settings.HotKeys;
                if (string.IsNullOrEmpty(hotkeydata))
                {
                    UnregisterAllHotKeys();
                    RefreshHotkeys();
                    return;
                }

                // === 拆成扁平的三元组序列 ===
                var entries = hotkeydata.Split(new[] { ",", "[", "]" }, StringSplitOptions.RemoveEmptyEntries);

                // 条目数不是 3 的整数倍说明配置被截断，直接返回、保持原有注册不动，避免解析出半条热键
                if (entries.Length % 3 != 0)
                    return;

                // 校验通过后才动现有注册，降低「配置有问题导致热键全丢」的风险
                UnregisterAllHotKeys();

                for (var i = 0; i + 2 < entries.Length; i += 3)
                {
                    // === 解析键码与修饰符 ===
                    int key, modifiers;
                    if (!int.TryParse(entries[i], out key) ||
                        !int.TryParse(entries[i + 1], out modifiers))
                        continue;

                    // === 解析设备 GUID ===
                    var deviceId = ConfigurationSettings.ParseGuid(entries[i + 2]);
                    if (deviceId == Guid.Empty)
                        continue;

                    // 本项目只保留循环切换热键，其他设备的热键即使读到也一律丢弃
                    if (deviceId != CycleHotkeyDeviceId)
                        continue;

                    // === 组装实例并注册 ===
                    var hk = new HotKey();
                    hk.DeviceId = deviceId;
                    hk.Modifiers = (Modifiers)modifiers;
                    hk.Key = (Keys)key;
                    _hotkeys.Add(hk);
                    hk.HotKeyPressed += hk_HotKeyPressed;
                    // 注册失败（键位被占用）时记下组合键文本，是否需要展示由界面决定
                    if (!hk.RegisterHotkey())
                        RegistrationFailures.Add(hk.HotKeyString);
                }
            }
            catch
            {
                // 兜底：解析或注册抛出任何异常都不让程序崩溃，只在失败列表里留一条说明
                RegistrationFailures.Add("热键配置解析失败，已保留原配置");
            }
        }

        // 单条热键的事件转发  参数：sender 触发事件的 HotKey  返回：无
        // 先判空再转发，避免主程序还没订阅时空引用；转发时保留 sender，便于上层区分来源
        private static void hk_HotKeyPressed(object sender, EventArgs e)
        {
            if (HotKeyPressed != null)
                HotKeyPressed(sender, e);
        }

        // 把所有热键写回设置并同步界面  参数：无  返回：无
        // 序列化成「[键码,修饰符,设备GUID]」的紧凑文本，键码与修饰符存整数，LoadHotKeys 读回时可直接强转
        public static void SaveHotKeys()
        {
            // === 拼接配置文本 ===
            var hotkeydata = "";
            foreach (var hk in _hotkeys)
            {
                hotkeydata += "[" + (int)hk.Key + "," + (int)hk.Modifiers + "," + hk.DeviceId + "]";
            }
            Program.Settings.HotKeys = hotkeydata;

            RefreshHotkeys();

            // Add/Delete 之后按当前 IsRegistered 重建失败列表
            RebuildRegistrationFailures();
        }

        // 按各热键的 IsRegistered 重建失败列表  参数：无  返回：无
        // 与 LoadHotKeys 里的记录互补：这里覆盖「保存时才发现没注册上」的情况，保证列表永远反映当前状态
        private static void RebuildRegistrationFailures()
        {
            RegistrationFailures.Clear();
            foreach (var hk in _hotkeys)
            {
                if (!hk.IsRegistered)
                    RegistrationFailures.Add(hk.HotKeyString);
            }
        }

        // ===================== 增删与查询 =====================
        // 添加一条热键  参数：hk 待添加的热键  返回：true 表示已注册成功并已保存
        // 先查重再注册；注册失败会把刚挂上的事件摘掉，保证调用方拿到 false 时不留任何副作用
        public static bool AddHotKey(HotKey hk)
        {
            // === 去重：键码与修饰符都相同即视为同一个热键 ===
            if (DuplicateHotKey(hk))
                return false;

            // 先减后加，避免同一实例被添加两次时事件被挂上两遍
            hk.HotKeyPressed -= hk_HotKeyPressed;
            hk.HotKeyPressed += hk_HotKeyPressed;

            if (!hk.RegisterHotkey())
            {
                // 注册失败就回滚订阅，热键不进列表，界面状态与系统保持一致
                hk.HotKeyPressed -= hk_HotKeyPressed;
                return false;
            }

            _hotkeys.Add(hk);

            SaveHotKeys();
            return true;
        }

        // 重建界面绑定的热键列表  参数：无  返回：无
        // 只挑 IsCycleHotKey 为真的项，令设置界面永远只有「循环切换设备」这一条热键
        public static void RefreshHotkeys()
        {
            HotKeys.Clear();
            var hotkeyList = _hotkeys.Where(x => x.IsCycleHotKey);

            foreach (var k in hotkeyList)
            {
                HotKeys.Add(k);
            }
        }

        // 判断是否已存在相同组合键  参数：hk 待检查的热键  返回：true 表示重复，不允许再加入
        // 只比键码与修饰符、不比设备：同一个组合键不允许绑到不同设备上，否则系统只会把 WM_HOTKEY 投给先注册者
        public static bool DuplicateHotKey(HotKey hk)
        {
            return _hotkeys.Any(k => hk.Key == k.Key && hk.Modifiers == k.Modifiers);
        }

        // 删除一条热键  参数：hk 待删除的热键  返回：无
        // 顺序为「摘事件 → 注销 → 移出列表 → 保存」，保证删完立刻持久化，重启后不会复活
        public static void DeleteHotKey(HotKey hk)
        {
            hk.HotKeyPressed -= hk_HotKeyPressed;  // 先断开转发，防止注销过程中再次触发事件
            hk.UnregisterHotkey();
            _hotkeys.Remove(hk);
            SaveHotKeys();
        }
    }
}
