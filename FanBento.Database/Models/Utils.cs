using System.Collections.Generic;

namespace FanBento.Database.Models
{
    public static class Utils
    {
        public static void ReOrder<T>(this List<T> list) where T : IOrder
        {
            list.Sort((order0, order1) => order0.Order - order1.Order);
        }

        public static void AddOrder<T>(this List<T> list) where T : class, IOrder
        {
            for (var i = 0; i < list.Count; i++) list[i].Order = i;
        }
    }
}