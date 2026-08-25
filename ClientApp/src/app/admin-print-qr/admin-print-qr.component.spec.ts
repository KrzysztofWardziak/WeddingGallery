import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminPrintQrComponent } from './admin-print-qr.component';

describe('AdminPrintQrComponent', () => {
  let component: AdminPrintQrComponent;
  let fixture: ComponentFixture<AdminPrintQrComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminPrintQrComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(AdminPrintQrComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
