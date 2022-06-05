import { Component, Input, Output, EventEmitter } from '@angular/core';
import { Router } from '@angular/router';


@Component({
  selector: 'app-side-menu-bar',
  templateUrl: './app-side-menu-bar.component.html',
  styleUrls: ['./app-side-menu-bar.component.scss']
})
export class AppSideMenuBarComponent {

  /* Value of the state of the side-menu. */
  @Input()
  pinned = false;

  /* Emits the 'pinned' boolean to other components when the pinned value changes. */
  @Output()
  pinnedChange: EventEmitter<boolean> = new EventEmitter();

  constructor(
    private router: Router
  ) { }

  /*
    Checks the current route of the browser and returns css style to show the selected item in the menu bar.

    @param buttonRoute: string
    Contains route of the button. Ex: products/add. Returns css styling when on this route
  */
  isOnRoute(buttonRoute: string): string {
    return this.router.url.endsWith(buttonRoute) ? 'background:rgba(156,39,176,.15); color:#d176e1;' : '';
  }

  /*
    Navigates to the correct webpage when a navigation button is clicked.

    @param route: string
    Contains the location where the browser has to navigate to. Ex: home
  */
  onClickNavigate(route: string): void {
    switch (route) {
      case 'home':
        this.router.navigate(['home']);
        break;
    }
  }

  /*
    Reverses the pin state of the menu and emit this value.
  */
  onClickPinMenu(): void {
    this.pinned = !this.pinned;
    this.pinnedChange.emit(this.pinned);
  }
}