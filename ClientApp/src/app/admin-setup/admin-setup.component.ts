import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-setup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-setup.component.html',
  styleUrl: './admin-setup.component.css'
})
export class AdminSetupComponent {
  eventName = '';
  isLoading = false;

  constructor(private apiService: ApiService, private router: Router) {
    if (localStorage.getItem('admin_event_id')) {
      this.router.navigate(['/admin']);
    }
  }

  createEvent() {
    if (!this.eventName) return;
    this.isLoading = true;
    this.apiService.createEvent(this.eventName).subscribe({
      next: (res) => {
        localStorage.setItem('admin_event_id', res.id);
        localStorage.setItem('admin_event_slug', res.slug);
        localStorage.setItem('admin_event_name', res.name);
        this.router.navigate(['/admin']);
      },
      error: (err) => {
        console.error(err);
        this.isLoading = false;
      }
    });
  }
}
