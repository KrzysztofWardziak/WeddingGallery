import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { QRCodeModule } from 'angularx-qrcode';
import { ApiService } from '../services/api.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, QRCodeModule, RouterLink],
  templateUrl: './admin-dashboard.component.html',
  styleUrl: './admin-dashboard.component.css'
})
export class AdminDashboardComponent implements OnInit {
  eventId = '';
  eventSlug = '';
  eventUrl = '';
  photos: any[] = [];
  imagesUrl = environment.imagesUrl;
  apiUrl = environment.apiUrl;

  constructor(private apiService: ApiService, private router: Router) {}

  ngOnInit() {
    this.eventId = localStorage.getItem('admin_event_id') || '';
    this.eventSlug = localStorage.getItem('admin_event_slug') || '';
    
    if (!this.eventId) {
      this.router.navigate(['/admin/setup']);
      return;
    }

    this.eventUrl = `${window.location.origin}/${this.eventSlug}`;
    this.loadPhotos();
  }

  loadPhotos() {
    this.apiService.getPhotos(this.eventId).subscribe({
      next: (data) => this.photos = data,
      error: (err) => console.error(err)
    });
  }

  deletePhoto(id: string) {
    if (confirm('Czy na pewno chcesz usunąć to zdjęcie?')) {
      this.apiService.deletePhoto(id).subscribe({
        next: () => this.loadPhotos(),
        error: (err) => console.error(err)
      });
    }
  }

  downloadAll() {
    window.location.href = `${this.apiUrl}/Photos/event/${this.eventId}/download`;
  }

  logout() {
    localStorage.removeItem('admin_event_id');
    localStorage.removeItem('admin_event_slug');
    localStorage.removeItem('admin_token');
    this.router.navigate(['/admin/login']);
  }
}
