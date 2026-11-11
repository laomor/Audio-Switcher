// ============================================================
// ConfigurationSettings.cs - 设置项门面与开机自启维护
// 功能：把热键、收藏设备、双设备切换等设置收敛成强类型属性，并维护 HKCU Run 键里的自启项
// 设计理念：界面只认属性，字符串读写与注册表细节全藏在这里；任何失败都静默降级，不打断用户
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FortyOne.AudioSwitcher.Configuration
{
    public class ConfigurationSettings
    {
        // ===================== 设置键名与匹配式 =====================
        // GUID_REGEX 只干一件事：从任意文本里抠出形如 8-4-4-4-12 的字母数字串。
        // 刻意写得宽松：[a-z0-9] 配 IgnoreCase 覆盖大小写，连字符放进 [-] 字符类以免转义，
        // 因为 GUID 常夹在设备描述或实例 ID 里，先按形状捞出来，合不合法交给 Guid 构造去判断
        public const string GUID_REGEX = @"([a-z0-9]{8}[-][a-z0-9]{4}[-][a-z0-9]{4}[-][a-z0-9]{4}[-][a-z0-9]{12})";
        // 下面四个常量既是 JSON 里的键名也是界面绑定的依据，改动会让旧配置整体失效，故集中管理
        public const string SETTING_AUTOSTARTWITHWINDOWS = "AutoStartWithWindows";  // 是否开机自启（"True"/"False"）
        public const string SETTING_HOTKEYS = "HotKeys";                            // 全局热键表（[键码,修饰符,GUID] 连续拼接）
        public const string SETTING_FAVOURITEDEVICES = "FavouriteDevices";          // 收藏设备 GUID 列表（[guid],[guid] 形式）
        public const string SETTING_DUALSWITCHMODE = "DualSwitchMode";              // 双设备切换模式开关（"True"/"False"）

        // Run 键写在 HKCU 下：不需要管理员权限，绿色版/便携版也能开机自启
        private const string AutoStartKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";  // 自启项所在的注册表路径
        private const string AutoStartValueName = "AudioSwitcher";                                 // 值名，固定一个以免重复自启

        private readonly ISettingsSource _configWriter;  // 底层存储（当前是 JsonSettings），所有读写都经它

        // 构造时立刻加载一次，调用方拿到对象就能直接读属性  参数：设置源  返回：无
        public ConfigurationSettings(ISettingsSource source)
        {
            _configWriter = source;
            _configWriter.Load();
        }

        // ===================== GUID 解析 =====================
        // 编译一次复用：加载收藏设备与热键配置时会解析设备 ID，每次 new Regex 都要重新编译模式，太浪费
        private static readonly Regex GuidPattern = new Regex(GUID_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 从任意文本里取出第一个 GUID  参数：原始文本  返回：Guid；没有匹配或内容非法时都返回 Guid.Empty
        // 正则只校验形状，内容仍可能不是合法十六进制，所以用 TryParse 兜住，调用方不必处理异常
        public static Guid ParseGuid(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return Guid.Empty;

            // foreach 配 return 表示只取第一个命中，后面的匹配直接放弃
            foreach (var match in GuidPattern.Matches(raw))
            {
                Guid id;
                return Guid.TryParse(match.ToString(), out id) ? id : Guid.Empty;
            }

            return Guid.Empty;
        }

        // ===================== 属性门面 =====================
        // 读布尔型设置  参数：键名  返回：布尔值；键缺失或内容解析不了都当 false
        // 从宽判定：脏数据只会把开关关掉，不会意外把功能打开
        private bool GetBoolean(string key)
        {
            var raw = _configWriter.Get(key);
            bool value;
            // && 链短路：空串连 TryParse 都不必走，省一次解析
            return !string.IsNullOrEmpty(raw) && bool.TryParse(raw, out value) && value;
        }

        // 双设备切换模式：开启后一次切换同时处理默认播放与通信默认两条链路
        public bool DualSwitchMode
        {
            get { return GetBoolean(SETTING_DUALSWITCHMODE); }
            set
            {
                // Set 内部会立刻落盘，所以属性赋值就等于「改了并保存」，不需要额外的 Save 调用
                _configWriter.Set(SETTING_DUALSWITCHMODE,
                    value.ToString());
            }
        }

        // 收藏设备列表：存成 [guid],[guid] 文本，界面自己解析，这里只当不透明字符串搬运
        public string FavouriteDevices
        {
            get { return _configWriter.Get(SETTING_FAVOURITEDEVICES); }
            set { _configWriter.Set(SETTING_FAVOURITEDEVICES, value); }
        }

        // 全局热键表：同样是方括号拼接文本，注册逻辑在窗体内完成
        public string HotKeys
        {
            get { return _configWriter.Get(SETTING_HOTKEYS); }
            set { _configWriter.Set(SETTING_HOTKEYS, value); }
        }

        // 开机自启开关：属性只是记忆值，真正的动作是增删注册表 Run 键
        public bool AutoStartWithWindows
        {
            get
            {
                return GetBoolean(SETTING_AUTOSTARTWITHWINDOWS);
            }
            set
            {
                try
                {
                    // === 先动注册表，再记配置 ===
                    // 顺序不能反：注册表写失败时要靠 catch 把配置改回 false，否则界面会显示「已开启」
                    if (value)
                        WriteAutoStartValue();
                    else
                        DeleteAutoStartValue();

                    _configWriter.Set(SETTING_AUTOSTARTWITHWINDOWS, value.ToString());
                }
                catch
                {
                    // 注册表被组策略锁住等情况：吞掉异常，把配置降级成 false，让界面与实际状态一致；
                    // 下次启动时 RepairAutoStart 还会再尝试校正一次
                    _configWriter.Set(SETTING_AUTOSTARTWITHWINDOWS, false.ToString());
                }
            }
        }

        // ===================== 开机自启（HKCU Run 键） =====================
        // 拼出本程序在 Run 键里应有的值  参数：无  返回：带引号的 exe 路径；拿不到入口程序时返回 null
        // 路径必须整段加引号：目录里有空格时，不带引号会被系统截断成 C:\Program 之类再带上参数
        private static string AutoStartValueForSelf()
        {
            var entry = Assembly.GetEntryAssembly();
            // 宿主进程（设计器、单元测试）没有入口程序，返回 null 让调用方放弃写注册表
            if (entry == null || string.IsNullOrEmpty(entry.Location))
                return null;

            return "\"" + entry.Location + "\"";
        }

        // 把自启项写成当前 exe  参数：无  返回：无
        private static void WriteAutoStartValue()
        {
            var value = AutoStartValueForSelf();
            if (value == null)
                return;

            // 键不存在就创建：整个 Run 键被清理工具删掉时，自启项仍然要能写进去。
            // 这里只写当前用户自己的分支，不需要管理员权限，也不影响其他用户和系统级设置
            using (var key = Registry.CurrentUser.CreateSubKey(AutoStartKeyPath))
            {
                if (key != null)
                    key.SetValue(AutoStartValueName, value);
            }
        }

        // 删除自启项  参数：无  返回：无
        private static void DeleteAutoStartValue()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, true))
            {
                // 先确认值存在再删：DeleteValue 对不存在的值会抛异常，而这里没有 try
                if (key != null && key.GetValue(AutoStartValueName) != null)
                    key.DeleteValue(AutoStartValueName);
            }
        }

        // 启动时校正自启项  参数：无  返回：无
        // 分四步：
        //   1. 从 %TEMP% 启动就直接放弃（升级器常把 exe 解压到临时目录再拉起，
        //      若把那条随机路径写进 Run 键，重启后指向的文件早被清理，用户会看到「找不到程序」）
        //   2. 用户没勾自启：把残留值删掉
        //   3. 用户勾了自启：算出自我的带引号路径
        //   4. 只有 ShouldRewriteAutoStart 判定该改时才写，避免每次启动都白写一次注册表
        public void RepairAutoStart()
        {
            try
            {
                if (IsRunningFromTemp())
                    return;

                bool wanted = AutoStartWithWindows;

                // 要自启时键不存在就直接创建，被清理工具删掉也能把项补回去；
                // 只需清理时不建空键，按原样打开即可
                using (var key = wanted
                    ? Registry.CurrentUser.CreateSubKey(AutoStartKeyPath)
                    : Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, true))
                {
                    if (key == null)
                        return;

                    var current = key.GetValue(AutoStartValueName) as string;

                    // === 不该自启 ===
                    if (!wanted)
                    {
                        if (current != null)
                            key.DeleteValue(AutoStartValueName);
                        return;
                    }

                    // === 该自启：与现有值比对 ===
                    var self = AutoStartValueForSelf();
                    if (self == null)
                        return;

                    if (!ShouldRewriteAutoStart(current, self))
                        return;

                    key.SetValue(AutoStartValueName, self);
                }
            }
            catch
            {
            }
        }

        // 判断当前进程是否从临时目录启动  参数：无  返回：在 %TEMP% 下为 true
        // 这是个安全阀：临时目录随时可能被清理，把那里的路径焊进开机自启只会制造每次开机的报错
        private static bool IsRunningFromTemp()
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry == null || string.IsNullOrEmpty(entry.Location))
                return false;

            var temp = Path.GetTempPath();
            // 补结尾反斜杠，否则 C:\Temp 会把 C:\Tempxxx\app.exe 也误判成临时目录
            if (!temp.EndsWith("\\"))
                temp += "\\";

            // 忽略大小写：Windows 路径本身不区分大小写
            return entry.Location.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }

        // 判断 Run 键里的旧值要不要改写成当前路径  参数：现有值、当前应写值  返回：需要改写为 true
        // 三段判断，顺序就是优先级：
        //   1. 根本没有值 → 该写（首次开启或被人手工删掉）
        //   2. 归一化后路径相同 → 不动：哪怕引号写法不同，也不做无意义的写入
        //   3. 旧路径指向的文件仍在 → 不动：那是用户另装的一份副本，尊重它，不去抢
        // 只有「旧路径已经失效」才落到最后返回 true，也就是程序目录搬走后自启项能自动跟到新位置
        internal static bool ShouldRewriteAutoStart(string current, string self)
        {
            if (string.IsNullOrEmpty(current))
                return true;

            var currentPath = NormalizeAutoStartPath(current);
            var selfPath = NormalizeAutoStartPath(self);

            if (string.Equals(currentPath, selfPath, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                return false;

            return true;
        }

        // 去掉自启值外层的引号与空白  参数：注册表里的原始值  返回：纯路径；无法识别时返回空串
        // 只做最低限度的解析：不拆命令行参数、不展开环境变量，因为这里写进去的永远是我们自己的带引号路径
        private static string NormalizeAutoStartPath(string value)
        {
            if (value == null)
                return string.Empty;

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            // 形如 "C:\path\app.exe"：取第一个右引号之前的部分，忽略后面可能跟的参数
            if (trimmed[0] == '"')
            {
                int end = trimmed.IndexOf('"', 1);
                if (end > 1)
                    return trimmed.Substring(1, end - 1);
            }

            // 没有引号（手工改成裸路径）时按去首尾引号兜底
            return trimmed.Trim('"');
        }

        // 补齐缺省设置  参数：无  返回：无
        // 逐项补而不是整体覆盖：升级后新增的项才有默认值，老用户改过的值不会被冲掉
        public void CreateDefaults()
        {
            if (!SettingExists(SETTING_AUTOSTARTWITHWINDOWS))
                AutoStartWithWindows = false;

            if (!SettingExists(SETTING_HOTKEYS))
                HotKeys = "[]";  // 空表，界面解析后得到「一个热键都没绑」

            if (!SettingExists(SETTING_FAVOURITEDEVICES))
                FavouriteDevices = "[]";  // 空收藏列表，界面据此显示空状态

            if (!SettingExists(SETTING_DUALSWITCHMODE))
                DualSwitchMode = false;
        }

        // 用另一份设置覆盖当前设置  参数：来源设置对象  返回：无
        // 用于导入配置或整体套用预设；AutoStart 只写配置值，Run 键由启动时的 RepairAutoStart 校正
        public void LoadFrom(ConfigurationSettings otherSettings)
        {
            _configWriter.Set(SETTING_AUTOSTARTWITHWINDOWS, otherSettings.AutoStartWithWindows.ToString());
            DualSwitchMode = otherSettings.DualSwitchMode;
            FavouriteDevices = otherSettings.FavouriteDevices;
            HotKeys = otherSettings.HotKeys;
        }

        // 判断某个键是否已存在于配置文件中  参数：键名  返回：存在为 true
        // 包一层而不是让调用方直接碰 ISettingsSource：这样存储实现换掉时界面代码不用改
        public bool SettingExists(string name)
        {
            return _configWriter.Contains(name);
        }
    }
}
