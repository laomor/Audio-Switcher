// ============================================================
// SettingsCardForm.cs - 主部分：状态字段、构造与对外事件
// 功能：集中放置卡片的全部状态字段、补间参数、开关项动画数组与对外事件，并在这里一次定死窗体外观
// 说明：本文件是 SettingsCardForm 的分块（partial），配合其它分块阅读
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FortyOne.AudioSwitcher.HotKeyData;

namespace FortyOne.AudioSwitcher
{
    // 设置卡片上的两个开关项；枚举值同时用作 _checkValue 等动画数组的下标
    public enum SettingsCardOption
    {
        AutoStartWithWindows = 0,  // 开机自启（设置行 0）
        DualSwitchMode = 1         // 双切换模式（设置行 1）
    }

    // 某个开关被拨动后的通知参数  返回：Option 哪一项，Value 拨动后的值
    // 卡片只管动画与命中，真正落盘由宿主的 OptionToggled 处理器完成
    public sealed class SettingsCardToggleEventArgs : EventArgs
    {
        // 构造即固化两个字段，避免事件在传递途中被下游改写
        public SettingsCardToggleEventArgs(SettingsCardOption option, bool value)
        {
            Option = option;
            Value = value;
        }

        public SettingsCardOption Option { get; private set; }  // 被拨动的开关项
        public bool Value { get; private set; }                 // 拨动后的布尔值
    }

    // 录入到一组合法热键后的通知参数  返回：Key 主键，Modifiers 修饰键组合
    // 由 OnKeyUp 在组合校验通过后触发，宿主据此重新注册全局热键
    public sealed class SettingsCardHotKeyEventArgs : EventArgs
    {
        // 与开关参数一样构造即固化，属性对外只读
        public SettingsCardHotKeyEventArgs(Keys key, Modifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public Keys Key { get; private set; }             // 主键（Keys 枚举）
        public Modifiers Modifiers { get; private set; }  // 修饰键位组合（Control/Alt/Shift）
    }

    // 设备行的渲染快照  说明：只是给列表画图用的数据副本，改它不影响系统里的真实设备
    public sealed class SettingsCardDevice
    {
        public Guid Id { get; set; }                      // 设备唯一标识，收藏表与星动画字典都以它为键
        public string Name { get; set; }                  // 设备友好名（左块主文字）
        public string InterfaceName { get; set; }         // 接口名（左块次要小字）
        public bool IsDefault { get; set; }               // 是否默认播放设备，决定是否走高亮
        public bool IsFavourite { get; set; }             // 是否已收藏，决定星标填充程度
        public bool IsCommunicationsDevice { get; set; }  // 是否通信设备，决定行尾角标
    }

    // 设备操作（激活/收藏）的事件参数  说明：只带 Guid，详情由宿主回查自己的设备表
    public sealed class SettingsCardDeviceEventArgs : EventArgs
    {
        // 构造即固化，避免订阅方在事件链中途读到半成品
        public SettingsCardDeviceEventArgs(Guid deviceId)
        {
            DeviceId = deviceId;
        }

        public Guid DeviceId { get; private set; }  // 被操作的设备标识
    }

    // 无边框圆角卡片窗口：弹出在托盘图标上方，列出播放设备、收藏星标与三项设置
    // 之所以拆成多个 partial：绘制、布局、输入、动效各自成文件，单文件才不会失控
    public sealed partial class SettingsCardForm : Form
    {

        // ===================== 补间基元 =====================
        // 把已过毫秒数归一化成 0~1 的进度  参数：elapsedMs 已过毫秒，durationMs 总时长  返回：裁剪后的进度
        // 卡片里的补间动画大多先取这个线性进度，再由调用方套缓动曲线；时长为 0 时直接返回 1，表示瞬间完成
        private static double Tween01(int elapsedMs, int durationMs)
        {
            if (durationMs <= 0)                        // 时长为 0 视为已结束，避免除零
                return 1.0;
            double t = elapsedMs / (double)durationMs;  // 强转 double，防止整数除法把进度截成 0
            if (t < 0) t = 0;                           // TickCount 回绕时会算出负值，先压回 0
            if (t > 1) t = 1;                           // 定时器有抖动可能略超时长，压到 1 防过冲
            return t;
        }

        // 以「起始时刻」为基准求进度  参数：startTick 起始 TickCount（毫秒），durationMs 总时长  返回：0~1 的进度
        // 之所以单独抽一层：每个动画只要记住自己何时开始，就不必各自再算一遍差值
        private static double TweenSince(int startTick, int durationMs)
        {
            return Tween01(Environment.TickCount - startTick, durationMs);  // TickCount 的 32 位回绕由 Tween01 的裁剪兜底
        }

        // ===================== 设备与字体资源 =====================
        private readonly List<SettingsCardDevice> _devices = new List<SettingsCardDevice>();  // 当前设备快照，下标即行号
        private const string UiFontFamily = "Microsoft YaHei UI";                             // 界面统一字体族

        private readonly Font _nameFont = new Font(UiFontFamily, 9f, FontStyle.Bold);          // 设备名 9pt 粗体
        private readonly Font _exitFont = new Font(UiFontFamily, 10f, FontStyle.Bold);         // 退出卡「退出」10pt 粗体
        private readonly Font _interfaceFont = new Font(UiFontFamily, 8f, FontStyle.Regular);  // 接口名 8pt 常规
        private readonly Image _commBadge = LoadCommBadgeImage();                              // 通信角标位图（Draw 分块生成）

        // ===================== 齿轮与显示状态 =====================
        private bool _hadFocus;     // 本次显示期间是否真的拿到过焦点（防抖用）
        private int _shownTick;     // 卡片显示时刻的 TickCount（毫秒），300ms 防抖窗口的起点
        private bool _gearHover;    // 齿轮是否悬停，影响齿轮的直径与透明度
        private bool _gearPressed;  // 齿轮是否按下

        // ===================== 滚动状态 =====================
        private double _scrollOffset;        // 当前滚动偏移（逻辑像素），0 表示列表顶部对齐
        private double _targetOffset;        // 补间目标偏移，由滚轮、拖动滚动条和列表刷新时设定
        private Timer _scrollTimer;          // 16ms 补间定时器，把 _scrollOffset 追向 _targetOffset
        private int _hoverDeviceIndex = -1;  // 悬停的设备行下标，-1 表示不在任何行上
        private bool _draggingThumb;         // 是否正在拖动滚动条滑块
        private int _dragStartY;             // 开始拖动时的鼠标 Y（客户区坐标），用来算位移
        private double _dragStartOffset;     // 开始拖动时的偏移，拖动全程以它为基准

        // ===================== 展开波纹状态 =====================
        // 点齿轮后以点击为圆心扩散出设置层（grow → bounce → 停住），下面这组就是那套状态机
        // 半径、起止值、阶段号都是逐帧读写的裸字段：16ms 一帧，包成对象反而增加分配
        private double _spreadRadius;      // 当前波纹半径（逻辑像素）
        private double _spreadFrom;        // 本阶段起始半径
        private double _spreadTo;          // 本阶段目标半径
        private int _spreadPhaseStart;     // 本阶段开始时刻（TickCount，毫秒）
        private int _spreadPhaseDuration;  // 本阶段时长（毫秒）
        private int _spreadPhase;          // 阶段号，取值见 Settings 分块的 SpreadPhase*
        private double _spreadMaxRadius;   // 覆盖整张卡片所需的最大半径（圆心到最远角）
        private PointF _spreadCenter;      // 波纹圆心，即点击点（逻辑坐标）
        private bool _spreadExpanded;      // 是否处于「已展开」意图，展开动画期间也是 true
        private bool _spreadFilled;        // 是否已铺满，grow 阶段结束才置 true，决定设置层内容是否可见
        private Timer _spreadTimer;        // 16ms 逐帧定时器

        // ===================== 开关项状态 =====================
        private bool _optAutoStart;   // 「开机自启」当前值，由宿主同步进来
        private bool _optDualSwitch;  // 「双切换模式」当前值

        // ===================== 开关项动画数组 =====================
        // 下列数组一律按「设置行下标」索引：0 = 开机自启，1 = 双切换模式
        // 用定长数组而不是每行一个对象：逐帧循环只遍历一次，下标与行号天然对齐，也不产生垃圾
        private readonly double[] _checkValue = { 0.0, 0.0 };        // 勾选填充进度（0~1），0 空、1 满
        private readonly int[] _checkAnimStart = { 0, 0 };           // 勾选补间起始时刻（TickCount，毫秒）
        private readonly double[] _checkAnimFrom = { 0.0, 0.0 };     // 补间起始值
        private readonly double[] _checkAnimTo = { 0.0, 0.0 };       // 补间目标值
        private readonly int[] _checkBounceStart = { 0, 0 };         // 勾选后回弹动画的起始时刻（毫秒）
        private readonly bool[] _checkFillRise = { false, false };   // 填充方式：true 由中心扩散，false 按透明度淡出
        private readonly double[] _checkHoverValue = { 0.0, 0.0 };   // 悬停进度（0~1）
        private readonly double[] _checkHoverFrom = { 0.0, 0.0 };    // 悬停补间起点
        private readonly double[] _checkHoverTarget = { 0.0, 0.0 };  // 悬停补间终点（0 或 1）
        private readonly int[] _checkHoverStart = { 0, 0 };          // 悬停补间起始时刻（毫秒）
        private readonly double[] _checkPressValue = { 0.0, 0.0 };   // 按下进度（0~1），控制勾选框的轻微下沉
        private readonly double[] _checkPressFrom = { 0.0, 0.0 };    // 按下补间起点
        private readonly double[] _checkPressTarget = { 0.0, 0.0 };  // 按下补间终点
        private readonly int[] _checkPressStart = { 0, 0 };          // 按下补间起始时刻（毫秒）
        private Timer _checkTimer;                                   // 统一驱动上面这些补间，16ms 一帧
        private int _settingHoverRow = -1;                           // 悬停的设置行下标，-1 表示没有
        private int _settingPressRow = -1;                           // 按下的设置行下标，-1 表示没有
        // ===================== 热键录入状态 =====================
        private string _hotKeyText = UiText.HotKeyNone;  // 热键框文本，未设置时为 UiText.HotKeyNone
        private bool _hotKeyError;                       // 是否显示错误态（注册失败后由宿主置位）
        private bool _hotKeyCapturing;                   // 是否正在捕获按键；true 时按键只记账不下发
        private string _hotKeyTextBeforeCapture;         // 捕获前的文本快照，Esc 撤销时回填
        private bool _hotKeyErrorBeforeCapture;          // 捕获前的错误态快照，与文本一起回滚
        private double _hotKeyFocusT;                    // 焦点光晕进度（0~1）
        private double _hotKeyFocusFrom;                 // 焦点补间起点
        private double _hotKeyFocusTarget;               // 焦点补间终点
        private int _hotKeyFocusStart;                   // 焦点补间起始时刻（毫秒）
        private Timer _hotKeyFocusTimer;                 // 焦点补间定时器
        private double _hotKeyHoverT;                    // 悬停光晕进度（0~1）
        private double _hotKeyHoverFrom;                 // 悬停补间起点
        private double _hotKeyHoverTarget;               // 悬停补间终点
        private int _hotKeyHoverStart;                   // 悬停补间起始时刻（毫秒）
        private bool _hotKeyHoverOn;                     // 悬停是否成立（命中测试结果，与进度分开存）
        private Timer _hotKeyHoverTimer;                 // 悬停补间定时器

        // ===================== 设备切换动画时长 =====================
        private const int SwitchDropDuration = 460;                                     // 默认高亮从旧行落到新行的时长（毫秒）
        private const int BadgeRevealDelay = 140;                                       // 角标比高亮块晚起步的时长（毫秒）
        private const int SwitchTotalDuration = SwitchDropDuration + BadgeRevealDelay;  // 动画总时长（毫秒），即 460+140，期间忽略重复切换
        private int _switchTargetIndex = -1;                                            // 要落到的高亮行下标，-1 表示没有切换
        private int _badgeTargetIndex = -1;                                             // 通信角标要落到的新行下标
        private int _switchStart;                                                       // 切换动画起始时刻（TickCount，毫秒）
        private Timer _switchTimer;                                                     // 切换动画定时器

        private int _switchFromIndex = -1;  // 切换起点行下标，用来算高亮的下落距离

        // ===================== 退出小卡片状态 =====================
        private bool _exitMenuMode;      // 是否切换成「退出」小卡片形态，会改 Region 与绘制内容
        private bool _exitRowHover;      // 退出按钮是否悬停
        private bool _exitRowPressed;    // 退出按钮是否按下
        private int _exitVanishStart;    // 收缩消失动画起始时刻（毫秒），0 表示未开始
        private bool _exitVanishDone;    // 收缩是否已结束，结束后由宿主退出程序
        private Timer _exitVanishTimer;  // 收缩动画定时器

        // ===================== 收藏星动效参数 =====================
        private const int StarHoverDuration = 200;     // 悬停放大/缩小补间时长（毫秒）
        private const int StarFillDuration = 220;      // 填充/取消填充补间时长（毫秒）
        private const int StarBounceRise = 100;        // 点击后放大阶段时长（毫秒）
        private const int StarBounceFall = 190;        // 放大后缩回阶段时长（毫秒）
        private const double StarHoverGrow = 0.17;     // 悬停时放大比例（0.17 即放大 17%）
        private const double StarPreviewAlpha = 0.28;  // 未收藏时预览星的透明度（0~1）
        private const double StarBounceLift = 0.13;    // 回弹时整体放大的比例（0.13 即放大 13%）

        // ===================== 单颗星的补间状态 =====================
        // 每颗星一份：三段补间各自记录 From/To/Start/Dur，逐帧用 Tween01 求进度，互不干扰
        // 之所以按设备 Guid 建字典而不是按行号：滚动、增删设备时行号会变，Guid 不会
        private sealed class StarAnim
        {
            // === 悬停缩放 ===
            public double Hover;      // 当前缩放进度（0~1）
            public double HoverFrom;  // 本段起点
            public double HoverTo;    // 本段终点
            public int HoverStart;    // 本段开始时刻（毫秒）
            public int HoverDur;      // 本段时长（毫秒）

            // === 金色填充 ===
            public double Fill;      // 填充进度（0~1），0 空心、1 实心
            public double FillFrom;  // 本段起点
            public double FillTo;    // 本段终点
            public int FillStart;    // 本段开始时刻（毫秒）
            public int FillDur;      // 本段时长（毫秒）

            // === 点击回弹 ===
            public double Bounce;      // 当前回弹缩放量（0~0.13），直接叠加到缩放系数
            public double BounceFrom;  // 本段起点
            public double BounceTo;    // 本段终点
            public int BounceStart;    // 本段开始时刻（毫秒）
            public int BounceDur;      // 本段时长（毫秒）
        }

        // ===================== 收藏星动画索引 =====================
        private readonly Dictionary<Guid, StarAnim> _starAnims = new Dictionary<Guid, StarAnim>();  // 星动画状态（按 Guid 存）
        private Timer _starTimer;                                                                   // 星动画 16ms 逐帧定时器
        private Guid _starHoverId = Guid.Empty;                                                     // 悬停星设备 Id

        // ===================== 构造与初始化 =====================
        // 构造卡片：定死窗体外观与双缓冲，并先把圆角 Region 套上  参数：无  返回：无
        // 这里不读任何业务状态：设备列表、开关值都由宿主在 ShowNear 前后注入
        public SettingsCardForm()
        {
            // === 窗口外观 ===
            FormBorderStyle = FormBorderStyle.None;        // 去掉标题栏与边框，形状靠 Region 自己裁
            ShowInTaskbar = false;                         // 不进任务栏，它只是托盘图标的附庸
            TopMost = true;                                // 永远压在最前，否则点完托盘会被前台窗口盖住
            StartPosition = FormStartPosition.Manual;      // 位置完全由 CalculateLocation 算，禁用系统摆放
            AutoScaleMode = AutoScaleMode.None;            // 关键：尺寸与坐标全按逻辑像素手算，再缩放一次就对不上 Region 与命中区
            ClientSize = new Size(CardWidth, CardHeight);  // 用布局常量定初始客户区，真实位置在 ShowNear 里算
            BackColor = CardBackBottom;                    // 兜底底色，避免重绘瞬间闪白
            KeyPreview = true;                             // 让 Esc 与录入的按键先落到窗体，不被子控件吃掉

            // === 托盘程序图标 ===
            var appMark = AudioSwitcher.CreateAppMark();  // 与主程序同一枚图标，Alt-Tab/预览里不至于空白
            if (appMark != null)
                Icon = appMark;                           // 图标可能生成失败，为 null 时保持默认

            // === 绘制与圆角 ===
            // 自绘 + 双缓冲：所有像素由 OnPaint 画，避免闪烁
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ApplyRoundCorners();  // 首次显示前建好圆角 Region
        }

        // ===================== 对外事件 =====================
        // 卡片自身不碰业务：切设备、收藏、设置落盘、热键重注册都通过下面的事件交回宿主
        // 这样卡片可以整体替换，宿主也不必知道卡片内部有哪些动画
        public event EventHandler<SettingsCardDeviceEventArgs> DeviceActivated;   // 单击设备行，请求把它设为默认

        public event EventHandler<SettingsCardDeviceEventArgs> FavouriteToggled;  // 点了星标，请求切换该设备收藏状态

        public event EventHandler<SettingsCardToggleEventArgs> OptionToggled;     // 拨动了开关项，值已由卡片内部取反

        public event EventHandler<SettingsCardHotKeyEventArgs> HotKeyChanged;     // 录到合法热键组合，请求重新注册

        public event EventHandler HotKeyCleared;                                  // 请求清空热键（Delete/Backspace）

        public event EventHandler HotKeyCaptureStarted;                           // 进入捕获态，宿主应暂停响应全局热键

        public event EventHandler HotKeyCaptureEnded;                             // 捕获结束（成功/取消/清空都算）

        public event EventHandler ExitRequested;                                  // 点了退出卡上的「退出」

        // ===================== 状态查询与窗口样式 =====================
        // 只有隐藏时刻这一个状态需要暴露给宿主  返回：最近一次 HideCard 的 TickCount（毫秒）
        public int LastHideTick { get; private set; }  // 宿主据此判断「刚收起又点托盘」该不该立刻重开

        // 给无边框窗口补一个系统投影（CS_DROPSHADOW）  参数：无  返回：本窗口的创建参数
        // 退出小卡片形态下不加：它收缩时 Region 一直在变，投影会跟着闪
        protected override CreateParams CreateParams
        {
            get
            {
                // === 投影开关 ===
                var cp = base.CreateParams;       // 先取基类参数，再按当前形态打补丁
                if (!_exitMenuMode)               // 退出小卡片不要投影
                    cp.ClassStyle |= 0x00020000;  // 0x00020000 = CS_DROPSHADOW，由系统绘制窗口阴影
                return cp;                        // 每次建窗都会重新取，形态变了也能生效
            }
        }

        // ===================== 齿轮几何 =====================
        // 齿轮按钮的命中区  参数：无  返回：右上角 GearHotZoneSize 见方的矩形（逻辑像素）
        private Rectangle GearHotZone
        {
            get { return new Rectangle(Width - GearHotZoneSize, 0, GearHotZoneSize, GearHotZoneSize); }  // 比齿轮大一圈，别太难点
        }

        // 齿轮的圆心  参数：无  返回：右上角命中区中心点（逻辑像素）
        private PointF GearCenter
        {
            get { return new PointF(Width - GearHotZoneSize / 2f, GearHotZoneSize / 2f); }  // 只改直径，圆心固定才不会跳
        }

    }
}