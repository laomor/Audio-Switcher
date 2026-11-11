// ============================================================
// SettingsCardForm.Settings.cs - 齿轮扩散动画、三行设置与热键输入框
// 功能：点击齿轮后让蓝色圆从点击点铺满卡片，再显示三行设置、复选框与热键框
// 说明：本文件是 SettingsCardForm 的分块（partial），与 Draw/Input/Star 等分块配合
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 齿轮扩散效果

        // ===================== 扩散状态与缓动 =====================

        private const int SpreadGrowDuration = 240;   // 扩散主时长（毫秒）：圆从一点长到铺满，再快就看不清生长
        private const int SpreadBounceDuration = 60;  // 回弹修正时长（毫秒）：超铺后收回只用 60ms，越短越像「弹」
        private const double SpreadOvershoot = 1.12;  // 回弹过冲：多铺 12% 再收回，读到一次轻微回弹而不是卡顿
        private const int SpreadPhaseNone = 0;        // 相位 0：无动画
        private const int SpreadPhaseGrow = 1;        // 相位 1：从点击点向外铺开
        private const int SpreadPhaseBounce = 2;      // 相位 2：超铺后收回到正好铺满
        private const int SpreadPhaseShrink = 3;      // 相位 3：从当前半径收回到 0

        // 扩散动画是否还在进行  参数：无  返回：true 表示定时器仍在跑
        // 为什么不直接看 _spreadPhase：停表与相位清零在同一函数内完成，用定时器的
        // Enabled 判断更直接，所有调用点看到的忙碌状态才一致；相位只用来区分展开、
        // 回弹、收起三种绘制分支。
        private bool IsSpreadBusy
        {
            get { return _spreadTimer != null && _spreadTimer.Enabled; }
        }

        // 切换扩散态：点齿轮开面板或收面板  参数：clickPoint 齿轮上的点击点（像素）  返回：无
        // 圆心取点击点：圆从手指按下的地方长出来，视觉上才像「这一下把卡片点开了」。
        private void ToggleSpread(PointF clickPoint)
        {
            // 动画期间不接受新的切换：中途换相位会让圆的边缘停在半路
            if (IsSpreadBusy)
                return;

            // === 展开 ===
            if (!_spreadExpanded)
            {
                _spreadExpanded = true;
                _spreadFilled = false;
                // 先清掉热键框的编辑态，免得面板刚展开还停在捕获中
                ClearHotKeyEditing();
                _spreadCenter = clickPoint;
                // 半径取到最远角：保证圆一定盖得住整张卡片，不会留角
                _spreadMaxRadius = ComputeSpreadMaxRadius(_spreadCenter);
                StartSpreadPhase(0, _spreadMaxRadius, SpreadGrowDuration, SpreadPhaseGrow);
            }
            // === 收起 ===
            else
            {
                // 收起前取消捕获并复位悬停：此时键盘与鼠标都不该继续作用于热键框
                CancelHotKeyCapture();
                ResetHotKeyHover();
                _spreadExpanded = false;
                _spreadFilled = false;
                StartSpreadPhase(_spreadRadius, 0, SpreadGrowDuration, SpreadPhaseShrink);
            }

            Invalidate();
        }

        // 计算能盖住整张卡片的圆半径  参数：center 圆心（像素）  返回：半径（像素）
        // dx/dy 各取到较远那一侧的距离，勾股即「圆心到最远角」的距离。
        private double ComputeSpreadMaxRadius(PointF center)
        {
            // 左右、上下各取更远的一边，四个角里最远的那个才会被覆盖
            double dx = Math.Max(center.X, Width - center.X);
            double dy = Math.Max(center.Y, Height - center.Y);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // 启动扩散动画的某一相位  参数：from 起始半径，to 目标半径，duration 时长（毫秒），phase 相位号  返回：无
        // 所有相位共用同一个定时器和同一组 from/to 字段，换相位只是改写这几个值再重新计时。
        private void StartSpreadPhase(double from, double to, int duration, int phase)
        {
            _spreadFrom = from;
            _spreadTo = to;
            _spreadPhaseDuration = duration;
            _spreadPhase = phase;
            _spreadPhaseStart = Environment.TickCount;

            // 懒创建定时器：只有真正需要扩散时才分配
            if (_spreadTimer == null)
            {
                _spreadTimer = new Timer { Interval = 16 };
                _spreadTimer.Tick += SpreadTimer_Tick;
            }
            // 已在跑就复用，不重新 Start：Start 会重置相位，等于让动画从头再来
            if (!_spreadTimer.Enabled)
                _spreadTimer.Start();
        }

        // 停止扩散定时器  参数：无  返回：无
        // 相位一并归零：供绘制分支复位，不参与忙碌判断。
        private void StopSpreadAnimation()
        {
            if (_spreadTimer != null && _spreadTimer.Enabled)
                _spreadTimer.Stop();
            _spreadPhase = SpreadPhaseNone;
        }

        // 复位到未展开的初始状态  参数：无  返回：无
        // 隐藏卡片与每次显示前用：连热键框的编辑态一起清，避免残留的光晕。
        private void ResetSpread()
        {
            StopSpreadAnimation();
            _spreadRadius = 0;
            _spreadExpanded = false;
            _spreadFilled = false;
            ClearHotKeyEditing();
        }

        // 扩散补间的每帧回调  参数：sender 触发事件的定时器  返回：无
        // 1. 按已过时间算线性进度并夹到 0~1
        // 2. 按相位挑缓动：铺开用 EaseOutQuad，收起用 EaseInOutQuad
        // 3. 插值出当前半径
        // 4. 主相位结束时切到回弹相位，其余相位结束就吸附并停表
        private void SpreadTimer_Tick(object sender, EventArgs e)
        {
            int elapsed = Environment.TickCount - _spreadPhaseStart;
            // 时长为 0 时直接当作已完成，避免除零
            double progress = _spreadPhaseDuration <= 0 ? 1.0 : elapsed / (double)_spreadPhaseDuration;
            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;

            // 铺开要「快起慢收」像水漫开，收起要「两头慢中间快」才不显得生硬
            double eased = _spreadPhase == SpreadPhaseShrink ? EaseInOutQuad(progress) : EaseOutQuad(progress);
            _spreadRadius = _spreadFrom + (_spreadTo - _spreadFrom) * eased;

            // 先按当前相位插值完，再判断是否该跨相位
            if (progress >= 1.0)
            {
                // 铺满的瞬间才允许显示三行设置：早一帧都会露出圆外的直角
                if (_spreadPhase == SpreadPhaseGrow)
                {
                    _spreadFilled = true;
                    // 回弹：从超铺半径退回正好铺满，只跑 60ms，读到的是「弹了一下」
                    StartSpreadPhase(_spreadMaxRadius * SpreadOvershoot, _spreadMaxRadius,
                        SpreadBounceDuration, SpreadPhaseBounce);
                    Invalidate();
                    return;
                }

                // 其它相位（回弹/收起）结束：吸附到目标值，避免浮点残差
                _spreadRadius = _spreadTo;
                StopSpreadAnimation();
            }

            Invalidate();
        }

        // 缓出二次曲线：先快后慢  参数：t 线性进度（0~1）  返回：缓动后的进度（0~1）
        private static double EaseOutQuad(double t)
        {
            // t*(2-t) 在 t=1 处斜率为 0，所以收尾很轻
            return t * (2 - t);
        }

        // 缓入缓出二次曲线：两头慢、中间快  参数：t 线性进度（0~1）  返回：缓动后的进度（0~1）
        private static double EaseInOutQuad(double t)
        {
            // 以 0.5 为界镜像拼接两段抛物线，中点的速度最大
            return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
        }

        // 绘制扩散中的蓝色圆  参数：g 卡片绘图表面  返回：无
        // 必须裁到圆角卡片路径里：否则圆会溢到窗口的透明四角上，破坏无边框观感。
        private void DrawSpread(Graphics g)
        {
            // 半径不到半像素时没有可见面积，跳过能省一次裁剪与填充
            if (_spreadRadius < 0.5)
                return;

            float r = (float)_spreadRadius;
            var cardRect = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (var clipPath = CreateRoundPath(cardRect, CornerRadius))
            {
                // 裁剪只在本次绘制生效，Save/Restore 成对出现，防止污染后续绘制
                var saved = g.Save();
                g.SetClip(clipPath, CombineMode.Intersect);
                // 纯色实心圆：扩散本身已是视觉重点，再加渐变只会变脏
                using (var brush = new SolidBrush(AccentBlue))
                {
                    g.FillEllipse(brush, _spreadCenter.X - r, _spreadCenter.Y - r, r * 2, r * 2);
                }
                g.Restore(saved);
            }
        }

        #endregion
        #region 设置模块（扩散铺满后出现的三行）

        // ===================== 三行设置 =====================

        // 三行设置的标题文案，下标与 SettingRowRect/SettingRowIndexAt 的行号一一对应。
        // 用静态只读数组而不是每次绘制拼字符串，三行的顺序只在这里定义一次。
        private static readonly string[] SettingLabels =
        {
            "开机自启动", "启用双切换模式", "设置切换热键"
        };

        // 设置区的点击分派  参数：p 点击坐标（像素）  返回：无
        // 先取消热键捕获：点空白或点另一行，都应当退出上一轮的捕获态。
        private void HandleSettingClick(Point p)
        {
            // 无条件取消：留着捕获会让键盘一直被接管，哪怕这次点的是空白
            CancelHotKeyCapture();

            // 铺满之前行矩形还没意义，这里会直接拿到 -1
            int row = SettingRowIndexAt(p);
            if (row < 0)
                return;

            // 第 3 行是热键框：命中略微外扩的矩形才进入捕获，与视觉上的外发光对齐
            if (row == 2)
            {
                if (Rectangle.Inflate(HotKeyBoxRect, 5, 3).Contains(p))
                    BeginHotKeyCapture();
                return;
            }

            // 前两行是复选框：必须点在复选框自身范围内才算切换
            if (!OptionHitRect(row).Contains(p))
                return;

            SettingsCardOption option;
            bool value;
            // === 按行号决定切哪个开关 ===
            if (row == 0)
            {
                _optAutoStart = !_optAutoStart;
                option = SettingsCardOption.AutoStartWithWindows;
                value = _optAutoStart;
            }
            else
            {
                _optDualSwitch = !_optDualSwitch;
                option = SettingsCardOption.DualSwitchMode;
                value = _optDualSwitch;
            }

            // 动过设置就清掉热键错误态：暖黄文案不该残留在已经改过的框上
            _hotKeyError = false;
            // 先起动画再抛事件：回调里做 IO 时，动画已经在跑了
            StartCheckAnimation(row, value);
            Invalidate();

            // 事件用局部变量接一下再判空：判空与调用之间订阅者可能被换掉
            var handler = OptionToggled;
            if (handler != null)
                handler(this, new SettingsCardToggleEventArgs(option, value));
        }

        // 坐标落在第几行设置上  参数：p 坐标（像素）  返回：行号 0~2；未铺满或未命中返回 -1
        private int SettingRowIndexAt(Point p)
        {
            // 没铺满时三行根本不可见，谈不上命中
            if (!_spreadFilled)
                return -1;

            // 行矩形互不重叠，命中即返回，遍历顺序无所谓
            for (int i = 0; i < 3; i++)
            {
                if (SettingRowRect(i).Contains(p))
                    return i;
            }
            return -1;
        }

        // 进入热键捕获态  参数：无  返回：无
        // 进入前把「当前文本 + 错误态」存档，Esc 取消时能原样恢复。
        private void BeginHotKeyCapture()
        {
            // 已在捕获中就不重复进入，否则存档会被同样的值覆盖，恢复也就失去意义
            if (_hotKeyCapturing)
                return;

            // 存档：取消捕获时按这份快照还原，而不是清空
            _hotKeyTextBeforeCapture = _hotKeyText;
            _hotKeyErrorBeforeCapture = _hotKeyError;
            _hotKeyCapturing = true;
            // 捕获期间不显示按键结果，先清掉上一次的错误标记
            _hotKeyError = false;
            StartHotKeyFocus(true);
            Invalidate();

            // 广播捕获开始：主窗体据此接管键盘
            var handler = HotKeyCaptureStarted;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        // 取消热键捕获并恢复进入前的值  参数：无  返回：无
        // 恢复而不是留空：按 Esc 的本意是「这次别改」，不是「把热键清掉」。
        private void CancelHotKeyCapture()
        {
            // 没在捕获就什么都不做，避免重复抛 HotKeyCaptureEnded
            if (!_hotKeyCapturing)
                return;

            _hotKeyCapturing = false;
            // 存档可能为 null（首次捕获前），判空后才恢复
            if (_hotKeyTextBeforeCapture != null)
            {
                _hotKeyText = _hotKeyTextBeforeCapture;
                _hotKeyError = _hotKeyErrorBeforeCapture;
            }
            // 关掉聚焦光晕
            StartHotKeyFocus(false);
            Invalidate();

            // 广播捕获结束：主窗体交还键盘
            var handler = HotKeyCaptureEnded;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        // 请求清除热键（由 Delete/Backspace 触发）  参数：无  返回：无
        // 先退出捕获再抛事件：清空是「确定要改」，与 Esc 的「放弃修改」是两条路。
        private void RequestClearHotKey()
        {
            CancelHotKeyCapture();

            var handler = HotKeyCleared;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        // 判断按键能否单独当热键  参数：key 按键枚举  返回：F1~F24 为 true
        // 单个字母或数字不做单键热键：那会吃掉正常输入，只有 F 系列是安全的单键。
        private static bool IsSafeFunctionKey(Keys key)
        {
            return key >= Keys.F1 && key <= Keys.F24;
        }

        // 绘制三行设置（仅在扩散铺满后可见）  参数：g 绘图表面  返回：无
        // 第 1/2 行是「标题 + 复选框」，第 3 行是「标题 + 热键框」，顺序与 SettingLabels 一致。
        private void DrawSettingRows(Graphics g)
        {
            // 圆还没铺满时三行会被压在蓝色圆外，不画
            if (!_spreadFilled)
                return;

            // === 第 1 行：开机自启动 ===
            DrawSettingLabel(g, SettingLabels[0], SettingRowRect(0));
            DrawOptionCheckBox(g, OptionCheckRect(0), 0);

            // === 第 2 行：双切换模式 ===
            DrawSettingLabel(g, SettingLabels[1], SettingRowRect(1));
            DrawOptionCheckBox(g, OptionCheckRect(1), 1);

            // === 第 3 行：热键框 ===
            DrawSettingLabel(g, SettingLabels[2], SettingRowRect(2));
            DrawHotKeyBox(g);
        }

        // 绘制一行设置的标题  参数：g 绘图表面，text 标题文本，row 该行矩形  返回：无
        // 起点右移 2 像素、可用宽度收窄 6 像素：左侧留内边距，右侧不顶到复选框。
        private void DrawSettingLabel(Graphics g, string text, Rectangle row)
        {
            // 单行 + 省略号：行高固定，换行会顶到下一行
            using (var brush = new SolidBrush(OnAccentWhite))
            using (var format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Near
            })
            {
                g.DrawString(text, _nameFont, brush,
                    new RectangleF(row.X + 2, row.Y, row.Width - 6, row.Height), format);
            }
        }

        // ===================== 复选框绘制 =====================

        // 绘制一个复选框：底框 + 填充 + 悬停圆点 + 逐段画出的对勾  参数：g 绘图表面，box 复选框矩形，row 行号  返回：无
        // 对勾是一条 a→b→c 的两段折线，按「已画长度 / 总长」推进：
        // a 在框内 26%/53% 处起笔，b 是 45%/73% 处的折点，c 在 77%/29% 处收笔；
        // 用相对比例而不是像素，复选框缩放或改尺寸时勾形会自动跟着走。
        private void DrawOptionCheckBox(Graphics g, Rectangle box, int row)
        {
            double value = _checkValue[row];
            // 缩放系数由悬停、按下、回弹三者叠加得出，见 CheckScaleFor
            double scale = CheckScaleFor(row);

            var state = g.Save();
            // 无缩放时跳过变换，省一次矩阵运算（每帧两行各画一次，能省则省）
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                // 绕中心缩放：先平移到中心，缩放，再平移回去
                float cx = box.X + box.Width / 2f;
                float cy = box.Y + box.Height / 2f;
                g.TranslateTransform(cx, cy);
                g.ScaleTransform((float)scale, (float)scale);
                g.TranslateTransform(-cx, -cy);
            }

            using (var path = RoundedRect(box.X, box.Y, box.Width, box.Height, OptionCheckRadius))
            {
                // 底框：极淡的白，让空复选框在蓝底上仍有轮廓
                using (var baseFill = new SolidBrush(Color.FromArgb(28, OnAccentWhite)))
                    g.FillPath(baseFill, path);

                // 值几乎为 0 时不画填充，避免半透明残渣
                if (value > 0.002)
                {
                    // === 填充：点亮用中心扩散，熄灭用透明度淡出 ===
                    if (_checkFillRise[row])
                    {
                        // 圆半径取到对角线的一半：进度为 1 时刚好盖满整个框
                        double radius = Math.Sqrt((double)box.Width * box.Width + (double)box.Height * box.Height) / 2.0 * value;
                        float ccx = box.X + box.Width / 2f;
                        float ccy = box.Y + box.Height / 2f;

                        // 裁到框内：圆会超出圆角矩形，不裁就把四个角涂成直角
                        var clipState = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        using (var inkFill = new SolidBrush(OnAccentWhite))
                        {
                            g.FillEllipse(inkFill, (float)(ccx - radius), (float)(ccy - radius),
                                (float)(radius * 2), (float)(radius * 2));
                        }
                        g.Restore(clipState);
                    }
                    else
                    {
                        // 熄灭直接按透明度淡出：收回不该有方向性
                        int fadeAlpha = (int)Math.Round(255 * value);
                        using (var fadeFill = new SolidBrush(Color.FromArgb(fadeAlpha, OnAccentWhite)))
                            g.FillPath(fadeFill, path);
                    }
                }

                // 描边随勾选进度提亮加粗，强化「已选」的重量感
                int strokeAlpha = (int)Math.Round(172 + 83 * value);
                float strokeWidth = (float)(1.3 + 0.7 * value);
                using (var pen = new Pen(Color.FromArgb(strokeAlpha, OnAccentWhite), strokeWidth))
                    g.DrawPath(pen, path);
            }

            // === 悬停圆点：勾选值过半前逐渐隐去，避免和实心填充打架 ===
            double hoverT = _checkHoverValue[row];
            if (hoverT > 0.002)
            {
                // dotFade 随 value 线性消失，勾选到一半就完全隐去
                double dotFade = 1.0 - Math.Min(1.0, value * 2.0);
                if (dotFade > 0.002)
                {
                    // 圆点半径随悬停进度长大
                    double dotR = 1.55 * hoverT;
                    float dcx = box.X + box.Width / 2f;
                    float dcy = box.Y + box.Height / 2f;
                    // 三个因子相乘：悬停强度 × 隐去比例 × 基准 Alpha
                    int dotAlpha = (int)Math.Round(210 * hoverT * dotFade);
                    using (var dot = new SolidBrush(Color.FromArgb(dotAlpha, OnAccentWhite)))
                    {
                        g.FillEllipse(dot, (float)(dcx - dotR), (float)(dcy - dotR),
                            (float)(dotR * 2), (float)(dotR * 2));
                    }
                }
            }

            // 勾在填充过半之后才开始落笔：0.5~1 线性映射成 0~1 的绘制进度
            double drawProgress = (value - 0.5) / 0.5;
            if (drawProgress > 0)
            {
                if (drawProgress > 1)
                    drawProgress = 1;

                // 三段关键点：起笔（左下）、折点（谷底）、收笔（右上）
                var a = new PointF(box.X + box.Width * 0.26f, box.Y + box.Height * 0.53f);
                var b = new PointF(box.X + box.Width * 0.45f, box.Y + box.Height * 0.73f);
                var c = new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.29f);
                // 先算出两段长度，才谈得上「按长度比例」逐段推进
                double len1 = Distance(a, b);
                double len2 = Distance(b, c);
                double drawn = drawProgress * (len1 + len2);

                // 圆头笔：笔画末端与折点都是圆的，短勾也不显得秃
                using (var pen = new Pen(AccentBlue, 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    // 已画长度还在第一段内：按比例截取 a→b
                    if (drawn <= len1)
                    {
                        float k = (float)(drawn / len1);
                        g.DrawLine(pen, a, new PointF(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k));
                    }
                    else
                    {
                        // 第二段：先补满 a→b，再按剩余长度截取 b→c
                        g.DrawLine(pen, a, b);
                        float k = (float)((drawn - len1) / len2);
                        g.DrawLine(pen, b, new PointF(b.X + (c.X - b.X) * k, b.Y + (c.Y - b.Y) * k));
                    }
                }
            }

            // 恢复缩放变换：两行复选框共用同一份 Graphics
            g.Restore(state);
        }

        // 计算复选框当前的缩放系数  参数：row 行号  返回：缩放倍数（基准 1.0）
        // 三个来源直接相加：悬停 +16%、按下 -5%、切换瞬间的正弦回弹最多 +10%；
        // 用加法而不是乘法，是为了让「按下」的缩小能抵掉一部分「悬停」的放大，
        // 指针停在复选框上按下时才会真的往里缩，手感上「按得下去」。
        private double CheckScaleFor(int row)
        {
            // 基准 1.0：不缩放时与像素对齐，最清晰
            double scale = 1.0;
            // 悬停放大：0~1 的补间值线性映射到 +16%
            scale += 0.16 * _checkHoverValue[row];
            // 按下缩小：0~1 的补间值线性映射到 -5%，与悬停叠加后通常仍净放大
            scale -= 0.05 * _checkPressValue[row];

            // 回弹只由「勾选」写入起始时间，取消时这里是 0
            if (_checkBounceStart[row] > 0)
            {
                double t = TweenSince(_checkBounceStart[row], CheckBounceDuration);
                // sin(πt) 在 0~1 之间走一个来回，中段最鼓
                scale += 0.10 * Math.Sin(Math.PI * t);
            }

            return scale;
        }

        // 启动一次勾选/取消的补间  参数：row 行号，value 勾选后的目标值  返回：无
        // 同时决定绘制方向（圆扩散还是淡出）与要不要回弹。
        private void StartCheckAnimation(int row, bool value)
        {
            // 起点取当前值：动画被中途打断时从半路继续，而不是从 0 重来
            _checkAnimFrom[row] = _checkValue[row];
            _checkAnimTo[row] = value ? 1.0 : 0.0;
            // 方向只影响绘制方式，与起止值分开记
            _checkFillRise[row] = value;
            // 点亮才回弹；取消时写 0，等于关掉这条补间
            _checkBounceStart[row] = value ? Environment.TickCount : 0;

            // 起止相同就不启动补间，省掉一轮空转的定时器
            if (Math.Abs(_checkAnimFrom[row] - _checkAnimTo[row]) < 0.001)
                _checkAnimStart[row] = 0;
            else
                _checkAnimStart[row] = Environment.TickCount;

            EnsureCheckTimer();
        }

        // 设置某行的悬停补间目标  参数：row 行号，on 是否悬停  返回：无
        // 已在目标值且没有进行中的补间时就返回，避免每次鼠标移动都唤醒定时器。
        private void SetCheckHover(int row, bool on)
        {
            double target = on ? 1.0 : 0.0;
            // 0.001 是浮点比较的容差，避免补间残值反复触发
            if (Math.Abs(_checkHoverValue[row] - target) < 0.001 && _checkHoverStart[row] == 0)
                return;

            // 从当前值出发：反向时能平滑接上
            _checkHoverFrom[row] = _checkHoverValue[row];
            _checkHoverTarget[row] = target;
            _checkHoverStart[row] = Environment.TickCount;
            EnsureCheckTimer();
        }

        // 设置某行的按压补间目标  参数：row 行号，on 是否按下  返回：无
        // 只有前两行有复选框，行号越界（例如热键行）一律忽略。
        private void SetCheckPress(int row, bool on)
        {
            if (row < 0 || row > 1)
                return;

            // 与悬停同理：已在目标值且无补间就不重启
            double target = on ? 1.0 : 0.0;
            if (Math.Abs(_checkPressValue[row] - target) < 0.001 && _checkPressStart[row] == 0)
                return;

            _checkPressFrom[row] = _checkPressValue[row];
            _checkPressTarget[row] = target;
            _checkPressStart[row] = Environment.TickCount;
            EnsureCheckTimer();
        }

        // 一次性清空两行复选框的悬停与按压状态  参数：无  返回：无
        // 直接写值而不走补间：指针已经离开窗口，过渡来不及被看到。
        private void ClearCheckHover()
        {
            // 两行都清：宁可多重置，也不要留下半亮的高光
            for (int row = 0; row < 2; row++)
            {
                _checkHoverValue[row] = 0.0;
                _checkHoverFrom[row] = 0.0;
                _checkHoverTarget[row] = 0.0;
                _checkHoverStart[row] = 0;
                _checkPressValue[row] = 0.0;
                _checkPressFrom[row] = 0.0;
                _checkPressTarget[row] = 0.0;
                _checkPressStart[row] = 0;
            }
        }

        // 确保复选框动画定时器已创建并在跑  参数：无  返回：无
        // 16ms 一次，与项目里其它补间同一节奏（约 60fps）。
        private void EnsureCheckTimer()
        {
            if (_checkTimer == null)
            {
                _checkTimer = new Timer { Interval = 16 };
                _checkTimer.Tick += CheckTimer_Tick;
            }

            if (!_checkTimer.Enabled)
                _checkTimer.Start();
        }

        // 复选框动画的每帧回调  参数：sender 触发事件的定时器  返回：无
        // 每行有四条独立补间：勾选值、回弹计时、悬停、按压；各自记起始 TickCount，
        // 互不干扰，只要还有一条在跑就保持定时器。
        private void CheckTimer_Tick(object sender, EventArgs e)
        {
            // 四条补间共用同一帧的时间戳，避免各自取 TickCount 造成错位
            int now = Environment.TickCount;
            bool busy = false;

            for (int row = 0; row < 2; row++)
            {
                // === 勾选值：0→1 点亮，1→0 熄灭 ===
                if (_checkAnimStart[row] != 0)
                {
                    double t = Tween01(now - _checkAnimStart[row], CheckValueDuration);

                    // 到点吸附，避免浮点残差让定时器一直跑
                    if (t >= 1.0)
                    {
                        _checkValue[row] = _checkAnimTo[row];
                        _checkAnimStart[row] = 0;
                    }
                    else
                    {
                        // 缓出三次：来势快、落点稳，符合「点一下马上有反馈」
                        double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
                        _checkValue[row] = _checkAnimFrom[row] +
                            (_checkAnimTo[row] - _checkAnimFrom[row]) * eased;
                        busy = true;
                    }
                }

                // === 回弹：只计时，形状在 CheckScaleFor 里用 sin 采样 ===
                if (_checkBounceStart[row] > 0)
                {
                    // 只负责在时长走完后清零；每帧不必写值，绘制时按时间直接算
                    if (now - _checkBounceStart[row] >= CheckBounceDuration)
                        _checkBounceStart[row] = 0;
                    else
                        busy = true;
                }

                // === 悬停：缩放用的 0~1 量 ===
                if (_checkHoverStart[row] != 0)
                {
                    double ht = Tween01(now - _checkHoverStart[row], CheckHoverDuration);

                    if (ht >= 1.0)
                    {
                        _checkHoverValue[row] = _checkHoverTarget[row];
                        _checkHoverStart[row] = 0;
                    }
                    else
                    {
                        // 与勾选值同一条 ease-out cubic，悬停放大才和勾选动画同一手感
                        double eased = 1.0 - Math.Pow(1.0 - ht, 3.0);
                        _checkHoverValue[row] = _checkHoverFrom[row] +
                            (_checkHoverTarget[row] - _checkHoverFrom[row]) * eased;
                        busy = true;
                    }
                }

                // === 按压：缩放用的 0~1 量 ===
                if (_checkPressStart[row] != 0)
                {
                    double pt = Tween01(now - _checkPressStart[row], CheckPressDuration);

                    if (pt >= 1.0)
                    {
                        _checkPressValue[row] = _checkPressTarget[row];
                        _checkPressStart[row] = 0;
                    }
                    else
                    {
                        // 按压用二次缓出，比三次更软：短促的按压不需要太强的加速度
                        double eased = 1.0 - Math.Pow(1.0 - pt, 2.0);
                        _checkPressValue[row] = _checkPressFrom[row] +
                            (_checkPressTarget[row] - _checkPressFrom[row]) * eased;
                        busy = true;
                    }
                }
            }

            // 统一重绘两行（含缩放余量）
            InvalidateSettingChecks();

            // 全部静止才停表：还有一条在跑就继续下一帧
            if (!busy && _checkTimer != null)
                _checkTimer.Stop();
        }

        // 使两行复选框区域失效重绘  参数：无  返回：无
        // 外扩 7 像素：悬停放大与回弹会画出框外，裁太紧会留下残影。
        private void InvalidateSettingChecks()
        {
            // 未铺满时三行不可见，重绘没有意义
            if (!_spreadFilled)
                return;

            for (int row = 0; row < 2; row++)
                Invalidate(Rectangle.Inflate(OptionCheckRect(row), 7, 7));
        }

        // 指针移动时更新设置区悬停  参数：p 鼠标坐标  返回：无
        // 悬停只认复选框自身的命中区，不认整行：整行变亮会让人以为点行也能切换。
        private void UpdateSettingHover(Point p)
        {
            // 热键框的悬停先更新，它和复选框互不影响
            UpdateHotKeyBoxHover(p);

            // 按住复选框后把指针拖出去：立刻撤销按压态，与「移开即取消」一致
            if (_settingPressRow >= 0 && !OptionHitRect(_settingPressRow).Contains(p))
            {
                int old = _settingPressRow;
                _settingPressRow = -1;
                SetCheckPress(old, false);
                Invalidate(Rectangle.Inflate(OptionCheckRect(old), 7, 7));
            }

            int row = -1;
            // 捕获热键期间不做行悬停：此时键盘才是主交互
            if (_spreadFilled && !_hotKeyCapturing)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (OptionHitRect(i).Contains(p))
                    {
                        row = i;
                        break;
                    }
                }
            }

            // 目标行没变就不动，避免无谓的重绘
            if (row == _settingHoverRow)
                return;

            // 旧的熄灭、新的点亮，中间不留交叉态
            int previous = _settingHoverRow;
            _settingHoverRow = row;
            if (previous >= 0)
            {
                SetCheckHover(previous, false);
                Invalidate(Rectangle.Inflate(OptionCheckRect(previous), 7, 7));
            }
            if (row >= 0)
            {
                SetCheckHover(row, true);
                Invalidate(Rectangle.Inflate(OptionCheckRect(row), 7, 7));
            }
        }

        // 两点间距离  参数：p、q 两个点  返回：欧氏距离（像素）
        private static double Distance(PointF p, PointF q)
        {
            double dx = p.X - q.X;
            double dy = p.Y - q.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ===================== 热键输入框 =====================

        // 设置热键框的悬停补间  参数：on 是否悬停  返回：无
        // 起点用自累加的 TickCount：0 被当作「无补间」的哨兵值，这里兜底成 1。
        private void SetHotKeyHover(bool on)
        {
            _hotKeyHoverOn = on;
            // 从当前值出发，中途反向也能平滑接上
            _hotKeyHoverFrom = _hotKeyHoverT;
            _hotKeyHoverTarget = on ? 1.0 : 0.0;
            _hotKeyHoverStart = Environment.TickCount;
            // TickCount 可能正好为 0（系统计时回绕），换成 1 以免被当成「没有补间」
            if (_hotKeyHoverStart == 0)
                _hotKeyHoverStart = 1;

            // 懒创建悬停定时器
            if (_hotKeyHoverTimer == null)
            {
                _hotKeyHoverTimer = new Timer { Interval = 16 };
                _hotKeyHoverTimer.Tick += HotKeyHoverTimer_Tick;
            }
            if (!_hotKeyHoverTimer.Enabled)
                _hotKeyHoverTimer.Start();

            // 立刻重绘：不等下一帧，悬停要跟手
            InvalidateHotKeyBox();
        }

        // 根据坐标更新热键框悬停  参数：p 鼠标坐标  返回：无
        // 命中区比可视框外扩 5/3 像素：贴着边框时也该有反馈。
        private void UpdateHotKeyBoxHover(Point p)
        {
            // 未铺满时热键框还不可见，不可能悬停
            bool on = _spreadFilled && Rectangle.Inflate(HotKeyBoxRect, 5, 3).Contains(p);
            if (on == _hotKeyHoverOn)
                return;

            SetHotKeyHover(on);
        }

        // 热键框悬停补间的每帧回调  参数：sender 触发事件的定时器  返回：无
        // 用缓出二次：指针移入时先快后慢，到边界不会「啪」地停住。
        private void HotKeyHoverTimer_Tick(object sender, EventArgs e)
        {
            double t = TweenSince(_hotKeyHoverStart, HotKeyHoverDuration);
            if (t >= 1.0)
            {
                _hotKeyHoverT = _hotKeyHoverTarget;
                _hotKeyHoverTimer.Stop();
            }
            else
            {
                _hotKeyHoverT = _hotKeyHoverFrom + (_hotKeyHoverTarget - _hotKeyHoverFrom) * EaseOutQuad(t);
            }

            InvalidateHotKeyBox();
        }

        // 立即复位热键框悬停（不补间）  参数：无  返回：无
        // 扩散收起时调用：面板都没了，再补间只是白白重绘。
        private void ResetHotKeyHover()
        {
            if (_hotKeyHoverTimer != null && _hotKeyHoverTimer.Enabled)
                _hotKeyHoverTimer.Stop();
            _hotKeyHoverOn = false;
            _hotKeyHoverT = 0;
            _hotKeyHoverFrom = 0;
            _hotKeyHoverTarget = 0;
        }

        // 启停热键框的聚焦光晕补间  参数：on true 进入聚焦，false 退出  返回：无
        // 聚焦是独立于悬停的一条通道：捕获中两者会同时存在，绘制时叠加而不是二选一。
        private void StartHotKeyFocus(bool on)
        {
            // 从当前值出发，退出聚焦时不会突然断掉
            _hotKeyFocusFrom = _hotKeyFocusT;
            _hotKeyFocusTarget = on ? 1.0 : 0.0;
            _hotKeyFocusStart = Environment.TickCount;
            // 同 SetHotKeyHover：0 是「无补间」的哨兵，兜底成 1
            if (_hotKeyFocusStart == 0)
                _hotKeyFocusStart = 1;

            if (_hotKeyFocusTimer == null)
            {
                _hotKeyFocusTimer = new Timer { Interval = 16 };
                _hotKeyFocusTimer.Tick += HotKeyFocusTimer_Tick;
            }
            // 已在跑就复用，避免捕获得很快时反复重启
            if (!_hotKeyFocusTimer.Enabled)
                _hotKeyFocusTimer.Start();

            InvalidateHotKeyBox();
        }

        // 立即复位聚焦光晕  参数：无  返回：无
        // 连悬停一起清：焦点没了，悬停的高亮也该跟着退。
        private void ResetHotKeyFocus()
        {
            if (_hotKeyFocusTimer != null && _hotKeyFocusTimer.Enabled)
                _hotKeyFocusTimer.Stop();
            _hotKeyFocusT = 0;
            _hotKeyFocusFrom = 0;
            _hotKeyFocusTarget = 0;
            ResetHotKeyHover();
        }

        // 清空热键框的编辑态（捕获中 + 错误态 + 光晕）  参数：无  返回：无
        // 面板收起或重置扩散时调用，保证下次展开是干净的初始态。
        private void ClearHotKeyEditing()
        {
            _hotKeyCapturing = false;
            _hotKeyError = false;
            ResetHotKeyFocus();
        }

        // 聚焦光晕补间的每帧回调  参数：sender 触发事件的定时器  返回：无
        // 用 EaseInOutQuad 而不是 EaseOutQuad：光晕是「呼吸」型变化，两头都该慢。
        private void HotKeyFocusTimer_Tick(object sender, EventArgs e)
        {
            double t = TweenSince(_hotKeyFocusStart, HotKeyFocusDuration);
            if (t >= 1.0)
            {
                _hotKeyFocusT = _hotKeyFocusTarget;
                _hotKeyFocusTimer.Stop();
            }
            else
            {
                double eased = EaseInOutQuad(t);
                _hotKeyFocusT = _hotKeyFocusFrom + (_hotKeyFocusTarget - _hotKeyFocusFrom) * eased;
            }

            InvalidateHotKeyBox();
        }

        // 使热键框区域失效重绘  参数：无  返回：无
        // 外扩 14/12 像素：光晕最多向外画到 4*1.3 像素，留足余量避免残影。
        private void InvalidateHotKeyBox()
        {
            // 未铺满时热键框不可见
            if (!_spreadFilled)
                return;

            Invalidate(Rectangle.Inflate(HotKeyBoxRect, 14, 12));
        }

        // 绘制热键输入框：光晕 + 底框描边 + 文本  参数：g 绘图表面  返回：无
        // 图层顺序即视觉权重：先光晕，再底框与描边，最后文字；捕获中不画文字。
        private void DrawHotKeyBox(Graphics g)
        {
            var baseBox = HotKeyBoxRect;

            // 聚焦度先夹到 0~1：补间误差越界会让光晕层数算错
            double focus = _hotKeyFocusT;
            if (focus < 0)
                focus = 0;
            if (focus > 1)
                focus = 1;
            float e = (float)focus;

            // 悬停度同样夹紧
            double hover = _hotKeyHoverT;
            if (hover < 0)
                hover = 0;
            if (hover > 1)
                hover = 1;
            // 悬停权重乘 (1-e)：聚焦已足够亮，悬停加成要退让，避免叠加过曝
            float hv = (float)hover * (1 - e);

            // 聚焦时整体外扩：grow 的一半向四周摊开，中心保持不动
            int growX = (int)Math.Round(HotKeyFocusGrowX * e / 2.0);
            int growY = (int)Math.Round(HotKeyFocusGrowY * e / 2.0);
            var box = new Rectangle(baseBox.X - growX, baseBox.Y - growY,
                baseBox.Width + growX * 2, baseBox.Height + growY * 2);

            // === 光晕：四层描边由内向外递减透明度 ===
            if (e > 0.002f)
            {
                // 从外往内画：先铺大圈再压小圈，内层才不会被外层盖住
                for (int i = 4; i >= 1; i--)
                {
                    // 每层比内层多伸出 1.3 像素
                    int reach = (int)Math.Round(i * 1.3);
                    // Alpha 随层号递减：内层亮、外层淡，形成柔和的辉光
                    int alpha = (int)Math.Round((14 + (5 - i) * 17) * e);
                    if (alpha <= 0)
                        continue;

                    var glow = new Rectangle(box.X - reach, box.Y - reach,
                        box.Width + reach * 2, box.Height + reach * 2);
                    using (var glowPath = RoundedRect(glow.X, glow.Y, glow.Width, glow.Height, 5 + reach))
                    using (var pen = new Pen(Color.FromArgb(alpha, HotKeyGlow), 1f))
                        g.DrawPath(pen, glowPath);
                }
            }

            // 底与描边都随「悬停 → 聚焦」连续提亮，不切换颜色，避免跳变
            int fillAlpha = (int)Math.Round(26 + 36 * hv + (88 - 26) * e);
            int penAlpha = (int)Math.Round(190 + 65 * hv + (255 - 190) * e);
            float penWidth = (float)(1.0 + 0.35 * hv + 0.8 * e);

            // 圆角 5 像素：与复选框的圆角接近，视觉上属于同一套控件
            using (var path = RoundedRect(box.X, box.Y, box.Width, box.Height, 5))
            {
                using (var fill = new SolidBrush(Color.FromArgb(fillAlpha, OnAccentWhite)))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(penAlpha, OnAccentWhite), penWidth))
                    g.DrawPath(pen, path);
            }

            // 捕获期间不画文本：空框本身就是「等待按键」的提示
            if (_hotKeyCapturing)
                return;

            // 错误态文字换成暖黄：只改文字颜色，边框保持白色
            var color = _hotKeyError ? HotKeyErrorInk : OnAccentWhite;

            // 垂直居中按字体实际行高算，不同 DPI 下都不会偏
            float lineHeight = _interfaceFont.GetHeight(g);
            float top = box.Y + (box.Height - lineHeight) / 2f;

            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Near,
                Alignment = StringAlignment.Near
            })
            {
                g.DrawString(_hotKeyText, _interfaceFont, brush,
                    new RectangleF(box.X + 6, top, box.Width - 12, lineHeight), format);
            }
        }

        #endregion
    }
}