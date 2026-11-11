// ============================================================
// SettingsCardForm.Warn.cs - 「热键冲突」提示浮层的淡入淡出与毛玻璃遮罩
// 功能：把卡片底层离屏渲染后缩到九分之一再放大回来，叠一层半透明遮罩显示两行提示
// 说明：本文件是 SettingsCardForm 的分块（partial），与 Draw/Settings/Input 等分块配合
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
        #region 热键失败提示浮层

        // ===================== 时长与几何常量 =====================

        private const int HotKeyWarnFadeIn = 220;   // 淡入时长（毫秒）：220ms 足够注意到有东西升起来
        private const int HotKeyWarnHold = 3500;    // 完全显示的停留时长（毫秒）：3.5 秒够读完两行字
        private const int HotKeyWarnFadeOut = 400;  // 淡出时长（毫秒）：比淡入更长，离场要更柔
        private const int HotKeyWarnRadius = 10;    // 遮罩圆角半径（像素）
        private const int HotKeyWarnInset = 2;      // 遮罩相对卡片的内缩（像素）：给卡片描边留位置

        // ===================== 运行时状态与离屏缓存 =====================

        private string _hotKeyWarnLine1 = "";                     // 主提示文案（第一行）
        private string _hotKeyWarnLine2 = "";                     // 次要说明（第二行，可为空）
        private double _hotKeyWarnAlpha;                          // 当前整体透明度（0~1）
        private int _hotKeyWarnPhase;                             // 当前阶段：0 空闲 / 1 淡入 / 2 停留 / 3 淡出
        private int _hotKeyWarnPhaseStart;                        // 当前阶段的起始 TickCount（毫秒）
        private double _hotKeyWarnFadeFrom = 1.0;                 // 淡出起点透明度：中途被打断时从当前值接着淡
        private int _hotKeyWarnFadeDuration = HotKeyWarnFadeOut;  // 淡出时长（毫秒）：自然淡出与「点击立即消失」共用
        private Timer _hotKeyWarnTimer;                           // 16ms 帧定时器，只在浮层可见期间运行

        private Bitmap _hotKeyWarnLayer;  // 卡片尺寸的离屏位图：模糊的素材来源
        private Bitmap _hotKeyWarnSmall;  // 缩到 1/9 的中间位图：放大回原尺寸时它就是「模糊」本身

        // ===================== 位图管理 =====================

        // 取一块尺寸匹配的离屏位图，尺寸不符就重建  参数：cache 缓存字段（按引用传入），width/height 目标尺寸（像素）  返回：可用位图
        // 用 ref 传字段：两个缓存字段共用这一份逻辑，省得写两遍几乎相同的代码。
        private static Bitmap EnsureWarnBitmap(ref Bitmap cache, int width, int height)
        {
            // 尺寸正好就复用：绘制期间不再分配新的 GDI 资源
            if (cache != null && cache.Width == width && cache.Height == height)
                return cache;

            // 尺寸变了先释放旧的：位图是稀缺资源，不能等 GC
            if (cache != null)
                cache.Dispose();

            // 统一 32 位带 Alpha：遮罩层需要逐像素透明度
            cache = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            return cache;
        }

        // 释放两张离屏位图  参数：无  返回：无
        // 字段置 null 是必要的：EnsureWarnBitmap 以 null 作为「还没建」的判据。
        private void DisposeWarnBitmaps()
        {
            if (_hotKeyWarnLayer != null)
            {
                _hotKeyWarnLayer.Dispose();
                _hotKeyWarnLayer = null;
            }

            // 第二张同样处理：漏一张就漏一块内存
            if (_hotKeyWarnSmall != null)
            {
                _hotKeyWarnSmall.Dispose();
                _hotKeyWarnSmall = null;
            }
        }

        // 浮层是否正在挡住底下的交互  参数：无  返回：true 时鼠标事件一律不往下分派
        // 判据是「阶段 1 或透明度 > 0.3」：
        // 阶段 1 是淡入中，此时 alpha 还很小，只看透明度会让浮层在最初一两帧形同虚设，
        // 用户仍能点到底下的设备行；而淡出到 0.3 以下就放开，剩下的残影已不影响点击判断，
        // 早点还回操作手感。
        private bool HotKeyWarnBlocking
        {
            get { return _hotKeyWarnPhase == 1 || (_hotKeyWarnPhase != 0 && _hotKeyWarnAlpha > 0.3); }
        }

        // 弹出热键冲突提示  参数：line1 主提示文案，line2 次要说明（可为空）  返回：无
        // 已经在退出/展开/扩散过程中就不弹：这些状态下的绘制层级与浮层冲突，
        // 硬弹会挡住正在进行的动画。
        public void ShowHotKeyWarning(string line1, string line2)
        {
            // 三种「正在做别的事」的模式下一律放弃，避免两层动画叠在一起
            if (_exitMenuMode || _spreadExpanded || IsSpreadBusy)
                return;

            // null 归一成空串，绘制时只需判断长度
            _hotKeyWarnLine1 = line1 ?? "";
            _hotKeyWarnLine2 = line2 ?? "";
            // 透明度、阶段、阶段起点一起重置，等于把时间线拨回开头
            _hotKeyWarnAlpha = 0;
            _hotKeyWarnPhase = 1;
            _hotKeyWarnPhaseStart = Environment.TickCount;
            _hotKeyWarnFadeFrom = 1.0;
            _hotKeyWarnFadeDuration = HotKeyWarnFadeOut;

            // 顺手清掉悬停态：浮层之下不该还有高亮的行
            _hoverDeviceIndex = -1;
            _gearHover = false;
            ClearStarHover();

            // 定时器懒创建，已在跑就直接复用
            EnsureHotKeyWarnTimer();
            // 显式 Start：之前可能刚被停掉
            _hotKeyWarnTimer.Start();
            Invalidate();
        }

        // 立即开始淡出（用户点击浮层时调用）  参数：无  返回：无
        // 从当前透明度起淡出、时长压到 160ms：点击是「我知道了」，要比自然消失更快。
        private void FadeOutHotKeyWarningNow()
        {
            // 没有浮层或已在淡出：不重复触发，否则时长会被反复改写
            if (_hotKeyWarnPhase == 0 || _hotKeyWarnPhase == 3)
                return;

            // 起点取当前值：淡到一半再点一次也不会闪
            _hotKeyWarnFadeFrom = _hotKeyWarnAlpha;
            // 160ms 是「立刻」与「别太生硬」之间的折中
            _hotKeyWarnFadeDuration = 160;
            _hotKeyWarnPhase = 3;
            _hotKeyWarnPhaseStart = Environment.TickCount;

            // 可能刚被停过，重新 Start
            if (_hotKeyWarnTimer != null)
                _hotKeyWarnTimer.Start();
        }

        // 直接关掉浮层（不播动画）  参数：无  返回：无
        // 窗口重建这类「来不及看动画」的场合用它，状态一次性归零。
        private void StopHotKeyWarning()
        {
            _hotKeyWarnPhase = 0;
            _hotKeyWarnAlpha = 0;
            if (_hotKeyWarnTimer != null)
                _hotKeyWarnTimer.Stop();
        }

        // 确保浮层的帧定时器已创建  参数：无  返回：无
        // 这里不主动 Start：何时开始计时由调用方决定，构造时就跑会白白空转。
        private void EnsureHotKeyWarnTimer()
        {
            // 已存在就直接返回：只有第一次需要挂 Tick 事件
            if (_hotKeyWarnTimer != null)
                return;
            _hotKeyWarnTimer = new Timer { Interval = 16 };
            _hotKeyWarnTimer.Tick += HotKeyWarnTimer_Tick;
        }

        // 浮层状态机的每帧回调  参数：sender 触发事件的定时器  返回：无
        // 用「阶段 + 阶段起始 TickCount」描述整条时间线：1 淡入 → 2 停留 → 3 淡出 → 0 空闲。
        private void HotKeyWarnTimer_Tick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            // 已过时间每帧只算一次，三个分支共用
            int elapsed = now - _hotKeyWarnPhaseStart;

            // 阶段即状态：每个 case 只负责「这一阶段该做什么」和「何时进下一阶段」
            switch (_hotKeyWarnPhase)
            {
                // 阶段 1：淡入，EaseOutRef 让透明度先快后慢地逼近 1
                case 1:
                    if (elapsed >= HotKeyWarnFadeIn)
                    {
                        // 到点把透明度钉成 1（避免浮点留下 0.999），随后转入停留阶段
                        _hotKeyWarnAlpha = 1.0;
                        _hotKeyWarnPhase = 2;
                        _hotKeyWarnPhaseStart = now;
                    }
                    else
                    {
                        // 用缓出而不是线性：最后一刻才「落定」，更像浮层浮上来
                        _hotKeyWarnAlpha = EaseOutRef((double)elapsed / HotKeyWarnFadeIn);
                    }
                    break;

                case 2:
                    // 停留结束：记下淡出起点与时长，再切到阶段 3
                    if (elapsed >= HotKeyWarnHold)
                    {
                        _hotKeyWarnFadeFrom = 1.0;
                        _hotKeyWarnFadeDuration = HotKeyWarnFadeOut;
                        _hotKeyWarnPhase = 3;
                        _hotKeyWarnPhaseStart = now;
                    }
                    break;

                case 3:
                    // 到点：透明度归零、阶段归零、停表，浮层彻底消失
                    if (elapsed >= _hotKeyWarnFadeDuration)
                    {
                        _hotKeyWarnAlpha = 0;
                        _hotKeyWarnPhase = 0;
                        _hotKeyWarnTimer.Stop();
                    }
                    else
                    {
                        // 1 - EaseOutRef(t)：从 1 平滑降到 0，开始快、接近 0 时慢
                        _hotKeyWarnAlpha = _hotKeyWarnFadeFrom *
                                          (1.0 - EaseOutRef((double)elapsed / _hotKeyWarnFadeDuration));
                    }
                    break;

                // 未知阶段（含 0）：停表退出，避免定时器空转
                default:
                    _hotKeyWarnTimer.Stop();
                    return;
            }

            Invalidate();
        }

        // ===================== 绘制 =====================

        // 遮罩矩形：整卡内缩 inset  参数：无  返回：遮罩矩形（像素）
        // 内缩 2 像素是给圆角描边留位置，遮罩压到边框上会糊掉卡片轮廓。
        private Rectangle HotKeyWarnRect()
        {
            return new Rectangle(HotKeyWarnInset, HotKeyWarnInset,
                Width - HotKeyWarnInset * 2, Height - HotKeyWarnInset * 2);
        }

        // 绘制提示浮层  参数：g 卡片绘图表面  返回：无
        // 三步：1. 离屏把卡片底层重画一遍  2. 缩到九分之一再放大回原尺寸  3. 按透明度铺回并叠遮罩与文字
        // 为什么先缩到 1/9 再放大：GDI+ 没有真正的模糊滤镜，缩小再放大等于让每个像素
        // 平均掉周围 9×9 的信息，配上 HighQualityBilinear 就是一次廉价的毛玻璃；
        // 比例再小就只剩色块、再大又糊得不够，1/9 是「还能认出底下有卡片」的折中。
        private void DrawHotKeyWarning(Graphics g)
        {
            double a = _hotKeyWarnAlpha;
            // 透明度为 0 时整层不可见，跳过也省掉一次离屏绘制
            if (a <= 0)
                return;

            var warnRect = HotKeyWarnRect();
            // 窗口被拉得极小时矩形会退化成 0 宽或 0 高，继续画会出异常
            if (warnRect.Width <= 0 || warnRect.Height <= 0)
                return;

            // 用整卡尺寸的离屏位图重画底层控件；不画浮层自己，避免自我叠加
            var layer = EnsureWarnBitmap(ref _hotKeyWarnLayer, Width, Height);
            using (var lg = Graphics.FromImage(layer))
            {
                lg.Clear(CardWhite);
                lg.SmoothingMode = SmoothingMode.AntiAlias;
                // 只画底层：齿轮、行、滚动条；浮层与文字留到后面单独叠
                DrawGearButton(lg);
                DrawRows(lg);
                DrawScrollbar(lg);
            }

            // 目标尺寸至少 1 像素：除以 9 后可能为 0，Math.Max 兜底
            int sw = Math.Max(1, warnRect.Width / 9);
            int sh = Math.Max(1, warnRect.Height / 9);
            var small = EnsureWarnBitmap(ref _hotKeyWarnSmall, sw, sh);
            using (var sg = Graphics.FromImage(small))
            {
                // 下采样：双线性在缩小时做区域平均，模糊正是从这里来的
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                // 高质量像素偏移，避免缩小后整体偏半像素
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // 只取 warnRect 那块内容缩到 sw×sh：内缩的 2 像素不参与模糊
                sg.DrawImage(layer, new Rectangle(0, 0, sw, sh), warnRect, GraphicsUnit.Pixel);
            }

            // Graphics 是共享的，先存下当前插值设置，画完再还原
            var savedInterp = g.InterpolationMode;
            var savedOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            try
            {
                using (var attrs = new ImageAttributes())
                {
                    var cm = new ColorMatrix();
                    // 颜色矩阵的 Matrix33 就是整体 Alpha，乘上 a 得到当前透明度
                    cm.Matrix33 = (float)a;
                    attrs.SetColorMatrix(cm);
                    // 放大回遮罩矩形（整卡内缩 2 像素）：小图被双线性摊开，得到柔和的底图
                    g.DrawImage(small,
                        new Rectangle(warnRect.X, warnRect.Y, warnRect.Width, warnRect.Height),
                        0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                }
            }
            // finally：无论绘制是否抛异常，都恢复共享的插值模式
            finally
            {
                g.InterpolationMode = savedInterp;
                g.PixelOffsetMode = savedOffset;
            }

            // === 遮罩：半透明玻璃色 + 一圈更淡的描边 ===
            using (var path = CreateRoundPath(warnRect, HotKeyWarnRadius))
            {
                // 170 是满不透明时的基准 Alpha，再乘浮层透明度
                using (var veil = new SolidBrush(Color.FromArgb(AlphaOf(170, a), OverlayGlass)))
                    g.FillPath(veil, path);
                // 描边比色纱淡：只用来交代边界，不抢文字
                using (var pen = new Pen(Color.FromArgb(AlphaOf(96, a), OverlayBorder), 1f))
                    g.DrawPath(pen, path);
            }

            // 没有主文案时不必再画文字（此时浮层只当一层遮罩）
            if (_hotKeyWarnLine1.Length == 0)
                return;

            // 文字透明度同样按 a 折算，与遮罩同步淡入淡出
            int textAlpha = AlphaOf(255, a);
            if (textAlpha <= 0)
                return;

            // 两行以中线为界：主文案在上、说明在下，间距 1 像素
            float centerY = warnRect.Y + warnRect.Height / 2f;
            var line1Rect = new Rectangle(warnRect.X, (int)Math.Round(centerY) - 22, warnRect.Width, 22);
            var line2Rect = new Rectangle(warnRect.X, (int)Math.Round(centerY) + 1, warnRect.Width, 18);

            using (var fmt = new StringFormat())
            {
                // 居中 + 单行：浮层高度固定，换行会把第二行挤出去
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;

                // 主文案用标题字体，权重更高
                using (var brush = new SolidBrush(Color.FromArgb(textAlpha, OverlayTextMain)))
                    g.DrawString(_hotKeyWarnLine1, _nameFont, brush, line1Rect, fmt);

                // 第二行为空时连本次绘制都省掉
                if (_hotKeyWarnLine2.Length > 0)
                    using (var brush = new SolidBrush(Color.FromArgb(textAlpha, OverlayTextSub)))
                        g.DrawString(_hotKeyWarnLine2, _interfaceFont, brush, line2Rect, fmt);
            }
        }

        // 把 0~1 的浮层透明度折算成 0~255 的 Alpha  参数：baseAlpha 基准 Alpha，a 浮层透明度（0~1）  返回：夹紧后的 Alpha
        // 单独抽出来是因为遮罩、描边、文字三处都要做同样的乘算与夹紧。
        private static int AlphaOf(int baseAlpha, double a)
        {
            // 四舍五入后再夹紧：乘出来的值可能略微越界
            int v = (int)Math.Round(baseAlpha * a);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }

        #endregion
    }
}
