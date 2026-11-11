// ============================================================
// AudioDeviceManager.cs - 音频设备控制器的懒加载单例
// 功能：为整个进程提供唯一的 AudioEndpointController 实例
// 设计理念：控制器内部握着 COM 接口与事件订阅，多实例会重复枚举设备并互相打断切换，故全进程只留一个
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using FortyOne.AudioSwitcher.Audio;

namespace FortyOne.AudioSwitcher
{
    public static class AudioDeviceManager
    {
        // ===================== 单例状态 =====================
        // 不加锁也不标 volatile：只在 UI 线程访问；创建后缓存、Shutdown() 时置空，不存在并发改写
        private static AudioEndpointController _instance;

        // 进程内唯一的设备控制器  参数：无  返回：首次访问时创建并缓存；Shutdown() 之后再访问会重新创建
        // ?? 的右侧只在 _instance 为 null 时求值，正常生命周期内只创建一次
        public static AudioEndpointController Controller
        {
            get
            {
                return _instance ?? (_instance = new AudioEndpointController());
            }
        }

        // ===================== 退出清理 =====================
        // 释放控制器：注销设备变更回调并释放 COM 枚举器  参数：无  返回：无
        // 进程退出时调用；从未创建过控制器就直接返回，不会为此新建一个
        public static void Shutdown()
        {
            if (_instance == null)
                return;

            _instance.Dispose();
            _instance = null;
        }
    }
}
