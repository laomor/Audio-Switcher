@echo off
rem ============================================================
rem build.cmd - AudioSwitcher 一键编译入口
rem 功能：自动挑选本机可用的 C# 编译器，把源码编译成 out\AudioSwitcher.exe
rem 设计理念：不依赖 Visual Studio 与目标框架包，整个文件夹拷走就能编译
rem 用法：在仓库根目录执行 build.cmd；成功输出 [build] OK，失败返回退出码 1
rem ============================================================


rem ===================== 为什么需要这层包装 =====================
rem build.rsp 本身是自洽的，所有路径都相对本目录，但有一个开关放不进去：
rem /noconfig。.NET Framework 的 csc.exe 启动时会自动加载同目录下的
rem csc.rsp，里面已经引用了 System、System.Core、System.Drawing 等程序集；
rem 而 build.rsp 用 /nostdlib+ 改引 refs 下的副本，两者相遇就会报
rem CS1703，也就是同一个程序集被导入两次。
rem /noconfig 必须出现在命令行上，写在响应文件里为时已晚，
rem 因为默认响应文件在读到那一行之前就已经被加载了。
rem
rem .NET SDK 自带的 Roslyn 根本不读 csc.rsp，所以它不需要 /noconfig，
rem 作为第二顺位候选编译器放在下面。

setlocal
cd /d "%~dp0"

rem 输出目录：不存在就先创建，避免编译器写文件时无目录可用
if not exist "out" mkdir "out"


rem ===================== 候选一：.NET Framework 自带的 csc.exe =====================
rem Windows 10/11 默认就有这套编译器，优先用它，启动最快
set "FWCSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%FWCSC%" (
    echo [build] using .NET Framework csc.exe
    "%FWCSC%" /noconfig "@build.rsp"
    goto :check
)


rem ===================== 候选二：.NET SDK 的 Roslyn =====================
rem 在 Program Files 下的 dotnet\sdk 里按目录名倒序排列，
rem 取第一个真正含有 csc.dll 的版本，也就是版本号最大的那套 SDK
set "DOTNETEXE=%ProgramFiles%\dotnet\dotnet.exe"
if exist "%DOTNETEXE%" (
    for /f "delims=" %%D in ('dir /b /ad /o-n "%ProgramFiles%\dotnet\sdk" 2^>nul') do (
        if exist "%ProgramFiles%\dotnet\sdk\%%D\Roslyn\bincore\csc.dll" (
            echo [build] using .NET SDK %%D Roslyn
            "%DOTNETEXE%" "%ProgramFiles%\dotnet\sdk\%%D\Roslyn\bincore\csc.dll" "@build.rsp"
            goto :check
        )
    )
)

rem 两种编译器都没找到：多半是这台机器既没装 .NET Framework 4.x 也没装 .NET SDK
echo [build] no C# compiler found - install .NET Framework 4.x or the .NET SDK
exit /b 1


:check
rem 编译器返回非零即视为失败；产物路径固定，不跟随编译器变化
if errorlevel 1 (
    echo [build] FAILED
    exit /b 1
)

echo [build] OK -^> out\AudioSwitcher.exe
exit /b 0
