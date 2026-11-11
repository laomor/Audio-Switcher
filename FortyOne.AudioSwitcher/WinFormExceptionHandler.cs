// ============================================================
// WinFormExceptionHandler.cs - 全局异常兜底：把未处理异常变成可读窗口
// 功能：接管 WinForms 消息循环异常与 CLR 未处理异常，统一弹异常详情窗口
// 设计理念：托盘程序没有控制台，异常必须可视；被取消的任务不算错误，静默忽略
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace FortyOne.AudioSwitcher
{
    public class WinFormExceptionHandler
    {
        // 界面线程抛出的异常  参数：事件源，线程异常参数  返回：无
        public static void OnThreadException(object sender, ThreadExceptionEventArgs t)
        {
            HandleException(t.Exception);
        }

        // 后台线程逃逸到 CLR 的异常  参数：事件源，未处理异常参数  返回：无
        public static void OnUnhandledCLRException(object sender, UnhandledExceptionEventArgs t)
        {
            HandleException((Exception) t.ExceptionObject);
        }

        // 统一处理入口  参数：待展示的异常  返回：无
        // 说明：TaskCanceledException 属于正常取消流程，不必打扰用户，其余一律弹出详情窗口
        public static void HandleException(Exception ex)
        {
            if (ex is TaskCanceledException)
                return;

            var edf = new ExceptionDisplayForm("发生未处理错误", ex);
            edf.ShowDialog();
        }
    }
}
