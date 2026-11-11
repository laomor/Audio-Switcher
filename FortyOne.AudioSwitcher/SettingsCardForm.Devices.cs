// ============================================================
// SettingsCardForm.Devices.cs - 设备列表数据、滚动偏移与点击行为
// 功能：维护 _devices 列表与滚动量，把鼠标点击变成“本地乐观更新 + 事件通知”
// 说明：本文件是 SettingsCardForm 的分块（partial），只管数据与状态，不碰 GDI+ 绘制
// 编译：build.cmd（详见 编译说明.md）
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 设备数据与滚动量

        // ===================== 退出模式与设备列表 =====================
        // 进入退出模式时把卡片缩成只有一行胶囊的尺寸；回到列表模式的分支保留着，当前退出流程不调用
        // 两个方向都要复位消散与星形动画的状态，只有进入时才改变卡片尺寸
        // 切换退出菜单模式  参数：on true 进入退出模式 false 回到列表模式  返回：无
        public void SetExitMenuMode(bool on)
        {
            _exitMenuMode = on;   // 后续绘制据此走两套分支
            _exitRowHover = false;
            _exitRowPressed = false;
            _exitVanishStart = 0;   // 复位消散动画：每次进入退出模式都从头播
            _exitVanishDone = false;
            // 计时器先停：避免在切换尺寸的同一帧里 tick 到旧状态
            if (_exitVanishTimer != null)
                _exitVanishTimer.Stop();
            ClearStarAnims();   // 星形悬停/填充动画一并清掉，回到列表模式时不会残留

            if (on)
            {
                _devices.Clear();   // 列表区不参与退出模式的绘制，清空可省掉遍历
                // 滚动量与目标量都归零：退出模式没有可滚动的内容
                _scrollOffset = 0;
                _targetOffset = 0;
                // 句柄存在时重建一次，让窗口区域按新尺寸重新裁出圆角
                if (IsHandleCreated)
                    RecreateHandle();
                ClientSize = new Size(ExitCardWidth, ExitCardHeight);   // 卡片缩成只剩一行胶囊的尺寸
                ApplyRoundCorners();                                    // 按新尺寸重设圆角区域
            }
        }

        // 退出模式下胶囊的满尺寸矩形：始终等于整块客户区，动画再在此基础上缩放
        // 退出行矩形  参数：无  返回：矩形（客户区坐标）
        private Rectangle ExitRowRect
        {
            get { return new Rectangle(0, 0, ClientSize.Width, ClientSize.Height); }   // 不做缓存：卡片尺寸会随模式切换变化，每次取实时客户区最省心
        }

        // 更新设备列表的简写重载：不保留滚动位置，由默认设备重新决定滚到哪
        // 更新列表  参数：devices 新设备列表（可为 null）  返回：无
        public void SetDevices(IList<SettingsCardDevice> devices)
        {
            SetDevices(devices, false);   // 等价于 preserveScrollPosition=false：默认设备会被滚进视野
        }

        // 更新设备列表，并决定要不要为“默认项换行 / 通信设备换行”补播动画
        // 更新列表  参数：devices 新列表（可为 null）  preserveScrollPosition true 保持当前滚动位置  返回：无
        // 步骤：1. 记下旧的默认行与通信行；2. 替换列表并夹紧偏移；3. 抑制乱序回跳；4. 判定动画落点。
        public void SetDevices(IList<SettingsCardDevice> devices, bool preserveScrollPosition)
        {
            int oldDefault = DefaultDeviceIndex();          // 旧默认行：既是掉落动画的起点，也是判断“默认项换没换”的依据
            int oldComm = CommunicationsDeviceIndex();      // 旧通信行：角标要不要播滑入，看它有没有变

            // 逐个拷贝而不是直接换引用：_devices 是只读字段，整个窗口生命周期内保持同一个列表实例
            _devices.Clear();
            // 允许传 null：调用方在枚举设备失败时可以先用空列表把界面清干净
            if (devices != null)
            {
                foreach (var d in devices)
                    _devices.Add(d);
            }

            // 设备数变了，偏移可能越界，先夹进合法范围再谈滚动
            ClampOffsets();
            // 不保持滚动位置时，让默认设备滚进视野
            if (!preserveScrollPosition)
                EnsureDefaultVisible(oldDefault);

            // 换表后重新取默认行：下面的判据全部基于“新列表里的行号”
            int newDefault = DefaultDeviceIndex();

            // === 抑制设备切换事件的乱序回跳 ===
            // 点击时本窗体已经乐观地把 IsDefault 改到新行，但主窗体随后可能先送来一份“点击前”的旧快照，
            // 于是 newDefault 会短暂退回 _switchFromIndex，视觉上就是默认项又弹回旧行。
            // 判据：动画开始不到 120ms、新默认行正好是刚离开的那行、又不是动画目标行——满足即认定这份列表过期，
            // 直接就地改写 IsDefault 对齐到动画目标，等下一份正确的列表到达再自然收敛。
            if (_switchStart != 0 && _switchTargetIndex >= 0 && _switchFromIndex >= 0
                && newDefault >= 0
                && newDefault == _switchFromIndex
                && newDefault != _switchTargetIndex
                && Environment.TickCount - _switchStart < 120)
            {
                // 就地改 IsDefault 而不是丢掉列表：这些是卡片自己的快照对象，改属性即可
                for (int i = 0; i < _devices.Count; i++)
                    _devices[i].IsDefault = i == _switchTargetIndex;   // 只把动画目标行标成默认
                newDefault = _switchTargetIndex;                       // 同步 newDefault，避免后文再触发一次掉落动画
            }

            // 通信设备也可能随换表换行，角标动画需要它
            int newComm = CommunicationsDeviceIndex();

            int dropIndex = -1;   // -1 表示“不需要掉落动画”
            // 只在窗口可见时播动画：隐藏时没人看，白跑计时器
            if (Visible && oldDefault >= 0 && newDefault >= 0 && oldDefault != newDefault)
                dropIndex = newDefault;   // 落点就是新的默认行

            int badgeIndex = -1;   // -1 表示“不需要角标动画”
            // 与掉落同理，只在可见且通信设备换行时才播
            if (Visible && oldComm >= 0 && newComm >= 0 && oldComm != newComm)
                badgeIndex = newComm;   // 落点就是新的通信设备行

            // 任一落点有效就启动动画；“目标没变”的情况由 StartDeviceSwitch 内部处理
            if (dropIndex >= 0 || badgeIndex >= 0)
                StartDeviceSwitch(dropIndex, badgeIndex, oldDefault);

            _hoverDeviceIndex = -1;   // 换表后旧悬停下标可能指向别的设备，一律清掉
            ClearStarHover();         // 星形悬停态同理

            // 没有动画时才整窗重绘；有动画时由计时器逐帧只刷列表区
            if (_switchStart == 0)
                Invalidate();
        }

        // 查找 Windows“默认通信设备”所在的行
        // 通信设备行  参数：无  返回：行下标；没有则 -1
        private int CommunicationsDeviceIndex()
        {
            // 线性扫描：通信设备与默认设备可能不是同一行，必须单独查
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i].IsCommunicationsDevice)
                    return i;
            }
            return -1;   // 没有通信设备（或已被禁用）：返回 -1 让调用方跳过角标
        }

        // 查找当前默认设备所在的行
        // 默认设备行  参数：无  返回：行下标；没有则 -1
        private int DefaultDeviceIndex()
        {
            // 设备最多几十行，维护索引反而容易和数据不同步
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i].IsDefault)
                    return i;
            }
            return -1;
        }

        // 把默认设备滚进可视区：只有它原本被滚出视野时才动滚动位置
        // 保证默认项可见  参数：previousDefaultIndex 上一次的默认行  返回：无
        private void EnsureDefaultVisible(int previousDefaultIndex)
        {
            int index = DefaultDeviceIndex();   // 以新列表为准
            // 没有默认设备，或内容本来就装得下，都不需要滚动
            if (index < 0 || MaxOffset <= 0)
                return;

            // 默认行没变就不打扰用户当前的滚动位置
            if (previousDefaultIndex == index)
                return;

            double rowTop = index * (RowHeight + RowGap);   // 该行上沿的内容坐标（像素）

            // 以当前目标偏移为基准判断：太靠上就让上沿贴视口顶，太靠下就让下沿贴视口底
            double target = _targetOffset;
            if (rowTop < target)
                target = rowTop;
            else if (rowTop + RowHeight > target + ViewHeight)
                target = rowTop + RowHeight - ViewHeight;

            // 夹进合法滚动范围
            if (target > MaxOffset) target = MaxOffset;
            if (target < 0) target = 0;

            // 位移小于半像素不启动动画：否则每次刷新都播一段看不出来的滚动
            if (Math.Abs(target - _targetOffset) > 0.5)
            {
                _targetOffset = target;   // 只改目标值，实际偏移由滚动动画逐帧逼近
                StartScrollAnimation();
            }
        }

        // 让默认设备刚好完整露出来所需的滚动偏移（卡片刚打开时的初始滚动位置）
        // 默认滚动位置  参数：无  返回：滚动偏移（像素，已夹紧）
        private double DefaultRowOffset()
        {
            // 找到第一个默认设备即返回
            for (int i = 0; i < _devices.Count; i++)
            {
                if (!_devices[i].IsDefault)
                    continue;

                double rowTop = i * (RowHeight + RowGap);   // 该行上沿的内容坐标
                double target = 0;                          // 默认从顶部开始：内容装得下时结果就是 0
                if (rowTop + RowHeight > ViewHeight)
                    target = rowTop + RowHeight - ViewHeight;   // 行下沿超出视口：让它贴到视口底部
                // 夹进合法滚动范围
                if (target > MaxOffset)
                    target = MaxOffset;
                if (target < 0)
                    target = 0;
                return target;
            }
            return 0;
        }

        // ===================== 选项与热键 =====================
        // 一次性同步两个开关状态与热键文案（主窗体打开卡片时用来灌初始状态）
        // 同步选项  参数：autoStart 开机自启  dualSwitch 双设备模式  hotKeyText 热键文本  返回：无
        public void SetOptions(bool autoStart, bool dualSwitch, string hotKeyText)
        {
            _optAutoStart = autoStart;
            _optDualSwitch = dualSwitch;
            // 空文案统一成“未设置”占位，热键行才不会看起来是空白
            _hotKeyText = string.IsNullOrEmpty(hotKeyText) ? UiText.HotKeyNone : hotKeyText;
            ClearHotKeyEditing();                     // 外部灌入的状态视为终态，先退出编辑态再刷新
            SetCheckValueImmediately(0, autoStart);   // 行 0：开机自启
            SetCheckValueImmediately(1, dualSwitch);  // 行 1：双设备模式
            Invalidate();                             // 最后整窗重绘一次，把热键与开关的终态画出来
        }

        // 把某行开关直接置到终态，不播动画（用于外部灌初始状态）
        // 立即置值  参数：row 行号（0 自启 1 双切换）  value 目标值  返回：无
        private void SetCheckValueImmediately(int row, bool value)
        {
            _checkValue[row] = value ? 1.0 : 0.0;      // 当前值就是目标值：绘制立刻画成终态
            _checkAnimFrom[row] = _checkValue[row];    // 起始值对齐，动画插值不会跳变
            _checkAnimTo[row] = _checkValue[row];      // 目标值对齐
            _checkAnimStart[row] = 0;                  // 时间戳清零：本类约定 0 表示“没有动画在跑”
            _checkBounceStart[row] = 0;                // 回弹动画同理
        }

        // 更新热键文案，并标记是否以错误色提示（热键注册失败时用）
        // 设置热键文本  参数：text 新文案  isError 是否用暖黄文字提示错误  返回：无
        public void SetHotKeyText(string text, bool isError)
        {
            _hotKeyText = string.IsNullOrEmpty(text) ? UiText.HotKeyNone : text;   // 空文案显示占位文本
            _hotKeyError = isError;   // 错误态由绘制分支决定颜色
            Invalidate();             // 立即重绘，让新文案与错误态马上生效
        }

        // 同步单个开关的目标状态，必要时播一段勾选动画
        // 设置开关  参数：option 选项枚举  value 目标值  返回：无
        public void SetOptionValue(SettingsCardOption option, bool value)
        {
            int row;   // row 只在这个 switch 里赋值，用于定位动画数组下标
            switch (option)
            {
                case SettingsCardOption.AutoStartWithWindows:
                    _optAutoStart = value;
                    row = 0;   // 行 0 对应开机自启
                    break;
                case SettingsCardOption.DualSwitchMode:
                    _optDualSwitch = value;
                    row = 1;   // 行 1 对应双设备模式
                    break;
                default:
                    return;    // 未知选项直接忽略：对新增枚举保持向前兼容
            }

            double target = value ? 1.0 : 0.0;   // 动画与判重都用 0~1 的归一化值
            // 已经在朝同一目标动画、或已经到位，就不要再起新动画：否则会打断正在走的曲线
            bool alreadyAnimatingToTarget = _checkAnimStart[row] != 0 &&
                Math.Abs(_checkAnimTo[row] - target) < 0.001;
            bool alreadyThere = Math.Abs(_checkValue[row] - target) < 0.001;

            // 需要真的变化才启动动画
            if (!alreadyAnimatingToTarget && !alreadyThere)
                StartCheckAnimation(row, value);
            Invalidate();   // 统一重绘一次，后续帧由勾选动画的计时器推进
        }

        // ===================== 尺寸与滚动量 =====================
        // 列表内容的总高度：所有行高度加行间间隔
        // 内容高度  参数：无  返回：总高（像素）
        private int ContentHeight
        {
            get
            {
                if (_devices.Count == 0)
                    return 0;   // 空列表没有内容高度
                return _devices.Count * RowHeight + (_devices.Count - 1) * RowGap;   // 行间隔只算在行与行之间，所以是 Count-1 个
            }
        }

        // 可视区高度：VisibleRows 行加它们的行间间隔
        // 视口高度  参数：无  返回：高度（像素）
        private static int ViewHeight
        {
            get { return VisibleRows * RowHeight + (VisibleRows - 1) * RowGap; }   // 静态属性：可见行数是布局常量，与实例状态无关
        }

        // 最大滚动偏移：内容高度超出视口的部分
        // 最大偏移  参数：无  返回：像素；内容装得下时为 0
        private double MaxOffset
        {
            get
            {
                double max = ContentHeight - ViewHeight;   // 负值意味着不需要滚动
                return max > 0 ? max : 0;                  // 统一收敛到 0，滚动条与惯性滚动都不必再判负
            }
        }

        // 把目标偏移与当前偏移都夹进 [0, MaxOffset]
        // 夹紧偏移  参数：无  返回：无
        private void ClampOffsets()
        {
            double max = MaxOffset;   // 目标偏移先夹：设置 → 动画 → 实际位置，源头必须合法
            if (_targetOffset > max) _targetOffset = max;   // 上限
            if (_targetOffset < 0) _targetOffset = 0;       // 下限
            // 当前偏移再夹：设备列表突然变短时，绘制不能停在越界位置
            if (_scrollOffset > max) _scrollOffset = max;
            if (_scrollOffset < 0) _scrollOffset = 0;
        }

        #endregion
        #region 点击行为

        // 用户点击某个设备行：先本地乐观更新，再把点击事件发给主窗体
        // 激活设备  参数：deviceIndex 行下标  返回：无
        private void ActivateDevice(int deviceIndex)
        {
            var device = _devices[deviceIndex];
            if (device.IsDefault)
                return;   // 点已生效的默认项不重复触发：否则会白跑一次动画和一次真实切换

            int fromIndex = DefaultDeviceIndex();   // 记下原默认行，作为高亮块掉落的起点
            // === 本地乐观更新：先改状态让界面立刻响应 ===
            for (int i = 0; i < _devices.Count; i++)
                _devices[i].IsDefault = i == deviceIndex;   // 就地改 IsDefault，不等主窗体回灌列表

            StartDeviceSwitch(deviceIndex, -1, fromIndex);   // 角标传 -1：本次不预判通信设备，双切模式下回灌列表时再补角标动画

            // 复制到局部变量再判空：订阅者在回调里退订时这里不会空引用
            var handler = DeviceActivated;
            if (handler != null)
                handler(this, new SettingsCardDeviceEventArgs(device.Id));   // 把设备 Id 发给主窗体，由它调用系统的音频策略接口
        }

        // 切换某行的收藏状态：先本地翻转并播星形动画，再通知主窗体持久化
        // 切换收藏  参数：deviceIndex 行下标  返回：无
        private void ToggleFavourite(int deviceIndex)
        {
            var device = _devices[deviceIndex];
            device.IsFavourite = !device.IsFavourite;       // 本地先翻：星形动画不被主窗体往返的延迟拖慢
            StartStarFill(device.Id, device.IsFavourite);   // 用设备 Id 启动填充动画：滚动后行下标变了也不会串行
            Invalidate();                                   // 立即重绘让星形先动起来，后续帧由星形动画计时器驱动
            // 再通知主窗体把收藏列表写回配置
            var handler = FavouriteToggled;
            if (handler != null)
                handler(this, new SettingsCardDeviceEventArgs(device.Id));   // 同样带设备 Id：主窗体据此找到要持久化的设备
        }

        #endregion
    }
}