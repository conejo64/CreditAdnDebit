import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { CatalogService, CatalogBin, CatalogProduct, CatalogCountry } from './catalog.service';
import { NotificationService } from '../../core/notification.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

@Component({
   selector: 'app-catalogs',
   standalone: true,
   imports: [CommonModule, ReactiveFormsModule],
   template: `
    <div class="page-container">
      <div class="page-header mb-4">
        <div>
          <h1 class="page-title d-flex align-items-center gap-2">
            <span class="material-symbols-rounded text-primary" style="font-size: 32px">list_alt</span>
            Catálogos y Parámetros Globales
          </h1>
          <p class="text-muted mt-1">Diccionarios del sistema centralizado de Bóveda (BINs, Productos y Mercados).</p>
        </div>
      </div>

      <div class="row">
         <!-- BINS -->
         <div class="col-lg-6 mb-4">
            <div class="card p-0 h-100">
               <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center bg-light">
                  <h3 class="m-0 d-flex align-items-center gap-2 text-main font-weight-600">
                     <span class="material-symbols-rounded text-primary">credit_card</span> Configuración de BINs
                  </h3>
                  <button class="btn btn-primary btn-sm" (click)="openBinModal()"><span class="material-symbols-rounded">add</span> Nuevo BIN</button>
               </div>
               <div class="table-responsive">
                  <table class="table">
                     <thead>
                        <tr>
                           <th>BIN/ICA</th>
                           <th>RED</th>
                           <th>TIPO</th>
                           <th>ESTADO</th>
                        </tr>
                     </thead>
                     <tbody>
                        <tr *ngFor="let bin of bins">
                           <td class="font-monospace text-primary-dark font-weight-600">{{bin.binStart}} - {{bin.binEnd}}</td>
                           <td>
                              <span class="badge" [ngClass]="getNetworkClass(bin.brand || '')">{{bin.brand}}</span>
                           </td>
                           <td class="text-muted text-sm">{{bin.product}} <br/><small>{{bin.issuerName}}</small></td>
                           <td>
                              <span class="badge" [ngClass]="bin.enabled ? 'badge-success' : 'badge-danger'">{{bin.enabled ? 'ACTIVO' : 'INACTIVO'}}</span>
                           </td>
                        </tr>
                     </tbody>
                  </table>
               </div>
            </div>
         </div>

         <!-- PRODUCTS -->
         <div class="col-lg-6 mb-4">
            <div class="card p-0 h-100">
               <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center bg-light">
                  <h3 class="m-0 d-flex align-items-center gap-2 text-main font-weight-600">
                     <span class="material-symbols-rounded text-secondary">inventory_2</span> Productos Bancarios
                  </h3>
                  <button class="btn btn-primary btn-sm" (click)="openProductModal()"><span class="material-symbols-rounded">add</span> Nuevo Prod</button>
               </div>
               <div class="table-responsive">
                  <table class="table">
                     <thead>
                        <tr>
                           <th>CÓDIGO</th>
                           <th>NOMBRE / TIPO</th>
                           <th>MARCA (BRAND)</th>
                        </tr>
                     </thead>
                     <tbody>
                        <tr *ngFor="let prod of products">
                           <td class="font-monospace font-weight-600">{{prod.code}}</td>
                           <td>
                              <div class="d-flex flex-column">
                                 <strong>{{prod.name}}</strong>
                                 <span class="text-muted text-sm d-flex align-items-center gap-1">
                                    <span class="material-symbols-rounded" style="font-size: 14px">category</span> {{prod.productType}}
                                 </span>
                              </div>
                           </td>
                           <td class="text-center font-weight-600 text-muted">{{prod.brand}}</td>
                        </tr>
                     </tbody>
                  </table>
               </div>
            </div>
         </div>
         <!-- COUNTRIES -->
         <div class="col-lg-12 mb-4">
            <div class="card p-0 h-100">
               <div class="card-header border-bottom p-3 d-flex justify-content-between align-items-center bg-light">
                  <h3 class="m-0 d-flex align-items-center gap-2 text-main font-weight-600">
                     <span class="material-symbols-rounded text-success">public</span> Mercados y Países
                  </h3>
                  <button class="btn btn-primary btn-sm" (click)="openCountryModal()"><span class="material-symbols-rounded">add</span> Nuevo País</button>
               </div>
               <div class="table-responsive">
                  <table class="table">
                     <thead>
                        <tr>
                           <th>ISO CODE</th>
                           <th>NOMBRE</th>
                           <th>NUMERIC</th>
                           <th>MONEDA</th>
                           <th>ESTADO</th>
                        </tr>
                     </thead>
                     <tbody>
                        <tr *ngFor="let c of countries">
                           <td class="font-monospace text-primary-dark font-weight-600">{{c.code}}</td>
                           <td>{{c.name}}</td>
                           <td class="text-muted">{{c.numericCode}}</td>
                           <td class="font-weight-600">{{c.currency}}</td>
                           <td>
                              <span class="badge" [ngClass]="c.enabled ? 'badge-success' : 'badge-danger'">{{c.enabled ? 'ACTIVO' : 'INACTIVO'}}</span>
                           </td>
                        </tr>
                     </tbody>
                  </table>
               </div>
            </div>
         </div>
      </div>

      <!-- MODAL BIN -->
      <div *ngIf="showBinModal" class="custom-modal-backdrop">
        <div class="custom-modal">
          <div class="custom-modal-header">
            <h3>Nuevo Rango BIN</h3>
            <button class="btn-close" (click)="closeBinModal()">&times;</button>
          </div>
          <div class="custom-modal-body">
            <form [formGroup]="binForm" (ngSubmit)="saveBin()">
              <div class="form-group mb-3">
                <label>BIN Inicio</label>
                <input type="number" class="form-control" formControlName="binStart">
              </div>
              <div class="form-group mb-3">
                <label>BIN Fin</label>
                <input type="number" class="form-control" formControlName="binEnd">
              </div>
              <div class="form-group mb-3">
                <label>Marca (Ej. VISA)</label>
                <input type="text" class="form-control" formControlName="brand">
              </div>
              <div class="form-group mb-3">
                <label>Código Producto</label>
                <input type="text" class="form-control" formControlName="product">
              </div>
              <div class="form-group mb-3">
                <label>Emisor (Opcional)</label>
                <input type="text" class="form-control" formControlName="issuerName">
              </div>
              <div class="form-group mb-3">
                <label>País (ISO 2, Opcional)</label>
                <input type="text" class="form-control" formControlName="countryCode">
              </div>
              <div class="d-flex justify-content-end gap-2 mt-4 text-right">
                <button type="button" class="btn btn-outline" (click)="closeBinModal()">Cancelar</button>
                <button type="submit" class="btn btn-primary" [disabled]="binForm.invalid">Guardar BIN</button>
              </div>
            </form>
          </div>
        </div>
      </div>

      <!-- MODAL PRODUCT -->
      <div *ngIf="showProductModal" class="custom-modal-backdrop">
        <div class="custom-modal">
          <div class="custom-modal-header">
            <h3>Nuevo Producto Bancario</h3>
            <button class="btn-close" (click)="closeProductModal()">&times;</button>
          </div>
          <div class="custom-modal-body">
            <form [formGroup]="productForm" (ngSubmit)="saveProduct()">
              <div class="form-group mb-3">
                <label>Código Interno</label>
                <input type="text" class="form-control" formControlName="code">
              </div>
              <div class="form-group mb-3">
                <label>Marca de Red</label>
                <input type="text" class="form-control" formControlName="brand">
              </div>
              <div class="form-group mb-3">
                <label>Tipo (Credit / Debit)</label>
                <input type="text" class="form-control" formControlName="productType">
              </div>
              <div class="form-group mb-3">
                <label>Nombre Comercial</label>
                <input type="text" class="form-control" formControlName="name">
              </div>
              <div class="d-flex justify-content-end gap-2 mt-4 text-right">
                <button type="button" class="btn btn-outline" (click)="closeProductModal()">Cancelar</button>
                <button type="submit" class="btn btn-primary" [disabled]="productForm.invalid">Guardar Prod.</button>
              </div>
            </form>
          </div>
        </div>
      </div>

      <!-- MODAL COUNTRY -->
      <div *ngIf="showCountryModal" class="custom-modal-backdrop">
        <div class="custom-modal">
          <div class="custom-modal-header">
            <h3>Nuevo País / Mercado</h3>
            <button class="btn-close" (click)="closeCountryModal()">&times;</button>
          </div>
          <div class="custom-modal-body">
            <form [formGroup]="countryForm" (ngSubmit)="saveCountry()">
              <div class="form-group mb-3">
                <label>Código ISO (2 letras)</label>
                <input type="text" class="form-control" formControlName="code" maxlength="2">
              </div>
              <div class="form-group mb-3">
                <label>Nombre del País</label>
                <input type="text" class="form-control" formControlName="name">
              </div>
              <div class="form-group mb-3">
                <label>Código Numérico</label>
                <input type="text" class="form-control" formControlName="numericCode">
              </div>
              <div class="form-group mb-3">
                <label>Moneda Base (Ej. USD, EUR)</label>
                <input type="text" class="form-control" formControlName="currency">
              </div>
              <div class="d-flex justify-content-end gap-2 mt-4 text-right">
                <button type="button" class="btn btn-outline" (click)="closeCountryModal()">Cancelar</button>
                <button type="submit" class="btn btn-primary" [disabled]="countryForm.invalid">Guardar País</button>
              </div>
            </form>
          </div>
        </div>
      </div>

    </div>
  `,
   styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .bg-light { background-color: #f8fafc; }
    
    .row { display: flex; flex-wrap: wrap; gap: 1.5rem; margin-right: -15px; margin-left: -15px;}
    .col-lg-6 { flex: 0 0 calc(50% - 1.5rem); min-width: 400px; padding: 0 15px;}

    .table-responsive { overflow-x: auto; max-height: 480px;}
    .table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
    .table th, .table td { padding: 1rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.70rem; text-transform: uppercase; position: sticky; top: 0; background-color: white;}
    .table tbody tr:hover { background-color: #fafbfc; }
    
    .font-monospace { font-family: 'Courier New', Courier, monospace; }
    .text-sm { font-size: 0.75rem; }
    .text-primary-dark { color: #1e3a8a; }
    
    .badge { padding: 0.25rem 0.5rem; border-radius: var(--radius-sm); font-size: 0.70rem; font-weight: 600; }
    .badge-success { background-color: #ecfdf5; color: #047857; }
    .badge-danger { background-color: #fef2f2; color: #b91c1c; }
    
    .net-visa { background: #e0e7ff; color: #1d4ed8; }
    .net-mc { background: #ffedd5; color: #c2410c; }
    .net-amex { background: #e2e8f0; color: #0f172a; }
    .net-local { background: #f3f4f6; color: #374151; }

    .custom-modal-backdrop { position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1050; }
    .custom-modal { background: white; border-radius: 8px; width: 450px; max-width: 90vw; box-shadow: 0 4px 12px rgba(0,0,0,0.15); animation: fadein 0.2s ease-in-out; }
    .custom-modal-header { padding: 1rem 1.5rem; border-bottom: 1px solid #e2e8f0; display: flex; justify-content: space-between; align-items: center; }
    .custom-modal-header h3 { margin: 0; font-size: 1.15rem; color: #1e293b; font-weight: 600; }
    .btn-close { background: none; border: none; font-size: 1.5rem; cursor: pointer; color: #64748b; }
    .custom-modal-body { padding: 1.5rem; max-height: 80vh; overflow-y: auto; text-align: left; }
    .form-group label { display: block; font-size: 0.8rem; font-weight: 600; color: #475569; margin-bottom: 0.25rem; }
    .form-control { width: 100%; padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 4px; font-size: 0.9rem; margin-bottom: 0.5rem; box-sizing: border-box; }
    .form-control:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 1px #3b82f6; }
    
    @keyframes fadein { from { opacity: 0; transform: translateY(-10px); } to { opacity: 1; transform: translateY(0); } }
  `]
})
export class CatalogsComponent implements OnInit {
   private catalogService = inject(CatalogService);
   private fb = inject(FormBuilder);
   private notifications = inject(NotificationService);

   bins: CatalogBin[] = [];
   products: CatalogProduct[] = [];
   countries: CatalogCountry[] = [];

   showBinModal = false;
   showProductModal = false;
   showCountryModal = false;

   binForm: FormGroup = this.fb.group({
      binStart: [null, [Validators.required, Validators.min(100000)]],
      binEnd: [null, [Validators.required, Validators.min(100000)]],
      brand: ['', Validators.required],
      product: ['', Validators.required],
      issuerName: [''],
      countryCode: ['']
   });

   productForm: FormGroup = this.fb.group({
      code: ['', Validators.required],
      brand: ['', Validators.required],
      productType: ['', Validators.required],
      name: ['', Validators.required]
   });

   countryForm: FormGroup = this.fb.group({
      code: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(2)]],
      name: ['', Validators.required],
      numericCode: ['', Validators.required],
      currency: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(3)]]
   });

   ngOnInit() {
      this.refreshCache();
   }

   refreshCache() {
      this.catalogService.getBins().pipe(
         catchError(err => {
            console.error('Error loading BINs:', err?.status, err?.message);
            this.notifications.warning('No se pudo cargar la lista de BINs');
            return of([] as CatalogBin[]);
         })
      ).subscribe(data => this.bins = data);

      this.catalogService.getProducts().pipe(
         catchError(err => {
            console.error('Error loading products:', err?.status, err?.message);
            this.notifications.warning('No se pudo cargar la lista de productos');
            return of([] as CatalogProduct[]);
         })
      ).subscribe(data => this.products = data);

      this.catalogService.getCountries().pipe(
         catchError(err => {
            console.error('Error loading countries:', err?.status, err?.message);
            this.notifications.warning('No se pudo cargar la lista de países/monedas');
            return of([] as CatalogCountry[]);
         })
      ).subscribe(data => this.countries = data);
   }

   openBinModal() { this.showBinModal = true; this.binForm.reset(); }
   closeBinModal() { this.showBinModal = false; }
   saveBin() {
      if (this.binForm.invalid) return;
      this.catalogService.createBin(this.binForm.value).subscribe({
         next: () => { 
            this.notifications.success('BIN registrado correctamente');
            this.closeBinModal(); 
            this.refreshCache(); 
         },
         error: err => { 
            console.error(err); 
            this.notifications.error('Error al registrar el BIN');
         }
      });
   }

   openProductModal() { this.showProductModal = true; this.productForm.reset(); }
   closeProductModal() { this.showProductModal = false; }
   saveProduct() {
      if (this.productForm.invalid) return;
      this.catalogService.createProduct(this.productForm.value).subscribe({
         next: () => { 
            this.notifications.success('Producto creado');
            this.closeProductModal(); 
            this.refreshCache(); 
         },
         error: err => { 
            console.error(err); 
            this.notifications.error('Error al registrar producto');
         }
      });
   }

   openCountryModal() { this.showCountryModal = true; this.countryForm.reset(); }
   closeCountryModal() { this.showCountryModal = false; }
   saveCountry() {
      if (this.countryForm.invalid) return;
      this.catalogService.createCountry(this.countryForm.value).subscribe({
         next: () => { 
            this.notifications.success('País registrado');
            this.closeCountryModal(); 
            this.refreshCache(); 
         },
         error: err => { 
            console.error(err); 
            this.notifications.error('Error al registrar país');
         }
      });
   }

   getNetworkClass(net: string): string {
      switch (net.toUpperCase()) {
         case 'VISA': return 'net-visa';
         case 'MASTERCARD': return 'net-mc';
         case 'AMEX': return 'net-amex';
         default: return 'net-local';
      }
   }
}
