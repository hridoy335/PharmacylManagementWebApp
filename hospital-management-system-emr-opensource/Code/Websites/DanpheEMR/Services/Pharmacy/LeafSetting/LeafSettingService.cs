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
            var leftSetting = (from SL in db.LeadSetting
                               select new PHRMLeafSetting
                               {
                                   LeafSettingId = SL.LeafSettingId,
                                   LeafType = SL.LeafType,
                                   TotalNumber = SL.TotalNumber,
                                   IsActive = SL.IsActive
                               }).Where(l => l.IsActive == true && l.LeafType == model.LeafType).FirstOrDefault();
            if (leftSetting == null)
            {
                db.LeadSetting.Add(model);
                db.SaveChanges();
                var result = new PHRM_LeafSetting()
                {
                    LeafSettingId = (from SL in db.LeadSetting
                                     where SL.LeafType == model.LeafType
                                     select SL.LeafSettingId).FirstOrDefault(),
                    TotalNumber = model.TotalNumber,
                    LeafType = model.LeafType,
                    CreatedBy = model.CreatedBy,
                    CreatedOn = model.CreatedOn,
                };
                return result;
            }
            else
            {
                var result = new PHRM_LeafSetting()
                {
                    LeafSettingId = leftSetting.LeafSettingId,
                    TotalNumber = leftSetting.TotalNumber,
                    LeafType = leftSetting.LeafType,
                    CreatedBy = model.CreatedBy,
                    CreatedOn = model.CreatedOn,
                };
                return result;
            }
        }

        public PHRM_LeafSetting UpdateLeafSettingInfo(PHRM_LeafSetting model)
        {
            var leftSetting = (from SL in db.LeadSetting
                               select new PHRMLeafSetting
                               {
                                   LeafSettingId = SL.LeafSettingId,
                                   LeafType = SL.LeafType,
                                   TotalNumber = SL.TotalNumber,
                                   IsActive = SL.IsActive
                               }).Where(l => l.IsActive == true && l.LeafType == model.LeafType).FirstOrDefault();
            if (leftSetting == null || leftSetting.LeafSettingId==model.LeafSettingId)
            {
                var result = new PHRM_LeafSetting()
                {
                    LeafSettingId = model.LeafSettingId,
                    LeafType = model.LeafType,
                    TotalNumber = model.TotalNumber,
                    IsActive = model.IsActive,
                };
                db.Entry(result).State = System.Data.Entity.EntityState.Modified;
                db.Entry(result).Property(rack => rack.CreatedBy).IsModified = false;
                db.Entry(result).Property(rack => rack.CreatedOn).IsModified = false;
                db.SaveChanges();

                return result;
            }
            else
            {
                var result = new PHRM_LeafSetting()
                {
                    LeafSettingId = leftSetting.LeafSettingId,
                    TotalNumber = leftSetting.TotalNumber,
                    LeafType = leftSetting.LeafType,
                    CreatedBy = model.CreatedBy,
                    CreatedOn = model.CreatedOn,
                };
                return result;
            }
        }
        #endregion





    }
}
