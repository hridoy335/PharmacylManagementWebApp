import { Component } from '@angular/core';
import { CoreService } from '../../core/shared/core.service';
import { SecurityService } from '../../security/shared/security.service';
import { MessageboxService } from '../../shared/messagebox/messagebox.service';
import { OperationTheatreBookingModel } from '../shared/ot-booking.model';
import { OTGridColumnSettings } from '../shared/ot-grid-column-settings';
import { OperationTheatreBLService } from '../shared/ot.bl.service';

@Component({
    selector: 'ot-booking-list',
    templateUrl: './ot-booking-list.html',
    host: { '(window:keyup)': 'hotkeys($event)' }
})

export class OtBookingListComponent {

    public showAddPage: boolean = false;
    public selectedOtPatientData: OperationTheatreBookingModel = new OperationTheatreBookingModel();
    public OTBookingList: Array<any> = null;
    public otBookingGridColumns: OTGridColumnSettings = null;
    public otData: any;
    public editMode: boolean = false;

    constructor(public otBlService: OperationTheatreBLService,
        public msgBoxServ: MessageboxService,
        public coreService: CoreService,
        public securityService: SecurityService
    ) {
        this.otBookingGridColumns = new OTGridColumnSettings();
        this.Load()
        this.OTBookingList = this.otBookingGridColumns.OTBookingList;
    }
    public AddNewOtBooking() {
        this.editMode = false;
        this.showAddPage = true;
    }

    GetBackFromOtBookingAdd(event) {
        this.showAddPage = false;
        this.selectedOtPatientData = null;
        this.Load();
    }

    public Load() {
        this.otBlService.GetAllOTBookingDetails().subscribe(res => {
            console.log(res.Results);
            if (res.Status = "OK") {
                this.otData = res.Results;
            }
        })
    }


    public patientListFormatter(data: any): string {
        let html = data["ShortName"] + ' [ ' + data['PatientCode'] + ' ]' + ' - ' + data['Age'] + ' - ' + ' ' + data['Gender'] + ' - ' + ' ' + data['PhoneNumber'];
        return html;
    }

    OTBookingListGidActions(data) {
        console.log(data);
        debugger;
        switch (data.Action) {
            case "edit": {
                this.selectedOtPatientData = data.Data;
                this.editMode = true;
                this.showAddPage = true;
                break;
            }
            case "confirm":
                {
                    this.selectedOtPatientData = data.Data;
                    this.editMode = true;
                    this.showAddPage = true;
                    break;
                }
            case "reschedule":
                {
                    break;
                }
            case "cancel":
                {
                    break;
                }
        }
    }
    hotkeys(event) {
        if (event.altKey && event.keyCode == 78) {
            this.AddNewOtBooking()
        }
    }
}