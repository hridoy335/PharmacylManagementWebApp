import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { GridEmitModel } from "../../../shared/danphe-grid/grid-emit.model";
import PHRMGridColumns from '../../shared/phrm-grid-columns';
import { PhrmLeafSettingModel } from '../../shared/phrm-leafsetting';
import { LeafSettingService } from './leafsettingService';

@Component({
  selector: 'app-leafsetting',
  templateUrl: './leafsetting.component.html',
  styleUrls: ['./leafsetting.component.css']
})


export class LeafsettingComponent implements OnInit {
  public CurrentLeafsetting: PhrmLeafSettingModel = new PhrmLeafSettingModel();
  public leafsettingList: Array<PhrmLeafSettingModel> = new Array<PhrmLeafSettingModel>();
  public showLeafsettingList: boolean = true;
  public globalListenFunc: Function;
  public showLeafSettingAddPage: boolean = false;
  public update: boolean = false;
  public index: number;
  renderer2: any;
  ESCAPE_KEYCODE: any;
  leafsettingGridColumns: ({ headerName: string; field: string; width: number; cellStyle?: undefined; cellRenderer?: undefined; } | { headerName: string; field: string; width: number; cellStyle: (params: any) => { 'text-transform': string; }; cellRenderer?: undefined; } | { headerName: string; field: string; width: number; cellRenderer: (params: any) => string; cellStyle?: undefined; })[];

  constructor(public changeDetector: ChangeDetectorRef,
    public leafsettingService: LeafSettingService,
  ) {

    this.getLeafsettingList();

    this.leafsettingGridColumns = PHRMGridColumns.PHRMLeafSettingList;
    // this.GeneralFieldLabel = coreservice.GetFieldLabelParameter();
  }

  ngOnInit() {
    // this.globalListenFunc = this.renderer2.listen('document', 'keydown', e => {
    //   if (e.keyCode == this.ESCAPE_KEYCODE) {
    //     this.Close()
    //   }
    // });

  }

  AddDispensary() {
    this.showLeafSettingAddPage = false;
    this.changeDetector.detectChanges();
    this.showLeafSettingAddPage = true;
    this.setFocusById('leafSettingLeftType');
  }
  setFocusById(id: string) {
    var Timer = setTimeout(() => {
      if (document.getElementById(id)) {
        let nextEl = <HTMLInputElement>document.getElementById(id);
        nextEl.focus();
        clearTimeout(Timer);
      }
    }, 50)
  }
  AddLeafSetting() {
    this.showLeafSettingAddPage = false;
    this.changeDetector.detectChanges();
    this.showLeafSettingAddPage = true;
    this.setFocusById('leafSettingLeftType');
  }
  Close() {
    this.CurrentLeafsetting = new PhrmLeafSettingModel();
    this.update = false;
    this.showLeafSettingAddPage = false;
  }

  public getLeafsettingList() {
    this.leafsettingService.GetAllLeafSettingList()
      .subscribe(res => {
        if (res.Status == "OK") {
          this.leafsettingList = res.Results;
          this.leafsettingList = this.leafsettingList.slice();
          console.log(res);
        }
        else {
          alert("Failed ! " + res.ErrorMessage);
          console.log(res.ErrorMessage)
        }
      });
  }



  Add() {
    // for (var i in this.CurrentLeafSetting .StoreValidator.controls) {
    //   this.CurrentLeafSetting .StoreValidator.controls[i].markAsDirty();
    //   this.CurrentLeafSetting .StoreValidator.controls[i].updateValueAndValidity();
    // }
    // if (this.CurrentLeafSetting .StoreValidator.valid) {
    //   this.CurrentLeafSetting .CreatedBy = this.securityService.GetLoggedInUser().EmployeeId;
    //   this.CurrentLeafSetting .CreatedOn = moment().format('YYYY-MM-DD HH:mm:ss');
    //   this.CurrentLeafSetting .IsActive = true;
    //   this.dispensaryService.AddDispensary(this.CurrentLeafSetting )
    //     .subscribe(
    //       res => {
    //         if (res.Status == "OK") {
    //           this.msgBoxServ.showMessage("success", ["Dispensary Added."]);
    //           this.AddNewStorePermissionToClientSide(this.CurrentLeafSetting );
    //           this.CallBackAddUpdate(res)
    //           this.CurrentLeafSetting  = new PHRMStoreModel();
    //         }
    //         else {
    //           this.msgBoxServ.showMessage("error", ["Something Wrong" + res.ErrorMessage]);
    //         }
    //       },
    //       err => {
    //         this.msgBoxServ.showMessage("error", ["Something Wrong" + err.ErrorMessage]);
    //       });
    // }
  }

  Update() {
    // for (var i in this.CurrentLeafSetting .StoreValidator.controls) {
    //   this.CurrentLeafSetting .StoreValidator.controls[i].markAsDirty();
    //   this.CurrentLeafSetting .StoreValidator.controls[i].updateValueAndValidity();
    // }
    // if (this.CurrentLeafSetting .StoreValidator.valid) {
    //   this.CurrentLeafSetting .CreatedBy = this.securityService.GetLoggedInUser().EmployeeId;
    //   this.CurrentLeafSetting .CreatedOn = moment().format('YYYY-MM-DD HH:mm:ss');
    //   this.dispensaryService.UpdateDispensary(this.CurrentLeafSetting )
    //     .subscribe(
    //       res => {
    //         if (res.Status == "OK") {
    //           this.msgBoxServ.showMessage("success", ['Dispensary Details Updated.']);
    //           this.CallBackAddUpdate(res)
    //           this.CurrentLeafSetting  = new PHRMStoreModel();
    //         }
    //         else {
    //           this.msgBoxServ.showMessage("failed", ["Something Wrong " + res.ErrorMessage]);
    //         }
    //       },
    //       err => {
    //         this.msgBoxServ.showMessage("error", ["Something Wrong " + err.ErrorMessage]);
    //       });
    // }
  }

  leafsettingGridActions($event: GridEmitModel) {
    switch ($event.Action) {
      case "edit": {
        // this.selectedItem = null;
        this.update = true;
        // this.index = $event.RowIndex;
        // this.showDispensaryAddPage = false;
        // this.changeDetector.detectChanges();
        // this.selectedItem = $event.Data;
        // this.CurrentLeafsetting.StoreId = this.selectedItem.StoreId;
        // this.CurrentLeafsetting.Name = this.selectedItem.Name;

        this.showLeafSettingAddPage = true;
        break;
      }
      case "activateDeactivateIsActive": {
        if ($event.Data != null) {
          // this.selectedItem = null;
          // this.selectedItem = $event.Data;
          // this.ActivateDeactivateStatus(this.selectedItem);
          // this.selectedItem = null;
        }
        break;
      }
      case "showPaymentModes": {
        // this.selectedItem = $event.Data;
        // this.openPaymentModesPopUp();
        break;
      }
      default:
        break;
    }
  }




}




