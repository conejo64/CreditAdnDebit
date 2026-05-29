import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { DelinquencyService, DelinquencyRecord, ContactAttempt, DelinquencyNote } from './delinquency.service';
import { catchError, EMPTY, switchMap } from 'rxjs';
import { ContactAttemptFormComponent } from './contact-attempt-form.component';
import { NoteFormComponent } from './note-form.component';

@Component({
  selector: 'app-delinquency-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, ContactAttemptFormComponent, NoteFormComponent],
  template: `
    <div class="page-container">
      <div class="page-header mb-4">
        <a [routerLink]="['/app/collections/delinquency']" class="back-link d-flex align-items-center gap-1 text-muted mb-2">
          <span class="material-symbols-rounded" style="font-size:18px">arrow_back</span>
          Volver a la lista
        </a>
        <h1 class="page-title d-flex align-items-center gap-2">
          <span class="material-symbols-rounded text-primary" style="font-size: 32px">warning</span>
          Detalle de Mora
        </h1>
      </div>

      <div *ngIf="errorState" class="error-banner mb-4" data-testid="detail-error">
        Error al cargar el registro de mora.
      </div>

      <div *ngIf="record" class="card p-4 mb-4" data-testid="record-overview">
        <dl class="overview-grid">
          <div><dt>Cuenta</dt><dd>{{ record.accountId }}</dd></div>
          <div><dt>Estado</dt><dd><span class="role-badge status-active">{{ record.status }}</span></dd></div>
          <div><dt>Bucket</dt><dd>{{ record.bucketLabel }}</dd></div>
          <div><dt>Días en mora</dt><dd class="text-danger font-weight-600">{{ record.daysInArrears }}</dd></div>
          <div><dt>Monto vencido</dt><dd class="font-weight-600">{{ record.overdueAmount | currency }}</dd></div>
          <div><dt>Creado</dt><dd>{{ record.createdOn | date:'short' }}</dd></div>
        </dl>
      </div>

      <!-- Tabs -->
      <div class="tab-bar mb-3" data-testid="tabs">
        <button class="tab-btn" [class.active]="activeTab === 'contacts'" (click)="setTab('contacts')">
          Intentos de Contacto
        </button>
        <button class="tab-btn" [class.active]="activeTab === 'notes'" (click)="setTab('notes')">
          Notas Internas
        </button>
      </div>

      <!-- Contact History tab -->
      <div *ngIf="activeTab === 'contacts'" data-testid="tab-contacts">
        <app-contact-attempt-form
          *ngIf="record"
          [delinquencyRecordId]="record.id"
          (submitted)="reloadContactAttempts()"
          data-testid="contact-attempt-form">
        </app-contact-attempt-form>

        <div class="card p-0 mt-3">
          <table class="table">
            <thead>
              <tr>
                <th>CANAL</th><th>RESULTADO</th><th>NOTAS</th><th>OPERADOR</th><th>FECHA</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let a of contactAttempts" data-testid="contact-attempt-row">
                <td>{{ a.channel }}</td>
                <td>{{ a.outcome }}</td>
                <td>{{ a.notes }}</td>
                <td>{{ a.attemptedBy }}</td>
                <td>{{ a.attemptedOn | date:'short' }}</td>
              </tr>
              <tr *ngIf="contactAttempts.length === 0">
                <td colspan="5" class="text-center py-4 text-muted" data-testid="contact-empty-state">
                  Sin intentos de contacto registrados.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Notes tab -->
      <div *ngIf="activeTab === 'notes'" data-testid="tab-notes">
        <app-note-form
          *ngIf="record"
          [delinquencyRecordId]="record.id"
          (submitted)="reloadNotes()"
          data-testid="note-form">
        </app-note-form>

        <div class="card p-0 mt-3">
          <table class="table">
            <thead>
              <tr><th>CONTENIDO</th><th>OPERADOR</th><th>FECHA</th></tr>
            </thead>
            <tbody>
              <tr *ngFor="let n of notes" data-testid="note-row">
                <td>{{ n.content }}</td>
                <td>{{ n.createdBy }}</td>
                <td>{{ n.createdOn | date:'short' }}</td>
              </tr>
              <tr *ngIf="notes.length === 0">
                <td colspan="3" class="text-center py-4 text-muted" data-testid="notes-empty-state">
                  Sin notas internas registradas.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .back-link { font-size: 0.875rem; text-decoration: none; }
    .overview-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; }
    .overview-grid dt { font-size: 0.75rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; }
    .overview-grid dd { font-size: 0.9rem; margin: 0; margin-top: 0.25rem; }
    .tab-bar { display: flex; gap: 0.5rem; border-bottom: 2px solid var(--border-color); }
    .tab-btn { background: none; border: none; padding: 0.6rem 1.2rem; cursor: pointer; font-size: 0.875rem; color: var(--text-muted); border-bottom: 2px solid transparent; margin-bottom: -2px; }
    .tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); font-weight: 600; }
    .role-badge { padding: 0.25rem 0.6rem; border-radius: var(--radius-sm); font-size: 0.70rem; font-weight: 600; text-transform: uppercase; }
    .status-active { background: #ecfdf5; color: #047857; }
    .font-weight-600 { font-weight: 600; }
    .text-danger { color: var(--danger, #dc2626); }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .error-banner { display: flex; align-items: center; gap: 0.75rem; padding: 1.25rem 1.5rem; background: #fef2f2; color: #991b1b; border-radius: var(--radius-sm); }
  `]
})
export class DelinquencyDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private delinquencyService = inject(DelinquencyService);

  record: DelinquencyRecord | null = null;
  contactAttempts: ContactAttempt[] = [];
  notes: DelinquencyNote[] = [];
  activeTab: 'contacts' | 'notes' = 'contacts';
  errorState = false;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) { this.errorState = true; return; }

    // Load delinquency record from the list (using page=1 — detail view fetches from existing endpoint)
    this.delinquencyService.getDelinquencies(1, 200)
      .pipe(catchError(() => { this.errorState = true; return EMPTY; }))
      .subscribe(result => {
        const found = result.items.find(r => r.id === id);
        if (!found) { this.errorState = true; return; }
        this.record = found;
        this.reloadContactAttempts();
        this.reloadNotes();
      });
  }

  setTab(tab: 'contacts' | 'notes'): void {
    this.activeTab = tab;
  }

  reloadContactAttempts(): void {
    if (!this.record) return;
    this.delinquencyService.getContactAttempts(this.record.id)
      .pipe(catchError(() => EMPTY))
      .subscribe(items => { this.contactAttempts = items; });
  }

  reloadNotes(): void {
    if (!this.record) return;
    this.delinquencyService.getNotes(this.record.id)
      .pipe(catchError(() => EMPTY))
      .subscribe(items => { this.notes = items; });
  }
}
