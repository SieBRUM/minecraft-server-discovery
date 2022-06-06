import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs/internal/Observable';
import { environment } from '../environments/environment';
import { IServer } from './models/server.model';


@Injectable({
    providedIn: 'root'
})
export class ApiService {

    API_URL = environment.url;

    constructor(private http: HttpClient) { }

    getAllServers(): Observable<HttpResponse<Array<IServer>>> {
        return this.http.get<Array<IServer>>(`${this.API_URL}main/server`, { observe: 'response' });
    }

    updateServer(ip: string): Observable<HttpResponse<IServer>> {
        return this.http.get<IServer>(`${this.API_URL}main/submit/${ip}`, { observe: 'response' });
    }
}