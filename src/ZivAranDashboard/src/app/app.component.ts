import { Component } from '@angular/core';
import { AranDashboardComponent } from './aran-dashboard.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [AranDashboardComponent],
  template: '<app-aran-dashboard></app-aran-dashboard>',
})
export class AppComponent {}
