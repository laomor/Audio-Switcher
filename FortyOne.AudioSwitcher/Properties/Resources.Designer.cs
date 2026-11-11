// ============================================================
// Resources.Designer.cs - 强类型资源访问类（ResXFileCodeGenerator 生成）
// 功能：把 Resources.resx 里的字符串包装成静态属性，拼错键名在编译期就能发现
// 说明：由 ResXFileCodeGenerator 自动生成，改 resx 后本文件会被整体重写，不要手改
// 注意：ConfigFile 与 OldConfigFile 是配置文件名在程序里的唯一来源，改名只需改 resx
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

namespace FortyOne.AudioSwitcher.Properties {

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {

        // ===================== 静态缓存字段 =====================
        private static global::System.Resources.ResourceManager resourceMan;     // 缓存的 ResourceManager：清单只解析一次
        private static global::System.Globalization.CultureInfo resourceCulture; // 调用方指定的区域性；null 表示跟随当前 UI 语言

        // ===================== 构造与资源访问 =====================
        // 空构造：本类只对外提供静态成员，实例化没有意义  参数：无  返回：无
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }

        // 首次访问时才创建 ResourceManager 并缓存  参数：无  返回：进程内共用的唯一实例
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("FortyOne.AudioSwitcher.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }

        // 允许运行期临时切换资源区域性  参数：无（get）/ 目标区域性（set）  返回：当前区域性
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }

        // ===================== 资源字符串 =====================
        // 当前配置文件名  参数：无  返回：Resources.resx 中 ConfigFile 键的字符串
        internal static string ConfigFile {
            get {
                return ResourceManager.GetString("ConfigFile", resourceCulture);
            }
        }

        // 迁移用的旧配置文件名  参数：无  返回：Resources.resx 中 OldConfigFile 键的字符串
        internal static string OldConfigFile {
            get {
                return ResourceManager.GetString("OldConfigFile", resourceCulture);
            }
        }
    }
}
