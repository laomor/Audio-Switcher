// ============================================================
// SettingsCardForm.Window.cs - 圆角窗口区域、显示隐藏与生命周期收尾
// 功能：用 Region 把无边框窗体裁成圆角卡片，管理显示隐藏、焦点防抖、按键处理与关闭时的资源释放
// 说明：本文件是 SettingsCardForm 的分块（partial），配合其它分块阅读
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FortyOne.AudioSwitcher.HotKeyData;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 窗口形状

        // 无边框窗体的形状完全由 Region 决定：Region 之外的像素既不会被画到，也不接收鼠标
        // 圆角、退出卡收缩、甚至收缩成一条细胶囊，都靠在这里重建 Region 实现

        // 尺寸一变就重建圆角 Region  参数：e 事件参数  返回：无
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);     // 先让基类处理，再按新尺寸重算
            ApplyRoundCorners();  // DPI 变化引起的尺寸重排也走这里
        }

        // 按当前形态重建窗口 Region  参数：无  返回：无
        // 普通形态用固定 CornerRadius；退出卡形态用 ExitVanishRect/ExitVanishRadius 算出的动态矩形与圆角
        private void ApplyRoundCorners()
        {
            // === 尺寸非法时直接放弃 ===
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)  // 窗口被缩到 0 时算路径会抛
                return;                                           // 保留上一次的 Region

            Rectangle bounds;
            int radius;

            // === 选形态 ===
            if (_exitMenuMode)
            {
                bounds = ExitVanishRect();                                // 收缩中的矩形，可能比客户区小
                radius = ExitVanishRadius(bounds, ExitVanishProgress());  // 进度越大半径越小，像胶囊化掉
            }
            else
            {
                bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);  // 普通形态：整块客户区
                radius = CornerRadius;                                              // 固定圆角
            }

            // === 退化情况 ===
            if (bounds.Width <= 0 || bounds.Height <= 0)  // 动画首尾帧可能退化成空
            {
                var emptyPrev = Region;
                Region = new Region(Rectangle.Empty);     // 空 Region 表示什么都不显示
                if (emptyPrev != null)
                    emptyPrev.Dispose();                  // 旧 Region 必须释放，否则每帧漏 GDI 对象
                return;
            }

            // === 半径夹紧 ===
            int maxR = Math.Min(bounds.Width, bounds.Height) / 2;  // 超过短边一半会画出畸形圆弧
            if (radius > maxR) radius = maxR;                      // 钳到上限
            if (radius < 1) radius = 1;                            // 至少 1 像素，0 会让路径退化

            // === 套用新 Region ===
            using (var path = CreateRoundPath(bounds, radius))  // 路径用完即弃，Region 会复制一份
            {
                var previous = Region;
                Region = new Region(path);                      // 整体替换，不能叠加
                if (previous != null)
                    previous.Dispose();                         // 替换之后再释放，绘制时不会用到旧对象
            }
        }

        // 生成圆角矩形路径  参数：bounds 目标矩形，radius 圆角半径（像素）  返回：调用方负责释放的 GraphicsPath
        // 四个角各画 90° 圆弧再连起来；直径 = 半径 × 2 是 AddArc 要的参数形式
        private static GraphicsPath CreateRoundPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();  // 由调用方 using 释放
            int diameter = radius * 2;      // AddArc 要的是外接正方形边长
            // 顺时针补四角：左上 180° → 右上 270° → 右下 0° → 左下 90°
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();             // 闭合轮廓，形成完整圆角矩形
            return path;
        }

        #endregion
        #region 焦点与显示

        // 卡片的显示时机完全由宿主（托盘点击）驱动：这里只管显示前后该复位什么、隐藏之后该释放什么

        private Timer _trimTimer;  // 延时收回工作集的单次定时器

        // 安排一次延时收回工作集  参数：无  返回：无
        // 为什么不在 HideCard 里直接 TrimWorkingSet：隐藏后 WinForms 还要跑几个消息、释放缓存的位图与 Region，
        // 立刻 trim 会把刚要复用的页又换出去；延迟 1500ms 等收尾落定，再一次性把工作集还给系统
        private void ScheduleWorkingSetTrim()
        {
            if (_trimTimer == null)                          // 定时器只建一次，反复显隐不重复分配
            {
                _trimTimer = new Timer { Interval = 1500 };  // 1500ms 够走完收尾，又不至于让用户察觉
                _trimTimer.Tick += TrimTimer_Tick;           // 只挂一次事件，避免每次调度都加处理器
            }
            _trimTimer.Stop();                               // 先停再启，相当于把计时拨回 1500ms
            _trimTimer.Start();                              // 重新计时
        }

        // 取消尚未执行的收回动作  参数：无  返回：无
        private void CancelWorkingSetTrim()
        {
            if (_trimTimer != null)  // 卡片马上要重新显示
                _trimTimer.Stop();   // 只停表不释放：下次还要用
        }

        // 定时器到点：停表并真正收回工作集  参数：sender/e 标准定时器参数  返回：无
        private void TrimTimer_Tick(object sender, EventArgs e)
        {
            _trimTimer.Stop();  // 单次触发：先停再干活，避免重入
            TrimWorkingSet();   // 缩容交给 NativeMemory，内部按需调整
        }

        // 收回本进程工作集，降低隐藏期间的常驻内存  参数：无  返回：无
        private static void TrimWorkingSet()
        {
            NativeMemory.TrimWorkingSet();  // 抽一层，让调用点不直接依赖 P/Invoke
        }

        // 窗口被激活时记下「确实拿到过焦点」  参数：e 事件参数  返回：无
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);  // 基类先处理
            _hadFocus = true;     // 这是 OnDeactivate 防抖判断的前提
        }

        // 失去焦点时决定要不要自动隐藏  参数：e 事件参数  返回：无
        // 关键在 300ms 防抖：刚弹出时系统会先给一次 Deactivate（焦点其实还在托盘/宿主那边），
        // 若立刻隐藏，用户根本看不到卡片；只有「曾经激活过」且「显示已超过 300ms」才真正隐藏
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);                                       // 基类先处理
            if (_exitVanishStart != 0 || _exitVanishDone)               // 正在收缩，或已收缩完
                return;                                                 // 交给退出流程收尾，不抢隐藏时机
            if (_hadFocus && Environment.TickCount - _shownTick > 300)  // 曾激活过且已显示超过 300ms
            {
                _hadFocus = false;                                      // 清标记，避免重复隐藏
                HideCard();                                             // 真正隐藏：复位动画并安排回收
            }
        }

        // 显示完成后主动抢焦点  参数：e 事件参数  返回：无
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);  // 此时句柄已可见
            Activate();       // 抢到焦点才收得到 Esc 与录入按键
        }

        // 按键按下：录入态下吞掉按键，普通态下 Esc 收起卡片  参数：e 按键事件  返回：无
        // 之所以在 KeyDown 而不是 KeyPress 里处理：功能键与修饰键没有字符，只有 KeyDown 拿得到 KeyCode
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_hotKeyCapturing)           // 录入态：所有按键归卡片
            {
                e.SuppressKeyPress = true;  // 吞掉按键，既防嘀声也防别的窗口收到

                if (!e.Control && !e.Alt && !e.Shift &&                    // 必须完全没有修饰键
                    (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back))  // Delete/Backspace 视为清空
                {
                    RequestClearHotKey();      // 交给宿主清空，随后由宿主回推文本
                    return;                    // 清空后立即退出录入态
                }

                if (e.KeyCode == Keys.Escape)  // Esc 取消录入，回滚到捕获前的状态
                    CancelHotKeyCapture();     // 取消后不再处理这次按键
                return;
            }

            if (e.KeyCode == Keys.Escape)  // 非录入态：Esc 收起卡片
            {
                e.Handled = true;  // 标记已处理，别再往下传
                HideCard();        // 隐藏并复位
                return;
            }
            base.OnKeyDown(e);     // 其余按键交回基类（含 Tab 焦点切换）
        }

        // 按键抬起：录入态下把这次按键组合成热键  参数：e 按键事件  返回：无
        // 用 KeyUp 而不是 KeyDown：按下 Ctrl/Shift 时 KeyDown 已先到，抬起时才拿得到完整组合
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (!_hotKeyCapturing)  // 非录入态不关心抬起
            {
                base.OnKeyUp(e);    // 保持默认行为
                return;
            }

            if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu ||  // 单独的修饰键
                e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)                                        // Win 键同理
                return;

            var modifiers = Modifiers.None;                 // 从事件里重组修饰键位
            if (e.Control) modifiers |= Modifiers.Control;  // Control 位
            if (e.Alt) modifiers |= Modifiers.Alt;          // Alt 位
            if (e.Shift) modifiers |= Modifiers.Shift;      // Shift 位

            if (modifiers == Modifiers.None && !IsSafeFunctionKey(e.KeyCode))  // 无修饰键时只接受安全功能键
                return;                                                        // 组合不合法就静默忽略

            CancelHotKeyCapture();        // 先退出录入态，再由事件通知宿主

            var handler = HotKeyChanged;  // 复制引用，防止回调里退订
            if (handler != null)          // 有订阅者才构造事件参数
                handler(this, new SettingsCardHotKeyEventArgs(e.KeyCode, modifiers));
        }

        // 窗体关闭：逐项释放卡片持有的定时器与常驻位图  参数：e 关闭事件  返回：无
        // 先停动画再释放资源：定时器若在释放后再 Tick，会摸到已经 Dispose 的对象
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // === 停滚动与波纹动画 ===
            StopScrollAnimation();       // 先停补间，下面释放时才不会被 Tick 打断
            if (_scrollTimer != null)
            {
                _scrollTimer.Dispose();  // 定时器是组件级资源，必须显式释放
                _scrollTimer = null;     // 置空，别处靠 null 判断是否已释放
            }
            StopSpreadAnimation();       // 波纹动画同理
            if (_spreadTimer != null)
            {
                _spreadTimer.Dispose();  // 同样要释放
                _spreadTimer = null;     // 置空
            }

            // === 释放位图与定时器 ===
            if (_commBadge != null)
                _commBadge.Dispose();        // 位图不释放会一直占着 GDI 句柄
            DisposeWarnBitmaps();            // 警告位图由 Warn 分块统一释放
            if (_trimTimer != null)
            {
                _trimTimer.Stop();           // 先停再放，避免释放瞬间还有一次 Tick
                _trimTimer.Dispose();        // 释放
                _trimTimer = null;           // 置空
            }
            if (_hotKeyWarnTimer != null)
            {
                _hotKeyWarnTimer.Stop();     // 热键警告定时器同样要收
                _hotKeyWarnTimer.Dispose();  // 释放
                _hotKeyWarnTimer = null;     // 置空
            }

            // === 统一停掉各类补间定时器 ===
            StopTimer(ref _checkTimer);  // 其余补间定时器统一走 StopTimer
            StopTimer(ref _hotKeyFocusTimer);
            StopTimer(ref _hotKeyHoverTimer);
            StopTimer(ref _switchTimer);
            StopTimer(ref _exitVanishTimer);
            StopTimer(ref _starTimer);   // 星标补间是这一组最后一个
            base.OnFormClosed(e);        // 基类销毁句柄，必须放在资源释放之后
        }

        // 停掉并释放一个定时器，同时把字段置空  参数：timer 以引用方式传入的定时器字段  返回：无
        // 用 ref 是为了省掉每个调用点的 null 判断，也让「停止 + 释放 + 置空」三步不会漏做
        private static void StopTimer(ref Timer timer)
        {
            if (timer == null)  // 已经是空就直接返回
                return;
            timer.Stop();       // 先停表，防止释放后还有一次 Tick
            timer.Dispose();    // 释放底层句柄
            timer = null;       // 置空，调用方的字段同步变 null
        }

        // 隐藏卡片并把所有瞬态状态复位  参数：无  返回：无
        // 隐藏不是简单 Hide()：动画、拖动、按下、录入态都要清掉，否则下次弹出来会带着上次的残影
        public void HideCard()
        {
            // === 记录隐藏时刻 ===
            LastHideTick = Environment.TickCount;  // 宿主据此判断「刚收起又点托盘」

            // === 复位动画与交互态 ===
            StopScrollAnimation();                 // 滚动补间停掉，否则隐藏后还在跑
            CancelHotKeyCapture();                 // 录入态一并取消，回滚未确认文本
            ResetSpread();                         // 波纹收起，下次点齿轮从零扩散
            StopHotKeyWarning();                   // 警告动画停掉，避免隐藏后仍闪
            _draggingThumb = false;                // 隐藏时可能正处于拖动中
            _gearPressed = false;                  // 否则下次显示会画成按下态

            // === 真正隐藏并安排回收 ===
            Hide();                    // 不是 Close：窗口要反复复用
            ScheduleWorkingSetTrim();  // 现在直接 trim 会把刚要复用的页换出去
        }

        // 把卡片显示到托盘图标上方  参数：trayIcon 托盘图标  返回：无
        // 每次显示都从干净状态开始：取消待执行的 trim、复位动画、把列表滚到默认行
        public void ShowNear(NotifyIcon trayIcon)
        {
            // === 取消待执行的回收 ===
            CancelWorkingSetTrim();              // 马上要显示，取消还没执行的 trim

            // === 复位显示状态 ===
            StopScrollAnimation();               // 停补间，下面要直接改偏移
            ResetSpread();                       // 波纹收起，避免上次展开态残留
            _scrollOffset = DefaultRowOffset();  // 让当前默认设备那一行可见
            _targetOffset = _scrollOffset;       // 目标同步，避免首帧就触发补间
            _hoverDeviceIndex = -1;              // 鼠标已不是上次那个点
            _hadFocus = false;                   // 防抖窗口从这次显示重新算
            _shownTick = Environment.TickCount;  // 记录显示时刻（毫秒）

            // === 定位并弹出 ===
            if (!IsHandleCreated)                          // 下面定位需要真实句柄
                CreateHandle();                            // 句柄没建就先建
            Location = CalculateLocation(trayIcon, Size);  // 位置由图标矩形算，尺寸用当前值
            Show();                                        // 先显示
            Activate();                                    // 显示后再抢焦点，Esc 与录入才收得到
        }

        #endregion
    }
}