// ============================================================
// SettingsCardForm.Draw.cs - 卡片自绘：设备行、滚动条、齿轮与退出动画
// 功能：用 GDI+ 画出设备行、收藏星、通信角标、滚动条与齿轮，并驱动退出胶囊的收缩消散
// 说明：本文件是 SettingsCardForm 的分块（partial），只负责绘制，不发起任何设备操作
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 绘制

        // 普通模式下整块客户区铺竖向渐变；退出模式下只画收缩中的胶囊，避免整窗重绘的闪烁
        // 铺底绘制  参数：e 携带绘制图面与脏区域  返回：无（坐标为窗口客户区坐标）
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // === 退出菜单模式：只画消散中的胶囊 ===
            if (_exitMenuMode)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // 当前帧的胶囊矩形（客户区坐标）：未启动动画时就是整块退出行
                var pillRect = ExitVanishRect();
                // 收缩末期宽或高会归零，此时整帧无需绘制
                if (pillRect.Width <= 0 || pillRect.Height <= 0)
                    return;

                // 先铺一层边框色：内层收缩后露出的那一圈就是它
                using (var border = new SolidBrush(ExitBorder))
                    e.Graphics.FillRectangle(border, pillRect);

                // t 是线性时间进度 0~1；inkT 把前 10% 拉成 0~1，用来在收缩正式开始前先压深底色
                double t = ExitVanishProgress();
                double inkT = t <= 0 ? 0 : (t >= 0.10 ? 1 : t / 0.10);
                // 内层底色按状态取值：动画中、按下、悬停、静置各不相同
                Color innerBack;
                // 底色优先级：动画中 > 按下 > 悬停 > 静置；动画优先，收缩过程不该被悬停打断
                if (_exitVanishStart != 0)
                    innerBack = LerpColor(CardWhite, ExitBorder, inkT);   // 动画中：白 → 边框色
                else if (_exitRowPressed)
                    innerBack = ExitPressBack;                            // 按下：按压底色
                else if (_exitRowHover)
                    innerBack = RowHoverBack;                             // 悬停：悬停底色
                else
                    innerBack = LerpColor(CardWhite, ExitBorder, inkT);   // 静置：inkT 为 0，结果就是卡片白

                // 内层四边各缩 2 像素：留下的这一圈就是胶囊的边框色
                var inner = new Rectangle(pillRect.X + 2, pillRect.Y + 2, pillRect.Width - 4, pillRect.Height - 4);
                // 内层还够大就走圆角路径，否则退化成直角实心块
                if (inner.Width > 2 && inner.Height > 6)
                {
                    // 圆角半径不超过内层短边的一半，否则 AddArc 会画出畸形圆弧
                    int rad = Math.Min(ExitRadius - 2, Math.Min(inner.Width, inner.Height) / 2);
                    if (rad < 1) rad = 1;
                    using (var path = CreateRoundPath(inner, rad))
                    using (var brush = new SolidBrush(innerBack))
                        e.Graphics.FillPath(brush, path);
                }
                // 内层已窄到画不出圆角：直接填满胶囊，保证最后几帧还有一点实色
                else if (pillRect.Width > 0 && pillRect.Height > 0)
                {
                    using (var brush = new SolidBrush(innerBack))
                        e.Graphics.FillRectangle(brush, pillRect);
                }
                return;
            }

            // === 普通模式：整块客户区铺竖向渐变 ===
            var rect = ClientRectangle;
            // 窗口尚未布局或尺寸退化时回退基类实现，避免拿 0 宽矩形构造渐变刷
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                base.OnPaintBackground(e);
                return;
            }

            // 从 CardBackTop 到 CardBackBottom 的竖向渐变，是整张卡片的底色
            using (var brush = new LinearGradientBrush(rect, CardBackTop, CardBackBottom, LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(brush, rect);
        }

        // 按由底到顶的叠放顺序绘制：设备行 → 滚动条 → 边框 → 展开区 → 设置行 → 齿轮 → 热键提示
        // 分层绘制  参数：e 携带绘制图面  返回：无（坐标为窗口客户区坐标）
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);   // 先让基类走完默认流程（本窗体没挂背景图，代价可忽略）
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // 退出菜单模式整个客户区只有一行胶囊，交给 DrawExitRow 独占绘制
            if (_exitMenuMode)
            {
                DrawExitRow(e.Graphics);
                return;
            }

            // 设备行与滚动条先画，边框高光和展开区后画，才能压在最上层
            DrawRows(e.Graphics);
            DrawScrollbar(e.Graphics);

            // 外边框路径内缩 1 像素：1px 描边正好落在客户区内侧，不会被窗口边缘裁掉一半
            using (var pen = new Pen(CardBorder))
            using (var path = CreateRoundPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), CornerRadius))
            {
                e.Graphics.DrawPath(pen, path);
            }

            // 展开区已经填满时内层高光会被完全遮住，省掉这一次描边
            if (!_spreadFilled)
            {
                using (var pen = new Pen(CardInnerHighlight))
                using (var path = CreateRoundPath(new Rectangle(1, 1, ClientSize.Width - 3, ClientSize.Height - 3), CornerRadius - 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            // 展开区与设置行叠在列表之上，齿轮始终是最后一层
            DrawSpread(e.Graphics);
            DrawSettingRows(e.Graphics);
            DrawGearButton(e.Graphics);

            // 热键注册失败时才提示，且标签页动画进行中先不画，避免文案来回闪
            if (!_spreadExpanded && !IsSpreadBusy)
                DrawHotKeyWarning(e.Graphics);
        }

        #region 退出胶囊消散

        // 启动退出胶囊的收缩消散：只置时间戳并拉起 16ms 计时器，每帧的绘制由 OnPaintBackground 跟进
        // 启动消散  参数：无  返回：无
        private void BeginExitVanish()
        {
            // 已在播放或已播完都不重启：重置时间戳会让曲线从头再来一次
            if (_exitVanishStart != 0 || _exitVanishDone)
                return;

            _exitVanishStart = Environment.TickCount;   // 起始时间戳（毫秒），整条曲线的 0 点
            // TickCount 回绕到 0 时用 1 顶上：本类约定 0 表示“动画未启动”
            if (_exitVanishStart == 0)
                _exitVanishStart = 1;

            // 动画期间不再响应鼠标状态，固定按最终形态收尾
            _exitRowHover = false;
            _exitRowPressed = false;

            // 首次使用时才创建计时器：16ms ≈ 60fps，与设备切换动画同频
            if (_exitVanishTimer == null)
            {
                _exitVanishTimer = new Timer { Interval = 16 };
                _exitVanishTimer.Tick += ExitVanishTimer_Tick;
            }

            // 圆角区域要按胶囊当前尺寸重算，否则收缩后四角会残留上一帧的像素
            ApplyRoundCorners();
            _exitVanishTimer.Start();   // 启动逐帧计时器
            Invalidate();               // 立刻刷一帧，不必等到第一个 Tick 才出现动画
        }

        // 消散动画的逐帧回调：每 16ms 检查一次进度，走满后收尾并通知外部
        // 逐帧推进  参数：sender 计时器  e 事件参数  返回：无
        private void ExitVanishTimer_Tick(object sender, EventArgs e)
        {
            // 起始时间戳被清掉（外部取消或已收尾）：停表，避免空转
            if (_exitVanishStart == 0)
            {
                _exitVanishTimer.Stop();
                return;
            }

            // 曲线走满：先停表，再按最终圆角复位，最后才通知外部去关窗口
            if (ExitVanishProgress() >= 1)
            {
                _exitVanishTimer.Stop();   // 先停表，收尾过程不应再被 Tick 打断
                ApplyRoundCorners();
                _exitVanishStart = 0;      // 清零起始时间戳，回到“未启动”状态
                FinishExitVanish();        // 发事件，由主窗体真正关闭卡片并退出程序
                return;
            }

            // 未满一帧：只更新窗口圆角并重绘，新的胶囊尺寸在 OnPaintBackground 里生效
            ApplyRoundCorners();
            Invalidate();
        }

        // 消散结束的收尾：把 ExitRequested 发给主窗体，由它执行真正的退出动作
        // 收尾通知  参数：无  返回：无
        private void FinishExitVanish()
        {
            // _exitVanishDone 兜住重复调用：ExitRequested 只发一次
            if (_exitVanishDone)
                return;
            _exitVanishDone = true;   // 先置标志再发事件，订阅者里再次触发也不会重入

            // 复制到局部变量再判空：订阅者在回调里退订时这里不会空引用
            var handler = ExitRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        // 当前消散进度，只取线性时间进度（缓动在各自的取值函数里做）
        // 取消散进度  参数：无  返回：0~1；动画未启动时为 0
        private double ExitVanishProgress()
        {
            if (_exitVanishStart == 0)
                return 0;
            return TweenSince(_exitVanishStart, ExitVanishDuration);   // 换算并钳位成 0~1
        }

        // 消散动画的缓动：前半段 4t³ 加速，后半段用对称的反向三次曲线减速
        // 缓动  参数：t 线性进度 0~1  返回：缓动后的进度 0~1
        private static double ExitVanishEase(double t)
        {
            // 端点直接返回，省掉后面的运算
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            // 前半段 4t³：t=0.5 时恰好 0.5，且 t=0 处导数为 0（起步慢）
            if (t < 0.5) return 4 * t * t * t;
            // 后半段做对称：令 u=2-2t，1-u³/2 在 t=0.5 处同样为 0.5，两段无缝相接
            double u = -2 * t + 2;
            return 1 - (u * u * u) / 2;
        }

        // 胶囊的横向缩放曲线：分五个时间片给出宽度倍率，整体是“先鼓一下再被横向抽走”
        // 横向倍率  参数：t 线性进度 0~1  返回：相对原始宽度的倍数
        private static double ExitVanishScaleX(double t)
        {
            // 时间片按整段消散时长归一化（0.19 / 0.30 / 0.58 / 0.72 是分界点）：
            //   0~0.19     横向鼓到 1.06，纵向同时压到 0.12——“按下去又弹开”的点击回馈
            //   0.19~0.30  停在这个扁宽形态，让回馈被看见
            //   0.30~0.58  主收缩：1.06 → 0.146，视觉上是一根横条快速缩没
            //   0.58~0.72  收尾：0.146 → 0.010，几乎成一条线
            //   0.72~1.00  0.010 → 0，最后归零
            if (t <= 0) return 1.00;   // 未开始
            if (t < 0.19) return 1.00 + 0.06 * ExitVanishEase(t / 0.19);   // 鼓出
            if (t < 0.30) return 1.06;   // 保持
            if (t < 0.58) return 1.06 + (0.146 - 1.06) * ExitVanishEase((t - 0.30) / 0.28);   // 主收缩
            if (t < 0.72) return 0.146 + (0.010 - 0.146) * ExitVanishEase((t - 0.58) / 0.14);   // 收尾
            if (t < 1.00) return 0.010 - 0.010 * ExitVanishEase((t - 0.72) / 0.28);   // 归零
            return 0;   // 曲线结束：宽度为 0
        }

        // 胶囊的纵向缩放曲线：先迅速压扁到 0.12 并基本不再变化，让收缩看起来是“横向被吸走”
        // 纵向倍率  参数：t 线性进度 0~1  返回：相对原始高度的倍数
        private static double ExitVanishScaleY(double t)
        {
            if (t <= 0) return 1.00;   // 未开始
            if (t < 0.19) return 1.00 + (0.12 - 1.00) * ExitVanishEase(t / 0.19);   // 压扁
            if (t < 0.30) return 0.12;   // 保持扁片
            if (t < 0.58) return 0.12 + (0.115 - 0.12) * ExitVanishEase((t - 0.30) / 0.28);   // 微缩
            if (t < 0.72) return 0.115 + (0.110 - 0.115) * ExitVanishEase((t - 0.58) / 0.14);   // 再微缩
            return 0.110;   // 锁死在 0.110：最终是一根细横条而不是一个点
        }

        // 当前帧胶囊的绘制矩形：以中心为锚点按两条曲线收缩，过程不会跑偏
        // 胶囊矩形  参数：无  返回：矩形（窗口客户区坐标）
        private Rectangle ExitVanishRect()
        {
            var full = ExitRowRect;
            if (_exitVanishStart == 0)
                return full;   // 未启动：就是退出行的满尺寸矩形

            double t = ExitVanishProgress();
            int w = (int)Math.Round(full.Width * ExitVanishScaleX(t));    // 宽度按横向曲线缩放
            int h = (int)Math.Round(full.Height * ExitVanishScaleY(t));   // 高度按纵向曲线缩放
            // 收缩末期宽高趋近 0，钳到 0 让上层按“不可见”直接跳过
            if (w < 0) w = 0;
            if (h < 0) h = 0;

            // 居中收缩：X/Y 各补上尺寸差的一半
            return new Rectangle(full.X + (full.Width - w) / 2, full.Y + (full.Height - h) / 2, w, h);
        }

        // 当前帧胶囊的圆角半径：随纵向缩放同步变小，细横条才不会被圆角吃掉
        // 胶囊圆角  参数：rect 当前胶囊矩形  t 线性进度  返回：像素半径
        private static int ExitVanishRadius(Rectangle rect, double t)
        {
            int maxR = Math.Min(rect.Width, rect.Height) / 2;   // 圆角上限：短边的一半
            if (maxR < 1) return 0;                             // 已经细到画不出圆弧
            int want = (int)Math.Round(ExitRadius * ExitVanishScaleY(t));   // 原始半径按纵向倍率缩放
            if (want > maxR) want = maxR;                       // 不超过短边一半
            if (want < 1) want = 1;                             // 至少 1：半径 0 会让圆角路径退化成直角
            return want;
        }

        #endregion

        // 绘制胶囊里的“退出”二字：只在动画前 14% 出现，之后只剩纯色块继续收缩
        // 退出文字  参数：g 绘制图面  返回：无（坐标为退出行客户区坐标）
        private void DrawExitRow(Graphics g)
        {
            var rect = ExitVanishRect();
            // 太小了，文字已经不可读
            if (rect.Width <= 4 || rect.Height <= 4)
                return;

            // t 为 0 表示静置：文字完全不透明
            double t = ExitVanishProgress();
            int alpha;
            if (t <= 0)
            {
                alpha = 255;   // 静置：不透明
            }
            else if (t >= 0.14)
            {
                return;   // 前 14% 之后不再画文字：避免文字被横向压扁
            }
            else
            {
                alpha = (int)Math.Round(255 * (1 - t / 0.14));   // 255 → 0 线性淡出
            }

            using (var brush = new SolidBrush(Color.FromArgb(alpha, DarkGrey)))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                g.DrawString("退出", _exitFont, brush, rect, format);   // 居中绘制：矩形本身在收缩，文字自然跟着中心走
            }
        }

        // ===================== 设备切换动画 =====================
        // 启动“默认项高亮块掉落 + 通信角标滑入”动画
        // 1. 判定落点是否真的变化；2. 未变则续跑计时器；3. 变了则重置起始时间戳从头播
        // 启动切换动画  参数：dropIndex 高亮块落点行（-1 不变）  badgeIndex 角标滑入行（-1 不变）  fromIndex 高亮块原行（-1 自行推断）  返回：无
        private void StartDeviceSwitch(int dropIndex, int badgeIndex, int fromIndex)
        {
            // 两个落点都没给：没有动画可播
            if (dropIndex < 0 && badgeIndex < 0)
                return;

            // 起点行优先用调用方给的值；否则沿用上一次动画的目标行；再没有就取当前默认行
            int startFrom = fromIndex;
            if (startFrom < 0)
                startFrom = (_switchStart != 0 && _switchTargetIndex >= 0) ? _switchTargetIndex : DefaultDeviceIndex();

            // 落点真的变了才重置动画；同一目标重复调用若也重置，动画会被反复拽回起点
            bool targetChanged = (dropIndex >= 0 && dropIndex != _switchTargetIndex)
                              || (badgeIndex >= 0 && badgeIndex != _badgeTargetIndex);

            if (dropIndex >= 0)
                _switchTargetIndex = dropIndex;   // 落点记到状态里，绘制时据此判断哪一行该播掉落
            if (badgeIndex >= 0)
                _badgeTargetIndex = badgeIndex;   // 角标落点同理

            // 目标没变：计时器可能被上一轮停掉了，续上后只重刷列表区
            if (_switchStart != 0 && !targetChanged)
            {
                if (_switchTimer != null && !_switchTimer.Enabled)
                    _switchTimer.Start();   // 续跑被停掉的计时器
                InvalidateList();           // 只刷列表区就够
                return;
            }

            _switchFromIndex = startFrom;              // 原行：切换开始 120ms 内，新默认行退回这里即判为过期快照
            _switchStart = Environment.TickCount;      // 动画起点（毫秒），三处动画都以它为基准
            // TickCount 回绕到 0 时用 1 顶上：0 在本类里表示“没有动画”
            if (_switchStart == 0)
                _switchStart = 1;

            // 首次使用时才创建计时器：16ms ≈ 60fps
            if (_switchTimer == null)
            {
                _switchTimer = new Timer { Interval = 16 };
                _switchTimer.Tick += SwitchTimer_Tick;
            }

            if (!_switchTimer.Enabled)
                _switchTimer.Start();   // 已在跑就别重复 Start

            InvalidateList();   // 进入动画前先刷一帧，从原状态起步
        }

        // 设备切换动画的逐帧回调：总时长走完就把动画状态清零，绘制自动回到静态分支
        // 逐帧推进  参数：sender 计时器  e 事件参数  返回：无
        private void SwitchTimer_Tick(object sender, EventArgs e)
        {
            // 起始时间戳已被清掉：停表
            if (_switchStart == 0)
            {
                _switchTimer.Stop();
                return;
            }

            // 走满 SwitchTotalDuration（掉落 + 角标延迟 + 角标展开）
            if (Environment.TickCount - _switchStart >= SwitchTotalDuration)
            {
                _switchStart = 0;   // 清零即“没有动画”，绘制会走不缩放的分支
                // 一次性复位：两个目标行与原行都清空，避免下一轮动画沿用旧落点
                _switchTargetIndex = -1;
                _badgeTargetIndex = -1;
                _switchFromIndex = -1;
            }

            InvalidateList();   // 先重绘再停表：保证最后一帧画的是复位后的静态状态

            // 上面刚清零就顺手停表，避免计时器空转
            if (_switchStart == 0)
                _switchTimer.Stop();
        }

        // 只把设备列表区标记为脏，动画每帧只重绘这一块
        // 局部重绘  参数：无  返回：无
        private void InvalidateList()
        {
            // 列表区从 ListTop 起、高 ViewHeight；齿轮、边框与展开区都不在脏区内，重绘面积与闪烁都最小
            Invalidate(new Rectangle(0, ListTop, Width, ViewHeight));
        }

        // 三次贝塞尔 ease-out 的反解：曲线按参数 s 定义，想让“时间走到 t”对应到正确位移，
        // 只能在 s 上做数值反解。这里用 20 次二分把 s 逼到横坐标误差小于 0.0005 再取纵坐标：
        // 既不用建查表，也不必迭代到严格收敛，460ms 的动画肉眼看不出台阶。
        // 反解缓动  参数：t 时间进度 0~1（横轴）  返回：位移进度 0~1（纵轴）
        private static double EaseOutRef(double t)
        {
            // 端点直接返回，省掉二分
            if (t <= 0) return 0;
            if (t >= 1) return 1;

            // lo/hi 是参数 s 的搜索区间；曲线单调，用 s=t 作初值已经离解不远
            double lo = 0, hi = 1, s = t;
            // 20 次二分把区间压到 1/2^20，远小于肉眼能分辨的位移
            for (int i = 0; i < 20; i++)
            {
                // 由 s 求贝塞尔横坐标：两个控制点 x 取 0.23 / 0.32，得到“先快后慢”的时间轴
                double xs = 3 * (1 - s) * (1 - s) * s * 0.23 + 3 * (1 - s) * s * s * 0.32 + s * s * s;
                // 横坐标误差 < 0.0005 即认为收敛
                if (Math.Abs(xs - t) < 0.0005)
                    break;
                if (xs < t) lo = s;   // xs 偏小说明 s 取小了：把左边界推到 s
                else hi = s;          // 否则收缩右边界
                s = (lo + hi) / 2;    // 取中点继续二分
            }
            // 纵坐标的两个控制点都取 1.0：出口切线水平，所以是 ease-out（先快后慢）
            return 3 * (1 - s) * (1 - s) * s * 1.0 + 3 * (1 - s) * s * s * 1.0 + s * s * s;
        }

        // 高亮块掉落的进度
        // 掉落进度  参数：无  返回：0~1 的缓动进度
        private double DropProgress()
        {
            // 以动画起点为 0 点，按 SwitchDropDuration 归一化
            double t = (double)(Environment.TickCount - _switchStart) / SwitchDropDuration;
            return EaseOutRef(t);   // EaseOutRef 内部已对 <0 / >1 钳位，这里不必先夹
        }

        // 通信角标滑入的进度：比高亮块晚 BadgeRevealDelay 毫秒起步
        // 角标进度  参数：无  返回：0~1 的缓动进度
        private double BadgeProgress()
        {
            // 减去延迟量再归一化；未到延迟时为负，EaseOutRef 会返回 0
            double t = (double)(Environment.TickCount - _switchStart - BadgeRevealDelay) / SwitchDropDuration;
            return EaseOutRef(t);   // 与掉落共用同一条缓动曲线，节奏才一致
        }

        // 线性插值两个颜色，用于高亮块、星形与文字的颜色过渡
        // 混色  参数：from 起点色  to 终点色  t 0~1 比例  返回：插值色（alpha 由调用方另给）
        private static Color LerpColor(Color from, Color to, double t)
        {
            // 端点直接返回，避免浮点误差带出 1 的色差
            if (t <= 0) return from;
            if (t >= 1) return to;
            // 只插值 RGB：alpha 由外层的 Color.FromArgb(alpha, color) 单独控制
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * t),
                (int)Math.Round(from.G + (to.G - from.G) * t),
                (int)Math.Round(from.B + (to.B - from.B) * t));
        }

        // ===================== 设备行绘制 =====================
        // 绘制列表里当前可见的设备行：滚出视口整行跳过，滚到边界的半行由裁剪切掉
        // 绘制可见行  参数：g 绘制图面  返回：无（坐标为列表区客户区坐标）
        private void DrawRows(Graphics g)
        {
            // 列表为空：没有可画的行
            if (_devices.Count == 0)
                return;

            // 列表区裁剪矩形：与布局分块共用 ListTop / ViewHeight，滚动边界才对得上
            var listClip = new Rectangle(0, ListTop, Width, ViewHeight);
            var saved = g.Save();   // 保存当前裁剪与变换状态，退出时恢复
            g.SetClip(listClip, CombineMode.Intersect);   // 只允许在列表区内绘制，滚到边界的半行会被切掉
            try
            {
                // 由滚动偏移反算第一行下标；多画一行（+1）盖住行首/行尾的半行
                int first = (int)Math.Floor(_scrollOffset / (RowHeight + RowGap));
                if (first < 0) first = 0;   // 偏移不会为负，钳到 0 保证行下标合法
                int last = first + VisibleRows + 1;   // 多算一行，覆盖滚动中露出的半行
                if (last > _devices.Count - 1) last = _devices.Count - 1;   // 不超过最后一行

                // 每行 = 左侧设备块 + 右侧收藏星块，两者之间是 BlockGap 的间隙
                for (int index = first; index <= last; index++)
                {
                    var device = _devices[index];
                    bool hovered = index == _hoverDeviceIndex;
                    DrawDeviceBlock(g, LeftBlockRect(index), device, hovered, index);   // 左：设备块（名字、接口名、通信角标）
                    DrawStarBlock(g, RightBlockRect(index), device.IsFavourite, device.Id);   // 右：收藏星块
                }
            }
            finally
            {
                // 无论绘制是否抛异常都要恢复裁剪，否则后续绘制全被裁掉
                g.Restore(saved);
            }
        }

        // 绘制单个设备块：左圆角底、默认项高亮块、名字与接口名、通信角标
        // 设备块  参数：g 绘制图面  rect 块矩形（列表区客户区坐标）  device 设备数据  hovered 是否悬停  index 行下标  返回：无
        private void DrawDeviceBlock(Graphics g, Rectangle rect, SettingsCardDevice device, bool hovered, int index)
        {
            // 整行都在视口之外：滚动时最容易命中这里，先跳掉省一次路径构造
            if (rect.Bottom < ListTop || rect.Top > ListTop + ViewHeight)
                return;

            // drop=1 表示没有掉落动画，高亮块直接按整行绘制
            double drop = 1.0;
            if (device.IsDefault && _switchStart != 0 && _switchTargetIndex >= 0 && index == _switchTargetIndex)
                drop = DropProgress();   // 只对“本次动画的目标行”算掉落进度

            // 左圆角块：设备块只圆左边两角，右边要和收藏星块拼在一起
            using (var path = CreateSideRoundPath(rect, BlockRadius, true))
            {
                // 默认项用卡片白打底（随后被高亮块覆盖）；悬停项用淡蓝；默认项悬停时保持白底
                using (var fill = new SolidBrush(hovered && !device.IsDefault ? RowHoverBack : CardWhite))
                    g.FillPath(fill, path);
                using (var pen = new Pen(BlockBorder, 1f))   // 1px 描边压住路径边缘，滚动时边缘不会抖
                    g.DrawPath(pen, path);
            }

            // === 默认项高亮块 ===
            if (device.IsDefault)
            {
                if (drop >= 0.999)   // 动画结束或没在动画：直接画满整行
                {
                    // 落地态：整行填 AccentBlue
                    using (var path = CreateSideRoundPath(rect, BlockRadius, true))
                    {
                        using (var fill = new SolidBrush(AccentBlue))
                            g.FillPath(fill, path);
                        using (var pen = new Pen(BlockBorder, 1f))
                            g.DrawPath(pen, path);
                    }
                }
                else
                {
                    // 以中心为锚点缩放：纵向中心从上一行的高度线性落到本行中心
                    double cy = rect.Y - rect.Height / 2.0 + rect.Height * drop;
                    double cw = rect.Width * drop;    // 宽度同比缩放，形成“由小落大”的观感
                    double chh = rect.Height * drop;  // 高度同比缩放
                    // 先算缩放后的矩形再交给路径工具，逐帧只变参数、不改路径语义
                    var blockRect = new Rectangle(
                        (int)Math.Round(rect.X + (rect.Width - cw) / 2.0),
                        (int)Math.Round(cy - chh / 2.0),
                        Math.Max(1, (int)Math.Round(cw)),
                        Math.Max(1, (int)Math.Round(chh)));

                    // 前段用 0.5→1.0 的透明度渐显，落地时刚好全不透明
                    int alpha = (int)Math.Round(255 * Math.Min(1.0, 0.5 + drop * 0.7));
                    if (alpha > 255) alpha = 255;   // Round 后可能溢出 255

                    // 裁剪到本行圆角路径：掉落中的块不会溢出到相邻行
                    using (var clipPath = CreateSideRoundPath(rect, BlockRadius, true))
                    {
                        var state = g.Save();   // 保存裁剪状态，画完再恢复
                        g.SetClip(clipPath, CombineMode.Intersect);   // 只在本行圆角范围内画高亮块
                        using (var path = CreateSideRoundPath(blockRect, BlockRadius, true))
                        {
                            // alpha 随 drop 增大，落地时不透明
                            using (var fill = new SolidBrush(Color.FromArgb(alpha, AccentBlue)))
                                g.FillPath(fill, path);
                        }
                        g.Restore(state);

                        // 描边始终按整行路径画：掉落过程中边框保持完整，不会跟着变小
                        using (var pen = new Pen(BlockBorder, 1f))
                            g.DrawPath(pen, clipPath);
                    }
                }
            }

            // === 文字排版 ===
            int textLeft = rect.X + BlockRadius;         // 左边从圆角之后起笔，避免文字压到圆弧
            int textWidth = rect.Right - textLeft - 10;  // 右侧留 10 像素呼吸位
            if (textWidth < 20)
                textWidth = 20;   // 面板极窄时仍给文字留最小宽度

            int nameWidth = textWidth;
            // 通信设备要在右侧给角标让位，否则名字会压到角标上
            if (device.IsCommunicationsDevice)
            {
                // 名字宽度 = 块右缘 - 角标内边距 - 角标尺寸 - 间距 - textLeft
                nameWidth = rect.Right - CommBadgeRightPad - CommBadgeSize - 6 - textLeft;
                if (nameWidth < 20)
                    nameWidth = 20;
            }

            // 名字颜色：默认项随 drop 从深灰渐变到反白，落地时对比度最高
            var nameColor = device.IsDefault ? LerpColor(DarkGrey, OnAccentWhite, drop) : DarkGrey;
            var interfaceColor = device.IsDefault
                ? LerpColor(MiddleGrey, SwitchTint, drop)   // 接口名用浅蓝，弱化次要信息
                : MiddleGrey;

            string name = device.Name ?? "";   // 名字可能为 null，统一成空串免得 DrawString 抛异常
            bool hasInterface = !string.IsNullOrEmpty(device.InterfaceName);
            // 超长名字用省略号截断且禁止换行：保证行高固定，滚动计算不被文字撑开
            using (var brush = new SolidBrush(nameColor))
            using (var format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center
            })
            {
                // 有接口名：名字占上 17 像素，接口名占下 16 像素
                if (hasInterface)
                {
                    var nameRect = new RectangleF(textLeft, rect.Y + 6, nameWidth, 17);
                    g.DrawString(name, _nameFont, brush, nameRect, format);
                    // 角标贴着名字行垂直居中
                    if (device.IsCommunicationsDevice)
                        DrawCommBadge(g, rect, nameRect, index);
                    using (var interfaceBrush = new SolidBrush(interfaceColor))
                    {
                        g.DrawString(device.InterfaceName, _interfaceFont, interfaceBrush,
                            new RectangleF(textLeft, rect.Y + 22, textWidth, 16), format);
                    }
                }
                // 无接口名：名字占满整行高度并垂直居中
                else
                {
                    var nameRect = new RectangleF(textLeft, rect.Y, nameWidth, rect.Height);
                    g.DrawString(name, _nameFont, brush, nameRect, format);
                    // 角标按整行居中
                    if (device.IsCommunicationsDevice)
                        DrawCommBadge(g, rect, nameRect, index);
                }
            }
        }

        // ===================== 通信角标 =====================
        // 从程序集嵌入资源里找出通信设备角标图（文件名以 .c.png 结尾）
        // 加载角标图  参数：无  返回：位图；资源缺失或解码失败时返回 null
        private static Image LoadCommBadgeImage()
        {
            try
            {
                var assembly = typeof(SettingsCardForm).Assembly;   // 角标图以嵌入资源形式随程序集发布，资源名前缀不定，只能按后缀找
                foreach (string resourceName in assembly.GetManifestResourceNames())
                {
                    // 后缀比对忽略大小写：嵌入资源名受项目文件与文件系统影响
                    if (!resourceName.EndsWith(".c.png", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // using 包住流，读完立刻释放
                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)   // 资源名存在但取不到流：换下一个候选
                            continue;
                        using (var image = Image.FromStream(stream))   // 复制成独立 Bitmap：流释放后原 Image 会失效
                            return new Bitmap(image);
                    }
                }
            }
            // 角标只是装饰：缺图或解码失败都静默降级，不让整张卡片跟着失败
            catch
            {
                // 吞掉异常：没有角标时按钮依然可用
            }
            return null;   // 没找到可用资源：调用方按“无角标”处理
        }

        // 绘制通信设备角标：位置贴设备块右缘，可带 16px 滑入与缩放的出现动画
        // 角标  参数：g 绘制图面  blockRect 所在设备块矩形  nameRect 名字行矩形  index 行下标  返回：无
        private void DrawCommBadge(Graphics g, Rectangle blockRect, RectangleF nameRect, int index)
        {
            if (_commBadge == null)   // 图没加载到就什么都不画（布局仍按有角标处理，名字早已让位）
                return;

            int badgeX = blockRect.Right - CommBadgeRightPad - CommBadgeSize;   // 角标右缘与块右缘之间留 CommBadgeRightPad
            // 垂直对齐名字行中线，再上移 1 像素修正视觉重心
            int badgeY = (int)Math.Round(nameRect.Y + (nameRect.Height - CommBadgeSize) / 2f) - 1;

            // reveal：只有“本次动画的目标行”才播滑入，其余行直接按完成态画
            double reveal = 1.0;
            if (_switchStart != 0 && _badgeTargetIndex >= 0 && index == _badgeTargetIndex)
                reveal = BadgeProgress();

            int slide = (int)Math.Round(16 * (1.0 - reveal));   // 从右侧 16 像素处滑入，reveal=0 时偏移最大
            int size = (int)Math.Round(CommBadgeSize * (0.45 + 0.55 * reveal));   // 尺寸从 45% 长到 100%，与滑入同时进行，形成“窜出来”的手感
            if (size < 1) size = 1;   // 至少 1 像素，DrawImage 不接受 0 尺寸

            // 以角标原始区域中心为锚点缩放，避免右上角漂移
            var dest = new Rectangle(
                badgeX + slide + (CommBadgeSize - size) / 2,
                badgeY + (CommBadgeSize - size) / 2,
                size, size);

            int alpha = (int)Math.Round(255 * Math.Min(1.0, reveal * 1.4));   // 透明度比尺寸先到位（×1.4），落地时已是全不透明
            if (alpha > 255) alpha = 255;   // 溢出钳位

            var oldMode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;   // 缩小绘制时用双三次插值，避免角标边缘出锯齿
            try
            {
                if (alpha >= 250)   // 接近不透明：走快速路径，省掉一次 ColorMatrix 变换
                {
                    g.DrawImage(_commBadge, dest);
                }
                else
                {
                    using (var attrs = new ImageAttributes())
                    {
                        // 半透明阶段用 ColorMatrix 的 Matrix33 通道整体缩放 alpha，比逐像素处理快得多
                        var matrix = new ColorMatrix { Matrix33 = alpha / 255f };   // Matrix33 就是 alpha 通道的缩放系数
                        attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        g.DrawImage(_commBadge, dest, 0, 0, _commBadge.Width, _commBadge.Height,
                            GraphicsUnit.Pixel, attrs);
                    }
                }
            }
            finally
            {
                g.InterpolationMode = oldMode;   // 无论如何都还原插值模式，别影响后面的文字与矢量绘制
            }
        }

        // ===================== 收藏星 =====================
        // 绘制收藏星块：暖白悬停底 + 可缩放/弹跳的星形，金色填充表示已收藏
        // 收藏星  参数：g 绘制图面  rect 星块矩形  filled 是否已收藏  deviceId 设备 Id（用于查动画状态）  返回：无
        private void DrawStarBlock(Graphics g, Rectangle rect, bool filled, Guid deviceId)
        {
            // 三个动画量都按设备 Id 查：滚动后行下标会变，用下标会让动画串到别的行
            double hover = StarHoverOf(deviceId);
            double bounce = StarBounceOf(deviceId);
            double fill = StarFillOf(deviceId, filled);

            // 右圆角块：与左侧设备块相反，只圆右边两角
            using (var path = CreateSideRoundPath(rect, BlockRadius, false))
            {
                // 底色从卡片白插值到暖白，悬停时整块“亮起来”
                using (var back = new SolidBrush(LerpColor(CardWhite, StarHoverBack, hover)))
                    g.FillPath(back, path);
                using (var pen = new Pen(BlockBorder, 1f))
                    g.DrawPath(pen, path);
            }

            double scale = 1.0 + StarHoverGrow * hover + bounce;   // 悬停放大与点击回弹都叠加到缩放系数，两者都按设备 Id 独立计时
            if (scale < 1.0)
                scale = 1.0;   // 钳住下限，保证星形不会被缩到基准尺寸以下

            var baseRect = StarRectInBlock(rect);   // 星形在块内居中的基准矩形
            var saved = g.Save();                   // 保存变换矩阵，退出时恢复
            try
            {
                if (scale > 1.0001)   // 有放大/弹跳才动矩阵，省掉无意义的浮点误差
                {
                    // 以星形中心为锚点：先平移到中心，缩放，再平移回去
                    float cx = baseRect.X + baseRect.Width / 2f;
                    float cy = baseRect.Y + baseRect.Height / 2f;
                    g.TranslateTransform(cx, cy);
                    g.ScaleTransform((float)scale, (float)scale);
                    g.TranslateTransform(-cx, -cy);
                }

                using (var path = CreateStarPath(baseRect))   // 10 个顶点连成的闭合五角星
                {
                    double gold = StarPreviewAlpha * hover * (1.0 - fill) + fill;   // 金色 = 悬停预览与收藏填充按权重互补相加，最后再钳到 1
                    if (gold > 0.001)   // 强度太小就不画，省一次 FillPath
                    {
                        int alpha = (int)Math.Round(255 * Math.Min(1.0, gold));
                        using (var b = new SolidBrush(Color.FromArgb(alpha, StarGold)))
                            g.FillPath(b, path);
                    }

                    // 收藏动画最后 10% 里描边让位给金色填充，避免深边把星“框死”
                    double outline = fill >= 0.9 ? Math.Max(0.0, 1.0 - (fill - 0.9) / 0.1) : 1.0;
                    if (outline > 0.001)
                    {
                        int alpha = (int)Math.Round(255 * outline);
                        var stroke = LerpColor(StarOutline, StarGold, hover);
                        using (var p = new Pen(Color.FromArgb(alpha, stroke), 1.6f))   // 描边 1.6px，比块边框略粗，让星形在小尺寸下也立得住
                            g.DrawPath(p, path);
                    }
                }
            }
            finally
            {
                // 恢复矩阵：后面的滚动条等绘制不受星形缩放影响
                g.Restore(saved);
            }
        }

        // 构造正五角星路径：外顶点与内顶点交替，共 10 个点
        // 星形路径  参数：r 星形外接矩形  返回：闭合的五角星路径
        private static GraphicsPath CreateStarPath(Rectangle r)
        {
            var path = new GraphicsPath();
            var cx = r.X + r.Width / 2f;
            var cy = r.Y + r.Height / 2f;
            var outer = Math.Min(r.Width, r.Height) / 2f - 1f;   // 外接圆半径取短边一半再减 1，给 1.6px 描边留余量
            var inner = outer * 0.45f;                           // 内半径取外半径的 0.45，这个比例决定星角的胖瘦
            // 偶数下标是星尖、奇数下标是星谷，所以数组长度是 10
            var pts = new PointF[10];
            // 从正上方（-90°）起步、每步 36°（π/5），绕满一圈正好 10 个点
            for (var i = 0; i < 10; i++)
            {
                var radius = i % 2 == 0 ? outer : inner;   // 偶数位用外半径（星尖），奇数位用内半径（星谷）
                var angle = -Math.PI / 2 + i * Math.PI / 5;   // 角度：-90° 起，每点 36°
                pts[i] = new PointF(cx + (float)(radius * Math.Cos(angle)), cy + (float)(radius * Math.Sin(angle)));
            }
            path.AddPolygon(pts);   // AddPolygon 自动把首尾点连起来闭合
            return path;
        }

        // ===================== 滚动条与圆角路径 =====================
        // 绘制滚动条：轨道与滑块都按条宽做完全圆角
        // 滚动条  参数：g 绘制图面  返回：无（坐标为列表区右侧、客户区坐标）
        private void DrawScrollbar(Graphics g)
        {
            if (MaxOffset <= 0)   // 内容装得下就不显示滚动条，免得出现一条无意义的灰条
                return;

            var track = ScrollTrackRect;   // 轨道矩形
            using (var b = new SolidBrush(ScrollTrack))
            using (var path = RoundedRect(track.X, track.Y, track.Width, track.Height, ScrollbarWidth))   // 半径取条宽，两端为半圆
                g.FillPath(b, path);

            var thumb = ScrollThumbRect;
            if (!thumb.IsEmpty)   // 滑块矩形为空：当前不可滚动，跳过
            {
                using (var b = new SolidBrush(ScrollThumb))
                using (var path = RoundedRect(thumb.X, thumb.Y, thumb.Width, thumb.Height, ScrollbarWidth))   // 滑块同样圆角
                    g.FillPath(b, path);
            }
        }

        // 构造“只有一侧带圆角”的矩形路径：设备块与收藏星块各取一半，拼成一行
        // 单侧圆角路径  参数：rect 目标矩形  radius 圆角半径  roundLeft true 圆左、false 圆右  返回：闭合路径
        private static GraphicsPath CreateSideRoundPath(Rectangle rect, int radius, bool roundLeft)
        {
            var path = new GraphicsPath();
            int d = radius * 2;   // d 是圆角直径，AddArc 的宽高都取 d
            // 直径超过宽或高时钳住，圆弧不会超出矩形
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            int x = rect.X;
            int y = rect.Y;
            // 用 w-1 / h-1 作内边界：1px 描边正好落在矩形内侧，不会被裁掉
            int w = rect.Width - 1;
            int h = rect.Height - 1;

            // 左圆：左上角起笔，右边两点直接连线
            if (roundLeft)
            {
                path.AddArc(x, y, d, d, 180, 90);                      // 左上角圆弧：180°（左中点）扫 90° 到 270°（上中点）
                path.AddLine(x + d / 2f, y, x + w, y);                 // 顶边：从圆角结束点走到右上直角
                path.AddLine(x + w, y, x + w, y + h);                  // 右边：向下走到右下直角
                path.AddLine(x + w, y + h, x + d / 2f, y + h);         // 底边：从右下直角回到左下圆角起点
                path.AddArc(x, y + h - d, d, d, 90, 90);               // 左下角圆弧：90°（下中点）扫 90° 到 180°（左中点）
            }
            // 右圆：路径顺序反过来，先直后圆
            else
            {
                path.AddLine(x, y, x + w - d / 2f, y);                 // 顶边：从左上直角走到右上圆角起点
                path.AddArc(x + w - d, y, d, d, 270, 90);              // 右上角圆弧：270°（上中点）扫 90° 到 360°（右中点）
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);        // 右下角圆弧：0°（右中点）扫 90° 到 90°（下中点）
                path.AddLine(x + w - d / 2f, y + h, x, y + h);         // 底边：从右下圆角结束点回到左下直角
                path.AddLine(x, y + h, x, y);                          // 左边：闭合回起点
            }

            path.CloseFigure();   // 闭合：FillPath 才不会有缺口漏色
            return path;
        }

        // 构造四角同半径的圆角矩形路径，滚动条的轨道与滑块都用它
        // 圆角矩形  参数：x/y/w/h 矩形  radius 圆角半径  返回：闭合路径
        private static GraphicsPath RoundedRect(int x, int y, int w, int h, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;   // d 为圆角直径
            // 直径不超过宽或高，否则圆弧会反向
            if (d > w) d = w;
            if (d > h) d = h;
            // 依次左上 → 右上 → 右下 → 左下，四个 90° 圆弧相接
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();   // 闭合路径
            return path;
        }

        #endregion
        #region 齿轮按钮

        // 绘制右上角的齿轮（设置）按钮
        // 齿轮按钮  参数：g 绘制图面  返回：无（坐标为窗口客户区坐标）
        private void DrawGearButton(Graphics g)
        {
            bool active = _spreadExpanded;   // 展开状态下齿轮保持“激活”外观，鼠标移开也不熄灭

            // 直径优先级：按下 > 悬停/展开 > 静置
            float diameter = _gearHover ? GearHoverDiameter : GearIdleDiameter;
            if (_gearPressed)
                diameter = GearPressDiameter;        // 按下时直接取按压直径，不再叠加放大
            else if (active)
                diameter = GearHoverDiameter;        // 展开时视同悬停

            float cx = GearCenter.X;
            float cy = GearCenter.Y;
            float radius = diameter / 2f;            // 圆心与半径来自布局分块，绘制只负责取用

            int alpha = (_gearHover || active) ? 204 : 128;   // 悬停或展开时底色更实（204），静置时半透（128），弱化存在感
            using (var brush = new SolidBrush(Color.FromArgb(alpha, AccentBlue)))
            {
                g.FillEllipse(brush, cx - radius, cy - radius, diameter, diameter);
            }

            // 只有悬停或展开时才画齿轮图标，静置时只是一个纯蓝圆点
            if ((_gearHover || active) && !_gearPressed)
            {
                float gearR = diameter * 0.3f;   // 图标半径取直径的 30%，齿数 8
                using (var path = CreateGearPath(cx, cy, gearR, 8))
                using (var white = new SolidBrush(OnAccentWhite))
                {
                    g.FillPath(white, path);
                }
            }
        }

        // 构造齿轮路径：方形齿 + 中心圆孔（靠 Alternate 填充模式挖出来）
        // 齿轮路径  参数：cx/cy 圆心  outerRadius 齿顶半径  teeth 齿数  返回：带中心孔的齿轮路径
        private static GraphicsPath CreateGearPath(float cx, float cy, float outerRadius, int teeth)
        {
            int points = teeth * 4;   // 每个齿占 4 个顶点：2 个齿顶 + 2 个齿根
            var pts = new PointF[points];
            float innerRadius = outerRadius * 0.74f;   // 齿根半径取齿顶的 0.74，太小齿会变尖，太大齿会变平
            // 从正上方起步、每点转 2π/points，绕满一圈
            for (int i = 0; i < points; i++)
            {
                float r = (i % 4 < 2) ? outerRadius : innerRadius;   // 下标 %4 < 2 的两点抬到齿顶，其余两点落回齿根，形成方齿
                double angle = -Math.PI / 2 + i * (Math.PI * 2 / points);
                pts[i] = new PointF(cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
            }

            // Alternate 填充模式：中心圆与齿轮多边形重叠的区域被翻转成“外部”，看起来就是一个孔
            var path = new GraphicsPath { FillMode = FillMode.Alternate };
            path.AddPolygon(pts);
            path.AddEllipse(cx - outerRadius * 0.3f, cy - outerRadius * 0.3f,   // 中心孔半径取齿顶的 30%
                outerRadius * 0.6f, outerRadius * 0.6f);
            return path;
        }

        #endregion
    }
}