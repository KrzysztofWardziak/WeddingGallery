import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

/// A chunked upload in flight. chunkSize is the server's advice on how much to send at once.
export interface UploadSession {
  uploadId: string;
  offset: number;
  chunkSize: number;
}

/// An event as the admin views it: identity plus how much media guests have contributed.
export interface AdminEvent {
  id: string;
  name: string;
  slug: string;
  photoCount: number;
  videoCount: number;
}

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

  getAdminEvents(): Observable<AdminEvent[]> {
    return this.http.get<AdminEvent[]>(`${this.baseUrl}/Admin/events`, { headers: this.getHeaders() });
  }

  // Irreversible. confirmName is verified server-side against the event's name, so this is
  // a real guard rather than a browser-only formality.
  deleteAdminEvent(eventId: string, confirmName: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/Admin/events/${eventId}?confirmName=${encodeURIComponent(confirmName)}`,
      { headers: this.getHeaders() });
  }

  getAdminEvent(eventId: string): Observable<AdminEvent> {
    return this.http.get<AdminEvent>(`${this.baseUrl}/Admin/events/${eventId}`, { headers: this.getHeaders() });
  }

  // One file per request: Cloudflare's free plan rejects requests over 100 MB, and a
  // single photo or short video stays under that on its own - batching them would not.
  // The API endpoint takes a list, so we send a list of one.
  uploadMedia(eventId: string, uploaderName: string, file: File): Observable<any> {
    const formData = new FormData();
    formData.append('eventId', eventId);
    formData.append('uploaderName', uploaderName);
    formData.append('files', file);
    return this.http.post(`${this.baseUrl}/photos/upload`, formData);
  }

  // Chunked upload, for files too large to cross Cloudflare in one request. Validation of
  // format and size happens here, before any bytes travel.
  startUpload(eventId: string, uploaderName: string, file: File): Observable<UploadSession> {
    return this.http.post<UploadSession>(`${this.baseUrl}/photos/uploads`, {
      eventId,
      uploaderName,
      fileName: file.name,
      totalSize: file.size
    });
  }

  // Asks the server how far the upload actually got. This is what makes a retry resume
  // rather than start over.
  getUploadOffset(uploadId: string): Observable<{ uploadId: string; offset: number; totalSize: number }> {
    return this.http.get<{ uploadId: string; offset: number; totalSize: number }>(
      `${this.baseUrl}/photos/uploads/${uploadId}`);
  }

  appendChunk(uploadId: string, offset: number, chunk: Blob): Observable<{ offset: number }> {
    return this.http.post<{ offset: number }>(
      `${this.baseUrl}/photos/uploads/${uploadId}/chunk?offset=${offset}`,
      chunk,
      { headers: { 'Content-Type': 'application/octet-stream' } });
  }

  completeUpload(uploadId: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/photos/uploads/${uploadId}/complete`, null);
  }

  abandonUpload(uploadId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/photos/uploads/${uploadId}`);
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
