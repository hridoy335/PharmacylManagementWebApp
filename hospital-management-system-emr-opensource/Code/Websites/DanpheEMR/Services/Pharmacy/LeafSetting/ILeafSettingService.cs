using DanpheEMR.Security;
using DanpheEMR.ServerModel.PharmacyModels;
using DanpheEMR.ViewModel.Pharmacy;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DanpheEMR.Services.Pharmacy.LeafSetting
{
    public interface ILeafSettingService 
    {
        List<PHRMLeafSetting> GetAllLeafSetting();
        PHRMLeafSetting GetLeafSettingById(int leafSettingId);
        PHRM_LeafSetting InsertLeafSettingInfo(PHRM_LeafSetting model); 
    }
}
