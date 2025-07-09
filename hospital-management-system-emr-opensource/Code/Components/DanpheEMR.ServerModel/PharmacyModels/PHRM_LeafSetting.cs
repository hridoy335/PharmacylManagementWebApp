using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanpheEMR.ServerModel.PharmacyModels
{
    public class PHRM_LeafSetting
    {
        [Key]
        public int LeafSettingId { get; set; }
        public string LeafType { get; set; }
        public int TotalNumber { get; set; }
        public bool IsActive { get; set; }
        public string CreatedOn { get; set; }
        public string CreatedBy { get; set; }
    }
}
