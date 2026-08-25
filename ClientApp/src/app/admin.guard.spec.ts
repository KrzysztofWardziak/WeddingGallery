import { TestBed } from '@angular/core/testing';

import { AdminGuard } from './admin.guard';

describe('AdminGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('should be created', () => {
    // The scaffold assumed a functional guard (adminGuard); this project uses the
    // class-based AdminGuard, and the mismatched import failed to compile, which
    // took the whole karma run down with it.
    expect(TestBed.inject(AdminGuard)).toBeTruthy();
  });
});
