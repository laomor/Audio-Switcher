// ============================================================
// AudioSwitcher.cs - 主窗体：托盘图标与设备切换总控
// 功能：承载托盘图标与设置卡片，转发全局热键，按收藏列表切换与轮换默认播放设备
// 设计理念：窗体永远不可见，只作为托盘程序的消息宿主，全部界面交给独立卡片窗口
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using FortyOne.AudioSwitcher.Audio;
using FortyOne.AudioSwitcher.Configuration;
using FortyOne.AudioSwitcher.HotKeyData;

namespace FortyOne.AudioSwitcher
{
    // 托盘程序的主窗体：一个不可见的消息宿主，只负责接收系统消息与托盘事件
    // 设备列表、开关、热键录入这些界面都画在独立的 SettingsCardForm 里
    public partial class AudioSwitcher : Form
    {
        // ===================== 单例与状态字段 =====================
        // 「窗体即全局入口」：托盘程序只需要一个窗口来收系统消息，Program 用它启动，退出流程用它摘图标
        private static AudioSwitcher _instance;     // 首次访问 Instance 时才构造，托盘程序只需一个实例

        // 热键屏蔽开关：设置卡片正在录制新热键时置 true，
        // 否则用户刚按下的组合键会立刻触发旧热键，把录入动作打断
        public bool DisableHotKeyFunction = false;  // 录制热键期间为 true

        // === 卡片与托盘计时状态 ===
        // 计时统一用 Environment.TickCount（毫秒，约 49 天回绕）；这里只做「差值 < 阈值」比较，回绕不影响判断
        private SettingsCardForm _settingsCard;                    // 设置卡片：设备列表 + 开关 + 热键
        private SettingsCardForm _exitMenuCard;                    // 右键菜单卡片：只有一个退出按钮
        private System.Windows.Forms.Timer _traySingleClickTimer;  // 单击判定定时器（120 毫秒）
        private int _lastTrayDoubleClickTick;                      // 上次双击时刻（毫秒时间戳）
        private int _cardShownByClickTick;                         // 卡片由单击弹出的时刻（毫秒时间戳）
        private int _lastTrayTooltipRefreshTick;                   // 上次刷新托盘提示的时刻（毫秒时间戳）

        // ===================== 构造与单例入口 =====================
        // 1. 初始化设计器控件，并在句柄创建完成后做一次性收尾
        // 2. 设置窗口图标，并按任务栏明暗加载对应的托盘图标
        // 3. 订阅系统主题变化，让托盘图标跟随明暗主题换色
        // 4. 加载配置与收藏设备，订阅设备变更与全局热键事件
        // 构造窗体并接好全部事件源  参数：无  返回：无
        public AudioSwitcher()
        {
            // === 控件与句柄 ===
            // 只有句柄创建完成后才能安全地 BeginInvoke，所以加载收尾挂在 HandleCreated 上
            InitializeComponent();
            HandleCreated += AudioSwitcher_HandleCreated;

            // === 窗口图标 ===
            // 程序化加载而不是依赖设计器资源：资源缺失时返回 null，可以静默跳过而不抛异常
            var appMark = CreateAppMark();
            if (appMark != null)
                Icon = appMark;

            // === 托盘图标 ===
            // 构造时先按当前主题取一次，之后由 UserPreferenceChanged 负责跟随切换
            ApplyTrayIcon();
            SystemEvents.UserPreferenceChanged += AudioSwitcher_UserPreferenceChanged;  // 主题变化时重取托盘图标

            // === 配置与事件订阅 ===
            // 恢复开机自启与收藏设备：LoadFavouriteDevices 期间 _loading 为 true，内部会抑制变更事件，
            // 整批装完只通知一次，会把刚读出来的列表写回配置
            LoadSettings();

            AudioDeviceManager.Controller.DevicesChanged += AudioDeviceManager_AudioDeviceChanged;

            HotKeyManager.HotKeyPressed += HotKeyManager_HotKeyPressed;
        }

        // 进程内唯一实例  参数：无  返回：AudioSwitcher 窗体（首次访问时构造）
        public static AudioSwitcher Instance
        {
            get { return _instance ?? (_instance = new AudioSwitcher()); }
        }

        // 托盘图标可见性  参数：true 显示 / false 隐藏  返回：无
        // 退出流程至少要摘图标，所以不外露 NotifyIcon 本身，只转发这一个属性
        public bool TrayIconVisible
        {
            get { return notifyIcon1.Visible; }
            set
            {
                try
                {
                    notifyIcon1.Visible = value;
                }
                catch
                {
                }
            }
        }

        // ===================== 应用图标资源 =====================
        // 读取程序集内嵌的扬声器图标，供主窗体与设置卡片作窗口图标  参数：size 边长（像素，0 表示原始尺寸）
        // 返回：Icon；资源缺失或解码失败时返回 null，调用方按「无图标」处理
        internal static Icon CreateAppMark(int size)
        {
            try
            {
                using (var stream = typeof(AudioSwitcher).Assembly
                    .GetManifestResourceStream("FortyOne.AudioSwitcher.Resources.SpeakersApp.ico"))
                {
                    if (stream == null)
                        return null;
                    return size > 0 ? new Icon(stream, size, size) : new Icon(stream);  // size > 0 时由 Icon 按目标尺寸重新取样
                }
            }
            catch
            {
            }
            return null;
        }

        // 无参重载：按资源原始尺寸取图标  参数：无  返回：Icon 或 null
        internal static Icon CreateAppMark()
        {
            return CreateAppMark(0);
        }

        // ===================== 任务栏主题与托盘图标 =====================
        // 按任务栏明暗加载对应图标  参数：size 边长（像素，0 为原始尺寸）、lightTaskbar 是否亮色任务栏
        // 返回：Icon；资源缺失时返回 null，由调用方保留原图标
        private static Icon LoadAppIcon(int size, bool lightTaskbar)
        {
            try
            {
                // 亮色任务栏配深色线稿、暗色任务栏配浅色线稿：单色图标只有与底色反差足够才看得清
                string resName = lightTaskbar
                    ? "FortyOne.AudioSwitcher.Resources.SpeakersLight.ico"
                    : "FortyOne.AudioSwitcher.Resources.SpeakersDark.ico";

                using (var stream = typeof(AudioSwitcher).Assembly
                    .GetManifestResourceStream(resName))
                {
                    if (stream == null)
                        return null;
                    return size > 0 ? new Icon(stream, size, size) : new Icon(stream);
                }
            }
            catch
            {
            }
            return null;
        }

        // 判断当前是否使用亮色任务栏  参数：无  返回：true 亮色 / false 暗色
        // 读的是 HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize 下的 SystemUsesLightTheme
        // （1 亮 / 0 暗）：这个值描述「系统模式」，决定任务栏与通知区的底色；同键下的 AppsUseLightTheme 只管应用窗口，
        // 不能拿来判断任务栏。读不到该值（Win7/8 或精简系统）时按暗色处理，正好符合这些系统深色任务栏的外观
        private static bool IsLightTaskbar()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SystemUsesLightTheme");  // 1 表示亮色，0 或类型不符都按暗色处理
                        if (value is int)
                            return (int) value != 0;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        // 按当前任务栏明暗重新加载托盘图标  参数：无  返回：无
        // 先记住旧图标、替换成功后再释放旧的：直接释放会让通知区短暂出现空白槽位
        private void ApplyTrayIcon()
        {
            // 用系统小图标尺寸加载，与通知区实际渲染尺寸一致，避免被系统二次缩放而发虚
            var trayIcon = LoadAppIcon(SystemInformation.SmallIconSize.Width, IsLightTaskbar());
            if (trayIcon == null)
                // 资源缺失时保持原图标不动：宁可图标不跟主题，也不能让通知区出现空位
                return;

            var previous = notifyIcon1.Icon;
            notifyIcon1.Icon = trayIcon;
            if (previous != null && previous != trayIcon)  // 不能释放刚换上的那个图标
                previous.Dispose();
        }

        // ===================== 系统主题变化监听 =====================
        // 系统配色变化时重取托盘图标  参数：e.Category 变化类别  返回：无
        // UserPreferenceChanged 除了主题还会因鼠标、键盘、无障碍等设置触发，只有 General 类别代表主题变化
        private void AudioSwitcher_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General)  // 其余类别直接忽略，免得无谓地重建图标
                return;

            try
            {
                // 事件可能在非 UI 线程触发，必须切回 UI 线程才能改 NotifyIcon；句柄已销毁时不再刷新
                if (IsHandleCreated)
                    BeginInvoke(new Action(ApplyTrayIcon));
            }
            catch
            {
                // 窗体正在销毁时 BeginInvoke 会抛异常，而此时刷新已无意义，吞掉即可
            }
        }

        // ===================== 窗体生命周期与设备事件 =====================
        // 句柄创建完成  参数：sender/e 事件源与参数（未使用）  返回：无
        // 构造阶段还没有句柄，只能等句柄就绪后再投递 Form_Load，之后的 BeginInvoke 才有目标线程
        private void AudioSwitcher_HandleCreated(object sender, EventArgs e)
        {
            BeginInvoke(new Action(Form_Load));  // 用 BeginInvoke 投递，先让句柄创建流程走完
        }

        private void Form_Load()
        {
            NativeMemory.TrimWorkingSet();

            RefreshNotifyIconItems();
        }

        // 音频设备新增、移除或默认设备变化  参数：sender/e 事件源与参数（未使用）  返回：无
        // 设备事件来自 Core Audio 的回调线程，改控件前必须切回 UI 线程
        private void AudioDeviceManager_AudioDeviceChanged(object sender, EventArgs e)
        {
            Action refreshAction = () => RefreshSettingsCardIfVisible();  // 抽出委托，两条分支执行同一动作

            if (InvokeRequired)
                BeginInvoke(refreshAction);
            else
                refreshAction();
        }

        // 拦截窗体的可见性设置  参数：value 调用方期望的可见性（一律忽略）  返回：无
        // 这是托盘程序的关键：若放过真正的 Show，点托盘图标会闪出一块 1x1 的空白窗体，
        // 窗口还会被 Alt+Tab 和任务栏收录；这里先保证句柄存在（NotifyIcon 与消息泵都要它），
        // 再用 base.SetVisibleCore(false) 把「变成可见」这一步掐掉，窗体永远只是一个消息宿主
        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated)
                // 句柄是消息宿主的前提，必须先建出来，否则后续 Invoke 无处落脚
                CreateHandle();

            // 无论调用方传 true 还是 false，都按不可见处理
            base.SetVisibleCore(false);
        }

        // ===================== 托盘交互：单击与双击 =====================
        // 托盘图标鼠标单击  参数：e.Button 按键、e.Clicks 连击次数  返回：无
        // 1. 右键：切换「退出」胶囊卡片
        // 2. 左键：双击之后的残余单击直接丢弃
        // 3. 左键：启动 120 毫秒定时器，到点仍没有第二击才按单击处理
        // 4. 同时先把卡片内容填好，定时器到点即可秒开
        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            // === 右键：退出菜单 ===
            if (e.Button == MouseButtons.Right)
            {
                ToggleExitMenu();
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            // === 丢弃双击后的残余单击 ===
            // 双击的第二下先触发 MouseDoubleClick，随后还会补发一次 MouseClick：不挡这一下，
            // 双击切完设备后紧接着的单击又会把卡片弹出来
            if (_lastTrayDoubleClickTick > 0 &&
                Environment.TickCount - _lastTrayDoubleClickTick < 60)
                return;

            // === 单击：延迟 120 毫秒判定 ===
            // 单击要弹卡片、双击要在收藏设备间轮换，两者都从左键单击开始，只能靠时间窗区分：等满系统双击间隔
            // （默认约 500 毫秒）会让托盘明显迟钝，120 毫秒是「响应够快」与「不与双击抢事件」之间的折中；
            // 更慢的双击仍可能先弹出卡片，这由双击处理里的 _cardShownByClickTick 兜底收回
            if (_traySingleClickTimer == null)
            {
                _traySingleClickTimer = new System.Windows.Forms.Timer
                {
                    Interval = 120  // 单击判定窗口（毫秒）
                };
                _traySingleClickTimer.Tick += TraySingleClickTimer_Tick;  // 定时器只建一次，之后复用
            }

            _traySingleClickTimer.Stop();  // 重新计时：连点时以最后一次单击为准
            _traySingleClickTimer.Start();

            PrepareSettingsCard();         // 先备好内容，到点直接显示
        }

        // 单击判定定时器到点，确认这是一次真正的单击  参数：sender/e 事件源与参数（未使用）  返回：无
        private void TraySingleClickTimer_Tick(object sender, EventArgs e)
        {
            _traySingleClickTimer.Stop();  // 先停表，避免重复触发
            ToggleSettingsCard();          // 显示或隐藏设置卡片
        }

        // ===================== 设置卡片：构建与显隐 =====================
        // 确保设置卡片存在并接好事件  参数：无  返回：无
        // 惰性创建：启动时不构造卡片，省下窗口构建开销，也让从不点托盘的用户不必付这份代价
        private void EnsureSettingsCard()
        {
            if (_settingsCard != null && !_settingsCard.IsDisposed)
                return;

            _settingsCard = new SettingsCardForm();                                   // 卡片只建一次，被系统释放后再重建
            _settingsCard.DeviceActivated += SettingsCard_DeviceActivated;            // 点击设备行 → 切换默认设备
            _settingsCard.FavouriteToggled += SettingsCard_FavouriteToggled;          // 收藏星标切换
            _settingsCard.OptionToggled += SettingsCard_OptionToggled;                // 自动启动 / 双切模式开关
            _settingsCard.HotKeyChanged += SettingsCard_HotKeyChanged;                // 录入新热键
            _settingsCard.HotKeyCleared += SettingsCard_HotKeyCleared;                // 清除热键
            _settingsCard.HotKeyCaptureStarted += SettingsCard_HotKeyCaptureStarted;  // 开始录制 → 屏蔽热键
            _settingsCard.HotKeyCaptureEnded += SettingsCard_HotKeyCaptureEnded;      // 结束录制 → 恢复热键
        }

        // 把当前设备与开关状态灌进卡片  参数：无  返回：无
        private void FillSettingsCard()
        {
            EnsureSettingsCard();
            _settingsCard.SetDevices(BuildSettingsCardDevices());

            var cycleHotkey = FindCycleHotkey();                                      // 全局只有一个循环热键
            // 三个开关一次性传入：逐个设置会让卡片反复重排，出现肉眼可见的跳动
            _settingsCard.SetOptions(
                Program.Settings.AutoStartWithWindows,
                Program.Settings.DualSwitchMode,
                cycleHotkey != null ? cycleHotkey.HotKeyString : UiText.HotKeyNone);  // 没有绑定热键时显示「未设置」占位文案
        }

        // 预先填充卡片内容但不显示（单击延迟判定期间调用）  参数：无  返回：无
        // 卡片已可见时不再填充：否则会把用户正在操作的列表项与滚动位置重置掉
        private void PrepareSettingsCard()
        {
            if (_settingsCard != null && !_settingsCard.IsDisposed && _settingsCard.Visible)
                return;

            FillSettingsCard();
        }

        // 切换设置卡片的显隐  参数：无  返回：无
        // 1. 已显示则收起
        // 2. 退出卡片可见或刚收起（800 毫秒内）时不弹，避免右键后紧接的单击把卡片又弹回来
        // 3. 设置卡片刚收起（800 毫秒内）时不重开，否则「关了又开」看起来像点不动
        // 4. 正常情况：填充并贴托盘弹出，同时检查热键注册失败提示
        private void ToggleSettingsCard()
        {
            // === 已显示则收起 ===
            if (_settingsCard != null && !_settingsCard.IsDisposed && _settingsCard.Visible)
            {
                _settingsCard.HideCard();
                return;
            }

            // === 退出卡片占位期间不弹设置卡片 ===
            // 800 毫秒是手感值：右键关掉退出卡片后马上左键，用户的意图是重新打开卡片，
            // 但两次点击在系统看来仍是一次连续操作，冷却期内不响应更稳
            if (_exitMenuCard != null && !_exitMenuCard.IsDisposed)
            {
                if (_exitMenuCard.Visible)
                {
                    _exitMenuCard.HideCard();
                    return;
                }
                if (_exitMenuCard.LastHideTick > 0 &&
                    Environment.TickCount - _exitMenuCard.LastHideTick < 800)
                    return;
            }

            // === 刚收起的卡片不立即重开 ===
            // 同一个 800 毫秒冷却：单击弹卡片后的第二下可能被系统算进双击，不设冷却就会关了又开
            if (_settingsCard != null && !_settingsCard.IsDisposed &&
                _settingsCard.LastHideTick > 0 &&
                Environment.TickCount - _settingsCard.LastHideTick < 800)
                return;

            // === 正常弹出 ===
            EnsureSettingsCard();

            _settingsCard.ShowNear(notifyIcon1);            // 贴着托盘图标定位，跟随通知区位置
            _cardShownByClickTick = Environment.TickCount;  // 记下弹出时刻，供双击判断「卡片是否刚被单击弹出」

            NotifyHotKeyFailures();                         // 热键失败只在卡片内提示，不额外弹系统通知
        }

        // 生成卡片要显示的设备行  参数：无  返回：SettingsCardDevice 列表
        // 高亮行的判定：只要存在播放默认设备就取它，一台都没有时才退回通信默认，
        // 这样卡片上高亮的那一行始终是用户实际在用的设备
        private List<SettingsCardDevice> BuildSettingsCardDevices()
        {
            // 只列激活设备：禁用设备不参与切换
            var devices = AudioDeviceManager.Controller.GetPlaybackDevices(AudioEndpointState.Active).ToList();

            bool hasDefaultPlayback = devices.Any(d => d.IsDefault);  // 是否存在播放默认设备

            var rows = new List<SettingsCardDevice>();
            // 按系统返回的顺序逐行追加，卡片顺序才不会每次刷新都变
            foreach (var ad in devices)
            {
                rows.Add(new SettingsCardDevice
                {
                    Id = ad.Id,
                    Name = ad.Name,
                    InterfaceName = ad.InterfaceName,
                    IsDefault = hasDefaultPlayback ? ad.IsDefault : ad.IsCommunicationDefault,  // 没有播放默认时退回通信默认
                    IsFavourite = FavouriteDeviceManager.IsFavouriteDevice(ad.Id),
                    // 角标只认系统里的通信默认设备：双切模式下本程序会把两者设成同一台，
                    // 通信默认被外部单独改过时，角标也落在真正的通信设备上
                    IsCommunicationsDevice = ad.IsCommunicationDefault
                });
            }
            return rows;
        }

        // ===================== 设置卡片：设备切换 =====================
        // 卡片上某个设备行被点击  参数：e.DeviceId 目标设备 Guid  返回：无
        private void SettingsCard_DeviceActivated(object sender, SettingsCardDeviceEventArgs e)
        {
            SetDefaultPlaybackDeviceById(e.DeviceId);  // 切换统一走异步版本，调用结束后会刷新卡片
        }

        // 把指定设备设为默认播放设备  参数：deviceId 目标设备 Guid  返回：无（async）
        // 1. 在激活设备里按 Guid 定位目标，找不到直接返回
        // 2. 设为默认播放设备；双切模式下同时设为通信默认设备
        // 3. 立即刷新卡片，让高亮行跟手
        // 4. 700 毫秒后再补刷一次，纠正系统延迟广播造成的旧状态
        // 5. 刷新托盘提示文本
        private async void SetDefaultPlaybackDeviceById(Guid deviceId)
        {
            // === 定位设备 ===
            // 重新枚举而不是复用卡片里那份对象：设备可能刚好被热插拔，卡片里的实例可能已失效
            var dev = AudioDeviceManager.Controller.GetPlaybackDevices(AudioEndpointState.Active)
                .FirstOrDefault(d => d.Id == deviceId);
            if (dev == null)
                return;

            // === 执行切换 ===
            try
            {
                await dev.SetAsDefaultAsync();       // 等系统真正完成切换再往下走

                // 双切模式：通信默认设备跟着一起换，否则通话仍走旧设备
                if (Program.Settings.DualSwitchMode)
                    dev.SetAsCommunication();
            }
            catch
            {
                // 切换失败（设备被拔掉、驱动拒绝）时静默忽略：界面状态以随后的刷新结果为准
            }

            // === 立即刷新 ===
            RefreshSettingsCardIfVisible(true);      // 保住用户刚才的滚动位置

            // === 700 毫秒后补刷 ===
            // 系统把「默认设备已变」广播到各个应用有延迟，紧接着的那次刷新可能仍读到旧设备，
            // 所以隔一小段时间再刷一次，让高亮行最终落到真正生效的设备上；定时器一次性用完即释放
            var lateRefresh = new System.Windows.Forms.Timer { Interval = 700 };
            lateRefresh.Tick += (s, e) =>
            {
                lateRefresh.Stop();                  // 先停再释放，一次性定时器不留句柄
                lateRefresh.Dispose();
                RefreshSettingsCardIfVisible(true);  // 复用同一个刷新入口
            };
            lateRefresh.Start();

            // 托盘提示文本也要跟着换，否则悬停时还显示旧设备名
            RefreshNotifyIconItems();
        }

        // ===================== 设置卡片：收藏与开关 =====================
        // 收藏星标被点击  参数：e.DeviceId 设备 Guid  返回：无
        // 收藏列表决定托盘双击与全局热键能在哪些设备之间轮换
        private void SettingsCard_FavouriteToggled(object sender, SettingsCardDeviceEventArgs e)
        {
            if (FavouriteDeviceManager.IsFavouriteDevice(e.DeviceId))   // 已收藏 → 取消
                FavouriteDeviceManager.RemoveFavouriteDevice(e.DeviceId);
            else
                FavouriteDeviceManager.AddFavouriteDevice(e.DeviceId);  // 未收藏 → 加入
        }

        // 开关类选项被切换  参数：e.Option 选项、e.Value 用户期望的值  返回：无
        // 写回配置后再把「实际生效值」回传给卡片：配置层可能拒绝或修正该值，卡片必须显示真实状态
        private void SettingsCard_OptionToggled(object sender, SettingsCardToggleEventArgs e)
        {
            var actual = e.Value;                                     // 先按用户期望值假定成功

            switch (e.Option)
            {
                case SettingsCardOption.AutoStartWithWindows:
                    Program.Settings.AutoStartWithWindows = e.Value;  // 写回配置，属性 setter 负责持久化
                    actual = Program.Settings.AutoStartWithWindows;   // 回读实际值，卡片以此为准
                    break;

                case SettingsCardOption.DualSwitchMode:
                    Program.Settings.DualSwitchMode = e.Value;        // 写回配置
                    actual = Program.Settings.DualSwitchMode;         // 回读实际值
                    break;
            }

            if (_settingsCard != null && !_settingsCard.IsDisposed)
                _settingsCard.SetOptionValue(e.Option, actual);       // 把实际生效值回传给卡片
        }

        // ===================== 设置卡片：热键录入 =====================
        // 注销当前的循环热键  参数：无  返回：被注销的热键，原本没有则返回 null
        // 换键或清键之前必须先注销旧的，否则同一个组合键会被注册两次，第二次必然失败
        private HotKey UnregisterCycleHotKey()
        {
            var hk = FindCycleHotkey();          // 找出当前生效的循环热键
            if (hk != null)
                HotKeyManager.DeleteHotKey(hk);  // 从系统注销，把组合键还给其他程序
            return hk;
        }

        // 用户录入了新的循环热键  参数：e.Key 主键、e.Modifiers 修饰键  返回：无
        // 注册失败时把旧热键装回去并提示：不能让用户因为一次失败的尝试失去原有的切换能力
        private void SettingsCard_HotKeyChanged(object sender, SettingsCardHotKeyEventArgs e)
        {
            var cycleHotkey = UnregisterCycleHotKey();                            // 先腾出旧组合键

            // 循环热键靠固定的 DeviceId 标识身份，注册时不能丢
            var hk = new HotKey();
            hk.DeviceId = HotKeyManager.CycleHotkeyDeviceId;                      // 固定标识：FindCycleHotkey 靠它查找
            hk.Key = e.Key;                                                       // 主键
            hk.Modifiers = e.Modifiers;                                           // 修饰键（Ctrl / Alt / Shift / Win）

            if (HotKeyManager.AddHotKey(hk))
            {
                _settingsCard.SetHotKeyText(hk.HotKeyString, false);              // 注册成功：显示新组合键并清掉错误样式
            }
            else
            {
                // 失败：把旧热键原样装回去，尽量恢复到用户操作前的状态
                if (cycleHotkey != null && HotKeyManager.AddHotKey(cycleHotkey))
                    _settingsCard.SetHotKeyText(cycleHotkey.HotKeyString, true);  // 回滚成功，但以错误样式提示新热键没生效
                else if (cycleHotkey != null)
                    _settingsCard.SetHotKeyText(UiText.HotKeyNone, true);         // 回滚也失败（组合键已被别的程序占用）：只能显示「未设置」
                else
                    _settingsCard.SetHotKeyText(UiText.HotKeyNone, false);        // 原本就没有热键：保持「未设置」、正常样式
            }
        }

        // 用户清除了循环热键  参数：sender/e 事件源与参数（未使用）  返回：无
        private void SettingsCard_HotKeyCleared(object sender, EventArgs e)
        {
            UnregisterCycleHotKey();                                    // 注销后配置里也不再保留

            if (_settingsCard != null && !_settingsCard.IsDisposed)
                _settingsCard.SetHotKeyText(UiText.HotKeyNone, false);  // 卡片显示「未设置」
        }

        // 开始录制新热键  参数：sender/e 事件源与参数（未使用）  返回：无
        // 录制期间屏蔽热键：否则用户按下的组合键会先触发旧热键，可能把卡片切走甚至切换设备
        private void SettingsCard_HotKeyCaptureStarted(object sender, EventArgs e)
        {
            DisableHotKeyFunction = true;   // 屏蔽热键分发
        }

        // 录制结束（成功或取消）  参数：sender/e 事件源与参数（未使用）  返回：无
        private void SettingsCard_HotKeyCaptureEnded(object sender, EventArgs e)
        {
            DisableHotKeyFunction = false;  // 恢复热键分发
        }

        // ===================== 托盘双击：轮换收藏设备 =====================
        // 托盘图标双击  参数：e 鼠标事件参数（未使用）  返回：无
        // 左键双击会被系统拆成「两次 MouseClick + 一次 MouseDoubleClick」，这里的顺序很关键：
        // 1. 停掉单击定时器，阻止第一次单击延迟到期后弹出卡片
        // 2. 记下双击时刻，让紧随其后的那次 MouseClick 被 60 毫秒规则丢弃
        // 3. 卡片若刚被单击弹出（600 毫秒内）先收起：用户的意图是切设备，不是看卡片
        // 4. 轮换到下一个收藏设备
        private async void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (_traySingleClickTimer != null)
            {
                _traySingleClickTimer.Stop();                  // 抢在定时器到点前拦下
            }

            _lastTrayDoubleClickTick = Environment.TickCount;  // 供 MouseClick 过滤双击后的残余单击

            if (_settingsCard != null && !_settingsCard.IsDisposed && _settingsCard.Visible &&
                _cardShownByClickTick > 0 &&
                Environment.TickCount - _cardShownByClickTick < 600)
                _settingsCard.HideCard();                      // 卡片是刚才单击弹出的，顺手收起

            await CycleToNextPlaybackDeviceAsync();            // 双击的最终目的是切设备
        }

        // 查找当前的循环切换热键  参数：无  返回：热键对象；未注册时返回 null
        // 热键列表没有按用途索引的接口，只能按固定 DeviceId 线性查找（通常只有一两个热键）
        private HotKey FindCycleHotkey()
        {
            foreach (var hk in HotKeyManager.HotKeys)  // 热键数量很少，线性查找足够
            {
                if (hk.DeviceId == HotKeyManager.CycleHotkeyDeviceId)
                    return hk;
            }
            return null;                               // 没找到：当前没绑定循环热键
        }

        // ===================== 设置持久化与提示 =====================
        // 从配置恢复运行时状态  参数：无  返回：无
        // 1. 校正开机自启配置与实际注册表状态
        // 2. 订阅收藏设备变化，变化后立刻写回配置
        // 3. 把「[guid],[guid]」形式的字符串解析成 Guid 列表装入 FavouriteDeviceManager
        private void LoadSettings()
        {
            Program.Settings.RepairAutoStart();  // 启动时校正一次

            FavouriteDeviceManager.FavouriteDevicesChanged += AudioDeviceManger_FavouriteDevicesChanged;  // 变化即写回配置

            // 配置里用「[guid],[guid]」的格式存收藏列表，这里按逗号与方括号一起切开
            var favDeviceStr = Program.Settings.FavouriteDevices.Split(new[] { ",", "[", "]" },
                StringSplitOptions.RemoveEmptyEntries);

            FavouriteDeviceManager.LoadFavouriteDevices(
                Array.ConvertAll(favDeviceStr, ConfigurationSettings.ParseGuid));  // 解析失败的项在加载时被跳过

        }

        // 把热键注册失败的情况显示在卡片上  参数：无  返回：无
        // 不用系统气泡通知：托盘程序的气泡容易被忽略，不同 Windows 版本的样式也不可控
        private void NotifyHotKeyFailures()
        {
            if (HotKeyManager.RegistrationFailures.Count == 0)         // 没有失败记录就什么都不显示
                return;

            // 组合键名可能很长（如 Alt+Ctrl+Shift+Win+PrintScreen），塞进两行提示会被截断；
            // 全局只有一个可设热键的入口，用户本来就知道是哪一个，冲突时给通用说明即可
            string first = HotKeyManager.RegistrationFailures[0];      // 只提示第一条，多条警告会把卡片撑高
            string line1 = first.Contains("，") ? first : "热键与其他软件冲突";  // 解析失败那类记录本身就是可展示的中文说明
            string line2 = "可在设置中更换";
            // 复用卡片内的两行提示，不弹模态对话框，避免打断用户操作
            _settingsCard.ShowHotKeyWarning(line1, line2);
        }

        // 收藏设备集合变化  参数：IDs 新的收藏设备 Guid 列表  返回：无
        // 序列化集中在这里完成，保证与 LoadSettings 的解析格式严格对应
        private void AudioDeviceManger_FavouriteDevicesChanged(List<Guid> IDs)
        {
            Program.Settings.FavouriteDevices = "[" + string.Join("],[", IDs.ToArray()) + "]";  // 与 LoadSettings 对应
        }

        // ===================== 托盘提示文本 =====================
        // 刷新托盘图标的悬停提示  参数：无  返回：无
        // 每次都重新取默认设备而不读缓存：提示文本的意义就在于立刻反映默认设备的变化
        private void RefreshNotifyIconItems()
        {
            var devices = AudioDeviceManager.Controller.GetPlaybackDevices(AudioEndpointState.Active).ToList();
            var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);                  // 优先用播放默认设备
            if (defaultDevice == null)
                defaultDevice = devices.FirstOrDefault(d => d.IsCommunicationDefault);     // 没有播放默认时退回通信默认

            var notifyText = "Audio Switcher";                                             // 取不到设备时的兜底文案

            if (defaultDevice != null)
            {
                var devName = defaultDevice.FullName ?? defaultDevice.Name ?? notifyText;  // FullName 带端点描述，更好辨认

                // NotifyIcon.Text 超过 63 个字符 .NET Framework 会抛异常，这里截到 60 再补省略号
                if (devName.Length >= 64)
                    notifyText = devName.Substring(0, 60) + "...";
                else
                    notifyText = devName;
            }

            // 只有文本真的变了才赋值，减少通知区重绘
            if (notifyIcon1.Text != notifyText)
                notifyIcon1.Text = notifyText;
        }

        // 鼠标划过托盘图标时刷新提示  参数：e 鼠标事件参数（未使用）  返回：无
        // MouseMove 触发非常频繁，用 400 毫秒节流：每次刷新都要枚举一遍音频设备，代价太高
        private void notifyIcon1_MouseMove(object sender, MouseEventArgs e)
        {
            int now = Environment.TickCount;              // 毫秒时间戳
            if (now - _lastTrayTooltipRefreshTick < 400)  // 未到节流窗口，直接返回
                return;

            _lastTrayTooltipRefreshTick = now;            // 记录本次刷新时刻
            RefreshNotifyIconItems();
        }

        // ===================== 全局热键与设备轮换 =====================
        // 全局热键按下  参数：sender 触发的 HotKey  返回：无
        // HotKeyManager 对所有热键共用一个事件，只能靠 sender 上的标识区分是哪一个
        private async void HotKeyManager_HotKeyPressed(object sender, EventArgs e)
        {
            if (DisableHotKeyFunction)                       // 正在录制热键：忽略，免得新组合键立刻生效
                return;

            if (sender is HotKey)
            {
                var hk = sender as HotKey;                   // 只有 HotKey 触发的事件才处理

                if (hk.IsCycleHotKey)                        // 只响应循环切换热键
                {
                    await CycleToNextPlaybackDeviceAsync();  // 轮换到下一个收藏设备
                }
            }
        }

        // 在收藏设备之间轮换到下一个可用的默认播放设备  参数：无  返回：无（async）
        // 1. 没有收藏设备直接返回
        // 2. 只保留仍在线的收藏设备（收藏列表可能残留已拔出的设备）
        // 3. 定位当前默认设备在收藏列表里的位置，从它的下一项开始尝试
        // 4. 依次 SetAsDefaultAsync，直到某个设备切换成功
        // 5. 双切模式下把成功设备同时设为通信默认
        // 6. 刷新卡片与托盘提示
        private async Task CycleToNextPlaybackDeviceAsync()
        {
            // === 前置检查 ===
            if (FavouriteDeviceManager.FavouriteDeviceCount <= 0)
                return;

            // === 候选设备 ===
            // 只留激活中的收藏设备：设备被拔掉后 Guid 还留在配置里，不能拿来切
            var activeDevices = AudioDeviceManager.Controller.GetPlaybackDevices(AudioEndpointState.Active).ToList();

            var favourites = activeDevices
                .Where(d => FavouriteDeviceManager.IsFavouriteDevice(d.Id))
                .ToList();

            // 收藏设备全都不在线：没有可切换的目标
            if (favourites.Count == 0)
                return;

            // === 确定起点 ===
            // 从「当前设备的下一项」开始，轮换才有顺序感；当前设备不在收藏里时就从第一个开始
            var start = 0;
            var currentDefaultDevice = activeDevices.FirstOrDefault(d => d.IsDefault);  // 优先用播放默认设备定位当前位置
            if (currentDefaultDevice == null)
                currentDefaultDevice = activeDevices.FirstOrDefault(d => d.IsCommunicationDefault);  // 退回通信默认设备

            if (currentDefaultDevice != null)
            {
                // 在收藏列表里找到当前设备，它的下一项就是轮换目标
                for (var i = 0; i < favourites.Count; i++)
                {
                    if (favourites[i].Id == currentDefaultDevice.Id)
                    {
                        start = (i + 1) % favourites.Count;  // 取余实现首尾相接
                        break;
                    }
                }
            }

            var changed = false;  // 记录是否已有设备切换成功
            // 从 start 开始沿环尝试，一旦成功立即跳出
            for (var i = 0; i < favourites.Count && !changed; i++)
            {
                var candidate = favourites[(start + i) % favourites.Count];  // 按环取下一个候选设备
                changed = await candidate.SetAsDefaultAsync();

                // 双切模式：通信默认设备跟着一起切
                if (changed && Program.Settings.DualSwitchMode)
                    candidate.SetAsCommunication();
            }

            RefreshSettingsCardIfVisible();  // 卡片可见时同步高亮行

            // 只有真的换了设备才刷托盘提示，否则纯属多余
            if (changed)
                RefreshNotifyIconItems();
        }

        // ===================== 卡片刷新与退出 =====================
        // 卡片可见时用最新设备列表刷新  参数：preserveScrollPosition 是否保持滚动位置  返回：无
        // 卡片不可见时什么都不做：不为一个看不见的窗口重建控件
        private void RefreshSettingsCardIfVisible(bool preserveScrollPosition = false)
        {
            if (_settingsCard != null && !_settingsCard.IsDisposed && _settingsCard.Visible)
                _settingsCard.SetDevices(BuildSettingsCardDevices(), preserveScrollPosition);  // 按调用方要求决定是否保持滚动位置
        }

        // 切换右键「退出」胶囊卡片的显隐  参数：无  返回：无
        // 退出卡片与设置卡片互斥：弹出前先收起设置卡片，避免两块窗口叠在托盘上方
        private void ToggleExitMenu()
        {
            if (_exitMenuCard != null && !_exitMenuCard.IsDisposed && _exitMenuCard.Visible)
            {
                _exitMenuCard.HideCard();                                                     // 已显示则收起
                return;
            }

            if (_settingsCard != null && !_settingsCard.IsDisposed && _settingsCard.Visible)  // 先收起设置卡片：两块卡片不同时显示
                _settingsCard.HideCard();

            if (_exitMenuCard == null || _exitMenuCard.IsDisposed)                            // 惰性创建：多数用户只在退出时才会用到它
            {
                _exitMenuCard = new SettingsCardForm();
                _exitMenuCard.SetExitMenuMode(true);                                          // 切成退出模式：卡片里只留一个退出按钮
                _exitMenuCard.ExitRequested += SettingsCard_ExitRequested;                    // 点退出才真正结束进程
            }

            _exitMenuCard.ShowNear(notifyIcon1);                                              // 与设置卡片用同一套贴托盘定位
        }

        // 用户点了退出按钮  参数：sender/e 事件源与参数（未使用）  返回：无
        // 走 Application.Exit 而不是 Environment.Exit：让 ApplicationExit 回调有机会摘掉托盘图标
        private void SettingsCard_ExitRequested(object sender, EventArgs e)
        {
            Application.Exit();  // 触发退出流程 → 摘图标 → 进程结束
        }
    }
}
