using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DanpheEMR.ViewModel.Pharmacy
{

    public class PHRMLeafSettingVM
    {
        public IList<PHRMLeafSetting> LeftSetting { get; set; } 
    }
    public class PHRMLeafSetting 
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
