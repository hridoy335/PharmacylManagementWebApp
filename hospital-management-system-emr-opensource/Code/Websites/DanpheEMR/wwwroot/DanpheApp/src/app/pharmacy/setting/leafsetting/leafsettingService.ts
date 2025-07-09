
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PhrmLeafSettingModel } from '../../shared/phrm-leafsetting';
import { LeadSettingEndPoint } from './leafsetting.endpoint';
@Injectable()
export class LeafSettingService {

    /**
     * @description eagerly loaded dispensary list -- sanjit 2Aug'21
     */
    private leafsettingList: PhrmLeafSettingModel[] = [];
    constructor(public leafsettingendpoint: LeadSettingEndPoint) { }

    //sanjit: 14 May'20, to implement authorization in Dispensary  Module.
    private _activeLeafSetting: PhrmLeafSettingModel = new PhrmLeafSettingModel();
    public get activeLeafSetting(): PhrmLeafSettingModel {
        return this._activeLeafSetting;
    }
    public set activeLeafSetting(leafsetting: PhrmLeafSettingModel) {
        this._activeLeafSetting = leafsetting;
    }



    GetAllLeafSettingList() {
        if (this.leafsettingList != null && this.leafsettingList.length > 0)
            return Observable.of({ Status: "OK", Results: this.leafsettingList });
        else
            return this.leafsettingendpoint.GetAllLeafSettingList()
                .map(res => res)
                .do(res => {
                    if (res.Status == "OK")
                        this.leafsettingList = res.Results
                    return res;

                });
    }

}

