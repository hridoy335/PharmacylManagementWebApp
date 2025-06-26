
import { ChangeDetectorRef, Component } from "@angular/core";
import { Router } from '@angular/router';
import { PharmacyBLService } from "../../pharmacy/shared/pharmacy.bl.service";
import { PharmacyService } from "../../pharmacy/shared/pharmacy.service";
import PHRMGridColumns from '../../pharmacy/shared/phrm-grid-columns';
import { PHRMInvoiceItemsModel } from "../../pharmacy/shared/phrm-invoice-items.model";
import { GridEmitModel } from "../../shared/danphe-grid/grid-emit.model";
import { MessageboxService } from "../../shared/messagebox/messagebox.service";
import { RouteFromService } from "../../shared/routefrom.service";

import { DrugsRequistionItemModel } from "../../nursing/shared/drugs-requistion-items.model";
import { Patient } from "../../patients/shared/patient.model";
import { PatientService } from "../../patients/shared/patient.service";
import { PatientsBLService } from "../../patients/shared/patients.bl.service";
import { NursingBLService } from "../shared/nursing.bl.service";

@Component({

    templateUrl: "../../view/nursing-view/RequisitionList.html" //"/NursingView/RequisitionList"
})
export class DrugRequestListComponent {
    patients: Array<Patient> = new Array<Patient>();
    patient: Patient = new Patient();

    public PHRMProvisionalItemsGridColumns: Array<any> = null;
    public PHRMProvisionalItemsList: any;
    public showProItemList: boolean = true;
    public showDispatchItem: boolean = false;
    public proItems: PHRMInvoiceItemsModel = new PHRMInvoiceItemsModel();
    public localDatalist: Array<DrugsRequistionItemModel> = new Array<DrugsRequistionItemModel>();
    public selectedDatalist: Array<DrugsRequistionItemModel> = new Array<DrugsRequistionItemModel>();
    public PHRMProItemsList: Array<DrugsRequistionItemModel> = new Array<DrugsRequistionItemModel>();
    public PHRMDispatchList: Array<PHRMInvoiceItemsModel> = new Array<PHRMInvoiceItemsModel>();

    // public medRouteList: Array<any> = ["mouth", "intravenous", "intramuscular", "inhalation", "vaginally", "eyes", "intravitreal injection"];

    public patientId: number;
    public invoiceItemId: any;
    constructor(public pharmacyBLService: PharmacyBLService,
        public nursingBlService: NursingBLService,
        public pharmacyService: PharmacyService,
        public msgBoxServ: MessageboxService,
        public patientService: PatientService,
        public patientBlService: PatientsBLService,
        public router: Router,
        public changeDetectorRef: ChangeDetectorRef,
        public routeFromService: RouteFromService) {
        this.PHRMProvisionalItemsGridColumns = PHRMGridColumns.NursingRequsitionList;
        this.LoadPHRMPOProvisionalItemsByStatus();
    }

    // Load drugs request list from nursing dept.
    LoadPHRMPOProvisionalItemsByStatus() {

        this.pharmacyBLService.GetAllPHRMProvisionalItemList()
            .subscribe(res => {
                if (res.Status == "OK") {
                    this.PHRMProvisionalItemsList = res.Results;
                    for (let i = 0; i < this.PHRMProvisionalItemsList.length; i++) {
                        this.patientId = this.PHRMProvisionalItemsList[i].PatientId;
                    }
                } else {
                    this.msgBoxServ.showMessage("failed", ['Failed to get OrderList.' + res.ErrorMessage]);
                }
            },
                err => {
                    this.msgBoxServ.showMessage("error", ['Failed to get OrderList.' + err.ErrorMessage]);
                }
            );
    }

    PHRMProvisionalItemsGridAction($event: GridEmitModel) {
        switch ($event.Action) {
            case "view":
                {
                    var data = $event.Data;
                    this.showDispatchItem = true;
                    this.ShowProvisionalItemsDetailsById(data.RequisitionId);
                    this.ShowDispatchedDrugList(data.RequisitionId);
                    //var data = $event.Data;
                    //this.SetPatServiceData(data);
                }
                break;
            default:
                break;
        }
    }

    CloseDrugsRequestsPage() {
        this.showDispatchItem = false;
    }

    // show the selected patients drugs item list
    ShowProvisionalItemsDetailsById(requsitionId) {
        debugger;
        var len = this.localDatalist.length;
        for (var i = 0; i < len; i++) {
            let selectedDataset = this.localDatalist[i];
            if (selectedDataset.ItemId == requsitionId) {
                this.selectedDatalist.push(selectedDataset);
            }
        }
        if (this.selectedDatalist[0] && requsitionId) {
            this.PHRMProItemsList = this.selectedDatalist;
            this.selectedDatalist = new Array<DrugsRequistionItemModel>();
        }
        else {
            (
                this.pharmacyBLService.GetPHRMDrugsItemList(requsitionId)
                    .subscribe(res => {
                        if (res.Status == "OK") {
                            this.PHRMProItemsList = res.Results;
                            this.PHRMProItemsList.forEach(itm => { this.localDatalist.push(itm); });

                        } else {
                            this.msgBoxServ.showMessage("failed", ['Failed to get List.' + res.ErrorMessage]);
                        }
                    },
                        err => {
                            this.msgBoxServ.showMessage("error", ['Failed to get List.' + err.ErrorMessage]);
                        }
                    )
            )
        }
    }

    // show dispatched drug list per requisition id
    ShowDispatchedDrugList(requsitionId) {
        debugger;
        this.pharmacyBLService.GetPHRMDrugsDispatchList(requsitionId)
            .subscribe(res => {
                if (res.Status == "OK") {
                    this.PHRMDispatchList = res.Results;
                    console.log(res);

                } else {
                    this.msgBoxServ.showMessage("failed", ['Failed to get List.' + res.ErrorMessage]);
                }
            },
                err => {
                    this.msgBoxServ.showMessage("error", ['Failed to get List.' + err.ErrorMessage]);
                }
            );

    }


    myItemListFormatter(data: any): string {
        let html = data["GenericName"] + "||" + data["ItemName"];
        return html;
    }

    SubmitDragsRequiest() {
        console.log("Yes");

    }
}