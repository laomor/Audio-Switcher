// ============================================================
// AssemblyInfo.cs - 程序集元数据：exe 标题、公司、版本与 COM 可见性
// 功能：写入资源管理器属性页里可见的名称与版权，并定死程序集版本 1.8.0.142
// 说明：这些特性直接编进 exe 的资源区，改标题或版本会立刻反映到文件属性与关于信息里
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// ===================== 程序集标识 =====================
[assembly: AssemblyTitle("Audio Switcher")]              // 资源管理器「文件说明」列，也是程序集的人读名称
[assembly: AssemblyDescription("Audio Switcher - GUI")]  // 程序集描述，主要在开发者工具里显示
[assembly: AssemblyConfiguration("")]                    // 留空表示不区分 Debug/Release，正式版与调试版共用同一套元数据
[assembly: AssemblyCompany("Forty One Ltd.")]            // 「公司」列，安装包与默认安装目录也会引用
[assembly: AssemblyProduct("Audio Switcher")]            // 「产品名称」列
[assembly: AssemblyCopyright("Copyright ©  2018")]       // 「版权」列，年份停留在 2018 属历史信息，不随版本号变动
[assembly: AssemblyTrademark("")]                        // 无商标，留空
[assembly: AssemblyCulture("")]                          // 空串 = 中性区域性，不生成附属程序集

// ===================== COM 可见性与 GUID =====================
[assembly: ComVisible(false)]  // 不向 COM 暴露任何类型：纯 .NET 工具，无需 regasm 注册

// GUID 是程序集的类型库标识；本程序不注册 COM，但值要保持不变，否则会被当成另一个程序集
[assembly: Guid("ce68968d-63e3-4a2d-914d-f231877f92fd")]

// ===================== 版本号 =====================
[assembly: AssemblyVersion("1.8.0.142")]               // 编译期绑定的程序集版本，引用方按它定位；主版本变动会破坏绑定
[assembly: AssemblyFileVersion("1.8.0.142")]           // 「文件版本」列，升级与回滚按它比较新旧
[assembly: AssemblyInformationalVersion("1.8.0.142")]  // 「产品版本」列与关于信息展示的版本

// 空串把中性资源文化设为 invariant；本程序不提供本地化附属程序集，其它文化仍走正常回退
[assembly: NeutralResourcesLanguage("")]