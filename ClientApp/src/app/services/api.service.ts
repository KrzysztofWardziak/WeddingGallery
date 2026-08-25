import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  private getHeaders() {
    const token = localStorage.getItem('admin_token');
    let headers = new HttpHeaders();
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }
    return headers;
  }

  login(password: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/Admin/login`, { password });
  }

  createEvent(name: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/Admin/events`, { name }, { headers: this.getHeaders() });
  }

  getEvent(slug: string): Observable<any> {
    return this.http.get(`${this.baseUrl}/events/${slug}`);
  }

  uploadPhotos(eventId: string, uploaderName: string, files: File[]): Observable<any> {
    const formData = new FormData();
    formData.append('eventId', eventId);
    formData.append('uploaderName', uploaderName);
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }
    return this.http.post(`${this.baseUrl}/photos/upload`, formData);
  }

  getPhotos(eventId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/Photos/event/${eventId}`);
  }

  deletePhoto(photoId: string): Observable<any> {
    return this.http.delete(`${this.baseUrl}/Admin/photos/${photoId}`, { headers: this.getHeaders() });
  }
}
