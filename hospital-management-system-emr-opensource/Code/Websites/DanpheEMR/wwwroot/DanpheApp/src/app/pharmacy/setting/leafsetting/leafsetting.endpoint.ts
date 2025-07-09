import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable()
export class LeadSettingEndPoint {
    baseUrl: string;
    options = { headers: new HttpHeaders({ 'Content-Type': 'application/json' }) };

    constructor(public http: HttpClient) {
        //this.baseUrl = '/api/Dispensary/'; 
        this.baseUrl = '/api/PHRMLeafSetting/';
    }

    GetAllLeafSettingList() {
        return this.http.get<any>(`${this.baseUrl}GetAllLeafSetting`);
        //return this.http.get<any>(`${this.baseUrl}Dispensaries`);
    }
    //   GetDispensaryById(dispensaryId: number) {
    //     return this.http.get<any>(`${this.baseUrl}GetDispensary?dispensaryId=${dispensaryId}`)
    //   }
    //   AddDispensary(dispensary: PHRMStoreModel) {
    //     return this.http.post<any>(`${this.baseUrl}NewDispensary`, dispensary, this.options);
    //   }
    //   UpdateDispensary(dispensary: PHRMStoreModel) {
    //     return this.http.put<any>(`${this.baseUrl}PutDispensary`, dispensary, this.options);
    //   }
    //   ActivateDeactivateDispensary(dispensaryId: number) {
    //     return this.http.put<any>(`${this.baseUrl}ActivateDeactivate?dispensaryId=${dispensaryId}`, this.options);
    //   }
    //   ActivateDispensary(dispensaryId: number, dispensaryName: string) {
    //     return this.http.put<any>("/api/Security/ActivateDispensary?dispensaryId=" + dispensaryId + "&dispensaryName=" + dispensaryName, this.options);
    //   }
    //   getActiveDispensary() {
    //     return this.http.get<any>("/api/Security/ActiveDispensary", this.options);
    //   }
    //   DeactivateDispensary() {
    //     return this.http.put<any>("/api/Security/DeactivateDispensary", this.options);
    //   }
}
