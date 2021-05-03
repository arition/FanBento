using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FanBento.Database.Models
{
    public interface IOrder
    {
        [JsonIgnore] public int Order { get; set; }
    }
}
