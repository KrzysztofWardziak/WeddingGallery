import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AdminEvent, ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-events',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './admin-events.component.html',
  styleUrl: './admin-events.component.css'
})
export class AdminEventsComponent implements OnInit {
  events: AdminEvent[] = [];
  isLoading = true;
  error = '';

  constructor(private apiService: ApiService, private router: Router) {}

  ngOnInit() {
    this.loadEvents();
  }

  loadEvents() {
    this.isLoading = true;
    this.error = '';
    this.apiService.getAdminEvents().subscribe({
      next: (events) => {
        this.events = events;
        this.isLoading = false;
      },
      error: (err) => {
        console.error(err);
        this.error = 'Nie udało się wczytać listy wydarzeń.';
        this.isLoading = false;
      }
    });
  }

  totalCount(event: AdminEvent): number {
    return event.photoCount + event.videoCount;
  }

  logout() {
    localStorage.removeItem('admin_token');
    this.router.navigate(['/admin/login']);
  }
}
