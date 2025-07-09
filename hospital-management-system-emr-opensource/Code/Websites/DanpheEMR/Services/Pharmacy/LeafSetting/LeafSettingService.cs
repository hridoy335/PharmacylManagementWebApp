using DanpheEMR.Core.Configuration;
using DanpheEMR.DalLayer;
using DanpheEMR.Security;
using DanpheEMR.ServerModel.PharmacyModels;
using DanpheEMR.ViewModel.Pharmacy;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace DanpheEMR.Services.Pharmacy.LeafSetting
{
    public class LeafSettingService: ILeafSettingService
    {

        #region DECLARATIONS
        private PharmacyDbContext db;
        private readonly string connString = null;
        #endregion

        #region CTOR
        public LeafSettingService(IOptions<MyConfiguration> _config)
        {
            connString = _config.Value.Connectionstring;
            db = new PharmacyDbContext(connString);
        } 
        #endregion


        #region CRUD LeafSetting
        
        public List<PHRMLeafSetting> GetAllLeafSetting()
        {
            var leftSetting = (from SL in db.LeadSetting
                                     select new PHRMLeafSetting
                                     {
                                         LeafSettingId = SL.LeafSettingId,
                                         LeafType = SL.LeafType,
                                         TotalNumber = SL.TotalNumber,
                                         IsActive = SL.IsActive
                                     }).Where(l => l.IsActive == true).ToList();
          
            return leftSetting;
        }

        public PHRMLeafSetting GetLeafSettingById(int leafSettingId)
        {
            var leftSetting = (from SL in db.LeadSetting
                                select new PHRMLeafSetting
                                {
                                    LeafSettingId = SL.LeafSettingId,
                                    LeafType = SL.LeafType,
                                    TotalNumber = SL.TotalNumber,
                                    IsActive = SL.IsActive
                                }).Where(l => l.IsActive == true && l.LeafSettingId==leafSettingId).FirstOrDefault();

            return leftSetting;
        }

        public PHRM_LeafSetting InsertLeafSettingInfo(PHRM_LeafSetting model)
        {
            db.LeadSetting.Add(model);
            db.SaveChanges();
            var result = new PHRM_LeafSetting()
            {
                LeafSettingId = model.LeafSettingId,
                //RackNo = model.RackNo,
                //ParentRackNo = (from rack in db.PHRMRack
                //                where rack.RackId == model.ParentId
                //                select rack.RackNo).FirstOrDefault(),
                //ParentId = model.ParentId,
                //StoreId = model.StoreId,
                //Description = model.Description,
                CreatedBy = model.CreatedBy,
                CreatedOn = model.CreatedOn,
            };
            return result;
        }
        #endregion





    }
}
