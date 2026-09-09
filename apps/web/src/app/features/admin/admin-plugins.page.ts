import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ui } from '../../core/i18n/strings';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-plugins',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin-plugins.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminPluginsPage {
  readonly areaId: AdminAreaId = 'plugins';
  readonly ui = ui;
}
