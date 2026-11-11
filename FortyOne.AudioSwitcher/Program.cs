// ============================================================
// Program.cs - 进程入口：单实例互斥、全局异常兜底与配置初始化
// 功能：保证全局只跑一个实例，安装异常钩子，准备 %LocalAppData% 下的配置目录，再启动主窗体
// 设计理念：进程级的一次性工作全部收在这里，任一步失败就提示并退出，不留下半初始化的托盘进程
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using FortyOne.AudioSwitcher.Configuration;
using FortyOne.AudioSwitcher.Properties;

namespace FortyOne.AudioSwitcher
{
    internal static class Program
    {

        // ===================== 全局状态 =====================
        public static string AppDataDirectory { get; private set; }         // 配置根目录（%LocalAppData%）

        public static ConfigurationSettings Settings { get; private set; }  // 全局唯一的配置对象，各处都从这里读写

        private static Mutex _singleInstanceMutex;                          // 单实例互斥体，进程存活期间一直持有

        // 1. 抢占单实例互斥体，已有实例就提示后退出
        // 2. 安装 UI 线程与 CLR 的全局异常钩子，避免未处理异常直接崩掉进程
        // 3. 启用可视化样式，并做 Vista 以上的系统版本检查
        // 4. 把 exe 目录下的旧配置迁到 %LocalAppData%，补齐默认设置
        // 5. 用 Application.Run 启动托盘主窗体，直到用户选择退出
        // 程序入口  参数：无  返回：无（返回即进程结束）
        [STAThread]
        private static void Main()
        {
            // === 单实例检查 ===
            // 用命名互斥体而不是「查找同名窗口」或「监听固定端口」：互斥体是内核对象，进程崩溃或被强杀时由系统
            // 自动回收，不会留下需要用户手工清理的锁文件或端口占用；Local\ 前缀把作用域限制在当前登录会话，
            // 多用户同时登录时每个人仍能各跑一份，互不干扰
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, @"Local\AudioSwitcher_SingleInstance", out createdNew);

            // createdNew 为 false 说明已有实例持有同名互斥体：先释放自己的句柄（不能影响在跑的那个），
            // 再提示用户去托盘区找它，然后直接退出
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                MessageBox.Show("Audio Switcher 已在运行，请从系统托盘区切换到它。", "Audio Switcher");
                return;
            }

            // === 全局异常兜底 ===
            // 消息循环里的异常默认会被静默吞掉或直接终止进程；这里把两类异常都接到统一处理器，
            // 至少让用户看到可读的错误信息，而不是窗口莫名消失
            Application.ThreadException += WinFormExceptionHandler.OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += WinFormExceptionHandler.OnUnhandledCLRException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // === 可视化样式与系统版本检查 ===
            // 可视化样式必须在创建任何窗口之前启用，否则控件仍按旧版外观绘制；
            // SetCompatibleTextRenderingDefault 全进程只能调用一次，所以放在这里而不是窗体构造里
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 主版本号 6 对应 Windows Vista：Vista 起才有 Core Audio API，更低版本连设备枚举都做不到
            if (Environment.OSVersion.Version.Major < 6)
            {
                MessageBox.Show("Audio Switcher 仅支持 Windows Vista 及以上系统", "不支持的操作系统");
                return;
            }

            // 进程退出回调先挂上，保证正常退出路径上也能摘掉托盘图标
            Application.ApplicationExit += Application_ApplicationExit;

            // 配置放 %LocalAppData%\AudioSwitcher 而不是 exe 目录：装到 Program Files 时 exe 目录只读，
            // 写配置会因权限失败；放用户目录还天然支持多用户各自独立的设置
            AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitcher");

            var settingsPath = Path.Combine(AppDataDirectory, Resources.ConfigFile);  // 新配置文件的完整路径

            // === 清理更老的 ini 配置 ===
            try
            {
                // 早期版本把 ini 放在 exe 目录，现在配置全在用户目录，这个文件只剩垃圾，删掉以免与新配置混淆
                var iniSettingsPath = Path.Combine(Directory.GetParent(Assembly.GetEntryAssembly().Location).FullName, Resources.OldConfigFile);

                if (File.Exists(iniSettingsPath))
                    File.Delete(iniSettingsPath);
            }
            catch
            {
                // 删不掉不影响启动：文件可能被占用或只读，留着也只是无害的旧文件
            }

            // === 初始化配置并迁移旧 json ===
            // 升级用户会在 exe 旁边留下旧版 json：先读出来合并进新配置再删旧文件，用户的设置不会丢
            try
            {
                // 首次运行时目录还不存在，补建之后写配置就不必再做存在性判断；
                // 建不出来会落到下面的 catch，给出可读提示而不是直接崩掉
                if (!Directory.Exists(AppDataDirectory))
                    Directory.CreateDirectory(AppDataDirectory);

                var oldJsonSettingsPath = Path.Combine(Directory.GetParent(Assembly.GetEntryAssembly().Location).FullName, Resources.ConfigFile);

                // === 建立新配置对象 ===
                // 先绑路径再构造：ConfigurationSettings 的构造函数会立刻按路径读取文件
                ISettingsSource jsonSource = new JsonSettings();
                jsonSource.SetFilePath(settingsPath);

                // 配置对象建好后就常驻：窗体、热键与设备管理都通过 Program.Settings 读写
                Settings = new ConfigurationSettings(jsonSource);

                // 旧位置存在配置文件时才迁移，全新安装没有这一步
                if (File.Exists(oldJsonSettingsPath))
                {
                    try
                    {
                        // 旧值覆盖新值：用户原有设置优先于默认值
                        var oldSource = new JsonSettings();
                        oldSource.SetFilePath(oldJsonSettingsPath);

                        var oldSettings = new ConfigurationSettings(oldSource);
                        if (!oldSource.LoadFailed)
                        {
                            Settings.LoadFrom(oldSettings);

                            // 迁移成功后删除旧文件，下次启动不再进入这段逻辑
                            File.Delete(oldJsonSettingsPath);
                        }
                        // 读不出来就原样留着：不用空值覆盖新配置，也不删掉用户仅存的那份文件
                    }
                    catch
                    {
                        // 旧配置迁移抛出异常时吞掉异常：不能让异常挡住新配置的创建
                    }
                }

                // 补齐默认值：升级后新增的配置项在这里拿到默认值，不必为每个版本写迁移代码
                Settings.CreateDefaults();
            }
            // 配置初始化抛出异常时无配置可用，继续运行只会不断抛异常，只能提示后退出
            catch
            {
                // 把路径和解决办法写进提示里，比抛一堆异常堆栈有用
                var errorMessage = String.Format("创建/读取设置文件 [{0}] 失败。请确保您对此文件具有读写权限。\r\n或尝试以管理员身份运行。",
                        settingsPath);
                MessageBox.Show(errorMessage, "设置文件 - 无法访问");
                return;
            }

            try
            {
                // Application.Run 并不显示窗口，它只是托盘程序的消息泵，直到用户选择退出才返回
                Application.Run(AudioSwitcher.Instance);
            }
            catch (Exception ex)
            {
                var title = "发生意外错误";

                // 消息循环整体抛出的异常用独立窗体展示，用户可以复制堆栈反馈
                var edf = new ExceptionDisplayForm(title, ex);
                edf.ShowDialog();
            }
        }

        // 进程退出回调  参数：sender 事件源（未使用）、e 事件参数（未使用）  返回：无
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            // 退出前显式隐藏托盘图标：否则通知区会留下一个点不动的「幽灵图标」，直到鼠标划过才消失
            AudioSwitcher.Instance.TrayIconVisible = false;

            // 再注销设备变更回调：进程结束时不留悬挂的 COM 订阅
            AudioDeviceManager.Shutdown();
        }
    }
}
