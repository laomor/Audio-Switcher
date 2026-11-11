// ============================================================
// AudioEndpoints.cs - Windows Core Audio API 的托管封装
// 功能：枚举渲染端点并读出名称，订阅设备变更通知，用未公开的 IPolicyConfig 接口切换系统默认设备
// 设计理念：不引入额外封装库，只声明用得到的 COM 接口；临时 COM 对象在 finally 释放，常驻枚举器在 Dispose 释放
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FortyOne.AudioSwitcher.Audio
{
    // ===================== 端点状态标志 =====================
    // 端点状态位掩码  说明：可按位组合后作为 EnumAudioEndpoints 的过滤条件
    [Flags]
    public enum AudioEndpointState
    {
        Active = 0x1,      // 设备已插入且启用
        Disabled = 0x2,    // 设备被用户禁用
        NotPresent = 0x4,  // 接口未插入（驱动未加载）
        Unplugged = 0x8,   // 已拔出插孔
        All = 0xF          // 上述四种状态的并集
    }

    // ===================== 单个音频设备模型 =====================
    // 设备快照  说明：只在枚举时构造一次，之后不持有任何 COM 引用，界面可以随意读取
    public sealed class AudioEndpoint
    {
        // 构造设备快照  参数：realId 端点 ID，name/interfaceName/fullName 为三级名称，两个 bool 标记默认与通信默认身份
        internal AudioEndpoint(string realId, string name, string interfaceName, string fullName,
            bool isDefault, bool isCommunicationDefault)
        {
            RealId = realId;                                  // COM 端点 ID 原文，切换默认设备时原样回传
            Name = name;                                      // 简短名称，列表里优先显示
            InterfaceName = interfaceName;                    // 接口名，用来区分同名设备
            IsDefault = isDefault;                            // 当前是否控制台默认设备
            IsCommunicationDefault = isCommunicationDefault;  // 当前是否通信默认设备
            Id = ParseEndpointGuid(realId);                   // 从端点 ID 解出的 GUID，供界面记忆选择
            FullName = fullName;                              // 完整名称，托盘悬停提示使用
        }

        // ===================== 设备属性 =====================
        public Guid Id { get; private set; }                      // 端点 GUID，界面用它去重与持久化

        public string RealId { get; private set; }                // 原始端点 ID，切换默认设备时直接回传

        public string Name { get; private set; }                  // 简短显示名，取自设备描述

        public string InterfaceName { get; private set; }         // 接口友好名，可为空

        public string FullName { get; private set; }              // 完整显示名，托盘悬停提示使用

        public bool IsDefault { get; private set; }               // 控制台默认设备标记

        public bool IsCommunicationDefault { get; private set; }  // 通信默认设备标记

        // ===================== 默认设备切换 =====================
        // 同时占下控制台与多媒体两个默认设备  返回：两个角色都切换成功才为 true
        // 说明：宿主只有同步 COM 调用，这里用 Task.FromResult 包一层，调用方可以无差别 await
        public Task<bool> SetAsDefaultAsync()
        {
            return Task.FromResult(PolicyConfig.SetDefault(RealId, Role.Console)
                                   && PolicyConfig.SetDefault(RealId, Role.Multimedia));  // 短路求值：控制台失败就不再改多媒体
        }

        // 只切换通信默认设备  返回：切换成功为 true
        // 说明：语音与会议软件读的是 Communications 角色，与播放用的 Console 互不影响
        public bool SetAsCommunication()
        {
            return PolicyConfig.SetDefault(RealId, Role.Communications);
        }

        // ===================== 显示文本 =====================
        // 直接取可读名称，便于调试时查看  返回：完整设备名
        public override string ToString()
        {
            return FullName;
        }

        // ===================== 端点 ID 解析 =====================
        // 解析端点 ID 末尾的 GUID  参数：endpointId 形如 {0.0.0.00000000}.{GUID}（播放端）  返回：GUID，解析失败为 Guid.Empty
        private static Guid ParseEndpointGuid(string endpointId)
        {
            // === 空输入直接放弃 ===
            if (string.IsNullOrEmpty(endpointId))
                return Guid.Empty;

            // === 定位最后一对花括号 ===
            int close = endpointId.LastIndexOf('}');
            int open = close > 0 ? endpointId.LastIndexOf('{', close) : -1;
            if (open < 0 || close <= open)
                return Guid.Empty;

            // === 只解析花括号内部，避免把 0.0.0.00000000 前缀当成 GUID ===
            Guid id;
            return Guid.TryParse(endpointId.Substring(open + 1, close - open - 1), out id) ? id : Guid.Empty;
        }
    }

    // ===================== 设备枚举与变更通知 =====================
    // 枚举播放设备并订阅设备变更  说明：类内持有常驻 COM 枚举器，调用方必须 Dispose；变更用事件回传，避免轮询
    public sealed class AudioEndpointController : IDisposable
    {
        // ===================== 字段与状态 =====================
        private readonly IMMDeviceEnumerator _enumerator;  // 常驻枚举器，枚举与注册通知都走它
        private readonly EndpointNotificationSink _sink;   // 回调接收器，必须保持强引用，否则被回收后回调会崩
        private bool _disposed;                            // 是否已释放，防止重复注销

        // ===================== 原生互操作 =====================
        // 交回 ole32 释放 PROPVARIANT  说明：LPWStr 由 COM 任务分配器分配，托管堆无法代劳
        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(IntPtr variant);

        // ===================== 构造与事件 =====================
        // 创建枚举器并注册设备变更通知  说明：注册失败不影响枚举，异常被吞掉，最坏情况只是列表不自动刷新
        public AudioEndpointController()
        {
            // === 创建 COM 枚举器与回调接收器 ===
            _enumerator = (IMMDeviceEnumerator) new MMDeviceEnumeratorComObject();
            _sink = new EndpointNotificationSink(this);
            // === 注册变更通知（不支持的会话里静默降级） ===
            try
            {
                _enumerator.RegisterEndpointNotificationCallback(_sink);
            }
            catch
            {
            }
        }

        // 任一设备状态变化（含增删、启用/禁用）或默认设备变化时触发  说明：在 COM 回调线程上同步触发，订阅方需自行切回 UI 线程
        public event EventHandler DevicesChanged;

        // 把变更转发给订阅方  说明：先取局部副本，防止执行期间订阅者注销导致枚举失效
        internal void NotifyDevicesChanged()
        {
            var handler = DevicesChanged;
            if (handler == null)
                return;

            try { handler(this, EventArgs.Empty); } catch { }  // 订阅方异常不能越过 COM 边界，这里吞掉
        }

        // ===================== 设备枚举 =====================
        // 枚举播放设备并逐个拍快照  参数：state 状态位掩码（本工程只传 Active）  返回：设备列表，失败时为空列表
        public IList<AudioEndpoint> GetPlaybackDevices(AudioEndpointState state)
        {
            var result = new List<AudioEndpoint>();

            // === 先取两类默认设备，供下面逐个比对 ===
            string defaultId = GetDefaultEndpointId(Role.Console);
            string communicationId = GetDefaultEndpointId(Role.Communications);

            // === 枚举渲染端点 ===
            IMMDeviceCollection collection = null;
            try
            {
                _enumerator.EnumAudioEndpoints(DataFlow.Render, (int) state, out collection);  // DataFlow.Render 只取播放侧
                if (collection == null)
                    return result;

                // === 逐个读取集合元素 ===
                uint count;
                collection.GetCount(out count);  // 数量在枚举期间可能变化，取到后按它遍历

                for (uint i = 0; i < count; i++)
                {
                    // === 单个设备读取失败只跳过它，不影响整体 ===
                    IMMDevice device = null;
                    try
                    {
                        collection.Item(i, out device);  // 按序号取出设备接口
                        if (device == null)
                            continue;

                        var endpoint = Describe(device, defaultId, communicationId);  // 读属性存储并组装快照
                        if (endpoint != null)
                            result.Add(endpoint);
                    }
                    // 设备在枚举途中被拔掉时 Item 或 Describe 会失败
                    catch
                    {
                    }
                    finally
                    {
                        Release(device);  // 每个设备接口都要单独释放
                    }
                }
            }
            finally
            {
                Release(collection);  // 集合本身也是 COM 对象
            }

            return result;
        }

        // ===================== 设备描述 =====================
        // 1. 取端点 ID，取不到就放弃这台设备
        // 2. 打开只读属性存储，依次读描述、接口名、友好名
        // 3. 显示名按 描述 → 友好名 → 未命名设备 的顺序降级
        // 4. 用两类默认 ID 比对出默认身份
        // 参数：device 目标设备，defaultId/communicationId 为两类默认设备 ID  返回：设备快照或 null
        private static AudioEndpoint Describe(IMMDevice device, string defaultId, string communicationId)
        {
            // === 端点 ID 是后续一切描述的前提 ===
            string realId = GetDeviceId(device);
            if (string.IsNullOrEmpty(realId))
                return null;

            // === 属性存储里的三个候选名，都可能为空 ===
            string description = null;    // 设备描述，最贴近用途
            string interfaceName = null;  // 接口友好名，可能是声卡型号
            string friendlyName = null;   // 友好名，用户在系统设置里看到的名字

            // === 只读打开属性存储，绝不修改设备配置 ===
            IPropertyStore store = null;
            try
            {
                device.OpenPropertyStore(StorageAccess.Read, out store);  // 只读打开，不修改设备配置
                if (store != null)
                {
                    description = ReadString(store, PropertyKeys.DeviceDescription);
                    interfaceName = ReadString(store, PropertyKeys.DeviceInterfaceFriendlyName);
                    friendlyName = ReadString(store, PropertyKeys.DeviceFriendlyName);
                }
            }
            finally
            {
                Release(store);
            }

            // === 显示名降级：描述 → 友好名 → 占位 ===
            var name = !string.IsNullOrEmpty(description)
                ? description
                : (!string.IsNullOrEmpty(friendlyName) ? friendlyName : "未命名设备");

            // === 完整名：有友好名就直接用，否则拼上接口名区分同名设备 ===
            var fullName = !string.IsNullOrEmpty(friendlyName)
                ? friendlyName
                : (string.IsNullOrEmpty(interfaceName) ? name : name + " (" + interfaceName + ")");

            // 端点 ID 大小写不固定，两类默认身份都用 OrdinalIgnoreCase 比较
            return new AudioEndpoint(
                realId,
                name,
                interfaceName,
                fullName,
                string.Equals(realId, defaultId, StringComparison.OrdinalIgnoreCase),
                string.Equals(realId, communicationId, StringComparison.OrdinalIgnoreCase));
        }

        // ===================== 默认设备查询 =====================
        // 读指定角色当前默认设备的端点 ID  参数：role 为控制台/多媒体/通信之一  返回：端点 ID，无默认设备时为 null
        private string GetDefaultEndpointId(Role role)
        {
            IMMDevice device = null;
            try
            {
                _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role, out device);
                return device == null ? null : GetDeviceId(device);
            }
            // 该角色一个可用设备都没有时查询会失败，按没有默认设备处理
            catch
            {
                return null;
            }
            finally
            {
                Release(device);
            }
        }

        // ===================== 设备 ID 读取 =====================
        // 取设备端点 ID  参数：device 目标设备  返回：ID 字符串或 null  说明：缓冲区由 COM 任务分配器分配，必须 FreeCoTaskMem 归还
        private static string GetDeviceId(IMMDevice device)
        {
            IntPtr buffer = IntPtr.Zero;  // 非托管字符串指针，失败时保持零值
            try
            {
                device.GetId(out buffer);  // 返回 CoTaskMem 分配的 UTF-16 字符串
                return buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(buffer);  // PtrToStringUni 只复制内容，所有权仍归缓冲区
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(buffer);
            }
        }

        // ===================== 属性存储读取 =====================
        // 1. 按位宽分配 PROPVARIANT 缓冲区（x64 24 字节、x86 16 字节）并整体清零
        // 2. 调 GetValue，非 0 的 HRESULT 视为读取失败
        // 3. 校验 vt 为 VT_LPWSTR(31)，再从偏移 8 取出字符串指针
        // 4. finally 里先 PropVariantClear 释放字符串，再释放缓冲区本身
        // 参数：store 属性存储，key 属性键  返回：属性值，缺失或类型不符时为 null
        private static string ReadString(IPropertyStore store, PropertyKey key)
        {
            int size = IntPtr.Size == 8 ? 24 : 16;     // x64 为 24 字节，x86 为 16 字节
            IntPtr pv = Marshal.AllocCoTaskMem(size);  // 交给 COM 填充，用完由本方法释放
            try
            {
                // 先整体清零，避免残留的 vt 或指针被 PropVariantClear 误读
                for (int i = 0; i < size; i++)
                    Marshal.WriteByte(pv, i, 0);

                var local = key;                         // GetValue 要求 ref，这里复制一份避免改动调用方的键
                if (store.GetValue(ref local, pv) != 0)  // 非 0 表示属性不存在或类型不匹配
                    return null;

                ushort vt = (ushort) Marshal.ReadInt16(pv, 0);  // vt 字段在结构体开头（偏移 0）
                if (vt != VariantType.LPWStr)                   // 只认 VT_LPWSTR，其它类型不解析
                    return null;

                // PROPVARIANT 在 x64 下 24 字节（x86 为 16）：偏移 0 是 vt(2 字节)，紧跟 6 字节保留，偏移 8 起才是联合体里的指针
                IntPtr text = Marshal.ReadIntPtr(pv, 8);
                return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text);  // 只复制字符串，所有权仍归缓冲区
            }
            finally
            {
                try { PropVariantClear(pv); } catch { }  // 先按 vt 释放内部字符串，否则会泄漏
                Marshal.FreeCoTaskMem(pv);               // 再释放缓冲区本身
            }
        }

        // ===================== 资源释放 =====================
        // 释放 COM 对象  参数：comObject 任意可能为 null 的引用  说明：先判 IsComObject，避免对非 COM 对象调用 ReleaseComObject
        private static void Release(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try { Marshal.ReleaseComObject(comObject); } catch { }  // 释放失败不打断上层流程
            }
        }

        // 注销通知回调并释放枚举器  说明：注销失败也要继续释放，因此两步各自兜住异常
        public void Dispose()
        {
            // === 幂等：重复 Dispose 直接返回 ===
            if (_disposed)
                return;
            _disposed = true;

            // === 先断开回调，再释放枚举器 ===
            try { _enumerator.UnregisterEndpointNotificationCallback(_sink); } catch { }  // 顺序不能反：枚举器释放后回调仍可能被触发
            Release(_enumerator);
        }
    }

    // ===================== 默认设备切换（PolicyConfig） =====================
    // 用未公开的 IPolicyConfig 接口改默认设备  说明：公开的 IMMDeviceEnumerator 只能读默认设备，写入没有别的入口
    internal static class PolicyConfig
    {
        // ===================== 未公开的 IPolicyConfig =====================
        // GUID 与 vtable 顺序取自未公开实现，方法必须按原样全量声明，删一个或换序都会让调用落到错误槽位
        // [PreserveSig] 让方法原样返回 HRESULT，调用方按 0 判成功，而不是被运行时抛成 COMException
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            // === 下面十个方法用不到，只用于占住 vtable 槽位 ===
            [PreserveSig] int GetMixFormat(string pszDeviceName, out IntPtr ppFormat);
            [PreserveSig] int GetDeviceFormat(string pszDeviceName, int bDefault, out IntPtr ppFormat);
            [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
            [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod(string pszDeviceName, int bDefault, out IntPtr pmftDefaultPeriod, out IntPtr pmftMinimumPeriod);
            [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
            [PreserveSig] int GetShareMode(string pszDeviceName, out IntPtr pMode);
            [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
            [PreserveSig] int GetPropertyValue(string pszDeviceName, int bFxStore, IntPtr key, out IntPtr pv);
            [PreserveSig] int SetPropertyValue(string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
            // === 真正用到的方法：第 11 个声明，往前的占位方法一个都不能少 ===
            [PreserveSig] int SetDefaultEndpoint(string wszDeviceId, int eRole);
            // 显示或隐藏端点，本工具暂未使用，保留以完整描述该接口
            [PreserveSig] int SetEndpointVisibility(string pszDeviceName, int bVisible);
        }

        // 实现 IPolicyConfig 的客户端类  说明：空类声明是 ComImport 的惯例，实例化后再转成接口使用
        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class CPolicyConfigClient
        {
        }

        // ===================== 切换实现 =====================
        // 把指定设备设为某角色的默认设备  参数：realId 端点 ID，role 要占用的角色  返回：HRESULT 为 0 视为成功
        // 说明：每次调用都新建并立即释放 client，避免 COM 对象长期留在静态字段里
        public static bool SetDefault(string realId, Role role)
        {
            if (string.IsNullOrEmpty(realId))
                return false;  // 没有端点 ID 就没有可切换的目标

            object client = null;
            try
            {
                // === 创建 IPolicyConfig 实例并调用 ===
                client = new CPolicyConfigClient();
                return ((IPolicyConfig) client).SetDefaultEndpoint(realId, (int) role) == 0;  // HRESULT 为 0 才算切换成功
            }
            // 未公开接口在系统更新后可能失效，失败一律按切换未生效处理
            catch
            {
                return false;
            }
            finally
            {
                if (client != null && Marshal.IsComObject(client))
                {
                    try { Marshal.ReleaseComObject(client); } catch { }
                }
            }
        }
    }

    #region COM 接口与常量

    // ===================== 枚举与常量 =====================
    // 数据流向  说明：本工具只处理 Render（播放），其余两项为完整性保留
    internal enum DataFlow
    {
        Render = 0,   // 播放端点
        Capture = 1,  // 录制端点
        All = 2       // 两者都枚举
    }

    // 音频角色  说明：系统把普通播放与语音通话分角色保存默认设备，所以角色必须显式指定
    internal enum Role
    {
        Console = 0,        // 控制台默认设备，普通播放走这个
        Multimedia = 1,     // 多媒体默认设备，通常与 Console 一致
        Communications = 2  // 通信默认设备，语音与会议软件使用
    }

    // 属性存储的访问模式  说明：只需要读，传 0（STGM_READ）即可
    internal static class StorageAccess
    {
        public const int Read = 0;  // STGM_READ
    }

    // PROPVARIANT 的类型标记  说明：只处理字符串，其它类型一律当作无值
    internal static class VariantType
    {
        public const ushort LPWStr = 31;  // VT_LPWSTR，以 null 结尾的 UTF-16 字符串
    }

    // ===================== 属性键 =====================
    // 属性键由 属性集 GUID + 集内序号 组成，与 PKEY 的二进制布局一一对应
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid FormatId;   // 属性集 GUID
        public int PropertyId;  // 集内序号

        // 构造属性键  参数：formatId 属性集 GUID，propertyId 集内序号
        public PropertyKey(Guid formatId, int propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    // 三个设备属性键  说明：写成属性而不是静态字段，避免属性键在类型初始化顺序里被提前求值
    internal static class PropertyKeys
    {
        // 设备属性集，PKEY_Device_FriendlyName 与 PKEY_Device_DeviceDesc 都在其中
        private static readonly Guid DeviceFormat = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        // 接口属性集，PKEY_DeviceInterface_FriendlyName 在其中
        private static readonly Guid InterfaceFormat = new Guid("026e516e-b814-414b-83cd-856d6fef4822");

        // 设备友好名  返回：属性键（设备属性集，序号 14）
        public static PropertyKey DeviceFriendlyName
        {
            get { return new PropertyKey(DeviceFormat, 14); }
        }

        // 设备描述  返回：属性键（设备属性集，序号 2）
        public static PropertyKey DeviceDescription
        {
            get { return new PropertyKey(DeviceFormat, 2); }
        }

        // 接口友好名  返回：属性键（接口属性集，序号 2）
        public static PropertyKey DeviceInterfaceFriendlyName
        {
            get { return new PropertyKey(InterfaceFormat, 2); }
        }
    }

    // ===================== COM 接口定义 =====================
    // 枚举器对象的 CLSID  说明：CoCreateInstance 之后即可转成 IMMDeviceEnumerator
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    // 设备枚举器  说明：方法顺序即 vtable 顺序，中间插入或调换会让调用落到错误槽位
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        // 按流向与状态位枚举端点集合
        [PreserveSig] int EnumAudioEndpoints(DataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        // 取指定角色的默认设备
        [PreserveSig] int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice device);
        // 按端点 ID 取设备（本工具用不到，但不能省略，否则后面的槽位全部前移）
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        // 注册设备变更回调
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        // 注销设备变更回调
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    // 端点集合  说明：只提供计数与按序号取元素，元素的生命周期由调用方负责
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        // 集合内端点数量
        [PreserveSig] int GetCount(out uint count);
        // 按序号取端点，序号从 0 开始
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    // 单个音频端点设备  说明：端点 ID、状态与属性存储都从这里取，方法顺序同样是 vtable
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        // 激活端点上的其它接口（本工具用不到，占位以保持后续槽位正确）
        [PreserveSig] int Activate(ref Guid interfaceId, int classContext, IntPtr activationParams, out IntPtr instance);
        // 打开属性存储，access 传 StorageAccess.Read
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        // 取端点 ID，字符串由 COM 任务分配器分配
        [PreserveSig] int GetId(out IntPtr id);
        // 取端点状态位（取值见 AudioEndpointState）
        [PreserveSig] int GetState(out int state);
    }

    // 只读属性存储  说明：GetValue 只负责填充调用方给的 PROPVARIANT，缓冲区与释放都由调用方管
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        // 属性总数
        [PreserveSig] int GetCount(out uint propertyCount);
        // 按序号取属性键
        [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
        // 读属性值，value 指向调用方分配的 PROPVARIANT
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        // 写属性值（本工具未用）
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr value);
        // 提交改动（本工具未用；只读打开时无需调用）
        [PreserveSig] int Commit();
    }

    // 设备变更回调契约  说明：方法顺序即 vtable，系统按固定槽位回调，缺一个就会错位
    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    internal interface IMMNotificationClient
    {
        // 端点状态变化：插入、拔出、启用、禁用
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        // 新端点接入
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        // 端点被移除
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        // 某个角色的默认设备被改到别的设备
        void OnDefaultDeviceChanged(DataFlow dataFlow, Role role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        // 端点属性变化，例如用户改了设备名
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    // ===================== 通知回调实现 =====================
    // 系统回调的接收端  说明：设备增删、状态与默认项变化都收敛成一次列表已过期通知，由界面重新枚举，避免在回调线程里做重活
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class EndpointNotificationSink : IMMNotificationClient
    {
        private readonly AudioEndpointController _owner;  // 反向引用宿主，仅用于转发通知

        // 构造回调接收器  参数：owner 为持有本对象的 controller
        public EndpointNotificationSink(AudioEndpointController owner)
        {
            _owner = owner;
        }

        // 端点状态变化
        public void OnDeviceStateChanged(string deviceId, uint newState) { _owner.NotifyDevicesChanged(); }
        // 新设备接入
        public void OnDeviceAdded(string deviceId) { _owner.NotifyDevicesChanged(); }
        // 设备移除
        public void OnDeviceRemoved(string deviceId) { _owner.NotifyDevicesChanged(); }
        // 默认设备变化
        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role role, string defaultDeviceId) { _owner.NotifyDevicesChanged(); }

        // 改个名字不值得重建整个列表，这里故意不通知
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    #endregion
}
