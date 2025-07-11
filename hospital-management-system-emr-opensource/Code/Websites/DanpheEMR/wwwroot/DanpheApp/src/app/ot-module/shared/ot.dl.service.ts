import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable()
export class OperationTheatreDLService {
  public http: HttpClient;
  public options = {
    headers: new HttpHeaders({ 'Content-Type': 'application/x-www-form-urlencoded' })
  };
  constructor(public _http: HttpClient) {
    this.http = _http;
  }

  public GetEmployeeList() {
    return this.http.get<any>("/api/EmployeeSettings/Employees");
  }

  public GetIcdList() {
    // return this.http.get<any>("/api/Admission?reqType=get-icd10-list");
    return this.http.get<any>("/api/Admission/ICD10");
  }

  public GetAllOTBookingDetails() {
    return this.http.get<any>("/api/OperationTheatre/BookingInfo")
  }

  public PostNewBookingDetails(d) {
    let data = JSON.stringify(d);
    return this.http.post<any>("/api/OperationTheatre/BookOperationTheatre", data, this.options);
  }

  public PutBookingDetails(data) {
    let strData = JSON.stringify(data);
    return this.http.put<any>("/api/OperationTheatre/BookingInfo", strData, this.options);
  }
}