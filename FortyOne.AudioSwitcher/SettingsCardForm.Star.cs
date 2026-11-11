// ============================================================
// SettingsCardForm.Star.cs - 收藏星形的悬停、填充与回弹动画
// 功能：按设备 GUID 维护每颗星的动画状态，在 16ms 定时器里逐帧推进
// 说明：本文件是 SettingsCardForm 的分块（partial），与 Draw/Settings 等分块配合
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 收藏星动效

        // ===================== 状态读取与写入 =====================

        // 取某台设备对应的星形动画状态，没有就就地新建  参数：id 设备 GUID  返回：该设备的 StarAnim
        // 用 GUID 而不是行号做键：设备列表会刷新重排，行号一变动画就会串到别的设备上。
        private StarAnim StarAnimFor(Guid id)
        {
            StarAnim anim;
            if (!_starAnims.TryGetValue(id, out anim))
            {
                anim = new StarAnim();
                _starAnims[id] = anim;
            }
            return anim;
        }

        // 读取某颗星当前的悬停进度  参数：id 设备 GUID  返回：悬停进度（0~1；无记录时为 0）
        private double StarHoverOf(Guid id)
        {
            StarAnim anim;
            return _starAnims.TryGetValue(id, out anim) ? anim.Hover : 0.0;
        }

        // 读取某颗星当前的回弹缩放量  参数：id 设备 GUID  返回：缩放增量（0 为未放大）
        private double StarBounceOf(Guid id)
        {
            StarAnim anim;
            return _starAnims.TryGetValue(id, out anim) ? anim.Bounce : 0.0;
        }

        // 读取某颗星的填充进度  参数：id 设备 GUID，filled 该设备是否已收藏  返回：填充进度（0~1）
        // FillStart 为 0 表示没有正在进行的填充动画，此时直接返回目标值：
        // 首次绘制不该从 0 补间出来，否则每次刷新列表都会看到星「重新长一遍」。
        private double StarFillOf(Guid id, bool filled)
        {
            StarAnim anim;
            if (_starAnims.TryGetValue(id, out anim) && anim.FillStart != 0)
                return anim.Fill;
            return filled ? 1.0 : 0.0;
        }

        // 启动某颗星的悬停补间  参数：id 设备 GUID，on 是否悬停  返回：无
        // 四个字段描述一段补间：起点、终点、时长（毫秒）、起始 TickCount；
        // 定时器只读这些字段，所以中途反向时只改起点、不跳变。
        private void SetStarHover(Guid id, bool on)
        {
            // 空 GUID 不是有效设备，直接忽略
            if (id == Guid.Empty)
                return;

            var anim = StarAnimFor(id);
            // 已经是熄灭态且没有进行中的补间，不必再把定时器叫醒
            if (!on && anim.HoverStart == 0 && anim.Hover <= 0.0)
                return;

            // 起点取当前值而不是上次的目标值，反向时才能平滑接上
            anim.HoverFrom = anim.Hover;
            anim.HoverTo = on ? 1.0 : 0.0;
            anim.HoverDur = StarHoverDuration;
            anim.HoverStart = Environment.TickCount;
            EnsureStarTimer();
        }

        // 启动一次填充补间（点亮或取消收藏）  参数：id 设备 GUID，filled 目标是否为已收藏  返回：无
        // 只有点亮才追加回弹：取消收藏是收束动作，弹一下反而显得轻浮。
        private void StartStarFill(Guid id, bool filled)
        {
            var anim = StarAnimFor(id);
            // 起点由目标值反推，避免依赖当前值造成中断时跳变
            anim.FillFrom = filled ? 0.0 : 1.0;
            anim.FillTo = filled ? 1.0 : 0.0;
            anim.FillDur = StarFillDuration;
            // 计时起点：定时器用「当前 TickCount - 这个值」算已过时间
            anim.FillStart = Environment.TickCount;

            // === 点亮时顺带起一段放大回弹 ===
            if (filled)
            {
                anim.BounceFrom = 0.0;
                anim.BounceTo = StarBounceLift;
                anim.BounceDur = StarBounceRise;
                // 回弹与填充各有各的时长：颜色和缩放不该互相等待
                anim.BounceStart = Environment.TickCount;
            }

            EnsureStarTimer();
        }

        // 根据指针位置切换当前悬停的那颗星  参数：p 鼠标坐标，deviceIndex 命中的设备行（-1 表示没有）  返回：无
        // 只认右侧星标块：指针停在行中间不该点亮星，否则整行都像可点。
        private void UpdateStarHover(Point p, int deviceIndex)
        {
            Guid target = Guid.Empty;
            // 扩散态或扩散动画中设备行已被设置行覆盖，不再认悬停
            if (!_spreadExpanded && !IsSpreadBusy && deviceIndex >= 0 && deviceIndex < _devices.Count)
            {
                if (RightBlockRect(deviceIndex).Contains(p))
                    target = _devices[deviceIndex].Id;
            }

            // 目标没变就不动，否则每次鼠标移动都会重启补间，星形一直抖
            if (target == _starHoverId)
                return;

            var previous = _starHoverId;
            _starHoverId = target;
            // 先熄灭旧的再点亮新的，两颗星之间不共享状态
            if (previous != Guid.Empty)
                SetStarHover(previous, false);
            if (target != Guid.Empty)
                SetStarHover(target, true);
        }

        // 清掉当前悬停的星  参数：无  返回：无
        // 只有记住当前悬停的 GUID 才能精准关闭，否则只能把全表熄灭一遍。
        private void ClearStarHover()
        {
            if (_starHoverId == Guid.Empty)
                return;

            var previous = _starHoverId;
            _starHoverId = Guid.Empty;
            SetStarHover(previous, false);
        }

        // 整体丢弃所有星形动画状态  参数：无  返回：无
        // 设备列表重建后旧 GUID 可能已不存在，留着字典只会让定时器一直空转。
        private void ClearStarAnims()
        {
            _starAnims.Clear();
            _starHoverId = Guid.Empty;
            // 定时器一并停掉：没有设备要画，继续跑只是白烧 CPU
            if (_starTimer != null)
                _starTimer.Stop();
        }

        // 确保星形动画定时器已创建并在跑  参数：无  返回：无
        // 16ms 一次，与项目里其它补间同一节奏（约 60fps）。
        private void EnsureStarTimer()
        {
            if (_starTimer == null)
            {
                _starTimer = new Timer { Interval = 16 };
                _starTimer.Tick += StarTimer_Tick;
            }

            if (!_starTimer.Enabled)
                _starTimer.Start();
        }

        // ===================== 逐帧推进 =====================

        // 星形动画的每帧回调  参数：sender 触发事件的定时器  返回：无
        // 遍历所有设备的动画状态，各自按「已过时间 / 时长」推进；
        // 只要还有一颗星在动就保持定时器，全部静止后停表，空闲时零开销。
        private void StarTimer_Tick(object sender, EventArgs e)
        {
            // 三组补间共用同一个时间戳，避免各自取 TickCount 造成几毫秒错位
            int now = Environment.TickCount;
            bool busy = false;

            // 遍历字典而不是设备列表：不在可视区的行也照常推进，滚回来时不会看到半截动画
            foreach (var pair in _starAnims)
            {
                var anim = pair.Value;
                bool rowBusy = false;

                // === 悬停：缓出三次曲线，起步快、收尾稳 ===
                if (anim.HoverStart != 0)
                {
                    double t = Tween01(now - anim.HoverStart, anim.HoverDur);

                    // 到点直接吸附到目标，避免浮点残差让定时器无限跑下去
                    if (t >= 1.0)
                    {
                        anim.Hover = anim.HoverTo;
                        anim.HoverStart = 0;
                    }
                    else
                    {
                        // (1-t)^3：前段变化快，接近终点时几乎不动，收尾自然
                        anim.Hover = anim.HoverFrom +
                            (anim.HoverTo - anim.HoverFrom) * (1.0 - Math.Pow(1.0 - t, 3.0));
                        rowBusy = true;
                    }
                }

                // === 填充：点亮用缓出、熄灭用缓入，方向不同曲线也不同 ===
                if (anim.FillStart != 0)
                {
                    double t = Tween01(now - anim.FillStart, anim.FillDur);

                    if (t >= 1.0)
                    {
                        anim.Fill = anim.FillTo;
                        anim.FillStart = 0;
                    }
                    else
                    {
                        // 点亮要「弹出来」所以快起慢收，熄灭要「收回去」所以慢起快收
                        double eased;
                        if (anim.FillTo >= 1.0)
                            eased = 1.0 - Math.Pow(1.0 - t, 3.0);
                        else
                            eased = t * t;

                        anim.Fill = anim.FillFrom + (anim.FillTo - anim.FillFrom) * eased;
                        rowBusy = true;
                    }
                }

                // === 回弹：升到 StarBounceLift 后立刻换成下落段 ===
                if (anim.BounceStart != 0)
                {
                    double t = Tween01(now - anim.BounceStart, anim.BounceDur);

                    if (t >= 1.0)
                    {
                        anim.Bounce = anim.BounceTo;
                        // 升段走完且目标为正值：自动追加下落段，形成一次往返而不是无限循环
                        if (anim.BounceTo > 0.0)
                        {
                            anim.BounceFrom = anim.BounceTo;
                            anim.BounceTo = 0.0;
                            // 下落用另一套时长：一升一落不必对称，落得略慢才像被拉住
                            anim.BounceDur = StarBounceFall;
                            anim.BounceStart = now;
                            rowBusy = true;
                        }
                        else
                        {
                            anim.BounceStart = 0;
                        }
                    }
                    else
                    {
                        anim.Bounce = anim.BounceFrom +
                            (anim.BounceTo - anim.BounceFrom) * (1.0 - Math.Pow(1.0 - t, 3.0));
                        rowBusy = true;
                    }
                }

                // 这颗星这一帧没有任何补间在动，跳过重绘
                if (!rowBusy)
                    continue;

                // 只要还有一颗星在动，定时器就得留着
                busy = true;

                // 按 GUID 反查行号：字典的键是设备 GUID，重绘区域却要靠行号算
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].Id == pair.Key)
                    {
                        // 外扩 2 像素：回弹时星形四周都会变大，裁太紧会留下残影
                        Invalidate(Rectangle.Inflate(RightBlockRect(i), 2, 2));
                        break;
                    }
                }
            }

            // 全部静止：停表，下一次悬停再唤醒
            if (!busy && _starTimer != null)
                _starTimer.Stop();
        }

        #endregion
    }
}