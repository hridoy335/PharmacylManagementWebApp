import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { LeafsettingComponent } from './leafsetting.component';

describe('LeafsettingComponent', () => {
  let component: LeafsettingComponent;
  let fixture: ComponentFixture<LeafsettingComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ LeafsettingComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(LeafsettingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
