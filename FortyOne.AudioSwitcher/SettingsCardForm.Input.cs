// ============================================================
// SettingsCardForm.Input.cs - 卡片窗体的鼠标交互与平滑滚动
// 功能：处理悬停、按下、滚轮与滚动条拖拽，让列表滚动带惯性而不是生硬跳行
// 说明：本文件是 SettingsCardForm 的分块（partial），与 Draw/Settings/Star 等分块配合
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 鼠标交互

        // ===================== 指针事件分派 =====================

        // 指针移动的总分发：按优先级判断这一帧该响应当前哪一层交互  参数：e 携带鼠标坐标  返回：无
        // 顺序即优先级，越靠前越独占：
        // 1. 退出菜单模式：只跟踪「退出」行，其余交互全部让路
        // 2. 热键冲突浮层：浮层期间吃掉移动事件，防止底下的控件仍在闪
        // 3. 拖拽滚动条：禁止星标悬停，否则滚动中星形会随行位移反复触发
        // 4. 设置行与齿轮：先更新它们的悬停，再决定是否继续做设备行命中
        // 5. 扩散态或扩散动画中：不做设备行命中测试，避免动画期间闪烁
        protected override void OnMouseMove(MouseEventArgs e)
        {
            // === 退出菜单模式：只点亮「退出」行 ===
            if (_exitMenuMode)
            {
                // 退场动画一旦开始就不再判命中：消失途中还点亮行会很怪
                if (_exitVanishStart != 0)
                {
                    base.OnMouseMove(e);
                    return;
                }
                // 只在状态真的翻转时重绘，鼠标在同一行内移动不必重画
                bool overRow = ExitRowRect.Contains(e.Location);
                if (overRow != _exitRowHover)
                {
                    _exitRowHover = overRow;
                    Invalidate();
                }
                base.OnMouseMove(e);
                return;
            }

            // === 热键冲突浮层期间不响应任何悬停 ===
            if (HotKeyWarnBlocking)
            {
                base.OnMouseMove(e);
                return;
            }

            // === 拖拽滚动条：位置由鼠标决定，星标悬停一律清掉 ===
            if (_draggingThumb)
            {
                ClearStarHover();
                DragThumbTo(e.Y);
                base.OnMouseMove(e);
                return;
            }

            // === 设置行与齿轮的悬停（与设备行互不干扰） ===
            UpdateSettingHover(e.Location);

            // 齿轮的热区比图标本身大，指针还没压到图标上就已有反馈
            bool hoveringGear = GearHotZone.Contains(e.Location);
            if (hoveringGear != _gearHover)
            {
                _gearHover = hoveringGear;
                Invalidate(GearHotZone);
            }

            // === 展开态或扩散动画中：设备行让位给设置行 ===
            if (_spreadExpanded || IsSpreadBusy)
            {
                ClearStarHover();
                if (_hoverDeviceIndex != -1)
                {
                    _hoverDeviceIndex = -1;
                    Invalidate();
                }
                base.OnMouseMove(e);
                return;
            }

            // 齿轮算作「没有悬停行」，避免齿轮与某一行同时高亮
            int index = hoveringGear ? -1 : DeviceIndexAt(e.Location);
            if (index != _hoverDeviceIndex)
            {
                _hoverDeviceIndex = index;
                Invalidate();
            }

            // 星标只认右侧区块：指针停在行中间不该点亮
            UpdateStarHover(e.Location, index);

            base.OnMouseMove(e);
        }

        // 指针离开窗口：把这一轮留下的悬停、按压状态全部复位  参数：e 事件参数（未使用）  返回：无
        // 这些状态分别驱动不同分块的绘制，漏清一个就会留下「卡住」的高亮。
        protected override void OnMouseLeave(EventArgs e)
        {
            // === 逐个复位：退出行、齿轮、设备行、星标、热键框、设置行 ===
            if (_exitMenuMode && _exitRowHover)
            {
                _exitRowHover = false;
                Invalidate();
            }
            if (_gearHover)
            {
                _gearHover = false;
                Invalidate(GearHotZone);
            }
            if (_hoverDeviceIndex != -1)
            {
                _hoverDeviceIndex = -1;
                Invalidate();
            }
            ClearStarHover();
            ResetHotKeyHover();
            // 设置行与复选框按压一起清：两者共用同一块重绘区域
            if (_settingHoverRow != -1 || _settingPressRow != -1)
            {
                _settingHoverRow = -1;
                _settingPressRow = -1;
                ClearCheckHover();
                InvalidateSettingChecks();
            }
            base.OnMouseLeave(e);
        }

        // 左键按下：按当前模式分派给设置行、齿轮、滚动条或设备行  参数：e 鼠标事件  返回：无
        // 设备行、滚动条、复选框与热键框在按下时立即生效；
        // 只有齿轮与退出行记录按下状态、留到 OnMouseUp 结算，以支持「按下后移开再抬起即取消」。
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 非左键不参与本窗体的任何交互
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }

            // === 浮层期间按下等于「我知道了」：先关提示，再吃掉这次点击 ===
            if (HotKeyWarnBlocking)
            {
                FadeOutHotKeyWarningNow();
                base.OnMouseDown(e);
                return;
            }

            // === 退出菜单模式：只结算「退出」行 ===
            if (_exitMenuMode)
            {
                if (_exitVanishStart != 0)
                {
                    base.OnMouseDown(e);
                    return;
                }
                _exitRowPressed = ExitRowRect.Contains(e.Location);
                Invalidate();
                base.OnMouseDown(e);
                return;
            }

            // === 齿轮：只记录按压，是否算点击留到抬起时判定 ===
            if (GearHotZone.Contains(e.Location))
            {
                _gearPressed = true;
                Invalidate(GearHotZone);
                base.OnMouseDown(e);
                return;
            }

            // === 展开态：命中复选框或设置行 ===
            if (_spreadExpanded || IsSpreadBusy)
            {
                // 圆还没铺满时三行不可见，不接受点击
                if (_spreadFilled)
                {
                    // 前两行是复选框：命中就记录按压并重绘（外扩 7 像素供缩放）
                    for (int i = 0; i < 2; i++)
                    {
                        if (OptionHitRect(i).Contains(e.Location))
                        {
                            _settingPressRow = i;
                            SetCheckPress(i, true);
                            Invalidate(Rectangle.Inflate(OptionCheckRect(i), 7, 7));
                            break;
                        }
                    }
                    // 第 3 行（热键框）与空白区统一交给它处理
                    HandleSettingClick(e.Location);
                }
                base.OnMouseDown(e);
                return;
            }

            // === 滚动条：按下即进入拖拽，先停掉补间 ===
            var thumb = ScrollThumbRect;
            if (!thumb.IsEmpty && thumb.Contains(e.Location))
            {
                // 停补间是必须的：拖拽改的是当前位置，补间还在跑就会把位置拽回去
                StopScrollAnimation();
                _draggingThumb = true;
                _dragStartY = e.Y;
                _dragStartOffset = _scrollOffset;
                base.OnMouseDown(e);
                return;
            }

            // === 设备行：左侧点击切换设备，右侧点击切换收藏 ===
            int index = DeviceIndexAt(e.Location);
            if (index >= 0)
            {
                if (RightBlockRect(index).Contains(e.Location))
                    ToggleFavourite(index);
                else
                    ActivateDevice(index);
            }

            base.OnMouseDown(e);
        }

        // 左键抬起：结算按下时记录的临时状态  参数：e 鼠标事件  返回：无
        // 延迟结算的控件以「按下点与抬起点落在同一热区」才算一次点击，避免误触。
        protected override void OnMouseUp(MouseEventArgs e)
        {
            // 浮层期间不结算任何东西
            if (HotKeyWarnBlocking)
            {
                base.OnMouseUp(e);
                return;
            }

            // === 退出菜单模式 ===
            if (_exitMenuMode)
            {
                // 先把按压标记取走再判断，动画期间重复进入会出问题
                bool wasPressed = _exitRowPressed;
                _exitRowPressed = false;
                Invalidate();

                if (wasPressed && ExitRowRect.Contains(e.Location))
                {
                    BeginExitVanish();
                }

                base.OnMouseUp(e);
                return;
            }

            // 拖拽一律结束：无论本次抬起落在哪里
            _draggingThumb = false;

            // === 设置行按压回弹（复选框缩回） ===
            if (_settingPressRow != -1)
            {
                int pressed = _settingPressRow;
                _settingPressRow = -1;
                SetCheckPress(pressed, false);
                Invalidate(Rectangle.Inflate(OptionCheckRect(pressed), 7, 7));
            }

            // === 齿轮点击 ===
            if (_gearPressed)
            {
                _gearPressed = false;
                Invalidate(GearHotZone);

                // 按下与抬起都在热区内才算一次点击
                if (GearHotZone.Contains(e.Location))
                    ToggleSpread(new PointF(e.X, e.Y));
            }

            base.OnMouseUp(e);
        }

        // 滚轮滚动：一格滚过一整行  参数：e 携带 Delta（一格为 120）  返回：无
        // 为什么一格就是一整行：卡片是逐行罗列的列表，一格的语义就该是「下一项」；
        // 若按像素比例换算，滚一格的距离会随行高变化，手感不可预期。
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // 这几种模式下没有可滚的列表，滚轮不该产生位移
            if (_exitMenuMode || _spreadExpanded || IsSpreadBusy || HotKeyWarnBlocking)
            {
                base.OnMouseWheel(e);
                return;
            }

            // 一格 = 行高 + 行距，正好是一整行的位移量
            double step = RowHeight + RowGap;
            double target = _targetOffset + (e.Delta > 0 ? -step : step);
            // 夹在 [0, MaxOffset]：越界的目标值会让补间一直追不回来
            if (target < 0) target = 0;
            if (target > MaxOffset) target = MaxOffset;

            _targetOffset = target;
            // 残差大于半像素才值得起补间，否则直接停表，省掉定时器抖动
            if (Math.Abs(_targetOffset - _scrollOffset) > 0.5)
                StartScrollAnimation();
            else
                StopScrollAnimation();

            base.OnMouseWheel(e);
        }

        // ===================== 滚动条拖拽 =====================

        // 把滚动条拖到鼠标所在位置  参数：mouseY 鼠标纵坐标（像素）  返回：无
        // 拖拽按相对位移映射，不走补间：手指到哪内容就跟到哪；
        // 偏移量按「鼠标位移占可拖动行程的比例」换算，行程为 0 时退化为置顶。
        private void DragThumbTo(int mouseY)
        {
            var track = ScrollTrackRect;
            var thumb = ScrollThumbRect;
            // 没有滚动条（内容不足一屏）时无可拖
            if (thumb.IsEmpty)
                return;

            // travel 是滚动条能移动的像素行程，也是换算的分母
            int travel = track.Height - thumb.Height;
            // 行程或可滚距离为 0：拖拽退化成「回到顶部」
            if (travel <= 0 || MaxOffset <= 0)
            {
                _scrollOffset = 0;
                _targetOffset = 0;
                InvalidateList();
                return;
            }

            // 线性映射：鼠标走完 travel，内容刚好走完 MaxOffset
            double offset = _dragStartOffset + (mouseY - _dragStartY) * MaxOffset / travel;
            if (offset < 0) offset = 0;
            if (offset > MaxOffset) offset = MaxOffset;

            // 同时写当前位置与目标值：松手后不会被旧的补间目标拽回去
            _scrollOffset = offset;
            _targetOffset = offset;
            InvalidateList();
        }

        #endregion
        #region 平滑滚动

        // 启动滚动补间定时器（16ms ≈ 60fps）  参数：无  返回：无
        // 重复调用是安全的：定时器已存在就复用，已在跑就不重启，避免相位被重置。
        private void StartScrollAnimation()
        {
            // 滚动会让行位移，旧的星标悬停已指向错误位置
            ClearStarHover();

            // 行高亮同理，重绘后由 OnMouseMove 重新判定
            _hoverDeviceIndex = -1;

            // 懒创建：第一次需要滚动时才分配定时器
            if (_scrollTimer == null)
            {
                _scrollTimer = new Timer { Interval = 16 };
                _scrollTimer.Tick += ScrollTimer_Tick;
            }
            // 已在跑就不再 Start：重复 Start 会重置相位，动画从头再来
            if (!_scrollTimer.Enabled)
                _scrollTimer.Start();
        }

        // 停止滚动补间  参数：无  返回：无
        // 只停表不改进度：下次滚轮从当前位置继续，不会跳回起点。
        private void StopScrollAnimation()
        {
            if (_scrollTimer != null && _scrollTimer.Enabled)
                _scrollTimer.Stop();
        }

        // 滚动补间的每帧回调：指数逼近目标偏移  参数：sender 触发事件的定时器  返回：无
        // 每帧吃掉剩余距离的 28%，不必预先算总时长；目标被连续滚轮改写时也能自然接住。
        private void ScrollTimer_Tick(object sender, EventArgs e)
        {
            double diff = _targetOffset - _scrollOffset;
            // 残差不足 0.6 像素人眼已看不出，直接吸附并停表
            if (Math.Abs(diff) < 0.6)
            {
                _scrollOffset = _targetOffset;
                StopScrollAnimation();
            }
            else
            {
                // 0.28 约十余帧收敛（60fps 下约 0.2 秒）：有惯性又不拖沓
                _scrollOffset += diff * 0.28;
            }

            InvalidateList();
        }

        #endregion
    }
}