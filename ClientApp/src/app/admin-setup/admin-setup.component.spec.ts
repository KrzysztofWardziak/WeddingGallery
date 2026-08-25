import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminSetupComponent } from './admin-setup.component';

describe('AdminSetupComponent', () => {
  let component: AdminSetupComponent;
  let fixture: ComponentFixture<AdminSetupComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSetupComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(AdminSetupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
