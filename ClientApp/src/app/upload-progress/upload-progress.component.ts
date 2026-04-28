import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-upload-progress',
  standalone: true,
  imports: [],
  templateUrl: './upload-progress.component.html',
  styleUrl: './upload-progress.component.css'
})
export class UploadProgressComponent implements OnInit {

  constructor(private router: Router) {}

  ngOnInit() {
    // Simulate upload progress and auto-redirect for mockup purposes
    setTimeout(() => {
      this.finishUpload();
    }, 3000);
  }

  finishUpload() {
    this.router.navigate(['/feed']);
  }
}
