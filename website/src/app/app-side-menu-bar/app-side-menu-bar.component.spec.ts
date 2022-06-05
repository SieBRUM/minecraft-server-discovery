import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AppSideMenuBarComponent } from './app-side-menu-bar.component';

describe('AppSideMenuBarComponent', () => {
  let component: AppSideMenuBarComponent;
  let fixture: ComponentFixture<AppSideMenuBarComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ AppSideMenuBarComponent ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AppSideMenuBarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
