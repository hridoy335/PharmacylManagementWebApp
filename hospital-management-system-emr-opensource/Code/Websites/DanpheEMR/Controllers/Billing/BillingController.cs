﻿using DanpheEMR.CommonTypes;
using DanpheEMR.Controllers.Billing;
using DanpheEMR.Core;
using DanpheEMR.Core.Caching;
using DanpheEMR.Core.Configuration;
using DanpheEMR.DalLayer;
using DanpheEMR.Enums;
using DanpheEMR.Security;
using DanpheEMR.ServerModel;
using DanpheEMR.ServerModel.BillingModels;
using DanpheEMR.Services.Billing.DTO;
using DanpheEMR.Services.SSF.DTO;
using DanpheEMR.Utilities;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using DbFunctions = System.Data.Entity.DbFunctions;
using DbUpdateException = System.Data.Entity.Infrastructure.DbUpdateException;
using EntityState = System.Data.Entity.EntityState;

namespace DanpheEMR.Controllers
{

    public class BillingController : CommonController
    {

        double cacheExpMinutes;//= 5;//this should come from configuration later on.
        bool realTimeRemoteSyncEnabled = false;
        bool RealTimeSSFClaimBooking = false;

        private int testCount = 1;

        private readonly BillingDbContext _billingDbContext;
        private readonly RadiologyDbContext _radiologyDbContext;
        private readonly CoreDbContext _coreDbContext;
        private readonly MasterDbContext _mstDBContext;
        private DanpheHTTPResponse<object> _objResponseData;

        public BillingController(IOptions<MyConfiguration> _config) : base(_config)
        {
            cacheExpMinutes = _config.Value.CacheExpirationMinutes;
            realTimeRemoteSyncEnabled = _config.Value.RealTimeRemoteSyncEnabled;
            RealTimeSSFClaimBooking = _config.Value.RealTimeSSFClaimBooking;
            _billingDbContext = new BillingDbContext(connString);
            _radiologyDbContext = new RadiologyDbContext(connString);
            _coreDbContext = new CoreDbContext(connString);
            _mstDBContext = new MasterDbContext(connString);
            _objResponseData = new DanpheHTTPResponse<object>();
            _objResponseData.Status = ENUM_Danphe_HTTP_ResponseStatus.OK;//this is for default
        }

        [Route("PatientCreditInfo")]
        [HttpGet]
        public IActionResult PatientCreditInfo(int patientId)
        {
            Func<object> func = () => GetPatientCreditInfo(patientId);
            return InvokeHttpGetFunction(func);
        }

        [HttpGet]
        [Route("PaymentPages")]
        public IActionResult LoadPaymentPages()
        {
            Func<object> func = () => _mstDBContext.PaymentPages.ToList();
            return InvokeHttpGetFunction(func);
        }

        [HttpGet]
        [Route("BillCfgItemsByPriceCategoryId")]
        public object LoadItemsPriceByPriceCategory(int priceCategoryId)
        {

            Func<object> func = () => _billingDbContext.BillItemsPriceCategoryMaps
                                                .Where(a => a.PriceCategoryId == priceCategoryId && a.IsActive == true)
                                                .Select(a => new
                                                {
                                                    PriceCategoryMapId = a.PriceCategoryServiceItemMapId,
                                                    BilItemPriceId = a.ServiceItemId,
                                                    PriceCategoryId = a.PriceCategoryId,
                                                    ServiceDepartmentId = a.ServiceDepartmentId,
                                                    ItemId = a.IntegrationItemId,
                                                    Price = a.Price,
                                                    DiscountApplicable = a.IsDiscountApplicable,
                                                    ItemLegalCode = a.ItemLegalCode,
                                                    ItemName = a.ItemLegalName

                                                })
                                                .ToList();
            return InvokeHttpGetFunction(func);

        }


        [HttpGet]
        [Route("BillCfgItemsVsPriceCategoryMaps")]
        public IActionResult LoadBilCfgItemsVsPriceCategoryMapping()
        {
            Func<object> func = () => _billingDbContext.BillItemsPriceCategoryMaps
                                                .Where(a => a.IsActive == true)
                                                .Select(a => new
                                                {
                                                    PriceCategoryMapId = a.PriceCategoryServiceItemMapId,
                                                    BilItemPriceId = a.ServiceItemId,
                                                    PriceCategoryId = a.PriceCategoryId,
                                                    ServiceDepartmentId = a.ServiceDepartmentId,
                                                    ItemId = a.IntegrationItemId,
                                                    Price = a.Price,
                                                    DiscountApplicable = a.IsDiscountApplicable,
                                                    //Discount = a.Discount,
                                                    //IsCoPayment = a.IsCoPayment,
                                                    //CoPaymentCashPercent = a.CoPaymentCashPercent,
                                                    //CoPaymentCreditPercent = a.CoPaymentCreditPercent

                                                })
                                                .ToList();
            return InvokeHttpGetFunction(func);
        }

        [HttpGet]
        [Route("SsfInvoices")]
        public IActionResult GetSSFInvoiceDetails(string fromDate, string toDate, string patientType)
        {
            Func<object> func = () => GetSsfInvoices(fromDate, toDate, patientType, _billingDbContext);
            return InvokeHttpGetFunction(func);

        }

        [HttpGet]
        [Route("MembershipTypeVsPriceCategoryMaps")]
        public IActionResult LoadMembershipTypeVsPriceCategoryMapping()
        {
            Func<object> func = () => _billingDbContext.PriceCategorySchemesMaps.Where(a => a.IsActive == true).ToList();
            return InvokeHttpGetFunction(func);
        }

        [HttpGet]
        [Route("PendingIncomingUserHandover")]
        public IActionResult GetPendingIncomingUserHandOver()
        {
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);
            Func<object> func = () => (from handover in _billingDbContext.HandoverTransaction
                                       join handOverBy in _billingDbContext.Employee.Include("Department") on handover.HandoverByEmpId equals handOverBy.EmployeeId
                                       join handOverTo in _billingDbContext.Employee on handover.HandoverToEmpId equals handOverTo.EmployeeId
                                       join counter in _billingDbContext.BillingCounter on handover.CounterId equals counter.CounterId
                                       where handover.IsActive == true && handover.HandoverStatus == ENUM_HandOverStatus.Pending && handover.HandoverType == ENUM_HandOverType.User
                                       && handover.HandoverToEmpId == currentUser.EmployeeId
                                       select new
                                       {
                                           HandoverTxnId = handover.HandoverTxnId,
                                           HandoverByEmpId = handover.HandoverByEmpId,
                                           HandoverAmount = handover.HandoverAmount,
                                           HandOverType = handover.HandoverType,
                                           CounterName = counter.CounterName,
                                           CounterId = counter.CounterId,
                                           HandOverByName = handOverBy.FullName,
                                           DepartmentName = handOverBy.Department != null ? handOverBy.Department.DepartmentName : null,
                                           HandOverToName = handOverTo.FullName,
                                           CreatedOn = handover.CreatedOn
                                       }).OrderBy(s => s.CreatedOn).ToList();

            return InvokeHttpGetFunction(func);
        }

        [HttpGet]
        [Route("PendingOutgoingHandover")]
        public IActionResult GetPendingOutgoingHandOver(string handOverType)
        {
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);
            Func<object> func = () => GetPendingOutgoingHandoverDetail(handOverType, currentUser);
            return InvokeHttpGetFunction(func);
        }


        [HttpPut]
        [Route("HandoverStatus")]
        public IActionResult UpdateHandOverStatus(int handOverTransactionId)
        {
            var txnDate = DateTime.Now;
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);

            Func<object> func = () => UpdateHandoverStatus(handOverTransactionId, txnDate, currentUser);
            return InvokeHttpPutFunction(func);
        }

        [HttpGet]
        [Route("InsurancePatientsProvisionalInfo")]
        public IActionResult InsurancePatientsProvisionalInfo()
        {
            //else if (reqType != null && reqType.ToLower() == "listpatientinsuranceprovisional")
            //{
            Func<object> func = () => (from bill in _billingDbContext.BillingTransactionItems.Include("Patient")
                                       where bill.BillStatus == ENUM_BillingStatus.provisional// "provisional"
                                       && bill.IsInsurance == true
                                       //couldn't use Patient.ShortName directly since it's not mapped to DB and hence couldn't be used inside LINQ.
                                       group bill by new { bill.PatientId, bill.Patient.PatientCode, bill.Patient.FirstName, bill.Patient.LastName, bill.Patient.MiddleName, bill.Patient.DateOfBirth, bill.Patient.Gender } into p
                                       select new
                                       {
                                           PatientId = p.Key.PatientId,
                                           PatientCode = p.Key.PatientCode,
                                           ShortName = p.Key.FirstName + " " + (string.IsNullOrEmpty(p.Key.MiddleName) ? "" : p.Key.MiddleName + " ") + p.Key.LastName,
                                           Gender = p.Key.Gender,
                                           DateOfBirth = p.Max(a => a.Patient.DateOfBirth.Value),
                                           LastCreditBillDate = p.Max(a => a.RequisitionDate.Value),
                                           TotalCredit = Math.Round(p.Sum(a => a.TotalAmount), 2)
                                       }).OrderByDescending(b => b.LastCreditBillDate).ToList();
            return InvokeHttpGetFunction<object>(func);

        }

        [HttpGet]
        [Route("TransferHandoverReport")]
        public IActionResult TransferHandoverReport(string fromDate, string toDate, string status, string handoverType)
        {
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", fromDate),
                        new SqlParameter("@ToDate", toDate),
                        new SqlParameter("@Status", status),
                        new SqlParameter("@HandoverType", handoverType)
                    };
            Func<object> func = () => DALFunctions.GetDataTableFromStoredProc("SP_TransferHandoverReport", paramList, _billingDbContext);
            return InvokeHttpGetFunction(func);

        }

        [HttpGet]
        [Route("ProvisionalItemsByPatientId")]
        public IActionResult ProvisionalItemsByPatientId(int patientId, int schemeId)
        {
            //else if (reqType != null && reqType == "provisionalItemsByPatientId" && InputId != null && InputId != 0)
            //{
            Func<object> func = () => GetProvisionalItemsMappedByPatientId(patientId, schemeId);
            return InvokeHttpGetFunction<object>(func);
        }


        [HttpGet]
        [Route("PatientsProvisionalInfo")]
        public IActionResult PatientsProvisionalInfo()
        {
            //  if (reqType != null && reqType.ToLower() == "listpatientunpaidtotal")
            //  {
            Func<object> func = () => (from bill in _billingDbContext.BillingTransactionItems.Include("Patient")
                                       join scheme in _billingDbContext.BillingSchemes on bill.DiscountSchemeId equals scheme.SchemeId
                                       join visit in _billingDbContext.Visit on bill.PatientVisitId equals visit.PatientVisitId
                                       where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional"
                                       && bill.IsProvisionalDischarge == false
                                       && (bill.IsInsurance == false || bill.IsInsurance == null)
                                       && (bill.VisitType == ENUM_VisitType.outpatient || bill.VisitType == ENUM_VisitType.emergency)
                                       //couldn't use Patient.ShortName directly since it's not mapped to DB and hence couldn't be used inside LINQ.
                                       group bill by new { bill.PatientId, bill.Patient.PatientCode, bill.Patient.ShortName, bill.DiscountSchemeId, scheme.SchemeName, bill.Patient.DateOfBirth, bill.Patient.Gender, bill.Patient.PhoneNumber, visit.PriceCategoryId } into p
                                       select new
                                       {
                                           PatientId = p.Key.PatientId,
                                           PatientCode = p.Key.PatientCode,
                                           ShortName = p.Key.ShortName,
                                           PhoneNumber = p.Key.PhoneNumber,
                                           Gender = p.Key.Gender,
                                           DateOfBirth = p.Max(a => a.Patient.DateOfBirth),
                                           LastCreditBillDate = p.Max(a => a.CreatedOn),
                                           TotalCredit = Math.Round(p.Sum(a => a.TotalAmount), 2),
                                           SchemeId = p.Key.DiscountSchemeId,
                                           SchemeName = p.Key.SchemeName,
                                           PriceCategoryId = p.Key.PriceCategoryId
                                       }).OrderByDescending(b => b.LastCreditBillDate).ToList();

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("PatientsProvisionaItems")]
        public IActionResult PatientsProvisionaItems(string FromDate, string ToDate)
        {
            // else if (reqType != null && reqType == "allProvisionalItems")
            // {
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                        new SqlParameter("@ToDate", ToDate)
                    };

            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_Outpatient_Provisional_Items_List", paramList, _billingDbContext);

            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("PatientVisitCreditBalance")]
        public IActionResult PatientVisitCreditBalance(int patientVisitId)
        {
            //else if (reqType != null && reqType == "creditbalanceByPatientId" && InputId != null && InputId != 0)
            //{
            Func<object> func = () => _billingDbContext.BillingTransactionItems
                .Where(bill => bill.PatientVisitId == patientVisitId
                //&& (bill.BillStatus == "provisional" || bill.BillStatus == "unpaid")
                && (bill.BillStatus == ENUM_BillingStatus.provisional || bill.BillStatus == ENUM_BillingStatus.unpaid)).DefaultIfEmpty()
                .Sum(a => a.TotalAmount == null ? 0 : a.TotalAmount);

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("ServiceDepartments")]
        public IActionResult ServiceDepartments()
        {
            //   else if (reqType != null && reqType == "allServiceDepts")
            Func<object> func = () => (from srvDpts in _billingDbContext.ServiceDepartment
                                       select new
                                       {
                                           ServiceDepartmentId = srvDpts.ServiceDepartmentId,
                                           ServiceDepartmentName = srvDpts.ServiceDepartmentName
                                       }).ToList();

            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("PatientMembershipInfo")]
        //public IActionResult PatientMembershipInfo(int patientId)
        //{
        //    // else if (reqType != null && reqType == "getPatMembershipInfo" && InputId != null && InputId != 0)
        //    //{

        //    Func<object> func = () => GetPatientMembershipInfo(patientId);
        //    return InvokeHttpGetFunction<object>(func);
        //}


        [HttpGet]
        [Route("MembershipTypes")]
        public IActionResult MembershipTypes()
        {
            //else if (reqType != null && reqType == "membership-types")
            //{
            Func<object> func = () => (from type in _billingDbContext.BillingSchemes
                                       where type.IsActive == true
                                       select new
                                       {
                                           type.SchemeId,
                                           MembershipType = type.SchemeName,
                                           MembershipTypeName = type.SchemeName + " (" + type.DiscountPercent + " % off)",
                                           type.DiscountPercent
                                       }).ToList();

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("InsurancePatientProvisionalItems")]
        public IActionResult InsurancePatientProvisionalItems(int patientId)
        {
            //else if (reqType != null && reqType == "insuranceprovisionalItemsByPatientId" && InputId != null && InputId != 0)
            //{

            Func<object> func = () => GetInsurancePatientProvisionalItems(patientId);
            return InvokeHttpGetFunction<object>(func);
        }



        //[HttpGet]
        //[Route("PatientProvisionalItemsByPatientIdAndVisitId")]
        //public IActionResult PatientProvisionalItemsByPatientIdAndVisitId(int patientId, int patientVisitId)
        //{
        //    //else if (reqType != null && reqType == "provItemsByPatIdAndVisitId" && InputId != null && InputId != 0 && patVisitId != null && patVisitId != 0)
        //    //{
        //    Func<object> func = () => GetPatientProvisionalItemsByPatientIdAndVisitId(patientId, patientVisitId);
        //    return InvokeHttpGetFunction<object>(func);
        //}



        [HttpGet]
        [Route("PatientProvisionalSlip")]
        public IActionResult PatientProvisionalSlip(int patientId, int? fiscalYearId, int? provReceiptNo, string visitType, int schemeId)
        {
            //else if (reqType == "ProvisionalItemsInfoForPrint")
            //{'
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            Func<object> func = () => (
                  from pat in _billingDbContext.Patient
                  where pat.PatientId == patientId
                  join cntrSub in _billingDbContext.CountrySubdivisions
                        on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
                  select new
                  {
                      PatientId = pat.PatientId,
                      PatientCode = pat.PatientCode,
                      PatientName = pat.ShortName,
                      ContactNo = pat.PhoneNumber,
                      DateOfbirth = pat.DateOfBirth,
                      Age = pat.Age,
                      Gender = pat.Gender,
                      CountrySubdivisionName = cntrSub.CountrySubDivisionName,
                      Address = pat.Address,
                      BillingUser = currentUser.UserName,
                      ItemsList = (from bill in _billingDbContext.BillingTransactionItems
                                   join fy in _billingDbContext.BillingFiscalYears on bill.ProvisionalFiscalYearId equals fy.FiscalYearId
                                   join scheme in _billingDbContext.BillingSchemes on bill.DiscountSchemeId equals scheme.SchemeId
                                   join patScheme in _billingDbContext.PatientSchemeMaps on new { patientId = bill.PatientId, schemeId = bill.DiscountSchemeId } equals new { patientId = patScheme.PatientId, schemeId = patScheme.SchemeId } into grp
                                   from patientSchemeMap in grp.DefaultIfEmpty()
                                   where bill.BillStatus == ENUM_BillingStatus.provisional
                                   && bill.DiscountSchemeId == schemeId
                                   //&& bill.BillingType== ENUM_BillingType.outpatient
                                   //exclude insurance items if any.
                                   && bill.PatientId == patientId && (bill.IsInsurance == null || bill.IsInsurance == false)
                                   //if provisional receipt number is null then take all, else take specific receipt only by checking both fiscalYrId and ProvReceiptNo.
                                   && (bill.ProvisionalFiscalYearId == fiscalYearId && bill.ProvisionalReceiptNo == provReceiptNo)
                                  // we're considering both ER and OPD as same for Provisional hence whenever emergency comes then we're changing it to outpatient.
                                  && (visitType == null || bill.VisitType.Replace("emergency", "outpatient") == visitType)
                                   select new
                                   {
                                       PatientPolicyNo = patientSchemeMap != null ? patientSchemeMap.PolicyNo : null,
                                       SchemeName = scheme.SchemeName,
                                       BillingTransactionItemId = bill.BillingTransactionItemId,
                                       ServiceDepartmentName = bill.ServiceDepartmentName,
                                       ServiceDepartmentId = bill.ServiceDepartmentId,
                                       AssignedToDrName = bill.PerformerName,
                                       FiscalYearFormatted = fy.FiscalYearFormatted,
                                       ItemName = bill.ItemName,
                                       Price = bill.Price,
                                       Quantity = bill.Quantity,
                                       SubTotal = bill.SubTotal,
                                       TaxableAmount = bill.TaxableAmount,
                                       DiscountAmount = bill.DiscountAmount,
                                       TotalAmount = bill.TotalAmount,
                                       CreatedOn = bill.CreatedOn,
                                       ProvisionalReceiptNo = bill.ProvisionalReceiptNo,
                                       IsCoPayment = bill.IsCoPayment,
                                       CoPaymentCashAmount = bill.CoPaymentCashAmount,
                                       CoPaymentCreditAmount = bill.CoPaymentCreditAmount
                                   })
                  }).FirstOrDefault();

            return InvokeHttpGetFunction<object>(func);
        }
        [HttpGet]
        [Route("BillCancellationReceipt")]
        public IActionResult BillCancellationReceipt(int patientId, int provisionalReturnItemId)
        {
            Func<object> func = () => GetCancelledItemDetail(patientId, provisionalReturnItemId);
            return InvokeHttpGetFunction<object>(func);
        }
        private object GetCancelledItemDetail(int patientId, int provisionalReturnItemId)
        {

            var provisionalCancelledReceiptDetail = (from cancelItm in _billingDbContext.BillingCancellation
                                                     join itm in _billingDbContext.BillingTransactionItems
                                                     on cancelItm.BillingTransactionItemId equals itm.BillingTransactionItemId
                                                     join pat in _billingDbContext.Patient on cancelItm.PatientId equals pat.PatientId
                                                     join cntrSub in _billingDbContext.CountrySubdivisions
                                                     on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
                                                     join servDep in _billingDbContext.ServiceDepartment
                                                     on cancelItm.ServiceDepartmentId equals servDep.ServiceDepartmentId
                                                     join emp in _billingDbContext.Employee
                                                     on pat.CreatedBy equals emp.EmployeeId
                                                     join scheme in _billingDbContext.BillingSchemes
                                                     on cancelItm.SchemeId equals scheme.SchemeId
                                                     where pat.PatientId == patientId && cancelItm.ProvisionalItemReturnId == provisionalReturnItemId
                                                     select new BillCancellationReceipt_DTO
                                                     {
                                                         PatientId = pat.PatientId,
                                                         PatientCode = pat.PatientCode,
                                                         PatientName = pat.ShortName,
                                                         ContactNo = pat.PhoneNumber,
                                                         DateOfbirth = pat.DateOfBirth,
                                                         Age = pat.Age,
                                                         Gender = pat.Gender,
                                                         CountrySubdivisionName = cntrSub.CountrySubDivisionName,
                                                         Address = pat.Address,
                                                         User = emp.FullName,
                                                         ItemCode = cancelItm.ItemCode,
                                                         ItemName = cancelItm.ItemName,
                                                         CancellationReceiptNo = cancelItm.CancellationReceiptNo,
                                                         ReferenceProvisionalReceiptNo = cancelItm.ReferenceProvisionalReceiptNo,
                                                         CreatedOn = cancelItm.CreatedOn,
                                                         CancelledQty = cancelItm.CancelledQty,
                                                         Price = cancelItm.Price,
                                                         CancelledSubtotal = cancelItm.CancelledSubtotal,
                                                         CancelledDiscountAmount = cancelItm.CancelledDiscountAmount,
                                                         CancelledTotalAmount = cancelItm.CancelledTotalAmount,
                                                         ProvisionalItemReturnId = cancelItm.ProvisionalItemReturnId,
                                                         CancellationRemarks = cancelItm.CancellationRemarks,
                                                         IsCoPayment = itm.IsCoPayment,
                                                         CoPayCash = itm.CoPaymentCashAmount,
                                                         ServiceDepartmentName = servDep.ServiceDepartmentName,
                                                         SchemeName = scheme.SchemeName,
                                                     }).FirstOrDefault();

            return provisionalCancelledReceiptDetail;
        }


        [HttpGet]
        [Route("InpatientsCancelledItems")]
        public IActionResult InPatientCancelledItems(int patientId, int patientVisitId)
        {
            Func<object> func = () => GetInpatientsCancelledItems(patientId, patientVisitId);
            return InvokeHttpGetFunction<object>(func);
        }

        private object GetInpatientsCancelledItems(int patientId, int patientVisitId)
        {

            var inpatientsCancelledItems = (from cancelItm in _billingDbContext.BillingCancellation
                                            join itm in _billingDbContext.BillingTransactionItems
                                            on cancelItm.BillingTransactionItemId equals itm.BillingTransactionItemId
                                            join pat in _billingDbContext.Patient on cancelItm.PatientId equals pat.PatientId
                                            join cntrSub in _billingDbContext.CountrySubdivisions
                                            on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
                                            join servDep in _billingDbContext.ServiceDepartment
                                            on cancelItm.ServiceDepartmentId equals servDep.ServiceDepartmentId
                                            join emp in _billingDbContext.Employee
                                            on pat.CreatedBy equals emp.EmployeeId
                                            where pat.PatientId == patientId && cancelItm.PatientVisitId == patientVisitId && itm.BillStatus == ENUM_BillingStatus.cancel
                                            select new BillCancellationReceipt_DTO
                                            {
                                                PatientId = pat.PatientId,
                                                PatientCode = pat.PatientCode,
                                                PatientName = pat.ShortName,
                                                ContactNo = pat.PhoneNumber,
                                                DateOfbirth = pat.DateOfBirth,
                                                Age = pat.Age,
                                                Gender = pat.Gender,
                                                CountrySubdivisionName = cntrSub.CountrySubDivisionName,
                                                Address = pat.Address,
                                                User = emp.FullName,
                                                ItemCode = cancelItm.ItemCode,
                                                ItemName = cancelItm.ItemName,
                                                CancellationReceiptNo = cancelItm.CancellationReceiptNo,
                                                ReferenceProvisionalReceiptNo = cancelItm.ReferenceProvisionalReceiptNo,
                                                CreatedOn = cancelItm.CreatedOn,
                                                CancelledQty = cancelItm.CancelledQty,
                                                Price = cancelItm.Price,
                                                CancelledSubtotal = cancelItm.CancelledSubtotal,
                                                CancelledDiscountAmount = cancelItm.CancelledDiscountAmount,
                                                CancelledTotalAmount = cancelItm.CancelledTotalAmount,
                                                ProvisionalItemReturnId = cancelItm.ProvisionalItemReturnId,
                                                CancellationRemarks = cancelItm.CancellationRemarks,
                                                IsCoPayment = itm.IsCoPayment,
                                                CoPayCash = itm.CoPaymentCashAmount,
                                                ServiceDepartmentName = servDep.ServiceDepartmentName,
                                            }).ToList();

            return inpatientsCancelledItems;
        }

        [HttpGet]
        [Route("InsurancePatientProvisionalSlip")]
        public IActionResult InsurancePatientProvisionalSlip(int patientId, int fiscalYearId, int provReceiptNo, string visitType)
        {
            //else if (reqType == "InsuranceProvisionalItemsInfoForPrint")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            Func<object> func = () => (
                  from pat in _billingDbContext.Patient
                  where pat.PatientId == patientId
                  join cntrSub in _billingDbContext.CountrySubdivisions
                        on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
                  select new
                  {
                      PatientId = pat.PatientId,
                      PatientCode = pat.PatientCode,
                      PatientName = pat.ShortName,
                      ContactNo = pat.PhoneNumber,
                      DateOfbirth = pat.DateOfBirth,
                      Age = pat.Age,
                      Gender = pat.Gender,
                      CountrySubdivisionName = cntrSub.CountrySubDivisionName,
                      Address = pat.Address,
                      BillingUser = currentUser.UserName,
                      ItemsList = (from bill in _billingDbContext.BillingTransactionItems
                                   join fy in _billingDbContext.BillingFiscalYears on bill.ProvisionalFiscalYearId equals fy.FiscalYearId
                                   where bill.BillStatus == ENUM_BillingStatus.provisional
                                   //exclude insurance items if any.
                                   && bill.PatientId == patientId && (bill.IsInsurance != null || bill.IsInsurance == true)
                                   //if provisional receipt number is null then take all, else take specific receipt only by checking both fiscalYrId and ProvReceiptNo.
                                   && (provReceiptNo == null ||
                                        (bill.ProvisionalFiscalYearId == fiscalYearId && bill.ProvisionalReceiptNo == provReceiptNo))
                                  // we're considering both ER and OPD as same for Provisional hence whenever emergency comes then we're changing it to outpatient.
                                  && (visitType == null || bill.VisitType.Replace("emergency", "outpatient") == visitType)
                                   select new
                                   {
                                       BillingTransactionItemId = bill.BillingTransactionItemId,
                                       ServiceDepartmentName = bill.ServiceDepartmentName,
                                       ServiceDepartmentId = bill.ServiceDepartmentId,
                                       AssignedToDrName = bill.PerformerName,
                                       FiscalYearFormatted = fy.FiscalYearFormatted,
                                       ItemName = bill.ItemName,
                                       Price = bill.Price,
                                       Quantity = bill.Quantity,
                                       SubTotal = bill.SubTotal,
                                       TaxableAmount = bill.TaxableAmount,
                                       DiscountAmount = bill.DiscountAmount,
                                       TotalAmount = bill.TotalAmount,
                                       CreatedOn = bill.CreatedOn,
                                       ProvisionalReceiptNo = bill.ProvisionalReceiptNo,
                                   })
                  }).FirstOrDefault();

            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("InPatientProvisionalItemsByPatientIdAndVisitId")]
        //public IActionResult InPatientProvisionalItemsByPatientIdAndVisitId(int patientId, int patientVisitId)
        //{
        //    //else if (reqType != null && reqType == "inPatProvItemsByPatIdAndVisitId" && InputId != null && InputId != 0 && patVisitId != null && patVisitId != 0)
        //    //{

        //    Func<object> func = () => GetInPatientProvisionalItemsByPatientIdAndVisitId(patientId, patientVisitId);
        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("InPatientProvisionalItems")]
        public IActionResult InPatientProvisionalItems(int patientId, int patientVisitId, string module)
        {
            //else if (reqType != null && reqType == "inPatientProvisionalItemList")
            //{

            Func<object> func = () => GetInPatientProvisionalItems(patientId, patientVisitId, module);
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("Invoices")]
        public IActionResult Invoices(string FromDate, string ToDate)
        {
            // else if (reqType != null && reqType.ToLower() == "listinvoicewisebill")
            //{
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                        new SqlParameter("@ToDate", ToDate)
                };

            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetBillingInvoicesBetweenDateRange", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("CreditNotes")]
        public IActionResult CreditNotes(string FromDate, string ToDate)
        {
            //else if (reqType != null && reqType.ToLower() == "credit-note-list")
            //{
            Func<DataTable> func = () => GetCreditNoteList(FromDate, ToDate);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("DuplicatePrint/ProvisionalReceipts")]
        public IActionResult ProvisionalReceiptsDetailsForDuplicatePrint()
        {
            // else if (reqType != null && reqType.ToLower() == "listprovisionalwisebill")
            //{
            //Func<object> func = () => ((
            //              from pat in _billingDbContext.Patient
            //              join cntrSub in _billingDbContext.CountrySubdivisions
            //                    on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
            //              join bill in _billingDbContext.BillingTransactionItems
            //                    on pat.PatientId equals bill.PatientId
            //              where bill.BillStatus == ENUM_BillingStatus.provisional
            //              //exclude insurance provisionals
            //              && (bill.IsInsurance == null || bill.IsInsurance == false)
            //              group bill by new
            //              {
            //                  bill.PatientId,
            //                  pat.PatientCode,
            //                  bill.DiscountSchemeId,
            //                  bill.ProvisionalReceiptNo,
            //                  pat.ShortName,
            //                  pat.PhoneNumber,
            //                  pat.DateOfBirth,
            //                  pat.Gender,
            //                  cntrSub.CountrySubDivisionName,
            //                  pat.Address,
            //                  bill.ProvisionalFiscalYearId
            //              } into grp

            //              select new
            //              {
            //                  PatientId = grp.Key.PatientId,
            //                  PatientCode = grp.Key.PatientCode,
            //                  PatientName = grp.Key.ShortName,
            //                  PhoneNumber = grp.Key.PhoneNumber,
            //                  DateOfbirth = grp.Key.DateOfBirth,
            //                  Gender = grp.Key.Gender,
            //                  CountrySubdivisionName = grp.Key.CountrySubDivisionName,
            //                  Address = grp.Key.Address,
            //                  LastBillDate = grp.Max(a => a.CreatedOn),
            //                  TotalAmount = grp.Sum(a => a.TotalAmount),
            //                  SchemeId = grp.Key.DiscountSchemeId,
            //                  ProvisionalFiscalYearId = grp.Key.ProvisionalFiscalYearId,
            //                  ProvisionalReceiptNo = grp.Key.ProvisionalReceiptNo
            //              }).OrderByDescending(a => a.LastBillDate).ToList());
            //return InvokeHttpGetFunction<object>(func);

            Func<object> func = () => (from bill in _billingDbContext.BillingTransactionItems.Include("Patient")
                                       join scheme in _billingDbContext.BillingSchemes on bill.DiscountSchemeId equals scheme.SchemeId
                                       join visit in _billingDbContext.Visit on bill.PatientVisitId equals visit.PatientVisitId
                                       where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional"
                                       && (bill.IsInsurance == false || bill.IsInsurance == null)
                                       group bill by new { bill.PatientId, bill.Patient.PatientCode, bill.Patient.ShortName, bill.DiscountSchemeId, scheme.SchemeName, bill.ProvisionalReceiptNo, bill.ProvisionalFiscalYearId, bill.BillingType, bill.Patient.DateOfBirth, bill.Patient.Gender, bill.Patient.PhoneNumber, visit.PriceCategoryId } into p
                                       select new
                                       {
                                           PatientId = p.Key.PatientId,
                                           PatientCode = p.Key.PatientCode,
                                           PatientName = p.Key.ShortName,
                                           PhoneNumber = p.Key.PhoneNumber,
                                           Gender = p.Key.Gender,
                                           DateOfBirth = p.Max(a => a.Patient.DateOfBirth),
                                           LastBillDate = p.Max(a => a.CreatedOn),
                                           TotalAmount = Math.Round(p.Sum(a => a.TotalAmount), 2),
                                           SchemeId = p.Key.DiscountSchemeId,
                                           SchemeName = p.Key.SchemeName,
                                           PriceCategoryId = p.Key.PriceCategoryId,
                                           ProvisionalReceiptNo = p.Key.ProvisionalReceiptNo,
                                           ProvisionalFiscalYearId = p.Key.ProvisionalFiscalYearId

                                       }).OrderByDescending(b => b.LastBillDate).ToList();
            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("DuplicateBillByReceiptId")]
        //public IActionResult DuplicateBillByReceiptId(int invoiceNo, int fiscalYearId, bool getVisitInfo)
        //{
        //    //else if (reqType != null && reqType == "duplicateBillsByReceiptId" && InputId != null && InputId != 0)
        //    //{
        //    Func<object> func = () => GetDuplicateBillByReceiptId(invoiceNo, fiscalYearId, getVisitInfo);
        //    return InvokeHttpGetFunction<object>(func);
        //}

        //[HttpGet]
        //[Route("InPatientForPartialBilling")]
        //public IActionResult InPatientForPartialBilling(int patientVisitId)
        //{
        //    // else if (reqType == "InPatientDetailForPartialBilling")
        //    //{


        //    Func<object> func = () => GetInPatientForPartialBilling(patientVisitId);
        //    return InvokeHttpGetFunction<object>(func);
        //}

        //[HttpGet]
        //[Route("DuplicateProvisionalBillsByReceiptId")]
        //public IActionResult DuplicateProvisionalBillsByReceiptId(int receiptNo, int fiscalYearId)
        //{
        //    //else if (reqType != null && reqType == "duplicateProvisionalBillsByReceiptId" && InputId != null && InputId != 0)
        //    //{
        //    ;
        //    Func<object> func = () => GetDuplicateProvisionalBillsByReceiptId(receiptNo, fiscalYearId);
        //    return InvokeHttpGetFunction<object>(func);

        //}

        [HttpGet]
        [Route("PatientReturnedInvoices_Old")]
        public IActionResult PatientReturnedInvoices_Old(int patientId)
        {
            //else if (reqType == "returned-patient-invoices")
            //{


            Func<object> func = () => GetPatientReturnedInvoices_Old(patientId);
            return InvokeHttpGetFunction<object>(func);
        }


        [HttpGet]
        [Route("PatientsPastBillSummary")]
        public IActionResult PatientsPastBillSummary(int patientId, int? schemeId = null)
        {
            // else if (reqType != null && reqType == "patientPastBillSummary" && InputId != null && InputId != 0)
            //{
            Func<object> func = () => GetPatientsPastBillSummary(patientId, schemeId);
            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("PatientPastBillSummaryForBillSettlement")]
        //public IActionResult PatientPastBillSummaryForBillSettlement(int patientId, bool IsPatientAdmitted)
        //{
        //    //else if (reqType != null && reqType == "patientPastBillSummaryForBillSettlements" && InputId != null && InputId != 0)
        //    //{
        //    Func<object> func = () => GetPatientPastBillSummaryForBillSettlement(patientId, IsPatientAdmitted);
        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("InsurancePatientPastBillSummary")]
        public IActionResult InsurancePatientPastBillSummary(int patientId)
        {
            // else if (reqType != null && reqType == "patientPastInsuranceBillSummary" && InputId != null && InputId != 0)
            //{

            Func<object> func = () => GetInsurancePatientPastBillSummary(patientId);
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("PatientBillHistoryDetail")]
        public IActionResult PatientBillHistoryDetail(int patientId)
        {
            // else if (reqType == "patient-bill-history-detail")
            //{

            Func<object> func = () => GetPatientBillHistoryDetail(patientId);
            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("OpdRequisitionItem")]
        //public IActionResult OpdRequisitionItem(int requisitionId)
        //{
        //    // else if (reqType == "OPDRequisitionItem")
        //    //{
        //    Func<object> func = () => GetOpdRequisitionItem(requisitionId);
        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("DoctorOrdersFromAllDepartments")]
        public IActionResult DoctorOrdersFromAllDepartments(int patientId)
        {
            //else if (reqType == "DoctorOrdersFromAllDepartments")
            //{

            Func<object> func = () => ((from billtot in _billingDbContext.BillItemRequisitions
                                        where billtot.PatientId == patientId && billtot.BillStatus == "pending"
                                        select billtot).ToList());
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("PatientPendingRequisitionsByDepartmentName")]
        public IActionResult PatientPendingRequisitionsByDepartmentName(int patientId, int serviceDepartmentId)
        {
            // else if (reqType == "pendingReqsByDeptname")
            //{

            Func<object> func = () => ((from req in _billingDbContext.BillItemRequisitions
                                        where req.PatientId == patientId && req.BillStatus == "pending"
                                        && req.ServiceDepartmentId == serviceDepartmentId
                                        select req).ToList());
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("ServiceDepartmentItems")]
        public IActionResult ServiceDepartmentItems(int serviceDepartmentId)
        {
            //else if (reqType == "serviceDeptItems" && serviceDeptId != 0)
            //{
            Func<object> func = () => GetServiceDepartmentItems(serviceDepartmentId);
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("BillCfgItems")]
        public IActionResult BillCfgItems(string srvDeptIdListStr, string itemIdListStr)
        {
            //else if (reqType == "billItemList")
            //{
            Func<object> func = () => GetBillCFGItemList(srvDeptIdListStr, itemIdListStr);
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("PendingDoctorOrderTotal")]
        public IActionResult PendingDoctorOrderTotal()
        {
            //else if (reqType == "pendingDoctorOrderTotal")
            //{

            Func<object> func = () => GetPendingDoctorOrderTotal();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("NursingOrderList")]
        public IActionResult NursingOrderList(int patientId)
        {
            //else if (reqType == "nursingOrderList" && reqType != null)
            //{
            Func<object> func = () => GetNursingOrderList(patientId);
            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("PatientUnpaidBillsForCancellation")]
        //public IActionResult PatientUnpaidBillsForCancellation(int patientId)
        //{
        //    //else if (reqType != null && reqType == "unpaidBillsbyPatientIdForCancellation" && InputId != null && InputId != 0)
        //    //{
        //    //for this request type, patientid comes as inputid.
        //    Func<object> func = () => ((from bill in _billingDbContext.BillItemRequisitions.Include("Patient")
        //                                where bill.BillStatus == "pending" && bill.PatientId == patientId
        //                                select bill).ToList<BillItemRequisition>().OrderBy(b => b.ServiceDepartmentId));
        //    return InvokeHttpGetFunction<object>(func);
        //}

        //[HttpGet]
        //[Route("PatientUnpaidBillsForCreditCancellation")]
        //public IActionResult PatientUnpaidBillsForCreditCancellation(int patientId)
        //{
        //    //else if (reqType != null && reqType == "unpaidBillsbyPatientIdForCreditCancellation" && InputId != null && InputId != 0)
        //    //{
        //    Func<object> func = () => ((from bill in _billingDbContext.BillingTransactionItems.Include("Patient")
        //                                where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" 
        //                                && bill.PatientId == patientId
        //                                select bill).ToList<BillingTransactionItemModel>()
        //                                  .OrderBy(b => b.ServiceDepartmentId).ToList());
        //    return InvokeHttpGetFunction<object>(func);
        //}


        [HttpGet]
        [Route("BillingCounters")]
        public IActionResult BillingCounters()
        {
            //else if (reqType == "getCounter")
            //{
            Func<object> func = () => ((from counter in _billingDbContext.BillingCounter
                                        select counter
                                  ).ToList<BillingCounter>().OrderBy(b => b.CounterId));

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("GetTxnItemsForEditDoctor")]
        public IActionResult GetTxnItemsForEditDoctor(string FromDate, string ToDate, string search)
        {
            //else if (reqType != null && reqType == "GetTxnItemsForEditDoctor")
            //{
            Func<object> func = () => GetTransactionItemsForEditDoctor(FromDate, ToDate, search);
            return InvokeHttpGetFunction<object>(func);
        }


        [HttpGet]
        [Route("GetTxnItemsForEditDoctorRad")]
        public IActionResult GetTxnItemsForEditDoctorRad(string FromDate, string ToDate, string search)
        {
            // else if (reqType != null && reqType == "GetTxnItemsForEditDoctorRad")
            //{

            string radIntegrationName = "radiology";// this is hardcoded/reserved keyword for Radiology related Servicedepartments.

            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                    new SqlParameter("@ToDate", ToDate) ,
                        new SqlParameter("@SearchText", search) ,
                       new SqlParameter("@SrvDptIntegrationName", radIntegrationName)

                    };

            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetBillTxnItemsBetnDateRange_ForDepartment", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        //[HttpGet]
        //[Route("OpdDoctorList")]
        //public IActionResult OpdDoctorList()
        //{
        //    //else if (reqType != null && reqType == "opd-doctors-list")
        //    //{
        //    Func<object> func = () => (from itm in _billingDbContext.BillItemPrice
        //                               join srvDpt in _billingDbContext.ServiceDepartment
        //                               on itm.ServiceDepartmentId equals srvDpt.ServiceDepartmentId
        //                               where srvDpt.ServiceDepartmentName == "OPD"
        //                               join emp in _billingDbContext.Employee
        //                               on itm.ItemId equals emp.EmployeeId
        //                               select new
        //                               {
        //                                   EmployeeId = emp.EmployeeId,
        //                                   EmployeeName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
        //                                   DepartmentId = emp.DepartmentId,
        //                                   ServiceDepartmentId = srvDpt.ServiceDepartmentId,
        //                                   OPDPrice = itm.Price,
        //                                   ItemName = itm.ItemName,
        //                               }).ToList();
        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("GetProviderList")]
        public IActionResult GetProviderList()
        {
            //else if (reqType != null && reqType == "GetProviderList")
            //{

            Func<object> func = () => GetProviderListFromDanpheCache();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("ListDoctors")]
        public IActionResult ListDoctors()
        {
            //else if (reqType == "doctor-list")
            //{
            //sud:9Aug'18--isappointmentapplicable field can be taken from employee now.. 

            Func<object> func = () => (from e in _mstDBContext.Employees
                                       where e.IsAppointmentApplicable.HasValue && e.IsAppointmentApplicable == true
                                       //sud:13Mar'19--get only Isactive=True doctors..
                                       && e.IsActive == true
                                       select e).ToList();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("ListAllReferrer")]
        public IActionResult ListAllReferrer()
        {
            //else if (reqType == "get-all-referrer-list")
            //{

            Func<object> func = () => (from e in _mstDBContext.Employees
                                       where e.IsActive == true
                                       && (e.IsExternal ? true : (e.IsAppointmentApplicable.HasValue && e.IsAppointmentApplicable == true))
                                       select e).ToList();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("BillingPackages")]
        public IActionResult BillingPackages()
        {
            // else if (reqType == "billing-packageList")
            //{

            Func<object> func = () => GetListOfBillingPackages();
            return InvokeHttpGetFunction<object>(func);
        }


        //[HttpGet]
        //[Route("GetLatestBillingInvoiceNo")]
        //public IActionResult GetLatestBillingInvoiceNo()
        //{
        //    //else if (reqType == "getLatestReceiptNo")
        //    //{
        //    Func<object> func = () => (from txn in _billingDbContext.BillingTransactions
        //                               select txn.InvoiceNo.Value).DefaultIfEmpty().Max();//.OrderByDescending(a => a).First();

        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("BillingFiscalYears")]
        public IActionResult BillingFiscalYears()
        {
            //else if (reqType == "all-fiscalYears")
            //{
            Func<object> func = () => _billingDbContext.BillingFiscalYears.ToList();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("CurrentFiscalYear")]
        public IActionResult CurrentFiscalYear()
        {
            //else if (reqType == "current-fiscalYear")
            //{
            Func<object> func = () => BillingBL.GetFiscalYear(connString);
            return InvokeHttpGetFunction<object>(func);
        }


        //[HttpGet]
        //[Route("GetPreviousHandoverAmounts")]
        //public IActionResult GetPreviousHandoverAmounts()
        //{
        //    //else if (reqType == "get-previous-amount")
        //    //{
        //    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
        //    Func<object> func = () => GetPreviousHandoverAmountsDetails(currentUser);
        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("PatientBillingContext")]
        public IActionResult PatientBillingContext(int patientId)
        {
            // else if (reqType == "patient-billing-context")
            //{
            Func<object> func = () => GetPatientBillingContext(patientId);
            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("HealthCardBillItem")]
        //public IActionResult HealthCardBillItem()
        //{
        //    // else if (reqType == "GetHealthCardBillItem")
        //    //{
        //    Func<object> func = () => (from billItem in _billingDbContext.BillServiceItems
        //                               join srvDept in _billingDbContext.ServiceDepartment on billItem.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //                               where billItem.ItemName == "Health Card"
        //                               select new
        //                               {
        //                                   ItemId = billItem.IntegrationItemId,
        //                                   ItemName = billItem.ItemName,
        //                                   ServiceDepartmentId = billItem.ServiceDepartmentId,
        //                                   ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //                                   Price = billItem.Price,
        //                                   TaxApplicable = billItem.IsTaxApplicable
        //                               }).FirstOrDefault();
        //    return InvokeHttpGetFunction<object>(func);
        //}

        [HttpGet]
        [Route("BillingTransactionByRequisitionId")]
        public IActionResult BillingTransactionByRequisitionId(int requisitionId, int patientId, string departmentName)
        {
            //else if (reqType == "billTxn-byRequisitioId")
            //{
            Func<object> func = () => GetBillingTransactionByRequisitionId(requisitionId, patientId, departmentName);
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("LabBillCfgItems")]
        public IActionResult LabBillCfgItems(string departmentName, string labType)
        {
            //else if (reqType == "department-items")
            //{
            //sud: 18Sept'18-- added isActive Clause in bill items..
            Func<object> func = () => (from item in _billingDbContext.BillServiceItems
                                       join srvDept in _billingDbContext.ServiceDepartment on item.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                                       join dept in _billingDbContext.Departments on srvDept.DepartmentId equals dept.DepartmentId
                                       join priceCatServItem in _billingDbContext.BillItemsPriceCategoryMaps on item.ServiceItemId equals priceCatServItem.ServiceItemId
                                       where item.IsActive == true && srvDept.IsActive == true && dept.IsActive == true && dept.DepartmentName.ToLower() == departmentName
                                       && priceCatServItem.PriceCategoryId == 1 //Krishna 13thMarch'23, 1 is hardcoded for now
                                       select new
                                       {
                                           BillItemPriceId = item.ServiceItemId,
                                           ServiceDepartmentId = srvDept.ServiceDepartmentId,
                                           ServiceDepartmentName = srvDept.ServiceDepartmentName,
                                           ServiceDepartmentShortName = srvDept.ServiceDepartmentShortName,
                                           Displayseq = item.DisplaySeq,
                                           ItemId = item.IntegrationItemId,
                                           ItemName = item.ItemName,
                                           //ProcedureCode = item.ProcedureCode,
                                           Price = priceCatServItem.Price,
                                           NormalPrice = priceCatServItem.Price,
                                           TaxApplicable = item.IsTaxApplicable,
                                           //DiscountApplicable = item.DiscountApplicable,
                                           Description = item.Description,
                                           IsDoctorMandatory = item.IsDoctorMandatory,//sud:5Feb'18--added for ward billing
                                           //InsForeignerPrice = item.InsForeignerPrice,
                                           //EHSPrice = item.EHSPrice,
                                           //SAARCCitizenPrice = item.SAARCCitizenPrice,
                                           //ForeignerPrice = item.ForeignerPrice,
                                           DefaultDoctorList = item.DefaultDoctorList//Pratik:23march'20--added for ward billing

                                       }).ToList().OrderBy(a => a.Displayseq);


            return InvokeHttpGetFunction<object>(func);
        }

        //[HttpGet]
        //[Route("AdmissionBillItems")]
        //public IActionResult AdmissionBillItems()
        //{
        //    // else if (reqType != null && reqType == "admission-bill-items")
        //    //{
        //    //below are hardcoded values from HAMS hospital, pls remove it soon.. <sud:20Dec'21>
        //    Func<object> func = () => (from bilItem in _billingDbContext.BillItemPrice
        //                               join servDept in _billingDbContext.ServiceDepartment on bilItem.ServiceDepartmentId equals servDept.ServiceDepartmentId
        //                               where bilItem.IntegrationName == "ADMISSION CHARGES (INDOOR)" || bilItem.IntegrationName == "Medical and Resident officer/Nursing Charges"
        //                               || bilItem.IntegrationName == "Medical Record Charge"
        //                               select new
        //                               {
        //                                   bilItem.ItemId,
        //                                   bilItem.ItemName,
        //                                   bilItem.Price,
        //                                   bilItem.TaxApplicable,
        //                                   bilItem.ServiceDepartmentId,
        //                                   servDept.ServiceDepartmentName,
        //                                   bilItem.ProcedureCode
        //                               }).ToList();
        //    return InvokeHttpGetFunction<object>(func);

        //}

        [HttpGet]
        [Route("CheckCreditBill")]
        public IActionResult CheckCreditBill(int patientId)
        {
            //else if (reqType == "check-credit-bill")
            //{
            Func<object> func = () => _billingDbContext.BillingTransactions
                                                        .Where(a => a.PatientId == patientId && a.BillStatus == "unpaid" && a.ReturnStatus != true)
                                                        .FirstOrDefault() != null ? true : false;

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("BillingCreditOrganizations")]
        public IActionResult BillingCreditOrganizations()
        {
            //else if (reqType == "get-credit-organization-list")
            //{
            Func<object> func = () => (from co in _billingDbContext.CreditOrganization
                                       where co.IsActive == true
                                       select co).ToList();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("ListUsers")]
        public IActionResult ListUsers()
        {
            // else if (reqType == "get-users-list")
            //{
            Func<object> func = () => (from emp in _billingDbContext.Employee
                                       join dep in _billingDbContext.Departments on emp.DepartmentId equals dep.DepartmentId
                                       //join user in rbacDbContext.Users on emp.EmployeeId equals user.EmployeeId
                                       where emp.IsActive == true
                                       //&& user.IsActive==true 
                                       select new
                                       {
                                           ShortName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
                                           UserId = emp.EmployeeId,
                                           //UserName=user.UserName,
                                           DepartmentName = dep.DepartmentName
                                       }).ToList();
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("Banks")]
        public IActionResult Banks()
        {
            //else if (reqType == "get-bank-list")
            //{

            Func<object> func = () => (from bank in _billingDbContext.Banks
                                       where bank.IsActive == true
                                       select bank).ToList();

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("EmployeeDueAmount")]
        public IActionResult GetEmployeeDueAmount()
        {
            //else if (reqType == "get-DueAmount")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            Func<object> func = () => BillingBL.GetEmpDueAmount(_billingDbContext, currentUser.EmployeeId); ;
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("PatientPastBillITxntems")]
        public IActionResult PatientPastBillITxntems(int patientId)
        {
            //else if (reqType == "past-test-list")
            //{

            Func<object> func = () => GetPatientPastBillITxntems(patientId);
            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("GetVisitInfoforStickerPrint")]
        public IActionResult GetVisitInfoforStickerPrint(int billingTransactionId)
        {
            //else if (reqType == "getVisitInfoforStickerPrint")
            //{

            List<SqlParameter> paramList = new List<SqlParameter>() { new SqlParameter("@BillingTransactionId", billingTransactionId) };
            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_Package_GetPatientVisitStickerInfo", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("HandoverTransactions")]
        public IActionResult HandoverTransactions()
        {
            //else if (reqType == "get-all-handover-transaction")
            //{
            Func<object> func = () => (from handover in _billingDbContext.HandoverTransaction
                                       join emp in _billingDbContext.Employee.Include("Department") on handover.HandoverByEmpId equals emp.EmployeeId
                                       join counter in _billingDbContext.BillingCounter on handover.CounterId equals counter.CounterId
                                       //join dep in dbContext.Departments on emp.DepartmentId equals dep.DepartmentId
                                       where handover.IsActive == true && handover.ReceivedById == null && handover.HandoverType == ENUM_HandOverType.Account
                                       select new
                                       {
                                           HandoverTxnId = handover.HandoverTxnId,
                                           HandoverByEmpId = handover.HandoverByEmpId,
                                           HandoverType = handover.HandoverType,
                                           BankName = handover.BankName,
                                           VoucherNumber = handover.VoucherNumber,
                                           VoucherDate = handover.VoucherDate,
                                           HandoverAmount = handover.HandoverAmount,
                                           DueAmount = handover.DueAmount,
                                           HandoverRemarks = handover.HandoverRemarks,
                                           CounterName = counter.CounterName,
                                           CounterId = counter.CounterId,
                                           UserId = emp.EmployeeId,
                                           UserName = emp.FullName,
                                           DepartmentId = emp.DepartmentId,
                                           DepartmentName = emp.Department != null ? emp.Department.DepartmentName : null,
                                           CreatedOn = handover.CreatedOn
                                       }).ToList().OrderBy(s => s.VoucherDate); ;

            return InvokeHttpGetFunction<object>(func);
        }

        [HttpGet]
        [Route("Report/HandoverReceive")]
        public IActionResult GetHandoverReceiveReport(string FromDate, string ToDate)
        {
            //else if (reqType == "get-handover-recive-report")
            //{
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                        new SqlParameter("@ToDate", ToDate)
                    };

            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_HandoverReceiveTransactionReport", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("Report/DailyCollectionVsHandover")]
        public IActionResult GetDailyCollectionVsHandoverReport(string FromDate, string ToDate)
        {
            //else if (reqType == "get-dailyCollection-vs-handover-report")
            //{
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                        new SqlParameter("@ToDate", ToDate)
                    };

            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_Report_DailyCollectionVsHandoverReport", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("Report/HandoverDetail")]
        public IActionResult GetHandoverDetailReport(string FromDate, string ToDate, int EmployeeId)
        {
            //else if (reqType == "get-handover-detail-report")
            //{
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                        new SqlParameter("@ToDate", ToDate),
                        new SqlParameter("@EmployeeId", EmployeeId)
                    };

            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_Report_HandoverDetailReport", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("Report/HandoverSummary")]
        public IActionResult GetHandoverSummaryReport(int fiscalYearId)
        {
            //else if (reqType == "get-handover-summary-report")
            //{
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FiscalYrId", fiscalYearId)
                    };
            Func<DataTable> func = () => DALFunctions.GetDataTableFromStoredProc("SP_Report_HandoverSummaryReport", paramList, _billingDbContext);
            return InvokeHttpGetFunction<DataTable>(func);
        }

        [HttpGet]
        [Route("InvoiceInfo")]
        public IActionResult GetInvoiceInfo(int invoiceNo, int fiscalYearId, int billingTransactionId)
        {
            //else if (reqType != null && reqType.ToLower() == "get-invoiceinfo-forprint")
            //{

            Func<object> func = () => GetBillingInvoiceInfoForPrint(invoiceNo, fiscalYearId, billingTransactionId);
            return InvokeHttpGetFunction<object>(func);
        }



        //[HttpGet]
        //public string Get(string reqType,
        //    int? InputId, int ProviderId,
        //    int EmployeeId,
        //    int serviceDeptId,
        //    int visitId,
        //    string serviceDeptName,
        //    int requisitionId,
        //    int patientId,
        //    string departmentName,
        //    string status,
        //    string FromDate,
        //    string ToDate,
        //    int? billingTransactionId,
        //    int? fiscalYrId,
        //    int? provReceiptNo,
        //    string visitType,
        //    bool getVisitInfo,
        //    int? patVisitId,
        //    bool isInsuranceReceipt,
        //    bool isInsurance,
        //    bool IsPatientAdmitted,
        //    string srvDeptIdListStr,
        //    string itemIdListStr,
        //    string search,
        //    int? invoiceNumber)
        //{
        //    DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
        //    RbacDbContext rbacDbContext = new RbacDbContext(connString);
        //    MasterDbContext masterDbContext = new MasterDbContext(connString);
        //    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
        //    int CreatedBy = ToInt(this.ReadQueryStringData("CreatedBy"));
        //    CoreDbContext coreDbContext = new CoreDbContext(connString);


        //    responseData.Status = "OK";//by default status would be OK, hence assigning at the top
        //    try
        //    {
        //        BillingDbContext dbContext = new BillingDbContext(connString);
        //        LabDbContext labDbContext = new LabDbContext(connString);
        //        RadiologyDbContext radiologyDbContext = new RadiologyDbContext(connString);
        //        #region //1.("allPatient")2.("Patientonly") 3.("Receipt") ------CREDIT VIEW--------
        //        #region //1. List out all the patient with the credit amount 

        //        if (reqType != null && reqType.ToLower() == "listpatientunpaidtotal")
        //        {
        //            //var allPatientCreditReceipts = (from bill in dbContext.BillingTransactionItems.Include("Patient")
        //            //                                where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional"
        //            //                                && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                                && (bill.VisitType == "outpatient" || bill.VisitType == "emergency")
        //            //                                //couldn't use Patient.ShortName directly since it's not mapped to DB and hence couldn't be used inside LINQ.
        //            //                                group bill by new { bill.PatientId, bill.Patient.PatientCode, bill.Patient.ShortName, bill.Patient.DateOfBirth, bill.Patient.Gender, bill.Patient.PhoneNumber } into p
        //            //                                select new
        //            //                                {
        //            //                                    PatientId = p.Key.PatientId,
        //            //                                    PatientCode = p.Key.PatientCode,
        //            //                                    ShortName = p.Key.ShortName,
        //            //                                    PhoneNumber = p.Key.PhoneNumber,
        //            //                                    Gender = p.Key.Gender,
        //            //                                    DateOfBirth = p.Max(a => a.Patient.DateOfBirth),
        //            //                                    LastCreditBillDate = p.Max(a => a.CreatedOn),
        //            //                                    TotalCredit = Math.Round(p.Sum(a => a.TotalAmount.Value), 2)
        //            //                                }).OrderByDescending(b => b.LastCreditBillDate).ToList();

        //            //responseData.Results = allPatientCreditReceipts;
        //        }
        //        #endregion


        //        //Getting unpaid insurance bill details
        //        else if (reqType != null && reqType.ToLower() == "listpatientinsuranceprovisional")
        //        {
        //            //    var allPatientInsuranceCreditReceipts = (from bill in dbContext.BillingTransactionItems.Include("Patient")
        //            //                                             where bill.BillStatus == ENUM_BillingStatus.provisional// "provisional"
        //            //                                             && bill.IsInsurance == true
        //            //                                             //couldn't use Patient.ShortName directly since it's not mapped to DB and hence couldn't be used inside LINQ.
        //            //                                             group bill by new { bill.PatientId, bill.Patient.PatientCode, bill.Patient.FirstName, bill.Patient.LastName, bill.Patient.MiddleName, bill.Patient.DateOfBirth, bill.Patient.Gender } into p
        //            //                                             select new
        //            //                                             {
        //            //                                                 PatientId = p.Key.PatientId,
        //            //                                                 PatientCode = p.Key.PatientCode,
        //            //                                                 ShortName = p.Key.FirstName + " " + (string.IsNullOrEmpty(p.Key.MiddleName) ? "" : p.Key.MiddleName + " ") + p.Key.LastName,
        //            //                                                 Gender = p.Key.Gender,
        //            //                                                 DateOfBirth = p.Max(a => a.Patient.DateOfBirth.Value),
        //            //                                                 LastCreditBillDate = p.Max(a => a.RequisitionDate.Value),
        //            //                                                 TotalCredit = Math.Round(p.Sum(a => a.TotalAmount.Value), 2)
        //            //                                             }).OrderByDescending(b => b.LastCreditBillDate).ToList();

        //            //    responseData.Results = allPatientInsuranceCreditReceipts;
        //        }

        //        

        //        

        //        //---Patient Membership---- firstly the membership Id is taken from that patient and after that discountpercent is taken from that id 

        //        else if (reqType != null && reqType == "getPatMembershipInfo" && InputId != null && InputId != 0)
        //        {
        //            //changed by sudarshan: we don't need 2 calls to get the membership detail.
        //            //also no need to join with the PatientMembershipFor now--
        //            //var currPatMembershipId = (from pat in dbContext.Patient
        //            //                           where pat.PatientId == InputId
        //            //                           select pat).FirstOrDefault().MembershipTypeId;
        //            //var patMembershipInfo = (from memb in dbContext.MembershipType
        //            //                         where memb.MembershipTypeId == currPatMembershipId
        //            //                         select new
        //            //                         {
        //            //                             PatientId = InputId,
        //            //                             MembershipTypeId = memb.MembershipTypeId,
        //            //                             DiscountPercent = memb.DiscountPercent,
        //            //                             MembershipTypeName = memb.MembershipTypeName
        //            //                         }).FirstOrDefault();

        //            //responseData.Status = "OK";
        //            //responseData.Results = patMembershipInfo;
        //        }

        //        //Getting MembershipType Lists --Yubraj 30th July '19
        //        else if (reqType != null && reqType == "membership-types")
        //        {
        //            //var membershipTypes = (from type in dbContext.MembershipType
        //            //                       where type.IsActive == true
        //            //                       select new
        //            //                       {
        //            //                           type.MembershipTypeId,
        //            //                           MembershipType = type.MembershipTypeName,
        //            //                           MembershipTypeName = type.MembershipTypeName + " (" + type.DiscountPercent + " % off)",
        //            //                           type.DiscountPercent
        //            //                       }).ToList();
        //            //responseData.Results = membershipTypes;
        //            //responseData.Status = "OK";
        //        }

        //        

        //        #region //3.if in patient level, show all receipts which are credit Insurance for this patient.
        //        else if (reqType != null && reqType == "insuranceprovisionalItemsByPatientId" && InputId != null && InputId != 0)
        //        {
        //            //PatientModel currPatient = dbContext.Patient.Where(pat => pat.PatientId == InputId).FirstOrDefault();
        //            //if (currPatient != null)
        //            //{
        //            //    string subDivName = (from pat in dbContext.Patient
        //            //                         join countrySubdiv in dbContext.CountrySubdivisions
        //            //                         on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
        //            //                         where pat.PatientId == currPatient.PatientId
        //            //                         select countrySubdiv.CountrySubDivisionName
        //            //                      ).FirstOrDefault();

        //            //    currPatient.CountrySubDivisionName = subDivName;

        //            //}
        //            //if (!isInsurance)
        //            //{
        //            //    //    isInsurance == false;
        //            //}

        //            ////for this request type, patientid comes as inputid.
        //            //var patCreditItems = (from bill in dbContext.BillingTransactionItems.Include("ServiceDepartment")
        //            //                      where bill.BillStatus == ENUM_BillingStatus.provisional //"provisional" 
        //            //                      && bill.PatientId == InputId
        //            //                      && bill.IsInsurance == true
        //            //                      select bill).ToList<BillingTransactionItemModel>().OrderBy(b => b.ServiceDepartmentId);

        //            ////clear patient object from Items, not needed since we're returning patient object separately
        //            //if (patCreditItems != null)
        //            //{

        //            //    var allEmployees = (from emp in dbContext.Employee
        //            //                        join dep in dbContext.Departments
        //            //                        on emp.DepartmentId equals dep.DepartmentId into empDpt
        //            //                        from emp2 in empDpt.DefaultIfEmpty()
        //            //                        select new
        //            //                        {
        //            //                            EmployeeId = emp.EmployeeId,
        //            //                            EmployeeName = emp.FirstName,
        //            //                            DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
        //            //                            DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
        //            //                        }).ToList();

        //            //    BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

        //            //    //remove relational property of BillingTransactionItem//sud: 12May'18
        //            //    //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
        //            //    foreach (BillingTransactionItemModel item in patCreditItems)
        //            //    {
        //            //        //item.ProvFiscalYear = "2075 / 76";
        //            //        item.ProvFiscalYear = fiscYear.FiscalYearFormatted;
        //            //        item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //            //                .Select(a => a.EmployeeName).FirstOrDefault();

        //            //        item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //            //             .Select(a => a.DepartmentName).FirstOrDefault();

        //            //        item.Patient = null;
        //            //    }
        //            //}

        //            ////create new anonymous type with patient information + Credit Items information : Anish:4May'18
        //            //var patCreditDetails = new
        //            //{
        //            //    Patient = currPatient,
        //            //    InsCreditItems = patCreditItems.OrderBy(itm => itm.CreatedOn).ToList()
        //            //};


        //            //responseData.Results = patCreditDetails;
        //        }
        //        #endregion
        //        #endregion
        //        else if (reqType != null && reqType == "provItemsByPatIdAndVisitId" && InputId != null && InputId != 0 && patVisitId != null && patVisitId != 0)
        //        {
        //            //PatientModel currPatient = dbContext.Patient.Where(pat => pat.PatientId == InputId).FirstOrDefault();
        //            //if (currPatient != null)
        //            //{
        //            //    string subDivName = (from countrySubdiv in dbContext.CountrySubdivisions
        //            //                         where countrySubdiv.CountrySubDivisionId == currPatient.CountrySubDivisionId
        //            //                         select countrySubdiv.CountrySubDivisionName
        //            //                      ).FirstOrDefault();

        //            //    currPatient.CountrySubDivisionName = subDivName;

        //            //}

        //            ////for this request type, patientid comes as inputid.
        //            //var patCreditItems = (from bill in dbContext.BillingTransactionItems.Include("ServiceDepartment")
        //            //                      where bill.BillStatus == ENUM_BillingStatus.provisional //"provisional" 
        //            //                      && bill.PatientId == InputId && bill.PatientVisitId == patVisitId
        //            //                      select bill).ToList<BillingTransactionItemModel>().OrderBy(b => b.ServiceDepartmentId);

        //            ////clear patient object from Items, not needed since we're returning patient object separately
        //            //if (patCreditItems != null)
        //            //{

        //            //    var allEmployees = (from emp in dbContext.Employee
        //            //                        join dep in dbContext.Departments
        //            //                        on emp.DepartmentId equals dep.DepartmentId into empDpt
        //            //                        from emp2 in empDpt.DefaultIfEmpty()
        //            //                        select new
        //            //                        {
        //            //                            EmployeeId = emp.EmployeeId,
        //            //                            EmployeeName = emp.FirstName,
        //            //                            DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
        //            //                            DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
        //            //                        }).ToList();

        //            //    //remove relational property of BillingTransactionItem//sud: 12May'18
        //            //    //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
        //            //    foreach (BillingTransactionItemModel item in patCreditItems)
        //            //    {
        //            //        item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //            //                .Select(a => a.EmployeeName).FirstOrDefault();

        //            //        item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //            //             .Select(a => a.DepartmentName).FirstOrDefault();

        //            //        item.Patient = null;
        //            //    }
        //            //}

        //            ////create new anonymous type with patient information + Credit Items information : Anish:4May'18
        //            //var patCreditDetails = new
        //            //{
        //            //    Patient = currPatient,
        //            //    CreditItems = patCreditItems.OrderBy(itm => itm.CreatedOn).ToList()
        //            //};


        //            //responseData.Results = patCreditDetails;
        //        }

        //        else if (reqType == "ProvisionalItemsInfoForPrint")
        //        {
        //            //var provReceiptInfo = (
        //            //      from pat in dbContext.Patient
        //            //      where pat.PatientId == patientId
        //            //      join cntrSub in dbContext.CountrySubdivisions
        //            //            on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
        //            //      select new
        //            //      {
        //            //          PatientId = pat.PatientId,
        //            //          PatientCode = pat.PatientCode,
        //            //          PatientName = pat.ShortName,
        //            //          ContactNo = pat.PhoneNumber,
        //            //          DateOfbirth = pat.DateOfBirth,
        //            //          Age = pat.Age,
        //            //          Gender = pat.Gender,
        //            //          CountrySubdivisionName = cntrSub.CountrySubDivisionName,
        //            //          Address = pat.Address,
        //            //          BillingUser = currentUser.UserName,
        //            //          ItemsList = (from bill in dbContext.BillingTransactionItems
        //            //                       join fy in dbContext.BillingFiscalYears on bill.ProvisionalFiscalYearId equals fy.FiscalYearId
        //            //                       where bill.BillStatus == ENUM_BillingStatus.provisional
        //            //                       //exclude insurance items if any.
        //            //                       && bill.PatientId == patientId && (bill.IsInsurance == null || bill.IsInsurance == false)
        //            //                       //if provisional receipt number is null then take all, else take specific receipt only by checking both fiscalYrId and ProvReceiptNo.
        //            //                       && (provReceiptNo == null ||
        //            //                            (bill.ProvisionalFiscalYearId == fiscalYrId && bill.ProvisionalReceiptNo == provReceiptNo))
        //            //                      // we're considering both ER and OPD as same for Provisional hence whenever emergency comes then we're changing it to outpatient.
        //            //                      && (visitType == null || bill.VisitType.Replace("emergency", "outpatient") == visitType)
        //            //                       select new
        //            //                       {
        //            //                           BillingTransactionItemId = bill.BillingTransactionItemId,
        //            //                           ServiceDepartmentName = bill.ServiceDepartmentName,
        //            //                           ServiceDepartmentId = bill.ServiceDepartmentId,
        //            //                           AssignedToDrName = bill.PerformerName,
        //            //                           FiscalYearFormatted = fy.FiscalYearFormatted,
        //            //                           ItemName = bill.ItemName,
        //            //                           Price = bill.Price,
        //            //                           Quantity = bill.Quantity,
        //            //                           SubTotal = bill.SubTotal,
        //            //                           TaxableAmount = bill.TaxableAmount,
        //            //                           DiscountAmount = bill.DiscountAmount,
        //            //                           TotalAmount = bill.TotalAmount,
        //            //                           CreatedOn = bill.CreatedOn,
        //            //                           ProvisionalReceiptNo = bill.ProvisionalReceiptNo,
        //            //                       })
        //            //      }).FirstOrDefault();


        //            //responseData.Results = provReceiptInfo;
        //            //responseData.Status = "OK";
        //        }

        //        else if (reqType == "InsuranceProvisionalItemsInfoForPrint")
        //        {
        //            //var provReceiptInfo = (
        //            //      from pat in dbContext.Patient
        //            //      where pat.PatientId == patientId
        //            //      join cntrSub in dbContext.CountrySubdivisions
        //            //            on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
        //            //      select new
        //            //      {
        //            //          PatientId = pat.PatientId,
        //            //          PatientCode = pat.PatientCode,
        //            //          PatientName = pat.ShortName,
        //            //          ContactNo = pat.PhoneNumber,
        //            //          DateOfbirth = pat.DateOfBirth,
        //            //          Age = pat.Age,
        //            //          Gender = pat.Gender,
        //            //          CountrySubdivisionName = cntrSub.CountrySubDivisionName,
        //            //          Address = pat.Address,
        //            //          BillingUser = currentUser.UserName,
        //            //          ItemsList = (from bill in dbContext.BillingTransactionItems
        //            //                       join fy in dbContext.BillingFiscalYears on bill.ProvisionalFiscalYearId equals fy.FiscalYearId
        //            //                       where bill.BillStatus == ENUM_BillingStatus.provisional
        //            //                       //exclude insurance items if any.
        //            //                       && bill.PatientId == patientId && (bill.IsInsurance != null || bill.IsInsurance == true)
        //            //                       //if provisional receipt number is null then take all, else take specific receipt only by checking both fiscalYrId and ProvReceiptNo.
        //            //                       && (provReceiptNo == null ||
        //            //                            (bill.ProvisionalFiscalYearId == fiscalYrId && bill.ProvisionalReceiptNo == provReceiptNo))
        //            //                      // we're considering both ER and OPD as same for Provisional hence whenever emergency comes then we're changing it to outpatient.
        //            //                      && (visitType == null || bill.VisitType.Replace("emergency", "outpatient") == visitType)
        //            //                       select new
        //            //                       {
        //            //                           BillingTransactionItemId = bill.BillingTransactionItemId,
        //            //                           ServiceDepartmentName = bill.ServiceDepartmentName,
        //            //                           ServiceDepartmentId = bill.ServiceDepartmentId,
        //            //                           AssignedToDrName = bill.PerformerName,
        //            //                           FiscalYearFormatted = fy.FiscalYearFormatted,
        //            //                           ItemName = bill.ItemName,
        //            //                           Price = bill.Price,
        //            //                           Quantity = bill.Quantity,
        //            //                           SubTotal = bill.SubTotal,
        //            //                           TaxableAmount = bill.TaxableAmount,
        //            //                           DiscountAmount = bill.DiscountAmount,
        //            //                           TotalAmount = bill.TotalAmount,
        //            //                           CreatedOn = bill.CreatedOn,
        //            //                           ProvisionalReceiptNo = bill.ProvisionalReceiptNo,
        //            //                       })
        //            //      }).FirstOrDefault();


        //            //responseData.Results = provReceiptInfo;
        //            //responseData.Status = "OK";
        //        }


        //        else if (reqType != null && reqType == "inPatProvItemsByPatIdAndVisitId" && InputId != null && InputId != 0 && patVisitId != null && patVisitId != 0)
        //        {
        //            //List<BillingTransactionItemVM> billItemVM = new List<BillingTransactionItemVM>();

        //            //PatientModel currPatient = dbContext.Patient.Where(pat => pat.PatientId == InputId).FirstOrDefault();
        //            //if (currPatient != null)
        //            //{
        //            //    string subDivName = (from pat in dbContext.Patient
        //            //                         join countrySubdiv in dbContext.CountrySubdivisions
        //            //                         on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
        //            //                         where pat.PatientId == currPatient.PatientId
        //            //                         select countrySubdiv.CountrySubDivisionName
        //            //                      ).FirstOrDefault();

        //            //    currPatient.CountrySubDivisionName = subDivName;

        //            //}

        //            ////for this request type, patientid comes as inputid.
        //            //var patCreditItems = (from bill in dbContext.BillingTransactionItems.Include("ServiceDepartment")
        //            //                      where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional"
        //            //                      && bill.PatientId == InputId && bill.PatientVisitId == patVisitId
        //            //                      select bill).ToList<BillingTransactionItemModel>().OrderBy(b => b.ServiceDepartmentId);


        //            ////clear patient object from Items, not needed since we're returning patient object separately
        //            //if (patCreditItems != null)
        //            //{

        //            //    var allEmployees = (from emp in dbContext.Employee
        //            //                        join dep in dbContext.Departments
        //            //                        on emp.DepartmentId equals dep.DepartmentId into empDpt
        //            //                        from emp2 in empDpt.DefaultIfEmpty()
        //            //                        select new
        //            //                        {
        //            //                            EmployeeId = emp.EmployeeId,
        //            //                            EmployeeName = emp.FirstName,
        //            //                            DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
        //            //                            DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
        //            //                        }).ToList();

        //            //    //remove relational property of BillingTransactionItem//sud: 12May'18
        //            //    //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
        //            //    foreach (BillingTransactionItemModel item in patCreditItems)
        //            //    {
        //            //        var blItm = new BillingTransactionItemVM();

        //            //        item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //            //                .Select(a => a.EmployeeName).FirstOrDefault();

        //            //        item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //            //             .Select(a => a.DepartmentName).FirstOrDefault();

        //            //        item.Patient = null;



        //            //        blItm.BillingTransactionItemId = item.BillingTransactionItemId;
        //            //        blItm.BillingTransactionId = item.BillingTransactionId;
        //            //        blItm.PatientId = item.PatientId;
        //            //        blItm.ProviderId = item.PerformerId;
        //            //        blItm.ProviderName = item.PerformerName;
        //            //        blItm.ServiceDepartmentId = item.ServiceDepartmentId;
        //            //        blItm.ServiceDepartmentName = item.ServiceDepartmentName;
        //            //        blItm.ProcedureCode = item.ProcedureCode;
        //            //        blItm.ItemId = item.ItemId;
        //            //        blItm.ItemName = item.ItemName;
        //            //        blItm.Price = item.Price;
        //            //        blItm.Quantity = item.Quantity;
        //            //        blItm.SubTotal = item.SubTotal;
        //            //        blItm.DiscountPercent = item.DiscountPercent;
        //            //        blItm.DiscountPercentAgg = item.DiscountPercentAgg;
        //            //        blItm.DiscountAmount = item.DiscountAmount;
        //            //        blItm.Tax = item.Tax;
        //            //        blItm.TotalAmount = item.TotalAmount;
        //            //        blItm.BillStatus = item.BillStatus;
        //            //        blItm.RequisitionId = item.RequisitionId;
        //            //        blItm.RequisitionDate = item.RequisitionDate;
        //            //        blItm.CounterDay = item.CounterDay;
        //            //        blItm.CounterId = item.CounterId;
        //            //        blItm.PaidDate = item.PaidDate;
        //            //        blItm.ReturnStatus = item.ReturnStatus;
        //            //        blItm.ReturnQuantity = item.ReturnQuantity;
        //            //        blItm.CreatedBy = item.CreatedBy;
        //            //        blItm.CreatedOn = item.CreatedOn;
        //            //        blItm.Remarks = item.Remarks;
        //            //        blItm.CancelRemarks = item.CancelRemarks;
        //            //        blItm.TaxPercent = item.TaxPercent;
        //            //        blItm.CancelledOn = item.CancelledOn;
        //            //        blItm.CancelledBy = item.CancelledBy;
        //            //        blItm.RequestedBy = item.PrescriberId;
        //            //        blItm.PatientVisitId = item.PatientVisitId;
        //            //        blItm.BillingPackageId = item.BillingPackageId;
        //            //        blItm.TaxableAmount = item.TaxableAmount;
        //            //        blItm.NonTaxableAmount = item.NonTaxableAmount;
        //            //        blItm.PaymentReceivedBy = item.PaymentReceivedBy;
        //            //        blItm.PaidCounterId = item.PaidCounterId;
        //            //        blItm.BillingType = item.BillingType;
        //            //        blItm.RequestingDeptId = item.RequestingDeptId;
        //            //        blItm.IsTaxApplicable = item.IsTaxApplicable;
        //            //        blItm.Patient = item.Patient;
        //            //        blItm.BillingTransaction = item.BillingTransaction;
        //            //        blItm.ServiceDepartment = item.ServiceDepartment;
        //            //        blItm.VisitType = item.VisitType;
        //            //        blItm.RequestingUserName = item.RequestingUserName;
        //            //        blItm.RequestingUserDept = item.RequestingUserDept;

        //            //        //Added by Anish: Oct 10- In case Of lab User should not be able to cancel the LabItem whose Report is already Generated
        //            //        if (item.ServiceDepartment.IntegrationName != null && item.ServiceDepartment.IntegrationName.ToLower() == "lab")
        //            //        {
        //            //            blItm.AllowCancellation = !(
        //            //                                  (from cmp in labDbContext.LabTestComponentResults
        //            //                                   where cmp.RequisitionId == item.RequisitionId
        //            //                                  && cmp.LabReportId.HasValue
        //            //                                   select cmp).ToList().Count > 0
        //            //                               );
        //            //        }
        //            //        else if (item.ServiceDepartment.IntegrationName != null && item.ServiceDepartment.IntegrationName.ToLower() == "radiology")
        //            //        {
        //            //            blItm.AllowCancellation = !(
        //            //                                  (from req in radiologyDbContext.ImagingRequisitions
        //            //                                   where req.ImagingRequisitionId == item.RequisitionId
        //            //                                  && req.OrderStatus == "final"
        //            //                                   select req).ToList().Count > 0
        //            //                               );
        //            //        }
        //            //        else
        //            //        {
        //            //            blItm.AllowCancellation = true;
        //            //        }

        //            //        billItemVM.Add(blItm);

        //            //    }
        //            //}

        //            ////create new anonymous type with patient information + Credit Items information : Anish:4May'18
        //            //var patCreditDetails = new
        //            //{
        //            //    Patient = currPatient,
        //            //    CreditItems = billItemVM.OrderByDescending(itm => itm.CreatedOn).ToList()
        //            //};


        //            //responseData.Results = patCreditDetails;
        //        }
        //        //to get Inpatient order list using SP
        //        else if (reqType != null && reqType == "inPatientProvisionalItemList")
        //        {
        //            //string module = this.ReadQueryStringData("module");

        //            //PatientModel currPatient = dbContext.Patient.Where(pat => pat.PatientId == patientId).FirstOrDefault();
        //            //if (currPatient != null)
        //            //{
        //            //    string subDivName = (from pat in dbContext.Patient
        //            //                         join countrySubdiv in dbContext.CountrySubdivisions
        //            //                         on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
        //            //                         where pat.PatientId == currPatient.PatientId
        //            //                         select countrySubdiv.CountrySubDivisionName
        //            //                      ).FirstOrDefault();

        //            //    currPatient.CountrySubDivisionName = subDivName;
        //            //    //remove relational property of patient//sud: 12May'18
        //            //    //currPatient.BillingTransactionItems = null;
        //            //}

        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@patientId", patientId),
        //            //    new SqlParameter("@patientVisitId", visitId),
        //            //    new SqlParameter("@moduleName", module)
        //            //};

        //            //DataTable patCreditItems = DALFunctions.GetDataTableFromStoredProc("SP_InPatient_Item_Details", paramList, dbContext);


        //            ////create new anonymous type with patient information + Credit Items information : Anish:4May'18
        //            //var patCreditDetails = new
        //            //{
        //            //    Patient = currPatient,
        //            //    BillItems = patCreditItems
        //            //};
        //            //responseData.Status = "OK";
        //            //responseData.Results = patCreditDetails;
        //        }
        //        // to list out the invoice details for duplicate printing
        //        else if (reqType != null && reqType.ToLower() == "listinvoicewisebill")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FromDate", FromDate),
        //            //    new SqlParameter("@ToDate", ToDate)
        //            //};

        //            //DataTable dtBilInvoiceDetails = DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetBillingInvoicesBetweenDateRange", paramList, dbContext);

        //            //responseData.Results = dtBilInvoiceDetails;

        //        }

        //        // to list out the invoice return details for duplicate printing
        //        else if (reqType != null && reqType.ToLower() == "credit-note-list")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FromDate", FromDate),
        //            //    new SqlParameter("@ToDate", ToDate)
        //            //};

        //            //DataTable dtBilInvoiceReturnDetails = DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetCreditNoteListBetweenDateRange", paramList, dbContext);

        //            //responseData.Results = dtBilInvoiceReturnDetails;

        //        }

        //        // to list out the Provisional details for duplicate printing
        //        else if (reqType != null && reqType.ToLower() == "listprovisionalwisebill")
        //        {
        //            //var provListForDuplicate = (
        //            //      from pat in dbContext.Patient
        //            //      join cntrSub in dbContext.CountrySubdivisions
        //            //            on pat.CountrySubDivisionId equals cntrSub.CountrySubDivisionId
        //            //      join bill in dbContext.BillingTransactionItems
        //            //            on pat.PatientId equals bill.PatientId
        //            //      where bill.BillStatus == ENUM_BillingStatus.provisional
        //            //      //exclude insurance provisionals
        //            //      && (bill.IsInsurance == null || bill.IsInsurance == false)
        //            //      group bill by new
        //            //      {
        //            //          bill.PatientId,
        //            //          pat.PatientCode,
        //            //          pat.ShortName,
        //            //          pat.PhoneNumber,
        //            //          pat.DateOfBirth,
        //            //          pat.Gender,
        //            //          cntrSub.CountrySubDivisionName,
        //            //          pat.Address,
        //            //      } into grp

        //            //      select new
        //            //      {
        //            //          PatientId = grp.Key.PatientId,
        //            //          PatientCode = grp.Key.PatientCode,
        //            //          PatientName = grp.Key.ShortName,
        //            //          PhoneNumber = grp.Key.PhoneNumber,
        //            //          DateOfbirth = grp.Key.DateOfBirth,
        //            //          Gender = grp.Key.Gender,
        //            //          CountrySubdivisionName = grp.Key.CountrySubDivisionName,
        //            //          Address = grp.Key.Address,
        //            //          LastBillDate = grp.Max(a => a.CreatedOn),
        //            //          TotalAmount = grp.Sum(a => a.TotalAmount)
        //            //      }).OrderByDescending(a => a.LastBillDate).ToList();



        //            //responseData.Results = provListForDuplicate;
        //            //responseData.Status = "OK";
        //        }
        //        else if (reqType != null && reqType == "duplicateBillsByReceiptId" && InputId != null && InputId != 0)
        //        {
        //            //var receipt = (from bill in dbContext.BillingTransactions.Include("BillingTransactionItems")
        //            //               join pat in dbContext.Patient on bill.PatientId equals pat.PatientId
        //            //               join fy in dbContext.BillingFiscalYears on bill.FiscalYearId equals fy.FiscalYearId
        //            //               join user in dbContext.User on bill.CreatedBy equals user.EmployeeId
        //            //               join cntrSbDvsn in dbContext.CountrySubdivisions on pat.CountrySubDivisionId equals cntrSbDvsn.CountrySubDivisionId
        //            //               where bill.InvoiceNo == InputId && bill.FiscalYearId == fiscalYrId
        //            //               select new
        //            //               {
        //            //                   Patient = pat,
        //            //                   Transaction = bill,
        //            //                   TransactionItems = bill.BillingTransactionItems,
        //            //                   FiscalYearObj = fy,
        //            //                   UserObject = user,
        //            //                   CountrySubDivObj = cntrSbDvsn
        //            //                   //.Include("BillingTransactionItems").Include("Patient")
        //            //               }).FirstOrDefault();


        //            //if (receipt != null)
        //            //{

        //            //    //Yubraj :: 22nd April '19 For finding creditOrganizationName to show in duplicate print
        //            //    if (receipt.Transaction != null)
        //            //    {
        //            //        receipt.Transaction.OrganizationName = dbContext.CreditOrganization.Where(a => a.OrganizationId == receipt.Transaction.OrganizationId).Select(b => b.OrganizationName).FirstOrDefault();
        //            //    }

        //            //    //sud:4May'21--Set BillingTransactionObject and Patient Object of TxnItems as null.
        //            //    //those objects gets automatically assigned during the join above.
        //            //    //don't need them since we're separately taking those objects below.
        //            //    if (receipt.TransactionItems != null && receipt.TransactionItems.Count > 0)
        //            //    {
        //            //        receipt.TransactionItems.ForEach(txnItm =>
        //            //        {
        //            //            txnItm.BillingTransaction = null;
        //            //            txnItm.Patient = null;
        //            //        });
        //            //    }





        //            //    string userName = receipt.UserObject != null ? receipt.UserObject.UserName : "";
        //            //    receipt.Transaction.FiscalYear = receipt.FiscalYearObj != null ? receipt.FiscalYearObj.FiscalYearFormatted : "";
        //            //    receipt.Patient.CountrySubDivisionName = receipt.CountrySubDivObj != null ? receipt.CountrySubDivObj.CountrySubDivisionName : "";

        //            //    //set navigational properties of Patient, BillingTransaction and BillingTransactioItems to null.
        //            //    //mandatory, otherwise the response size will be too big.
        //            //    receipt.Transaction.Patient = null;
        //            //    receipt.Transaction.BillingTransactionItems = null;



        //            //    //ashim:29Sep2018 rename this as patADTInfo:
        //            //    var patVisitInfo = new object();
        //            //    if (getVisitInfo)
        //            //    {
        //            //        AdmissionDbContext admissionDbContext = new AdmissionDbContext(connString);
        //            //        patVisitInfo = (from admission in admissionDbContext.Admissions
        //            //                        where admission.PatientId == receipt.Patient.PatientId
        //            //                        select new
        //            //                        {
        //            //                            LastAdmissionDate = admission.AdmissionDate,
        //            //                            LastDischargedDate = admission.DischargeDate
        //            //                        }).OrderByDescending(a => a.LastAdmissionDate).FirstOrDefault();
        //            //    }

        //            //    //ashim: 29Sep2018 : To get patient's latest visit info incase of copy from earlier receipt in return receipt page.
        //            //    var patientLatestVisitInfo = new object();
        //            //    if (getVisitInfo)
        //            //    {
        //            //        patientLatestVisitInfo = (from visit in dbContext.Visit
        //            //                                  where visit.PatientId == receipt.Patient.PatientId
        //            //                                  select new
        //            //                                  {
        //            //                                      LatestVisitType = visit.VisitType,
        //            //                                      LatestVisitId = visit.PatientVisitId,
        //            //                                      LatestVisitCode = visit.VisitCode,
        //            //                                      QueueNo = visit.QueueNo
        //            //                                  }).OrderByDescending(a => a.LatestVisitId).FirstOrDefault();
        //            //    }

        //            //    //Returning new anonymous type. 
        //            //    responseData.Results = new
        //            //    {
        //            //        Patient = receipt.Patient,
        //            //        Transaction = receipt.Transaction,
        //            //        TransactionItems = receipt.TransactionItems,
        //            //        UserName = userName,
        //            //        //ashim: 29Sep2018: Rename this as AdmissionInfo
        //            //        VisitInfo = patVisitInfo,
        //            //        //ashim: 29Sep2018 : To get patient's latest visit info incase of copy from earlier receipt in return receipt page.
        //            //        LatestPatientVisitInfo = patientLatestVisitInfo
        //            //    };

        //            //}

        //        }

        //        else if (reqType == "InPatientDetailForPartialBilling")
        //        {
        //            //var visitNAdmission = (from visit in dbContext.Visit.Include(v => v.Admission)
        //            //                       where visit.PatientVisitId == patVisitId
        //            //                       select visit).FirstOrDefault();

        //            //var patientDetail = (from pat in dbContext.Patient
        //            //                     join sub in dbContext.CountrySubdivisions on pat.CountrySubDivisionId equals sub.CountrySubDivisionId
        //            //                     where pat.PatientId == visitNAdmission.PatientId
        //            //                     select new PatientDetailVM
        //            //                     {
        //            //                         PatientId = pat.PatientId,
        //            //                         PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
        //            //                         HospitalNo = pat.PatientCode,
        //            //                         DateOfBirth = pat.DateOfBirth,
        //            //                         Gender = pat.Gender,
        //            //                         Address = pat.Address,
        //            //                         ContactNo = pat.PhoneNumber,
        //            //                         InpatientNo = visitNAdmission.VisitCode,
        //            //                         CountrySubDivision = sub.CountrySubDivisionName,
        //            //                         PANNumber = pat.PANNumber
        //            //                     }).FirstOrDefault();

        //            //responseData.Results = patientDetail;
        //            //responseData.Status = "OK";
        //        }

        //        else if (reqType == "getCancelItems")
        //        {
        //            //var results = (from pat in dbContext.Patient
        //            //               join countrysub in dbContext.CountrySubdivisions on pat.CountrySubDivisionId equals countrysub.CountrySubDivisionId
        //            //               where pat.PatientId == InputId
        //            //               select new
        //            //               {
        //            //                   PatientCode = pat.PatientCode,
        //            //                   ShortName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName) + " " + pat.LastName,
        //            //                   PhoneNumber = pat.PhoneNumber,
        //            //                   Address = pat.Address,
        //            //                   CountrySubDivisionName = countrysub.CountrySubDivisionName,
        //            //                   DateOfBirth = pat.DateOfBirth,
        //            //                   Gender = pat.Gender

        //            //               }).FirstOrDefault();
        //            //responseData.Results = results;
        //            //responseData.Status = "OK";
        //        }

        //        //Provisional Duplicate invoices from ReceiptNo and Fiscal Year --7th June
        //        else if (reqType != null && reqType == "duplicateProvisionalBillsByReceiptId" && InputId != null && InputId != 0)
        //        {
        //            //BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

        //            //var patId = (from itm in dbContext.BillingTransactionItems
        //            //             where itm.ProvisionalReceiptNo == InputId
        //            //             && itm.ProvisionalFiscalYearId == fiscalYrId
        //            //             select itm.PatientId).FirstOrDefault();

        //            //var patientInfo = (from pat in dbContext.Patient where pat.PatientId == patId select pat).FirstOrDefault();
        //            //var receipt = (from bill in dbContext.BillingTransactionItems
        //            //               where bill.ProvisionalReceiptNo == InputId && bill.ProvisionalFiscalYearId == fiscalYrId
        //            //               //&& bill.IsInsuranceBilling == isInsuranceReceipt
        //            //               select bill).ToList();

        //            ////if (receipt.Transaction != null)
        //            ////{
        //            ////    receipt.Transaction.OrganizationName = dbContext.CreditOrganization.Where(a => a.OrganizationId == receipt.Transaction.OrganizationId).Select(b => b.OrganizationName).FirstOrDefault();
        //            ////}
        //            //var CountrySubDivisionName = (from sub in dbContext.CountrySubdivisions where sub.CountrySubDivisionId == patientInfo.CountrySubDivisionId select sub.CountrySubDivisionName).FirstOrDefault();
        //            //patientInfo.CountrySubDivisionName = CountrySubDivisionName;
        //            ////Returning new anonymous type. 
        //            //responseData.Results = new
        //            //{
        //            //    FiscalYear = fiscYear.FiscalYearFormatted,
        //            //    ReceiptNo = InputId,
        //            //    Patient = patientInfo,
        //            //    Transaction = receipt
        //            //};
        //        }

        //        else if (reqType == "returned-patient-invoices")
        //        {
        //            //var invoices = (from bill in dbContext.BillingTransactions.Include("BillingTransactionItems").Include("Patient")
        //            //                join fiscalYear in dbContext.BillingFiscalYears on bill.FiscalYearId equals fiscalYear.FiscalYearId
        //            //                where bill.ReturnStatus == true && bill.PatientId == patientId
        //            //                select new
        //            //                {
        //            //                    bill.BillingTransactionId,
        //            //                    bill.PatientId,
        //            //                    bill.PatientVisitId,
        //            //                    bill.PaymentMode,
        //            //                    bill.PaymentDetails,
        //            //                    bill.DiscountPercent,
        //            //                    bill.Remarks,
        //            //                    InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + bill.InvoiceCode + bill.InvoiceNo.ToString(),
        //            //                    bill.TotalAmount,
        //            //                    bill.BillingTransactionItems,
        //            //                    bill.CreatedOn,
        //            //                    bill.PackageId,
        //            //                    bill.PackageName,
        //            //                    bill.TransactionType,
        //            //                    bill.LabTypeName
        //            //                }).OrderByDescending(a => a.CreatedOn).Take(5).ToList();

        //            ////if any of invoices contains OPD items, then remove it: sud:7Aug'18--needed since OPD are billed only from VISIT MODULE, not Billing.. 
        //            //if (invoices != null && invoices.Count > 0)
        //            //{

        //            //    invoices.RemoveAll(inv => inv.BillingTransactionItems.Where(itm => inv.PackageId == null && itm.ServiceDepartmentName == "OPD").Count() > 0);
        //            //}

        //            //responseData.Results = invoices;
        //        }

        //        //This block is for NormalProvisionalBilling 
        //        else if (reqType != null && reqType == "patientPastBillSummary" && InputId != null && InputId != 0)
        //        {
        //            //Part-1: Get Deposit Balance of this patient. 
        //            //get all deposit related transactions of this patient. and sum them acc to DepositType groups.
        //            //var patientAllDepositTxns = (from bill in dbContext.BillingDeposits
        //            //                             where bill.PatientId == InputId && bill.IsActive == true//here PatientId comes as InputId from client.
        //            //                             group bill by new { bill.PatientId, bill.DepositType } into p
        //            //                             select new
        //            //                             {
        //            //                                 DepositType = p.Key.DepositType,
        //            //                                 SumAmount = p.Sum(a => a.Amount)
        //            //                             }).ToList();
        //            ////separate sum of each deposit types and calculate deposit balance.
        //            //double? totalDepositAmt, totalDepositDeductAmt, totalDepositReturnAmt, currentDepositBalance;
        //            //currentDepositBalance = totalDepositAmt = totalDepositDeductAmt = totalDepositReturnAmt = 0;

        //            //if (patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "deposit").FirstOrDefault() != null)
        //            //{
        //            //    totalDepositAmt = patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "deposit").FirstOrDefault().SumAmount;
        //            //}
        //            //if (patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "depositdeduct").FirstOrDefault() != null)
        //            //{
        //            //    totalDepositDeductAmt = patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "depositdeduct").FirstOrDefault().SumAmount;
        //            //}
        //            //if (patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "returndeposit").FirstOrDefault() != null)
        //            //{
        //            //    totalDepositReturnAmt = patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "returndeposit").FirstOrDefault().SumAmount;
        //            //}
        //            ////below is the formula to calculate deposit balance.
        //            //currentDepositBalance = totalDepositAmt - totalDepositDeductAmt - totalDepositReturnAmt;

        //            ////Part-2: Get Total Provisional Items
        //            ////for this request type, patientid comes as inputid.
        //            //var patProvisional = (from bill in dbContext.BillingTransactionItems
        //            //                          //sud: 4May'18 changed unpaid to provisional
        //            //                      where bill.PatientId == InputId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
        //            //                      && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                      group bill by new { bill.PatientId } into p
        //            //                      select new
        //            //                      {
        //            //                          TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
        //            //                      }).FirstOrDefault();

        //            //var patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;



        //            ////Part-3: Return a single object with Both Balances (Deposit and Credit).
        //            ////exclude returned invoices from credit total
        //            ////var patCredits = dbContext.BillingTransactions
        //            ////                .Where(b => b.PatientId == InputId && b.BillStatus == "unpaid" && b.ReturnStatus == false && b.IsInsuranceBilling == false)
        //            ////                 .Sum(b => b.TotalAmount);

        //            ////double patCreditAmt = patCredits != null ? patCredits.Value : 0;

        //            ////var patCredits = (from bill in dbContext.BillingTransactionItems
        //            ////                  where bill.PatientId == InputId
        //            ////                  && bill.BillStatus == ENUM_BillingStatus.unpaid// "unpaid"
        //            ////                  //&& (bill.ReturnStatus == false || bill.ReturnStatus == null)
        //            ////                  && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            ////                  join ret in dbContext.BillInvoiceReturnItems 
        //            ////                  on bill.BillingTransactionItemId equals ret.BillingTransactionItemId into g
        //            ////                  from returnItems in g.DefaultIfEmpty()
        //            ////                  group bill by new { bill.PatientId } into p
        //            ////                  select new
        //            ////                  {
        //            ////                      TotalUnPaidAmt = p.Sum(a => a.TotalAmount)
        //            ////                  }).FirstOrDefault();


        //            //var patCredits = (from bill in dbContext.BillingTransactionItems
        //            //                  join retItems in dbContext.BillInvoiceReturnItems
        //            //                  on bill.BillingTransactionItemId equals retItems.BillingTransactionItemId into g
        //            //                  from items in g.DefaultIfEmpty()
        //            //                  where bill.PatientId == InputId
        //            //                  && bill.BillStatus == ENUM_BillingStatus.unpaid
        //            //                  && (bill.IsInsurance == false || bill.IsInsurance == null)

        //            //                  select new
        //            //                  {
        //            //                      TotalAmount = bill.TotalAmount.HasValue ? bill.TotalAmount : 0,
        //            //                      TotalReturnAmount = items.RetTotalAmount.HasValue ? items.RetTotalAmount : 0
        //            //                  }
        //            //                  );

        //            ////var patCreditAmt = patCredits != null ? patCredits.TotalUnPaidAmt : 0;
        //            //var patCreditAmt = patCredits != null ? patCredits.Sum(a => a.TotalAmount) - patCredits.Sum(b => b.TotalReturnAmount) : 0;

        //            ////Part-4: Get Total Paid Amount
        //            //var patPaid = (from bill in dbContext.BillingTransactionItems
        //            //               where bill.PatientId == InputId
        //            //               && bill.BillStatus == ENUM_BillingStatus.paid // "paid"
        //            //               && (bill.ReturnStatus == false || bill.ReturnStatus == null)
        //            //               && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //               group bill by new { bill.PatientId } into p
        //            //               select new
        //            //               {
        //            //                   TotalPaidAmt = p.Sum(a => a.TotalAmount)
        //            //               }).FirstOrDefault();

        //            //var patPaidAmt = patPaid != null ? patPaid.TotalPaidAmt : 0;

        //            ////Part-5: get Total Discount Amount
        //            ////var patDiscount = dbContext.BillingTransactionItems
        //            ////                .Where(b => b.PatientId == InputId && b.BillStatus == "unpaid" && b.ReturnStatus == false && b.IsInsurance == false)
        //            ////                 .Sum(b => b.DiscountAmount);

        //            ////double patDiscountAmt = patDiscount != null ? patDiscount.Value : 0;

        //            ////var patDiscount = (from bill in dbContext.BillingTransactionItems
        //            ////                   where bill.PatientId == InputId
        //            ////                   && bill.BillStatus == ENUM_BillingStatus.unpaid// "unpaid"
        //            ////                   && (bill.ReturnStatus == false || bill.ReturnStatus == null)
        //            ////                   && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            ////                   group bill by new { bill.PatientId } into p
        //            ////                   select new
        //            ////                   {
        //            ////                       TotalDiscountAmt = p.Sum(a => a.DiscountAmount)
        //            ////                   }).FirstOrDefault();


        //            //var patDiscount = (from bill in dbContext.BillingTransactionItems
        //            //                   join retItems in dbContext.BillInvoiceReturnItems
        //            //                   on bill.BillingTransactionItemId equals retItems.BillingTransactionItemId into g
        //            //                   from items in g.DefaultIfEmpty()
        //            //                   where bill.PatientId == InputId
        //            //                   //&& bill.BillStatus == ENUM_BillingStatus.unpaid
        //            //                   && (bill.IsInsurance == false || bill.IsInsurance == null)

        //            //                   select new
        //            //                   {
        //            //                       TotalDiscountAmount = bill.DiscountAmount,
        //            //                       TotalReturnDiscountAmount = items.RetDiscountAmount
        //            //                   }
        //            //                  );

        //            //var patDiscountAmt = patDiscount != null ? patDiscount.Sum(a => a.TotalDiscountAmount) - patDiscount.Sum(b => b.TotalReturnDiscountAmount) : 0;

        //            ////Part-6: get Total Cancelled Amount
        //            //var patCancel = (from bill in dbContext.BillingTransactionItems
        //            //                     //sud: 4May'18 changed unpaid to provisional
        //            //                 where bill.PatientId == InputId
        //            //                 && bill.BillStatus == ENUM_BillingStatus.cancel// "cancel"
        //            //                 && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                 group bill by new { bill.PatientId } into p
        //            //                 select new
        //            //                 {
        //            //                     TotalPaidAmt = p.Sum(a => a.TotalAmount)
        //            //                 }).FirstOrDefault();

        //            //var patCancelAmt = patCancel != null ? patCancel.TotalPaidAmt : 0;

        //            ////Part-7: get Total Cancelled Amount
        //            ////var patReturn = dbContext.BillingTransactionItems
        //            ////                .Where(b => b.PatientId == InputId && b.ReturnStatus == true) //&& (b.BillStatus == "paid" || b.BillStatus == "unpaid") && b.IsInsurance == false)
        //            ////                 .Sum(b => b.TotalAmount);

        //            ////var patReturn = (from bill in dbContext.BillingTransactionItems
        //            ////                 where bill.PatientId == InputId
        //            ////                 && bill.ReturnStatus == true
        //            ////                 && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            ////                 group bill by new { bill.PatientId } into p
        //            ////                 select new
        //            ////                 {
        //            ////                     TotalPaidAmt = p.Sum(a => a.TotalAmount)
        //            ////                 }).FirstOrDefault();

        //            //var patReturn = (from rtnItems in dbContext.BillInvoiceReturnItems
        //            //                 where rtnItems.PatientId == InputId
        //            //                 && (rtnItems.IsInsurance == false || rtnItems.IsInsurance == null)
        //            //                 && (rtnItems.BillStatus == ENUM_BillingStatus.paid) //shankar
        //            //                 group rtnItems by new { rtnItems.PatientId } into p
        //            //                 select new
        //            //                 {
        //            //                     TotalAmt = p.Sum(a => a.RetTotalAmount)
        //            //                 }).FirstOrDefault();

        //            //var patReturnAmt = patReturn != null ? patReturn.TotalAmt : 0;


        //            //var patDepositRefund = dbContext.BillingDeposits.Where(a => a.DepositType == ENUM_BillDepositType.ReturnDeposit && a.PatientId == InputId).Sum(b => b.Amount);

        //            ////Part-7: Return a single object with Both Balances (Deposit and Credit).
        //            //var patBillHistory = new
        //            //{
        //            //    PatientId = InputId,
        //            //    PaidAmount = patPaidAmt,
        //            //    DiscountAmount = patDiscountAmt,
        //            //    CancelAmount = patCancelAmt,
        //            //    ReturnedAmount = patReturnAmt,
        //            //    CreditAmount = patCreditAmt,
        //            //    ProvisionalAmt = patProvisionalAmt,
        //            //    TotalDue = patCreditAmt + patProvisionalAmt,
        //            //    DepositBalance = currentDepositBalance,
        //            //    BalanceAmount = currentDepositBalance - (patCreditAmt + patProvisionalAmt),
        //            //    RefundAmount = patDepositRefund != null ? patDepositRefund : 0
        //            //};


        //            //responseData.Results = patBillHistory;
        //        }

        //        else if (reqType != null && reqType == "patientPastBillSummaryForBillSettlements" && InputId != null && InputId != 0)
        //        {
        //            //Part-1: Get Deposit Balance of this patient. 
        //            //get all deposit related transactions of this patient. and sum them acc to DepositType groups.
        //            //var patientAllDepositTxns = (from bill in dbContext.BillingDeposits
        //            //                             where bill.PatientId == InputId && bill.IsActive == true//here PatientId comes as InputId from client.
        //            //                             group bill by new { bill.PatientId, bill.DepositType } into p
        //            //                             select new
        //            //                             {
        //            //                                 DepositType = p.Key.DepositType,
        //            //                                 SumAmount = p.Sum(a => a.Amount)
        //            //                             }).ToList();
        //            ////separate sum of each deposit types and calculate deposit balance.
        //            //double? totalDepositAmt, totalDepositDeductAmt, totalDepositReturnAmt, currentDepositBalance;
        //            //currentDepositBalance = totalDepositAmt = totalDepositDeductAmt = totalDepositReturnAmt = 0;

        //            //if (patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "deposit").FirstOrDefault() != null)
        //            //{
        //            //    totalDepositAmt = patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "deposit").FirstOrDefault().SumAmount;
        //            //}
        //            //if (patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "depositdeduct").FirstOrDefault() != null)
        //            //{
        //            //    totalDepositDeductAmt = patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "depositdeduct").FirstOrDefault().SumAmount;
        //            //}
        //            //if (patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "returndeposit").FirstOrDefault() != null)
        //            //{
        //            //    totalDepositReturnAmt = patientAllDepositTxns.Where(bil => bil.DepositType.ToLower() == "returndeposit").FirstOrDefault().SumAmount;
        //            //}
        //            ////below is the formula to calculate deposit balance.
        //            //currentDepositBalance = totalDepositAmt - totalDepositDeductAmt - totalDepositReturnAmt;


        //            ////Part-2: Get Total Provisional Items
        //            ////for this request type, patientid comes as inputid. 
        //            //double? patProvisionalAmt = 0;

        //            //if (IsPatientAdmitted == false)
        //            //{
        //            //    var patProvisional = (from bill in dbContext.BillingTransactionItems
        //            //                              //sud: 4May'18 changed unpaid to provisional
        //            //                          where bill.PatientId == InputId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
        //            //                          && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                          group bill by new { bill.PatientId } into p
        //            //                          select new
        //            //                          {
        //            //                              TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
        //            //                          }).FirstOrDefault();

        //            //    patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;
        //            //}
        //            //else
        //            //{
        //            //    patProvisionalAmt = 0;
        //            //}




        //            ////Part-3: Return a single object with Both Balances (Deposit and Credit).
        //            ////exclude returned invoices from credit total
        //            ////var patCredits = dbContext.BillingTransactions
        //            ////                .Where(b => b.PatientId == InputId && b.BillStatus == "unpaid" && b.ReturnStatus == false && b.IsInsuranceBilling == false)
        //            ////                 .Sum(b => b.TotalAmount);

        //            ////double patCreditAmt = patCredits != null ? patCredits.Value : 0;

        //            //var patCredits = (from bill in dbContext.BillingTransactionItems
        //            //                  where bill.PatientId == InputId
        //            //                  && bill.BillStatus == ENUM_BillingStatus.unpaid// "unpaid"
        //            //                  && (bill.ReturnStatus == false || bill.ReturnStatus == null)
        //            //                  && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                  group bill by new { bill.PatientId } into p
        //            //                  select new
        //            //                  {
        //            //                      TotalUnPaidAmt = p.Sum(a => a.TotalAmount)
        //            //                  }).FirstOrDefault();

        //            //var patCreditAmt = patCredits != null ? patCredits.TotalUnPaidAmt : 0;

        //            ////Part-4: Get Total Paid Amount
        //            //var patPaid = (from bill in dbContext.BillingTransactionItems
        //            //               where bill.PatientId == InputId
        //            //               && bill.BillStatus == ENUM_BillingStatus.paid // "paid"
        //            //               && (bill.ReturnStatus == false || bill.ReturnStatus == null)
        //            //               && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //               group bill by new { bill.PatientId } into p
        //            //               select new
        //            //               {
        //            //                   TotalPaidAmt = p.Sum(a => a.TotalAmount)
        //            //               }).FirstOrDefault();

        //            //var patPaidAmt = patPaid != null ? patPaid.TotalPaidAmt : 0;

        //            ////Part-5: get Total Discount Amount
        //            ////var patDiscount = dbContext.BillingTransactionItems
        //            ////                .Where(b => b.PatientId == InputId && b.BillStatus == "unpaid" && b.ReturnStatus == false && b.IsInsurance == false)
        //            ////                 .Sum(b => b.DiscountAmount);

        //            ////double patDiscountAmt = patDiscount != null ? patDiscount.Value : 0;

        //            //var patDiscount = (from bill in dbContext.BillingTransactionItems
        //            //                   where bill.PatientId == InputId
        //            //                   && bill.BillStatus == ENUM_BillingStatus.unpaid// "unpaid"
        //            //                   && (bill.ReturnStatus == false || bill.ReturnStatus == null)
        //            //                   && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                   group bill by new { bill.PatientId } into p
        //            //                   select new
        //            //                   {
        //            //                       TotalDiscountAmt = p.Sum(a => a.DiscountAmount)
        //            //                   }).FirstOrDefault();

        //            //var patDiscountAmt = patDiscount != null ? patDiscount.TotalDiscountAmt : 0;

        //            ////Part-6: get Total Cancelled Amount
        //            //var patCancel = (from bill in dbContext.BillingTransactionItems
        //            //                     //sud: 4May'18 changed unpaid to provisional
        //            //                 where bill.PatientId == InputId
        //            //                 && bill.BillStatus == ENUM_BillingStatus.cancel// "cancel"
        //            //                 && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            //                 group bill by new { bill.PatientId } into p
        //            //                 select new
        //            //                 {
        //            //                     TotalPaidAmt = p.Sum(a => a.TotalAmount)
        //            //                 }).FirstOrDefault();

        //            //var patCancelAmt = patCancel != null ? patCancel.TotalPaidAmt : 0;

        //            ////Part-7: get Total Cancelled Amount
        //            ////var patReturn = dbContext.BillingTransactionItems
        //            ////                .Where(b => b.PatientId == InputId && b.ReturnStatus == true) //&& (b.BillStatus == "paid" || b.BillStatus == "unpaid") && b.IsInsurance == false)
        //            ////                 .Sum(b => b.TotalAmount);

        //            ////var patReturn = (from bill in dbContext.BillingTransactionItems
        //            ////                 where bill.PatientId == InputId
        //            ////                 && bill.ReturnStatus == true
        //            ////                 && (bill.IsInsurance == false || bill.IsInsurance == null)
        //            ////                 group bill by new { bill.PatientId } into p
        //            ////                 select new
        //            ////                 {
        //            ////                     TotalPaidAmt = p.Sum(a => a.TotalAmount)
        //            ////                 }).FirstOrDefault();

        //            //var patReturn = (from rtnItems in dbContext.BillInvoiceReturnItems
        //            //                 where rtnItems.PatientId == InputId
        //            //                 && (rtnItems.IsInsurance == false || rtnItems.IsInsurance == null)
        //            //                 && (rtnItems.BillStatus == ENUM_BillingStatus.unpaid) //shankar
        //            //                 group rtnItems by new { rtnItems.PatientId } into p
        //            //                 select new
        //            //                 {
        //            //                     TotalAmt = p.Sum(a => a.RetTotalAmount)
        //            //                 }).FirstOrDefault();

        //            //var patReturnAmt = patReturn != null ? patReturn.TotalAmt : 0;

        //            ////Part-7: Return a single object with Both Balances (Deposit and Credit).
        //            //var patBillHistory = new
        //            //{
        //            //    PatientId = InputId,
        //            //    PaidAmount = patPaidAmt,
        //            //    DiscountAmount = patDiscountAmt,
        //            //    CancelAmount = patCancelAmt,
        //            //    ReturnedAmount = patReturnAmt,
        //            //    CreditAmount = patCreditAmt - patReturnAmt,
        //            //    ProvisionalAmt = patProvisionalAmt,
        //            //    TotalDue = patCreditAmt + patProvisionalAmt - patReturnAmt,
        //            //    DepositBalance = currentDepositBalance,
        //            //    BalanceAmount = currentDepositBalance - (patCreditAmt + patProvisionalAmt - patReturnAmt)
        //            //};


        //            //responseData.Results = patBillHistory;
        //        }

        //        //This block is for InsuranceProvisionalBilling 
        //        else if (reqType != null && reqType == "patientPastInsuranceBillSummary" && InputId != null && InputId != 0)
        //        {
        //            //Part-1: Get Insurance Balance of this patient. 

        //            //var patientInsuranceBalance = (from bill in dbContext.Insurances where bill.PatientId == InputId select bill.CurrentBalance).FirstOrDefault();
        //            //double? currentInsurancebalance;
        //            //currentInsurancebalance = patientInsuranceBalance;

        //            ////Part-2: Get Total Provisional Items
        //            ////for this request type, patientid comes as inputid.
        //            //var patProvisional = (from bill in dbContext.BillingTransactionItems
        //            //                      where bill.PatientId == InputId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
        //            //                      && bill.IsInsurance == true
        //            //                      group bill by new { bill.PatientId } into p
        //            //                      select new
        //            //                      {
        //            //                          TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
        //            //                      }).FirstOrDefault();

        //            //var patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;



        //            ////Part-3: Return a single object with Both Balances (Deposit and Credit).
        //            ////exclude returned invoices from credit total
        //            //var patCredits = dbContext.BillingTransactions
        //            //                .Where(b => b.PatientId == InputId && b.BillStatus != "paid" && b.ReturnStatus != true && b.IsInsuranceBilling == true)
        //            //                 .Sum(b => b.TotalAmount);

        //            //double patCreditAmt = patCredits != null ? patCredits.Value : 0;


        //            ////Part-4: Return a single object with Both Balances (Deposit and Credit).
        //            //var patInsuranceBillHistory = new
        //            //{
        //            //    PatientId = InputId,
        //            //    CreditAmount = patCreditAmt,
        //            //    ProvisionalAmt = patProvisionalAmt,
        //            //    TotalDue = patCreditAmt + patProvisionalAmt,
        //            //    DepositBalance = currentInsurancebalance,
        //            //    BalanceAmount = currentInsurancebalance - (patCreditAmt + patProvisionalAmt)
        //            //};


        //            //responseData.Results = patInsuranceBillHistory;
        //        }

        //        //list of Active Bills,Provisional Items, Settlements and Credit Invoices
        //        else if (reqType == "patient-bill-history-detail")
        //        {
        //            //var invoices = (from txn in dbContext.BillingTransactions
        //            //                join fiscalYear in dbContext.BillingFiscalYears on txn.FiscalYearId equals fiscalYear.FiscalYearId
        //            //                where txn.PatientId == patientId
        //            //                //&& (txn.IsInsuranceBilling == false || txn.IsInsuranceBilling == null)
        //            //                select new
        //            //                {
        //            //                    TransactionId = txn.BillingTransactionId,
        //            //                    Date = txn.CreatedOn,
        //            //                    InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + txn.InvoiceCode + txn.InvoiceNo.ToString(),
        //            //                    Amount = txn.TotalAmount,
        //            //                    BillStatus = txn.BillStatus,
        //            //                    PaymentMode = txn.PaymentMode,
        //            //                    IsReturned = txn.ReturnStatus,
        //            //                    IsInsuranceBilling = txn.IsInsuranceBilling,
        //            //                    ReceivedAmount = txn.ReceivedAmount
        //            //                }).OrderBy(a => a.BillStatus == "paid").ThenByDescending(a => a.Date).ToList();

        //            //var provisionalItems = (from txnItm in dbContext.BillingTransactionItems
        //            //                        join srvDept in dbContext.ServiceDepartment on txnItm.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                        join fiscalYear in dbContext.BillingFiscalYears on txnItm.ProvisionalFiscalYearId equals fiscalYear.FiscalYearId
        //            //                        where txnItm.PatientId == patientId && txnItm.BillStatus == ENUM_BillingStatus.provisional // "provisional"
        //            //                        && (txnItm.IsInsurance == false || txnItm.IsInsurance == null)
        //            //                        select new
        //            //                        {
        //            //                            TransactionItemId = txnItm.BillingTransactionItemId,
        //            //                            Date = txnItm.CreatedOn,
        //            //                            ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                            ItemName = txnItm.ItemName,
        //            //                            Amount = txnItm.TotalAmount,
        //            //                            ReceiptNo = fiscalYear.FiscalYearFormatted + "-" + "PR" + txnItm.ProvisionalReceiptNo.ToString(),
        //            //                            BillStatus = txnItm.BillStatus,
        //            //                            Quantity = txnItm.Quantity,
        //            //                            Price = txnItm.Price,
        //            //                            Discount = txnItm.DiscountAmount,
        //            //                            Tax = txnItm.Tax,
        //            //                            SubTotal = txnItm.SubTotal
        //            //                        }).OrderByDescending(a => a.Date).ToList();
        //            //var paidItems = (from itms in dbContext.BillingTransactionItems
        //            //                 join srvDept in dbContext.ServiceDepartment on itms.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                 join billtxn in dbContext.BillingTransactions on itms.BillingTransactionId equals billtxn.BillingTransactionId
        //            //                 join fiscalYear in dbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
        //            //                 where itms.PatientId == patientId && itms.BillStatus == ENUM_BillingStatus.paid// "paid"
        //            //                 && (itms.IsInsurance == false || itms.IsInsurance == null)
        //            //                 select new
        //            //                 {
        //            //                     TransactionItemId = itms.BillingTransactionItemId,
        //            //                     Date = billtxn.CreatedOn,
        //            //                     InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
        //            //                     ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                     ItemName = itms.ItemName,
        //            //                     Amount = itms.TotalAmount,
        //            //                     Quantity = itms.Quantity,
        //            //                     Price = itms.Price,
        //            //                     Discount = itms.DiscountAmount,
        //            //                     Tax = itms.Tax,
        //            //                     SubTotal = itms.SubTotal,

        //            //                 }).OrderByDescending(a => a.Date).ToList();

        //            //var unpaidItems = (from crdItems in dbContext.BillingTransactionItems
        //            //                   join srvDept in dbContext.ServiceDepartment on crdItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                   join billtxn in dbContext.BillingTransactions on crdItems.BillingTransactionId equals billtxn.BillingTransactionId
        //            //                   join fiscalYear in dbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
        //            //                   where crdItems.PatientId == patientId && crdItems.BillStatus == ENUM_BillingStatus.unpaid //"unpaid"
        //            //                   && (crdItems.IsInsurance == false || crdItems.IsInsurance == null)
        //            //                   select new
        //            //                   {
        //            //                       TransactionItemId = crdItems.BillingTransactionItemId,
        //            //                       Date = crdItems.CreatedOn,
        //            //                       InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
        //            //                       ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                       ItemName = crdItems.ItemName,
        //            //                       Amount = crdItems.TotalAmount,
        //            //                       Quantity = crdItems.Quantity,
        //            //                       Discount = crdItems.DiscountAmount,
        //            //                       Tax = crdItems.Tax,
        //            //                       SubTotal = crdItems.SubTotal,

        //            //                   }).OrderByDescending(a => a.Date).ToList();

        //            ////var returnedItems = (from rtnItems in dbContext.BillingTransactionItems
        //            ////                     join srvDept in dbContext.ServiceDepartment on rtnItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            ////                     join billtxn in dbContext.BillingTransactions on rtnItems.BillingTransactionId equals billtxn.BillingTransactionId
        //            ////                     join fiscalYear in dbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
        //            ////                     where rtnItems.PatientId == patientId
        //            ////                     && billtxn.ReturnStatus == true
        //            ////                     && (billtxn.BillStatus == ENUM_BillingStatus.paid || billtxn.BillStatus == ENUM_BillingStatus.unpaid)
        //            ////                      //&& (billtxn.BillStatus == "paid" || billtxn.BillStatus == "unpaid")
        //            ////                      && (rtnItems.IsInsurance == false || rtnItems.IsInsurance == null)
        //            ////                     select new
        //            ////                     {
        //            ////                         TransactionItemId = rtnItems.BillingTransactionItemId,
        //            ////                         Date = rtnItems.CreatedOn,
        //            ////                         InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
        //            ////                         ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            ////                         ItemName = rtnItems.ItemName,
        //            ////                         Amount = rtnItems.TotalAmount,
        //            ////                         Quantity = rtnItems.Quantity,
        //            ////                         Discount = rtnItems.DiscountAmount,
        //            ////                         Tax = rtnItems.Tax,
        //            ////                         SubTotal = rtnItems.SubTotal,

        //            ////                     }).OrderByDescending(a => a.Date).ToList();

        //            //var returnedItems = (from rtnItems in dbContext.BillInvoiceReturnItems
        //            //                     join srvDept in dbContext.ServiceDepartment on rtnItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                     join billRtnTxn in dbContext.BillInvoiceReturns on rtnItems.BillReturnId equals billRtnTxn.BillReturnId
        //            //                     join fiscalYear in dbContext.BillingFiscalYears on billRtnTxn.FiscalYearId equals fiscalYear.FiscalYearId
        //            //                     where rtnItems.PatientId == patientId
        //            //                     && (rtnItems.IsInsurance == false || rtnItems.IsInsurance == null)
        //            //                     select new
        //            //                     {
        //            //                         TransactionItemId = rtnItems.BillingTransactionItemId,
        //            //                         Date = rtnItems.CreatedOn,
        //            //                         InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billRtnTxn.InvoiceCode + billRtnTxn.RefInvoiceNum.ToString(),
        //            //                         ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                         ItemName = rtnItems.ItemName,
        //            //                         Amount = rtnItems.RetTotalAmount,
        //            //                         Quantity = rtnItems.RetQuantity,
        //            //                         Discount = rtnItems.RetDiscountAmount,
        //            //                         Tax = rtnItems.RetTaxAmount,
        //            //                         SubTotal = rtnItems.RetSubTotal,
        //            //                         BillStatus = billRtnTxn.BillStatus,
        //            //                         PaymentMode = billRtnTxn.PaymentMode,

        //            //                     }).OrderByDescending(a => a.Date).ToList();



        //            //var insuranceItems = (from insrItems in dbContext.BillingTransactionItems
        //            //                      join srvDept in dbContext.ServiceDepartment on insrItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                      join billtxn in dbContext.BillingTransactions on insrItems.BillingTransactionId equals billtxn.BillingTransactionId
        //            //                      join fiscalYear in dbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
        //            //                      where insrItems.PatientId == patientId && billtxn.IsInsuranceBilling == true
        //            //                      select new
        //            //                      {
        //            //                          TransactionItemId = insrItems.BillingTransactionItemId,
        //            //                          Date = insrItems.CreatedOn,
        //            //                          InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
        //            //                          ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                          ItemName = insrItems.ItemName,
        //            //                          Amount = insrItems.TotalAmount,
        //            //                          Quantity = insrItems.Quantity,
        //            //                          Discount = insrItems.DiscountAmount,
        //            //                          Tax = insrItems.Tax,
        //            //                          SubTotal = insrItems.SubTotal,

        //            //                      }).OrderByDescending(a => a.Date).ToList();

        //            //var settlements = (from settlement in dbContext.BillSettlements
        //            //                   where settlement.PatientId == patientId
        //            //                   select new
        //            //                   {
        //            //                       SettlementId = settlement.SettlementId,
        //            //                       SettlementReceipt = "SR" + settlement.SettlementReceiptNo.ToString(),
        //            //                       SettlementDate = settlement.SettlementDate,
        //            //                       PaidAmount = settlement.PaidAmount,
        //            //                   }).OrderByDescending(a => a.SettlementDate).ToList();
        //            //var deposits = (from deposit in dbContext.BillingDeposits
        //            //                join biltxn in dbContext.BillingTransactions on deposit.BillingTransactionId equals biltxn.BillingTransactionId into biltxnTemp
        //            //                from billingtxn in biltxnTemp.DefaultIfEmpty()
        //            //                join settlement in dbContext.BillSettlements on deposit.SettlementId equals settlement.SettlementId into settlementTemp
        //            //                from billSettlement in settlementTemp.DefaultIfEmpty()
        //            //                where deposit.PatientId == patientId
        //            //                select new
        //            //                {
        //            //                    DepositId = deposit.DepositId,
        //            //                    ReceiptNum = deposit.ReceiptNo, //used only to check whether No exists or not in client side
        //            //                    ReceiptNo = "DR" + deposit.ReceiptNo.ToString(),
        //            //                    Date = deposit.CreatedOn,
        //            //                    Amount = deposit.Amount,
        //            //                    Balance = deposit.DepositBalance,
        //            //                    DepositType = deposit.DepositType,
        //            //                    SettlementInvoice = deposit.SettlementId != null ? "SR " + billSettlement.SettlementReceiptNo.ToString() : null,
        //            //                    ReferenceInvoice = billingtxn.InvoiceCode + billingtxn.InvoiceNo,
        //            //                }).OrderBy(a => a.Date).ToList();

        //            //var CancelItems = (from cancelItems in dbContext.BillingTransactionItems
        //            //                   join fiscalYear in dbContext.BillingFiscalYears on cancelItems.ProvisionalFiscalYearId equals fiscalYear.FiscalYearId
        //            //                   where cancelItems.PatientId == patientId
        //            //                   && cancelItems.BillStatus == ENUM_BillingStatus.cancel //"cancel"
        //            //                   && (cancelItems.IsInsurance == false || cancelItems.IsInsurance == null)
        //            //                   select new
        //            //                   {
        //            //                       TransactionItemId = cancelItems.BillingTransactionItemId,
        //            //                       CreatedDate = cancelItems.CreatedOn,
        //            //                       CancelledDate = cancelItems.CancelledOn,
        //            //                       ItemName = cancelItems.ItemName,
        //            //                       ServiceDepartmentName = cancelItems.ServiceDepartmentName,
        //            //                       Amount = cancelItems.TotalAmount,
        //            //                       Quantity = cancelItems.Quantity,
        //            //                       Discount = cancelItems.DiscountAmount,
        //            //                       Tax = cancelItems.Tax,
        //            //                       SubTotal = cancelItems.SubTotal,
        //            //                       BillStatus = cancelItems.BillStatus,
        //            //                   }).OrderByDescending(a => a.CancelledDate).ToList();


        //            //responseData.Results = new
        //            //{
        //            //    IsLoaded = true,
        //            //    PatientId = patientId,
        //            //    Invoices = invoices,
        //            //    ProvisionalItems = provisionalItems,
        //            //    Settlements = settlements,
        //            //    PaidItems = paidItems,
        //            //    UnpaidItems = unpaidItems,
        //            //    Deposits = deposits,
        //            //    ReturnedItems = returnedItems,
        //            //    InsuranceItems = insuranceItems,
        //            //    CancelledItems = CancelItems

        //            //};

        //        }

        //        #region  //4.("billItemRequisition")  get price of doctor from Appointment
        //        else if (reqType == "OPDRequisitionItem")
        //        {
        //            //patientvisitid is requisitionid for opd-ticket
        //            //Int64 patientVisitId = Convert.ToInt64(requisitionId);
        //            //BillItemRequisition billItem = (from bill in dbContext.BillItemRequisitions.Include("Patient")
        //            //                                where bill.RequisitionId == patientVisitId
        //            //                                select bill).FirstOrDefault();
        //            //responseData.Results = billItem;
        //        }
        //        //Doctor Order Pay All //to s
        //        else if (reqType == "DoctorOrdersFromAllDepartments")
        //        {
        //            //List<BillItemRequisition> billItem = (from billtot in dbContext.BillItemRequisitions
        //            //                                      where billtot.PatientId == patientId && billtot.BillStatus == "pending"
        //            //                                      select billtot).ToList();
        //            //responseData.Results = billItem;
        //        }
        //        #endregion

        //        #region  //5.("billitemreqpatientdetails") to get the order requested from the doctor to show on the billing transaction view
        //        else if (reqType == "pendingReqsByDeptname")
        //        {
        //            //var pendingRequests = (from req in dbContext.BillItemRequisitions
        //            //                       where req.PatientId == patientId && req.BillStatus == "pending"
        //            //                       && req.ServiceDepartmentId == serviceDeptId
        //            //                       select req).ToList();
        //            ////commented: sud: 21May'18 -- simply return billitemRequisitions, no need to complicate this.

        //            ////var pendingRequests = (from bill in dbContext.BillItemRequisitions
        //            ////                       join doc in dbContext.Employee on bill.ProviderId equals doc.EmployeeId
        //            ////                       where (bill.PatientId == patientId && bill.BillStatus == "pending"
        //            ////                       && bill.ServiceDepartmentId == serviceDeptId)
        //            ////                       select new
        //            ////                       {
        //            ////                           bill.BillItemRequisitionId,
        //            ////                           bill.BillStatus,
        //            ////                           bill.DepartmentName,
        //            ////                           bill.ItemId,
        //            ////                           bill.ItemName,
        //            ////                           bill.Quantity,
        //            ////                           bill.Price,
        //            ////                           bill.PatientId,
        //            ////                           bill.PatientVisitId,
        //            ////                           bill.ProcedureCode,
        //            ////                           bill.ProviderId,
        //            ////                           bill.RequisitionId,
        //            ////                           bill.ServiceDepartmentId,
        //            ////                           bill.CreatedBy,
        //            ////                           bill.CreatedOn,
        //            ////                           bill.AssignedTo,
        //            ////                           RequestedBy = doc.FirstName + " " + (string.IsNullOrEmpty(doc.MiddleName) ? "" : doc.MiddleName + " ") + doc.LastName
        //            ////                       }).ToList();
        //            //responseData.Results = "OK";
        //            //responseData.Results = pendingRequests;

        //        }
        //        #endregion

        //        //Sud:20Dec'21--Below API not used anywhere hence removing it.. 
        //        //// Reporting Departmentwise---move to reporting controller : sudarshan:11June'17
        //        //else if (reqType == "departmentwiseReport")
        //        //{
        //        //    var result = (from val in dbContext.BillingTransactionItems.Include("BillingTransactions")
        //        //                  where val.BillStatus == ENUM_BillingStatus.paid //"paid"
        //        //                  group val by new { val.ServiceDepartmentName } into p
        //        //                  select new
        //        //                  {
        //        //                      ServiceDepartmentName = p.Key.ServiceDepartmentName,
        //        //                      ProductCount = p.Count()
        //        //                  }).ToList();
        //        //    responseData.Results = result;
        //        //}

        //        #region  //5.("service") to generate the items from selecting the department 
        //        else if (reqType == "serviceDeptItems" && serviceDeptId != 0)
        //        {

        //            //List<BillingItemVM> itemList = (List<BillingItemVM>)DanpheCache.Get("billItem-srvdept-" + serviceDeptId);
        //            //if (itemList == null)
        //            //{
        //            //    ServiceDepartmentModel serviceDept = (from service in dbContext.ServiceDepartment.Include("BillItemPriceList")
        //            //                                          where service.ServiceDepartmentId == serviceDeptId
        //            //                                          select service).FirstOrDefault();
        //            //    if (serviceDept.BillItemPriceList.Count != 0)
        //            //    {
        //            //        itemList = serviceDept.BillItemPriceList
        //            //                                    .Where(itm => itm.IsActive == true)//Include only Active Items-sud:7Aug'17.
        //            //                                    .Select(t => new BillingItemVM  //mapping to the billing and Test items Format 
        //            //                                    {
        //            //                                        ProcedureCode = t.ProcedureCode,
        //            //                                        ItemName = t.ItemName,
        //            //                                        ItemPrice = t.Price,
        //            //                                        ItemId = t.ItemId,
        //            //                                        TaxApplicable = t.TaxApplicable,
        //            //                                    }).ToList();

        //            //        DanpheCache.Add("billItem-srvdept-" + serviceDeptId, itemList, DateTime.Now.AddMinutes(cacheExpMinutes));

        //            //    }
        //            //}

        //            //responseData.Results = itemList;

        //        }
        //        else if (reqType == "billItemList")
        //        {
        //            //bool filterBySrvDeptId = false;
        //            //bool filterByItemId = false;
        //            //List<int> srvDeptIds = new List<int>();
        //            //List<int> itemIds = new List<int>();

        //            //if (!string.IsNullOrWhiteSpace(srvDeptIdListStr))
        //            //{
        //            //    srvDeptIds = DanpheJSONConvert.DeserializeObject<List<int>>(srvDeptIdListStr);
        //            //    filterBySrvDeptId = srvDeptIds.Count() > 0 ? true : false;
        //            //}
        //            //if (!string.IsNullOrWhiteSpace(itemIdListStr))
        //            //{
        //            //    itemIds = DanpheJSONConvert.DeserializeObject<List<int>>(itemIdListStr);
        //            //    filterByItemId = itemIds.Count() > 0 ? true : false;
        //            //}


        //            //var itemList = (from item in dbContext.BillItemPrice
        //            //                join srv in dbContext.ServiceDepartment on item.ServiceDepartmentId equals srv.ServiceDepartmentId
        //            //                where item.IsActive == true && item.IsInsurancePackage == false && item.IsNormalPriceApplicable == true
        //            //                && srv.IsActive == true
        //            //                && (filterBySrvDeptId ? srvDeptIds.Contains(srv.ServiceDepartmentId) : true)
        //            //                && (filterByItemId ? itemIds.Contains(item.ItemId) : true)
        //            //                select new
        //            //                {
        //            //                    BillItemPriceId = item.BillItemPriceId,
        //            //                    ServiceDepartmentId = srv.ServiceDepartmentId,
        //            //                    ServiceDepartmentName = srv.ServiceDepartmentName,
        //            //                    ServiceDepartmentShortName = srv.ServiceDepartmentShortName,
        //            //                    SrvDeptIntegrationName = srv.IntegrationName,
        //            //                    Displayseq = item.DisplaySeq,
        //            //                    ItemId = item.ItemId,
        //            //                    ItemCode = item.ItemCode,  //pratik :17 jan2020 
        //            //                    ItemName = item.ItemName,
        //            //                    ProcedureCode = item.ProcedureCode,
        //            //                    Price = item.Price,
        //            //                    TaxApplicable = item.TaxApplicable,
        //            //                    DiscountApplicable = item.DiscountApplicable,
        //            //                    Description = item.Description,
        //            //                    IsDoctorMandatory = item.IsDoctorMandatory,
        //            //                    IsZeroPriceAllowed = item.IsZeroPriceAllowed,
        //            //                    NormalPrice = item.Price,
        //            //                    EHSPrice = item.EHSPrice != null ? item.EHSPrice : 0, //sud:25Feb'19--For different price categories
        //            //                    SAARCCitizenPrice = item.SAARCCitizenPrice != null ? item.SAARCCitizenPrice : 0,//sud:25Feb'19--For different price categories
        //            //                    ForeignerPrice = item.ForeignerPrice != null ? item.ForeignerPrice : 0,//sud:25Feb'19--For different price categories
        //            //                    GovtInsurancePrice = item.GovtInsurancePrice != null ? item.GovtInsurancePrice : 0,//sud:25Feb'19--For different price categories
        //            //                    InsForeignerPrice = item.InsForeignerPrice != null ? item.InsForeignerPrice : 0,//pratik:8Nov'19--For different price categories
        //            //                    IsErLabApplicable = item.IsErLabApplicable,//pratik:10Feb'21--For LPH
        //            //                    Doctor = (from doc in dbContext.Employee.DefaultIfEmpty()
        //            //                              where doc.IsAppointmentApplicable == true && doc.EmployeeId == item.ItemId && srv.ServiceDepartmentName == "OPD"
        //            //                              && srv.ServiceDepartmentId == item.ServiceDepartmentId
        //            //                              select new
        //            //                              {
        //            //                                  //Temporary logic, correct it later on... 
        //            //                                  DoctorId = doc != null ? doc.EmployeeId : 0,
        //            //                                  DoctorName = doc != null ? doc.FirstName + " " + (string.IsNullOrEmpty(doc.MiddleName) ? doc.MiddleName + " " : "") + doc.LastName : "",
        //            //                              }).FirstOrDefault(),

        //            //                    IsNormalPriceApplicable = item.IsNormalPriceApplicable,
        //            //                    IsEHSPriceApplicable = item.IsEHSPriceApplicable,
        //            //                    IsForeignerPriceApplicable = item.IsForeignerPriceApplicable,
        //            //                    IsSAARCPriceApplicable = item.IsSAARCPriceApplicable,
        //            //                    IsInsForeignerPriceApplicable = item.IsInsForeignerPriceApplicable,
        //            //                    AllowMultipleQty = item.AllowMultipleQty,
        //            //                    DefaultDoctorList = item.DefaultDoctorList,
        //            //                    IsPriceChangeAllowed = item.IsPriceChangeAllowed

        //            //                }).ToList().OrderBy(a => a.Displayseq);

        //            //responseData.Status = "OK";
        //            //responseData.Results = itemList;

        //        }
        //        #endregion

        //        #region  //6.("getallitemreqfromdoctor")To show the all the department's request order from the doctor to the Billing View

        //        else if (reqType == "pendingDoctorOrderTotal")
        //        {
        //            //var requestDetails = (from billItemReq in dbContext.BillItemRequisitions
        //            //                      join pat in dbContext.Patient.Include("CountrySubDivision") on billItemReq.PatientId equals pat.PatientId
        //            //                      join item in dbContext.BillItemPrice on billItemReq.ItemId equals item.ItemId
        //            //                      join doc in dbContext.Employee on billItemReq.ProviderId equals doc.EmployeeId
        //            //                      join srvDpt in dbContext.ServiceDepartment on billItemReq.ServiceDepartmentId equals srvDpt.ServiceDepartmentId
        //            //                      where (billItemReq.BillStatus == "pending" && billItemReq.ServiceDepartmentId == item.ServiceDepartmentId)
        //            //                      group new { billItemReq, doc, srvDpt, pat } by new
        //            //                      {
        //            //                          billItemReq.PatientId,
        //            //                          //billItemReq.Patient.PatientCode,
        //            //                          //billItemReq.Patient.FirstName,
        //            //                          //billItemReq.Patient.LastName,
        //            //                          //billItemReq.Patient.MiddleName,
        //            //                          //billItemReq.Patient.PhoneNumber,
        //            //                          //billItemReq.Patient.Gender,
        //            //                          srvDpt.ServiceDepartmentName,
        //            //                          srvDpt.ServiceDepartmentId,
        //            //                          pat,
        //            //                          //billItemReq.CreatedBy,
        //            //                          DSalutation = doc.Salutation,//sud:13Mar'19--need to get doctor's salutation as well..
        //            //                          DFirstName = doc.FirstName,
        //            //                          DMiddleName = doc.MiddleName,
        //            //                          DLastName = doc.LastName
        //            //                      } into r
        //            //                      select new
        //            //                      {
        //            //                          //add more patient information if required.: sud-14May'18
        //            //                          Patient = new
        //            //                          {
        //            //                              PatientId = r.Key.pat.PatientId,
        //            //                              ShortName = r.Key.pat.FirstName + " " + (string.IsNullOrEmpty(r.Key.pat.MiddleName) ? "" : r.Key.pat.MiddleName + " ") + r.Key.pat.LastName,
        //            //                              PatientCode = r.Key.pat.PatientCode,
        //            //                              PhoneNumber = r.Key.pat.PhoneNumber,
        //            //                              Gender = r.Key.pat.Gender,
        //            //                              Address = r.Key.pat.Address,
        //            //                              DateOfBirth = r.Key.pat.DateOfBirth.Value,
        //            //                              CountrySubDivision = r.Key.pat.CountrySubDivision.CountrySubDivisionName,
        //            //                              PANNumber = r.Key.pat.PANNumber
        //            //                          },

        //            //                          PatientId = r.Key.PatientId,
        //            //                          RequestDate = r.Max(a => a.billItemReq.CreatedOn),
        //            //                          ServiceDepartmentName = r.Key.ServiceDepartmentName, //departmentName should be provided to show item on Txn view 
        //            //                          ServiceDepatmentId = r.Key.ServiceDepartmentId,
        //            //                          TotalAmount = r.Sum(a => a.billItemReq.Price * a.billItemReq.Quantity),
        //            //                          RequestedBy = (string.IsNullOrEmpty(r.Key.DSalutation) ? "" : r.Key.DSalutation + ". ") + r.Key.DFirstName + " " + (string.IsNullOrEmpty(r.Key.DMiddleName) ? "" : r.Key.DMiddleName + " ") + r.Key.DLastName,

        //            //                      }).OrderByDescending(a => a.RequestDate).ToList();
        //            //responseData.Status = "OK";
        //            //responseData.Results = requestDetails;
        //        }
        //        #endregion

        //        //Load all nursing order related one patient by PatientId
        //        else if (reqType == "nursingOrderList" && reqType != null)
        //        {
        //            //var nursingOrderList = (from bill in dbContext.BillItemRequisitions
        //            //                        join srvDpt in dbContext.ServiceDepartment
        //            //                        on bill.ServiceDepartmentId equals srvDpt.ServiceDepartmentId
        //            //                        where bill.PatientId == patientId
        //            //                        orderby bill.CreatedOn descending
        //            //                        select new
        //            //                        {
        //            //                            DepartmentName = srvDpt.ServiceDepartmentName,
        //            //                            ItemName = bill.ItemName,
        //            //                            Quantity = bill.Quantity,
        //            //                            BillStatus = bill.BillStatus,
        //            //                            CreatedOn = bill.CreatedOn
        //            //                        }

        //            //                        ).ToList();
        //            //responseData.Results = nursingOrderList;
        //        }

        //        else if (reqType == "listpatientforCancellation")
        //        {
        //            //List<BillItemRequisition> pendingRequests = (from bill in dbContext.BillItemRequisitions
        //            //                                             where (bill.PatientId == patientId && bill.BillStatus == "pending"
        //            //                                             && bill.DepartmentName.ToLower() == departmentName.ToLower())
        //            //                                             select bill).ToList();
        //            //responseData.Results = pendingRequests;

        //        }
        //        //" Requisition Cancellation"
        //        else if (reqType != null && reqType == "unpaidBillsbyPatientIdForCancellation" && InputId != null && InputId != 0)
        //        {
        //            //for this request type, patientid comes as inputid.
        //            //var patientUnpaidCancl = (from bill in dbContext.BillItemRequisitions.Include("Patient")
        //            //                          where bill.BillStatus == "pending" && bill.PatientId == InputId
        //            //                          select bill).ToList<BillItemRequisition>().OrderBy(b => b.ServiceDepartmentId);

        //            //responseData.Results = patientUnpaidCancl;


        //        }
        //        // Credit Cancellation 
        //        else if (reqType != null && reqType == "unpaidBillsbyPatientIdForCreditCancellation" && InputId != null && InputId != 0)
        //        {
        //            //for this request type, patientid comes as inputid.
        //            //var patientUnpaidCancl = (from bill in dbContext.BillingTransactionItems.Include("Patient")
        //            //                          where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" 
        //            //                          && bill.PatientId == InputId
        //            //                          select bill).ToList<BillingTransactionItemModel>()
        //            //                          .OrderBy(b => b.ServiceDepartmentId).ToList();

        //            //responseData.Results = patientUnpaidCancl;


        //        }


        //        else if (reqType == "getCounter")
        //        {
        //            //var getCounter = (from counter in dbContext.BillingCounter
        //            //                  select counter
        //            //                  ).ToList<BillingCounter>().OrderBy(b => b.CounterId);
        //            //responseData.Results = getCounter;
        //        }

        //        else if (reqType != null && reqType == "GetTxnItemsForEditDoctor")
        //        {
        //            //PatientDbContext patientDbContext = new PatientDbContext(connString);
        //            //List<ServiceDepartmentModel> allServDepts = dbContext.ServiceDepartment.ToList();
        //            //OPD items cannot be edited -- Business Rule (MNK)--sud: 11Aug'17
        //            //List<string> excludedServicedeptNames = new List<string>() { "OPD", "EMERGENCY" };

        //            ////get list of servicedepartments where Edit Doctor is applicable. 
        //            //var srvDeptsWithEditApplicable = dbContext.ServiceDepartment.Where(a => !excludedServicedeptNames.Contains(a.ServiceDepartmentName));

        //            ////this is because we have to get data till todate not in between todate to fromdate 
        //            ////so i m adding  1 day to the todate 
        //            ////var toDate = ToDate.AddDays(1);
        //            ////List<PatientModel> allPatients = dbContext.Patient.AsEnumerable().ToList();
        //            ////int searchListLength = 0;//this is default value.
        //            ////List<ParameterModel> allParams = coreDbContext.Parameters.ToList();

        //            ////ParameterModel listNumber = allParams.Where(a => a.ParameterGroupName == "Common" && a.ParameterName == "ServerSideSearchListLength").FirstOrDefault<ParameterModel>();
        //            ////if (listNumber != null)
        //            ////{
        //            ////    searchListLength = Convert.ToInt32(listNumber.ParameterValue);
        //            ////}

        //            //search = search == null ? string.Empty : search.ToLower();
        //            ////sud:13Mar'20--take fromdate-todate also in search scope, default fromdate is 1000 days back (almost 3 years)
        //            //DateTime startDate = string.IsNullOrEmpty(FromDate) ? DateTime.Now.AddDays(-1000).Date : DateTime.Parse(FromDate).Date;
        //            //DateTime endDate = string.IsNullOrEmpty(ToDate) ? DateTime.Now.Date : DateTime.Parse(ToDate).Date;



        //            //var TxnItemList = (from itm in dbContext.BillingTransactionItems.Include("BillingTransaction")
        //            //                   join ser in srvDeptsWithEditApplicable on itm.ServiceDepartmentId equals ser.ServiceDepartmentId
        //            //                   join pat in dbContext.Patient on itm.PatientId equals pat.PatientId
        //            //                   from bip in dbContext.BillItemPrice.Where(b => b.ServiceDepartmentId == itm.ServiceDepartmentId && b.ItemId == itm.ItemId)
        //            //                   from emp in dbContext.Employee.Where(emp => emp.EmployeeId == itm.PerformerId).DefaultIfEmpty() //using left join yub--30th Sept 2018.
        //            //                   where
        //            //                      itm.BillStatus != ENUM_BillingStatus.cancel // "cancel" 
        //            //                      && itm.BillStatus != ENUM_BillingStatus.adtCancel
        //            //                      && itm.ReturnStatus != true

        //            //                      //sud:13Mar'20-- search between given dates as well.
        //            //                      && (DbFunctions.TruncateTime(itm.CreatedOn) >= startDate && DbFunctions.TruncateTime(itm.CreatedOn) <= endDate)

        //            //                       &&

        //            //                   (pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName
        //            //                   + pat.PatientCode
        //            //                   + pat.PhoneNumber
        //            //                   + ser.ServiceDepartmentName
        //            //                   + itm.ItemName
        //            //                   ).Contains(search)
        //            //                   select new
        //            //                   {
        //            //                       Date = itm.CreatedOn,
        //            //                       ServiceDepartmentId = itm.ServiceDepartmentId,
        //            //                       ServiceDepartmentName = ser.ServiceDepartmentName,
        //            //                       ItemId = itm.ItemId,
        //            //                       ItemName = itm.ItemName,
        //            //                       PerformerId = itm.PerformerId,
        //            //                       PerformerName = emp.FullName,
        //            //                       PatientId = itm.PatientId,
        //            //                       BillingTransactionItemId = itm.BillingTransactionItemId,
        //            //                       //receiptno here is: invoice code+ invoicenumber//added: sud-21May'18
        //            //                       ReceiptNo = itm.BillingTransaction != null ? itm.BillingTransaction.InvoiceCode + itm.BillingTransaction.InvoiceNo : "",
        //            //                       PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
        //            //                       DateOfBirth = pat.DateOfBirth,
        //            //                       Gender = pat.Gender,
        //            //                       PhoneNumber = pat.PhoneNumber,
        //            //                       BillingTransactionId = itm.BillingTransactionId != null ? itm.BillingTransactionId : 0,  //Zero if NULL. 
        //            //                       PatientCode = pat.PatientCode,
        //            //                       BillStatus = itm.BillStatus,
        //            //                       PrescriberId = itm.PrescriberId,
        //            //                       DoctorMandatory = bip.IsDoctorMandatory
        //            //                   }).OrderByDescending(a => a.BillingTransactionItemId).AsQueryable();


        //            ////take 200 rows (or search length) only when FromDate-ToDate is null, else it's Coming From DateFilter and items are already filtered by Date Range.
        //            //if (string.IsNullOrEmpty(FromDate) && string.IsNullOrEmpty(ToDate))
        //            //{
        //            //    if (search == "" && CommonFunctions.GetCoreParameterBoolValue(coreDbContext, "Common", "ServerSideSearchComponent", "BillingEditDoctor") == true)
        //            //    {
        //            //        TxnItemList = TxnItemList.Take(CommonFunctions.GetCoreParameterIntValue(coreDbContext, "Common", "ServerSideSearchListLength"));
        //            //    }
        //            //}

        //            //var finalResults = TxnItemList.ToList();
        //            //responseData.Results = finalResults;


        //            //if (search == null)
        //            //{
        //            //    if (string.IsNullOrEmpty(FromDate) && string.IsNullOrEmpty(ToDate))
        //            //    {
        //            //        var TxnItemList = (from itm in dbContext.BillingTransactionItems.Include("BillingTransaction")
        //            //                           join ser in srvDeptsWithEditApplicable on itm.ServiceDepartmentId equals ser.ServiceDepartmentId
        //            //                           join pat in dbContext.Patient on itm.PatientId equals pat.PatientId
        //            //                           from emp in dbContext.Employee.Where(emp => emp.EmployeeId == itm.ProviderId).DefaultIfEmpty()
        //            //                           where itm.BillStatus != ENUM_BillingStatus.cancel // "cancel"
        //            //                           && itm.ReturnStatus != true
        //            //                           select new
        //            //                           {
        //            //                               Date = itm.CreatedOn,
        //            //                               ServiceDepartmentId = itm.ServiceDepartmentId,
        //            //                               ServiceDepartmentName = ser.ServiceDepartmentName,
        //            //                               ItemId = itm.ItemId,
        //            //                               ItemName = itm.ItemName,
        //            //                               ProviderId = itm.ProviderId,
        //            //                               ProviderName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
        //            //                               PatientId = itm.PatientId,
        //            //                               BillingTransactionItemId = itm.BillingTransactionItemId,
        //            //                               ReceiptNo = itm.BillingTransaction != null ? itm.BillingTransaction.InvoiceCode + itm.BillingTransaction.InvoiceNo : "",
        //            //                               PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
        //            //                               DateOfBirth = pat.DateOfBirth,
        //            //                               Gender = pat.Gender,
        //            //                               PhoneNumber = pat.PhoneNumber,
        //            //                               BillingTransactionId = itm.BillingTransactionId != null ? itm.BillingTransactionId : 0,  //Zero if NULL. 
        //            //                               PatientCode = pat.PatientCode,
        //            //                           }).OrderByDescending(a => a.BillingTransactionItemId).Take(searchListLength).ToList();
        //            //        responseData.Results = TxnItemList;
        //            //    }
        //            //    else // for search by Date. / sud/yub:11Aug'19 - return all items in matching date range.
        //            //    {
        //            //        //by default fromdate-todate will be today. convert incoming from-to dates string to startdate, enddate
        //            //        DateTime startDate = DateTime.Today;
        //            //        DateTime endDate = DateTime.Today;

        //            //        DateTime.TryParse(FromDate, out startDate);
        //            //        DateTime.TryParse(ToDate, out endDate);

        //            //        var TxnItemList = (from itm in dbContext.BillingTransactionItems.Include("BillingTransaction")
        //            //                           join ser in srvDeptsWithEditApplicable on itm.ServiceDepartmentId equals ser.ServiceDepartmentId
        //            //                           join pat in dbContext.Patient on itm.PatientId equals pat.PatientId

        //            //                           from emp in dbContext.Employee.Where(emp => emp.EmployeeId == itm.ProviderId).DefaultIfEmpty()

        //            //                           where itm.BillStatus != ENUM_BillingStatus.cancel // "cancel" 
        //            //                           && itm.ReturnStatus != true
        //            //                              && (DbFunctions.TruncateTime(itm.CreatedOn) >= startDate.Date && DbFunctions.TruncateTime(itm.CreatedOn) <= endDate.Date)
        //            //                           select new
        //            //                           {
        //            //                               Date = itm.CreatedOn,
        //            //                               ServiceDepartmentId = itm.ServiceDepartmentId,
        //            //                               ServiceDepartmentName = ser.ServiceDepartmentName,
        //            //                               ItemId = itm.ItemId,
        //            //                               ItemName = itm.ItemName,
        //            //                               ProviderId = itm.ProviderId,
        //            //                               ProviderName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
        //            //                               PatientId = itm.PatientId,
        //            //                               BillingTransactionItemId = itm.BillingTransactionItemId,
        //            //                               ReceiptNo = itm.BillingTransaction != null ? itm.BillingTransaction.InvoiceCode + itm.BillingTransaction.InvoiceNo : "",
        //            //                               PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
        //            //                               DateOfBirth = pat.DateOfBirth,
        //            //                               Gender = pat.Gender,
        //            //                               PhoneNumber = pat.PhoneNumber,
        //            //                               BillingTransactionId = itm.BillingTransactionId != null ? itm.BillingTransactionId : 0,  //Zero if NULL. 
        //            //                               PatientCode = pat.PatientCode,
        //            //                           }).OrderByDescending(a => a.BillingTransactionItemId).ToList();
        //            //        responseData.Results = TxnItemList;

        //            //    }



        //            //}
        //            //else
        //            //{
        //            //    var TxnItemList = (from itm in dbContext.BillingTransactionItems.Include("BillingTransaction")
        //            //                       join ser in srvDeptsWithEditApplicable on itm.ServiceDepartmentId equals ser.ServiceDepartmentId
        //            //                       join pat in dbContext.Patient on itm.PatientId equals pat.PatientId
        //            //                       from emp in dbContext.Employee.Where(emp => emp.EmployeeId == itm.ProviderId).DefaultIfEmpty() //using left join yub--30th Sept 2018.
        //            //                       where
        //            //                          itm.BillStatus != ENUM_BillingStatus.cancel // "cancel" 
        //            //                          && itm.ReturnStatus != true
        //            //                          &&

        //            //                       (pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName
        //            //                       + pat.PatientCode
        //            //                       + pat.PhoneNumber
        //            //                       + ser.ServiceDepartmentName
        //            //                       + itm.ItemName
        //            //                       ).Contains(search)
        //            //                       select new
        //            //                       {
        //            //                           Date = itm.CreatedOn,
        //            //                           ServiceDepartmentId = itm.ServiceDepartmentId,
        //            //                           ServiceDepartmentName = ser.ServiceDepartmentName,
        //            //                           ItemId = itm.ItemId,
        //            //                           ItemName = itm.ItemName,
        //            //                           ProviderId = itm.ProviderId,
        //            //                           ProviderName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
        //            //                           PatientId = itm.PatientId,
        //            //                           BillingTransactionItemId = itm.BillingTransactionItemId,
        //            //                           //receiptno here is: invoice code+ invoicenumber//added: sud-21May'18
        //            //                           ReceiptNo = itm.BillingTransaction != null ? itm.BillingTransaction.InvoiceCode + itm.BillingTransaction.InvoiceNo : "",
        //            //                           PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
        //            //                           DateOfBirth = pat.DateOfBirth,
        //            //                           Gender = pat.Gender,
        //            //                           PhoneNumber = pat.PhoneNumber,
        //            //                           BillingTransactionId = itm.BillingTransactionId != null ? itm.BillingTransactionId : 0,  //Zero if NULL. 
        //            //                           PatientCode = pat.PatientCode,
        //            //                       }).OrderByDescending(a => a.BillingTransactionItemId).Take(searchListLength).ToList();

        //            //    responseData.Results = TxnItemList;
        //            //}

        //        }
        //        else if (reqType != null && reqType == "GetTxnItemsForEditDoctorRad")
        //        {
        //            //                    @FromDate Datetime = null,
        //            //@ToDate DateTime = null,
        //            //		@SearchText varchar(100),
        //            //		@SrvDptIntegrationName varchar(50)
        //            //string radIntegrationName = "radiology";// this is hardcoded/reserved keyword for Radiology related Servicedepartments.

        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FromDate", FromDate),
        //            //    new SqlParameter("@ToDate", ToDate) ,
        //            //    new SqlParameter("@SearchText", search) ,
        //            //   new SqlParameter("@SrvDptIntegrationName", radIntegrationName)

        //            //};

        //            //DataTable dtRadItemsForEdit = DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetBillTxnItemsBetnDateRange_ForDepartment", paramList, dbContext);


        //            //responseData.Results = dtRadItemsForEdit;

        //        }
        //        else if (reqType != null && reqType == "opd-doctors-list")
        //        {
        //            //var opdDocs = (from itm in dbContext.BillItemPrice
        //            //               join srvDpt in dbContext.ServiceDepartment
        //            //               on itm.ServiceDepartmentId equals srvDpt.ServiceDepartmentId
        //            //               where srvDpt.ServiceDepartmentName == "OPD"
        //            //               join emp in dbContext.Employee
        //            //               on itm.ItemId equals emp.EmployeeId
        //            //               select new
        //            //               {
        //            //                   EmployeeId = emp.EmployeeId,
        //            //                   EmployeeName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
        //            //                   DepartmentId = emp.DepartmentId,
        //            //                   ServiceDepartmentId = srvDpt.ServiceDepartmentId,
        //            //                   OPDPrice = itm.Price,
        //            //                   ItemName = itm.ItemName,
        //            //               }).ToList();


        //            ////MasterDbContext mstDBContext = new MasterDbContext(connString);


        //            ////var docotorList = (from emp in mstDBContext.Employee
        //            ////                   join dep in mstDBContext.Departments on emp.DepartmentId equals dep.DepartmentId
        //            ////                   where dep.IsAppointmentApplicable == true
        //            ////                   select emp)
        //            ////                   .ToList().Select(emp => new { EmployeeId = emp.EmployeeId, EmployeeName = emp.FullName });
        //            //responseData.Results = opdDocs;
        //        }
        //        else if (reqType != null && reqType == "GetProviderList")
        //        {
        //            ////sud: 15Jun'18 -- removed departmentjoin as IsAppointmentApplicable field is now added in Employee Level as well.
        //            //MasterDbContext mstDBContext = new MasterDbContext(connString);
        //            //var docotorList = (from emp in mstDBContext.Employees
        //            //                   join dep in mstDBContext.Departments on emp.DepartmentId equals dep.DepartmentId
        //            //                   where dep.IsAppointmentApplicable == true
        //            //                   select emp)
        //            //                   .ToList().Select(emp => new { EmployeeId = emp.EmployeeId, EmployeeName = emp.FullName });

        //            //List<EmployeeModel> empListFromCache = (List<EmployeeModel>)DanpheCache.GetMasterData(MasterDataEnum.Employee);
        //            //var docotorList = empListFromCache.Where(emp => emp.IsAppointmentApplicable.HasValue
        //            //                                      && emp.IsAppointmentApplicable == true).ToList()
        //            //                                      .Select(emp => new { EmployeeId = emp.EmployeeId, EmployeeName = emp.FullName });


        //            //responseData.Results = docotorList;
        //        }
        //        else if (reqType == "doctor-list")
        //        {
        //            //sud:9Aug'18--isappointmentapplicable field can be taken from employee now.. 
        //            //MasterDbContext mstDBContext = new MasterDbContext(connString);
        //            //var doctorList = (from e in mstDBContext.Employees
        //            //                  where e.IsAppointmentApplicable.HasValue && e.IsAppointmentApplicable == true
        //            //                  //sud:13Mar'19--get only Isactive=True doctors..
        //            //                  && e.IsActive == true
        //            //                  select e).ToList();
        //            //responseData.Results = doctorList;
        //        }

        //        else if (reqType == "get-all-referrer-list")
        //        {

        //            //MasterDbContext mstDBContext = new MasterDbContext(connString);
        //            //var doctorList = (from e in mstDBContext.Employees
        //            //                  where e.IsActive == true
        //            //                  && (e.IsExternal ? true : (e.IsAppointmentApplicable.HasValue && e.IsAppointmentApplicable == true))
        //            //                  select e).ToList();
        //            //responseData.Results = doctorList;
        //        }


        //        else if (reqType == "billing-packageList")
        //        {
        //            //List<BillingPackageModel> packageList = dbContext.BillingPackages.Where(a => a.IsActive == true && a.InsuranceApplicable == false).ToList();
        //            //if (packageList.Count > 0)
        //            //{
        //            //    foreach (var package in packageList)
        //            //    {
        //            //        XmlDocument doc = new XmlDocument();
        //            //        doc.LoadXml(package.BillingItemsXML);
        //            //        package.BillingItemsXML = JsonConvert.SerializeXmlNode(doc, Newtonsoft.Json.Formatting.None, true);
        //            //    }
        //            //}
        //            //responseData.Status = "OK";
        //            //responseData.Results = packageList;
        //        }
        //        //if none of the request type matches, then return Failed Status
        //        else if (reqType == "getLatestReceiptNo")
        //        {
        //            //int ReceiptNo = (from txn in dbContext.BillingTransactions
        //            //                 select txn.InvoiceNo.Value).DefaultIfEmpty().Max();//.OrderByDescending(a => a).First();

        //            //responseData.Status = "OK";
        //            //responseData.Results = ReceiptNo;
        //        }
        //        else if (reqType == "all-fiscalYears")
        //        {
        //            //responseData.Results = dbContext.BillingFiscalYears.ToList();
        //            //responseData.Status = "OK";
        //        }
        //        else if (reqType == "current-fiscalYear")
        //        {
        //            //responseData.Results = BillingBL.GetFiscalYear(connString);
        //            //responseData.Status = "OK";
        //        }
        //        else if (reqType == "get-previous-amount")
        //        {
        //            //var todayDate = DateTime.Now.Date;
        //            ////BillingHandoverModel handoverDetail = (from amt in dbContext.Handover
        //            ////                               where amt.CreatedOn == todayDate
        //            ////                               select amt).OrderByDescending(a => a.HandoverId).FirstOrDefault();

        //            //BillingHandoverModel handoverGiven = new BillingHandoverModel();
        //            //List<BillingHandoverModel> hoGivenList = (from amt in dbContext.Handover
        //            //                                          where amt.UserId == currentUser.EmployeeId //getting indiviual user handover information
        //            //                                          select amt).ToList();

        //            //hoGivenList.ForEach(val =>
        //            //{
        //            //    if (val.CreatedOn.HasValue ? val.CreatedOn.Value.Date == todayDate : false)
        //            //    {
        //            //        handoverGiven = val;
        //            //    }
        //            //});


        //            //BillingHandoverModel handoverReceived = new BillingHandoverModel();
        //            //List<BillingHandoverModel> hoReceivedList = (from amt in dbContext.Handover
        //            //                                             where amt.HandOverUserId == currentUser.EmployeeId //getting indiviual user handover information
        //            //                                             select amt).ToList();

        //            //hoReceivedList.ForEach(val =>
        //            //{
        //            //    if (val.CreatedOn.HasValue ? val.CreatedOn.Value.Date == todayDate : false)
        //            //    {
        //            //        handoverGiven = val;
        //            //    }
        //            //});

        //            //responseData.Status = "OK";

        //            //responseData.Results = new { Given = handoverGiven, Received = handoverReceived };



        //        }
        //        else if (reqType == "patient-billing-context")
        //        {


        //            //var currPat = dbContext.Patient.Where(p => p.PatientId == patientId).FirstOrDefault();
        //            //PatientBillingContextVM currBillContext = new PatientBillingContextVM();

        //            //if (currPat != null)
        //            //{
        //            //    //get latest bed assigned to this patient if not discharged.
        //            //    var adtDetail = (from adm in dbContext.Admissions
        //            //                     where adm.PatientId == currPat.PatientId && adm.AdmissionStatus == "admitted"
        //            //                     join beds in dbContext.PatientBedInfos
        //            //                     on adm.PatientVisitId equals beds.PatientVisitId
        //            //                     select new
        //            //                     {
        //            //                         BedInfo = beds,
        //            //                         adm.PatientVisitId, //added: ashim : 08Aug2018 : to update PatientVisitId in Depoist.
        //            //                         AdmissionDate = adm.AdmissionDate,
        //            //                         AdmissionCase = adm.AdmissionCase

        //            //                     }).OrderByDescending(adt => adt.BedInfo.PatientBedInfoId).FirstOrDefault();

        //            //    int? requestingDeptId = null;
        //            //    string billingType = "outpatient";//by default its outpatient
        //            //    int? patientVisitId = null;
        //            //    DateTime? AdmissionDate = null;
        //            //    string AdmissionCase = null;
        //            //    if (adtDetail != null)
        //            //    {
        //            //        requestingDeptId = adtDetail.BedInfo.RequestingDeptId;
        //            //        patientVisitId = adtDetail.PatientVisitId;
        //            //        billingType = "inpatient";
        //            //        AdmissionDate = adtDetail.AdmissionDate;
        //            //        AdmissionCase = adtDetail.AdmissionCase;

        //            //    }
        //            //    //added: ashim : 08Aug2018 : to update PatientVisitId in Depoist.
        //            //    else
        //            //    {
        //            //        VisitModel patientVisit = dbContext.Visit.Where(visit => visit.PatientId == currPat.PatientId)
        //            //                .OrderByDescending(a => a.PatientVisitId)
        //            //                .FirstOrDefault();
        //            //        //if the latest visit is inpatient even the patient was discharged use null for visitId
        //            //        if (patientVisit != null && patientVisit.VisitType.ToLower() != ENUM_VisitType.inpatient)
        //            //        {
        //            //            patientVisitId = (int?)patientVisit.PatientVisitId;
        //            //        }
        //            //    }

        //            //    //for insurance details
        //            //    currBillContext.Insurance = (from ins in dbContext.Insurances
        //            //                                 join insProvider in dbContext.InsuranceProviders on ins.InsuranceProviderId equals insProvider.InsuranceProviderId
        //            //                                 join pat in dbContext.Patient on ins.PatientId equals pat.PatientId
        //            //                                 where insProvider.InsuranceProviderName == "Government Insurance" && ins.PatientId == currPat.PatientId
        //            //                                 select new InsuranceVM
        //            //                                 {
        //            //                                     PatientId = ins.PatientId,
        //            //                                     InsuranceProviderId = ins.InsuranceProviderId,
        //            //                                     CurrentBalance = ins.CurrentBalance,
        //            //                                     InsuranceNumber = ins.InsuranceNumber,
        //            //                                     IMISCode = ins.IMISCode,
        //            //                                     InsuranceProviderName = insProvider.InsuranceProviderName,
        //            //                                     PatientInsurancePkgTxn = (from pkgTxn in dbContext.PatientInsurancePackageTransactions
        //            //                                                               join pkg in dbContext.BillingPackages on pkgTxn.PackageId equals pkg.BillingPackageId
        //            //                                                               where pkgTxn.PatientId == currPat.PatientId && pkgTxn.IsCompleted == false
        //            //                                                               select new PatientInsurancePkgTxnVM
        //            //                                                               {
        //            //                                                                   PatientInsurancePackageId = pkgTxn.PatientInsurancePackageId,
        //            //                                                                   PackageId = pkgTxn.PackageId,
        //            //                                                                   PackageName = pkg.BillingPackageName,
        //            //                                                                   StartDate = pkgTxn.StartDate
        //            //                                                               }).FirstOrDefault()
        //            //                                 }).FirstOrDefault();

        //            //    var patProvisional = (from bill in dbContext.BillingTransactionItems
        //            //                          where bill.PatientId == currPat.PatientId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
        //            //                          && bill.IsInsurance == true
        //            //                          group bill by new { bill.PatientId } into p
        //            //                          select new
        //            //                          {
        //            //                              TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
        //            //                          }).FirstOrDefault();

        //            //    var patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;
        //            //    if (currBillContext.Insurance != null)
        //            //    {
        //            //        currBillContext.Insurance.InsuranceProvisionalAmount = patProvisionalAmt;
        //            //    }
        //            //    currBillContext.PatientId = currPat.PatientId;
        //            //    currBillContext.BillingType = billingType;
        //            //    currBillContext.RequestingDeptId = requestingDeptId;
        //            //    currBillContext.PatientVisitId = patientVisitId == 0 ? null : patientVisitId;
        //            //    currBillContext.AdmissionDate = AdmissionDate;
        //            //    currBillContext.AdmissionCase = AdmissionCase;

        //            //    var currentPatientPriceCategoryMap = dbContext.PatientMapPriceCategories.Where(a => a.LatestPatientVisitId == currBillContext.PatientVisitId && a.PatientId == currBillContext.PatientId && a.IsActive == true).FirstOrDefault();

        //            //    currBillContext.PatientMapPriceCategory = currentPatientPriceCategoryMap;
        //            //}
        //            //responseData.Status = "OK";
        //            //responseData.Results = currBillContext;

        //        }

        //        #region Get Health-Card Item
        //        else if (reqType == "GetHealthCardBillItem")
        //        {
        //            //var HealthCardBillItm = (from billItem in dbContext.BillItemPrice
        //            //                         join srvDept in dbContext.ServiceDepartment on billItem.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                         where billItem.ItemName == "Health Card"
        //            //                         select new
        //            //                         {
        //            //                             ItemId = billItem.ItemId,
        //            //                             ItemName = billItem.ItemName,
        //            //                             ServiceDepartmentId = billItem.ServiceDepartmentId,
        //            //                             ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                             Price = billItem.Price,
        //            //                             TaxApplicable = billItem.TaxApplicable
        //            //                         }).FirstOrDefault();


        //            //responseData.Status = "OK";
        //            //responseData.Results = HealthCardBillItm;
        //        }
        //        #endregion
        //        //get bill transaction in case of transfer visit
        //        //pass integrationname and use for other serveice departments if needed.
        //        //Sud:26June'19--Integration name is mandatory beacause requistionId could match between eg: Labs and Visit.
        //        else if (reqType == "billTxn-byRequisitioId")
        //        {

        //            //var bilTxnId = (from biltxnItem in dbContext.BillingTransactionItems
        //            //                join srv in dbContext.ServiceDepartment on biltxnItem.ServiceDepartmentId equals srv.ServiceDepartmentId
        //            //                where biltxnItem.RequisitionId == requisitionId
        //            //                && srv.IntegrationName.ToLower() == departmentName.ToLower()
        //            //                && biltxnItem.PatientId == patientId
        //            //                select biltxnItem.BillingTransactionId).FirstOrDefault();

        //            //if (bilTxnId != null && bilTxnId.HasValue)
        //            //{

        //            //    var retVal = new
        //            //    {
        //            //        bill = dbContext.BillingTransactions.Where(b => b.BillingTransactionId == bilTxnId).FirstOrDefault(),
        //            //        billTxnItems = (from txnItem in dbContext.BillingTransactionItems
        //            //                        where txnItem.BillingTransactionId == bilTxnId
        //            //                        select new
        //            //                        {
        //            //                            txnItem.BillingTransactionItemId,
        //            //                            txnItem.ItemName,
        //            //                            txnItem.ItemId,
        //            //                            txnItem.ServiceDepartmentName,
        //            //                            txnItem.Price,
        //            //                            txnItem.Quantity,
        //            //                            txnItem.SubTotal,
        //            //                            txnItem.DiscountAmount,
        //            //                            txnItem.TaxableAmount,
        //            //                            txnItem.Tax,
        //            //                            txnItem.TotalAmount,
        //            //                            txnItem.DiscountPercent,
        //            //                            txnItem.DiscountPercentAgg,
        //            //                            txnItem.PerformerId,
        //            //                            txnItem.PerformerName,
        //            //                            txnItem.BillStatus,
        //            //                            txnItem.RequisitionId,
        //            //                            txnItem.BillingPackageId,
        //            //                            txnItem.TaxPercent,
        //            //                            txnItem.NonTaxableAmount,
        //            //                            txnItem.BillingType,
        //            //                            txnItem.VisitType
        //            //                        }).ToList()
        //            //    };

        //            //    responseData.Results = retVal;
        //            //    responseData.Status = "OK";
        //            //}
        //            //else
        //            //{
        //            //    responseData.Results = null;
        //            //    responseData.Status = "OK";
        //            //}



        //            //var billTxn = (from bill in dbContext.BillingTransactions
        //            //               join billTxnItem in dbContext.BillingTransactionItems on bill.BillingTransactionId
        //            //               equals billTxnItem.BillingTransactionId
        //            //               join srvDept in dbContext.ServiceDepartment
        //            //               on billTxnItem.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //               where billTxnItem.RequisitionId == requisitionId

        //            //               //we need IntegrationName here in all cases.. Don't Remove it...! sud:26June'19
        //            //               && srvDept.IntegrationName.ToLower() == departmentName.ToLower()
        //            //               select new
        //            //               {
        //            //                   bill,
        //            //                   billTxnItems = (from txnItem in dbContext.BillingTransactionItems
        //            //                                   where txnItem.BillingTransactionId == bill.BillingTransactionId
        //            //                                   select new
        //            //                                   {
        //            //                                       txnItem.BillingTransactionItemId,
        //            //                                       txnItem.ItemName,
        //            //                                       txnItem.ItemId,
        //            //                                       txnItem.ServiceDepartmentName,
        //            //                                       txnItem.Price,
        //            //                                       txnItem.Quantity,
        //            //                                       txnItem.SubTotal,
        //            //                                       txnItem.DiscountAmount,
        //            //                                       txnItem.TaxableAmount,
        //            //                                       txnItem.Tax,
        //            //                                       txnItem.TotalAmount,
        //            //                                       txnItem.DiscountPercent,
        //            //                                       txnItem.DiscountPercentAgg,
        //            //                                       txnItem.ProviderId,
        //            //                                       txnItem.ProviderName,
        //            //                                       txnItem.BillStatus,
        //            //                                       txnItem.RequisitionId,
        //            //                                       txnItem.BillingPackageId,
        //            //                                       txnItem.TaxPercent,
        //            //                                       txnItem.NonTaxableAmount,
        //            //                                       txnItem.BillingType,
        //            //                                       txnItem.VisitType
        //            //                                   }).ToList()
        //            //               }).FirstOrDefault();


        //            //var billTxn = (from bill in dbContext.BillingTransactions
        //            //               join billTxnItem in dbContext.BillingTransactionItems on bill.BillingTransactionId equals billTxnItem.BillingTransactionId
        //            //               join srvDept in dbContext.ServiceDepartment on billTxnItem.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //               where billTxnItem.RequisitionId == requisitionId && srvDept.IntegrationName.ToLower() == departmentName.ToLower()
        //            //               select new
        //            //               {
        //            //                   bill,
        //            //                   billTxnItems = (from txnItem in dbContext.BillingTransactionItems
        //            //                                   where txnItem.BillingTransactionId == bill.BillingTransactionId
        //            //                                   select new
        //            //                                   {
        //            //                                       txnItem.ItemName,
        //            //                                       txnItem.TotalAmount
        //            //                                   }).ToList()
        //            //               }).FirstOrDefault();
        //            //responseData.Status = "OK";
        //            //responseData.Results = billTxn;

        //            //responseData.Status = "OK";
        //            //responseData.Results = billTxn;
        //        }
        //        else if (reqType == "department-items")
        //        {
        //            //sud: 18Sept'18-- added isActive Clause in bill items..
        //            //var labType = this.ReadQueryStringData("labType");
        //            //var billingItems = (from item in dbContext.BillItemPrice
        //            //                    join srvDept in dbContext.ServiceDepartment on item.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //            //                    join dept in dbContext.Departments on srvDept.DepartmentId equals dept.DepartmentId
        //            //                    where item.IsActive == true && srvDept.IsActive == true && dept.IsActive == true && dept.DepartmentName.ToLower() == departmentName
        //            //                    select new
        //            //                    {
        //            //                        BillItemPriceId = item.BillItemPriceId,
        //            //                        ServiceDepartmentId = srvDept.ServiceDepartmentId,
        //            //                        ServiceDepartmentName = srvDept.ServiceDepartmentName,
        //            //                        ServiceDepartmentShortName = srvDept.ServiceDepartmentShortName,
        //            //                        Displayseq = item.DisplaySeq,
        //            //                        ItemId = item.ItemId,
        //            //                        ItemName = item.ItemName,
        //            //                        ProcedureCode = item.ProcedureCode,
        //            //                        Price = item.Price,
        //            //                        NormalPrice = item.Price,
        //            //                        TaxApplicable = item.TaxApplicable,
        //            //                        DiscountApplicable = item.DiscountApplicable,
        //            //                        Description = item.Description,
        //            //                        IsDoctorMandatory = item.IsDoctorMandatory,//sud:5Feb'18--added for ward billing
        //            //                        InsForeignerPrice = item.InsForeignerPrice,
        //            //                        EHSPrice = item.EHSPrice,
        //            //                        SAARCCitizenPrice = item.SAARCCitizenPrice,
        //            //                        ForeignerPrice = item.ForeignerPrice,
        //            //                        DefaultDoctorList = item.DefaultDoctorList//Pratik:23march'20--added for ward billing

        //            //                    }).ToList().OrderBy(a => a.Displayseq);
        //            //responseData.Status = "OK";
        //            //responseData.Results = billingItems;
        //        }
        //        else if (reqType != null && reqType == "admission-bill-items")
        //        {
        //            //below are hardcoded values from HAMS hospital, pls remove it soon.. <sud:20Dec'21>
        //            //var billItems = (from bilItem in dbContext.BillItemPrice
        //            //                 join servDept in dbContext.ServiceDepartment on bilItem.ServiceDepartmentId equals servDept.ServiceDepartmentId
        //            //                 where bilItem.IntegrationName == "ADMISSION CHARGES (INDOOR)" || bilItem.IntegrationName == "Medical and Resident officer/Nursing Charges"
        //            //                 || bilItem.IntegrationName == "Medical Record Charge"
        //            //                 select new
        //            //                 {
        //            //                     bilItem.ItemId,
        //            //                     bilItem.ItemName,
        //            //                     bilItem.Price,
        //            //                     bilItem.TaxApplicable,
        //            //                     bilItem.ServiceDepartmentId,
        //            //                     servDept.ServiceDepartmentName,
        //            //                     bilItem.ProcedureCode
        //            //                 }).ToList();

        //            //responseData.Status = "OK";
        //            //responseData.Results = billItems;
        //        }

        //        else if (reqType == "check-credit-bill")
        //        {
        //            //BillingTransactionModel billTxn = dbContext.BillingTransactions.Where(a => a.PatientId == patientId && a.BillStatus == "unpaid" && a.ReturnStatus != true).FirstOrDefault();
        //            //if (billTxn != null)
        //            //{
        //            //    responseData.Results = true;
        //            //}
        //            //else
        //            //{
        //            //    responseData.Results = false;
        //            //}
        //            //responseData.Status = "OK";
        //        }
        //        else if (reqType == "get-credit-organization-list")
        //        {
        //            //var orgList = (from co in dbContext.CreditOrganization
        //            //               where co.IsActive == true
        //            //               select co).ToList();
        //            //responseData.Results = orgList;
        //        }
        //        //getting user list
        //        else if (reqType == "get-users-list")
        //        {
        //            //var allUsrs = (from user in rbacDbContext.Users
        //            //               where user.IsActive == true
        //            //               select user).ToList();
        //            //getting userList from Employee table
        //            //var allUsrs = (from emp in dbContext.Employee
        //            //               join dep in dbContext.Departments on emp.DepartmentId equals dep.DepartmentId
        //            //               //join user in rbacDbContext.Users on emp.EmployeeId equals user.EmployeeId
        //            //               where emp.IsActive == true
        //            //               //&& user.IsActive==true 
        //            //               select new
        //            //               {
        //            //                   ShortName = emp.FirstName + " " + (string.IsNullOrEmpty(emp.MiddleName) ? "" : emp.MiddleName + " ") + emp.LastName,
        //            //                   UserId = emp.EmployeeId,
        //            //                   //UserName=user.UserName,
        //            //                   DepartmentName = dep.DepartmentName
        //            //               }).ToList();

        //            //responseData.Status = "OK";
        //            //responseData.Results = allUsrs;
        //        }
        //        else if (reqType == "get-bank-list")
        //        {

        //            var bankList = (from bank in dbContext.Banks
        //                            where bank.IsActive == true
        //                            select bank).ToList();
        //            responseData.Results = bankList;
        //            responseData.Status = "OK";
        //        }

        //        else if (reqType == "get-DueAmount")
        //        {

        //            //responseData.Results = BillingBL.GetEmpDueAmount(dbContext, currentUser.EmployeeId); ;
        //            //responseData.Status = "OK";
        //        }

        //        else if (reqType == "past-test-list")
        //        {
        //            //var numberOfPastDays = (from param in dbContext.AdminParameters
        //            //                        where param.ParameterGroupName.ToLower() == "bill" && param.ParameterName == "PastBillMaximumDays"
        //            //                        select param.ParameterValue).FirstOrDefault();

        //            //if (numberOfPastDays == null)
        //            //{
        //            //    numberOfPastDays = "7";
        //            //}
        //            //List<SqlParameter> paramList = new List<SqlParameter>() { new SqlParameter("@PatientId", patientId), new SqlParameter("@maxPastDays", numberOfPastDays) };
        //            //DataTable patpastBills = DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetPatientPastBills", paramList, dbContext);


        //            ////var intNum = 7;
        //            ////DateTime lastFewDaysDays = DateTime.Now.AddDays(intNum);

        //            ////var allTestList = (from items in dbContext.BillingTransactionItems
        //            ////                   where  (items.CreatedOn.HasValue ? items.CreatedOn > lastFewDaysDays : true)
        //            ////                   && items.PatientId == patientId && items.BillStatus != "cancel"
        //            ////                   select new
        //            ////                   {
        //            ////                       CreatedOn = items.CreatedOn,
        //            ////                       InvoiceCode = (from billTransaction in dbContext.BillingTransactions
        //            ////                                      where billTransaction.BillingTransactionId==items.BillingTransactionId
        //            ////                                      select billTransaction.InvoiceCode).FirstOrDefault(),
        //            ////                       InvoiceNo = (from billTransaction in dbContext.BillingTransactions
        //            ////                                    where billTransaction.BillingTransactionId == items.BillingTransactionId
        //            ////                                    select billTransaction.InvoiceNo).FirstOrDefault(),
        //            ////                       ServiceDepartmentName = items.ServiceDepartmentName,
        //            ////                       ItemName = items.ItemName,
        //            ////                       Price = items.Price,
        //            ////                       BillStatus = items.BillStatus,
        //            ////                       CreatedBy = items.CreatedBy
        //            ////                   }).ToList();



        //            //responseData.Status = "OK";
        //            //responseData.Results = patpastBills;
        //        }
        //        else if (reqType == "getVisitInfoforStickerPrint")
        //        {

        //            //List<SqlParameter> paramList = new List<SqlParameter>() { new SqlParameter("@BillingTransactionId", billingTransactionId) };
        //            //DataTable patStickerDetails = DALFunctions.GetDataTableFromStoredProc("SP_Package_GetPatientVisitStickerInfo", paramList, dbContext);
        //            //responseData.Results = patStickerDetails;
        //            //responseData.Status = "OK";
        //        }
        //        else if (reqType == "get-all-handover-transaction")
        //        {
        //            //var HandoverTransactions = (from handover in dbContext.HandoverTransaction
        //            //                            join emp in dbContext.Employee.Include("Department") on handover.HandoverByEmpId equals emp.EmployeeId
        //            //                            join counter in dbContext.BillingCounter on handover.CounterId equals counter.CounterId
        //            //                            //join dep in dbContext.Departments on emp.DepartmentId equals dep.DepartmentId
        //            //                            where handover.IsActive == true && handover.ReceivedById == null && handover.HandoverType == ENUM_HandOverType.Account
        //            //                            select new
        //            //                            {
        //            //                                HandoverTxnId = handover.HandoverTxnId,
        //            //                                HandoverByEmpId = handover.HandoverByEmpId,
        //            //                                HandoverType = handover.HandoverType,
        //            //                                BankName = handover.BankName,
        //            //                                VoucherNumber = handover.VoucherNumber,
        //            //                                VoucherDate = handover.VoucherDate,
        //            //                                HandoverAmount = handover.HandoverAmount,
        //            //                                DueAmount = handover.DueAmount,
        //            //                                HandoverRemarks = handover.HandoverRemarks,
        //            //                                CounterName = counter.CounterName,
        //            //                                CounterId = counter.CounterId,
        //            //                                UserId = emp.EmployeeId,
        //            //                                UserName = emp.FullName,
        //            //                                DepartmentId = emp.DepartmentId,
        //            //                                DepartmentName = emp.Department != null ? emp.Department.DepartmentName : null,
        //            //                                CreatedOn = handover.CreatedOn
        //            //                            }).ToList().OrderBy(s => s.VoucherDate); ;
        //            //responseData.Results = HandoverTransactions;
        //            //responseData.Status = "OK";
        //        }
        //        else if (reqType == "get-handover-recive-report")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FromDate", FromDate),
        //            //    new SqlParameter("@ToDate", ToDate)
        //            //};

        //            //DataTable handoverTxn = DALFunctions.GetDataTableFromStoredProc("SP_HandoverReceiveTransactionReport", paramList, dbContext);
        //            //responseData.Results = handoverTxn;
        //        }
        //        else if (reqType == "get-dailyCollection-vs-handover-report")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FromDate", FromDate),
        //            //    new SqlParameter("@ToDate", ToDate)
        //            //};

        //            //DataTable handoverTxn = DALFunctions.GetDataTableFromStoredProc("SP_Report_DailyCollectionVsHandoverReport", paramList, dbContext);
        //            //responseData.Results = handoverTxn;
        //        }

        //        else if (reqType == "get-handover-detail-report")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FromDate", FromDate),
        //            //    new SqlParameter("@ToDate", ToDate),
        //            //    new SqlParameter("@EmployeeId", EmployeeId)
        //            //};

        //            //DataTable handoverTxn = DALFunctions.GetDataTableFromStoredProc("SP_Report_HandoverDetailReport", paramList, dbContext);
        //            //responseData.Results = handoverTxn;
        //        }

        //        else if (reqType == "get-handover-summary-report")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@FiscalYrId", fiscalYrId)
        //            //};
        //            //DataTable handoverTxn = DALFunctions.GetDataTableFromStoredProc("SP_Report_HandoverSummaryReport", paramList, dbContext);
        //            //responseData.Results = handoverTxn;
        //        }
        //        // to list out the invoice return details for duplicate printing
        //        else if (reqType != null && reqType.ToLower() == "get-invoiceinfo-forprint")
        //        {
        //            //List<SqlParameter> paramList = new List<SqlParameter>() {
        //            //    new SqlParameter("@InvoiceNumber", invoiceNumber),
        //            //    new SqlParameter("@FiscalYearId", fiscalYrId),
        //            //     new SqlParameter("@BillingTxnIdInput", billingTransactionId)
        //            //};


        //            ////there are five return table coming from this stored procedure.
        //            //DataSet dsPrintData = DALFunctions.GetDatasetFromStoredProc("SP_BIL_GetInvoiceDetailsForPrint", paramList, dbContext);

        //            //DataTable dtPatientInfo = dsPrintData.Tables[0];
        //            //DataTable dtInvoiceInfo = dsPrintData.Tables[1];
        //            //DataTable dtInvItems = dsPrintData.Tables[2];
        //            //DataTable dtVisitInfo = dsPrintData.Tables[3];
        //            //DataTable dtDepositsInfo = dsPrintData.Tables[4];

        //            ////group them in a new anonymous object and send to client.
        //            //var printInfoToReturn = new
        //            //{
        //            //    PatientInfo = BilPrint_PatientInfoVM.MapDataTableToSingleObject(dtPatientInfo),
        //            //    InvoiceInfo = BilPrint_InvoiceInfoVM.MapDataTableToSingleObject(dtInvoiceInfo),
        //            //    InvoiceItems = BilPrint_InvoiceItemVM.MapDataTableToObjectList(dtInvItems),
        //            //    VisitInfo = BilPrint_VisitInfoVM.MapDataTableToSingleObject(dtVisitInfo),
        //            //    DepositList = BilPrint_DepositListVM.MapDataTableToObjectList(dtDepositsInfo),
        //            //    IsInvoiceFound = dtInvoiceInfo.Rows.Count > 0 ? true : false//this flag decides whether or not to display in client side.
        //            //};

        //            //printInfoToReturn.VisitInfo.ItemsRequestingDoctorsId = printInfoToReturn.InvoiceItems.Select(s => s.RequestedBy).Distinct().ToList();
        //            //printInfoToReturn.VisitInfo.ItemsRequestingDoctors = String.Join(",", printInfoToReturn.InvoiceItems.Where(d => (d.RequestedBy > 0)).Select(s => s.RequestedByName).Distinct());

        //            //responseData.Results = printInfoToReturn;

        //        }

        //        else
        //        {
        //            responseData.Status = "Failed";
        //            responseData.ErrorMessage = "request type is incorrect.";
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        responseData.Status = "Failed";
        //        responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
        //    }
        //    return DanpheJSONConvert.SerializeObject(responseData, true);

        //}

        [Route("billing-transaction")]
        [HttpPost]
        #region Post Method to handle the Billing Post Request..
        public object PostBillingTransaction()
        {
            //BillingTransactionModel billingTransactionModel = new BillingTransactionModel();
            string strBillingTransactionData = this.ReadPostData();
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);

            Func<object> func = () => NewBillingTransaction(strBillingTransactionData, currentUser);
            return InvokeHttpPostFunction(func);
        }


        [Route("provisional-billing")]
        [HttpPost]
        #region Handles the Provisonal Billing Requests..
        public object PostProvisionalBilling()
        {
            BillingTransactionModel billingTransactionModel = new BillingTransactionModel();
            string strBillingTransactionData = this.ReadPostData();
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);

            Func<object> func = () => NewProvisionalBilling(ref billingTransactionModel, strBillingTransactionData, currentUser);
            return InvokeHttpPostFunction(func);
        }

        #endregion

        [HttpPost]
        [Route("PayProvisionalBills")]
        public IActionResult PayProvisionalBills()
        {
            // else if (reqType == "post-billingTransaction")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);
            string ipDataString = this.ReadPostData();

            Func<object> func = () => ProcessProvisionalBills(ipDataString, currentUser);
            return InvokeHttpPostFunction<object>(func);
        }


        [HttpPost]
        [Route("SaveBillingTransactionItems")]
        public IActionResult SaveBillingTransactionItems()
        {
            //else if (reqType == "post-billingTransactionItems")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();

            Func<object> func = () => AddBillingTransactionItems(ipDataString, currentUser);
            return InvokeHttpPostFunction<object>(func);
        }

        [HttpPost]
        [Route("SaveBillItemsRequisition")]
        public IActionResult SaveBillItemsRequisition()
        {
            // else if (reqType == "billItemsRequisition")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();
            Func<object> func = () => AddBillItemsRequisition(ipDataString, currentUser);
            return InvokeHttpPostFunction<object>(func);
        }

        [HttpPost]
        [Route("SaveHTMLFile")]
        public IActionResult SaveHTMLFile(string PrinterName, string FilePath)
        {
            //else if (reqType == "saveHTMLfile")
            // {
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();
            //string PrinterName = this.ReadQueryStringData("PrinterName");
            //string FilePath = this.ReadQueryStringData("FilePath");
            Func<object> func = () => AddHTMLFile(ipDataString, currentUser, PrinterName, FilePath);
            return InvokeHttpPostFunction<object>(func);
        }

        //[HttpPost]
        //[Route("PostHandoverDenominationDetail")]
        //public IActionResult PostHandoverDenominationDetail()
        //{
        //    //else if (reqType == "post-handover-denomination-detail")
        //    //{
        //    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
        //    string ipDataString = this.ReadPostData();
        //    Func<object> func = () => SaveHandoverDenominationDetail(ipDataString, currentUser);
        //    return InvokeHttpPostFunction<object>(func);
        //}


        [HttpPost]
        [Route("HandoverTransactionDetail")]
        public IActionResult PostHandoverTransactionDetail()
        {
            //else if (reqType == "post-handover-transaction-detail")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();
            Func<object> func = () => SaveHandOverTransactionDetail(ipDataString, currentUser);
            return InvokeHttpPostFunction<object>(func);
        }



        //[HttpPost]// POST api/values
        //public string Post()
        //{
        //    //DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();//type 'object' since we have variable return types
        //    //responseData.Status = "OK";//by default status would be OK, hence assigning at the top
        //    //string ipDataString = this.ReadPostData();
        //    //string reqType = this.ReadQueryStringData("reqType");
        //    //string serviceDepartment = this.ReadQueryStringData("serviceDepartment");
        //    //string PrinterName = this.ReadQueryStringData("PrinterName");
        //    //string FilePath = this.ReadQueryStringData("FilePath");
        //    //try
        //    //{

        //    //    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
        //    //    int CreatedBy = ToInt(this.ReadQueryStringData("CreatedBy"));
        //    //    BillingDbContext billingDbContext = new BillingDbContext(connString);
        //    //    //ashim: 23Dec2018 Delete this ASAP
        //    //    if (reqType == "billTxnItems")//submit
        //    //    {
        //    //        //once the items are added to the transactionitem-table, update the status in BillItemRequisition as 'paid'
        //    //        //List<BillingTransactionItemModel> billTranItems = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(ipDataString);
        //    //        ////int serviceDeptId = dbContext.ServiceDepartment.Where(s=>s.ServiceDepartmentName==service)
        //    //        //MasterDbContext masterDbContext = new MasterDbContext(connString);
        //    //        //List<EmployeeModel> allEmps = masterDbContext.Employees.ToList();
        //    //        //List<ServiceDepartmentModel> allSrvDepts = billingDbContext.ServiceDepartment.ToList();

        //    //        //if (billTranItems != null && billTranItems.Count > 0)
        //    //        //{
        //    //        //    //a single bill may contain package items as well as other items.
        //    //        //    int? packageId = null;
        //    //        //    bool isInsurancePackage = false;
        //    //        //    foreach (BillingTransactionItemModel txnItem in billTranItems)
        //    //        //    {
        //    //        //        txnItem.CreatedOn = System.DateTime.Now;
        //    //        //        txnItem.RequisitionDate = System.DateTime.Now;

        //    //        //        //assign providername and servicedepartmentname to each of the incoming transaction items.
        //    //        //        //Needs Revision: 12-12-17: sud: I think we don't need to get providername since that property already comes from client side: 
        //    //        //        txnItem.PerformerName = (from a in allEmps where a.EmployeeId == txnItem.PerformerId select a.FullName).FirstOrDefault();
        //    //        //        txnItem.ServiceDepartmentName = (from b in allSrvDepts where b.ServiceDepartmentId == txnItem.ServiceDepartmentId select b.ServiceDepartmentName).FirstOrDefault();


        //    //        //        billingDbContext.BillingTransactionItems.Add(txnItem);
        //    //        //        //a single bill may contain package items as well as other items.
        //    //        //        //if any of the item contains insurance-package it must AddPatientInsurancePackage
        //    //        //        if (txnItem.IsInsurancePackage)
        //    //        //        {
        //    //        //            isInsurancePackage = true;
        //    //        //            packageId = txnItem.BillingPackageId;
        //    //        //        }
        //    //        //    }
        //    //        //    billingDbContext.SaveChanges();
        //    //        //    if (isInsurancePackage)
        //    //        //    {
        //    //        //        BillingBL.AddPatientInsurancePackage(billingDbContext, packageId ?? default(int), billTranItems[0].PatientId, currentUser.EmployeeId);
        //    //        //    }
        //    //        //    //update the Requisitions billingstatus as 'paid' for above items. 
        //    //        //    //List<Int32?> requisitionIds = (from a in billTranItems select a.BillItemRequisitionId).ToList();
        //    //        //    foreach (var billTransItem in billTranItems)
        //    //        //    {
        //    //        //        BillItemRequisition billItemRequisition = new BillItemRequisition();
        //    //        //        billItemRequisition = (from bill in billingDbContext.BillItemRequisitions
        //    //        //                               where bill.RequisitionId == billTransItem.RequisitionId
        //    //        //                               && bill.ServiceDepartmentId == billTransItem.ServiceDepartmentId
        //    //        //                               select bill).FirstOrDefault();
        //    //        //        if (billItemRequisition != null)
        //    //        //        {
        //    //        //            billItemRequisition.BillStatus = "paid";
        //    //        //            billingDbContext.Entry(billItemRequisition).State = EntityState.Modified;
        //    //        //        }

        //    //        //    }

        //    //        //    billingDbContext.SaveChanges();
        //    //        //    responseData.Results = billTranItems;//check if we need to send back all the input array back to client.--sudarshan
        //    //        //}
        //    //    }

        //    //    else if (reqType == "post-billingTransaction")
        //    //    {
        //    //        //bool transactionSuccess = false;
        //    //        //BillingTransactionModel billTransaction = DanpheJSONConvert.DeserializeObject<BillingTransactionModel>(ipDataString);

        //    //        //if (BillingTransactionBL.IsDepositAvailable(billingDbContext, billTransaction.PatientId, billTransaction.DepositUsed))
        //    //        //{
        //    //        //    //Transaction Begins  
        //    //        //    using (var dbContextTransaction = billingDbContext.Database.BeginTransaction())
        //    //        //    {
        //    //        //        try
        //    //        //        {
        //    //        //            billTransaction = BillingTransactionBL.PostBillingTransaction(billingDbContext, connString, billTransaction, currentUser, DateTime.Now);
        //    //        //            //sud:4May'21--we're not using billing controller for Insurance anymore.
        //    //        //            //if (billTransaction.IsInsuranceBilling == true)
        //    //        //            //{
        //    //        //            //    BillingBL.UpdateInsuranceCurrentBalance(connString,
        //    //        //            //        billTransaction.PatientId,
        //    //        //            //        billTransaction.InsuranceProviderId ?? default(int),
        //    //        //            //        currentUser.EmployeeId, billTransaction.TotalAmount ?? default(int), true);
        //    //        //            //}


        //    //        //            //Billing User should be assigned from the server side avoiding assigning from client side 
        //    //        //            //which is causing issue in billing receipt while 2 user loggedIn in the same browser
        //    //        //            billTransaction.BillingUserName = currentUser.UserName;//Yubaraj: 28June'19                            

        //    //        //            dbContextTransaction.Commit(); //end of transaction
        //    //        //            transactionSuccess = true;

        //    //        //            //send to IRD only after transaction is committed successfully: sud-23Dec'18
        //    //        //            //Below is asynchronous call, so even if IRD api is down, it won't hold the execution of other code..
        //    //        //            if (realTimeRemoteSyncEnabled)
        //    //        //            {
        //    //        //                if (billTransaction.Patient == null)
        //    //        //                {
        //    //        //                    PatientModel pat = billingDbContext.Patient.Where(p => p.PatientId == billTransaction.PatientId).FirstOrDefault();
        //    //        //                    billTransaction.Patient = pat;
        //    //        //                }
        //    //        //                //Sud:23Dec'18--making parallel thread call (asynchronous) to post to IRD. so that it won't stop the normal execution of logic.
        //    //        //                // BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext);
        //    //        //                Task.Run(() => BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext));
        //    //        //            }

        //    //        //            responseData.Status = "OK";
        //    //        //        }
        //    //        //        catch (Exception ex)
        //    //        //        {
        //    //        //            transactionSuccess = false;
        //    //        //            HandleException(ex);
        //    //        //            //rollback all changes if any error occurs
        //    //        //            dbContextTransaction.Rollback();
        //    //        //            //throw ex;
        //    //        //        }
        //    //        //    }
        //    //        //}
        //    //        //else
        //    //        //{
        //    //        //    responseData.Status = "Failed";
        //    //        //    responseData.Results = null;
        //    //        //    responseData.ErrorMessage = "Deposit Amount is Invalid, Please try again.";
        //    //        //}

        //    //        //if (transactionSuccess)
        //    //        //{
        //    //        //    ////Starts: This code is for Temporary solution for Checking and Updating the Invoice Number if there is duplication Found
        //    //        //    //List<SqlParameter> paramList = new List<SqlParameter>() {
        //    //        //    //        new SqlParameter("@fiscalYearId", billTransaction.FiscalYearId),
        //    //        //    //        new SqlParameter("@billingTransactionId", billTransaction.BillingTransactionId),
        //    //        //    //        new SqlParameter("@invoiceNumber", billTransaction.InvoiceNo)
        //    //        //    //    };

        //    //        //    //DataSet dataFromSP = DALFunctions.GetDatasetFromStoredProc("SP_BIL_Update_Duplicate_Invoice_If_Exists", paramList, billingDbContext);
        //    //        //    //var data = new List<object>();
        //    //        //    //if (dataFromSP.Tables.Count > 0)
        //    //        //    //{
        //    //        //    //    billTransaction.InvoiceNo = Convert.ToInt32(dataFromSP.Tables[0].Rows[0]["LatestInvoiceNumber"].ToString());
        //    //        //    //}
        //    //        //    ////Ends
        //    //        //    responseData.Results = billTransaction;//check if we need to send back all the input object back to client.--sudarshan
        //    //        //}


        //    //    }

        //    //    else if (reqType == "post-billingTransactionItems")
        //    //    {
        //    //        //Transaction Begins 
        //    //        //List<BillingTransactionItemModel> billTranItems = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(ipDataString);
        //    //        //billTranItems.ForEach(a =>
        //    //        //{
        //    //        //    a.CoPaymentCashAmount = a.CoPaymentCashAmount == null ? 0 : a.CoPaymentCashAmount;
        //    //        //    a.CoPaymentCreditAmount = a.CoPaymentCreditAmount == null ? 0 : a.CoPaymentCreditAmount;
        //    //        //});
        //    //        //using (var dbContextTransaction = billingDbContext.Database.BeginTransaction())
        //    //        //{
        //    //        //    try
        //    //        //    {
        //    //        //        if (billTranItems != null && billTranItems.Count > 0)
        //    //        //        {
        //    //        //            billTranItems = BillingTransactionBL.PostUpdateBillingTransactionItems(billingDbContext,
        //    //        //                connString,
        //    //        //                billTranItems,
        //    //        //                currentUser,
        //    //        //                DateTime.Now,
        //    //        //                billTranItems[0].BillStatus,
        //    //        //                billTranItems[0].CounterId);

        //    //        //            var userName = (from emp in billingDbContext.Employee where emp.EmployeeId == currentUser.EmployeeId select emp.FirstName).FirstOrDefault();
        //    //        //            billTranItems.ForEach(usr => usr.RequestingUserName = userName);

        //    //        //            responseData.Results = billTranItems;//check if we need to send back all the input object back to client.--sudarshan
        //    //        //            dbContextTransaction.Commit(); //end of transaction
        //    //        //            responseData.Status = "OK";
        //    //        //        }
        //    //        //    }
        //    //        //    catch (Exception ex)
        //    //        //    {
        //    //        //        //rollback all changes if any error occurs
        //    //        //        dbContextTransaction.Rollback();
        //    //        //        throw ex;
        //    //        //    }
        //    //        //}

        //    //    }

        //    //    else if (reqType == "postBillTransaction")//submit
        //    //    {
        //    //        //bool transactionSuccess = false;
        //    //        //BillingTransactionModel billTransaction = DanpheJSONConvert.DeserializeObject<BillingTransactionModel>(ipDataString);
        //    //        //if (billTransaction != null)
        //    //        //{
        //    //        //    //Transaction Begins  
        //    //        //    using (var dbContextTransaction = billingDbContext.Database.BeginTransaction())
        //    //        //    {
        //    //        //        try
        //    //        //        {
        //    //        //            //step: 1-- make copy of billingTxnItems into new list, so thate EF doesn't add txn items again.
        //    //        //            DateTime createdOn = DateTime.Now;
        //    //        //            List<BillingTransactionItemModel> newTxnItems = new List<BillingTransactionItemModel>();
        //    //        //            if (billTransaction.BillingTransactionItems != null && billTransaction.BillingTransactionItems.Count > 0)
        //    //        //            {
        //    //        //                foreach (var txnItem in billTransaction.BillingTransactionItems)
        //    //        //                {
        //    //        //                    newTxnItems.Add(BillingTransactionItemModel.GetClone(txnItem));
        //    //        //                }
        //    //        //                billTransaction.BillingTransactionItems = null;
        //    //        //            }
        //    //        //            //if paymentmode is credit, paiddate and paidamount should be null
        //    //        //            //handle this in client side as well. 
        //    //        //            billTransaction.CreatedBy = currentUser.EmployeeId;
        //    //        //            if (billTransaction.BillStatus == ENUM_BillingStatus.unpaid)// "unpaid")
        //    //        //            {
        //    //        //                billTransaction.PaidDate = null;
        //    //        //                billTransaction.PaidAmount = null;
        //    //        //                billTransaction.PaymentReceivedBy = null;
        //    //        //                billTransaction.PaidCounterId = null;
        //    //        //            }
        //    //        //            else
        //    //        //            {
        //    //        //                billTransaction.PaidDate = createdOn;
        //    //        //                billTransaction.PaidCounterId = billTransaction.CounterId;
        //    //        //                billTransaction.PaymentReceivedBy = billTransaction.CreatedBy;
        //    //        //            }

        //    //        //            BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

        //    //        //            //ashim: 26Aug2018: Moved from client side to server side.
        //    //        //            billTransaction.CreatedOn = createdOn;
        //    //        //            billTransaction.CreatedBy = currentUser.EmployeeId;
        //    //        //            billTransaction.FiscalYearId = fiscYear.FiscalYearId;
        //    //        //            billTransaction.InvoiceNo = BillingBL.GetInvoiceNumber(connString);
        //    //        //            billTransaction.InvoiceCode = billTransaction.IsInsuranceBilling == true ? "INS" : InvoiceCode;
        //    //        //            billingDbContext.BillingTransactions.Add(billTransaction);
        //    //        //            billingDbContext.SaveChanges();


        //    //        //            //step:3-- if there's deposit deduction, then add to deposit table. 
        //    //        //            if (billTransaction.DepositReturnAmount != null && billTransaction.DepositReturnAmount > 0)
        //    //        //            {
        //    //        //                VisitModel patientVisit = billingDbContext.Visit.Where(visit => visit.PatientId == billTransaction.PatientId)
        //    //        //                     .OrderByDescending(a => a.PatientVisitId)
        //    //        //                     .FirstOrDefault();
        //    //        //                BillingDeposit dep = new BillingDeposit()
        //    //        //                {
        //    //        //                    DepositType = ENUM_BillDepositType.DepositDeduct,// "depositdeduct",
        //    //        //                    Remarks = "Deposit used in InvoiceNo. " + billTransaction.InvoiceCode + billTransaction.InvoiceNo,
        //    //        //                    Amount = billTransaction.DepositReturnAmount,
        //    //        //                    BillingTransactionId = billTransaction.BillingTransactionId,
        //    //        //                    DepositBalance = billTransaction.DepositBalance,
        //    //        //                    FiscalYearId = billTransaction.FiscalYearId,
        //    //        //                    CounterId = billTransaction.CounterId,
        //    //        //                    CreatedBy = billTransaction.CreatedBy,
        //    //        //                    CreatedOn = DateTime.Now,
        //    //        //                    PatientId = billTransaction.PatientId,
        //    //        //                    PatientVisitId = patientVisit != null ? (int?)patientVisit.PatientVisitId : null,
        //    //        //                    ReceiptNo = BillingBL.GetDepositReceiptNo(connString)
        //    //        //                };

        //    //        //                billingDbContext.BillingDeposits.Add(dep);
        //    //        //                billingDbContext.SaveChanges();
        //    //        //            }


        //    //        //            ///review: sudarshan--24june-- Call the ItemsStatus->Put once PostTransaction is successful. we don't have to go round-trip from client again.. 
        //    //        //            // UpdateStatusOfBillTxnItems(dbContext, billTransaction.BillingTransactionItems);
        //    //        //            billTransaction.FiscalYear = fiscYear.FiscalYearFormatted;//added: sud:5May'18
        //    //        //                                                                      //check if we need to send back all the input object back to client.--sudarshan

        //    //        //            //sud:4May'21--below code is not used anywhere
        //    //        //            //if (billTransaction.IsInsuranceBilling == true)
        //    //        //            //{
        //    //        //            //    BillingBL.UpdateInsuranceCurrentBalance(connString,
        //    //        //            //        billTransaction.PatientId,
        //    //        //            //        billTransaction.InsuranceProviderId ?? default(int),
        //    //        //            //        currentUser.EmployeeId, billTransaction.TotalAmount ?? default(int), true);
        //    //        //            //}

        //    //        //            billingDbContext.SaveChanges();
        //    //        //            dbContextTransaction.Commit(); //end of transaction                                
        //    //        //            transactionSuccess = true;

        //    //        //            //send to IRD only after transaction is committed successfully: sud-23Dec'18
        //    //        //            if (realTimeRemoteSyncEnabled)
        //    //        //            {
        //    //        //                if (billTransaction.Patient == null)
        //    //        //                {
        //    //        //                    PatientModel pat = billingDbContext.Patient.Where(p => p.PatientId == billTransaction.PatientId).FirstOrDefault();
        //    //        //                    billTransaction.Patient = pat;
        //    //        //                }
        //    //        //                //Sud:23Dec'18--making parallel thread call (asynchronous) to post to IRD. so that it won't stop the normal execution of logic.
        //    //        //                // BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext);
        //    //        //                Task.Run(() => BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext));
        //    //        //            }

        //    //        //            responseData.Status = "OK";
        //    //        //        }
        //    //        //        catch (Exception ex)
        //    //        //        {
        //    //        //            transactionSuccess = false;
        //    //        //            //rollback all changes if any error occurs
        //    //        //            dbContextTransaction.Rollback();
        //    //        //            throw ex;
        //    //        //        }
        //    //        //    }


        //    //        //    if (transactionSuccess)
        //    //        //    {
        //    //        //        ////Starts: This code is for Temporary solution for Checking and Updating the Invoice Number if there is duplication Found
        //    //        //        //List<SqlParameter> paramList = new List<SqlParameter>() {
        //    //        //        //        new SqlParameter("@fiscalYearId", billTransaction.FiscalYearId),
        //    //        //        //        new SqlParameter("@billingTransactionId", billTransaction.BillingTransactionId),
        //    //        //        //        new SqlParameter("@invoiceNumber", billTransaction.InvoiceNo)
        //    //        //        //    };

        //    //        //        //DataSet dataFromSP = DALFunctions.GetDatasetFromStoredProc("SP_BIL_Update_Duplicate_Invoice_If_Exists", paramList, billingDbContext);
        //    //        //        //var data = new List<object>();
        //    //        //        //if (dataFromSP.Tables.Count > 0)
        //    //        //        //{
        //    //        //        //    billTransaction.InvoiceNo = Convert.ToInt32(dataFromSP.Tables[0].Rows[0]["LatestInvoiceNumber"].ToString());
        //    //        //        //}
        //    //        //        ////Ends
        //    //        //        responseData.Results = billTransaction;
        //    //        //    }
        //    //        //}
        //    //        //else
        //    //        //{
        //    //        //    responseData.Status = "Failed";
        //    //        //    responseData.ErrorMessage = "billTransaction is invalid";
        //    //        //}
        //    //    }

        //    //    #region //the requested orders will get inside the BillItemRequisition Table 
        //    //    else if (reqType == "billItemsRequisition")
        //    //    {

        //    //        //List<BillItemRequisition> ItemRequests = DanpheJSONConvert.DeserializeObject<List<BillItemRequisition>>(ipDataString);

        //    //        //if (ItemRequests != null && ItemRequests.Count > 0)
        //    //        //{
        //    //        //    List<BillItemPrice> allBillingItems = billingDbContext.BillItemPrice.ToList();
        //    //        //    List<ServiceDepartmentModel> allSrvDepts = billingDbContext.ServiceDepartment.ToList();


        //    //        //    foreach (var req in ItemRequests)
        //    //        //    {
        //    //        //        ///Required for Doctor-Order, check if that can be passed from there (client side).

        //    //        //        ServiceDepartmentModel srvDepartment = null;
        //    //        //        if (req.ServiceDepartmentId != 0)
        //    //        //        {
        //    //        //            srvDepartment = (from srv in allSrvDepts
        //    //        //                             where (srv.ServiceDepartmentId == req.ServiceDepartmentId)
        //    //        //                             select srv).FirstOrDefault();
        //    //        //        }
        //    //        //        else if (req.DepartmentName != null || req.DepartmentName != "")
        //    //        //        {
        //    //        //            srvDepartment = (from billItem in billingDbContext.BillItemPrice
        //    //        //                             join srvDept in billingDbContext.ServiceDepartment on billItem.ServiceDepartmentId equals srvDept.ServiceDepartmentId
        //    //        //                             join dept in billingDbContext.Departments on srvDept.DepartmentId equals dept.DepartmentId
        //    //        //                             where (dept.DepartmentName.ToLower() == req.DepartmentName.ToLower()
        //    //        //                             && billItem.ItemName.ToLower() == req.ItemName.ToLower())
        //    //        //                             select srvDept).FirstOrDefault();
        //    //        //        }

        //    //        //        if (srvDepartment != null)
        //    //        //        {
        //    //        //            double? itemPrice = (from itmpr in allBillingItems
        //    //        //                                 where (itmpr.ItemName == req.ItemName && itmpr.ServiceDepartmentId == srvDepartment.ServiceDepartmentId)
        //    //        //                                 select itmpr.Price).FirstOrDefault();
        //    //        //            //Check -If ItemPrice is zero in db then take ItemPrice from client req
        //    //        //            double? itmPrice = itemPrice > 0 ? itemPrice : req.Price;
        //    //        //            //used in case of visit.procedurecode is providerId
        //    //        //            //getting item name from provider id
        //    //        //            if (string.IsNullOrEmpty(req.ItemName))
        //    //        //            {
        //    //        //                req.ItemName = (from itmpr in allBillingItems
        //    //        //                                where (itmpr.ProcedureCode == req.ProcedureCode)
        //    //        //                                select itmpr.ItemName).FirstOrDefault();
        //    //        //            }
        //    //        //            req.Price = Convert.ToDouble(itmPrice);
        //    //        //            req.ServiceDepartmentId = srvDepartment.ServiceDepartmentId;
        //    //        //            req.ServiceDepartment = srvDepartment.ServiceDepartmentName;
        //    //        //            req.BillStatus = "pending";
        //    //        //            req.CreatedOn = System.DateTime.Now;
        //    //        //            billingDbContext.BillItemRequisitions.Add(req);
        //    //        //        }
        //    //        //        else
        //    //        //        {
        //    //        //            throw new Exception("Service Department is not proper.");
        //    //        //        }
        //    //        //    }

        //    //        //    billingDbContext.SaveChanges();
        //    //        //    responseData.Results = ItemRequests;//check if we need to send back all the input array back to client.--sudarshan
        //    //        //}
        //    //    }
        //    //    #endregion
        //    //    #region save HTML file from server
        //    //    else if (reqType == "saveHTMLfile")
        //    //    {
        //    //        //ipDataString is input (HTML string)
        //    //        //if (ipDataString.Length > 0)
        //    //        //{
        //    //        //    //right now we are naming file as printerName + employeeId.html so that there is no mis match in htmlfile from different users.

        //    //        //    var fileName = PrinterName + currentUser.EmployeeId + ".html";
        //    //        //    //byte[] htmlbytearray = System.Text.Encoding.ASCII.GetBytes(ipDataString);
        //    //        //    //saving file to default folder, html file need to be delete after print is called.
        //    //        //    //System.IO.File.WriteAllBytes(@FilePath + fileName, htmlbytearray);
        //    //        //    System.IO.File.WriteAllText(@FilePath + fileName, ipDataString);

        //    //        //    responseData.Status = "OK";
        //    //        //    responseData.Results = 1;
        //    //        //}
        //    //    }
        //    //    #endregion

        //    //    //not used
        //    //    //sud: 10sept'18--to save billingtxn, txnitems, and visit for BillingPackage.
        //    //    else if (reqType == "postBillingTxnWithPackage")
        //    //    {
        //    //        //BillingTransactionModel billTransaction = DanpheJSONConvert.DeserializeObject<BillingTransactionModel>(ipDataString);
        //    //        //if (billTransaction != null)
        //    //        //{
        //    //        //    //if paymentmode is credit, paiddate and paidamount should be null
        //    //        //    //handle this in client side as well. 
        //    //        //    billTransaction.CreatedBy = currentUser.EmployeeId;
        //    //        //    billTransaction.PaidDate = DateTime.Now;
        //    //        //    billTransaction.PaidCounterId = billTransaction.CounterId;
        //    //        //    billTransaction.PaymentReceivedBy = billTransaction.CreatedBy;

        //    //        //    BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

        //    //        //    //assign AuditTrail properties in server side only
        //    //        //    billTransaction.CreatedOn = DateTime.Now;
        //    //        //    billTransaction.CreatedBy = currentUser.EmployeeId;
        //    //        //    billTransaction.FiscalYearId = fiscYear.FiscalYearId;
        //    //        //    billTransaction.InvoiceNo = BillingBL.GetInvoiceNumber(connString);
        //    //        //    billTransaction.InvoiceCode = BillingBL.InvoiceCode;

        //    //        //    //below will add both txn as well as TransactionItems.
        //    //        //    billingDbContext.BillingTransactions.Add(billTransaction);
        //    //        //    billingDbContext.SaveChanges();

        //    //        //    //if there's any OPD item, then we should add them to the visit table as well..
        //    //        //    if (billTransaction.BillingTransactionItems != null && billTransaction.BillingTransactionItems.Count > 0)
        //    //        //    {
        //    //        //        //IMPORTANT: ServiceDepartmentName and ItemName are HARD-CODED here, pls do not change from frontend.. 
        //    //        //        List<BillingTransactionItemModel> opdItems = billTransaction.BillingTransactionItems.Where(itm => itm.ServiceDepartmentName == "OPD" && itm.ItemName.ToLower() == "consultation charge").ToList();
        //    //        //        if (opdItems != null && opdItems.Count > 0)
        //    //        //        {
        //    //        //            VisitDbContext visDbContext = new VisitDbContext(connString);
        //    //        //            foreach (BillingTransactionItemModel txnItem in opdItems)
        //    //        //            {
        //    //        //                VisitModel vis = GetVisitModelFromBillTxnItem(txnItem, visDbContext);
        //    //        //                visDbContext.Visits.Add(vis);
        //    //        //                visDbContext.SaveChanges();

        //    //        //                //once visit is added, we have to update VisitId and RequisitionId fields of BillingTxnItemtable.
        //    //        //                billingDbContext.BillingTransactionItems.Attach(txnItem);
        //    //        //                txnItem.PatientVisitId = vis.PatientVisitId;
        //    //        //                txnItem.RequisitionId = vis.PatientVisitId;
        //    //        //                billingDbContext.Entry(txnItem).Property(a => a.PatientVisitId).IsModified = true;
        //    //        //                billingDbContext.Entry(txnItem).Property(a => a.RequisitionId).IsModified = true;
        //    //        //                billingDbContext.SaveChanges();

        //    //        //            }

        //    //        //        }
        //    //        //        //BillingTransactionBL.UpdateTxnItemsBillStatus(billingDbContext,
        //    //        //        //          billTransaction.BillingTransactionItems,
        //    //        //        //          billTransaction.BillStatus,
        //    //        //        //          currentUser.EmployeeId,
        //    //        //        //          billTransaction.CreatedOn,
        //    //        //        //          billTransaction.CounterId,
        //    //        //        //          billTransaction.BillingTransactionId);

        //    //        //    }


        //    //        //    billTransaction.FiscalYear = fiscYear.FiscalYearFormatted;//added: sud:5May'18

        //    //        //    if (realTimeRemoteSyncEnabled)
        //    //        //    {
        //    //        //        if (billTransaction.Patient == null)
        //    //        //        {
        //    //        //            PatientModel pat = billingDbContext.Patient.Where(p => p.PatientId == billTransaction.PatientId).FirstOrDefault();
        //    //        //            billTransaction.Patient = pat;
        //    //        //        }


        //    //        //        //Sud:23Dec'18--making parallel thread call (asynchronous) to post to IRD. so that it won't stop the normal execution of logic.
        //    //        //        //BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext);
        //    //        //        Task.Run(() => BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext));
        //    //        //    }


        //    //        //    //Starts: This code is for Temporary solution for Checking and Updating the Invoice Number if there is duplication Found
        //    //        //    List<SqlParameter> paramList = new List<SqlParameter>() {
        //    //        //                new SqlParameter("@fiscalYearId", billTransaction.FiscalYearId),
        //    //        //                new SqlParameter("@billingTransactionId", billTransaction.BillingTransactionId),
        //    //        //                new SqlParameter("@invoiceNumber", billTransaction.InvoiceNo)
        //    //        //            };

        //    //        //    DataSet dataFromSP = DALFunctions.GetDatasetFromStoredProc("SP_BIL_Update_Duplicate_Invoice_If_Exists", paramList, billingDbContext);
        //    //        //    var data = new List<object>();
        //    //        //    if (dataFromSP.Tables.Count > 0)
        //    //        //    {
        //    //        //        billTransaction.InvoiceNo = Convert.ToInt32(dataFromSP.Tables[0].Rows[0]["LatestInvoiceNumber"].ToString());
        //    //        //    }
        //    //        //    //Ends
        //    //        //    responseData.Results = billTransaction;
        //    //        //    responseData.Status = "OK";
        //    //        //}
        //    //        //else
        //    //        //{
        //    //        //    responseData.Status = "Failed";
        //    //        //    responseData.ErrorMessage = "billTransaction is invalid";
        //    //        //}
        //    //    }

        //    //    else if (reqType == "post-handover-denomination-detail")
        //    //    {
        //    //        //BillingHandoverModel handoverInfo = DanpheJSONConvert.DeserializeObject<BillingHandoverModel>(ipDataString);
        //    //        //var denominationDetails = handoverInfo.denomination;
        //    //        ////var hello = handoverInfo;
        //    //        ////Transaction Begins  
        //    //        //using (var dbContextTransaction = billingDbContext.Database.BeginTransaction())
        //    //        //{
        //    //        //    try
        //    //        //    {
        //    //        //        var TotalAmount = handoverInfo.denomination.Sum(a => a.CurrencyType * a.Quantity);

        //    //        //        if (handoverInfo != null)
        //    //        //        {
        //    //        //            BillingHandoverModel handover = new BillingHandoverModel();
        //    //        //            {
        //    //        //                //handover.HandoverId =;
        //    //        //                handover.UserId = currentUser.EmployeeId;
        //    //        //                handover.HandoverType = handoverInfo.HandoverType;
        //    //        //                handover.PreviousAmount = handoverInfo.PreviousAmount;
        //    //        //                handover.HandoverAmount = TotalAmount;
        //    //        //                handover.HandOverUserId = handoverInfo.UserId;
        //    //        //                handover.TotalAmount = handoverInfo.PreviousAmount + TotalAmount;
        //    //        //                handover.CounterId = handoverInfo.CounterId;
        //    //        //                handover.CreatedBy = currentUser.EmployeeId;
        //    //        //                handover.CreatedOn = System.DateTime.Now;
        //    //        //            }
        //    //        //            billingDbContext.Handover.Add(handover);
        //    //        //            billingDbContext.SaveChanges();

        //    //        //            if (denominationDetails.Count != 0)
        //    //        //            {
        //    //        //                for (int i = 0; i < denominationDetails.Count; i++)
        //    //        //                {
        //    //        //                    if (denominationDetails[i].Quantity != null && denominationDetails[i].Quantity != 0)
        //    //        //                    {
        //    //        //                        var handoverDetail = denominationDetails[i];
        //    //        //                        if (handoverDetail.DenominationId == null || handoverDetail.DenominationId == 0)
        //    //        //                        {
        //    //        //                            BillingDenominationModel denomination = new BillingDenominationModel();
        //    //        //                            {
        //    //        //                                denomination.HandoverId = handover.HandoverId;
        //    //        //                                denomination.CurrencyType = denominationDetails[i].CurrencyType;
        //    //        //                                denomination.Quantity = denominationDetails[i].Quantity;
        //    //        //                                denomination.Amount = denomination.CurrencyType * denomination.Quantity;
        //    //        //                            }
        //    //        //                            billingDbContext.Denomination.Add(denomination);
        //    //        //                        }
        //    //        //                    }
        //    //        //                    billingDbContext.SaveChanges();
        //    //        //                }
        //    //        //                dbContextTransaction.Commit(); //end of transaction
        //    //        //            }
        //    //        //        }
        //    //        //        else
        //    //        //        {
        //    //        //            responseData.Status = "Failed";
        //    //        //            responseData.ErrorMessage = "request type is incorrect.";
        //    //        //        }
        //    //        //    }
        //    //        //    catch (Exception ex)
        //    //        //    {
        //    //        //        //rollback all changes if any error occurs
        //    //        //        dbContextTransaction.Rollback();
        //    //        //        throw ex;
        //    //        //    }
        //    //        //}
        //    //    }

        //    //    else if (reqType == "post-handover-transaction-detail")
        //    //    {
        //    //        //DateTime txnDateTime = System.DateTime.Now;

        //    //        //BillingHandoverTransactionModel HandovertransactionInfo = DanpheJSONConvert.DeserializeObject<BillingHandoverTransactionModel>(ipDataString);
        //    //        //HandovertransactionInfo.CreatedBy = currentUser.EmployeeId;
        //    //        //HandovertransactionInfo.CreatedOn = txnDateTime;
        //    //        //HandovertransactionInfo.IsActive = true;
        //    //        //billingDbContext.HandoverTransaction.Add(HandovertransactionInfo);
        //    //        //billingDbContext.SaveChanges();

        //    //        //EmpCashTransactionModel empCashTransaction = new EmpCashTransactionModel();
        //    //        //empCashTransaction.TransactionType = "HandoverGiven";
        //    //        //empCashTransaction.ReferenceNo = HandovertransactionInfo.HandoverTxnId;
        //    //        //empCashTransaction.InAmount = 0;
        //    //        //empCashTransaction.OutAmount = HandovertransactionInfo.HandoverAmount;
        //    //        //empCashTransaction.EmployeeId = currentUser.EmployeeId;
        //    //        //empCashTransaction.TransactionDate = txnDateTime;
        //    //        //empCashTransaction.CounterID = HandovertransactionInfo.CounterId;

        //    //        //BillingBL.AddEmpCashTransaction(billingDbContext, empCashTransaction);
        //    //        //BillingBL.ReCalculateEmployeeDueAmount(billingDbContext, currentUser.EmployeeId, txnDateTime);

        //    //    }
        //    //}
        //    //catch (Exception ex)
        //    //{
        //    //    responseData.Status = "Failed";
        //    //    responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
        //    //}
        //    //return DanpheJSONConvert.SerializeObject(responseData, true);
        //}

        private void HandleException(Exception exception)
        {
            if (exception is DbUpdateException dbUpdateEx)
            {
                if (dbUpdateEx.InnerException != null
                        && dbUpdateEx.InnerException.InnerException != null)
                {
                    if (dbUpdateEx.InnerException.InnerException is SqlException sqlException)
                    {
                        switch (sqlException.Number)
                        {
                            case 2627:  // Unique constraint error
                                throw new Exception("Unique constraint error");
                            case 2601:  // Duplicated key row error
                                throw new Exception("Duplicate Key row error");

                            default:
                                throw exception;
                        }
                    }
                }
            }
        }


        [HttpPut]
        [Route("UpdateBillItemsReqsStatus")]
        public IActionResult UpdateBillItemsReqsStatus(string status)
        {

            //if (reqType == "billItemReqsStatus")
            //{

            string ipDataString = this.ReadPostData();

            Func<string> func = () => UpdateBillItemsRequisitionStatus(ipDataString, status);
            return InvokeHttpPutFunction<string>(func);
        }

        [HttpPut]
        [Route("UpdateBillStatusForCreditCancellation")]
        public IActionResult UpdateBillStatusForCreditCancellation(string status, string remarks, int CreatedBy)
        {
            //else if (reqType == "UpdateBillStatusForCreditCancellation")
            //{
            string ipDataString = this.ReadPostData();

            Func<int?> func = () => PutBillStatusForCreditCancellation(ipDataString, status, remarks, CreatedBy);
            return InvokeHttpPutFunction<int?>(func);
        }

        [HttpPut]
        [Route("CancelBillingTransactionItems")]
        public IActionResult CancelBillingTransactionItems()
        {
            //  else if (reqType == "cancelBillTxnItems")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);
            string ipDataString = this.ReadPostData();
            Func<object> func = () => CancelBillTxnItems(ipDataString, currentUser);
            return InvokeHttpPutFunction<object>(func);
        }

        [HttpPut]
        [Route("InvoicePrintCount")]
        public IActionResult InvoicePrintCount(int PrintCount, int billingTransactionId)
        {
            //  else if (reqType == "UpdatePrintCountafterPrint")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();

            Func<string> func = () => UpdateInvoicePrintCount(billingTransactionId, currentUser, PrintCount);
            return InvokeHttpPutFunction<string>(func);
        }

        [HttpPut]
        [Route("BillTxnItemDoctors")]
        public IActionResult BillTxnItemDoctors(string PrescriberObj, string PerformerObj)
        {
            //else if (reqType == "UpdateDoctorafterDoctorEdit")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();

            Func<object> func = () => UpdateDoctorAfterDoctorEdit(ipDataString, currentUser, PrescriberObj, PerformerObj);
            return InvokeHttpPutFunction<object>(func);
        }

        [HttpPut]
        [Route("UpdateDoctorafterDoctorEditRadiology")]
        public IActionResult UpdateDoctorafterDoctorEditRadiology(string PrescriberObj, string PerformerObj)
        {
            // else if (reqType == "UpdateDoctorafterDoctorEditRadiology")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();

            Func<object> func = () => UpdateDoctorForRadiology(ipDataString, currentUser, PrescriberObj, PerformerObj);
            return InvokeHttpPutFunction<object>(func);
        }

        [HttpPut]
        [Route("CancelOutpatientProvisionalItem")]
        public IActionResult CancelInPatientBillRequest()
        {
            // else if (reqType == "cancelInpatientBillRequest")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);
            string ipDataString = this.ReadPostData();
            Func<object> func = () => CancelOutpatientProvisionalItem(ipDataString, currentUser, Get_billingDbContext());
            return InvokeHttpPutFunction<object>(func);
        }


        [HttpPut]
        [Route("CancelInpatientItemFromWard")]
        public IActionResult CancelInpatientItemFromWard()
        {
            //else if (reqType == "cancelInpatientItemFromWard")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();
            Func<DataTable> func = () => CancelIpItemFromWard(ipDataString, currentUser);
            return InvokeHttpPutFunction<DataTable>(func);
        }

        [HttpPut]
        [Route("EditItemPriceQtyDiscAndProvider")]
        public IActionResult EditItemPriceQtyDiscAndProvider()
        {
            //else if (reqType == "EditItemPrice_Qty_Disc_Provider")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();

            Func<object> func = () => EditItemPriceQuantityDiscountAndProvider(currentUser, ipDataString);
            return InvokeHttpPutFunction<object>(func);
        }

        //[HttpPut]
        //[Route("UpdateItemGroupDiscount")]
        //public IActionResult UpdateItemGroupDiscount()
        //{
        //    //  else if (reqType == "item-group-discount")
        //    //{
        //    string ipDataString = this.ReadPostData();
        //    Func<object> func = () => UpdateItemGroupDisc(ipDataString);
        //    return InvokeHttpPutFunction<object>(func);
        //}

        [HttpPut]
        [Route("HandoverTransactionDetail")]
        public IActionResult PutHandoverTransactionDetail()
        {
            //else if (reqType == "update-handover-transaction-detail")
            //{
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
            string ipDataString = this.ReadPostData();

            Func<string> func = () => UpdateHandoverTxnDetail(currentUser, ipDataString);
            return InvokeHttpPutFunction<string>(func);
        }

        [HttpPut]
        [Route("UpdateProvisionalBillingTxnItems")]
        public ActionResult PutProvisionalBillingTxnItems()
        {
            RbacUser currentUser = HttpContext.Session.Get<RbacUser>(ENUM_SessionVariables.CurrentUser);
            string ipDataString = this.ReadPostData();

            Func<string> func = () => UpdateProvisionalBillingTxnItems(ipDataString, currentUser);
            return InvokeHttpPutFunction(func);
        }



        //[HttpPut]
        //public string Put()
        //{
        //    DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
        //    responseData.Status = "OK";
        //    try
        //    {
        //        BillingDbContext billingDbContext = new BillingDbContext(connString);
        //        LabDbContext labDbContext = new LabDbContext(connString);
        //        RadiologyDbContext radioDbContext = new RadiologyDbContext(connString);
        //        string str = this.ReadPostData();
        //        string reqType = this.ReadQueryStringData("reqType");
        //        string status = this.ReadQueryStringData("status");
        //        //string Provider = this.ReadQueryStringData("ProviderObj");
        //        string remarks = this.ReadQueryStringData("remarks");
        //        int billItemReqId = ToInt(this.ReadQueryStringData("billItemReqId"));
        //        int PrintCount = ToInt(this.ReadQueryStringData("PrintCount"));
        //        //int ProviderId = ToInt(this.ReadQueryStringData("ProviderId"));
        //        int CreatedBy = ToInt(this.ReadQueryStringData("CreatedBy"));
        //        int billingTransactionId = ToInt(this.ReadQueryStringData("billingTransactionId"));
        //        int invoiceNo = ToInt(this.ReadQueryStringData("invoiceNo"));
        //        int patientId = ToInt(this.ReadQueryStringData("patientId"));
        //        int insuranceProviderId = ToInt(this.ReadQueryStringData("patientId"));//check and update this--
        //        int updatedInsBalance = ToInt(this.ReadQueryStringData("updatedInsBalance"));


        //        string Performer = this.ReadQueryStringData("PerformerObj");
        //        string Prescriber = this.ReadQueryStringData("PrescriberObj");


        //        RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");


        //        if (reqType == "billItemReqsStatus")
        //        {
        //            //List<Int32> billItemReqIds = DanpheJSONConvert.DeserializeObject<List<Int32>>(str);
        //            //BillItemRequisition billItem = new BillItemRequisition();
        //            //foreach (var id in billItemReqIds)
        //            //{
        //            //    billItem = (from bill in billingDbContext.BillItemRequisitions
        //            //                where bill.BillItemRequisitionId == id
        //            //                select bill).FirstOrDefault();
        //            //    if (billItem != null)
        //            //    {
        //            //        billItem.BillStatus = status;
        //            //        billingDbContext.Entry(billItem).State = EntityState.Modified;
        //            //    }
        //            //}
        //            //billingDbContext.SaveChanges();
        //            //responseData.Results = "Bill Status Updated";
        //        }


        //        //-------------------------To update Cancel status of the Credit Bills--------------------------------- 
        //        else if (reqType == "UpdateBillStatusForCreditCancellation")
        //        {
        //            // List<Int32> BillTransactionItemIds = DanpheJSONConvert.DeserializeObject<List<Int32>>(str);
        //            //int? currTxnItmId = DanpheJSONConvert.DeserializeObject<int?>(str);

        //            //if (currTxnItmId != null && currTxnItmId.Value != 0)
        //            //{

        //            //    BillingTransactionItemModel dbBillTxnItem = billingDbContext.BillingTransactionItems
        //            //                               .Where(a => a.BillingTransactionItemId == currTxnItmId)
        //            //                               .FirstOrDefault<BillingTransactionItemModel>();
        //            //    if (dbBillTxnItem != null)
        //            //    {
        //            //        dbBillTxnItem.CancelRemarks = remarks;
        //            //        dbBillTxnItem.CancelledBy = CreatedBy;
        //            //        dbBillTxnItem.BillStatus = status.ToLower();
        //            //        dbBillTxnItem.CancelledOn = System.DateTime.Now;//added: sudarshan-25jul
        //            //        billingDbContext.Entry(dbBillTxnItem).State = EntityState.Modified;
        //            //    }
        //            //}
        //            ////foreach (var id in BillTransactionItemIds)
        //            ////{

        //            ////}
        //            //billingDbContext.SaveChanges();
        //            //responseData.Status = "OK";
        //            //responseData.Results = currTxnItmId;
        //        }
        //        //to cancel multiple items at once--needed in provisional items cancellation.sud:12May'18
        //        else if (reqType == "cancelBillTxnItems")
        //        {

        //            // List<Int32> BillTransactionItemIds = DanpheJSONConvert.DeserializeObject<List<Int32>>(str);
        //            //List<BillingTransactionItemModel> txnItemsToCancel = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(str);

        //            //if (txnItemsToCancel != null && txnItemsToCancel.Count > 0)
        //            //{
        //            //    //Transaction Begins  
        //            //    using (var dbContextTransaction = billingDbContext.Database.BeginTransaction())
        //            //    {
        //            //        try
        //            //        {
        //            //            for (int i = 0; i < txnItemsToCancel.Count; i++)
        //            //            {
        //            //                txnItemsToCancel[i] = BillingTransactionBL.UpdateTxnItemBillStatus(billingDbContext,
        //            //              txnItemsToCancel[i],
        //            //              "cancel",
        //            //              currentUser,
        //            //              DateTime.Now);
        //            //            }
        //            //            billingDbContext.SaveChanges();
        //            //            dbContextTransaction.Commit(); //end of transaction
        //            //            responseData.Status = "OK";
        //            //            responseData.Results = txnItemsToCancel;
        //            //        }
        //            //        catch (Exception ex)
        //            //        {
        //            //            //rollback all changes if any error occurs
        //            //            dbContextTransaction.Rollback();
        //            //            throw ex;
        //            //        }
        //            //    }
        //            //}
        //        }

        //        // Update the Print Count on Bill transaction after the Receipt print 
        //        else if (reqType == "UpdatePrintCountafterPrint")
        //        {


        //            //BillingTransactionModel dbBillPrintReq = billingDbContext.BillingTransactions
        //            //                        .Where(a => a.BillingTransactionId == billingTransactionId)
        //            //                        .FirstOrDefault<BillingTransactionModel>();
        //            //if (dbBillPrintReq != null)
        //            //{
        //            //    dbBillPrintReq.PrintCount = PrintCount;
        //            //    dbBillPrintReq.PrintedOn = System.DateTime.Now;
        //            //    dbBillPrintReq.PrintedBy = currentUser.EmployeeId;
        //            //    billingDbContext.Entry(dbBillPrintReq).State = EntityState.Modified;
        //            //}


        //            //billingDbContext.SaveChanges();
        //            //responseData.Status = "OK";
        //            //responseData.Results = "Print count updated successfully.";
        //        }
        //        //edit Doctor 

        //        else if (reqType == "UpdateDoctorafterDoctorEdit")
        //        {
        //            //int BillTxnItemId = DanpheJSONConvert.DeserializeObject<int>(str);

        //            //var PerformerObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Performer);
        //            //var PrescriberObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Prescriber);
        //            //BillingTransactionItemModel dbEditDoctor = billingDbContext.BillingTransactionItems
        //            //    .Where(a => a.BillingTransactionItemId == BillTxnItemId)
        //            //    .FirstOrDefault<BillingTransactionItemModel>();

        //            //LabRequisitionModel dbEditPrescriberInLabReq = billingDbContext.LabRequisitions
        //            //                                                .Where(a => a.RequisitionId == dbEditDoctor.RequisitionId)
        //            //                                                .FirstOrDefault();

        //            //if (dbEditDoctor != null)
        //            //{
        //            //    dbEditDoctor.PerformerName = PerformerObj != null ? PerformerObj.EmployeeName : null;
        //            //    dbEditDoctor.PerformerId = PerformerObj != null ? PerformerObj.EmployeeId : null;
        //            //    dbEditDoctor.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
        //            //    dbEditDoctor.ModifiedBy = currentUser.EmployeeId;
        //            //    dbEditDoctor.ModifiedOn = DateTime.Now;
        //            //    //billingDbContext.Entry(dbEditDoctor).State = EntityState.Modified;
        //            //    billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerId).IsModified = true;
        //            //    billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerName).IsModified = true;
        //            //    billingDbContext.Entry(dbEditDoctor).Property(p => p.PrescriberId).IsModified = true;
        //            //    billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedBy).IsModified = true;
        //            //    billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedOn).IsModified = true;
        //            //}
        //            //if (dbEditPrescriberInLabReq != null)
        //            //{
        //            //    dbEditPrescriberInLabReq.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
        //            //    dbEditPrescriberInLabReq.PrescriberName = PrescriberObj != null ? PrescriberObj.EmployeeName : null;

        //            //    billingDbContext.Entry(dbEditPrescriberInLabReq).Property(p => p.PrescriberId).IsModified = true;
        //            //    billingDbContext.Entry(dbEditPrescriberInLabReq).Property(p => p.PrescriberName).IsModified = true;

        //            //}
        //            //billingDbContext.SaveChanges();
        //            //radioDbContext.SaveChanges();
        //            //responseData.Status = "OK";
        //            //responseData.Results = BillTxnItemId;

        //        }
        //        // radiology Edit doctors
        //        else if (reqType == "UpdateDoctorafterDoctorEditRadiology")
        //        {
        //            //int BillTxnItemId = DanpheJSONConvert.DeserializeObject<int>(str);
        //            //int RequisitionId = Convert.ToInt32(this.ReadQueryStringData("RequisitionId"));
        //            //var PerformerObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Performer);
        //            //var PrescriberObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Prescriber);
        //            //BillingTransactionItemModel dbEditDoctor = billingDbContext.BillingTransactionItems
        //            //    .Where(a => a.BillingTransactionItemId == BillTxnItemId)
        //            //    .FirstOrDefault<BillingTransactionItemModel>();

        //            //using (var dbTransaction = billingDbContext.Database.BeginTransaction())
        //            //{
        //            //    try
        //            //    {
        //            //        if (dbEditDoctor != null)
        //            //        {
        //            //            dbEditDoctor.PerformerName = PerformerObj != null ? PerformerObj.EmployeeName : null;
        //            //            dbEditDoctor.PerformerId = PerformerObj != null ? PerformerObj.EmployeeId : null;
        //            //            dbEditDoctor.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
        //            //            dbEditDoctor.ModifiedBy = currentUser.EmployeeId;
        //            //            dbEditDoctor.ModifiedOn = DateTime.Now;

        //            //            billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerName).IsModified = true;
        //            //            billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerId).IsModified = true;
        //            //            billingDbContext.Entry(dbEditDoctor).Property(p => p.PrescriberId).IsModified = true;
        //            //            billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedBy).IsModified = true;
        //            //            billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedOn).IsModified = true;
        //            //            //billingDbContext.Entry(dbEditDoctor).State = EntityState.Modified;
        //            //            billingDbContext.SaveChanges();

        //            //        }

        //            //        if (RequisitionId > 0)
        //            //        {
        //            //            ImagingRequisitionModel dbEditDoctorRad = radioDbContext.ImagingRequisitions
        //            //               .Where(a => a.ImagingRequisitionId == RequisitionId)
        //            //               .FirstOrDefault<ImagingRequisitionModel>();

        //            //            if (dbEditDoctorRad != null)
        //            //            {
        //            //                dbEditDoctorRad.PrescriberName = PerformerObj != null ? PrescriberObj.EmployeeName : null;
        //            //                dbEditDoctorRad.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
        //            //                dbEditDoctorRad.ModifiedBy = currentUser.EmployeeId;
        //            //                dbEditDoctorRad.ModifiedOn = DateTime.Now;

        //            //                radioDbContext.Entry(dbEditDoctorRad).Property(p => p.PrescriberName).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRad).Property(p => p.PrescriberId).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRad).Property(p => p.ModifiedBy).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRad).Property(p => p.ModifiedOn).IsModified = true;
        //            //                //radioDbContext.Entry(dbEditDoctorRad).State = EntityState.Modified;
        //            //                radioDbContext.SaveChanges();

        //            //            }
        //            //        }
        //            //        if (RequisitionId > 0)
        //            //        {

        //            //            ImagingReportModel dbEditDoctorRadReport = radioDbContext.ImagingReports
        //            //                .Where(a => a.ImagingRequisitionId == RequisitionId)
        //            //                .FirstOrDefault<ImagingReportModel>();
        //            //            if (dbEditDoctorRadReport != null)
        //            //            {
        //            //                dbEditDoctorRadReport.PrescriberName = PrescriberObj != null ? PrescriberObj.EmployeeName : null;
        //            //                dbEditDoctorRadReport.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
        //            //                dbEditDoctorRadReport.PerformerId = PerformerObj != null ? PerformerObj.EmployeeId : null;
        //            //                dbEditDoctorRadReport.PerformerName = PerformerObj != null ? PerformerObj.EmployeeName : null;

        //            //                dbEditDoctorRadReport.ModifiedBy = currentUser.EmployeeId;
        //            //                dbEditDoctorRadReport.ModifiedOn = DateTime.Now;

        //            //                radioDbContext.Entry(dbEditDoctorRadReport).Property(p => p.PrescriberName).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRadReport).Property(p => p.PerformerId).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRadReport).Property(p => p.PerformerName).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRadReport).Property(p => p.ModifiedBy).IsModified = true;
        //            //                radioDbContext.Entry(dbEditDoctorRadReport).Property(p => p.ModifiedOn).IsModified = true;
        //            //                radioDbContext.SaveChanges();

        //            //                //radioDbContext.Entry(dbEditDoctorRadReport).State = EntityState.Modified;
        //            //            }

        //            //        }

        //            //        //billingDbContext.SaveChanges();
        //            //        //radioDbContext.SaveChanges();


        //            //        dbTransaction.Commit();
        //            //        responseData.Status = "OK";
        //            //        responseData.Results = BillTxnItemId;
        //            //    }
        //            //    catch (Exception ex)
        //            //    {
        //            //        dbTransaction.Rollback();
        //            //        throw ex;
        //            //    }
        //            //}
        //        }

        //        else if (reqType == "cancelInpatientBillRequest")
        //        {

        //            //BillingTransactionItemModel txnItmFromClient = DanpheJSONConvert.DeserializeObject<BillingTransactionItemModel>(str);


        //            //BillingTransactionItemModel billItem = billingDbContext.BillingTransactionItems
        //            //                                                .Where(itm =>
        //            //                                                        itm.RequisitionId == txnItmFromClient.RequisitionId
        //            //                                                        && itm.ItemId == txnItmFromClient.ItemId
        //            //                                                        && itm.PatientId == txnItmFromClient.PatientId
        //            //                                                        && itm.PatientVisitId == txnItmFromClient.PatientVisitId
        //            //                                                        && itm.BillingTransactionItemId == txnItmFromClient.BillingTransactionItemId
        //            //                                                    ).FirstOrDefault<BillingTransactionItemModel>();

        //            //billingDbContext.BillingTransactionItems.Attach(billItem);

        //            //billingDbContext.Entry(billItem).Property(a => a.BillStatus).IsModified = true;
        //            //billingDbContext.Entry(billItem).Property(a => a.CancelledBy).IsModified = true;
        //            //billingDbContext.Entry(billItem).Property(a => a.CancelledOn).IsModified = true;
        //            //billingDbContext.Entry(billItem).Property(a => a.CancelRemarks).IsModified = true;

        //            //billItem.BillStatus = ENUM_BillingStatus.cancel;// "cancel";
        //            //billItem.CancelledBy = currentUser.EmployeeId;
        //            //billItem.CancelledOn = System.DateTime.Now;
        //            //billItem.CancelRemarks = txnItmFromClient.CancelRemarks;
        //            //billingDbContext.SaveChanges();
        //            //responseData.Status = "OK";
        //            //responseData.Results = billItem;
        //        }

        //        else if (reqType == "cancelInpatientItemFromWard")
        //        {
        //            //BillingTransactionItemModel inpatientTest = DanpheJSONConvert.DeserializeObject<BillingTransactionItemModel>(str);
        //            //List<SqlParameter> paramList = new List<SqlParameter>(){
        //            //                                new SqlParameter("@BillingTransactionItemId", inpatientTest.BillingTransactionItemId),
        //            //                                new SqlParameter("@RequisitionId", (inpatientTest.RequisitionId.HasValue? inpatientTest.RequisitionId : 0)),
        //            //                                new SqlParameter("@IntegrationName", inpatientTest.ItemIntegrationName),
        //            //                                new SqlParameter("@UserId", currentUser.EmployeeId),
        //            //                                new SqlParameter("@Remarks", inpatientTest.CancelRemarks)
        //            //                            };

        //            //DataTable cancelItem = DALFunctions.GetDataTableFromStoredProc("SP_BillItemCancellation_From_Ward", paramList, billingDbContext);

        //            //billingDbContext.SaveChanges();
        //            //responseData.Status = "OK";
        //            //responseData.Results = cancelItem;

        //        }

        //        else if (reqType == "EditItemPrice_Qty_Disc_Provider")
        //        {

        //            //BillingTransactionItemModel txnItmFromClient = DanpheJSONConvert.DeserializeObject<BillingTransactionItemModel>(str);
        //            //txnItmFromClient.ModifiedBy = currentUser.EmployeeId;
        //            //BillingTransactionBL.UpdateBillingTransactionItems(billingDbContext, txnItmFromClient);
        //            //if (txnItmFromClient.ModifiedBy != null)
        //            //{
        //            //    var ModifiedByName = (from emp in billingDbContext.Employee
        //            //                          where emp.EmployeeId == txnItmFromClient.ModifiedBy
        //            //                          select emp.FirstName + " " + emp.LastName).FirstOrDefault();
        //            //    txnItmFromClient.ModifiedByName = ModifiedByName;
        //            //}
        //            //responseData.Status = "OK";
        //            //responseData.Results = txnItmFromClient;
        //        }

        //        else if (reqType == "item-group-discount")
        //        {
        //            //List<BillingTransactionItemModel> txnItems = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(str);
        //            //txnItems.ForEach(item =>
        //            //{
        //            //    BillingTransactionBL.UpdateBillingTransactionItems(billingDbContext, item);
        //            //});
        //            //responseData.Status = "OK";
        //        }

        //        else if (reqType == "update-handover-transaction-detail")
        //        {
        //            //BillingHandoverTransactionModel handoverTxnFromClient = DanpheJSONConvert.DeserializeObject<BillingHandoverTransactionModel>(str);

        //            //BillingHandoverTransactionModel handovertxnFromDb = billingDbContext.HandoverTransaction.Where(txn => txn.HandoverTxnId == handoverTxnFromClient.HandoverTxnId).FirstOrDefault();

        //            ////billingDbContext.HandoverTransaction.Attach(handovertxnFromDb);
        //            //handovertxnFromDb.HandoverByEmpId = handoverTxnFromClient.HandoverByEmpId;
        //            //handovertxnFromDb.CounterId = handoverTxnFromClient.CounterId;
        //            //handovertxnFromDb.HandoverType = handoverTxnFromClient.HandoverType;
        //            //handovertxnFromDb.BankName = handoverTxnFromClient.BankName;
        //            //handovertxnFromDb.VoucherNumber = handoverTxnFromClient.VoucherNumber;
        //            //handovertxnFromDb.VoucherDate = handoverTxnFromClient.VoucherDate;
        //            //handovertxnFromDb.HandoverAmount = handoverTxnFromClient.HandoverAmount;
        //            //handovertxnFromDb.DueAmount = handoverTxnFromClient.DueAmount;
        //            //handovertxnFromDb.HandoverRemarks = handoverTxnFromClient.HandoverRemarks;
        //            //handovertxnFromDb.ReceiveRemarks = handoverTxnFromClient.ReceiveRemarks;
        //            //handovertxnFromDb.ReceivedById = currentUser.EmployeeId;
        //            //handovertxnFromDb.ReceivedOn = System.DateTime.Now;
        //            //handovertxnFromDb.HandoverStatus = ENUM_HandOverStatus.Received;

        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverByEmpId).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.CounterId).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverType).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.BankName).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.VoucherNumber).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.VoucherDate).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverAmount).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.DueAmount).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverRemarks).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.ReceivedById).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.ReceivedOn).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.ReceiveRemarks).IsModified = true;
        //            //billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverStatus).IsModified = true;

        //            //billingDbContext.SaveChanges();
        //            //responseData.Status = "OK";
        //            //responseData.Results = "Handover Amount Recived.";
        //        }

        //        else
        //        {
        //            responseData.Status = "Failed";
        //            responseData.ErrorMessage = "request type is incorrect.";
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        responseData.Status = "Failed";
        //        responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
        //    }
        //    return DanpheJSONConvert.SerializeObject(responseData, true);
        //}

        //we're calculating days by subtracting AdmissionDate from DischargeDate
        //minimum days will be 1  <needs revision> sud: 20Aug'18
        private int CalculateBedStayForAdmission(AdmissionModel adm)
        {
            int totalDays = 1;

            if (adm != null)
            {

                DateTime admissionTime = adm.AdmissionDate;
                DateTime dischargeTime = (DateTime)(adm.DischargeDate != null ? adm.DischargeDate : DateTime.Now);

                int daysDiff = ((TimeSpan)(dischargeTime - admissionTime)).Days;
                if (daysDiff != 1)
                {
                    totalDays = daysDiff;
                }
            }
            return totalDays;
        }


        private VisitModel GetVisitModelFromBillTxnItem(BillingTransactionItemModel txnItem, VisitDbContext visitDbContext)
        {
            //get provider name from providerId
            string visitCode = VisitBL.CreateNewPatientVisitCode("outpatient", connString);

            VisitModel retModel = new VisitModel()
            {
                PatientId = txnItem.PatientId,
                PerformerId = txnItem.PerformerId,
                PerformerName = txnItem.PerformerName,
                VisitDate = DateTime.Now.Date,
                VisitTime = DateTime.Now.TimeOfDay,
                VisitCode = visitCode,
                VisitStatus = ENUM_VisitStatus.initiated,// "initiated",
                VisitType = ENUM_VisitType.outpatient,// "outpatient",
                BillingStatus = ENUM_BillingStatus.paid,// "paid",
                AppointmentType = ENUM_AppointmentType.New,// "New",
                IsActive = true,
                CreatedOn = DateTime.Now,
                CreatedBy = txnItem.CreatedBy
            };

            return retModel;
        }
        private object GetProvisionalItemsMappedByPatientId(int patientId, int schemeId)
        {
            PatientModel currPatient = _billingDbContext.Patient.Where(pat => pat.PatientId == patientId).FirstOrDefault();
            if (currPatient != null)
            {
                string subDivName = (from pat in _billingDbContext.Patient
                                     join countrySubdiv in _billingDbContext.CountrySubdivisions
                                     on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
                                     where pat.PatientId == currPatient.PatientId
                                     select countrySubdiv.CountrySubDivisionName
                                  ).FirstOrDefault();

                currPatient.CountrySubDivisionName = subDivName;

            }

            //for this request type, patientid comes as inputid.
            var patientProvisionalItems = (from bill in _billingDbContext.BillingTransactionItems.Include("ServiceDepartment")
                                  join priceCatServItm in _billingDbContext.BillItemsPriceCategoryMaps
                                  on new { serviceItemId = bill.ServiceItemId, priceCategoryId = bill.PriceCategoryId } equals new { serviceItemId = priceCatServItm.ServiceItemId, priceCategoryId = priceCatServItm.PriceCategoryId }
                                  where bill.BillStatus == ENUM_BillingStatus.provisional && (bill.BillingType == "outpatient" || bill.BillingType == "emergency") // "provisional" 
                                  && bill.PatientId == patientId && bill.DiscountSchemeId == schemeId
                                  && (bill.IsInsurance == false || bill.IsInsurance == null)
                                  select new BillingTransactionItems_DTO
                                  {
                                      BillingTransactionItemId = bill.BillingTransactionItemId,
                                      BillingTransactionId = bill.BillingTransactionId,
                                      PatientId = bill.PatientId,
                                      PerformerId = bill.PerformerId,
                                      PerformerName = bill.PerformerName,
                                      ServiceDepartmentId = bill.ServiceDepartmentId,
                                      ServiceDepartmentName = bill.ServiceDepartmentName,
                                      ServiceItemId = bill.ServiceItemId,
                                      PriceCategoryId = bill.PriceCategoryId,
                                      ItemCode = bill.ItemCode,
                                      IntegrationItemId = bill.IntegrationItemId,
                                      ProcedureCode = bill.ProcedureCode,
                                      ItemId = bill.ItemId,
                                      ItemName = bill.ItemName,
                                      Price = bill.Price,
                                      Quantity = bill.Quantity,
                                      SubTotal = bill.SubTotal,
                                      DiscountPercent = bill.DiscountPercent,
                                      DiscountPercentAgg = bill.DiscountPercentAgg,
                                      DiscountAmount = bill.DiscountAmount,
                                      Tax = bill.Tax,
                                      TotalAmount = bill.TotalAmount,
                                      BillStatus = bill.BillStatus,
                                      RequisitionId = bill.RequisitionId,
                                      RequisitionDate = bill.RequisitionDate,
                                      CounterDay = bill.CounterDay,
                                      CounterId = bill.CounterId,
                                      PaidDate = bill.PaidDate,
                                      ReturnStatus = bill.ReturnStatus,
                                      ReturnQuantity = bill.ReturnQuantity,
                                      CreatedBy = bill.CreatedBy,
                                      CreatedOn = bill.CreatedOn,
                                      Remarks = bill.Remarks,
                                      CancelRemarks = bill.CancelRemarks,
                                      TaxPercent = bill.TaxPercent,
                                      CancelledOn = bill.CancelledOn,
                                      CancelledBy = bill.CancelledBy,
                                      PrescriberId = bill.PrescriberId,
                                      PatientVisitId = bill.PatientVisitId,
                                      BillingPackageId = bill.BillingPackageId,
                                      TaxableAmount = bill.TaxableAmount,
                                      NonTaxableAmount = bill.NonTaxableAmount,
                                      PaymentReceivedBy = bill.PaymentReceivedBy,
                                      PaidCounterId = bill.PaidCounterId,
                                      BillingType = bill.BillingType,
                                      RequestingDeptId = bill.RequestingDeptId,
                                      ModifiedBy = bill.ModifiedBy,
                                      ModifiedOn = bill.ModifiedOn,
                                      IsCoPayment = bill.IsCoPayment,
                                      CoPaymentCashAmount = bill.CoPaymentCashAmount,
                                      CoPaymentCreditAmount = bill.CoPaymentCreditAmount,
                                      PatientInsurancePackageId = bill.PatientInsurancePackageId,
                                      ServiceDepartment = bill.ServiceDepartment,
                                      VisitType = bill.VisitType,
                                      PriceCategory = bill.PriceCategory,
                                      ProvisionalReceiptNo = bill.ProvisionalReceiptNo,
                                      ProvisionalFiscalYearId = bill.ProvisionalFiscalYearId,
                                      IsInsurance = bill.IsInsurance,
                                      DiscountSchemeId = bill.DiscountSchemeId,
                                      OrderStatus = bill.OrderStatus,
                                      LabTypeName = bill.LabTypeName,
                                      ReferredById = bill.ReferredById,
                                      DischargeStatementId = bill.DischargeStatementId,
                                      IsPriceChangeAllowed = priceCatServItm.IsPriceChangeAllowed
                                  }).ToList().OrderBy(b => b.BillingTransactionItemId);

            //clear patient object from Items, not needed since we're returning patient object separately
            if (patientProvisionalItems != null)
            {

                var allEmployees = (from emp in _billingDbContext.Employee
                                    join dep in _billingDbContext.Departments
                                    on emp.DepartmentId equals dep.DepartmentId into empDpt
                                    from emp2 in empDpt.DefaultIfEmpty()
                                    select new
                                    {
                                        EmployeeId = emp.EmployeeId,
                                        EmployeeName = emp.FirstName,
                                        DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
                                        DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
                                    }).ToList();

                BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

                //remove relational property of BillingTransactionItem//sud: 12May'18
                //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
                foreach (var item in patientProvisionalItems)
                {
                    //item.ProvFiscalYear = "2075 / 76";
                    item.ProvFiscalYear = fiscYear.FiscalYearFormatted;
                    item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
                            .Select(a => a.EmployeeName).FirstOrDefault();

                    item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
                         .Select(a => a.DepartmentName).FirstOrDefault();

                    //item.Patient = null;
                }


            }

            //create new anonymous type with patient information + Credit Items information : Anish:4May'18
            var patCreditDetails = new
            {
                Patient = currPatient,
                ProvisionalItems = patientProvisionalItems.OrderBy(itm => itm.CreatedOn).ToList()
            };
            return patCreditDetails;
        }

        private DataTable GetCreditNoteList(string FromDate, string ToDate)
        {
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@FromDate", FromDate),
                        new SqlParameter("@ToDate", ToDate)
                    };

            DataTable dtBilInvoiceReturnDetails = DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetCreditNoteListBetweenDateRange", paramList, _billingDbContext);

            return dtBilInvoiceReturnDetails;
        }
        //private object GetDuplicateBillByReceiptId(int invoiceNo, int fiscalYearId, bool getVisitInfo)
        //{
        //    var receipt = (from bill in _billingDbContext.BillingTransactions.Include("BillingTransactionItems")
        //                   join pat in _billingDbContext.Patient on bill.PatientId equals pat.PatientId
        //                   join fy in _billingDbContext.BillingFiscalYears on bill.FiscalYearId equals fy.FiscalYearId
        //                   join user in _billingDbContext.User on bill.CreatedBy equals user.EmployeeId
        //                   join cntrSbDvsn in _billingDbContext.CountrySubdivisions on pat.CountrySubDivisionId equals cntrSbDvsn.CountrySubDivisionId
        //                   where bill.InvoiceNo == invoiceNo && bill.FiscalYearId == fiscalYearId
        //                   select new
        //                   {
        //                       Patient = pat,
        //                       Transaction = bill,
        //                       TransactionItems = bill.BillingTransactionItems,
        //                       FiscalYearObj = fy,
        //                       UserObject = user,
        //                       CountrySubDivObj = cntrSbDvsn
        //                       //.Include("BillingTransactionItems").Include("Patient")
        //                   }).FirstOrDefault();

        //    string userName = "";
        //    var patVisitInfo = new object();
        //    var patientLatestVisitInfo = new object();
        //    if (receipt != null)
        //    {

        //        //Yubraj :: 22nd April '19 For finding creditOrganizationName to show in duplicate print
        //        if (receipt.Transaction != null)
        //        {
        //            receipt.Transaction.OrganizationName = _billingDbContext.CreditOrganization.Where(a => a.OrganizationId == receipt.Transaction.OrganizationId).Select(b => b.OrganizationName).FirstOrDefault();
        //        }

        //        //sud:4May'21--Set BillingTransactionObject and Patient Object of TxnItems as null.
        //        //those objects gets automatically assigned during the join above.
        //        //don't need them since we're separately taking those objects below.
        //        if (receipt.TransactionItems != null && receipt.TransactionItems.Count > 0)
        //        {
        //            receipt.TransactionItems.ForEach(txnItm =>
        //            {
        //                txnItm.BillingTransaction = null;
        //                txnItm.Patient = null;
        //            });
        //        }

        //        userName = receipt.UserObject != null ? receipt.UserObject.UserName : "";
        //        receipt.Transaction.FiscalYear = receipt.FiscalYearObj != null ? receipt.FiscalYearObj.FiscalYearFormatted : "";
        //        receipt.Patient.CountrySubDivisionName = receipt.CountrySubDivObj != null ? receipt.CountrySubDivObj.CountrySubDivisionName : "";

        //        //set navigational properties of Patient, BillingTransaction and BillingTransactioItems to null.
        //        //mandatory, otherwise the response size will be too big.
        //        receipt.Transaction.Patient = null;
        //        receipt.Transaction.BillingTransactionItems = null;



        //        //ashim:29Sep2018 rename this as patADTInfo:

        //        if (getVisitInfo)
        //        {
        //            AdmissionDbContext admissionDbContext = new AdmissionDbContext(connString);
        //            patVisitInfo = (from admission in admissionDbContext.Admissions
        //                            where admission.PatientId == receipt.Patient.PatientId
        //                            select new
        //                            {
        //                                LastAdmissionDate = admission.AdmissionDate,
        //                                LastDischargedDate = admission.DischargeDate
        //                            }).OrderByDescending(a => a.LastAdmissionDate).FirstOrDefault();
        //        }

        //        //ashim: 29Sep2018 : To get patient's latest visit info incase of copy from earlier receipt in return receipt page.

        //        if (getVisitInfo)
        //        {
        //            patientLatestVisitInfo = (from visit in _billingDbContext.Visit
        //                                      where visit.PatientId == receipt.Patient.PatientId
        //                                      select new
        //                                      {
        //                                          LatestVisitType = visit.VisitType,
        //                                          LatestVisitId = visit.PatientVisitId,
        //                                          LatestVisitCode = visit.VisitCode,
        //                                          QueueNo = visit.QueueNo
        //                                      }).OrderByDescending(a => a.LatestVisitId).FirstOrDefault();
        //        }
        //    }
        //    //Returning new anonymous type. 
        //    return new
        //    {
        //        Patient = receipt.Patient,
        //        Transaction = receipt.Transaction,
        //        TransactionItems = receipt.TransactionItems,
        //        UserName = userName,
        //        //ashim: 29Sep2018: Rename this as AdmissionInfo
        //        VisitInfo = patVisitInfo,
        //        //ashim: 29Sep2018 : To get patient's latest visit info incase of copy from earlier receipt in return receipt page.
        //        LatestPatientVisitInfo = patientLatestVisitInfo
        //    };
        //}

        //private object GetInPatientForPartialBilling(int patientVisitId)
        //{
        //    var visitNAdmission = (from visit in _billingDbContext.Visit.Include(v => v.Admission)
        //                           where visit.PatientVisitId == patientVisitId
        //                           select visit).FirstOrDefault();

        //    var patientDetail = (from pat in _billingDbContext.Patient
        //                         join sub in _billingDbContext.CountrySubdivisions on pat.CountrySubDivisionId equals sub.CountrySubDivisionId
        //                         where pat.PatientId == visitNAdmission.PatientId
        //                         select new PatientDetailVM
        //                         {
        //                             PatientId = pat.PatientId,
        //                             PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
        //                             HospitalNo = pat.PatientCode,
        //                             DateOfBirth = pat.DateOfBirth,
        //                             Gender = pat.Gender,
        //                             Address = pat.Address,
        //                             ContactNo = pat.PhoneNumber,
        //                             InpatientNo = visitNAdmission.VisitCode,
        //                             CountrySubDivision = sub.CountrySubDivisionName,
        //                             PANNumber = pat.PANNumber
        //                         }).FirstOrDefault();
        //    return patientDetail;
        //}

        //private object GetDuplicateProvisionalBillsByReceiptId(int receiptNo, int fiscalYearId)
        //{
        //    BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

        //    var patId = (from itm in _billingDbContext.BillingTransactionItems
        //                 where itm.ProvisionalReceiptNo == receiptNo
        //                 && itm.ProvisionalFiscalYearId == fiscalYearId
        //                 select itm.PatientId).FirstOrDefault();

        //    var patientInfo = (from pat in _billingDbContext.Patient where pat.PatientId == patId select pat).FirstOrDefault();
        //    var receipt = (from bill in _billingDbContext.BillingTransactionItems
        //                   where bill.ProvisionalReceiptNo == receiptNo && bill.ProvisionalFiscalYearId == fiscalYearId
        //                   //&& bill.IsInsuranceBilling == isInsuranceReceipt
        //                   select bill).ToList();

        //    //if (receipt.Transaction != null)
        //    //{
        //    //    receipt.Transaction.OrganizationName = dbContext.CreditOrganization.Where(a => a.OrganizationId == receipt.Transaction.OrganizationId).Select(b => b.OrganizationName).FirstOrDefault();
        //    //}
        //    var CountrySubDivisionName = (from sub in _billingDbContext.CountrySubdivisions where sub.CountrySubDivisionId == patientInfo.CountrySubDivisionId select sub.CountrySubDivisionName).FirstOrDefault();
        //    patientInfo.CountrySubDivisionName = CountrySubDivisionName;
        //    //Returning new anonymous type. 
        //    return new
        //    {
        //        FiscalYear = fiscYear.FiscalYearFormatted,
        //        ReceiptNo = receiptNo,
        //        Patient = patientInfo,
        //        Transaction = receipt
        //    };
        //}

        private object GetPatientReturnedInvoices_Old(int patientId)
        {
            var invoices = (from bill in _billingDbContext.BillingTransactions.Include("BillingTransactionItems").Include("Patient")
                            join fiscalYear in _billingDbContext.BillingFiscalYears on bill.FiscalYearId equals fiscalYear.FiscalYearId
                            where bill.ReturnStatus == true && bill.PatientId == patientId
                            select new
                            {
                                bill.BillingTransactionId,
                                bill.PatientId,
                                bill.PatientVisitId,
                                bill.PaymentMode,
                                bill.PaymentDetails,
                                bill.DiscountPercent,
                                bill.Remarks,
                                InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + bill.InvoiceCode + bill.InvoiceNo.ToString(),
                                bill.TotalAmount,
                                bill.BillingTransactionItems,
                                bill.CreatedOn,
                                bill.PackageId,
                                bill.PackageName,
                                bill.TransactionType,
                                bill.LabTypeName
                            }).OrderByDescending(a => a.CreatedOn).Take(5).ToList();

            //if any of invoices contains OPD items, then remove it: sud:7Aug'18--needed since OPD are billed only from VISIT MODULE, not Billing.. 
            if (invoices != null && invoices.Count > 0)
            {

                invoices.RemoveAll(inv => inv.BillingTransactionItems.Where(itm => inv.PackageId == null && itm.ServiceDepartmentName == "OPD").Count() > 0);
            }
            return invoices;
        }
        private object GetPatientsPastBillSummary(int patientId, int? schemeId = null)
        {

            //Part-1: Get Deposit Balance of this patient. 
            //get all deposit related transactions of this patient. and sum them acc to DepositType groups.
            var patientAllDepositTxns = (from bill in _billingDbContext.BillingDeposits
                                         where bill.PatientId == patientId && bill.IsActive == true//here PatientId comes as InputId from client.
                                         group bill by new { bill.PatientId, bill.TransactionType } into p
                                         select new
                                         {
                                             //DepositType = p.Key.DepositType,
                                             TransactionType = p.Key.TransactionType,
                                             SumInAmount = p.Sum(a => a.InAmount),
                                             SumOutAmount = p.Sum(a => a.OutAmount)
                                         }).ToList();
            //separate sum of each deposit types and calculate deposit balance.
            decimal totalDepositAmt, totalDepositDeductAmt, totalDepositReturnAmt, currentDepositBalance;
            currentDepositBalance = totalDepositAmt = totalDepositDeductAmt = totalDepositReturnAmt = 0;

            if (patientAllDepositTxns.Where(bil => bil.TransactionType.ToLower() == "deposit").FirstOrDefault() != null)
            {
                totalDepositAmt = patientAllDepositTxns.Where(bil => bil.TransactionType.ToLower() == "deposit").FirstOrDefault().SumInAmount;
            }
            if (patientAllDepositTxns.Where(bil => bil.TransactionType.ToLower() == "depositdeduct").FirstOrDefault() != null)
            {
                totalDepositDeductAmt = patientAllDepositTxns.Where(bil => bil.TransactionType.ToLower() == "depositdeduct").FirstOrDefault().SumOutAmount;
            }
            if (patientAllDepositTxns.Where(bil => bil.TransactionType.ToLower() == "returndeposit").FirstOrDefault() != null)
            {
                totalDepositReturnAmt = patientAllDepositTxns.Where(bil => bil.TransactionType.ToLower() == "returndeposit").FirstOrDefault().SumOutAmount;
            }
            //below is the formula to calculate deposit balance.
            currentDepositBalance = totalDepositAmt - totalDepositDeductAmt - totalDepositReturnAmt;

            //Part-2: Get Total Provisional Items
            //for this request type, patientid comes as inputid.
            var patProvisional = (from bill in _billingDbContext.BillingTransactionItems
                                      //sud: 4May'18 changed unpaid to provisional
                                  where bill.PatientId == patientId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
                                  && bill.IsInsurance == false && (schemeId != null ? bill.DiscountSchemeId == schemeId : bill.DiscountSchemeId == bill.DiscountSchemeId)
                                  group bill by new { bill.PatientId } into p
                                  select new
                                  {
                                      TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
                                  }).FirstOrDefault();

            var patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;


            var patCredits = (from bill in _billingDbContext.BillingTransactionItems
                              join retItems in _billingDbContext.BillInvoiceReturnItems
                              on bill.BillingTransactionItemId equals retItems.BillingTransactionItemId into g
                              from items in g.DefaultIfEmpty()
                              where bill.PatientId == patientId
                              && bill.BillStatus == ENUM_BillingStatus.unpaid
                              && (schemeId != null ? bill.DiscountSchemeId == schemeId : bill.DiscountSchemeId == bill.DiscountSchemeId)
                              && (bill.IsInsurance == false)
                              select new
                              {
                                  TotalAmount = bill.TotalAmount,
                                  TotalReturnAmount = items != null ? items.RetTotalAmount : 0
                              }).ToList();

            //var patCreditAmt = patCredits != null ? patCredits.TotalUnPaidAmt : 0;
            var patCreditAmt = patCredits != null ? patCredits.Sum(a => a.TotalAmount) - patCredits.Sum(b => b.TotalReturnAmount) : 0;

            //Part-4: Get Total Paid Amount
            var patPaid = (from bill in _billingDbContext.BillingTransactionItems
                           where bill.PatientId == patientId
                           && bill.BillStatus == ENUM_BillingStatus.paid // "paid"
                           && (bill.ReturnStatus == false || bill.ReturnStatus == null)
                           && (bill.IsInsurance == false)
                           && (schemeId != null ? bill.DiscountSchemeId == schemeId : bill.DiscountSchemeId == bill.DiscountSchemeId)
                           group bill by new { bill.PatientId } into p
                           select new
                           {
                               TotalPaidAmt = p.Sum(a => a.TotalAmount)
                           }).FirstOrDefault();

            var patPaidAmt = patPaid != null ? patPaid.TotalPaidAmt : 0;

            //Part-5: get Total Discount Amount


            var patDiscount = (from bill in _billingDbContext.BillingTransactionItems
                               join retItems in _billingDbContext.BillInvoiceReturnItems
                               on bill.BillingTransactionItemId equals retItems.BillingTransactionItemId into g
                               from items in g.DefaultIfEmpty()
                               where bill.PatientId == patientId
                               && (schemeId != null ? bill.DiscountSchemeId == schemeId : bill.DiscountSchemeId == bill.DiscountSchemeId)
                               //&& bill.BillStatus == ENUM_BillingStatus.unpaid
                               && (bill.IsInsurance == false)

                               select new
                               {
                                   TotalDiscountAmount = bill.DiscountAmount,
                                   TotalReturnDiscountAmount = items != null ? items.RetDiscountAmount : 0
                               }).ToList();

            var patDiscountAmt = patDiscount != null ? patDiscount.Sum(a => a.TotalDiscountAmount) - patDiscount.Sum(b => b.TotalReturnDiscountAmount) : 0;

            //Part-6: get Total Cancelled Amount
            var patCancel = (from bill in _billingDbContext.BillingTransactionItems
                                 //sud: 4May'18 changed unpaid to provisional
                             where bill.PatientId == patientId
                             && bill.BillStatus == ENUM_BillingStatus.cancel// "cancel"
                             && (bill.IsInsurance == false)
                             && (schemeId != null ? bill.DiscountSchemeId == schemeId : bill.DiscountSchemeId == bill.DiscountSchemeId)
                             group bill by new { bill.PatientId } into p
                             select new
                             {
                                 TotalPaidAmt = p.Sum(a => a.TotalAmount)
                             }).FirstOrDefault();

            var patCancelAmt = patCancel != null ? patCancel.TotalPaidAmt : 0;

            //Part-7: get Total Cancelled Amount

            var patReturn = (from rtnItems in _billingDbContext.BillInvoiceReturnItems
                             where rtnItems.PatientId == patientId
                             && (rtnItems.IsInsurance == false)
                             && (rtnItems.BillStatus == ENUM_BillingStatus.paid) //shankar
                             group rtnItems by new { rtnItems.PatientId } into p
                             select new
                             {
                                 TotalAmt = p.Sum(a => a.RetTotalAmount)
                             }).FirstOrDefault();

            var patReturnAmt = patReturn != null ? patReturn.TotalAmt : 0;


            var patDepositRefund = _billingDbContext.BillingDeposits.Where(a => a.TransactionType == ENUM_DepositTransactionType.ReturnDeposit && a.PatientId == patientId)
                                                                    .Select(s => s.OutAmount)
                                                                    .DefaultIfEmpty(0)
                                                                    .Sum();

            //Part-7: Return a single object with Both Balances (Deposit and Credit).
            var patBillHistory = new
            {
                PatientId = patientId,
                PaidAmount = patPaidAmt,
                DiscountAmount = patDiscountAmt,
                CancelAmount = patCancelAmt,
                ReturnedAmount = patReturnAmt,
                CreditAmount = patCreditAmt,
                ProvisionalAmt = patProvisionalAmt,
                TotalDue = patCreditAmt + patProvisionalAmt,
                DepositBalance = currentDepositBalance,
                BalanceAmount = currentDepositBalance - (decimal)(patCreditAmt + patProvisionalAmt),
                RefundAmount = patDepositRefund
            };

            return patBillHistory;
        }

        private object GetInsurancePatientPastBillSummary(int patientId)
        {
            //Part-1: Get Insurance Balance of this patient. 

            var patientInsuranceBalance = (from bill in _billingDbContext.Insurances where bill.PatientId == patientId select bill.CurrentBalance).FirstOrDefault();
            double? currentInsurancebalance;
            currentInsurancebalance = patientInsuranceBalance;

            //Part-2: Get Total Provisional Items
            //for this request type, patientid comes as inputid.
            var patProvisional = (from bill in _billingDbContext.BillingTransactionItems
                                  where bill.PatientId == patientId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
                                  && bill.IsInsurance == true
                                  group bill by new { bill.PatientId } into p
                                  select new
                                  {
                                      TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
                                  }).FirstOrDefault();

            var patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;



            //Part-3: Return a single object with Both Balances (Deposit and Credit).
            //exclude returned invoices from credit total
            var patCredits = _billingDbContext.BillingTransactions
                            .Where(b => b.PatientId == patientId && b.BillStatus != "paid" && b.ReturnStatus != true && b.IsInsuranceBilling == true)
                             .Sum(b => b.TotalAmount);

            double patCreditAmt = patCredits;


            //Part-4: Return a single object with Both Balances (Deposit and Credit).
            var patInsuranceBillHistory = new
            {
                PatientId = patientId,
                CreditAmount = patCreditAmt,
                ProvisionalAmt = patProvisionalAmt,
                TotalDue = patCreditAmt + patProvisionalAmt,
                DepositBalance = currentInsurancebalance,
                BalanceAmount = currentInsurancebalance - (patCreditAmt + patProvisionalAmt)
            };


            return patInsuranceBillHistory;
        }
        private object GetPatientBillHistoryDetail(int patientId)
        {
            var invoices = (from txn in _billingDbContext.BillingTransactions
                            join fiscalYear in _billingDbContext.BillingFiscalYears on txn.FiscalYearId equals fiscalYear.FiscalYearId
                            where txn.PatientId == patientId
                            //&& (txn.IsInsuranceBilling == false || txn.IsInsuranceBilling == null)
                            select new
                            {
                                TransactionId = txn.BillingTransactionId,
                                Date = txn.CreatedOn,
                                InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + txn.InvoiceCode + txn.InvoiceNo.ToString(),
                                Amount = txn.TotalAmount,
                                BillStatus = txn.BillStatus,
                                PaymentMode = txn.PaymentMode,
                                IsReturned = txn.ReturnStatus,
                                IsInsuranceBilling = txn.IsInsuranceBilling,
                                ReceivedAmount = txn.ReceivedAmount
                            }).OrderBy(a => a.BillStatus == "paid").ThenByDescending(a => a.Date).ToList();

            var provisionalItems = (from txnItm in _billingDbContext.BillingTransactionItems
                                    join srvDept in _billingDbContext.ServiceDepartment on txnItm.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                                    join fiscalYear in _billingDbContext.BillingFiscalYears on txnItm.ProvisionalFiscalYearId equals fiscalYear.FiscalYearId
                                    where txnItm.PatientId == patientId && txnItm.BillStatus == ENUM_BillingStatus.provisional // "provisional"
                                    && (txnItm.IsInsurance == false || txnItm.IsInsurance == null)
                                    select new
                                    {
                                        TransactionItemId = txnItm.BillingTransactionItemId,
                                        Date = txnItm.CreatedOn,
                                        ServiceDepartmentName = srvDept.ServiceDepartmentName,
                                        ItemName = txnItm.ItemName,
                                        Amount = txnItm.TotalAmount,
                                        ReceiptNo = fiscalYear.FiscalYearFormatted + "-" + "PR" + txnItm.ProvisionalReceiptNo.ToString(),
                                        BillStatus = txnItm.BillStatus,
                                        Quantity = txnItm.Quantity,
                                        Price = txnItm.Price,
                                        Discount = txnItm.DiscountAmount,
                                        Tax = txnItm.Tax,
                                        SubTotal = txnItm.SubTotal
                                    }).OrderByDescending(a => a.Date).ToList();
            var paidItems = (from itms in _billingDbContext.BillingTransactionItems
                             join srvDept in _billingDbContext.ServiceDepartment on itms.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                             join billtxn in _billingDbContext.BillingTransactions on itms.BillingTransactionId equals billtxn.BillingTransactionId
                             join fiscalYear in _billingDbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
                             where itms.PatientId == patientId && itms.BillStatus == ENUM_BillingStatus.paid// "paid"
                             && (itms.IsInsurance == false || itms.IsInsurance == null)
                             select new
                             {
                                 TransactionItemId = itms.BillingTransactionItemId,
                                 Date = billtxn.CreatedOn,
                                 InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
                                 ServiceDepartmentName = srvDept.ServiceDepartmentName,
                                 ItemName = itms.ItemName,
                                 Amount = itms.TotalAmount,
                                 Quantity = itms.Quantity,
                                 Price = itms.Price,
                                 Discount = itms.DiscountAmount,
                                 Tax = itms.Tax,
                                 SubTotal = itms.SubTotal,

                             }).OrderByDescending(a => a.Date).ToList();

            var unpaidItems = (from crdItems in _billingDbContext.BillingTransactionItems
                               join srvDept in _billingDbContext.ServiceDepartment on crdItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                               join billtxn in _billingDbContext.BillingTransactions on crdItems.BillingTransactionId equals billtxn.BillingTransactionId
                               join fiscalYear in _billingDbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
                               where crdItems.PatientId == patientId && crdItems.BillStatus == ENUM_BillingStatus.unpaid //"unpaid"
                               && (crdItems.IsInsurance == false || crdItems.IsInsurance == null)
                               select new
                               {
                                   TransactionItemId = crdItems.BillingTransactionItemId,
                                   Date = crdItems.CreatedOn,
                                   InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
                                   ServiceDepartmentName = srvDept.ServiceDepartmentName,
                                   ItemName = crdItems.ItemName,
                                   Amount = crdItems.TotalAmount,
                                   Quantity = crdItems.Quantity,
                                   Discount = crdItems.DiscountAmount,
                                   Tax = crdItems.Tax,
                                   SubTotal = crdItems.SubTotal,

                               }).OrderByDescending(a => a.Date).ToList();


            var returnedItems = (from rtnItems in _billingDbContext.BillInvoiceReturnItems
                                 join srvDept in _billingDbContext.ServiceDepartment on rtnItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                                 join billRtnTxn in _billingDbContext.BillInvoiceReturns on rtnItems.BillReturnId equals billRtnTxn.BillReturnId
                                 join fiscalYear in _billingDbContext.BillingFiscalYears on billRtnTxn.FiscalYearId equals fiscalYear.FiscalYearId
                                 where rtnItems.PatientId == patientId
                                 && (rtnItems.IsInsurance == false || rtnItems.IsInsurance == null)
                                 select new
                                 {
                                     TransactionItemId = rtnItems.BillingTransactionItemId,
                                     Date = rtnItems.CreatedOn,
                                     InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billRtnTxn.InvoiceCode + billRtnTxn.RefInvoiceNum.ToString(),
                                     ServiceDepartmentName = srvDept.ServiceDepartmentName,
                                     ItemName = rtnItems.ItemName,
                                     Amount = rtnItems.RetTotalAmount,
                                     Quantity = rtnItems.RetQuantity,
                                     Discount = rtnItems.RetDiscountAmount,
                                     Tax = rtnItems.RetTaxAmount,
                                     SubTotal = rtnItems.RetSubTotal,
                                     BillStatus = billRtnTxn.BillStatus,
                                     PaymentMode = billRtnTxn.PaymentMode,

                                 }).OrderByDescending(a => a.Date).ToList();



            var insuranceItems = (from insrItems in _billingDbContext.BillingTransactionItems
                                  join srvDept in _billingDbContext.ServiceDepartment on insrItems.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                                  join billtxn in _billingDbContext.BillingTransactions on insrItems.BillingTransactionId equals billtxn.BillingTransactionId
                                  join fiscalYear in _billingDbContext.BillingFiscalYears on billtxn.FiscalYearId equals fiscalYear.FiscalYearId
                                  where insrItems.PatientId == patientId && billtxn.IsInsuranceBilling == true
                                  select new
                                  {
                                      TransactionItemId = insrItems.BillingTransactionItemId,
                                      Date = insrItems.CreatedOn,
                                      InvoiceNo = fiscalYear.FiscalYearFormatted + "-" + billtxn.InvoiceCode + billtxn.InvoiceNo.ToString(),
                                      ServiceDepartmentName = srvDept.ServiceDepartmentName,
                                      ItemName = insrItems.ItemName,
                                      Amount = insrItems.TotalAmount,
                                      Quantity = insrItems.Quantity,
                                      Discount = insrItems.DiscountAmount,
                                      Tax = insrItems.Tax,
                                      SubTotal = insrItems.SubTotal,

                                  }).OrderByDescending(a => a.Date).ToList();

            var settlements = (from settlement in _billingDbContext.BillSettlements
                               where settlement.PatientId == patientId
                               select new
                               {
                                   SettlementId = settlement.SettlementId,
                                   SettlementReceipt = "SR" + settlement.SettlementReceiptNo.ToString(),
                                   SettlementDate = settlement.SettlementDate,
                                   PaidAmount = settlement.PaidAmount,
                               }).OrderByDescending(a => a.SettlementDate).ToList();
            var deposits = (from deposit in _billingDbContext.BillingDeposits
                            join biltxn in _billingDbContext.BillingTransactions on deposit.BillingTransactionId equals biltxn.BillingTransactionId into biltxnTemp
                            from billingtxn in biltxnTemp.DefaultIfEmpty()
                            join settlement in _billingDbContext.BillSettlements on deposit.SettlementId equals settlement.SettlementId into settlementTemp
                            from billSettlement in settlementTemp.DefaultIfEmpty()
                            where deposit.PatientId == patientId
                            select new
                            {
                                DepositId = deposit.DepositId,
                                ReceiptNum = deposit.ReceiptNo, //used only to check whether No exists or not in client side
                                ReceiptNo = "DR" + deposit.ReceiptNo.ToString(),
                                Date = deposit.CreatedOn,
                                //Amount = deposit.Amount,
                                InAmount = deposit.InAmount,
                                OutAmount = deposit.OutAmount,
                                Balance = deposit.DepositBalance,
                                TransactionType = deposit.TransactionType,
                                SettlementInvoice = deposit.SettlementId != null ? "SR " + billSettlement.SettlementReceiptNo.ToString() : null,
                                ReferenceInvoice = billingtxn.InvoiceCode + billingtxn.InvoiceNo,
                            }).OrderBy(a => a.Date).ToList();

            var CancelItems = (from cancelItems in _billingDbContext.BillingTransactionItems
                               join fiscalYear in _billingDbContext.BillingFiscalYears on cancelItems.ProvisionalFiscalYearId equals fiscalYear.FiscalYearId
                               where cancelItems.PatientId == patientId
                               && cancelItems.BillStatus == ENUM_BillingStatus.cancel //"cancel"
                               && (cancelItems.IsInsurance == false || cancelItems.IsInsurance == null)
                               select new
                               {
                                   TransactionItemId = cancelItems.BillingTransactionItemId,
                                   CreatedDate = cancelItems.CreatedOn,
                                   CancelledDate = cancelItems.CancelledOn,
                                   ItemName = cancelItems.ItemName,
                                   ServiceDepartmentName = cancelItems.ServiceDepartmentName,
                                   Amount = cancelItems.TotalAmount,
                                   Quantity = cancelItems.Quantity,
                                   Discount = cancelItems.DiscountAmount,
                                   Tax = cancelItems.Tax,
                                   SubTotal = cancelItems.SubTotal,
                                   BillStatus = cancelItems.BillStatus,
                               }).OrderByDescending(a => a.CancelledDate).ToList();


            return new
            {
                IsLoaded = true,
                PatientId = patientId,
                Invoices = invoices,
                ProvisionalItems = provisionalItems,
                Settlements = settlements,
                PaidItems = paidItems,
                UnpaidItems = unpaidItems,
                Deposits = deposits,
                ReturnedItems = returnedItems,
                InsuranceItems = insuranceItems,
                CancelledItems = CancelItems

            };
        }
        //private object GetOpdRequisitionItem(int requisitionId)
        //{

        //    //patientvisitid is requisitionid for opd-ticket
        //    Int64 patientVisitId = Convert.ToInt64(requisitionId);
        //    BillItemRequisition billItem = (from bill in _billingDbContext.BillItemRequisitions.Include("Patient")
        //                                    where bill.RequisitionId == patientVisitId
        //                                    select bill).FirstOrDefault();
        //    return billItem;
        //}

        private object GetServiceDepartmentItems(int serviceDepartmentId)
        {
            List<BillingItemVM> itemList = (List<BillingItemVM>)DanpheCache.Get("billItem-srvdept-" + serviceDepartmentId);
            if (itemList == null)
            {
                ServiceDepartmentModel serviceDept = (from service in _billingDbContext.ServiceDepartment.Include("BillItemPriceList")
                                                      where service.ServiceDepartmentId == serviceDepartmentId
                                                      select service).FirstOrDefault();
                if (serviceDept.BillItemPriceList.Count != 0)
                {

                    itemList = (from itm in serviceDept.BillItemPriceList
                                join priceCatServItem in _billingDbContext.BillItemsPriceCategoryMaps on itm.ServiceItemId equals priceCatServItem.ServiceItemId
                                where itm.IsActive == true && priceCatServItem.PriceCategoryId == 1 //Krishna, 13thMarch'23, 1 is for Normal and hardcoded for now
                                select new BillingItemVM
                                {
                                    ItemName = itm.ItemName,
                                    ItemPrice = (double)priceCatServItem.Price,
                                    IntegrationItemId = itm.IntegrationItemId,
                                    TaxApplicable = itm.IsTaxApplicable,
                                }).ToList();

                    //itemList = serviceDept.BillItemPriceList
                    //                            .Where(itm => itm.IsActive == true)//Include only Active Items-sud:7Aug'17.
                    //                            .Select(t => new BillingItemVM  //mapping to the billing and Test items Format 
                    //                            {
                    //                                //ProcedureCode = t.ProcedureCode,
                    //                                ItemName = t.ItemName,
                    //                                ItemPrice = t.Price,
                    //                                IntegrationItemId = t.IntegrationItemId,
                    //                                TaxApplicable = t.IsTaxApplicable,
                    //                            }).ToList();

                    DanpheCache.Add("billItem-srvdept-" + serviceDepartmentId, itemList, DateTime.Now.AddMinutes(cacheExpMinutes));

                }
            }

            return itemList;
        }

        private object GetBillCFGItemList(string srvDeptIdListStr, string itemIdListStr)
        {

            bool filterBySrvDeptId = false;
            bool filterByItemId = false;
            List<int> srvDeptIds = new List<int>();
            List<int> itemIds = new List<int>();

            //we're filtering by servicedepartmentids or ItemIds only when those is passed from client side.
            //else we'll return all the available items.
            if (!string.IsNullOrWhiteSpace(srvDeptIdListStr))
            {
                srvDeptIds = DanpheJSONConvert.DeserializeObject<List<int>>(srvDeptIdListStr);
                filterBySrvDeptId = srvDeptIds.Count() > 0 ? true : false;
            }
            if (!string.IsNullOrWhiteSpace(itemIdListStr))
            {
                itemIds = DanpheJSONConvert.DeserializeObject<List<int>>(itemIdListStr);
                filterByItemId = itemIds.Count() > 0 ? true : false;
            }


            var itemList = (from item in _billingDbContext.BillServiceItems
                            join srv in _billingDbContext.ServiceDepartment on item.ServiceDepartmentId equals srv.ServiceDepartmentId
                            join priceCatServItem in _billingDbContext.BillItemsPriceCategoryMaps on item.ServiceItemId equals priceCatServItem.ServiceItemId
                            where item.IsActive == true
                            //&& priceCatServItem.PriceCategoryId == 1 //Krishna, 13thMarch'23 1 is for Normal and hardcoded for now, commented for now, logic is written in client side for temporary purpose
                            //&& item.IsInsurancePackage == false && item.IsNormalPriceApplicable == true
                            && srv.IsActive == true
                            && (filterBySrvDeptId ? srvDeptIds.Contains(srv.ServiceDepartmentId) : true)
                            && (filterByItemId ? itemIds.Contains(item.IntegrationItemId) : true)
                            select new
                            {
                                BillItemPriceId = item.ServiceItemId,
                                ServiceDepartmentId = srv.ServiceDepartmentId,
                                ServiceDepartmentName = srv.ServiceDepartmentName,
                                ServiceDepartmentShortName = srv.ServiceDepartmentShortName,
                                SrvDeptIntegrationName = srv.IntegrationName,
                                Displayseq = item.DisplaySeq,
                                ItemId = item.IntegrationItemId,
                                ServiceItemId = item.ServiceItemId,
                                ItemCode = item.ItemCode,  //pratik :17 jan2020 
                                ItemName = item.ItemName,
                                PriceCategoryId = priceCatServItem.PriceCategoryId,
                                Price = priceCatServItem.Price,
                                DiscountApplicable = priceCatServItem.IsDiscountApplicable,
                                TaxApplicable = item.IsTaxApplicable,
                                Description = item.Description,
                                IsDoctorMandatory = item.IsDoctorMandatory,
                                NormalPrice = priceCatServItem.Price,
                                IsErLabApplicable = item.IsErLabApplicable,
                                Doctor = (from doc in _billingDbContext.Employee.DefaultIfEmpty()
                                          where doc.IsAppointmentApplicable == true && doc.EmployeeId == item.IntegrationItemId && srv.ServiceDepartmentName == "OPD"
                                          && srv.ServiceDepartmentId == item.ServiceDepartmentId
                                          select new
                                          {
                                              //Temporary logic, correct it later on... 
                                              DoctorId = doc != null ? doc.EmployeeId : 0,
                                              DoctorName = doc != null ? doc.FirstName + " " + (string.IsNullOrEmpty(doc.MiddleName) ? doc.MiddleName + " " : "") + doc.LastName : "",
                                          }).FirstOrDefault(),
                                AllowMultipleQty = item.AllowMultipleQty,
                                DefaultDoctorList = item.DefaultDoctorList,
                                IntegrationItemId = item.IntegrationItemId
                                //IsPriceChangeAllowed = item.IsPriceChangeAllowed

                            }).ToList().OrderBy(a => a.Displayseq);

            return itemList;
        }
        private object GetPendingDoctorOrderTotal()
        {
            var requestDetails = (from billItemReq in _billingDbContext.BillItemRequisitions
                                  join pat in _billingDbContext.Patient.Include("CountrySubDivision") on billItemReq.PatientId equals pat.PatientId
                                  join item in _billingDbContext.BillServiceItems on billItemReq.ItemId equals item.IntegrationItemId
                                  join doc in _billingDbContext.Employee on billItemReq.ProviderId equals doc.EmployeeId
                                  join srvDpt in _billingDbContext.ServiceDepartment on billItemReq.ServiceDepartmentId equals srvDpt.ServiceDepartmentId
                                  where (billItemReq.BillStatus == "pending" && billItemReq.ServiceDepartmentId == item.ServiceDepartmentId)
                                  group new { billItemReq, doc, srvDpt, pat } by new
                                  {
                                      billItemReq.PatientId,
                                      //billItemReq.Patient.PatientCode,
                                      //billItemReq.Patient.FirstName,
                                      //billItemReq.Patient.LastName,
                                      //billItemReq.Patient.MiddleName,
                                      //billItemReq.Patient.PhoneNumber,
                                      //billItemReq.Patient.Gender,
                                      srvDpt.ServiceDepartmentName,
                                      srvDpt.ServiceDepartmentId,
                                      pat,
                                      //billItemReq.CreatedBy,
                                      DSalutation = doc.Salutation,//sud:13Mar'19--need to get doctor's salutation as well..
                                      DFirstName = doc.FirstName,
                                      DMiddleName = doc.MiddleName,
                                      DLastName = doc.LastName
                                  } into r
                                  select new
                                  {
                                      //add more patient information if required.: sud-14May'18
                                      Patient = new
                                      {
                                          PatientId = r.Key.pat.PatientId,
                                          ShortName = r.Key.pat.FirstName + " " + (string.IsNullOrEmpty(r.Key.pat.MiddleName) ? "" : r.Key.pat.MiddleName + " ") + r.Key.pat.LastName,
                                          PatientCode = r.Key.pat.PatientCode,
                                          PhoneNumber = r.Key.pat.PhoneNumber,
                                          Gender = r.Key.pat.Gender,
                                          Address = r.Key.pat.Address,
                                          DateOfBirth = r.Key.pat.DateOfBirth.Value,
                                          CountrySubDivision = r.Key.pat.CountrySubDivision.CountrySubDivisionName,
                                          PANNumber = r.Key.pat.PANNumber
                                      },

                                      PatientId = r.Key.PatientId,
                                      RequestDate = r.Max(a => a.billItemReq.CreatedOn),
                                      ServiceDepartmentName = r.Key.ServiceDepartmentName, //departmentName should be provided to show item on Txn view 
                                      ServiceDepatmentId = r.Key.ServiceDepartmentId,
                                      TotalAmount = r.Sum(a => a.billItemReq.Price * a.billItemReq.Quantity),
                                      RequestedBy = (string.IsNullOrEmpty(r.Key.DSalutation) ? "" : r.Key.DSalutation + ". ") + r.Key.DFirstName + " " + (string.IsNullOrEmpty(r.Key.DMiddleName) ? "" : r.Key.DMiddleName + " ") + r.Key.DLastName,

                                  }).OrderByDescending(a => a.RequestDate).ToList();
            return requestDetails;
        }
        private object GetNursingOrderList(int patientId)
        {
            var nursingOrderList = (from bill in _billingDbContext.BillItemRequisitions
                                    join srvDpt in _billingDbContext.ServiceDepartment
                                    on bill.ServiceDepartmentId equals srvDpt.ServiceDepartmentId
                                    where bill.PatientId == patientId
                                    orderby bill.CreatedOn descending
                                    select new
                                    {
                                        DepartmentName = srvDpt.ServiceDepartmentName,
                                        ItemName = bill.ItemName,
                                        Quantity = bill.Quantity,
                                        BillStatus = bill.BillStatus,
                                        CreatedOn = bill.CreatedOn
                                    }).ToList();
            return nursingOrderList;
        }
        private object GetTransactionItemsForEditDoctor(string fromDate, string toDate, string search)
        {
            //PatientDbContext patientDbContext = new PatientDbContext(connString);
            //List<ServiceDepartmentModel> allServDepts = dbContext.ServiceDepartment.ToList();
            //OPD items cannot be edited -- Business Rule (MNK)--sud: 11Aug'17
/*            List<string> excludedServicedeptNames = new List<string>() { "OPD", "EMERGENCY" };*/
            List<string> excludedServicedeptIntegrationNames = new List<string>() { "OPD" };


            //get list of servicedepartments where Edit Doctor is applicable. 
            var srvDeptsWithEditApplicable = _billingDbContext.ServiceDepartment.Where(a => !excludedServicedeptIntegrationNames.Contains(a.IntegrationName));

            //this is because we have to get data till todate not in between todate to fromdate 
            //so i m adding  1 day to the todate 
            //var toDate = ToDate.AddDays(1);
            //List<PatientModel> allPatients = dbContext.Patient.AsEnumerable().ToList();
            //int searchListLength = 0;//this is default value.
            //List<ParameterModel> allParams = coreDbContext.Parameters.ToList();

            //ParameterModel listNumber = allParams.Where(a => a.ParameterGroupName == "Common" && a.ParameterName == "ServerSideSearchListLength").FirstOrDefault<ParameterModel>();
            //if (listNumber != null)
            //{
            //    searchListLength = Convert.ToInt32(listNumber.ParameterValue);
            //}

            search = (search == null || search == "null") ? string.Empty : search.ToLower();
            fromDate = (fromDate == null || fromDate == "null") ? string.Empty : fromDate;
            toDate = (toDate == null || toDate == "null") ? string.Empty : toDate;
            //sud:13Mar'20--take fromdate-todate also in search scope, default fromdate is 1000 days back (almost 3 years)
            DateTime startDate = string.IsNullOrEmpty(fromDate) ? DateTime.Now.AddDays(-1000).Date : DateTime.Parse(fromDate).Date;
            DateTime endDate = string.IsNullOrEmpty(toDate) ? DateTime.Now.Date : DateTime.Parse(toDate).Date;



            var TxnItemList = (from itm in _billingDbContext.BillingTransactionItems.Include("BillingTransaction")
                               join ser in srvDeptsWithEditApplicable on itm.ServiceDepartmentId equals ser.ServiceDepartmentId
                               join pat in _billingDbContext.Patient on itm.PatientId equals pat.PatientId
                               from bip in _billingDbContext.BillServiceItems.Where(b => b.ServiceDepartmentId == itm.ServiceDepartmentId && b.ServiceItemId == itm.ServiceItemId)
                               from emp in _billingDbContext.Employee.Where(emp => emp.EmployeeId == itm.PerformerId).DefaultIfEmpty() //using left join yub--30th Sept 2018.
                               where
                                  itm.BillStatus != ENUM_BillingStatus.cancel // "cancel" 
                                  && itm.BillStatus != ENUM_BillingStatus.adtCancel

                                  //sud:13Mar'20-- search between given dates as well.
                                  && (DbFunctions.TruncateTime(itm.CreatedOn) >= startDate && DbFunctions.TruncateTime(itm.CreatedOn) <= endDate)

                                   &&

                               (pat.ShortName
                               + pat.PatientCode
                               + pat.PhoneNumber
                               + ser.ServiceDepartmentName
                               + itm.ItemName
                               ).Contains(search)
                               select new
                               {
                                   Date = itm.CreatedOn,
                                   ServiceDepartmentId = itm.ServiceDepartmentId,
                                   ServiceDepartmentName = ser.ServiceDepartmentName,
                                   ItemId = itm.ItemId,
                                   ItemName = itm.ItemName,
                                   PerformerId = itm.PerformerId,
                                   PerformerName = emp.FullName,
                                   PatientId = itm.PatientId,
                                   BillingTransactionItemId = itm.BillingTransactionItemId,
                                   //receiptno here is: invoice code+ invoicenumber//added: sud-21May'18
                                   ReceiptNo = itm.BillingTransaction != null ? itm.BillingTransaction.InvoiceCode + itm.BillingTransaction.InvoiceNo : "",
                                   PatientName = pat.ShortName,
                                   DateOfBirth = pat.DateOfBirth,
                                   Gender = pat.Gender,
                                   PhoneNumber = pat.PhoneNumber,
                                   BillingTransactionId = itm.BillingTransactionId != null ? itm.BillingTransactionId : 0,  //Zero if NULL. 
                                   PatientCode = pat.PatientCode,
                                   BillStatus = itm.BillStatus,
                                   PrescriberId = itm.PrescriberId,
                                   DoctorMandatory = bip.IsDoctorMandatory
                               }).OrderByDescending(a => a.BillingTransactionItemId).AsQueryable();


            //take 200 rows (or search length) only when FromDate-ToDate is null, else it's Coming From DateFilter and items are already filtered by Date Range.
            if (string.IsNullOrEmpty(fromDate) && string.IsNullOrEmpty(toDate))
            {
                if (search == "" && CommonFunctions.GetCoreParameterBoolValue(_coreDbContext, "Common", "ServerSideSearchComponent", "BillingEditDoctor") == true)
                {
                    TxnItemList = TxnItemList.Take(CommonFunctions.GetCoreParameterIntValue(_coreDbContext, "Common", "ServerSideSearchListLength"));
                }
            }

            var finalResults = TxnItemList.ToList();
            return finalResults;
        }

        private object GetProviderListFromDanpheCache()
        {
            List<EmployeeModel> empListFromCache = (List<EmployeeModel>)DanpheCache.GetMasterData(MasterDataEnum.Employee);
            var docotorList = empListFromCache.Where(emp => emp.IsAppointmentApplicable.HasValue
                                                  && emp.IsAppointmentApplicable == true).ToList()
                                                  .Select(emp => new { EmployeeId = emp.EmployeeId, EmployeeName = emp.FullName });
            return docotorList;
        }
        private object GetListOfBillingPackages()
        {
            List<BillingPackageModel> packageList = _billingDbContext.BillingPackages.Where(a => a.IsActive == true && a.InsuranceApplicable == false).ToList();
            if (packageList.Count > 0)
            {
                foreach (var package in packageList)
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(package.BillingItemsXML);
                    package.BillingItemsXML = JsonConvert.SerializeXmlNode(doc, Newtonsoft.Json.Formatting.None, true);
                }
            }
            return packageList;
        }

        //private object GetPreviousHandoverAmountsDetails(RbacUser currentUser)
        //{
        //    var todayDate = DateTime.Now.Date;

        //    BillingHandoverModel handoverGiven = new BillingHandoverModel();
        //    List<BillingHandoverModel> hoGivenList = (from amt in _billingDbContext.Handover
        //                                              where amt.UserId == currentUser.EmployeeId //getting indiviual user handover information
        //                                              select amt).ToList();

        //    hoGivenList.ForEach(val =>
        //    {
        //        if (val.CreatedOn.HasValue ? val.CreatedOn.Value.Date == todayDate : false)
        //        {
        //            handoverGiven = val;
        //        }
        //    });


        //    BillingHandoverModel handoverReceived = new BillingHandoverModel();
        //    List<BillingHandoverModel> hoReceivedList = (from amt in _billingDbContext.Handover
        //                                                 where amt.HandOverUserId == currentUser.EmployeeId //getting indiviual user handover information
        //                                                 select amt).ToList();

        //    hoReceivedList.ForEach(val =>
        //    {
        //        if (val.CreatedOn.HasValue ? val.CreatedOn.Value.Date == todayDate : false)
        //        {
        //            handoverGiven = val;
        //        }
        //    });

        //    return new { Given = handoverGiven, Received = handoverReceived };
        //}
        private object GetPatientBillingContext(int patientId)
        {
            var currPat = _billingDbContext.Patient.Where(p => p.PatientId == patientId).FirstOrDefault();
            PatientBillingContextVM currBillContext = new PatientBillingContextVM();

            if (currPat != null)
            {
                //get latest bed assigned to this patient if not discharged.
                var adtDetail = (from adm in _billingDbContext.Admissions
                                 where adm.PatientId == currPat.PatientId && adm.AdmissionStatus == "admitted"
                                 join beds in _billingDbContext.PatientBedInfos
                                 on adm.PatientVisitId equals beds.PatientVisitId
                                 select new
                                 {
                                     BedInfo = beds,
                                     adm.PatientVisitId, //added: ashim : 08Aug2018 : to update PatientVisitId in Depoist.
                                     AdmissionDate = adm.AdmissionDate,
                                     AdmissionCase = adm.AdmissionCase

                                 }).OrderByDescending(adt => adt.BedInfo.PatientBedInfoId).FirstOrDefault();

                int? requestingDeptId = null;
                string billingType = "outpatient";//by default its outpatient
                int? patientVisitId = null;
                DateTime? AdmissionDate = null;
                string AdmissionCase = null;
                if (adtDetail != null)
                {
                    requestingDeptId = adtDetail.BedInfo.RequestingDeptId;
                    patientVisitId = adtDetail.PatientVisitId;
                    billingType = "inpatient";
                    AdmissionDate = adtDetail.AdmissionDate;
                    AdmissionCase = adtDetail.AdmissionCase;

                }
                //added: ashim : 08Aug2018 : to update PatientVisitId in Depoist.
                else
                {
                    VisitModel patientVisit = _billingDbContext.Visit.Where(visit => visit.PatientId == currPat.PatientId)
                            .OrderByDescending(a => a.PatientVisitId)
                            .FirstOrDefault();
                    //if the latest visit is inpatient even the patient was discharged use null for visitId
                    if (patientVisit != null && patientVisit.VisitType.ToLower() != ENUM_VisitType.inpatient)
                    {
                        patientVisitId = (int?)patientVisit.PatientVisitId;
                    }
                }

                //for insurance details
                currBillContext.Insurance = (from ins in _billingDbContext.Insurances
                                             join insProvider in _billingDbContext.InsuranceProviders on ins.InsuranceProviderId equals insProvider.InsuranceProviderId
                                             join pat in _billingDbContext.Patient on ins.PatientId equals pat.PatientId
                                             where insProvider.InsuranceProviderName == "Government Insurance" && ins.PatientId == currPat.PatientId
                                             select new InsuranceVM
                                             {
                                                 PatientId = ins.PatientId,
                                                 InsuranceProviderId = ins.InsuranceProviderId,
                                                 CurrentBalance = ins.CurrentBalance,
                                                 InsuranceNumber = ins.InsuranceNumber,
                                                 IMISCode = ins.IMISCode,
                                                 InsuranceProviderName = insProvider.InsuranceProviderName,
                                                 PatientInsurancePkgTxn = (from pkgTxn in _billingDbContext.PatientInsurancePackageTransactions
                                                                           join pkg in _billingDbContext.BillingPackages on pkgTxn.PackageId equals pkg.BillingPackageId
                                                                           where pkgTxn.PatientId == currPat.PatientId && pkgTxn.IsCompleted == false
                                                                           select new PatientInsurancePkgTxnVM
                                                                           {
                                                                               PatientInsurancePackageId = pkgTxn.PatientInsurancePackageId,
                                                                               PackageId = pkgTxn.PackageId,
                                                                               PackageName = pkg.BillingPackageName,
                                                                               StartDate = pkgTxn.StartDate
                                                                           }).FirstOrDefault()
                                             }).FirstOrDefault();

                var patProvisional = (from bill in _billingDbContext.BillingTransactionItems
                                      where bill.PatientId == currPat.PatientId && bill.BillStatus == ENUM_BillingStatus.provisional // "provisional" //here PatientId comes as InputId from client.
                                      && bill.IsInsurance == true
                                      group bill by new { bill.PatientId } into p
                                      select new
                                      {
                                          TotalProvisionalAmt = p.Sum(a => a.TotalAmount)
                                      }).FirstOrDefault();

                var patProvisionalAmt = patProvisional != null ? patProvisional.TotalProvisionalAmt : 0;
                if (currBillContext.Insurance != null)
                {
                    currBillContext.Insurance.InsuranceProvisionalAmount = patProvisionalAmt;
                }
                currBillContext.PatientId = currPat.PatientId;
                currBillContext.BillingType = billingType;
                currBillContext.RequestingDeptId = requestingDeptId;
                currBillContext.PatientVisitId = patientVisitId == 0 ? null : patientVisitId;
                currBillContext.AdmissionDate = AdmissionDate;
                currBillContext.AdmissionCase = AdmissionCase;

                var currentPatientSchemeMap = _billingDbContext.PatientSchemeMaps.Where(a => a.LatestPatientVisitId == currBillContext.PatientVisitId && a.PatientId == currBillContext.PatientId && a.IsActive == true).FirstOrDefault();

                currBillContext.PatientSchemeMap = currentPatientSchemeMap;
            }
            return currBillContext; ;
        }

        private object GetBillingTransactionByRequisitionId(int requisitionId, int patientId, string departmentName)
        {
            var bilTxnId = (from biltxnItem in _billingDbContext.BillingTransactionItems
                            join srv in _billingDbContext.ServiceDepartment on biltxnItem.ServiceDepartmentId equals srv.ServiceDepartmentId
                            where biltxnItem.RequisitionId == requisitionId
                            && srv.IntegrationName.ToLower() == departmentName.ToLower()
                            && biltxnItem.PatientId == patientId
                            select biltxnItem.BillingTransactionId).FirstOrDefault();

            if (bilTxnId != null && bilTxnId.HasValue)
            {

                var retVal = new
                {
                    bill = _billingDbContext.BillingTransactions.Where(b => b.BillingTransactionId == bilTxnId).FirstOrDefault(),
                    billTxnItems = (from txnItem in _billingDbContext.BillingTransactionItems
                                    where txnItem.BillingTransactionId == bilTxnId
                                    select new
                                    {
                                        txnItem.BillingTransactionItemId,
                                        txnItem.ItemName,
                                        txnItem.ItemId,
                                        txnItem.ServiceDepartmentName,
                                        txnItem.Price,
                                        txnItem.Quantity,
                                        txnItem.SubTotal,
                                        txnItem.DiscountAmount,
                                        txnItem.TaxableAmount,
                                        txnItem.Tax,
                                        txnItem.TotalAmount,
                                        txnItem.DiscountPercent,
                                        txnItem.DiscountPercentAgg,
                                        txnItem.PerformerId,
                                        txnItem.PerformerName,
                                        txnItem.BillStatus,
                                        txnItem.RequisitionId,
                                        txnItem.BillingPackageId,
                                        txnItem.TaxPercent,
                                        txnItem.NonTaxableAmount,
                                        txnItem.BillingType,
                                        txnItem.VisitType
                                    }).ToList()
                };

                return retVal;

            }
            else
            {
                return null;

            }
        }
        private object GetPatientPastBillITxntems(int patientId)
        {
            var numberOfPastDays = (from param in _billingDbContext.AdminParameters
                                    where param.ParameterGroupName.ToLower() == "bill" && param.ParameterName == "PastBillMaximumDays"
                                    select param.ParameterValue).FirstOrDefault();

            if (numberOfPastDays == null)
            {
                numberOfPastDays = "7";
            }
            List<SqlParameter> paramList = new List<SqlParameter>() { new SqlParameter("@PatientId", patientId), new SqlParameter("@maxPastDays", numberOfPastDays) };
            DataTable patpastBills = DALFunctions.GetDataTableFromStoredProc("SP_BIL_GetPatientPastBills", paramList, _billingDbContext);
            return patpastBills;
        }

        private object GetBillingInvoiceInfoForPrint(int invoiceNo, int fiscalYearId, int billingTransactionId)
        {
            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@InvoiceNumber", invoiceNo),
                        new SqlParameter("@FiscalYearId", fiscalYearId),
                         new SqlParameter("@BillingTxnIdInput", billingTransactionId)
                    };


            //there are five return table coming from this stored procedure.
            DataSet dsPrintData = DALFunctions.GetDatasetFromStoredProc("SP_BIL_GetInvoiceDetailsForPrint", paramList, _billingDbContext);

            DataTable dtPatientInfo = dsPrintData.Tables[0];
            DataTable dtInvoiceInfo = dsPrintData.Tables[1];
            DataTable dtInvItems = dsPrintData.Tables[2];
            DataTable dtVisitInfo = dsPrintData.Tables[3];
            DataTable dtDepositsInfo = dsPrintData.Tables[4];
            DataTable dtInvoiceSummary = dsPrintData.Tables[5];

            //group them in a new anonymous object and send to client.
            var printInfoToReturn = new
            {
                PatientInfo = BilPrint_PatientInfoVM.MapDataTableToSingleObject(dtPatientInfo),
                InvoiceInfo = BilPrint_InvoiceInfoVM.MapDataTableToSingleObject(dtInvoiceInfo),
                InvoiceItems = BilPrint_InvoiceItemVM.MapDataTableToObjectList(dtInvItems),
                VisitInfo = BilPrint_VisitInfoVM.MapDataTableToSingleObject(dtVisitInfo),
                DepositList = BilPrint_DepositListVM.MapDataTableToObjectList(dtDepositsInfo),
                BillingInvoiceSummary = BilPrint_BillingInvoiceSummary.MapDataTableToObjectList(dtInvoiceSummary),
                IsInvoiceFound = dtInvoiceInfo.Rows.Count > 0 ? true : false//this flag decides whether or not to display in client side.
            };

            printInfoToReturn.VisitInfo.ItemsRequestingDoctorsId = printInfoToReturn.InvoiceItems.Select(s => s.RequestedBy).Distinct().ToList();
            printInfoToReturn.VisitInfo.ItemsRequestingDoctors = String.Join(",", printInfoToReturn.InvoiceItems.Where(d => (d.RequestedBy > 0)).Select(s => s.RequestedByName).Distinct());

            return printInfoToReturn;
        }

        ////Sud:20Mar'23--Not used by any Frontend Component--Hence Commented..
        //private object GetPatientMembershipInfo(int patientId)
        //{
        //    var currPatMembershipId = _billingDbContext.Patient.FirstOrDefault(p => p.PatientId == patientId).MembershipTypeId;
        //    var patMembershipInfo = (from memb in _billingDbContext.BillingSchemes
        //                             where memb.SchemeId == currPatMembershipId
        //                             select new
        //                             {
        //                                 PatientId = patientId,
        //                                 MembershipTypeId = memb.SchemeId,
        //                                 DiscountPercent = memb.DiscountPercent,
        //                                 MembershipTypeName = memb.SchemeName
        //                             }).FirstOrDefault();

        //    return patMembershipInfo;
        //}

        private object GetInsurancePatientProvisionalItems(int patientId)
        {
            PatientModel currPatient = _billingDbContext.Patient.Where(pat => pat.PatientId == patientId).FirstOrDefault();
            if (currPatient != null)
            {
                string subDivName = (from pat in _billingDbContext.Patient
                                     join countrySubdiv in _billingDbContext.CountrySubdivisions
                                     on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
                                     where pat.PatientId == currPatient.PatientId
                                     select countrySubdiv.CountrySubDivisionName
                                  ).FirstOrDefault();

                currPatient.CountrySubDivisionName = subDivName;

            }

            //for this request type, patientid comes as inputid.
            var patCreditItems = (from bill in _billingDbContext.BillingTransactionItems.Include("ServiceDepartment")
                                  where bill.BillStatus == ENUM_BillingStatus.provisional //"provisional" 
                                  && bill.PatientId == patientId
                                  && bill.IsInsurance == true
                                  select bill).ToList<BillingTransactionItemModel>().OrderBy(b => b.ServiceDepartmentId);

            //clear patient object from Items, not needed since we're returning patient object separately
            if (patCreditItems != null)
            {

                var allEmployees = (from emp in _billingDbContext.Employee
                                    join dep in _billingDbContext.Departments
                                    on emp.DepartmentId equals dep.DepartmentId into empDpt
                                    from emp2 in empDpt.DefaultIfEmpty()
                                    select new
                                    {
                                        EmployeeId = emp.EmployeeId,
                                        EmployeeName = emp.FirstName,
                                        DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
                                        DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
                                    }).ToList();

                BillingFiscalYear fiscYear = BillingBL.GetFiscalYear(connString);

                //remove relational property of BillingTransactionItem//sud: 12May'18
                //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
                foreach (BillingTransactionItemModel item in patCreditItems)
                {
                    //item.ProvFiscalYear = "2075 / 76";
                    item.ProvFiscalYear = fiscYear.FiscalYearFormatted;
                    item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
                            .Select(a => a.EmployeeName).FirstOrDefault();

                    item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
                         .Select(a => a.DepartmentName).FirstOrDefault();

                    item.Patient = null;
                }
            }

            //create new anonymous type with patient information + Credit Items information : Anish:4May'18
            var patCreditDetails = new
            {
                Patient = currPatient,
                InsCreditItems = patCreditItems.OrderBy(itm => itm.CreatedOn).ToList()
            };
            return patCreditDetails;
        }

        private object GetPatientProvisionalItemsByPatientIdAndVisitId(int patientId, int patientVisitId)
        {
            PatientModel currPatient = _billingDbContext.Patient.Where(pat => pat.PatientId == patientId).FirstOrDefault();
            if (currPatient != null)
            {
                string subDivName = (from countrySubdiv in _billingDbContext.CountrySubdivisions
                                     where countrySubdiv.CountrySubDivisionId == currPatient.CountrySubDivisionId
                                     select countrySubdiv.CountrySubDivisionName
                                  ).FirstOrDefault();

                currPatient.CountrySubDivisionName = subDivName;

            }

            //for this request type, patientid comes as inputid.
            var patCreditItems = (from bill in _billingDbContext.BillingTransactionItems.Include("ServiceDepartment")
                                  where bill.BillStatus == ENUM_BillingStatus.provisional //"provisional" 
                                  && bill.PatientId == patientId && bill.PatientVisitId == patientVisitId
                                  select bill).ToList<BillingTransactionItemModel>().OrderBy(b => b.ServiceDepartmentId);

            //clear patient object from Items, not needed since we're returning patient object separately
            if (patCreditItems != null)
            {

                var allEmployees = (from emp in _billingDbContext.Employee
                                    join dep in _billingDbContext.Departments
                                    on emp.DepartmentId equals dep.DepartmentId into empDpt
                                    from emp2 in empDpt.DefaultIfEmpty()
                                    select new
                                    {
                                        EmployeeId = emp.EmployeeId,
                                        EmployeeName = emp.FirstName,
                                        DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
                                        DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
                                    }).ToList();

                //remove relational property of BillingTransactionItem//sud: 12May'18
                //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
                foreach (BillingTransactionItemModel item in patCreditItems)
                {
                    item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
                            .Select(a => a.EmployeeName).FirstOrDefault();

                    item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
                         .Select(a => a.DepartmentName).FirstOrDefault();

                    item.Patient = null;
                }
            }

            //create new anonymous type with patient information + Credit Items information : Anish:4May'18
            var patCreditDetails = new
            {
                Patient = currPatient,
                CreditItems = patCreditItems.OrderBy(itm => itm.CreatedOn).ToList()
            };
            return patCreditDetails;
        }

        //private object GetInPatientProvisionalItemsByPatientIdAndVisitId(int patientId, int patientVisitId)
        //{
        //    List<BillingTransactionItemVM> billItemVM = new List<BillingTransactionItemVM>();

        //    PatientModel currPatient = _billingDbContext.Patient.Where(pat => pat.PatientId == patientId).FirstOrDefault();
        //    if (currPatient != null)
        //    {
        //        string subDivName = (from pat in _billingDbContext.Patient
        //                             join countrySubdiv in _billingDbContext.CountrySubdivisions
        //                             on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
        //                             where pat.PatientId == currPatient.PatientId
        //                             select countrySubdiv.CountrySubDivisionName
        //                          ).FirstOrDefault();

        //        currPatient.CountrySubDivisionName = subDivName;

        //    }

        //    //for this request type, patientid comes as inputid.
        //    var patCreditItems = (from bill in _billingDbContext.BillingTransactionItems.Include("ServiceDepartment")
        //                          where bill.BillStatus == ENUM_BillingStatus.provisional // "provisional"
        //                          && bill.PatientId == patientId && bill.PatientVisitId == patientVisitId
        //                          select bill).ToList<BillingTransactionItemModel>().OrderBy(b => b.ServiceDepartmentId);


        //    //clear patient object from Items, not needed since we're returning patient object separately
        //    if (patCreditItems != null)
        //    {

        //        var allEmployees = (from emp in _billingDbContext.Employee
        //                            join dep in _billingDbContext.Departments
        //                            on emp.DepartmentId equals dep.DepartmentId into empDpt
        //                            from emp2 in empDpt.DefaultIfEmpty()
        //                            select new
        //                            {
        //                                EmployeeId = emp.EmployeeId,
        //                                EmployeeName = emp.FirstName,
        //                                DepartmentCode = emp2 != null ? emp2.DepartmentCode : "N/A",
        //                                DepartmentName = emp2 != null ? emp2.DepartmentName : "N/A"
        //                            }).ToList();

        //        //remove relational property of BillingTransactionItem//sud: 12May'18
        //        //assign requesting department and user for each provisional items.. -- sud: 25Sept'18
        //        foreach (BillingTransactionItemModel item in patCreditItems)
        //        {
        //            var blItm = new BillingTransactionItemVM();

        //            item.RequestingUserName = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //                    .Select(a => a.EmployeeName).FirstOrDefault();

        //            item.RequestingUserDept = allEmployees.Where(a => a.EmployeeId == item.CreatedBy)
        //                 .Select(a => a.DepartmentName).FirstOrDefault();

        //            item.Patient = null;



        //            blItm.BillingTransactionItemId = item.BillingTransactionItemId;
        //            blItm.BillingTransactionId = item.BillingTransactionId;
        //            blItm.PatientId = item.PatientId;
        //            blItm.ProviderId = item.PerformerId;
        //            blItm.ProviderName = item.PerformerName;
        //            blItm.ServiceDepartmentId = item.ServiceDepartmentId;
        //            blItm.ServiceDepartmentName = item.ServiceDepartmentName;
        //            blItm.ProcedureCode = item.ProcedureCode;
        //            blItm.ItemId = item.ItemId;
        //            blItm.ItemName = item.ItemName;
        //            blItm.Price = item.Price;
        //            blItm.Quantity = item.Quantity;
        //            blItm.SubTotal = item.SubTotal;
        //            blItm.DiscountPercent = item.DiscountPercent;
        //            blItm.DiscountPercentAgg = item.DiscountPercentAgg;
        //            blItm.DiscountAmount = item.DiscountAmount;
        //            blItm.Tax = item.Tax;
        //            blItm.TotalAmount = item.TotalAmount;
        //            blItm.BillStatus = item.BillStatus;
        //            blItm.RequisitionId = item.RequisitionId;
        //            blItm.RequisitionDate = item.RequisitionDate;
        //            blItm.CounterDay = item.CounterDay;
        //            blItm.CounterId = item.CounterId;
        //            blItm.PaidDate = item.PaidDate;
        //            blItm.ReturnStatus = item.ReturnStatus;
        //            blItm.ReturnQuantity = item.ReturnQuantity;
        //            blItm.CreatedBy = item.CreatedBy;
        //            blItm.CreatedOn = item.CreatedOn;
        //            blItm.Remarks = item.Remarks;
        //            blItm.CancelRemarks = item.CancelRemarks;
        //            blItm.TaxPercent = item.TaxPercent;
        //            blItm.CancelledOn = item.CancelledOn;
        //            blItm.CancelledBy = item.CancelledBy;
        //            blItm.RequestedBy = item.PrescriberId;
        //            blItm.PatientVisitId = item.PatientVisitId;
        //            blItm.BillingPackageId = item.BillingPackageId;
        //            blItm.TaxableAmount = item.TaxableAmount;
        //            blItm.NonTaxableAmount = item.NonTaxableAmount;
        //            blItm.PaymentReceivedBy = item.PaymentReceivedBy;
        //            blItm.PaidCounterId = item.PaidCounterId;
        //            blItm.BillingType = item.BillingType;
        //            blItm.RequestingDeptId = item.RequestingDeptId;
        //            blItm.IsTaxApplicable = item.IsTaxApplicable;
        //            blItm.Patient = item.Patient;
        //            blItm.BillingTransaction = item.BillingTransaction;
        //            blItm.ServiceDepartment = item.ServiceDepartment;
        //            blItm.VisitType = item.VisitType;
        //            blItm.RequestingUserName = item.RequestingUserName;
        //            blItm.RequestingUserDept = item.RequestingUserDept;

        //            //Added by Anish: Oct 10- In case Of lab User should not be able to cancel the LabItem whose Report is already Generated
        //            if (item.ServiceDepartment.IntegrationName != null && item.ServiceDepartment.IntegrationName.ToLower() == "lab")
        //            {
        //                blItm.AllowCancellation = !(
        //                                      (from cmp in _labDbContext.LabTestComponentResults
        //                                       where cmp.RequisitionId == item.RequisitionId
        //                                      && cmp.LabReportId.HasValue
        //                                       select cmp).ToList().Count > 0
        //                                   );
        //            }
        //            else if (item.ServiceDepartment.IntegrationName != null && item.ServiceDepartment.IntegrationName.ToLower() == "radiology")
        //            {
        //                blItm.AllowCancellation = !(
        //                                      (from req in _radiologyDbContext.ImagingRequisitions
        //                                       where req.ImagingRequisitionId == item.RequisitionId
        //                                      && req.OrderStatus == "final"
        //                                       select req).ToList().Count > 0
        //                                   );
        //            }
        //            else
        //            {
        //                blItm.AllowCancellation = true;
        //            }

        //            billItemVM.Add(blItm);

        //        }
        //    }

        //    //create new anonymous type with patient information + Credit Items information : Anish:4May'18
        //    var patCreditDetails = new
        //    {
        //        Patient = currPatient,
        //        CreditItems = billItemVM.OrderByDescending(itm => itm.CreatedOn).ToList()
        //    };
        //    return patCreditDetails;
        //}

        private object GetInPatientProvisionalItems(int patientId, int patientVisitId, string module)
        {
            PatientModel currPatient = _billingDbContext.Patient.Where(pat => pat.PatientId == patientId).FirstOrDefault();
            if (currPatient != null)
            {
                string subDivName = (from pat in _billingDbContext.Patient
                                     join countrySubdiv in _billingDbContext.CountrySubdivisions
                                     on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
                                     where pat.PatientId == currPatient.PatientId
                                     select countrySubdiv.CountrySubDivisionName
                                  ).FirstOrDefault();

                currPatient.CountrySubDivisionName = subDivName;
                //remove relational property of patient//sud: 12May'18
                //currPatient.BillingTransactionItems = null;
            }

            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@patientId", patientId),
                        new SqlParameter("@patientVisitId", patientVisitId),
                        new SqlParameter("@moduleName", module)
                    };

            DataTable patCreditItems = DALFunctions.GetDataTableFromStoredProc("SP_InPatient_Item_Details", paramList, _billingDbContext);


            //create new anonymous type with patient information + Credit Items information : Anish:4May'18
            var patCreditDetails = new
            {
                Patient = currPatient,
                BillItems = patCreditItems
            };
            return patCreditDetails;
        }

        private object ProcessProvisionalBills(string ipDataString, RbacUser currentUser)
        {
            bool transactionSuccess = false;
            BillingTransactionModel billTransaction = DanpheJSONConvert.DeserializeObject<BillingTransactionModel>(ipDataString);
            if (BillingTransactionBL.IsDepositAvailable(_billingDbContext, billTransaction.PatientId, billTransaction.DepositUsed))
            {
                //Transaction Begins  
                using (var dbContextTransaction = _billingDbContext.Database.BeginTransaction())
                {
                    try
                    {
                        var admissionDetail = _billingDbContext.Admissions.FirstOrDefault(adm => adm.PatientVisitId == billTransaction.PatientVisitId);
                        if(admissionDetail != null && admissionDetail.IsProvisionalDischarge == true && admissionDetail.IsProvisionalDischargeCleared == false)
                        {
                            admissionDetail.ModifiedBy = currentUser.EmployeeId;
                            admissionDetail.ModifiedOn = DateTime.Now;
                            admissionDetail.IsProvisionalDischargeCleared = true;

                            _billingDbContext.Entry(admissionDetail).State = EntityState.Modified;
                            _billingDbContext.SaveChanges();
                        }
                        billTransaction = BillingTransactionBL.PostBillingTransaction(_billingDbContext, connString, null, billTransaction, currentUser, DateTime.Now);

                        //Billing User should be assigned from the server side avoiding assigning from client side 
                        //which is causing issue in billing receipt while 2 user loggedIn in the same browser
                        billTransaction.BillingUserName = currentUser.UserName;//Yubaraj: 28June'19                            

                        dbContextTransaction.Commit(); //end of transaction
                        transactionSuccess = true;

                        //send to IRD only after transaction is committed successfully: sud-23Dec'18
                        //Below is asynchronous call, so even if IRD api is down, it won't hold the execution of other code..
                        if (realTimeRemoteSyncEnabled)
                        {
                            if (billTransaction.Patient == null)
                            {
                                PatientModel pat = _billingDbContext.Patient.Where(p => p.PatientId == billTransaction.PatientId).FirstOrDefault();
                                billTransaction.Patient = pat;
                            }
                            //Sud:23Dec'18--making parallel thread call (asynchronous) to post to IRD. so that it won't stop the normal execution of logic.
                            // BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext);
                            Task.Run(() => BillingBL.SyncBillToRemoteServer(billTransaction, "sales", _billingDbContext));
                        }
                        return billTransaction;
                    }
                    catch (Exception ex)
                    {
                        transactionSuccess = false;
                        HandleException(ex);
                        //rollback all changes if any error occurs
                        dbContextTransaction.Rollback();
                        throw ex;
                    }
                }
            }
            else
            {
                throw new Exception("Deposit Amount is Invalid, Please try again.");
            }
        }

        private object AddBillingTransactionItems(string ipDataString, RbacUser currentUser)
        {
            List<BillingTransactionItemModel> billTranItems = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(ipDataString);
            billTranItems.ForEach(a =>
            {
                a.CoPaymentCashAmount = a.CoPaymentCashAmount == null ? 0 : a.CoPaymentCashAmount;
                a.CoPaymentCreditAmount = a.CoPaymentCreditAmount == null ? 0 : a.CoPaymentCreditAmount;
            });
            using (var dbContextTransaction = _billingDbContext.Database.BeginTransaction())
            {
                try
                {
                    if (billTranItems != null && billTranItems.Count > 0)
                    {
                        billTranItems = BillingTransactionBL.PostUpdateBillingTransactionItems(_billingDbContext,
                            connString,
                            billTranItems,
                            currentUser,
                            DateTime.Now,
                            billTranItems[0].BillStatus,
                            billTranItems[0].CounterId);

                        var userName = (from emp in _billingDbContext.Employee where emp.EmployeeId == currentUser.EmployeeId select emp.FirstName).FirstOrDefault();
                        billTranItems.ForEach(usr => usr.RequestingUserName = userName);

                        dbContextTransaction.Commit(); //end of transaction
                        return billTranItems;
                    }
                    else
                    {
                        throw new Exception("No Billing Transaction Items To Save");
                    }
                }
                catch (Exception ex)
                {
                    //rollback all changes if any error occurs
                    dbContextTransaction.Rollback();
                    throw ex;
                }
            }
        }

        private object AddBillItemsRequisition(string ipDataString, RbacUser currentUser)
        {

            List<BillItemRequisition> ItemRequests = DanpheJSONConvert.DeserializeObject<List<BillItemRequisition>>(ipDataString);

            if (ItemRequests != null && ItemRequests.Count > 0)
            {
                List<BillServiceItemModel> allBillingItems = _billingDbContext.BillServiceItems.ToList();
                List<ServiceDepartmentModel> allSrvDepts = _billingDbContext.ServiceDepartment.ToList();

                foreach (var req in ItemRequests)
                {
                    ///Required for Doctor-Order, check if that can be passed from there (client side).

                    ServiceDepartmentModel srvDepartment = null;
                    if (req.ServiceDepartmentId != 0)
                    {
                        srvDepartment = (from srv in allSrvDepts
                                         where (srv.ServiceDepartmentId == req.ServiceDepartmentId)
                                         select srv).FirstOrDefault();
                    }
                    else if (req.DepartmentName != null || req.DepartmentName != "")
                    {
                        srvDepartment = (from billItem in _billingDbContext.BillServiceItems
                                         join srvDept in _billingDbContext.ServiceDepartment on billItem.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                                         join dept in _billingDbContext.Departments on srvDept.DepartmentId equals dept.DepartmentId
                                         where (dept.DepartmentName.ToLower() == req.DepartmentName.ToLower()
                                         && billItem.ItemName.ToLower() == req.ItemName.ToLower())
                                         select srvDept).FirstOrDefault();
                    }

                    if (srvDepartment != null)
                    {
                        double? itemPrice = (from itmpr in allBillingItems
                                             join priceCatServItem in _billingDbContext.BillItemsPriceCategoryMaps on itmpr.ServiceItemId equals priceCatServItem.ServiceItemId
                                             where (itmpr.ItemName == req.ItemName && itmpr.ServiceDepartmentId == srvDepartment.ServiceDepartmentId && priceCatServItem.PriceCategoryId == 1) //Krishna 13thMarch'23 1 is for Normal and Hardcoded for now
                                             select (double?)priceCatServItem.Price).FirstOrDefault();
                        //Check -If ItemPrice is zero in db then take ItemPrice from client req
                        double? itmPrice = itemPrice > 0 ? itemPrice : req.Price;
                        //used in case of visit.procedurecode is providerId
                        //getting item name from provider id
                        if (string.IsNullOrEmpty(req.ItemName))
                        {
                            req.ItemName = (from itmpr in allBillingItems
                                                //where (itmpr.ProcedureCode == req.ProcedureCode)
                                            select itmpr.ItemName).FirstOrDefault();
                        }
                        req.Price = Convert.ToDouble(itmPrice);
                        req.ServiceDepartmentId = srvDepartment.ServiceDepartmentId;
                        req.ServiceDepartment = srvDepartment.ServiceDepartmentName;
                        req.BillStatus = "pending";
                        req.CreatedOn = System.DateTime.Now;
                        _billingDbContext.BillItemRequisitions.Add(req);
                    }
                    else
                    {
                        throw new Exception("Service Department is not proper.");
                    }
                }

                _billingDbContext.SaveChanges();
                return ItemRequests;//check if we need to send back all the input array back to client.--sudarshan
            }
            else
            {
                throw new Exception("No Items Requisition to Save");
            }
        }

        private object AddHTMLFile(string ipDataString, RbacUser currentUser, string printerName, string filePath)
        {
            //ipDataString is input (HTML string)
            if (ipDataString.Length > 0)
            {
                //right now we are naming file as printerName + employeeId.html so that there is no mis match in htmlfile from different users.

                var fileName = printerName + currentUser.EmployeeId + ".html";
                //byte[] htmlbytearray = System.Text.Encoding.ASCII.GetBytes(ipDataString);
                //saving file to default folder, html file need to be delete after print is called.
                //System.IO.File.WriteAllBytes(@FilePath + fileName, htmlbytearray);
                System.IO.File.WriteAllText(@filePath + fileName, ipDataString);

                return 1;
            }
            else
            {
                throw new Exception("No data to save");
            }
        }

        //private object SaveHandoverDenominationDetail(string ipDataString, RbacUser currentUser)
        //{

        //    BillingHandoverModel handoverInfo = DanpheJSONConvert.DeserializeObject<BillingHandoverModel>(ipDataString);
        //    var denominationDetails = handoverInfo.denomination;
        //    //var hello = handoverInfo;
        //    //Transaction Begins  
        //    using (var dbContextTransaction = _billingDbContext.Database.BeginTransaction())
        //    {
        //        try
        //        {
        //            var TotalAmount = handoverInfo.denomination.Sum(a => a.CurrencyType * a.Quantity);

        //            if (handoverInfo != null)
        //            {
        //                BillingHandoverModel handover = new BillingHandoverModel();
        //                {
        //                    //handover.HandoverId =;
        //                    handover.UserId = currentUser.EmployeeId;
        //                    handover.HandoverType = handoverInfo.HandoverType;
        //                    handover.PreviousAmount = handoverInfo.PreviousAmount;
        //                    handover.HandoverAmount = TotalAmount;
        //                    handover.HandOverUserId = handoverInfo.UserId;
        //                    handover.TotalAmount = handoverInfo.PreviousAmount + TotalAmount;
        //                    handover.CounterId = handoverInfo.CounterId;
        //                    handover.CreatedBy = currentUser.EmployeeId;
        //                    handover.CreatedOn = System.DateTime.Now;
        //                }
        //                _billingDbContext.Handover.Add(handover);
        //                _billingDbContext.SaveChanges();

        //                if (denominationDetails.Count != 0)
        //                {
        //                    for (int i = 0; i < denominationDetails.Count; i++)
        //                    {
        //                        if (denominationDetails[i].Quantity != null && denominationDetails[i].Quantity != 0)
        //                        {
        //                            var handoverDetail = denominationDetails[i];
        //                            if (handoverDetail.DenominationId == null || handoverDetail.DenominationId == 0)
        //                            {
        //                                BillingDenominationModel denomination = new BillingDenominationModel();
        //                                {
        //                                    denomination.HandoverId = handover.HandoverId;
        //                                    denomination.CurrencyType = denominationDetails[i].CurrencyType;
        //                                    denomination.Quantity = denominationDetails[i].Quantity;
        //                                    denomination.Amount = denomination.CurrencyType * denomination.Quantity;
        //                                }
        //                                _billingDbContext.Denomination.Add(denomination);
        //                            }
        //                        }
        //                        _billingDbContext.SaveChanges();
        //                    }
        //                    dbContextTransaction.Commit(); //end of transaction
        //                }
        //                return denominationDetails;
        //            }
        //            else
        //            {
        //                throw new Exception("No handover detail to post");
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            //rollback all changes if any error occurs
        //            dbContextTransaction.Rollback();
        //            throw ex;
        //        }
        //    }
        //}

        private object SaveHandOverTransactionDetail(string ipDataString, RbacUser currentUser)
        {

            DateTime txnDateTime = System.DateTime.Now;

            BillingHandoverTransactionModel HandovertransactionInfo = DanpheJSONConvert.DeserializeObject<BillingHandoverTransactionModel>(ipDataString);
            HandovertransactionInfo.CreatedBy = currentUser.EmployeeId;
            HandovertransactionInfo.CreatedOn = txnDateTime;
            HandovertransactionInfo.IsActive = true;
            _billingDbContext.HandoverTransaction.Add(HandovertransactionInfo);
            _billingDbContext.SaveChanges();

            EmpCashTransactionModel empCashTransaction = new EmpCashTransactionModel();
            empCashTransaction.TransactionType = "HandoverGiven";
            empCashTransaction.ReferenceNo = HandovertransactionInfo.HandoverTxnId;
            empCashTransaction.InAmount = 0;
            empCashTransaction.OutAmount = HandovertransactionInfo.HandoverAmount;
            empCashTransaction.EmployeeId = currentUser.EmployeeId;
            empCashTransaction.TransactionDate = txnDateTime;
            empCashTransaction.CounterID = HandovertransactionInfo.CounterId;

            BillingBL.AddEmpCashTransaction(_billingDbContext, empCashTransaction);
            BillingBL.ReCalculateEmployeeDueAmount(_billingDbContext, currentUser.EmployeeId, txnDateTime);
            return HandovertransactionInfo;
        }

        private string UpdateBillItemsRequisitionStatus(string ipDataString, string status)
        {
            List<Int32> billItemReqIds = DanpheJSONConvert.DeserializeObject<List<Int32>>(ipDataString);
            BillItemRequisition billItem = new BillItemRequisition();
            foreach (var id in billItemReqIds)
            {
                billItem = (from bill in _billingDbContext.BillItemRequisitions
                            where bill.BillItemRequisitionId == id
                            select bill).FirstOrDefault();
                if (billItem != null)
                {
                    billItem.BillStatus = status;
                    _billingDbContext.Entry(billItem).State = EntityState.Modified;
                }
            }
            _billingDbContext.SaveChanges();
            return "Bill Items Requisition updated";
        }

        private int? PutBillStatusForCreditCancellation(string ipDataString, string status, string remarks, int CreatedBy)
        {

            int? currTxnItmId = DanpheJSONConvert.DeserializeObject<int?>(ipDataString);

            if (currTxnItmId != null && currTxnItmId.Value != 0)
            {

                BillingTransactionItemModel dbBillTxnItem = _billingDbContext.BillingTransactionItems
                                           .Where(a => a.BillingTransactionItemId == currTxnItmId)
                                           .FirstOrDefault<BillingTransactionItemModel>();
                if (dbBillTxnItem != null)
                {
                    dbBillTxnItem.CancelRemarks = remarks;
                    dbBillTxnItem.CancelledBy = CreatedBy;
                    dbBillTxnItem.BillStatus = status.ToLower();
                    dbBillTxnItem.CancelledOn = System.DateTime.Now;//added: sudarshan-25jul
                    _billingDbContext.Entry(dbBillTxnItem).State = EntityState.Modified;
                }
            }

            _billingDbContext.SaveChanges();
            return currTxnItmId;
        }

        private object CancelBillTxnItems(string ipDataString, RbacUser currentUser)
        {
            List<BillingTransactionItemModel> txnItemsToCancel = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(ipDataString);

            if (txnItemsToCancel != null && txnItemsToCancel.Count > 0)
            {
                //Transaction Begins  
                using (var dbContextTransaction = _billingDbContext.Database.BeginTransaction())
                {
                    try
                    {
                        for (int i = 0; i < txnItemsToCancel.Count; i++)
                        {
                            txnItemsToCancel[i] = BillingTransactionBL.UpdateTxnItemBillStatus(_billingDbContext,
                          txnItemsToCancel[i],
                          "cancel",
                          currentUser,
                          DateTime.Now);
                        }


                        _billingDbContext.SaveChanges();
                        dbContextTransaction.Commit(); //end of transaction
                        return txnItemsToCancel;
                    }
                    catch (Exception ex)
                    {
                        //rollback all changes if any error occurs
                        dbContextTransaction.Rollback();
                        throw ex;
                    }
                }
            }
            else
            {
                throw new Exception("No txn Item Selected To Cancel");
            }

        }
        private string UpdateInvoicePrintCount(int billingTransactionId, RbacUser currentUser, int printCount)
        {
            BillingTransactionModel dbBillPrintReq = _billingDbContext.BillingTransactions
                                    .Where(a => a.BillingTransactionId == billingTransactionId)
                                    .FirstOrDefault<BillingTransactionModel>();
            if (dbBillPrintReq != null)
            {
                dbBillPrintReq.PrintCount = printCount;
                dbBillPrintReq.PrintedOn = System.DateTime.Now;
                dbBillPrintReq.PrintedBy = currentUser.EmployeeId;
                _billingDbContext.Entry(dbBillPrintReq).State = EntityState.Modified;
            }

            _billingDbContext.SaveChanges();
            return "Print count updated successfully.";
        }

        private object UpdateDoctorAfterDoctorEdit(string ipDataString, RbacUser currentUser, string Prescriber, string Performer)
        {
            int BillTxnItemId = DanpheJSONConvert.DeserializeObject<int>(ipDataString);

            var PerformerObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Performer);
            var PrescriberObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Prescriber);
            BillingTransactionItemModel dbEditDoctor = _billingDbContext.BillingTransactionItems
                .Where(a => a.BillingTransactionItemId == BillTxnItemId)
                .FirstOrDefault<BillingTransactionItemModel>();

            LabRequisitionModel dbEditPrescriberInLabReq = _billingDbContext.LabRequisitions
                                                            .Where(a => a.BillingTransactionItemId == dbEditDoctor.BillingTransactionItemId)
                                                            .FirstOrDefault();

            if (dbEditDoctor != null)
            {
                dbEditDoctor.PerformerName = PerformerObj != null ? PerformerObj.EmployeeName : null;
                dbEditDoctor.PerformerId = PerformerObj != null ? PerformerObj.EmployeeId : null;
                dbEditDoctor.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
                dbEditDoctor.ModifiedBy = currentUser.EmployeeId;
                dbEditDoctor.ModifiedOn = DateTime.Now;
                //billingDbContext.Entry(dbEditDoctor).State = EntityState.Modified;
                _billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerId).IsModified = true;
                _billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerName).IsModified = true;
                _billingDbContext.Entry(dbEditDoctor).Property(p => p.PrescriberId).IsModified = true;
                _billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedBy).IsModified = true;
                _billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedOn).IsModified = true;
            }
            if (dbEditPrescriberInLabReq != null)
            {
                dbEditPrescriberInLabReq.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
                dbEditPrescriberInLabReq.PrescriberName = PrescriberObj != null ? PrescriberObj.EmployeeName : null;

                _billingDbContext.Entry(dbEditPrescriberInLabReq).Property(p => p.PrescriberId).IsModified = true;
                _billingDbContext.Entry(dbEditPrescriberInLabReq).Property(p => p.PrescriberName).IsModified = true;

            }
            _billingDbContext.SaveChanges();
            return BillTxnItemId;
        }

        private object UpdateDoctorForRadiology(string ipDataString, RbacUser currentUser, string Prescriber, string Performer)
        {

            int BillTxnItemId = DanpheJSONConvert.DeserializeObject<int>(ipDataString);
            int RequisitionId = Convert.ToInt32(this.ReadQueryStringData("RequisitionId"));
            var PerformerObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Performer);
            var PrescriberObj = DanpheJSONConvert.DeserializeObject<EmployeeBasicDetail>(Prescriber);
            BillingTransactionItemModel dbEditDoctor = _billingDbContext.BillingTransactionItems
                .Where(a => a.BillingTransactionItemId == BillTxnItemId)
                .FirstOrDefault<BillingTransactionItemModel>();

            using (var dbTransaction = _billingDbContext.Database.BeginTransaction())
            {
                try
                {
                    if (dbEditDoctor != null)
                    {
                        dbEditDoctor.PerformerName = PerformerObj != null ? PerformerObj.EmployeeName : null;
                        dbEditDoctor.PerformerId = PerformerObj != null ? PerformerObj.EmployeeId : null;
                        dbEditDoctor.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
                        dbEditDoctor.ModifiedBy = currentUser.EmployeeId;
                        dbEditDoctor.ModifiedOn = DateTime.Now;

                        _billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerName).IsModified = true;
                        _billingDbContext.Entry(dbEditDoctor).Property(p => p.PerformerId).IsModified = true;
                        _billingDbContext.Entry(dbEditDoctor).Property(p => p.PrescriberId).IsModified = true;
                        _billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedBy).IsModified = true;
                        _billingDbContext.Entry(dbEditDoctor).Property(p => p.ModifiedOn).IsModified = true;
                        //billingDbContext.Entry(dbEditDoctor).State = EntityState.Modified;
                        _billingDbContext.SaveChanges();

                    }

                    if (RequisitionId > 0)
                    {
                        ImagingRequisitionModel dbEditDoctorRad = _radiologyDbContext.ImagingRequisitions
                           .Where(a => a.ImagingRequisitionId == RequisitionId)
                           .FirstOrDefault<ImagingRequisitionModel>();

                        if (dbEditDoctorRad != null)
                        {
                            dbEditDoctorRad.PrescriberName = PerformerObj != null ? PrescriberObj.EmployeeName : null;
                            dbEditDoctorRad.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
                            dbEditDoctorRad.ModifiedBy = currentUser.EmployeeId;
                            dbEditDoctorRad.ModifiedOn = DateTime.Now;

                            _radiologyDbContext.Entry(dbEditDoctorRad).Property(p => p.PrescriberName).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRad).Property(p => p.PrescriberId).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRad).Property(p => p.ModifiedBy).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRad).Property(p => p.ModifiedOn).IsModified = true;
                            //radioDbContext.Entry(dbEditDoctorRad).State = EntityState.Modified;
                            _radiologyDbContext.SaveChanges();

                        }
                    }
                    if (RequisitionId > 0)
                    {

                        ImagingReportModel dbEditDoctorRadReport = _radiologyDbContext.ImagingReports
                            .Where(a => a.ImagingRequisitionId == RequisitionId)
                            .FirstOrDefault<ImagingReportModel>();
                        if (dbEditDoctorRadReport != null)
                        {
                            dbEditDoctorRadReport.PrescriberName = PrescriberObj != null ? PrescriberObj.EmployeeName : null;
                            dbEditDoctorRadReport.PrescriberId = PrescriberObj != null ? PrescriberObj.EmployeeId : null;
                            dbEditDoctorRadReport.PerformerId = PerformerObj != null ? PerformerObj.EmployeeId : null;
                            dbEditDoctorRadReport.PerformerName = PerformerObj != null ? PerformerObj.EmployeeName : null;

                            dbEditDoctorRadReport.ModifiedBy = currentUser.EmployeeId;
                            dbEditDoctorRadReport.ModifiedOn = DateTime.Now;

                            _radiologyDbContext.Entry(dbEditDoctorRadReport).Property(p => p.PrescriberName).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRadReport).Property(p => p.PerformerId).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRadReport).Property(p => p.PerformerName).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRadReport).Property(p => p.ModifiedBy).IsModified = true;
                            _radiologyDbContext.Entry(dbEditDoctorRadReport).Property(p => p.ModifiedOn).IsModified = true;
                            _radiologyDbContext.SaveChanges();

                        }

                    }


                    dbTransaction.Commit();
                    return BillTxnItemId;
                }
                catch (Exception ex)
                {
                    dbTransaction.Rollback();
                    throw ex;
                }
            }
        }

        private BillingDbContext Get_billingDbContext()
        {
            return _billingDbContext;
        }

        private object CancelOutpatientProvisionalItem(string ipDataString, RbacUser currentUser, BillingDbContext _billingDbContext)
        {

            using (var dbTransaction = _billingDbContext.Database.BeginTransaction())
            {
                try
                {

                    BillingTransactionItemModel txnItmFromClient = DanpheJSONConvert.DeserializeObject<BillingTransactionItemModel>(ipDataString);


                    BillingTransactionItemModel billItem = _billingDbContext.BillingTransactionItems
                                                                    .Where(itm => itm.ServiceItemId == txnItmFromClient.ServiceItemId
                                                                            && itm.PatientId == txnItmFromClient.PatientId
                                                                            && itm.PatientVisitId == txnItmFromClient.PatientVisitId
                                                                            && itm.BillingTransactionItemId == txnItmFromClient.BillingTransactionItemId
                                                                        ).FirstOrDefault<BillingTransactionItemModel>();

                    _billingDbContext.BillingTransactionItems.Attach(billItem);

                    _billingDbContext.Entry(billItem).Property(a => a.BillStatus).IsModified = true;
                    _billingDbContext.Entry(billItem).Property(a => a.CancelledBy).IsModified = true;
                    _billingDbContext.Entry(billItem).Property(a => a.CancelledOn).IsModified = true;
                    _billingDbContext.Entry(billItem).Property(a => a.CancelRemarks).IsModified = true;

                    billItem.BillStatus = ENUM_BillingStatus.cancel;// "cancel";
                    billItem.CancelledBy = currentUser.EmployeeId;
                    billItem.CancelledOn = System.DateTime.Now;
                    billItem.CancelRemarks = txnItmFromClient.CancelRemarks;

                    //fetch cancellation rules for lab and radiology
                    var parameter = _billingDbContext.AdminParameters.Where(a => a.ParameterGroupName == "Common" && a.ParameterName == "CancellationRules").FirstOrDefault();
                    bool IsCancellationEnabled = false;
                    var LabCancellationStatus = new List<string>();
                    var ImagingCancellationStatus = new List<string>();
                    if (parameter != null)
                    {
                        string paramValueStr = parameter.ParameterValue;
                        // Deserialize the JSON string into a dynamic object
                        dynamic data = JsonConvert.DeserializeObject(paramValueStr);
                        if (data != null)
                        {
                            // Accessing properties of the JSON object
                            IsCancellationEnabled = data.Enable;
                            LabCancellationStatus = data.LabItemsInBilling.ToObject<List<string>>();
                            ImagingCancellationStatus = data.ImagingItemsInBilling.ToObject<List<string>>();
                        }
                    }

                    bool allowCancellationForLab = true;
                    bool allowCancellationForImaging = true;


                    var labItem = _billingDbContext.LabRequisitions.FirstOrDefault(a => a.BillingTransactionItemId == billItem.BillingTransactionItemId);
                    var radiologyItem = _billingDbContext.RadiologyImagingRequisitions.FirstOrDefault(a => a.BillingTransactionItemId == billItem.BillingTransactionItemId);

                    if (IsCancellationEnabled && labItem != null && !LabCancellationStatus.Contains(labItem.OrderStatus))
                    {
                        allowCancellationForLab = false;
                    }

                    if (IsCancellationEnabled && radiologyItem != null && !ImagingCancellationStatus.Contains(radiologyItem.OrderStatus))
                    {
                        allowCancellationForImaging = false;
                    }

                    if (labItem != null && allowCancellationForLab) //check for cancellation rules
                    {
                        //cancel this item
                        labItem.BillingStatus = ENUM_BillingStatus.cancel;
                        labItem.BillCancelledBy = currentUser.EmployeeId;
                        labItem.BillCancelledOn = System.DateTime.Now;
                        //update BillingStatus = cancel, BillCancelledBy, BillCancelledOn
                    }
                    if (radiologyItem != null && allowCancellationForImaging) //check for cancellation rules
                    {
                        //cancel this item
                        // update BillingStatus = cancel, BillCancelledBy, BillCancelledOn
                        radiologyItem.BillingStatus = ENUM_BillingStatus.cancel;
                        radiologyItem.BillCancelledBy = currentUser.EmployeeId;
                        radiologyItem.BillCancelledOn = System.DateTime.Now;

                    }
                    if ((labItem != null && allowCancellationForLab) || (radiologyItem != null && allowCancellationForImaging))
                    {
                        //dump this cancelled item to a new table created
                        //logic to add to a new table.
                        BillingCancellationModel cancelledItem = new BillingCancellationModel();
                        {
                            var PatientVisitId = txnItmFromClient.PatientVisitId;

                            int fiscalYearId = _billingDbContext.BillingFiscalYears.Where(f => f.StartYear <= DateTime.Today && f.EndYear >= DateTime.Today).Select(f => f.FiscalYearId).FirstOrDefault();

                            int receiptNo = BillingTransactionBL.GetCancellationReceiptNo(_billingDbContext, fiscalYearId);

                            cancelledItem.BillingTransactionItemId = billItem.BillingTransactionItemId;
                            cancelledItem.ReferenceProvisionalReceiptNo = (int)billItem.ProvisionalReceiptNo;
                            cancelledItem.CancellationReceiptNo = receiptNo;
                            cancelledItem.CancellationFiscalYearId = (int)billItem.ProvisionalFiscalYearId;
                            cancelledItem.PatientId = billItem.PatientId;
                            cancelledItem.PatientVisitId = (int)PatientVisitId;
                            cancelledItem.BillingType = billItem.BillingType;
                            cancelledItem.VisitType = billItem.VisitType;
                            cancelledItem.ServiceItemId = billItem.ServiceItemId;
                            cancelledItem.ServiceDepartmentId = billItem.ServiceDepartmentId;
                            cancelledItem.ItemName = billItem.ItemName;
                            cancelledItem.ItemCode = billItem.ItemCode;
                            cancelledItem.IntegrationItemId = (int)billItem.IntegrationItemId;
                            cancelledItem.Price = (decimal)billItem.Price;
                            cancelledItem.CancelledQty = (int)billItem.Quantity;
                            cancelledItem.CancelledSubtotal = (decimal)billItem.SubTotal;
                            cancelledItem.CancelledDiscountPercent = (decimal)billItem.DiscountPercent;
                            cancelledItem.CancelledDiscountAmount = (decimal)billItem.DiscountAmount;
                            cancelledItem.CancelledTotalAmount = (decimal)billItem.TotalAmount;
                            cancelledItem.PerformerId = billItem.PerformerId;
                            cancelledItem.PrescriberId = billItem.PrescriberId;
                            cancelledItem.CancelledCounterId = billItem.CounterId;
                            cancelledItem.CancellationRemarks = billItem.CancelRemarks;
                            cancelledItem.SchemeId = billItem.DiscountSchemeId;
                            cancelledItem.PriceCategoryId = billItem.PriceCategoryId;
                            cancelledItem.CreatedBy = currentUser.EmployeeId;
                            cancelledItem.CreatedOn = DateTime.Now;
                            cancelledItem.IsActive = true;
                            cancelledItem.ModifiedOn = null;
                            cancelledItem.ModifiedBy = null;
                            _billingDbContext.BillingCancellation.Add(cancelledItem);
                            _billingDbContext.SaveChanges();
                        };


                        dbTransaction.Commit();
                        return cancelledItem.ProvisionalItemReturnId;
                    }
                    else
                    {
                        string orderStatus;
                        if (labItem != null)
                        {
                            orderStatus = labItem.OrderStatus;
                        }
                        else if (radiologyItem != null)
                        {
                            orderStatus = radiologyItem.OrderStatus;
                        }
                        else
                        {
                            orderStatus = "Unknown";
                        }

                        throw new Exception($"Cancellation not allowed for the items with {orderStatus} orderStatus");
                    }



                }
                catch (Exception ex)
                {
                    dbTransaction.Rollback();
                    throw ex;
                }
            }
        }

        private DataTable CancelIpItemFromWard(string ipDataString, RbacUser currentUser)
        {

            BillingTransactionItemModel inpatientTest = DanpheJSONConvert.DeserializeObject<BillingTransactionItemModel>(ipDataString);
            List<SqlParameter> paramList = new List<SqlParameter>(){
                                                    new SqlParameter("@BillingTransactionItemId", inpatientTest.BillingTransactionItemId),
                                                    new SqlParameter("@RequisitionId", (inpatientTest.RequisitionId.HasValue? inpatientTest.RequisitionId : 0)),
                                                    new SqlParameter("@IntegrationName", inpatientTest.ItemIntegrationName),
                                                    new SqlParameter("@UserId", currentUser.EmployeeId),
                                                    new SqlParameter("@Remarks", inpatientTest.CancelRemarks)
                                                };

            DataTable cancelItem = DALFunctions.GetDataTableFromStoredProc("SP_BillItemCancellation_From_Ward", paramList, _billingDbContext);

            _billingDbContext.SaveChanges();
            return cancelItem;
        }

        private object EditItemPriceQuantityDiscountAndProvider(RbacUser currentUser, string ipDataString)
        {
            BillingTransactionItemModel txnItmFromClient = DanpheJSONConvert.DeserializeObject<BillingTransactionItemModel>(ipDataString);
            txnItmFromClient.ModifiedBy = currentUser.EmployeeId;
            BillingTransactionBL.UpdateBillingTransactionItems(_billingDbContext, txnItmFromClient);
            if (txnItmFromClient.ModifiedBy != null)
            {
                var ModifiedByName = (from emp in _billingDbContext.Employee
                                      where emp.EmployeeId == txnItmFromClient.ModifiedBy
                                      select emp.FirstName + " " + emp.LastName).FirstOrDefault();
                txnItmFromClient.ModifiedByName = ModifiedByName;
            }
            return txnItmFromClient;
        }

        //private string UpdateItemGroupDisc(string ipDataString)
        //{
        //    List<BillingTransactionItemModel> txnItems = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(ipDataString);
        //    txnItems.ForEach(item =>
        //    {
        //        BillingTransactionBL.UpdateBillingTransactionItems(_billingDbContext, item);
        //    });
        //    return "Item Group Discount Updated";
        //}

        private string UpdateHandoverTxnDetail(RbacUser currentUser, string ipDataString)
        {
            BillingHandoverTransactionModel handoverTxnFromClient = DanpheJSONConvert.DeserializeObject<BillingHandoverTransactionModel>(ipDataString);

            BillingHandoverTransactionModel handovertxnFromDb = _billingDbContext.HandoverTransaction.Where(txn => txn.HandoverTxnId == handoverTxnFromClient.HandoverTxnId).FirstOrDefault();

            //billingDbContext.HandoverTransaction.Attach(handovertxnFromDb);
            handovertxnFromDb.HandoverByEmpId = handoverTxnFromClient.HandoverByEmpId;
            handovertxnFromDb.CounterId = handoverTxnFromClient.CounterId;
            handovertxnFromDb.HandoverType = handoverTxnFromClient.HandoverType;
            handovertxnFromDb.BankName = handoverTxnFromClient.BankName;
            handovertxnFromDb.VoucherNumber = handoverTxnFromClient.VoucherNumber;
            handovertxnFromDb.VoucherDate = handoverTxnFromClient.VoucherDate;
            handovertxnFromDb.HandoverAmount = handoverTxnFromClient.HandoverAmount;
            handovertxnFromDb.DueAmount = handoverTxnFromClient.DueAmount;
            handovertxnFromDb.HandoverRemarks = handoverTxnFromClient.HandoverRemarks;
            handovertxnFromDb.ReceiveRemarks = handoverTxnFromClient.ReceiveRemarks;
            handovertxnFromDb.ReceivedById = currentUser.EmployeeId;
            handovertxnFromDb.ReceivedOn = System.DateTime.Now;
            handovertxnFromDb.HandoverStatus = ENUM_HandOverStatus.Received;

            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverByEmpId).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.CounterId).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverType).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.BankName).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.VoucherNumber).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.VoucherDate).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverAmount).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.DueAmount).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverRemarks).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.ReceivedById).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.ReceivedOn).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.ReceiveRemarks).IsModified = true;
            _billingDbContext.Entry(handovertxnFromDb).Property(a => a.HandoverStatus).IsModified = true;

            _billingDbContext.SaveChanges();
            return "Handover Amount Recived.";
        }

        private object GetPatientCreditInfo(int patientId)
        {
            var TotalCreditSales = _billingDbContext.BillingTransactions.Where(a => a.PatientId == patientId
                                                   && a.BillStatus == ENUM_BillingStatus.unpaid
                                                   && a.PaymentMode == ENUM_BillPaymentMode.credit && a.IsInsuranceBilling != true)
                                           .Select(b => b.TotalAmount).Sum();

            var TotalReturn = (from txn in _billingDbContext.BillingTransactions
                               join itms in _billingDbContext.BillingTransactionItems
                               on txn.BillingTransactionId equals itms.BillingTransactionId
                               join retItms in _billingDbContext.BillInvoiceReturnItems
                               on itms.BillingTransactionItemId equals retItms.BillingTransactionItemId
                               where txn.PatientId == patientId && txn.BillStatus == ENUM_BillingStatus.unpaid && txn.PaymentMode == ENUM_BillPaymentMode.credit && txn.IsInsuranceBilling == false
                               select retItms.RetTotalAmount).ToList().Sum();

            //var TotalReturn = _billingDbContext.BillInvoiceReturnItems
            //                    .Join(_billingDbContext.BillInvoiceReturnItems,
            //                    bill => bill.BillingTransactionId,
            //                    billItems => billItems.BillingTransactionId,
            //                    (bill, billItems) => new { bill, billItems })
            //                    .Join(_billingDbContext.BillInvoiceReturnItems,
            //                    txns => txns.billItems.BillingTransactionItemId,
            //                    retItms => retItms.BillingTransactionItemId,
            //                    (txns, retItms) => new { txns, retItms })
            //                    .Select(s => s.retItms.RetTotalAmount)
            //                    .Sum();

            var CreditInfo = (TotalCreditSales - TotalReturn);
            return CreditInfo;
        }

        private object GetSsfInvoices(string fromDate, string toDate, string patientType, BillingDbContext billingDbContext)
        {
            List<SqlParameter> paramsList = new List<SqlParameter>();
            paramsList.Add(new SqlParameter("@FromDate", fromDate));
            paramsList.Add(new SqlParameter("@ToDate", toDate));
            paramsList.Add(new SqlParameter("@PatientType", patientType));


            //there are five return table coming from this stored procedure.
            DataSet dsPrintData = DALFunctions.GetDatasetFromStoredProc("SP_SSF_InvoiceInfo", paramsList, billingDbContext);

            DataTable dtPatientInfo = dsPrintData.Tables[0];
            DataTable dtInvoiceInfo = dsPrintData.Tables[1];
            DataTable dtInvItems = dsPrintData.Tables[2];
            DataTable dtPhrmInvoice = dsPrintData.Tables[3];
            DataTable dtPhrmInvItems = dsPrintData.Tables[4];
            DataTable dtLabReportInfo = dsPrintData.Tables[5];
            DataTable dtRadiologyReportInfo = dsPrintData.Tables[6];
            DataTable dtBillingInvoiceReturns = dsPrintData.Tables[7];
            DataTable dtPharmacyInvoiceReturns = dsPrintData.Tables[8];


            //group them in a new anonymous object and send to client.
            var printInfoToReturn = new
            {
                PatientInfo = dtPatientInfo,
                BillingInvoiceInfo = dtInvoiceInfo,
                BillingInvoiceItems = dtInvItems,
                PhrmInvoices = dtPhrmInvoice,
                PhrmInvoiceItems = dtPhrmInvItems,
                LabReportInfo = dtLabReportInfo,
                RadiologyReportInfo = dtRadiologyReportInfo,
                BillingInvoiceReturns = dtBillingInvoiceReturns,
                PharmacyInvoiceReturns = dtPharmacyInvoiceReturns
            };
            return printInfoToReturn;
        }

        private object GetPendingOutgoingHandoverDetail(string handOverType, RbacUser currentUser)
        {
            var txn = new Object();
            if (handOverType == ENUM_HandOverType.User)
            {
                txn = _billingDbContext.HandoverTransaction.Where(a => a.HandoverType == handOverType
                               && a.HandoverStatus == ENUM_HandOverStatus.Pending && a.HandoverByEmpId == currentUser.EmployeeId)
                               .Join(_billingDbContext.Employee, handover => handover.HandoverToEmpId, employee => employee.EmployeeId, (handover, employee) => new
                               {
                                   CreatedOn = handover.CreatedOn,
                                   HandOverAmount = handover.HandoverAmount,
                                   HandOverByName = currentUser.UserName,
                                   HandOverToName = employee.FullName
                               }).ToListAsync();
            }
            else
            {
                txn = _billingDbContext.HandoverTransaction.Where(a => a.HandoverType == handOverType
                               && a.HandoverStatus == ENUM_HandOverStatus.Pending && a.HandoverByEmpId == currentUser.EmployeeId)
                               .Select(a => new
                               {
                                   CreatedOn = a.CreatedOn,
                                   HandOverAmount = a.HandoverAmount,
                                   HandOverByName = currentUser.UserName,
                               }).ToListAsync();
            }
            return txn;
        }

        private IActionResult UpdateHandoverStatus(int handOverTransactionId, DateTime txnDate, RbacUser currentUser)
        {
            BillingHandoverTransactionModel txn = _billingDbContext.HandoverTransaction.Where(a => a.HandoverTxnId == handOverTransactionId).FirstOrDefault();
            txn.HandoverStatus = ENUM_HandOverStatus.Received;
            txn.ReceivedById = currentUser.EmployeeId;
            txn.ReceivedOn = txnDate;
            _billingDbContext.Entry(txn).Property(a => a.HandoverStatus).IsModified = true;
            _billingDbContext.Entry(txn).Property(a => a.ReceivedById).IsModified = true;
            _billingDbContext.Entry(txn).Property(a => a.ReceivedOn).IsModified = true;
            _billingDbContext.SaveChanges();

            EmpCashTransactionModel empCashTransaction = new EmpCashTransactionModel();
            empCashTransaction.TransactionType = ENUM_EmpCashTransactionType.HandoverReceived;
            empCashTransaction.ReferenceNo = handOverTransactionId;
            empCashTransaction.InAmount = txn.HandoverAmount;
            empCashTransaction.OutAmount = 0;
            empCashTransaction.EmployeeId = currentUser.EmployeeId;
            empCashTransaction.TransactionDate = txnDate;
            empCashTransaction.CounterID = txn.CounterId;

            BillingBL.AddEmpCashTransaction(_billingDbContext, empCashTransaction);
            BillingBL.ReCalculateEmployeeDueAmount(_billingDbContext, currentUser.EmployeeId, txnDate);
            return Ok("HandOver Successfully Received.");
        }

        private object NewBillingTransaction(string strBillingTransactionData, RbacUser currentUser)
        {
            BillingTransactionPostVM billingTransactionPostVM = DanpheJSONConvert.DeserializeObject<BillingTransactionPostVM>(strBillingTransactionData);
            BillingTransactionModel billingTransactionModel = new BillingTransactionModel();
            using (var billingTransactionScope = _billingDbContext.Database.BeginTransaction())
            {
                try
                {

                    if (!BillingTransactionBL.IsDepositAvailable(_billingDbContext, billingTransactionPostVM.Txn.PatientId, billingTransactionPostVM.Txn.DepositUsed))
                    {
                        throw new Exception("Invalid Deposit Amount is Submitted");
                    }


                    //if (billingTransactionPostVM.LabRequisition != null && billingTransactionPostVM.LabRequisition.Count > 0)
                    //{
                    //    billingTransactionPostVM.LabRequisition = AddLabRequisition(_billingDbContext, billingTransactionPostVM.LabRequisition, currentUser.EmployeeId);
                    //}
                    //if (billingTransactionPostVM.ImagingItemRequisition != null && billingTransactionPostVM.ImagingItemRequisition.Count > 0)
                    //{
                    //    billingTransactionPostVM.ImagingItemRequisition = AddImagingRequisition(_billingDbContext, billingTransactionPostVM.ImagingItemRequisition, currentUser.EmployeeId);
                    //}
                    if (billingTransactionPostVM.VisitItems != null && billingTransactionPostVM.VisitItems.Count > 0)
                    {
                        billingTransactionPostVM.VisitItems.ForEach(a => a.BillingStatus = billingTransactionPostVM.Txn.BillStatus);
                        billingTransactionPostVM.VisitItems = AddVisitItems(_billingDbContext, billingTransactionPostVM.VisitItems, currentUser.EmployeeId);
                    }

                    //billingTransactionModel.BillingTransactionItems = new List<BillingTransactionItemModel>();

                    //if (billingTransactionPostVM.LabRequisition != null && billingTransactionPostVM.LabRequisition.Count > 0)
                    //{
                    //    MapLabRequsitionId(billingTransactionPostVM.Txn.BillingTransactionItems, billingTransactionPostVM.LabRequisition);
                    //}
                    //if (billingTransactionPostVM.ImagingItemRequisition != null && billingTransactionPostVM.ImagingItemRequisition.Count > 0)
                    //{
                    //    MapRadiologyRequisitionId(billingTransactionPostVM.Txn.BillingTransactionItems, billingTransactionPostVM.ImagingItemRequisition);
                    //}
                    if (billingTransactionPostVM.VisitItems != null && billingTransactionPostVM.VisitItems.Count > 0)
                    {
                        MapPatientVisitId(billingTransactionPostVM.Txn.BillingTransactionItems, billingTransactionPostVM.VisitItems);
                    }
                    billingTransactionModel = billingTransactionPostVM.Txn;

                    billingTransactionModel = PostBillingTransaction(_billingDbContext, billingTransactionPostVM, billingTransactionModel, currentUser);

                    billingTransactionModel.BillingUserName = currentUser.UserName;

                    if (billingTransactionModel != null)
                    {
                        billingTransactionScope.Commit();
                    }

                }
                catch (Exception ex)
                {
                    billingTransactionScope.Rollback();
                    throw new Exception(ex.Message + " exception details:" + ex.ToString());
                }
            }

            return billingTransactionModel;
        }
        private object NewProvisionalBilling(ref BillingTransactionModel billingTransactionModel, string strBillingTransactionData, RbacUser currentUser)
        {
            BillingTransactionPostVM billingTransactionPostVM = DanpheJSONConvert.DeserializeObject<BillingTransactionPostVM>(strBillingTransactionData);

            using (var provisionalBillingTxnScope = _billingDbContext.Database.BeginTransaction())
            {

                try
                {
                    //if (billingTransactionPostVM.LabRequisition != null && billingTransactionPostVM.LabRequisition.Count > 0)
                    //{
                    //    billingTransactionPostVM.LabRequisition = AddLabRequisition(_billingDbContext, billingTransactionPostVM.LabRequisition, currentUser.EmployeeId);
                    //}
                    //if (billingTransactionPostVM.ImagingItemRequisition != null && billingTransactionPostVM.ImagingItemRequisition.Count > 0)
                    //{
                    //    billingTransactionPostVM.ImagingItemRequisition = AddImagingRequisition(_billingDbContext, billingTransactionPostVM.ImagingItemRequisition, currentUser.EmployeeId);
                    //}
                    //if (billingTransactionPostVM.VisitItems != null && billingTransactionPostVM.VisitItems.Count > 0)
                    //{
                    //    billingTransactionPostVM.VisitItems = AddVisitItems(_billingDbContext, billingTransactionPostVM.VisitItems, currentUser.EmployeeId);
                    //}

                    //billingTransactionModel.BillingTransactionItems = new List<BillingTransactionItemModel>();

                    //if (billingTransactionPostVM.LabRequisition != null && billingTransactionPostVM.LabRequisition.Count > 0)
                    //{
                    //    MapLabRequsitionId(billingTransactionPostVM.Txn.BillingTransactionItems, billingTransactionPostVM.LabRequisition);
                    //}
                    //if (billingTransactionPostVM.ImagingItemRequisition != null && billingTransactionPostVM.ImagingItemRequisition.Count > 0)
                    //{
                    //    MapRadiologyRequisitionId(billingTransactionPostVM.Txn.BillingTransactionItems, billingTransactionPostVM.ImagingItemRequisition);
                    //}
                    //if (billingTransactionPostVM.VisitItems != null && billingTransactionPostVM.VisitItems.Count > 0)
                    //{
                    //    MapPatientVisitId(billingTransactionPostVM.Txn.BillingTransactionItems, billingTransactionPostVM.VisitItems);
                    //}
                    billingTransactionModel = billingTransactionPostVM.Txn;

                    billingTransactionModel.BillingTransactionItems = PostProvisionalBilling(_billingDbContext, billingTransactionPostVM, billingTransactionModel, currentUser);

                    billingTransactionModel.BillingUserName = currentUser.UserName;

                    if (billingTransactionModel.BillingTransactionItems != null && billingTransactionModel.BillingTransactionItems.Count > 0)
                    {
                        provisionalBillingTxnScope.Commit();
                    }
                }
                catch (Exception ex)
                {
                    provisionalBillingTxnScope.Rollback();
                    throw new Exception(ex.Message + " exception details:" + ex.ToString());
                }
            }
            return billingTransactionModel.BillingTransactionItems;
        }
        #region Post Provisional Billing after the items requisition..
        private List<BillingTransactionItemModel> PostProvisionalBilling(BillingDbContext billingDbContext, BillingTransactionPostVM billingTransactionPostVM, BillingTransactionModel billingTransactionModel, RbacUser currentUser)
        {
            try
            {
                if (billingTransactionModel != null && billingTransactionModel.BillingTransactionItems.Count > 0)
                {
                    billingTransactionModel.BillingTransactionItems = BillingTransactionBL.PostUpdateBillingTransactionItems(billingDbContext,
                        connString,
                        billingTransactionModel.BillingTransactionItems,
                        currentUser,
                        DateTime.Now,
                        billingTransactionModel.BillingTransactionItems[0].BillStatus,
                        billingTransactionModel.BillingTransactionItems[0].CounterId);

                    var userName = (from emp in billingDbContext.Employee where emp.EmployeeId == currentUser.EmployeeId select emp.FirstName).FirstOrDefault();
                    billingTransactionModel.BillingTransactionItems.ForEach(usr => usr.RequestingUserName = userName);

                    BillingTransactionBL.AddProvisionalRequisitions(billingDbContext, connString, billingTransactionPostVM, billingTransactionModel.BillingTransactionItems, currentUser);

                }
                return billingTransactionModel.BillingTransactionItems;
            }
            catch (Exception ex)
            {

                throw ex;
            }

        }

        #endregion
        #endregion
        #region Maps the PatientVisitId with RequisitionId for erVisit or opVisit
        private List<BillingTransactionItemModel> MapPatientVisitId(List<BillingTransactionItemModel> billingTransactionItems, List<VisitModel> visitItems)
        {
            for (int i = 0; i < billingTransactionItems.Count; i++)
            {
                var erVisit = visitItems.Where(a => billingTransactionItems[i].ItemName.ToLower() == "emergency registration" && a.PerformerId == billingTransactionItems[i].PerformerId);
                if (erVisit != null && (billingTransactionItems[i].RequisitionId == null || billingTransactionItems[i].RequisitionId == 0))
                {
                    billingTransactionItems[i].RequisitionId = erVisit.Select(a => a.ParentVisitId).FirstOrDefault();

                }

                var opVisit = visitItems.Where(a => billingTransactionItems[i].ItemIntegrationName.ToLower() == "opd" && a.PerformerId == billingTransactionItems[i].PerformerId);
                if (opVisit != null && (billingTransactionItems[i].RequisitionId == null || billingTransactionItems[i].RequisitionId == 0))
                {
                    billingTransactionItems[i].RequisitionId = opVisit.Select(a => a.PatientVisitId).FirstOrDefault();
                }

            }

            return billingTransactionItems;
        }

        #endregion
        #region Maps the RequisitionId after Lab items are added in the LabRequisition table
        private List<BillingTransactionItemModel> MapLabRequsitionId(List<BillingTransactionItemModel> billingTransactionItems, List<LabRequisitionModel> labRequisition)
        {
            var itms = billingTransactionItems.Where(a => a.ItemIntegrationName.ToLower() == "lab").ToList();
            for (int i = 0; i < itms.Count; i++)
            {

                itms[i].RequisitionId = labRequisition[i].RequisitionId;
                itms[i].PatientVisitId = labRequisition[i].PatientVisitId;

            }

            return itms;
        }

        #endregion
        #region Maps the ImagingRequisitionId to the RequisitionId..
        private List<BillingTransactionItemModel> MapRadiologyRequisitionId(List<BillingTransactionItemModel> billingTransactionItems, List<ImagingRequisitionModel> imagingRequisitions)
        {
            var itms = billingTransactionItems.Where(a => a.ItemIntegrationName.ToLower() == "radiology").ToList();
            for (int i = 0; i < itms.Count; i++)
            {
                itms[i].RequisitionId = imagingRequisitions[i].ImagingRequisitionId;
                itms[i].PatientVisitId = imagingRequisitions[i].PatientVisitId;

            }

            return itms;
        }

        #endregion


        #region After the Requisitions are made it makes the invoice..
        private BillingTransactionModel PostBillingTransaction(BillingDbContext billingDbContext, BillingTransactionPostVM billingTransactionPostVM, BillingTransactionModel billingTransactionModel, RbacUser currentUser)
        {

            try
            {
                billingTransactionModel = BillingTransactionBL.PostBillingTransaction(billingDbContext, connString, billingTransactionPostVM, billingTransactionModel, currentUser, DateTime.Now);

                //Billing User should be assigned from the server side avoiding assigning from client side 
                //which is causing issue in billing receipt while 2 user loggedIn in the same browser
                billingTransactionModel.BillingUserName = currentUser.UserName;//Yubaraj: 28June'19                            


                //send to IRD only after transaction is committed successfully: sud-23Dec'18
                //Below is asynchronous call, so even if IRD api is down, it won't hold the execution of other code..
                if (realTimeRemoteSyncEnabled)
                {
                    if (billingTransactionModel.Patient == null)
                    {
                        PatientModel pat = billingDbContext.Patient.Where(p => p.PatientId == billingTransactionModel.PatientId).FirstOrDefault();
                        billingTransactionModel.Patient = pat;
                    }
                    //making parallel thread call (asynchronous) to post to IRD. so that it won't stop the normal execution of logic.
                    // BillingBL.SyncBillToRemoteServer(billTransaction, "sales", billingDbContext);
                    Task.Run(() => BillingBL.SyncBillToRemoteServer(billingTransactionModel, "sales", billingDbContext));
                }

                //Send to SSF Server for Real time ClaimBooking.
                var patientSchemes = billingDbContext.PatientSchemeMaps.Where(a => a.SchemeId == billingTransactionModel.SchemeId && a.PatientId == billingTransactionModel.PatientId).FirstOrDefault();
                if (patientSchemes != null)
                {
                    int priceCategoryId = billingTransactionModel.BillingTransactionItems[0].PriceCategoryId;
                    var priceCategory = billingDbContext.PriceCategoryModels.Where(a => a.PriceCategoryId == priceCategoryId).FirstOrDefault();
                    if (priceCategory != null && priceCategory.PriceCategoryName.ToLower() == "ssf" && RealTimeSSFClaimBooking)
                    {
                        //making parallel thread call (asynchronous) to post to SSF Server. so that it won't stop the normal BillingFlow.
                        SSFDbContext ssfDbContext = new SSFDbContext(connString);
                        var billObj = new SSF_ClaimBookingBillDetail_DTO()
                        {
                            InvoiceNoFormatted = $"BL{billingTransactionModel.InvoiceNo}",
                            TotalAmount = (decimal)billingTransactionModel.TotalAmount,
                            ClaimCode = (long)billingTransactionModel.ClaimCode
                        };

                        SSF_ClaimBookingService_DTO claimBooking = SSF_ClaimBookingService_DTO.GetMappedToBookClaim(billObj, patientSchemes);

                        Task.Run(() => BillingBL.SyncToSSFServer(claimBooking, "billing", ssfDbContext, patientSchemes, currentUser));
                    }
                }
                return billingTransactionModel;
            }
            catch (Exception ex)
            {
                throw ex;

            }

        }

        #endregion
        #region It creates the visit for the patient..
        private List<VisitModel> AddVisitItems(BillingDbContext billingDbContext, List<VisitModel> visitItems, int employeeId)
        {
            /*visitItems.ForEach(visit =>
            {
                visit.VisitCode = CreateNewPatientVisitCode(visit.VisitType, billingDbContext);
                billingDbContext.Visit.Add(visit);

            });
            billingDbContext.SaveChanges();*/
            GeneratePatientVisitCodeAndSave(billingDbContext, visitItems);
            return visitItems;
        }

        #region Generates the unque patientVisitcode (it keeps on trying until it gets the unique Visitcode)..
        private void GeneratePatientVisitCodeAndSave(BillingDbContext billingDbContext, List<VisitModel> visitItems)
        {
            try
            {
                visitItems.ForEach(visit =>
                {
                    //below code can be used to test the workflow for the duplicate visitCode...
                    if (testCount == 1) { visit.VisitCode = "V2200003"; testCount++; }
                    else
                        visit.VisitCode = CreateNewPatientVisitCode(visit.VisitType, billingDbContext);
                    billingDbContext.Visit.Add(visit);

                });
                billingDbContext.SaveChanges();
            }
            catch (Exception ex)
            {

                if (ex is DbUpdateException dbUpdateEx)
                {
                    if (dbUpdateEx.InnerException?.InnerException is SqlException sqlException)
                    {

                        if (sqlException.Number == 2627)// unique constraint error 
                        {
                            GeneratePatientVisitCodeAndSave(billingDbContext, visitItems);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    else throw;
                }
                else throw;
            }
        }

        #endregion
        #endregion
        #region Creates the PatientVisitCode..
        private string CreateNewPatientVisitCode(string visitType, BillingDbContext billingDbContext)
        {
            try
            {
                var visitCode = "";
                if (visitType != null)
                {
                    //VisitDbContext visitDbContext = new VisitDbContext(connString);
                    var year = DateTime.Now.Year;
                    var patVisitId = billingDbContext.Visit.Where(s => s.VisitType == visitType && s.VisitDate.Year == year && s.VisitCode != null).DefaultIfEmpty()
                        .Max(t => t.PatientVisitId == null ? 0 : t.PatientVisitId);
                    string codeChar;
                    switch (visitType)
                    {
                        case "inpatient":
                            codeChar = "H";
                            break;
                        case "emergency":
                            codeChar = "ER";
                            break;
                        default:
                            codeChar = "V";
                            break;
                    }
                    if (patVisitId > 0)
                    {
                        var vCodMax = (from v in billingDbContext.Visit
                                       where v.PatientVisitId == patVisitId
                                       select v.VisitCode).FirstOrDefault();
                        int newCodeDigit = Convert.ToInt32(vCodMax.Substring(codeChar.Length + 2)) + 1;
                        visitCode = (string)codeChar + DateTime.Now.ToString("yy") + String.Format("{0:D5}", newCodeDigit);
                    }
                    else
                    {
                        visitCode = (string)codeChar + DateTime.Now.ToString("yy") + String.Format("{0:D5}", 1);
                    }
                }
                return visitCode;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        #endregion
        #region Creates the Radiology Requisition..
        private List<ImagingRequisitionModel> AddImagingRequisition(BillingDbContext billingDbContext, List<ImagingRequisitionModel> imagingItemRequisition, int employeeId)
        {
            try
            {
                //getting the imagingtype because imagingtypename is needed in billing for getting service department
                List<RadiologyImagingTypeModel> Imgtype = billingDbContext.RadiologyImagingTypes
                                    .ToList<RadiologyImagingTypeModel>();

                var notValidForReportingItem = billingDbContext.RadiologyImagingItems.Where(i => i.IsValidForReporting == false).Select(m => m.ImagingItemId);

                if (imagingItemRequisition != null && imagingItemRequisition.Count > 0)
                {
                    foreach (var req in imagingItemRequisition)
                    {
                        req.ImagingDate = System.DateTime.Now;
                        req.CreatedOn = DateTime.Now;
                        req.CreatedBy = employeeId;
                        req.IsActive = true;
                        if (req.PrescriberId != null && req.PrescriberId != 0)
                        {
                            var emp = billingDbContext.Employee.Where(a => a.EmployeeId == req.PrescriberId).FirstOrDefault();
                            req.PrescriberName = emp.FullName;
                        }
                        if (req.ImagingTypeId != null)
                        {
                            req.ImagingTypeName = Imgtype.Where(a => a.ImagingTypeId == req.ImagingTypeId).Select(a => a.ImagingTypeName).FirstOrDefault();
                            req.Urgency = string.IsNullOrEmpty(req.Urgency) ? "normal" : req.Urgency;
                            //req.WardName = ;
                        }
                        else
                        {
                            req.ImagingTypeId = Imgtype.Where(a => a.ImagingTypeName.ToLower() == req.ImagingTypeName.ToLower()).Select(a => a.ImagingTypeId).FirstOrDefault();
                        }
                        if (!notValidForReportingItem.Contains(req.ImagingItemId.Value))
                        {
                            billingDbContext.RadiologyImagingRequisitions.Add(req);
                        }
                    }
                    billingDbContext.SaveChanges();
                    return imagingItemRequisition;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        #endregion
        #region creates the LabRequisition..
        private List<LabRequisitionModel> AddLabRequisition(BillingDbContext billingDbContext, List<LabRequisitionModel> labRequisition, int CurrentUserId)
        {
            try
            {
                List<LabRequisitionModel> labReqListFromClient = labRequisition;
                LabVendorsModel defaultVendor = billingDbContext.LabVendors.Where(val => val.IsDefault == true).FirstOrDefault();

                if (labReqListFromClient != null && labReqListFromClient.Count > 0)
                {
                    PatientDbContext patientContext = new PatientDbContext(connString);
                    List<LabTestModel> allLabTests = billingDbContext.LabTests.ToList();
                    int patId = labReqListFromClient[0].PatientId;
                    //get patient as querystring from client side rather than searching it from request's list.
                    PatientModel currPatient = patientContext.Patients.Where(p => p.PatientId == patId)
                        .FirstOrDefault<PatientModel>();

                    if (currPatient != null)
                    {

                        labReqListFromClient.ForEach(req =>
                        {
                            req.ResultingVendorId = defaultVendor.LabVendorId;
                            LabTestModel labTestdb = allLabTests.Where(a => a.LabTestId == req.LabTestId).FirstOrDefault<LabTestModel>();
                            //get PatientId from clientSide
                            if (labTestdb.IsValidForReporting == true)
                            {
                                req.CreatedOn = req.OrderDateTime = System.DateTime.Now;
                                req.ReportTemplateId = labTestdb.ReportTemplateId;
                                req.LabTestSpecimen = null;
                                req.LabTestSpecimenSource = null;
                                req.LabTestName = labTestdb.LabTestName;
                                req.RunNumberType = labTestdb.RunNumberType;
                                //req.OrderStatus = "active";
                                req.LOINC = "LOINC Code";
                                req.BillCancelledBy = null;
                                req.BillCancelledOn = null;
                                if (req.PrescriberId != null && req.PrescriberId != 0)
                                {
                                    var emp = billingDbContext.Employee.Where(a => a.EmployeeId == req.PrescriberId).FirstOrDefault();
                                    req.PrescriberName = emp.FullName;
                                }

                                //req.PatientVisitId = visitId;//assign above visitid to this requisition.
                                if (String.IsNullOrEmpty(currPatient.MiddleName))
                                    req.PatientName = currPatient.FirstName + " " + currPatient.LastName;
                                else
                                    req.PatientName = currPatient.FirstName + " " + currPatient.MiddleName + " " + currPatient.LastName;

                                req.OrderDateTime = DateTime.Now;
                                billingDbContext.LabRequisitions.Add(req);

								//billingDbContext.Database.Log = Console.WriteLine;

								billingDbContext.SaveChanges();
                            }
                        });

                    }
                    return labReqListFromClient;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }


        }

        #endregion

        #region This method will update the Provisional Items
        private string UpdateProvisionalBillingTxnItems(string ipDataString, RbacUser currentUser)
        {
            List<BillingTransactionItemModel> txnItems = DanpheJSONConvert.DeserializeObject<List<BillingTransactionItemModel>>(ipDataString);
            if (txnItems != null)
            {
                BillingTransactionBL.UpdateProvisionalBillingTransactionItems(_billingDbContext, txnItems, currentUser);
                return "Provisional items updated successfully.";
            }
            else
            {
                return "Provisional items Not found to update.";
            }
        } 
        #endregion
    }


    internal class EmployeeBasicDetail
    {
        public int? EmployeeId { get; set; }
        public string EmployeeName { get; set; }

    }
}
