// ============================================================
// ExceptionDisplayForm.cs - 异常详情窗口：把崩溃现场原样摊给用户
// 功能：构造时收下异常对象，Load 时把它的 ToString() 全文灌进只读多行文本框，供用户查看与复制
// 说明：全程序只有它以带标题栏的对话框形式出现，其余窗口都是无边框弹出层，只做最小排版
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Windows.Forms;

namespace FortyOne.AudioSwitcher
{
    public sealed partial class ExceptionDisplayForm : Form
    {
        // 只存引用、不提前拼字符串：ToString() 可能很长，展开推迟到 Load，构造函数保持轻量
        private readonly Exception exception;

        // ===================== 构造与初始化 =====================
        // 保存异常引用并套用外部指定的标题  参数：title 标题栏文字  ex 待展示的异常（可为 null）  返回：无
        public ExceptionDisplayForm(string title, Exception ex)
        {
            InitializeComponent();  // 先让设计器把控件造出来并布好，后面设置标题才有落脚处

            exception = ex;
            // 标题必须在 InitializeComponent 之后设置，否则会被设计器写死的“发生错误”顶掉
            Text = title;
        }

        // ===================== 事件响应 =====================
        // 关闭窗口，把后续处理交回弹窗前的 WinFormExceptionHandler  参数：sender 关闭按钮  e 事件参数（未使用）  返回：无
        private void button1_Click(object sender, EventArgs e)
        {
            Close();  // 只关自己，不终止进程：进程去留由调用方决定
        }

        // 把异常展开成文本显示  参数：sender 本窗体  e 事件参数（未使用）  返回：无
        private void ExceptionDisplayForm_Load(object sender, EventArgs e)
        {
            // 用 ToString() 而不是 Message：类型名、消息、完整堆栈一次给全，用户可直接复制回传
            if (exception != null)
                txtError.Text = exception.ToString();  // 为 null 时保持空白，不制造第二层错误
        }
    }
}