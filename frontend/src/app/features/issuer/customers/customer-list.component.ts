import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CustomerService, Customer } from './customer.service';
import { CatalogService } from '../../switch/catalog.service';
import { NotificationService } from '../../../core/notification.service';
import { catchError, debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { of, Subject } from 'rxjs';

@Component({
  selector: 'app-customer-list',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page-container">
      <div class="page-header d-flex justify-content-between align-items-center mb-4">
        <div>
          <h1 class="page-title">Gestión de Clientes</h1>
          <p class="text-muted mt-1">Busque, edite y cree los perfiles de los portahabientes.</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline" (click)="loadCustomers()">
            <span class="material-symbols-rounded">refresh</span>
            Refrescar
          </button>
          <button class="btn btn-primary" (click)="openCreateModal()">
            <span class="material-symbols-rounded">person_add</span>
            Crear Cliente
          </button>
        </div>
      </div>

      <!-- Filters & Search -->
      <div class="filters-card card mb-4">
        <div class="search-box">
          <span class="material-symbols-rounded search-icon">search</span>
          <input type="text" class="search-input" placeholder="Buscar por Nombre, DNI/RUC o Correo..." (input)="onSearch($event)">
        </div>
      </div>

      <!-- Customers Table -->
      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>CLIENTE</th>
                <th>DOCUMENTO (DNI/RUC)</th>
                <th>CONTACTO</th>
                <th>FECHA CREACIÓN</th>
                <th class="text-right">ACCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let c of filteredCustomers">
                <td>
                  <div class="user-block">
                    <div class="user-avatar">{{c.fullName.charAt(0) | uppercase}}</div>
                    <div class="user-details">
                      <span class="user-name">{{c.fullName}}</span>
                      <span class="user-email text-muted">ID: {{c.customerNumber}}</span>
                    </div>
                  </div>
                </td>
                <td class="font-weight-500">{{c.documentId}}</td>
                <td>
                  <div class="d-flex flex-column">
                    <span>{{c.email}}</span>
                    <span class="text-muted text-sm">{{c.phone}}</span>
                  </div>
                </td>
                <td class="text-muted">{{ (c.createdOn | date:'medium') || 'Hace un momento' }}</td>
                <td class="text-right">
                  <div class="action-buttons">
                    <button class="btn btn-outline btn-sm me-2" (click)="view360(c.id)">
                      <span class="material-symbols-rounded" style="font-size: 16px;">visibility</span> Vista 360
                    </button>
                  </div>
                </td>
              </tr>
              <tr *ngIf="filteredCustomers.length === 0">
                <td colspan="5" class="text-center py-5 text-muted">
                  <span class="material-symbols-rounded" style="font-size: 48px; opacity: 0.5;">person_search</span>
                  <p class="mt-2">No se encontraron clientes.</p>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Create Modal -->
      <div class="modal-overlay" [class.show]="isModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Alta de Nuevo Cliente</h3>
            <button class="icon-btn" (click)="closeCreateModal()">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body" style="max-height: 60vh; overflow-y: auto;">
            <div class="input-group">
              <label>Nombre Completo / Razón Social</label>
              <input type="text" class="form-control" placeholder="Ej. Juan Pérez" #fullName>
            </div>
            
            <div class="d-flex gap-3">
              <div class="input-group w-50">
                <label>Tipo Documento</label>
                <select class="form-control" #docType>
                  <option *ngFor="let dt of docTypes" [value]="dt">{{dt}}</option>
                </select>
              </div>
              <div class="input-group w-50">
                <label>Documento de Identidad</label>
                <input type="text" class="form-control" placeholder="0928312XXX" #docId>
              </div>
            </div>

            <div class="d-flex gap-3">
              <div class="input-group w-100">
                <label>Correo Electrónico</label>
                <input type="email" class="form-control" placeholder="juan@mail.com" #email>
              </div>
              <div class="input-group w-100">
                <label>Teléfono</label>
                <input type="text" class="form-control" placeholder="+xx xxxxxxxx" #phone>
              </div>
            </div>
            
            <div class="d-flex gap-3">
              <div class="input-group w-50">
                <label>Sexo</label>
                <select class="form-control" #gender>
                  <option *ngFor="let g of genders" [value]="g">{{g}}</option>
                </select>
              </div>
              <div class="input-group w-50">
                <label>Ciudad de Residencia</label>
                <select class="form-control" #resCity>
                  <option *ngFor="let c of cities" [value]="c">{{c}}</option>
                </select>
              </div>
            </div>
            
            <div class="input-group">
              <label>Dirección de Facturación</label>
              <input type="text" class="form-control" placeholder="Ej. Av. Francisco de Orellana" #billAddr>
            </div>
            
            <div class="d-flex gap-3">
              <div class="input-group w-50">
                <label>Dirección Estado de Cuenta</label>
                <input type="text" class="form-control" placeholder="Dirección..." #stmtAddr>
              </div>
              <div class="input-group w-50">
                <label>Ciudad Edo. Cuenta</label>
                <select class="form-control" #stmtCity>
                  <option *ngFor="let c of cities" [value]="c">{{c}}</option>
                </select>
              </div>
            </div>
            
            <div class="input-group">
              <label>Ciudad Reposición de Plástico</label>
              <select class="form-control" #cardCity>
                <option *ngFor="let c of cities" [value]="c">{{c}}</option>
              </select>
            </div>
          </div>
          <div class="modal-footer d-flex justify-content-between">
            <button class="btn btn-outline" (click)="closeCreateModal()">Cancelar</button>
            <button class="btn btn-primary" (click)="saveCustomer(fullName.value, docId.value, email.value, phone.value, docType.value, gender.value, billAddr.value, stmtAddr.value, resCity.value, stmtCity.value, cardCity.value)">Guardar</button>
          </div>
        </div>
      </div>

    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .filters-card { padding: 1rem 1.5rem; }
    .search-box { display: flex; align-items: center; background-color: var(--bg-main); border-radius: var(--radius-md); padding: 0.75rem 1rem; width: 100%; max-width: 500px;}
    .search-icon { color: var(--text-muted); }
    .search-input { border: none; background: transparent; outline: none; width: 100%; margin-left: 0.5rem; font-family: inherit; font-size: 0.875rem;}
    
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .table tbody tr:hover { background-color: #fafbfc; }
    
    .user-block { display: flex; align-items: center; gap: 1rem; }
    .user-avatar { width: 40px; height: 40px; border-radius: var(--radius-md); background-color: #f3f4f6; color: var(--primary); display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 1.25rem;}
    .user-details { display: flex; flex-direction: column; }
    .user-name { font-weight: 600; color: var(--text-main); }
    .text-sm { font-size: 0.75rem; }

    .action-buttons { display: flex; justify-content: flex-end; }
    .btn-sm { padding: 0.35rem 0.75rem; font-size: 0.75rem; display: inline-flex; align-items: center; gap: 0.25rem;}
    
    /* Modal */
    .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); display: none; align-items: center; justify-content: center; z-index: 1000; backdrop-filter: blur(2px);}
    .modal-overlay.show { display: flex; }
    .modal-card { width: 100%; max-width: 600px; padding: 0; animation: scaleUp 0.3s ease; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-header h3 { margin: 0; font-size: 1.25rem; }
    .modal-body { padding: 1.5rem; }
    .modal-footer { padding: 1rem 1.5rem; border-top: 1px solid var(--border-color); background: var(--bg-main); border-radius: 0 0 var(--radius-lg) var(--radius-lg); }
    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0.25rem; }
    .icon-btn:hover { color: var(--danger); }

    @keyframes scaleUp { from { transform: scale(0.95); opacity: 0; } to { transform: scale(1); opacity: 1; } }
  `]
})
export class CustomerListComponent {
  private customerService = inject(CustomerService);
  private catalogService = inject(CatalogService);
  private notifications = inject(NotificationService);
  private router = inject(Router);

  customers: Customer[] = [];
  filteredCustomers: Customer[] = [];
  isModalOpen = false;
  private searchSubject = new Subject<string>();

  docTypes: string[] = ['CEDULA'];
  genders: string[] = ['N/A'];
  cities: string[] = ['N/A'];

  constructor() {
    this.loadCustomers();
    this.loadCatalogs();
    this.searchSubject.pipe(
      debounceTime(400),
      distinctUntilChanged()
    ).subscribe(term => this.performSearch(term));
  }

  loadCatalogs() {
    this.catalogService.getDocumentTypes().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los tipos de documento');
        return of([]);
      })
    ).subscribe(d => this.docTypes = d);

    this.catalogService.getGenders().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar los catálogos de género');
        return of([]);
      })
    ).subscribe(d => this.genders = d);

    this.catalogService.getCities().pipe(
      catchError(() => {
        this.notifications.error('No se pudieron cargar las ciudades');
        return of([]);
      })
    ).subscribe(d => this.cities = d);
  }

  loadCustomers() {
    this.customerService.getCustomers().pipe(
      catchError(err => {
        this.notifications.error('Error al cargar la lista de clientes');
        console.error('Error loading customers:', err?.status, err?.message);
        return of([] as Customer[]);
      })
    ).subscribe(data => {
      this.customers = data;
      this.filteredCustomers = data;
    });
  }

  onSearch(event: any) {
    this.searchSubject.next(event.target.value);
  }

  performSearch(term: string) {
    const t = term.toLowerCase();
    this.filteredCustomers = this.customers.filter(c =>
      c.fullName.toLowerCase().includes(t) ||
      c.documentId.toLowerCase().includes(t) ||
      c.email.toLowerCase().includes(t)
    );
  }

  view360(id: string) {
    this.router.navigate(['/app/issuer/customers', id]);
  }

  openCreateModal() { this.isModalOpen = true; }
  closeCreateModal() { this.isModalOpen = false; }

  saveCustomer(fullName: string, documentId: string, email: string, phone: string, documentType: string, gender: string, billingAddress: string, statementAddress: string, residenceCity: string, statementCity: string, cardDeliveryCity: string) {
    const payload = { fullName, documentId, email, phone, documentType, gender, billingAddress, statementAddress, residenceCity, statementCity, cardDeliveryCity };
    this.customerService.createCustomer(payload).pipe(
      catchError(err => {
        console.error('Error creating customer:', err?.status, err?.message);
        this.notifications.error('No se pudo crear el cliente. Verifique la conexión con el servidor.');
        return of(null);
      })
    ).subscribe(res => {
      if (res) {
        this.customers.unshift(res);
        this.filteredCustomers = [...this.customers];
        this.closeCreateModal();
      }
    });
  }
}
