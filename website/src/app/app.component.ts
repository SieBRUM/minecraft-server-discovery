import { Component } from '@angular/core';
import { MatDrawerMode } from '@angular/material/sidenav';
import { Router } from '@angular/router';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent {

  /* Contains the value whether the menu is opened yes or no */
  isMenuOpen = true;
  /*
    Contains the value whether the menu is pinned yes or no.
    Value is received from the EventEmitter in the SideMenu component.
  */
  isMenuPinned = false;

  constructor(
    private router: Router
  ) {
    this.initMenuBar();
  }

  /*
    Opens or closes the side bar.
  */
  onClickSideBar(): void {
    this.isMenuOpen = !this.isMenuOpen;
  }

  /*
    Sets the correct menu state based on the pinned state.
  */
  getMenuMode(): MatDrawerMode {
    return this.isMenuPinned ? 'side' : 'over';
  }

  /*
    Handles the changes when the pinned state changes.
    Write the new value to localstorage and force-open the menu.
    @param pinned: boolean
    The new value of the pinned state
  */
  onPinnedChange(pinned: boolean): void {
    if (this.isMenuPinned === pinned) {
      return;
    }

    this.isMenuPinned = pinned;
    this.isMenuOpen = true;
    localStorage.setItem('menu-pinned', this.isMenuPinned.toString());
  }

  /*
    Initialise the sidebar state.
    Check if the localstorage contains previously saved menu values and use those.
    If non-existing values are stored in the localstorage, use the default values and save to localstorage.
  */
  initMenuBar(): void {
    const localStoragePinMenu = localStorage.getItem('menu-pinned');
    if (localStoragePinMenu === 'true') {
      this.isMenuPinned = true;
      this.isMenuOpen = true;
      return;
    }

    if (localStoragePinMenu === 'false') {
      this.isMenuPinned = false;
      this.isMenuOpen = false;
      return;
    }

    localStorage.setItem('menu-pinned', this.isMenuPinned.toString());
  }
}