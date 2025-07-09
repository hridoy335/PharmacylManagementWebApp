using DanpheEMR.Security;
using DanpheEMR.ViewModel.Pharmacy;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using DanpheEMR.CommonTypes;
using DanpheEMR.Core.Configuration;
using DanpheEMR.DalLayer;
using DanpheEMR.Services.Pharmacy.Rack;
using Microsoft.Extensions.Options;
using DanpheEMR.Services.Pharmacy.LeafSetting;
using DanpheEMR.ServerModel.PharmacyModels;
using DanpheEMR.Utilities;

namespace DanpheEMR.Controllers.Pharmacy
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class PHRMLeafSettingController : CommonController
    {
        public ILeafSettingService leafSettingService;
        private readonly PharmacyDbContext pharmacyDbContext;

        public PHRMLeafSettingController(IOptions<MyConfiguration> _config, ILeafSettingService _leafSettingService) : base(_config)
        {
            leafSettingService = _leafSettingService;
            pharmacyDbContext = new PharmacyDbContext(connString);
        }
        public DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();

        #region


        //[HttpGet]
        //public async Task<IActionResult> GetLeafSettingById(int leafSettingId)
        //{
        //    try
        //    {
        //        //responseData.Results = await _supplierLedgerService.GetSupplierLedgerGRDetails(supplierId);
        //        responseData.Status = "OK";
        //    }
        //    catch (Exception ex)
        //    {
        //        responseData.Status = "Failed";
        //        responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
        //    }
        //    return Ok(responseData);
        //}


        // GET: api/GetAllLeafSetting
        [HttpGet("GetAllLeafSetting")]
        public IActionResult GetAll()
        {
            var d= leafSettingService.GetAllLeafSetting();
            return Ok(leafSettingService.GetAllLeafSetting());

        }

        // GET api/pharmacyRack/id
        //[HttpGet("{id:int}")]     //  to route only id is int
        //[HttpGet("{id = 231}")]   // assign default value if value of id is not provided
        [HttpGet("{id}")]
        public IActionResult GetLeafSettingById(int id)
        {
            return Ok(leafSettingService.GetLeafSettingById(id));
        }

        //[HttpGet]
        //[Route("~/api/Rack/GetStoreRackNameByItemId/{ItemId}")]
        //public IActionResult GetStoreRackNameByItemId([FromRoute] int ItemId)
        //{
        //    var responseData = new DanpheHTTPResponse<object>();
        //    try
        //    {
        //        responseData.Status = "OK";
        //        responseData.Results = rackService.GetStoreRackNameByItemId(ItemId);
        //    }
        //    catch (System.Exception)
        //    {
        //        responseData.Status = "Failed";
        //        responseData.ErrorMessage = "Failed to load Rack";
        //        return BadRequest(responseData);
        //    }
        //    return Ok(responseData);
        //}

        // POST api/values
        [HttpPost]
        public IActionResult InsertLeafSettingInfo([FromBody] PHRM_LeafSetting value)
        {
            var currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            value.CreatedBy = currentUser.EmployeeId.ToString();
            value.CreatedOn = DateTime.Now.ToString();
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            return Ok(leafSettingService.InsertLeafSettingInfo(value));
        }

        //// PUT api/values/5
        //[HttpPut("{id}")]
        //public IActionResult Put(int id, [FromBody] RackViewModel value)
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        return BadRequest(ModelState);
        //    }
        //    value.RackId = id;
        //    rackService.UpdateRack(value);
        //    return Ok(rackService.GetRack(id));

        //}

        //// DELETE api/values/5
        //[HttpDelete("{id}")]
        //public void Delete(int id)
        //{
        //    leafSettingService.DeleteRack(id);
        //}
        #endregion


    }
}
