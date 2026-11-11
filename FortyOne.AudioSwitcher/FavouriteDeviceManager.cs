// ============================================================
// FavouriteDeviceManager.cs - 收藏设备清单：内存列表与变更通知
// 功能：维护被用户点星收藏的播放设备 GUID 列表，增删后通知订阅者写回设置文件
// 设计理念：只管内存与事件，磁盘持久化交给订阅者，本类不认识设置文件
// 编译：build.cmd（详见 编译说明.md）
// ============================================================

using System;
using System.Collections.Generic;

namespace FortyOne.AudioSwitcher
{
    public static class FavouriteDeviceManager
    {
        // ===================== 类型与状态 =====================

        // 收藏列表变化时的回调：把最新的完整列表原样传出去
        public delegate void FavouriteDeviceIDsChangedEventHandler(List<Guid> ids);

        // 当前收藏的设备 GUID 集合
        private static List<Guid> FavouriteDeviceIDs = new List<Guid>();

        // 正在批量加载：此期间不对外通知，装载完成后统一派发一次
        private static bool _loading;

        // ===================== 查询 =====================

        // 收藏总数  返回：当前收藏的设备个数
        public static int FavouriteDeviceCount
        {
            get { return FavouriteDeviceIDs.Count; }
        }

        // 收藏列表变化事件：只在增删或加载完成后触发，加载过程中被抑制
        public static event FavouriteDeviceIDsChangedEventHandler FavouriteDevicesChanged;

        // 从设置里恢复收藏列表  参数：GUID 数组，可能含 Guid.Empty 或重复项  返回：恒为 true
        public static bool LoadFavouriteDevices(Guid[] favouriteIDs)
        {
            _loading = true;                                     // 进入静默期：装载过程中触发的变更不对外通知
            try
            {
                FavouriteDeviceIDs = new List<Guid>();

                foreach (var s in favouriteIDs)
                {
                    // 设置串里解析失败的项会退化成 Guid.Empty，直接丢弃
                    if (s == Guid.Empty)
                        continue;
                    if (!FavouriteDeviceIDs.Contains(s))         // 去重：同一设备收藏两次没有意义
                        FavouriteDeviceIDs.Add(s);
                }
            }
            finally
            {
                _loading = false;
            }

            FireFavouriteDeviceChanged();                        // 整批装完只通知一次
            return true;
        }

        // 判断某设备是否已收藏  参数：设备 GUID  返回：true 表示在收藏列表中
        public static bool IsFavouriteDevice(Guid id)
        {
            return FavouriteDeviceIDs.Contains(id);
        }

        // 新增收藏  参数：设备 GUID  返回：加入成功返回该 GUID，已存在则返回 Guid.Empty
        public static Guid AddFavouriteDevice(Guid id)
        {
            if (FavouriteDeviceIDs.Contains(id))
                return Guid.Empty;

            FavouriteDeviceIDs.Add(id);

            FireFavouriteDeviceChanged();

            return id;
        }

        // 取消收藏  参数：设备 GUID  返回：传入的 GUID（不判断它原本是否在列表里）
        public static Guid RemoveFavouriteDevice(Guid id)
        {
            FavouriteDeviceIDs.Remove(id);

            FireFavouriteDeviceChanged();

            return id;
        }

        // 派发收藏列表变化事件  说明：加载期间直接返回，保证一次加载只发一次通知
        private static void FireFavouriteDeviceChanged()
        {
            if (_loading)
                return;
            if (FavouriteDevicesChanged != null)
                FavouriteDevicesChanged(FavouriteDeviceIDs);
        }
    }
}
