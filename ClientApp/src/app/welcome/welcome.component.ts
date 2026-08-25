import { Component, OnInit } from '@angular/core';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../services/api.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-welcome',
  standalone: true,
  imports: [RouterLink, CommonModule],
  templateUrl: './welcome.component.html',
  styleUrl: './welcome.component.css'
})
export class WelcomeComponent implements OnInit {
  eventName = '';
  isLoading = true;
  hasError = false;

  constructor(private route: ActivatedRoute, private apiService: ApiService, private router: Router) {}

  ngOnInit() {
    const slug = this.route.snapshot.paramMap.get('slug');
    if (slug) {
      this.apiService.getEvent(slug).subscribe({
        next: (event) => {
          this.eventName = event.name;
          localStorage.setItem('guest_event_id', event.id);
          this.isLoading = false;
        },
        error: () => {
          this.hasError = true;
          this.isLoading = false;
        }
      });
    } else {
      this.hasError = true;
      this.isLoading = false;
    }
  }
}
