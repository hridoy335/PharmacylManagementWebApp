import { FormBuilder, FormGroup, Validators } from "@angular/forms";

export class PhrmLeafSettingModel {

    public LeafSettingId: number = 0;
    public TotalNumber: number = 0;
    public LeafType: string = null;
    public IsActive: boolean = true;
    public CreatedBy: number = 0;
    public CreatedOn: string = null;
    public LeafSetting: FormGroup = null;
    constructor() {
        var _formBuilder = new FormBuilder();
        this.LeafSetting = _formBuilder.group({
            'LeafType': ['', Validators.compose([Validators.required, Validators.maxLength(5)])],
            'TotalNumber': ['', Validators.compose([Validators.required, Validators.maxLength(6)])],
        });
    }

    public IsDirty(fieldName): boolean {
        if (fieldName == undefined)
            return this.LeafSetting.dirty;
        else
            return this.LeafSetting.controls[fieldName].dirty;
    }

    public IsValid(): boolean {
        if (this.LeafSetting.valid) { return true; }
        else { return false; }
    }
    public IsValidCheck(fieldName, validator): boolean {
        if (fieldName == undefined) {
            return this.LeafSetting.valid;
        }
        else
            return !(this.LeafSetting.hasError(validator, fieldName));
    }

    // public IsValidCheck(fieldName: string, validator: string): boolean {
    //     const control = this.LeafSetting.get(fieldName);
    //     if (!control) return false;
    //     return !control.hasError(validator);
    // }
}


