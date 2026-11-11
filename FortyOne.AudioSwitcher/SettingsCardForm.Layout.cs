// ============================================================
// SettingsCardForm.Layout.cs - 尺寸常量、布局矩形与屏幕定位
// 功能：定义卡片的全部逻辑尺寸，按设备下标换算出各矩形，并把卡片摆到托盘图标上方且不越出屏幕边缘
// 说明：本文件是 SettingsCardForm 的分块（partial），配合其它分块阅读
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 尺寸

        // 下面所有数字都是逻辑像素（96 DPI 基准），与 AutoScaleMode = None 配套
        // 只在这里定义一次：绘制、命中测试、Region 都引用同一份常量，改这里就等于改整张卡片的比例

        // ===================== 卡片本体 =====================
        private const int CornerRadius = 12;     // 卡片四角圆角半径（像素），Region 与描边共用
        private const int CardWidth = 240;       // 客户区宽（像素）：左留白 16 + 左块 152 + 间隙 8 + 右块 40 + 右留白 24
        private const int CardHeight = 152;      // 客户区高（像素）：标题区 + 两行设备 + 底部留白
        private const int GapAboveIcon = 8;      // 卡片与托盘图标之间的间距（像素）
        private const int ScreenEdgeMargin = 4;  // 卡片与工作区边缘的最小间距（像素）

        // ===================== 齿轮按钮 =====================
        private const int GearHotZoneSize = 26;    // 齿轮命中区边长（像素），取右上角一块
        private const int GearIdleDiameter = 13;   // 静止直径（像素）
        private const int GearHoverDiameter = 16;  // 悬停直径（像素），比静止略大
        private const int GearPressDiameter = 14;  // 按下直径（像素），略小做按压感

        // ===================== 设备列表 =====================
        private const int ListTop = 27;          // 列表顶边 y（像素），标题行之下
        private const int ListLeft = 16;         // 列表左边距（像素）
        private const int RowHeight = 44;        // 单行设备块高（像素）
        private const int RowGap = 10;           // 行与行之间的间隙（像素）
        private const int VisibleRows = 2;       // 视口可见行数，ViewHeight 与滚动阈值按它算
        private const int LeftBlockWidth = 152;  // 左块（设备名 + 接口名）宽（像素）
        private const int BlockGap = 8;          // 左右两块之间的间隙（像素）
        private const int RightBlockWidth = 40;  // 右块（星标）宽（像素）
        private const int BlockRadius = 22;      // 左右两块的圆角半径（像素）
        private const int StarSize = 18;         // 星形外接正方形边长（像素）
        private const int StarShiftX = 2;        // 星形相对块中心的水平偏移（像素）
        private const int ScrollbarWidth = 5;    // 滚动条宽度（像素）

        // ===================== 设置项 =====================
        private const int SettingLeft = 15;           // 设置层左边距（像素）
        private const int SettingRight = 15;          // 设置层右边距（像素）
        private const int SettingTop = 30;            // 第一行设置项的 y（像素）
        private const int SettingRowHeight = 30;      // 单行设置项高（像素）
        private const int SettingRowStep = 32;        // 设置行的步进（像素），含行间空隙
        private const int OptionCheckSize = 18;       // 勾选框边长（像素）
        private const int OptionCheckRadius = 5;      // 勾选框圆角半径（像素）
        private const int CheckValueDuration = 300;   // 勾选值补间时长（毫秒）
        private const int CheckBounceDuration = 300;  // 勾选回弹补间时长（毫秒）
        private const int CheckHoverDuration = 300;   // 勾选框悬停补间时长（毫秒）
        private const int CheckPressDuration = 90;    // 勾选框按下补间时长（毫秒），短到跟手
        private const int HotKeyBoxWidth = 110;       // 热键框宽（像素）
        private const int HotKeyBoxHeight = 22;       // 热键框高（像素）
        private const int HotKeyFocusDuration = 260;  // 焦点光晕补间时长（毫秒）
        private const int HotKeyFocusGrowX = 8;       // 焦点态热键框左右合计外扩（像素），两侧各一半
        private const int HotKeyFocusGrowY = 4;       // 焦点态热键框上下合计外扩（像素），两侧各一半
        private const int HotKeyHoverDuration = 200;  // 热键框悬停补间时长（毫秒）
        private const int CommBadgeSize = 11;         // 通信角标边长（像素）
        private const int CommBadgeRightPad = 14;     // 角标距行右缘的距离（像素）

        // ===================== 退出卡 =====================
        private const int ExitCardWidth = 96;               // 退出小卡片宽（像素）
        private const int ExitCardHeight = 40;              // 退出小卡片高（像素）
        private const int ExitRadius = ExitCardHeight / 2;  // 圆角半径（像素），取高一半即胶囊形

        private const int ExitVanishDuration = 340;  // 收缩消失动画时长（毫秒）

        #endregion
        #region 布局矩形

        // 这一组只做「下标 → 矩形」的纯计算：几何全由上面的常量推出，绘制、命中、动画共用同一份结果

        // 某行设备在客户区里的 y 坐标  参数：deviceIndex 设备下标  返回：行顶边 y（逻辑像素）
        // 结果已叠加滚动偏移，并四舍五入到整数像素，避免 GDI+ 画在半像素上发虚
        private int RowTop(int deviceIndex)
        {
            return (int)Math.Round(ListTop + deviceIndex * (RowHeight + RowGap) - _scrollOffset);  // 已叠加滚动偏移
        }

        // 左块矩形（设备名 + 接口名）  参数：deviceIndex 设备下标  返回：逻辑像素矩形
        private Rectangle LeftBlockRect(int deviceIndex)
        {
            return new Rectangle(ListLeft, RowTop(deviceIndex), LeftBlockWidth, RowHeight);  // 宽固定，高等于整行
        }

        // 右块矩形（收藏星标）  参数：deviceIndex 设备下标  返回：逻辑像素矩形
        private Rectangle RightBlockRect(int deviceIndex)
        {
            return new Rectangle(ListLeft + LeftBlockWidth + BlockGap, RowTop(deviceIndex),  // 紧接左块右侧，留 BlockGap 间隙
                RightBlockWidth, RowHeight);                                                 // 宽度固定
        }

        // 星标在右块里的居中矩形  参数：block 右块矩形  返回：星形外接正方形（逻辑像素）
        private static Rectangle StarRectInBlock(Rectangle block)
        {
            return new Rectangle(block.X + (block.Width - StarSize) / 2 - StarShiftX,  // 水平再减 StarShiftX，让视觉重心对齐块中心
                block.Y + (block.Height - StarSize) / 2, StarSize, StarSize);          // 垂直严格居中
        }

        // 滚动条轨道矩形  返回：贴右侧的竖条（逻辑像素），高度等于视口高
        private Rectangle ScrollTrackRect
        {
            // 右边距留 11 像素，不让滚动条贴住卡片边缘
            get { return new Rectangle(Width - ScrollbarWidth - 11, ListTop, ScrollbarWidth, ViewHeight); }
        }

        // 滚动条滑块矩形  返回：随滚动偏移在轨道内滑动的滑块（逻辑像素）
        // 滑块长度按「视口高 ÷ 内容高」算，并有 20 像素下限，否则设备一多就细到按不住
        private Rectangle ScrollThumbRect
        {
            get
            {
                if (MaxOffset <= 0)                                                          // 没有可滚内容就不画滑块
                    return Rectangle.Empty;
                var track = ScrollTrackRect;
                int thumbHeight = (int)(track.Height * (double)ViewHeight / ContentHeight);  // 滑块与轨道的高度比 = 视口高 ÷ 内容高
                if (thumbHeight < 20)                                                        // 20 像素是还能稳稳按住的极限
                    thumbHeight = 20;
                int travel = track.Height - thumbHeight;                                     // 轨道内可供滑块移动的行程
                int offset = (int)Math.Round(travel * _scrollOffset / MaxOffset);            // 偏移按比例映射到行程上
                if (offset < 0) offset = 0;                                                  // 夹紧下界
                if (offset > travel) offset = travel;                                        // 夹紧上界
                return new Rectangle(track.X, track.Y + offset, track.Width, thumbHeight);   // x 与轨道一致，y 随偏移变化
            }
        }

        // 第 index 行设置项的整行矩形  参数：index 行下标（0 自启 / 1 双切换 / 2 热键）  返回：逻辑像素矩形
        private Rectangle SettingRowRect(int index)
        {
            return new Rectangle(SettingLeft, SettingTop + index * SettingRowStep,  // 左右各留 15 像素，宽度由客户区推出
                Width - SettingLeft - SettingRight, SettingRowHeight);
        }

        // 开关勾选框矩形  参数：index 行下标  返回：贴行右端的正方形（逻辑像素）
        private Rectangle OptionCheckRect(int index)
        {
            var row = SettingRowRect(index);
            return new Rectangle(row.Right - OptionCheckSize - 2,                               // 再留 2 像素，免得描边被块边切掉
                row.Y + (row.Height - OptionCheckSize) / 2, OptionCheckSize, OptionCheckSize);  // 垂直居中于该行
        }

        // 开关的命中矩形  参数：index 行下标  返回：勾选框外扩 3 像素后的矩形
        private Rectangle OptionHitRect(int index)
        {
            return Rectangle.Inflate(OptionCheckRect(index), 3, 3);  // 外扩是为了 18 像素的小方框也好点中
        }

        // 热键框矩形  返回：第三行设置项右端的热键框（逻辑像素）
        private Rectangle HotKeyBoxRect
        {
            get
            {
                var row = SettingRowRect(2);                      // 热键固定是设置行 2
                return new Rectangle(row.Right - HotKeyBoxWidth,  // 贴行右缘并垂直居中；焦点外扩在绘制时另算
                    row.Y + (row.Height - HotKeyBoxHeight) / 2, HotKeyBoxWidth, HotKeyBoxHeight);
            }
        }

        // 屏幕点 → 设备行下标  参数：p 客户区坐标（逻辑像素）  返回：命中的行下标，未命中返回 -1
        // 步骤：1. 排除视口外的点；2. 换算到内容坐标；3. 排除行间空隙；4. 再要求落在左右块内
        private int DeviceIndexAt(Point p)
        {
            if (p.Y < ListTop || p.Y > ListTop + ViewHeight)                            // 视口之外一律不算，哪怕内容坐标落在某行
                return -1;

            double contentY = p.Y - ListTop + _scrollOffset;                            // 内容坐标 = 客户区 y - 列表顶 + 滚动偏移
            int index = (int)(contentY / (RowHeight + RowGap));                         // 行步进含间隙，整除即得行号
            if (index < 0 || index >= _devices.Count)                                   // 越界保护：索引必须在设备数量之内
                return -1;

            double withinRow = contentY - index * (RowHeight + RowGap);                 // 行内偏移超过 RowHeight 说明落在空隙里
            if (withinRow >= RowHeight)                                                 // 空隙不属于任何可点区域
                return -1;

            if (LeftBlockRect(index).Contains(p) || RightBlockRect(index).Contains(p))  // 必须真的点在左块或右块上，块间缝不响应
                return index;
            return -1;                                                                  // 在行矩形内但落在块外
        }

        #endregion
        #region 定位

        // 托盘图标由 explorer 托管，位置既不在本进程里，也不遵循普通窗口坐标，定位的麻烦全在这里
        // 顺序是：先拿图标屏幕矩形 → 换算成逻辑坐标 → 再贴着工作区边缘夹紧

        // 算出卡片应出现的屏幕左上角  参数：trayIcon 托盘图标，cardSize 卡片尺寸  返回：屏幕坐标（逻辑像素）
        // 优先放在图标正上方；上方放不下就翻到下方；左右以图标中心对齐卡片中心，并夹在工作区内
        private static Point CalculateLocation(NotifyIcon trayIcon, Size cardSize)
        {
            // === 拿图标矩形 ===
            Rectangle iconRect;
            if (!TryGetTrayIconRect(trayIcon, out iconRect))           // 反射拿不到就走回退分支
            {
                int iconSide = SystemInformation.SmallIconSize.Width;  // 用系统小图标尺寸，与真实托盘图标同量级
                var cursor = Cursor.Position;                          // 用户刚点过托盘，卡片就落在手边
                iconRect = new Rectangle(cursor.X - iconSide / 2, cursor.Y - iconSide / 2, iconSide, iconSide);
            }
            else
            {
                iconRect = ToLogicalRect(iconRect);                    // shell 给的是物理像素，必须换算成逻辑像素
            }

            // === 水平夹紧 ===
            var workArea = Screen.FromRectangle(iconRect).WorkingArea;      // 用图标矩形判断在哪块屏，多显示器下比用鼠标稳

            int x = iconRect.Left + (iconRect.Width - cardSize.Width) / 2;  // 卡片水平中心对齐图标中心
            int minX = workArea.Left + ScreenEdgeMargin;                    // 工作区（已扣任务栏）左侧留 4 像素
            int maxX = workArea.Right - cardSize.Width - ScreenEdgeMargin;  // 右侧同样留 4 像素
            if (x < minX)                                                   // 越界就贴左
                x = minX;
            if (x > maxX)                                                   // 越界就贴右
                x = maxX;

            // === 垂直方向 ===
            int y = iconRect.Top - cardSize.Height - GapAboveIcon;                // 默认放图标上方，间距 GapAboveIcon
            if (y < workArea.Top + ScreenEdgeMargin)                              // 上方空间不足（图标靠近屏幕顶部）
            {
                y = iconRect.Bottom + GapAboveIcon;                               // 改为放到图标下方
                int maxY = workArea.Bottom - cardSize.Height - ScreenEdgeMargin;  // 下方也要留边距，防止压到任务栏
                if (y > maxY)                                                     // 下沿越界就贴上去
                    y = maxY;
            }

            return new Point(x, y);  // 只返回左上角，尺寸由 ShowNear 自己带着
        }

        // 物理像素矩形 → 逻辑像素矩形  参数：rect shell 返回的物理像素矩形  返回：与客户区同一坐标系的矩形
        // 除缩放比的原因：Shell_NotifyIconGetRect 给的是物理像素，而本窗体 AutoScaleMode = None，
        // 客户区一律按逻辑像素算；不除这一下，150% 缩放下卡片会偏出去小半个图标
        private static Rectangle ToLogicalRect(Rectangle rect)
        {
            double scale = GetDisplayScale();
            if (scale <= 1.0 || scale > 4.0)  // 100% 或异常缩放（>400%）原样返回，不做不可靠换算
                return rect;

            return new Rectangle(
                (int)Math.Round(rect.Left / scale),                  // 左边除以缩放比
                (int)Math.Round(rect.Top / scale),                   // 上边同理
                (int)Math.Max(1, Math.Round(rect.Width / scale)),    // 宽至少 1 像素，避免取整成 0
                (int)Math.Max(1, Math.Round(rect.Height / scale)));  // 高同理
        }

        // 取系统 DPI 相对 96 的缩放比  参数：无  返回：1.0 表示 100%，1.5 表示 150%
        // 用 GetDpiForSystem 而不是读控件 DeviceDpi：这里要在窗体定位之前就完成换算
        private static double GetDisplayScale()
        {
            try
            {
                uint dpi = GetDpiForSystem();  // 老系统没有这个导出函数时会走 catch
                if (dpi == 0)                  // 取不到按 100% 处理
                    return 1.0;
                return dpi / 96.0;             // 96 DPI 是 Windows 的逻辑基准
            }
            catch
            {
                return 1.0;                    // 任何异常都退回 1.0，宁可略偏也不抛给调用方
            }
        }

        // 尝试用 shell 接口拿托盘图标的屏幕矩形  参数：trayIcon 托盘图标，rect 输出物理像素矩形  返回：成功为 true
        // 为什么要反射：NotifyIcon 没有公开「图标在屏幕哪里」，只能取出它私有的 window 句柄与 id，
        // 再交给 Shell_NotifyIconGetRect 去问 explorer；任何一步失败都返回 false，由调用方回退到鼠标位置
        private static bool TryGetTrayIconRect(NotifyIcon trayIcon, out Rectangle rect)
        {
            // === 输出参数默认值 ===
            rect = Rectangle.Empty;  // 失败路径统一返回空矩形，调用方不必判空
            if (trayIcon == null)    // 托盘还没建好就直接失败
                return false;

            try
            {
                // === 反射取私有字段 ===
                var windowField = typeof(NotifyIcon).GetField("window",  // NotifyIcon 把宿主窗口藏在私有字段 window 里
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var idField = typeof(NotifyIcon).GetField("id",          // 图标 id 也在私有字段 id 里，两者一起才唯一定位
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (windowField == null || idField == null)              // 字段名随 .NET 版本可能变，取不到就认输
                    return false;

                // === 校验句柄与 id ===
                var nativeWindow = windowField.GetValue(trayIcon) as NativeWindow;  // 必须是已创建的 NativeWindow
                if (nativeWindow == null || nativeWindow.Handle == IntPtr.Zero)     // 句柄为 0 说明托盘还没有真正的窗口
                    return false;

                object idValue = idField.GetValue(trayIcon);                        // id 可能是 int 或 uint，用 Convert 兼容
                if (idValue == null)                                                // 取不到 id 同样放弃
                    return false;

                // === 组装 NOTIFYICONIDENTIFIER ===
                var identifier = new NOTIFYICONIDENTIFIER();                       // 句柄 + id + Guid 共同标识一个托盘图标
                identifier.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));  // cbSize 必须是结构体实际大小，否则 shell 拒绝
                identifier.hWnd = nativeWindow.Handle;                             // 宿主窗口句柄
                identifier.uID = Convert.ToUInt32(idValue);                        // id 转成 uint，字段本身是 UINT
                identifier.guidItem = Guid.Empty;                                  // 按 id 定位时 guidItem 必须留空

                // === 问 shell 要矩形 ===
                RECT nativeRect;                                                                 // 拿到的仍是物理像素
                int hr = Shell_NotifyIconGetRect(ref identifier, out nativeRect);                // 0 = S_OK
                if (hr != 0)                                                                     // 失败不抛异常，交给调用方回退
                    return false;
                if (nativeRect.Right <= nativeRect.Left || nativeRect.Bottom <= nativeRect.Top)  // 宽高非正即无效矩形
                    return false;

                // === 转成托管矩形 ===
                rect = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
                return true;   // 走到这里才算真的拿到了
            }
            catch
            {
                return false;  // 反射交互都可能抛，一律降级为「拿不到」
            }
        }

        // Shell_NotifyIconGetRect 的入参  说明：字段顺序必须与 shell 的同名结构完全一致
        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;     // 结构体大小（字节），调用方必须填
            public IntPtr hWnd;    // 承载托盘图标的窗口句柄
            public uint uID;       // 图标 id（UINT），与 guidItem 二选一
            public Guid guidItem;  // 用 Guid 注册的图标填这里，按 id 定位时留空
        }

        // Win32 的 RECT  说明：右/下是开区间，宽 = Right - Left
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;    // 左边界（物理像素）
            public int Top;     // 上边界
            public int Right;   // 右边界（不含）
            public int Bottom;  // 下边界（不含）
        }

        // 向 shell 查询托盘图标的屏幕矩形（Win7+）  参数：identifier 图标标识，iconLocation 输出物理像素矩形  返回：0 表示成功
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]  // shell32 导出，签名保持原样
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        // 取系统 DPI（Win10 1607+）  返回：当前 DPI，96 即 100%
        // 没有调用 SetProcessDpiAwareness，所以这里拿到的是系统级 DPI 而非单显示器 DPI
        [DllImport("user32.dll")]  // user32 导出；老系统调用会抛，由 GetDisplayScale 兜住
        private static extern uint GetDpiForSystem();

        #endregion
    }
}