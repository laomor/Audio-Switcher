// ============================================================
// AudioSwitcher.Designer.cs - 托盘窗体的界面定义（设计器生成）
// 功能：创建一个 NotifyIcon，设定托盘文字与可见性，并挂上单击、双击、移动三个鼠标事件
// 说明：本文件由 Visual Studio 窗体设计器生成，手改会在下次保存界面时被覆盖，因此只做注释补充
// 注意：窗体自身是 1×1、无边框、不占任务栏的隐形窗口，用户能看到的一切都在托盘区
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

namespace FortyOne.AudioSwitcher
{
    partial class AudioSwitcher
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

        // 创建托盘图标、写托盘文字并挂接三个鼠标事件  参数：无  返回：无
        private void InitializeComponent()
        {
			// === 组件容器与托盘图标 ===
			this.components = new System.ComponentModel.Container();
			this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
			this.SuspendLayout();
			// === 托盘图标文字与可见性 ===
			this.notifyIcon1.Text = "Audio Switcher";  // 鼠标悬停时显示的提示文字
			this.notifyIcon1.Visible = true;           // 必须为 true，托盘图标才会出现在通知区域
			// === 鼠标事件挂接：单击弹卡片、双击切设备、移动刷新悬停提示 ===
			this.notifyIcon1.MouseClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseClick);
			this.notifyIcon1.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseDoubleClick);
			this.notifyIcon1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseMove);
			// === 自动缩放与客户区尺寸：窗体从不显示，1×1 足够 ===
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(1, 1);
			// === 窗口样式：无边框、不可缩放、不占任务栏 ===
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.Margin = new System.Windows.Forms.Padding(2, 3, 2, 3);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "AudioSwitcher";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
			this.Text = "Audio Switcher";
			this.ResumeLayout(false);

        }

        #endregion

        // ===================== 控件字段 =====================
        private System.Windows.Forms.NotifyIcon notifyIcon1;  // 本窗体唯一的界面元素，随进程存活到退出
	}
}
