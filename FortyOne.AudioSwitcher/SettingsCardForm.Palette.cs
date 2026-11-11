// ============================================================
// SettingsCardForm.Palette.cs - 卡片调色板：所有颜色常量
// 功能：集中定义卡片本体、设备行、星标、滚动条、热键框与退出卡的每一处颜色，绘制代码只引用名字、不写字面量
// 说明：本文件是 SettingsCardForm 的分块（partial），配合其它分块阅读
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System.Drawing;

namespace FortyOne.AudioSwitcher
{
    public partial class SettingsCardForm
    {
        #region 配色

        // === 强调色 ===
        private static readonly Color AccentBlue = Color.FromArgb(0, 102, 255);  // 强调蓝：开关打开、设置区蓝底、默认设备高亮

        // === 卡片本体 ===
        private static readonly Color CardBackTop = Color.FromArgb(252, 253, 255);              // 卡片渐变顶色（近白）
        private static readonly Color CardBackBottom = Color.FromArgb(228, 232, 240);           // 卡片渐变底色（兼窗体兜底）
        private static readonly Color CardBorder = Color.FromArgb(176, 186, 203);               // 卡片外框 1px 描边，比底色深一档
        private static readonly Color CardInnerHighlight = Color.FromArgb(120, 255, 255, 255);  // alpha 120 白色内高光

        // === 设备行与星标 ===
        private static readonly Color BlockBorder = Color.FromArgb(122, 134, 152);    // 设备行左右两块内容块的描边
        private static readonly Color RowHoverBack = Color.FromArgb(246, 250, 255);   // 设备行悬停底色（极浅蓝）
        private static readonly Color StarGold = Color.FromArgb(247, 181, 0);         // 已收藏星的填充金
        private static readonly Color StarOutline = Color.FromArgb(51, 51, 51);       // 星形描边深灰，未填充时勾轮廓
        private static readonly Color StarHoverBack = Color.FromArgb(255, 251, 240);  // 星块悬停时的暖白底
        private static readonly Color SwitchTint = Color.FromArgb(226, 238, 255);     // 默认项接口名的浅蓝文字色，掉落时由中灰渐变而来

        // === 文字灰阶 ===
        private static readonly Color DarkGrey = Color.FromArgb(53, 53, 53);       // 主文字：设备名、退出按钮
        private static readonly Color MiddleGrey = Color.FromArgb(118, 118, 118);  // 次要文字：接口名

        // === 退出卡 ===
        private static readonly Color ExitPressBack = Color.FromArgb(226, 236, 250);  // 退出卡按下时的底色
        private static readonly Color ExitBorder = Color.FromArgb(104, 116, 134);     // 退出卡描边

        // === 滚动条 ===
        private static readonly Color ScrollTrack = Color.FromArgb(238, 238, 238);  // 滚动条轨道
        private static readonly Color ScrollThumb = Color.FromArgb(170, 170, 170);  // 滚动条滑块

        // === 热键框 ===
        private static readonly Color HotKeyGlow = Color.FromArgb(232, 244, 255);      // 热键框焦点光晕底（浅蓝）
        private static readonly Color HotKeyErrorInk = Color.FromArgb(255, 226, 138);  // 仅用于错误态文字（暖黄），描边保持白色

        // === 覆盖层 ===
        private static readonly Color OverlayGlass = Color.FromArgb(252, 251, 249);    // 覆盖层玻璃底（近白米色）
        private static readonly Color OverlayBorder = Color.FromArgb(208, 205, 201);   // 覆盖层描边
        private static readonly Color OverlayTextMain = Color.FromArgb(43, 43, 43);    // 覆盖层主文字
        private static readonly Color OverlayTextSub = Color.FromArgb(122, 122, 122);  // 覆盖层次要文字

        // === 纯白基色 ===
        private static readonly Color CardWhite = Color.White;      // 卡片内容块的纯白底
        private static readonly Color OnAccentWhite = Color.White;  // 叠在强调色上的白，单独命名便于换主题

        #endregion
    }
}
