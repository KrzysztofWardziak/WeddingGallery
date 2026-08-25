import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-login.component.html',
  styleUrl: './admin-login.component.css'
})
export class AdminLoginComponent {
  password = '';
  error = '';
  isLoading = false;

  constructor(private apiService: ApiService, private router: Router) {}

  login() {
    this.error = '';
    this.isLoading = true;
    this.apiService.login(this.password).subscribe({
      next: (res: any) => {
        localStorage.setItem('admin_token', res.token);
        this.isLoading = false;

        // Always the event list: it is the only screen that works whether the admin has
        // zero, one, or a dozen events.
        this.router.navigate(['/admin/events']);
      },
      error: () => {
        this.error = 'Nieprawidłowe hasło';
        this.isLoading = false;
      }
    });
  }
}
