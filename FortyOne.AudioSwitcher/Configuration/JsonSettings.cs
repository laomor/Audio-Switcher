// ============================================================
// JsonSettings.cs - 基于 JSON 文件的设置源
// 功能：实现 ISettingsSource，把扁平 JSON 设置读写到调用方指定路径的文件里
// 设计理念：读写共用一把锁，落盘先用临时文件再整体替换，宁可写慢一点也不留半截配置
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System.Collections.Generic;
using System.IO;

namespace FortyOne.AudioSwitcher.Configuration
{
    public class JsonSettings : ISettingsSource
    {
        // ===================== 状态 =====================
        private readonly object _mutex = new object();        // 唯一的锁，读、写、落盘全部串行
        private string _path;                                 // 设置文件的完整路径
        private IDictionary<string, string> _settingsObject;  // 内存里的键值表，Get/Contains 直接查它

        // 上一次 Load 是否因文件读不出来而退化成空配置；调用方据此判断旧文件还能不能拿来迁移
        public bool LoadFailed { get; private set; }

        // 构造时先给一张空表，保证 _settingsObject 任何时刻都非空，省掉调用方的判空分支  参数：无  返回：无
        public JsonSettings()
        {
            _settingsObject = new Dictionary<string, string>();
        }

        // ===================== 生命周期 =====================
        // 记录配置文件路径  参数：完整路径  返回：无
        // 这里只存不读，真正的读盘留给 Load，方便调用方先设好路径再统一加载
        public void SetFilePath(string path)
        {
            _path = path;
        }

        // 从磁盘载入设置  参数：无  返回：无
        // 任何异常都退化成空配置：配置文件损坏时程序要能照常启动，而不是卡在启动路径上
        public void Load()
        {
            lock (_mutex)
            {
                LoadFailed = false;
                try
                {
                    if (File.Exists(_path))
                    {
                        var loaded = SimpleJson.ParseFlatObject(File.ReadAllText(_path));
                        // 解析结果为 null（空文件或字面量 null）时保留内存里已有的设置
                        if (loaded != null)
                            _settingsObject = loaded;
                    }
                }
                catch
                {
                    LoadFailed = true;
                    _settingsObject = new Dictionary<string, string>();
                }
            }
        }

        // 把内存设置写回磁盘  参数：无  返回：无
        // 全程吞掉异常：保存设置属于尽力而为，失败也只是丢这一次修改，不该让界面弹错误
        public void Save()
        {
            try
            {
                lock (_mutex)
                {
                    var json = SimpleJson.Serialize(_settingsObject, true);

                    // === 先写临时文件 ===
                    // 临时文件放在同一目录，保证后面的 File.Replace 不跨卷，否则它会直接失败
                    var tmpPath = _path + ".tmp";
                    File.WriteAllText(tmpPath, json);
                    try
                    {
                        // === 原子替换 ===
                        // File.Replace 是单次文件系统操作，进程中途被杀也只会留下旧文件或新文件
                        if (File.Exists(_path))
                            File.Replace(tmpPath, _path, null);
                        else
                            File.Move(tmpPath, _path);
                    }
                    catch
                    {
                        // === 回退：直接覆盖 ===
                        // 目标被占用或权限不足时 Replace 会失败，此时「设置写得进去」比「写入原子」更要紧；
                        // 直接写有可能留下半截文件，但 Load 的容错会把这种文件当空配置处理
                        File.WriteAllText(_path, json);
                        try { File.Delete(tmpPath); }
                        catch { }
                    }
                }
            }
            catch
            {
            }
        }

        // ===================== 键值读写 =====================
        // 取一个设置值  参数：键名  返回：值；缺失或显式 null 时返回空字符串
        // 不返回 null 是有意为之，调用方可以放心直接比较，不必层层判空
        public string Get(string key)
        {
            lock (_mutex)
            {
                string value;
                if (_settingsObject != null && _settingsObject.TryGetValue(key, out value) && value != null)
                    return value;
                return string.Empty;
            }
        }

        // 判断某个键是否写过  参数：键名  返回：存在为 true
        // 与 Get 返回空串区分开：补默认值时要靠它判断「用户从没配过」
        public bool Contains(string key)
        {
            lock (_mutex)
            {
                return _settingsObject != null && _settingsObject.ContainsKey(key);
            }
        }

        // 写一个设置并立即落盘  参数：键名、值  返回：无
        // 每次 Set 都同步写文件而不攒批：设置项只有个位数，换来断电也不丢上一次修改
        public void Set(string key, string value)
        {
            lock (_mutex)
            {
                _settingsObject[key] = value;
                // 这里会再次进入 Save 的 lock；lock 基于 Monitor，同线程可重入，不会自锁
                Save();
            }
        }
    }
}
