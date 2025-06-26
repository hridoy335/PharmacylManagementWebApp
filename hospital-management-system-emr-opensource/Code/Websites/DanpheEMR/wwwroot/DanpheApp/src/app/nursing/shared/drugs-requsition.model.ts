import { PHRMPatient } from "../../pharmacy/shared/phrm-patient.model";
import { DrugsRequistionItemModel } from './drugs-requistion-items.model';
export class DrugsRequisitonModel {

    public RequisitionId: number = 0;
    public VisitId: number = 0;
    public PatientId: number = 0;
    public ReferenceId: string = null;
    public CreatedBy: number = 0;
    public CreateOn: string = "";

    public ItemName: string = "";
    public RequisitionItems: Array<DrugsRequistionItemModel> = new Array<DrugsRequistionItemModel>();

    //only for read purpose
    public selectedPatient: PHRMPatient = new PHRMPatient();
    //public selectedPatient: Patient = new Patient();
    //only for show in list
    public PatientName: string = "";

    public Frequency: number = 0;

    public HowManyDays: number = 0;

    public Notes: string = "";
    public PrescriberId: string = "";
    public OrderStatus: string = "";

    public Dosage: number = 0;

    public GenericId: number = 0;

}