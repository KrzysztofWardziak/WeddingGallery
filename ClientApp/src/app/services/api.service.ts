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

  // One file per request: Cloudflare's free plan rejects requests over 100 MB,
  // and a single photo never comes close. The API endpoint takes a list, so we
  // send a list of one.
  uploadPhoto(eventId: string, uploaderName: string, file: File): Observable<any> {
    const formData = new FormData();
    formData.append('eventId', eventId);
    formData.append('uploaderName', uploaderName);
    formData.append('files', file);
    return this.http.post(`${this.baseUrl}/photos/upload`, formData);
  }

  getPhotos(eventId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/Photos/event/${eventId}`);
  }

  deletePhoto(photoId: string): Observable<any> {
    return this.http.delete(`${this.baseUrl}/Admin/photos/${photoId}`, { headers: this.getHeaders() });
  }

  downloadEventZip(eventId: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/Photos/event/${eventId}/download`, {
      headers: this.getHeaders(),
      responseType: 'blob'
    });
  }
}
