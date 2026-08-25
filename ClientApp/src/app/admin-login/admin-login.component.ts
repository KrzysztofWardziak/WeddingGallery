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
        
        // If they already created an event, go to dashboard, else go to setup
        if (localStorage.getItem('admin_event_id')) {
          this.router.navigate(['/admin']);
        } else {
          this.router.navigate(['/admin/setup']);
        }
      },
      error: () => {
        this.error = 'Nieprawidłowe hasło';
        this.isLoading = false;
      }
    });
  }
}
