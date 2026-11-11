// ============================================================
// ExceptionDisplayForm.Designer.cs - 异常详情窗口的界面定义（设计器生成）
// 功能：声明四个控件与全部布局数值，供 ExceptionDisplayForm 构造函数调用 InitializeComponent
// 说明：本文件由 Visual Studio 窗体设计器生成，手改会在下次保存界面时被覆盖，因此只做注释补充
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

namespace FortyOne.AudioSwitcher
{
    partial class ExceptionDisplayForm
    {
        // ===================== 组件容器与释放 =====================
        private System.ComponentModel.IContainer components = null;

        // 先释放设计器托管的组件，再交还基类清理窗口句柄  参数：disposing true 表示由 Dispose() 主动调用  返回：无
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        // 构造四个控件、写入设计器保存的布局数值并挂接 Load  参数：无  返回：无
        private void InitializeComponent()
        {
            // === 控件构造 ===
            this.label1 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.txtError = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // === 顶部错误提示：红色、居中、两行文字 ===
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.Color.Red;
            this.label1.Location = new System.Drawing.Point(33, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(235, 34);
            this.label1.TabIndex = 0;
            this.label1.Text = "发生错误，影响了\r\nAudio Switcher 的正常运行";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // === 关闭按钮：主要关闭入口，标题栏的关闭按钮同样可用 ===
            this.button1.Location = new System.Drawing.Point(216, 262);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 3;
            this.button1.Text = "关闭";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // === 错误详情文本框：只读多行 + 纵向滚动条，堆栈再长也能翻 ===
            this.txtError.Location = new System.Drawing.Point(12, 72);
            this.txtError.Multiline = true;
            this.txtError.Name = "txtError";
            this.txtError.ReadOnly = true;
            this.txtError.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtError.Size = new System.Drawing.Size(279, 180);
            this.txtError.TabIndex = 5;
            // === “错误：”标签：位于文本框左上方，说明下面这段文字是什么 ===
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(9, 56);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(32, 13);
            this.label3.TabIndex = 6;
            this.label3.Text = "错误：";
            // === 自动缩放与客户区尺寸 ===
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(303, 295);
            // === 控件加入窗体（加入顺序即 Z 序） ===
            this.Controls.Add(this.label3);
            this.Controls.Add(this.txtError);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label1);
            // === 窗口样式：固定工具窗口、不可最大化/最小化、屏幕居中 ===
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ExceptionDisplayForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "发生错误";
            // === 事件挂接：异常文本要等 Load 时才知道 ===
            this.Load += new System.EventHandler(this.ExceptionDisplayForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        // ===================== 控件字段 =====================
        private System.Windows.Forms.Label label1;      // 顶部红色提示文字，AutoSize 由文本撑开
        private System.Windows.Forms.Button button1;    // 关闭按钮
        private System.Windows.Forms.TextBox txtError;  // 只读多行文本框，承载异常全文
        private System.Windows.Forms.Label label3;      // “错误：”说明标签
    }
}